// GTA Viet Life: shared runtime core.
// Compatibility: preserves the AppDomain GVL.* contract and the legacy key=value save format.
// Durable event delivery is at-least-once: consumers should deduplicate by sequence number.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using GTA;

public class GtaVietLifeCore : Script
{
    const string SaveFile = "GtaVietLifeCore.sav";
    const string BackupFile = "GtaVietLifeCore.sav.bak";
    const string TempFile = "GtaVietLifeCore.sav.tmp";
    static readonly string[] Modules = { "Personal", "Legal", "Body", "Social", "Life", "Economy" };
    static readonly object OutboxLock = new object();
    static readonly object SaveLock = new object();

    string server = "http://127.0.0.1:5577";
    string hostServer = "http://127.0.0.1:5580";
    string hostSessionId = Guid.NewGuid().ToString("N");
    string hostToken = "";
    readonly object HostLock = new object();
    volatile bool hostConnected;
    bool hostConnecting;
    int nextHostHeartbeat;
    bool forward = true, logEvents = true, forwarding;
    long gameDay, lastSeenSeq;
    int lastMinute = -1, nextPublish, nextForward, nextSave;
    readonly List<string> outbox = new List<string>();
    readonly long[] clock = new long[6];

    public GtaVietLifeCore()
    {
        int port = Settings.GetValue("Bridge", "Port", 5577);
        server = "http://127.0.0.1:" + port;
        int enginePort = Settings.GetValue("Engine", "Port", 5580);
        hostServer = "http://127.0.0.1:" + enginePort;
        hostToken = ReadHostToken();
        forward = Settings.GetValue("Core", "ForwardEventsToBridge", true);
        logEvents = Settings.GetValue("Core", "LogEvents", true);
        foreach (string m in Modules) Set("GVL.Mod." + m, Settings.GetValue("Modules", m, true));
        Load();
        if (Get("GVL.Pay") == null)
        {
            Func<int, string, string, bool> pay = DefaultPay;
            Set("GVL.Pay", pay); Set("GVL.PayOwner", "core");
        }
        if (Get("GVL.Earn") == null)
        {
            Action<int, string, string> earn = DefaultEarn;
            Set("GVL.Earn", earn);
        }
        Set("GVL.CoreVersion", 2);
        Tick += OnTick;
        Aborted += delegate { CloseHostSession(); Save(); };
    }

    static object Get(string k) { try { return AppDomain.CurrentDomain.GetData(k); } catch { return null; } }
    static void Set(string k, object v) { try { AppDomain.CurrentDomain.SetData(k, v); } catch { } }

    public static void Emit(string type, string data)
    {
        AppDomain d = AppDomain.CurrentDomain;
        List<string> list = d.GetData("GVL.Events") as List<string>;
        if (list == null) { list = new List<string>(); d.SetData("GVL.Events", list); }
        lock (list)
        {
            object o = d.GetData("GVL.EventSeq");
            long seq = (o is long ? (long)o : 0) + 1;
            d.SetData("GVL.EventSeq", seq);
            list.Add(seq + "\t" + (type ?? "core.unknown") + "\t" + (data ?? ""));
        }
    }

    static string Clean(string s) { return (s ?? "").Replace(";", ",").Replace("\t", " ").Replace("=", "-"); }

    static bool DefaultPay(int amount, string cat, string note)
    {
        if (amount <= 0) return true;
        if (Game.Player.Money < amount) return false;
        Game.Player.Money = Game.Player.Money - amount;
        Emit("ledger.expense", "amount=" + amount + ";cat=" + Clean(cat) + ";note=" + Clean(note));
        return true;
    }

    static void DefaultEarn(int amount, string cat, string note)
    {
        if (amount <= 0) return;
        Game.Player.Money = Game.Player.Money + amount;
        Emit("ledger.income", "amount=" + amount + ";cat=" + Clean(cat) + ";note=" + Clean(note));
    }

