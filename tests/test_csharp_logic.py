"""Compile the GTA-free C# logic blocks of the scripts and run them.

Blocks marked `// <gvl-testable:name>` are extracted verbatim from game/*.cs and built with the
.NET Framework compiler that ships with Windows (C# 5), together with the harness below.
"""
import os
import re
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GAME = ROOT / "game"


def find_compiler():
    windir = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Microsoft.NET"
    for framework in ("Framework64", "Framework"):
        candidate = windir / framework / "v4.0.30319" / "csc.exe"
        if candidate.is_file():
            return candidate
    return None


def testable_block(script, name):
    source = (GAME / script).read_text(encoding="utf-8-sig")
    match = re.search(r"// <gvl-testable:%s>(.*?)// </gvl-testable:%s>" % (name, name), source, re.S)
    if not match:
        raise AssertionError("%s has no testable block '%s'" % (script, name))
    return ("using System;\nusing System.Collections.Generic;\nusing System.Globalization;\n"
            "using System.IO;\nusing System.Text;\n" + match.group(1))


HARNESS = r'''
using System;
using System.Collections.Generic;
using System.IO;

static class Harness
{
    static int failures;
    static void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL " + what); } }

    const string Observe = "/engine/v1/simulation/actors/observe";
    const string Work = "/engine/v1/simulation/actors/work-completed";
    const string Buildings = "/engine/v1/world/buildings";
    const string Sync = "/engine/v1/world/businesses/sync";

    // Same loop shape as Core's worker: peek head, send, remove on success or permanent failure.
    static void Drain(GvlPostQueue q, Func<GvlPost, int> send, List<string> delivered, List<string> dropped)
    {
        for (int guard = 0; guard < 1000; guard++)
        {
            GvlPost item = q.Head();
            if (item == null) return;
            int status = send(item);
            if (status == 200) { q.Remove(item); delivered.Add(item.Body); continue; }
            if (q.Failed(item, status)) { dropped.Add(item.Body); continue; }
            return;
        }
    }

    static void Paths()
    {
        Check(GvlSendRules.IsEnginePostPath(Observe), "observe allowed");
        Check(GvlSendRules.IsEnginePostPath(Work), "work allowed");
        Check(GvlSendRules.IsEnginePostPath(Buildings), "world allowed");
        Check(!GvlSendRules.IsEnginePostPath("/engine/v1/shutdown"), "shutdown refused");
        Check(!GvlSendRules.IsEnginePostPath("/engine/v1/simulation/advance"), "advance refused");
        Check(!GvlSendRules.IsEnginePostPath(Work + "/x"), "suffix refused");
        Check(!GvlSendRules.IsEnginePostPath(null), "null refused");
    }

    static void PermanentErrorDoesNotBlock()
    {
        foreach (int bad in new[] { 400, 409, 413, 422 })
        {
            GvlPostQueue q = new GvlPostQueue();
            q.Add(Buildings, "bad"); q.Add(Buildings, "good");
            List<string> ok = new List<string>(), dropped = new List<string>();
            int status = bad;
            Drain(q, delegate(GvlPost p) { return p.Body == "bad" ? status : 200; }, ok, dropped);
            Check(q.Count == 0 && ok.Count == 1 && dropped.Count == 1, "permanent " + bad + " is skipped");
        }
    }

    static void BoundedRetry(int status, int limit)
    {
        GvlPostQueue q = new GvlPostQueue();
        q.Add(Buildings, "bad"); q.Add(Buildings, "good");
        List<string> ok = new List<string>(), dropped = new List<string>();
        for (int i = 1; i < limit; i++) Drain(q, delegate(GvlPost p) { return p.Body == "bad" ? status : 200; }, ok, dropped);
        Check(q.Count == 2 && dropped.Count == 0, status + " retried before the limit");
        Drain(q, delegate(GvlPost p) { return p.Body == "bad" ? status : 200; }, ok, dropped);
        Check(q.Count == 0 && ok.Count == 1 && dropped.Count == 1, status + " skipped at the limit");
    }

    static void TransientErrorKeepsEverything()
    {
        foreach (int status in new[] { 0, 401, 403, 408, 429, 502, 503, 504 })
        {
            GvlPostQueue q = new GvlPostQueue();
            q.Add(Work, "w1"); q.Add(Work, "w2");
            List<string> ok = new List<string>(), dropped = new List<string>();
            int s = status;
            for (int i = 0; i < 100; i++) Drain(q, delegate(GvlPost p) { return s; }, ok, dropped);
            Check(q.Count == 2 && dropped.Count == 0, "transient " + status + " keeps the queue");
            Drain(q, delegate(GvlPost p) { return 200; }, ok, dropped);
            Check(string.Join(",", ok.ToArray()) == "w1,w2", "order kept after " + status);
        }
        GvlPostQueue offline = new GvlPostQueue();
        offline.Add(Work, "w");
        for (int i = 0; i < 50; i++) offline.Failed(offline.Head(), 0);
        Check(!offline.Failed(offline.Head(), 404), "time spent offline does not use up retry attempts");
    }

    static void CoalescingAndPersistence()
    {
        GvlPostQueue q = new GvlPostQueue();
        q.Add(Sync, "{\"business_id\":\"a\",\"cash_balance\":1}");
        q.Add(Work, "{\"request_id\":\"shift-1\"}");
        q.Add(Observe, "{\"actor_id\":\"player.main\",\"observed\":1}");
        q.Add(Sync, "{\"business_id\":\"b\",\"cash_balance\":2}");
        q.Add(Sync, "{\"business_id\":\"a\",\"cash_balance\":3}");
        q.Add(Observe, "{\"actor_id\":\"player.main\",\"observed\":2}");
        q.Add(Work, "{\"request_id\":\"shift-2\"}");
        Check(q.Count == 5, "latest snapshot replaces the older one, work reports are all kept");
        Check(q.Head().Body.Contains("\"cash_balance\":3"), "replacement keeps queue position");

        q.Failed(q.Head(), 503);
        string saved = q.Serialize();
        GvlPostQueue restored = new GvlPostQueue();
        restored.Restore(saved);
        Check(restored.Count == 2, "only work reports survive a restart");
        Check(restored.Head().Path == Work && restored.Head().Body == "{\"request_id\":\"shift-1\"}", "same request id after restart");
        restored.Restore("garbage\n1\t/engine/v1/shutdown\t{}\n");
        Check(restored.Count == 2, "invalid saved lines are ignored");

        GvlPost inFlight = restored.Head();
        restored.Remove(inFlight); restored.Remove(inFlight);
        Check(restored.Count == 1, "removing twice only removes the sent item");
    }

    static void Capacity()
    {
        GvlPostQueue q = new GvlPostQueue();
        q.Add(Work, "keep");
        GvlPost evicted = null;
        for (int i = 0; i < GvlPostQueue.Capacity; i++) evicted = q.Add(Buildings, "b" + i) ?? evicted;
        Check(q.Count == GvlPostQueue.Capacity, "queue is bounded");
        Check(evicted != null && evicted.Path == Buildings && q.Head().Body == "keep", "durable report is not evicted first");
    }

    // Event outboxes: isolate the single rejected event, deliver all others in order.
    static void Batches()
    {
        List<string> outbox = new List<string>();
        for (int i = 0; i < 7; i++) outbox.Add(i == 4 ? "poison" : "e" + i);
        GvlBatchIsolation iso = new GvlBatchIsolation(false);
        List<string> delivered = new List<string>(), dropped = new List<string>();
        for (int round = 0; round < 40 && outbox.Count > 0; round++)
        {
            int n = GvlSendRules.BatchCount(outbox, iso.Limit, GvlSendRules.MaxBatchBytes);
            List<string> batch = outbox.GetRange(0, n);
            if (!batch.Contains("poison")) { iso.Succeeded(n); delivered.AddRange(batch); outbox.RemoveRange(0, n); }
            else if (iso.Failed(400, n)) { dropped.AddRange(batch); outbox.RemoveRange(0, n); }
        }
        Check(outbox.Count == 0, "outbox drains despite a poison event");
        Check(string.Join(",", delivered.ToArray()) == "e0,e1,e2,e3,e5,e6", "good events delivered in order");
        Check(dropped.Count == 1 && dropped[0] == "poison", "only the poison event is dropped");
        Check(iso.Limit == GvlSendRules.MaxBatchEvents, "full batches resume afterwards");

        GvlBatchIsolation down = new GvlBatchIsolation(false);
        bool any = false;
        for (int i = 0; i < 100; i++) any = down.Failed(0, 1) || any;
        Check(!any, "network failure never drops events");
        for (int i = 0; i < 100; i++) any = down.Failed(500, 1) || any;
        Check(!any, "server fault never drops money events");

        GvlBatchIsolation bounded = new GvlBatchIsolation(true);
        int tries = 1;
        while (!bounded.Failed(500, 1) && tries < 100) tries++;
        Check(tries == GvlSendRules.MaxServerErrorAttempts, "bridge copy gives up on a poison event");

        List<string> big = new List<string>();
        for (int i = 0; i < 100; i++) big.Add(new string('x', 3000));
        int count = GvlSendRules.BatchCount(big, 100, GvlSendRules.MaxBatchBytes);
        Check(count >= 1 && count * 3096 <= GvlSendRules.MaxBatchBytes, "batch stays under the host body limit");
        big[0] = new string('x', 90000);
        Check(GvlSendRules.BatchCount(big, 100, GvlSendRules.MaxBatchBytes) == 1, "oversized event is sent alone");
    }

    // Drives the fade the way DoiSong.OnTick does: one check per tick, game time frozen within a tick.
    static void Fade()
    {
        GvlFade fade = new GvlFade();
        int charged = 0, applied = 0;
        Action done = delegate { applied++; };
        Check(fade.Start(1000, 900, 180, done), "fade starts"); charged++;
        Check(!fade.Start(1000, 900, 90, done), "second fade refused while one is running");
        int minutes; Action action;
        for (int now = 1000; now < 1900; now += 16)
            Check(!fade.TryFinish(now, out minutes, out action), "not due at " + now);
        Check(fade.TryFinish(1900, out minutes, out action) && minutes == 180, "finishes when due");
        action();
        Check(!fade.TryFinish(5000, out minutes, out action) && action == null, "finishes once");
        Check(charged == 1 && applied == 1 && !fade.Active, "one charge, one effect");
        Check(fade.Start(6000, 900, 60, done), "can start again");
        Check(fade.TryFinish(int.MaxValue, out minutes, out action) && minutes == 60, "abort flushes the pending effect");
    }

    // Bubble: on at 120 m, off only beyond 150 m, thousands of doors.
    static void Streaming()
    {
        GvlRegistry reg = new GvlRegistry();
        Check(reg.Add(0f, 0f, 30f, "seed"), "add");
        Check(!reg.Add(1.5f, 1f, 30.5f, "scan") && reg.Count == 1, "same door merged");
        Check(reg.Add(-400f, -900f, 30f, "scan"), "negative coordinates");
        Check(reg.Add(0f, 0f, 60f, "scan"), "door on another floor is separate");
        Check(reg.Nearest(0.5f, 0.5f, 30.2f, 1.8f).Z == 30f, "nearest respects height");
        Check(reg.Nearest(5f, 5f, 30f, 1.8f) == null, "out of reach");
        GvlBubble bubble = new GvlBubble();
        string id = GvlRegistry.MakeId(0f, 0f, 30f);
        bubble.Update(reg, 121f, 0f, 30f); Check(!bubble.Active.Contains(id), "not loaded at 121");
        bubble.Update(reg, 119f, 0f, 30f); Check(bubble.Active.Contains(id), "loaded at 119");
        bubble.Update(reg, 149f, 0f, 30f); Check(bubble.Active.Contains(id), "kept at 149");
        bubble.Update(reg, 151f, 0f, 30f); Check(!bubble.Active.Contains(id), "unloaded beyond 150");

        GvlRegistry big = new GvlRegistry();
        for (int i = 0; i < 100; i++) for (int j = 0; j < 60; j++) big.Add(i * 9f - 400f, j * 9f - 300f, 20f, "scan");
        Check(big.Count == GvlRegistry.Capacity, "capacity bound");
        int expected = 0;
        foreach (string line in big.Serialize())
        {
            string[] f = line.Split('|');
            float x = float.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture);
            float y = float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture);
            if (GvlRegistry.Distance(0f, 0f, x, y) <= 120f) expected++;
        }
        Check(expected > 100 && big.Within(0f, 0f, 20f, 120f).Count == expected, "grid query equals brute force");
        GvlRegistry copy = new GvlRegistry();
        foreach (string line in reg.Serialize()) copy.Load(line);
        Check(copy.Count == reg.Count && copy.Find(id) != null, "round trip keeps ids");
        Check(!copy.Load("garbage") && !copy.Load("1|2|x|scan") && !copy.Load(null), "bad lines ignored");
        Check(GvlRegistry.LooksLikeDoor(1.1f, 0.12f, 2.4f), "door size");
        Check(!GvlRegistry.LooksLikeDoor(4f, 0.1f, 2.4f) && !GvlRegistry.LooksLikeDoor(1.1f, 1.1f, 2.4f), "gate and box rejected");
    }

    static void Templates()
    {
        GvlTemplateHealth health = new GvlTemplateHealth();
        int first = GvlTemplates.Assign("12_34_5", 23, health);
        Check(first >= 0 && first < 23 && GvlTemplates.Assign("12_34_5", 23, health) == first, "stable assignment");
        health.Failed(first); Check(GvlTemplates.Assign("12_34_5", 23, health) == first, "one failure retried");
        health.Failed(first); Check(GvlTemplates.Assign("12_34_5", 23, health) == (first + 1) % 23, "broken template skipped");
        GvlTemplateHealth restored = new GvlTemplateHealth();
        foreach (string line in health.Serialize()) restored.Load(line);
        Check(!restored.Usable(first), "failures persisted");
        for (int i = 0; i < 23; i++) { health.Failed(i); health.Failed(i); }
        Check(GvlTemplates.Assign("12_34_5", 23, health) == -1, "nothing usable");
        HashSet<int> skip = new HashSet<int>(); skip.Add(first);
        Check(GvlTemplates.Assign("12_34_5", 23, new GvlTemplateHealth(), skip) == (first + 1) % 23, "just-failed template skipped");
        HashSet<int> used = new HashSet<int>();
        for (int i = 0; i < 1000; i++) used.Add(GvlTemplates.Assign(i + "_" + (i * 7) + "_30", 23, new GvlTemplateHealth()));
        Check(used.Count == 23, "all templates used across many buildings");
        float ax, ay, bx, by;
        GvlTemplates.Offset("a", "bed", 0, out ax, out ay); GvlTemplates.Offset("a", "bed", 0, out bx, out by);
        Check(ax == bx && ay == by, "seeded offset is deterministic");
        GvlTemplates.Offset("b", "bed", 0, out bx, out by); Check(ax != bx || ay != by, "differs per building");
        double r = Math.Sqrt(ax * ax + ay * ay); Check(r >= 1.79 && r <= 5.81, "offset keeps clear of the door");
    }

    static void Portal()
    {
        GvlPortal p = new GvlPortal();
        p.BeginEnter(1000);
        Check(p.Step(1100, true, true) == GvlPortal.Wait, "frozen at least 200 ms even if loaded");
        Check(p.Step(1500, false, true) == GvlPortal.Wait, "waits for collision");
        Check(p.Step(1600, true, false) == GvlPortal.Wait, "waits for interior");
        Check(p.Step(1700, true, true) == GvlPortal.Done && p.Phase == GvlPortal.Inside, "released inside");
        Check(p.Step(1800, true, true) == GvlPortal.Wait, "single release");
        p.BeginExit(2000);
        Check(p.Step(2250, true, true) == GvlPortal.Done && p.Phase == GvlPortal.Outside, "exit");
        p.BeginEnter(3000);
        Check(p.Step(3000 + GvlPortal.TimeoutMs, false, false) == GvlPortal.Abort && p.Phase == GvlPortal.Outside, "load timeout aborts");
        p.BeginExit(20000);
        Check(p.Step(20000 + GvlPortal.TimeoutMs, false, false) == GvlPortal.Done, "exit never traps the player");
    }

    static void Crowd()
    {
        GvlOccupancy o = new GvlOccupancy(3);
        Check(o.TryEnter("a", 1) && o.TryEnter("a", 2) && o.TryEnter("a", 3), "three fit");
        Check(!o.TryEnter("a", 4) && !o.TryEnter("b", 1), "capacity and single location");
        Check(o.TryReserve("a", "bed", 1) && !o.TryReserve("a", "bed", 2), "one per anchor");
        Check(!o.TryReserve("b", "bed", 2), "cannot reserve elsewhere");
        Check(o.TryReserve("a", "desk", 1) && o.TryReserve("a", "bed", 2), "moving frees the old anchor");
        o.Leave(2); Check(o.TryEnter("a", 4) && o.TryReserve("a", "bed", 4), "leaving frees place and anchor");

        GvlStuck s = new GvlStuck();
        Check(!s.Update(7, 0, 10f), "first sample");
        Check(!s.Update(7, 7999, 9.8f), "not yet");
        Check(s.Update(7, 8000, 9.8f), "stuck after eight seconds without progress");
        s.Clear(7);
        bool any = false;
        for (int t = 0; t <= 60000; t += 1000) any = s.Update(8, t, 100f - t / 1000f) || any;
        Check(!any, "steady progress is not stuck");
    }

    static void Wallet()
    {
        GvlWalletReporter w = new GvlWalletReporter();
        Check(w.ShouldReport(0, 100), "first");
        Check(!w.ShouldReport(1000, 200), "rate limited");
        Check(w.ShouldReport(5000, 200), "change reported");
        Check(!w.ShouldReport(30000, 200), "unchanged is quiet");
        Check(w.ShouldReport(65000, 200), "periodic repeat");
        w.Invalidate(); Check(w.ShouldReport(65001, 200), "character switch reports at once");
        Check(w.ShouldReport(10, 200), "timer reset after a reload");
    }

    static List<string> LoadAll(GvlSaveFile file, out bool ok)
    {
        List<string> got = new List<string>();
        ok = file.Load(delegate(string line) { if (line.StartsWith("bad")) throw new FormatException(line); got.Add(line); });
        return got;
    }

    static GvlSaveFile Open(string path) { return new GvlSaveFile(path, GvlSaveFile.LooksKeyValue, null); }

    static void SaveFiles()
    {
        string folder = Path.Combine(Path.GetTempPath(), "gvl-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "a.sav");
            bool ok;
            GvlSaveFile f = Open(path);
            Check(LoadAll(f, out ok).Count == 0 && ok && !f.Blocked, "no file yet is a new game");
            Check(f.Write(new[] { "money=1" }) && f.Write(new[] { "money=2" }), "writes");
            Check(string.Join(",", LoadAll(Open(path), out ok).ToArray()) == "money=2", "reads the newest");
            Check(File.Exists(path + ".bak") && !File.Exists(path + ".tmp"), "keeps a backup, no temp left");

            // Cut off in the middle of a write: fall back to the backup and keep the damaged copy.
            File.WriteAllText(path, GvlSaveFile.Header + "\r\nmoney=3\r\n");
            f = Open(path);
            Check(string.Join(",", LoadAll(f, out ok).ToArray()) == "money=1" && ok, "truncated file uses backup");
            Check(File.ReadAllText(path + ".broken").Contains("money=3"), "damaged file preserved");
            Check(f.Write(new[] { "money=4" }), "can save after recovery");
            Check(string.Join(",", LoadAll(Open(path + ".bak"), out ok).ToArray()) == "money=1", "good backup not replaced by the damaged file");

            // Both unreadable: never overwrite.
            File.WriteAllText(path, "");
            File.WriteAllBytes(path + ".bak", new byte[64]);
            f = Open(path);
            LoadAll(f, out ok);
            Check(!ok && f.Blocked && !f.Write(new[] { "money=0" }), "blocked when nothing is readable");
            Check(new FileInfo(path).Length == 0 && new FileInfo(path + ".bak").Length == 64, "files untouched");

            // Files written by earlier builds have no markers; one bad line does not lose the others.
            string legacy = Path.Combine(folder, "b.sav");
            File.WriteAllLines(legacy, new[] { "a=1", "bad=x", "c=3" });
            f = Open(legacy);
            Check(string.Join(",", LoadAll(f, out ok).ToArray()) == "a=1,c=3" && ok, "bad line skipped");
            Check(File.ReadAllText(legacy + ".broken").Contains("bad=x"), "original kept when a line is dropped");

            string empty = Path.Combine(folder, "c.sav");
            f = Open(empty);
            f.Write(new string[0]);
            Check(LoadAll(Open(empty), out ok).Count == 0 && ok, "an empty save is valid");
        }
        finally { Directory.Delete(folder, true); }
        Check(GvlSource.Id("legacy") == "gta5.shvdn" && GvlSource.Id("") == "gta5.shvdn", "old saves keep their event source");
        Check(GvlSource.Id("abc") == "gta5.shvdn:abc", "new counters get their own source");
    }

    static void Business()
    {
        List<int[]> c = new List<int[]>();
        c.Add(new[] { 0, 0 }); c.Add(new[] { 1, 1 }); c.Add(new[] { 2, 2 });
        Check(GvlShopContracts.RemoveShop(c, 1) == 1 && c.Count == 2, "contract of the closed shop is cancelled");
        Check(c[0][1] == 0 && c[1][0] == 2 && c[1][1] == 1, "later depots shift with their shop");
        Check(GvlCollateral.RepayOnSale(60000, 100000, 92000) == 0, "enough collateral left");
        Check(GvlCollateral.RepayOnSale(60000, 50000, 46000) == 30000, "excess is repaid from the sale");
        Check(GvlCollateral.RepayOnSale(60000, 0, 46000) == 46000, "never more than the proceeds");
        Check(GvlCollateral.RepayOnSale(0, 0, 100) == 0, "no loan");
        Check(GvlCollateral.RepayOnSale(int.MaxValue, int.MaxValue, 5) == 5, "no overflow");
    }

    static void Days()
    {
        GvlDayWatch core = new GvlDayWatch();
        Check(!core.NewDay(5, 600), "first sight of the core day is a baseline");
        Check(!core.NewDay(5, 10), "the core decides, not the clock");
        Check(core.NewDay(6, 20) && !core.NewDay(6, 30), "one tick per core day");
        Check(!core.NewDay(2, 40) && core.NewDay(3, 50), "a lower counter only moves the baseline");
        GvlDayWatch alone = new GvlDayWatch();
        Check(!alone.NewDay(-1, 1430) && alone.NewDay(-1, 5), "without core the midnight wrap counts");
        Check(!alone.NewDay(-1, 600) && !alone.NewDay(-1, 500), "a small rewind is not a day");
        Check(GvlDayWatch.CoreDay() == -1, "no clock published");
        AppDomain.CurrentDomain.SetData("GVL.Clock", new long[] { 0, 9, 0, 0, 0, 0 });
        Check(GvlDayWatch.CoreDay() == 9, "reads the published day");
    }

    static void Bank()
    {
        Check(GvlBankSync.Decide(null, 0, 100) == GvlBankSync.None, "offline: keep playing with the game figure");
        Check(GvlBankSync.Decide(new long[] { 0, 0, 0 }, 0, 100) == GvlBankSync.TakeOver, "ledger not initialised");
        Check(GvlBankSync.Decide(new long[] { 1, 700, 4 }, 5, 100) == GvlBankSync.None, "own change not applied yet");
        Check(GvlBankSync.Decide(new long[] { 1, 700, 5 }, 5, 100) == GvlBankSync.Adopt, "ledger wins once caught up");
        Check(GvlBankSync.Decide(new long[] { 1, 100, 9 }, 5, 100) == GvlBankSync.None, "already equal");
        Check(GvlBankSync.Decide(new long[] { 1, -5, 9 }, 0, 100) == GvlBankSync.None
              && GvlBankSync.Decide(new long[] { 1, 3000000000L, 9 }, 0, 100) == GvlBankSync.None, "impossible balance ignored");
    }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "control") Check(false, "control case");
        else { Paths(); PermanentErrorDoesNotBlock(); BoundedRetry(404, GvlSendRules.MaxNotFoundAttempts);
               BoundedRetry(500, GvlSendRules.MaxServerErrorAttempts); TransientErrorKeepsEverything();
               CoalescingAndPersistence(); Capacity(); Batches(); Fade();
               Streaming(); Templates(); Portal(); Crowd(); Wallet(); SaveFiles(); Business(); Days(); Bank(); }
        Console.WriteLine("failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
'''


class CSharpLogicTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.compiler = find_compiler()
        if cls.compiler is None:
            if os.environ.get("CI"):
                raise AssertionError("csc.exe is required on CI")
            raise unittest.SkipTest(".NET Framework csc.exe not found")
        cls.folder = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls.folder.cleanup)
        folder = Path(cls.folder.name)
        files = {"Outbox.cs": testable_block("GtaVietLifeCore.cs", "outbox"),
                 "Fade.cs": testable_block("GtaVietLifeDoiSong.cs", "fade"),
                 "Uis.cs": testable_block("GtaVietLifeInteriors.cs", "uis"),
                 "Wallet.cs": testable_block("GtaVietLifePersonal.cs", "wallet"),
                 "SaveFile.cs": testable_block("GtaVietLifeSaveFile.cs", "savefile"),
                 "Business.cs": testable_block("GtaVietLifeWorld.cs", "business"),
                 "DayWatch.cs": testable_block("GtaVietLifeSaveFile.cs", "daywatch"),
                 "BankSync.cs": testable_block("GtaVietLifeSaveFile.cs", "banksync"),
                 "Harness.cs": HARNESS}
        for name, text in files.items():
            (folder / name).write_text(text, encoding="utf-8-sig")
        cls.exe = folder / "harness.exe"
        built = subprocess.run([str(cls.compiler), "/nologo", "/warnaserror-", "/target:exe",
                                "/out:" + str(cls.exe)] + [str(folder / n) for n in files],
                               capture_output=True, text=True, errors="replace", timeout=120)
        if built.returncode:
            raise AssertionError("C# harness did not compile:\n" + built.stdout + built.stderr)

    def run_harness(self, *args):
        return subprocess.run([str(self.exe), *args], capture_output=True, text=True, errors="replace", timeout=60)

    def test_outbox_and_fade_behaviour(self):
        result = self.run_harness()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_control_case_fails(self):
        self.assertEqual(self.run_harness("control").returncode, 1)


