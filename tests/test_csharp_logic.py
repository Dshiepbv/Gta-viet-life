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
    return "using System;\nusing System.Collections.Generic;\nusing System.Text;\n" + match.group(1)


HARNESS = r'''
using System;
using System.Collections.Generic;

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

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "control") Check(false, "control case");
        else { Paths(); PermanentErrorDoesNotBlock(); BoundedRetry(404, GvlSendRules.MaxNotFoundAttempts);
               BoundedRetry(500, GvlSendRules.MaxServerErrorAttempts); TransientErrorKeepsEverything();
               CoalescingAndPersistence(); Capacity(); Batches(); Fade(); }
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


class ScriptQueueSafetyTests(unittest.TestCase):
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