    void OnTick(object sender, EventArgs e)
    {
        int now = Game.GameTime;
        MaintainHostSession(now);
        TimeSpan t = World.CurrentTimeOfDay;
        int minute = t.Hours * 60 + t.Minutes;
        if (lastMinute >= 0 && minute < lastMinute && lastMinute - minute > 600)
        {
            gameDay++;
            Emit("core.day", "day=" + gameDay + ";weekday=" + (gameDay % 7));
            Save();
        }
        lastMinute = minute;

        if (now > nextPublish)
        {
            nextPublish = now + 250;
            clock[0] = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            clock[1] = gameDay; clock[2] = minute; clock[3] = gameDay % 7;
            clock[4] = gameDay % 7 >= 5 ? 1 : 0; clock[5] = t.Hours;
            Set("GVL.Clock", clock);
            DrainEvents();
        }
        if (forward && now > nextForward) { nextForward = now + 3000; Forward(); }
        if (now > nextSave) { nextSave = now + 120000; Save(); }
    }

    void DrainEvents()
    {
        List<string> list = Get("GVL.Events") as List<string>;
        if (list == null) return;
        List<string> fresh = new List<string>();
        lock (list)
        {
            foreach (string line in list)
            {
                if (string.IsNullOrEmpty(line)) continue;
                int tab = line.IndexOf('\t');
                long seq;
                if (tab > 0 && long.TryParse(line.Substring(0, tab), out seq) && seq > lastSeenSeq)
                {
                    fresh.Add(line);
                    lastSeenSeq = seq;
                }
            }
            if (list.Count > 300) list.RemoveRange(0, list.Count - 300);
        }
        if (fresh.Count == 0) return;
        if (logEvents)
        {
            try
            {
                StringBuilder b = new StringBuilder();
                foreach (string f in fresh)
                    b.Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                     .Append(" d").Append(gameDay).Append(" ").Append(f.Replace('\t', ' ')).AppendLine();
                File.AppendAllText("GtaVietLifeEvents.log", b.ToString());
            }
            catch { }
        }
        if (forward)
        {
            lock (OutboxLock) outbox.AddRange(fresh);
        }
        Save();
    }