class SaveSafetyTests(unittest.TestCase):
    def test_no_script_writes_a_save_file_directly(self):
        offenders = []
        for path in sorted(GAME.glob("*.cs")):
            source = path.read_text(encoding="utf-8-sig")
            offenders += [(path.name, m) for m in
                          re.findall(r"File\.(?:WriteAll\w+|Replace|Copy)\((SaveFile|NamedFile)\b", source)]
        self.assertEqual(offenders, [], "save files must go through GvlSaveFile")

    def test_every_script_using_the_helper_compiles_it(self):
        for project in (GAME / "compile").glob("*.Compile.csproj"):
            text = project.read_text(encoding="utf-8-sig")
            script = re.search(r'Include="\.\./(GtaVietLife\w*\.cs)"', text).group(1)
            if "GvlSaveFile" in (GAME / script).read_text(encoding="utf-8-sig"):
                self.assertIn("../GtaVietLifeSaveFile.cs", text, project.name)

    def test_every_bank_change_is_written_to_the_ledger(self):
        source = (GAME / "GtaVietLifeEconomy.cs").read_text(encoding="utf-8-sig")
        changes = [line for line in source.splitlines() if re.search(r"\bbank\s*(\+=|-=)", line)]
        self.assertGreater(len(changes), 5)
        self.assertEqual([line for line in changes
                          if 'Post("bank"' not in line and "void Deposit(" not in line], [])

    def test_core_reads_the_wallet_on_the_script_thread(self):
        source = (GAME / "GtaVietLifeCore.cs").read_text(encoding="utf-8-sig")
        for body in re.findall(r"Task\.Run\(delegate(.*?)\n        \}\);", source, re.S):
            self.assertNotIn("Game.Player.Money", body)
            self.assertNotIn("SyncPlayerWalletBaseline", body)


class ScriptQueueSafetyTests(unittest.TestCase):
    def test_no_script_overwrites_the_wallet_from_a_saved_value(self):
        source = (GAME / "GtaVietLifePersonal.cs").read_text(encoding="utf-8-sig")
        self.assertEqual(re.findall(r"Game\.Player\.Money = saved\w*", source), [])

    def test_scripts_use_no_api_missing_from_the_legacy_runtime(self):
        for path in GAME.glob("*.cs"):
            source = path.read_text(encoding="utf-8-sig")
            for banned in (".Task.Combat(", ".Model.GetDimensions(", "Notification.PostTicker("):
                self.assertNotIn(banned, source, path.name)

    def test_no_method_requeues_itself_into_a_queue_drained_until_empty(self):
        offenders = []
        for path in sorted(GAME.glob("*.cs")):
            source = path.read_text(encoding="utf-8-sig")
            drained = set(re.findall(r"while \((\w+)\.Count > 0\)", source))
            for match in re.finditer(r"\n    [\w<> ]*?\bvoid (\w+)\(\)\s*\{(.*?)\n    \}", source, re.S):
                method, body = match.groups()
                offenders += [(path.name, method, queue) for queue in drained
                              if re.search(r"\b%s\.Enqueue\(%s\)" % (queue, method), body)]
        self.assertEqual(offenders, [], "these loops never end within one game tick")


if __name__ == "__main__":
    unittest.main()