    // Snapshot is retained until HTTP succeeds. A crash after remote acceptance can replay it;
    // sequence IDs make this explicitly at-least-once rather than silently lossy.
    void Forward()
    {
        string[] batch;
        lock (OutboxLock)
        {
            if (forwarding || outbox.Count == 0) return;
            forwarding = true;
            int n = Math.Min(100, outbox.Count);
            batch = outbox.GetRange(0, n).ToArray();
        }
        Task.Run(delegate
        {
            bool ok = false;
            try
            {
                NameValueCollection c = new NameValueCollection();
                c["lines"] = string.Join("\n", batch);
                c["day"] = gameDay.ToString(CultureInfo.InvariantCulture);
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    wc.UploadValues(server + "/gvl_event", "POST", c);
                }
                ok = true;
            }
            catch (Exception ex) { Log("forward event loi: " + ex.Message); }
            finally
            {
                if (ok)
                {
                    lock (OutboxLock)
                    {
                        int remove = Math.Min(batch.Length, outbox.Count);
                        for (int i = 0; i < remove; i++)
                        {
                            if (outbox[0] != batch[i]) break;
                            outbox.RemoveAt(0);
                        }
                    }
                    Save();
                }
                lock (OutboxLock) forwarding = false;
            }
        });
    }

    void Save()
    {
        lock (SaveLock)
        try
        {
            List<string> snapshot;
            lock (OutboxLock) snapshot = new List<string>(outbox);
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", snapshot.ToArray())));
            string[] lines = {
                "day=" + gameDay.ToString(CultureInfo.InvariantCulture),
                "seq=" + lastSeenSeq.ToString(CultureInfo.InvariantCulture),
                "outbox=" + encoded
            };
            File.WriteAllLines(TempFile, lines, Encoding.UTF8);
            if (File.Exists(SaveFile))
            {
                try { File.Copy(SaveFile, BackupFile, true); } catch { }
                File.Replace(TempFile, SaveFile, null);
            }
            else File.Move(TempFile, SaveFile);
        }
        catch (Exception ex) { Log("save loi: " + ex.Message); }
    }

    void Load()
    {
        if (!LoadFrom(SaveFile)) LoadFrom(BackupFile);
        object current = Get("GVL.EventSeq");
        long currentSeq = current is long ? (long)current : 0;
        Set("GVL.EventSeq", Math.Max(lastSeenSeq, currentSeq));
    }

    bool LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            long loadedDay = 0, loadedSeq = 0;
            List<string> loadedOutbox = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string key = line.Substring(0, i), value = line.Substring(i + 1);
                if (key == "day") long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out loadedDay);
                else if (key == "seq") long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out loadedSeq);
                else if (key == "outbox" && value.Length > 0)
                {
                    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                    foreach (string item in decoded.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) loadedOutbox.Add(item);
                }
            }
            gameDay = Math.Max(0, loadedDay);
            lastSeenSeq = Math.Max(0, loadedSeq);
            lock (OutboxLock) outbox.AddRange(loadedOutbox);
            return true;
        }
        catch (Exception ex) { Log("load " + path + " loi: " + ex.Message); return false; }
    }

    sealed class HostWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            request.Timeout = 3000;
            HttpWebRequest http = request as HttpWebRequest;
            if (http != null) http.ReadWriteTimeout = 3000;
            return request;
        }
    }

    string ReadHostToken()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GtaVietLife", "host.token");
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : "";
        }
        catch { return ""; }
    }

    void MaintainHostSession(int now)
    {
        if (now < nextHostHeartbeat) return;
        nextHostHeartbeat = now + 5000;
        lock (HostLock)
        {
            if (hostConnecting) return;
            hostConnecting = true;
        }
        Task.Run(delegate
        {
            try
            {
                if (!hostConnected) RegisterHostSession();
                else SendHostHeartbeat();
            }
            catch (Exception ex) { hostConnected = false; Log("engine host heartbeat loi: " + ex.Message); }
            finally { lock (HostLock) hostConnecting = false; }
        });
    }

    void RegisterHostSession()
    {
        try
        {
            if (string.IsNullOrEmpty(hostToken)) hostToken = ReadHostToken();
            if (string.IsNullOrEmpty(hostToken)) throw new Exception("khong tim thay host.token");
            string body = "{\"protocol\":\"gvl-engine/1\",\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\",\"session_id\":\"" + hostSessionId +
                "\",\"adapter\":{\"id\":\"gta5.shvdn\",\"version\":\"0.1.0\",\"base_game\":\"gta5\",\"base_game_build\":\"unknown\"},\"capabilities\":[\"adapter.session\"]}";
            using (WebClient wc = new HostWebClient())
            {
                wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                wc.Headers["X-GVL-Token"] = hostToken;
                string response = wc.UploadString(hostServer + "/engine/v1/handshake", "POST", body);
                if (response.IndexOf("\"accepted\":true", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception("host tu choi handshake: " + response);
            }
            hostConnected = true;
            Log("engine host handshake thanh cong, session=" + hostSessionId);
        }
        catch (Exception ex) { hostConnected = false; Log("engine host handshake chua thanh cong: " + ex.Message); }
    }

    void SendHostHeartbeat()
    {
        string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\",\"session_id\":\"" + hostSessionId + "\"}";
        using (WebClient wc = new HostWebClient())
        {
            wc.Proxy = null; wc.Encoding = Encoding.UTF8;
            wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
            wc.Headers["X-GVL-Token"] = hostToken;
            string response = wc.UploadString(hostServer + "/engine/v1/heartbeat", "POST", body);
            if (response.IndexOf("\"accepted\":true", StringComparison.OrdinalIgnoreCase) < 0)
                throw new Exception("host heartbeat bi tu choi: " + response);
        }
    }

    void CloseHostSession()
    {
        try
        {
            if (string.IsNullOrEmpty(hostToken)) return;
            using (WebClient wc = new HostWebClient())
            {
                wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                wc.Headers["X-GVL-Token"] = hostToken;
                wc.UploadString(hostServer + "/engine/v1/close", "POST", "{\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\",\"session_id\":\"" + hostSessionId + "\"}");
            }
        }
        catch { }
    }

    static void Log(string msg)
    {
        try { File.AppendAllText("GtaVietLifeCore.log", DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + msg + Environment.NewLine); }
        catch { }
    }
}