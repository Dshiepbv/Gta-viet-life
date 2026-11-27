// GTA Viet Life: shared runtime core.
// Compatibility: preserves the AppDomain GVL.* contract and the legacy key=value save format.
// Durable event delivery is at-least-once: consumers should deduplicate by sequence number.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
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
    bool hostConnecting, residentSyncing;
    long nextHostHeartbeatTicks;
    int nextResidentSync, nextEngineCommandPoll;
    bool engineCommandPolling;
    readonly object EngineCommandLock = new object();
    readonly HashSet<string> queuedEngineCommandIds = new HashSet<string>();
    bool forward = true, logEvents = true, forwarding;
    long gameDay, lastSeenSeq, currentWorldMinute;
    int lastMinute = -1, nextPublish, nextForward, nextSave;
    readonly List<string> outbox = new List<string>();
    readonly List<string> engineOutbox = new List<string>();
    bool engineEventForwarding;
    int nextEngineEventForward;
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
        if (Get("GVL.PayCash") == null) Set("GVL.PayCash", new Func<int, string, string, bool>(DefaultPayCash));
        if (Get("GVL.EarnCash") == null) Set("GVL.EarnCash", new Action<int, string, string>(DefaultEarnCash));
        Set("GVL.CoreVersion", 2);
        Set("GVL.Engine.Commands", new Queue<Dictionary<string, object>>());
        Set("GVL.Engine.Ack", new Action<string>(AckEngineCommand));
        Set("GVL.Engine.Report", new Action<string, string, string>(ReportEngineCommand));
        Set("GVL.Engine.Context", new Func<string, string, string>(GetNpcDialogueContext));
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

    // Cash-only delegates preserve legacy in-game cash semantics while recording a shared ledger event.
    static bool DefaultPayCash(int amount, string cat, string note)
    {
        if (amount <= 0) return true;
        if (Game.Player.Money < amount) return false;
        Game.Player.Money = Game.Player.Money - amount;
        Emit("ledger.expense", "amount=" + amount + ";cat=" + Clean(cat) + ";note=" + Clean(note) + ";acct=cash");
        return true;
    }

    static void DefaultEarnCash(int amount, string cat, string note)
    {
        if (amount <= 0) return;
        Game.Player.Money = Game.Player.Money + amount;
        Emit("ledger.income", "amount=" + amount + ";cat=" + Clean(cat) + ";note=" + Clean(note) + ";acct=cash");
    }

    void OnTick(object sender, EventArgs e)
    {
        int now = Game.GameTime;
        TimeSpan t = World.CurrentTimeOfDay;
        int minute = t.Hours * 60 + t.Minutes;
        if (lastMinute >= 0 && minute < lastMinute && lastMinute - minute > 600)
        {
            gameDay++;
            Emit("core.day", "day=" + gameDay + ";weekday=" + (gameDay % 7));
            Save();
        }
        lastMinute = minute;
        currentWorldMinute = gameDay * 1440L + minute;
        MaintainHostSession(Stopwatch.GetTimestamp());
        if (hostConnected && now > nextResidentSync) { nextResidentSync = now + 10000; SyncResidentProfiles(); }
        if (hostConnected && now > nextEngineCommandPoll) { nextEngineCommandPoll = now + 2000; PollEngineCommands(); }

        if (now > nextPublish)
        {
            nextPublish = now + 250;
            clock[0] = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            clock[1] = gameDay; clock[2] = minute; clock[3] = gameDay % 7;
            clock[4] = gameDay % 7 >= 5 ? 1 : 0; clock[5] = t.Hours;
            Set("GVL.Clock", clock);
            DrainEvents();
        }
        if (hostConnected && now > nextEngineEventForward) { nextEngineEventForward = now + 5000; ForwardEngineEvents(); }
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
        lock (OutboxLock) engineOutbox.AddRange(fresh);
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
            List<string> snapshot, engineSnapshot;
            lock (OutboxLock) { snapshot = new List<string>(outbox); engineSnapshot = new List<string>(engineOutbox); }
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", snapshot.ToArray())));
            string engineEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", engineSnapshot.ToArray())));
            string[] lines = {
                "day=" + gameDay.ToString(CultureInfo.InvariantCulture),
                "seq=" + lastSeenSeq.ToString(CultureInfo.InvariantCulture),
                "outbox=" + encoded,
                "engine_outbox=" + engineEncoded
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
            List<string> loadedEngineOutbox = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string key = line.Substring(0, i), value = line.Substring(i + 1);
                if (key == "day") long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out loadedDay);
                else if (key == "seq") long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out loadedSeq);
                else if ((key == "outbox" || key == "engine_outbox") && value.Length > 0)
                {
                    string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                    List<string> destination = key == "outbox" ? loadedOutbox : loadedEngineOutbox;
                    foreach (string item in decoded.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) destination.Add(item);
                }
            }
            gameDay = Math.Max(0, loadedDay);
            lastSeenSeq = Math.Max(0, loadedSeq);
            lock (OutboxLock) { outbox.AddRange(loadedOutbox); engineOutbox.AddRange(loadedEngineOutbox); }
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

    // Use a monotonic wall clock: Game.GameTime can stop while GTA is paused.
    void MaintainHostSession(long nowTicks)
    {
        if (nowTicks < nextHostHeartbeatTicks) return;
        nextHostHeartbeatTicks = nowTicks + Stopwatch.Frequency * 5L;
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
            string gamePath = "";
            string gameVersion = "unknown";
            string gameEdition = "unknown";
            try
            {
                gamePath = Process.GetCurrentProcess().MainModule.FileName;
                gameVersion = FileVersionInfo.GetVersionInfo(gamePath).FileVersion ?? "unknown";
                string executableName = Path.GetFileName(gamePath);
                if (executableName.IndexOf("Enhanced", StringComparison.OrdinalIgnoreCase) >= 0)
                    gameEdition = "gta5-enhanced";
                else if (executableName.Equals("GTA5.exe", StringComparison.OrdinalIgnoreCase))
                    gameEdition = "gta5-legacy";
            }
            catch { }
            string body = "{\"protocol\":\"gvl-engine/1\",\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\",\"session_id\":\"" + hostSessionId +
                "\",\"world_minute\":" + currentWorldMinute.ToString(CultureInfo.InvariantCulture) + ",\"adapter\":{\"id\":\"gta5.shvdn\",\"version\":\"0.1.0\",\"base_game\":\"" + gameEdition + "\",\"base_game_build\":\"" + gameVersion +
                "\"},\"capabilities\":[\"adapter.session\",\"adapter.command_poll\",\"adapter.actor_activity\"]}";
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

    static string JsonEscape(string value)
    {
        StringBuilder b = new StringBuilder();
        foreach (char ch in value ?? "")
        {
            if (ch == '\\') b.Append("\\\\");
            else if (ch == '"') b.Append("\\\"");
            else if (ch == '\n') b.Append("\\n");
            else if (ch == '\r') b.Append("\\r");
            else if (ch == '\t') b.Append("\\t");
            else if (ch < 32) b.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
            else b.Append(ch);
        }
        return b.ToString();
    }

    // Import legacy persistent residents into the game-neutral life engine.
    // This syncs identity and schedule only; it does not claim physical NPC control.
    void SyncResidentProfiles()
    {
        lock (HostLock)
        {
            if (residentSyncing || !hostConnected || string.IsNullOrEmpty(hostToken)) return;
            residentSyncing = true;
        }
        Task.Run(delegate
        {
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, "GtaVietLifeResidents.txt");
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 3) continue;
                    string npcId = parts[1].Trim();
                    string name = parts[2].Trim();
                    if (!npcId.StartsWith("res_", StringComparison.Ordinal) || name.Length == 0) continue;
                    string home = npcId + ".home";
                    string work = npcId + ".work";
                    string lunch = npcId + ".lunch";
                    string leisure = npcId + ".leisure";
                    string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\",\"npc\":{" +
                        "\"npc_id\":\"" + JsonEscape(npcId) + "\",\"display_name\":\"" + JsonEscape(name) +
                        "\",\"home_location_id\":\"" + home + "\",\"occupation\":\"resident\",\"life_goal\":\"social\"," +
                        "\"traits\":[\"community_resident\"],\"schedule\":[" +
                        "{\"start_minute\":0,\"end_minute\":360,\"activity\":\"sleep\",\"location_id\":\"" + home + "\"}," +
                        "{\"start_minute\":360,\"end_minute\":420,\"activity\":\"free_time\",\"location_id\":\"" + home + "\"}," +
                        "{\"start_minute\":420,\"end_minute\":660,\"activity\":\"work\",\"location_id\":\"" + work + "\"}," +
                        "{\"start_minute\":660,\"end_minute\":780,\"activity\":\"eat\",\"location_id\":\"" + lunch + "\"}," +
                        "{\"start_minute\":780,\"end_minute\":1020,\"activity\":\"work\",\"location_id\":\"" + work + "\"}," +
                        "{\"start_minute\":1020,\"end_minute\":1140,\"activity\":\"leisure\",\"location_id\":\"" + leisure + "\"}," +
                        "{\"start_minute\":1140,\"end_minute\":1380,\"activity\":\"free_time\",\"location_id\":\"" + home + "\"}," +
                        "{\"start_minute\":1380,\"end_minute\":1440,\"activity\":\"sleep\",\"location_id\":\"" + home + "\"}]}}";
                    using (WebClient wc = new HostWebClient())
                    {
                        wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                        wc.Headers["X-GVL-Token"] = hostToken;
                        wc.UploadString(hostServer + "/engine/v1/simulation/npcs", "POST", body);
                    }
                }
            }
            catch (Exception ex) { Log("dong bo cu dan vao life engine loi: " + ex.Message); }
            finally { lock (HostLock) residentSyncing = false; }
        });
    }

    void PollEngineCommands()
    {
        lock (HostLock)
        {
            if (engineCommandPolling || !hostConnected || string.IsNullOrEmpty(hostToken)) return;
            engineCommandPolling = true;
        }
        Task.Run(delegate
        {
            try
            {
                using (WebClient wc = new HostWebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    wc.Headers["X-GVL-Token"] = hostToken;
                    string response = wc.DownloadString(hostServer + "/engine/v1/adapter/commands");
                    JavaScriptSerializer json = new JavaScriptSerializer();
                    Dictionary<string, object> root = json.Deserialize<Dictionary<string, object>>(response);
                    object raw;
                    if (root == null || !root.TryGetValue("commands", out raw)) return;
                    object[] commands = raw as object[];
                    if (commands == null) return;
                    Queue<Dictionary<string, object>> queue = Get("GVL.Engine.Commands") as Queue<Dictionary<string, object>>;
                    if (queue == null) { queue = new Queue<Dictionary<string, object>>(); Set("GVL.Engine.Commands", queue); }
                    foreach (object item in commands)
                    {
                        Dictionary<string, object> command = item as Dictionary<string, object>;
                        if (command == null) continue;
                        object idValue;
                        if (!command.TryGetValue("command_id", out idValue)) continue;
                        string id = Convert.ToString(idValue, CultureInfo.InvariantCulture);
                        if (string.IsNullOrEmpty(id)) continue;
                        lock (EngineCommandLock)
                        {
                            if (!queuedEngineCommandIds.Add(id)) continue;
                        }
                        lock (queue) queue.Enqueue(command);
                    }
                }
            }
            catch (Exception ex) { Log("poll lenh life engine loi: " + ex.Message); }
            finally { lock (HostLock) engineCommandPolling = false; }
        });
    }

    void AckEngineCommand(string commandId)
    {
        ReportEngineCommand(commandId, "completed", "");
    }

    // Record both successful execution and terminal adapter outcomes. Deferred work
    // stays in the local queue; unsupported work must not remain pending forever.
    void ReportEngineCommand(string commandId, string status, string reason)
    {
        if (string.IsNullOrEmpty(commandId) || string.IsNullOrEmpty(hostToken)) return;
        if (status != "completed" && status != "failed" && status != "unsupported" &&
            status != "rejected" && status != "cancelled") status = "failed";
        if (reason != null && reason.Length > 240) reason = reason.Substring(0, 240);
        Task.Run(delegate
        {
            try
            {
                bool success = status == "completed";
                string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
                    "\",\"command_id\":\"" + JsonEscape(commandId) +
                    "\",\"result\":{\"success\":" + (success ? "true" : "false") +
                    ",\"status\":\"" + status + "\",\"reason\":\"" + JsonEscape(reason ?? "") + "\"}}";
                using (WebClient wc = new HostWebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    wc.Headers["X-GVL-Token"] = hostToken;
                    wc.UploadString(hostServer + "/engine/v1/adapter/commands/ack", "POST", body);
                }
            }
            catch (Exception ex) { Log("bao ket qua lenh life engine loi: " + ex.Message); }
            // Host pending state is authoritative; lost reports are safely redelivered.
            lock (EngineCommandLock) queuedEngineCommandIds.Remove(commandId);
        });
    }

    // Context reads are performed by the voice bridge worker, never on the GTA game tick.
    // The Host remains authoritative; the bridge receives bounded facts only.
    string GetNpcDialogueContext(string npcId, string playerId)
    {
        if (string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(hostToken) || !hostConnected) return "";
        try
        {
            string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
                "\",\"npc_id\":\"" + JsonEscape(npcId) + "\",\"player_id\":\"" +
                JsonEscape(playerId ?? "player.main") + "\"}";
            using (WebClient wc = new HostWebClient())
            {
                wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                wc.Headers["X-GVL-Token"] = hostToken;
                return wc.UploadString(hostServer + "/engine/v1/simulation/context", "POST", body);
            }
        }
        catch (Exception ex) { Log("doc ngu canh NPC tu life engine loi: " + ex.Message); return ""; }
    }

    void SendHostHeartbeat()
    {
        string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\",\"session_id\":\"" + hostSessionId +
            "\",\"world_minute\":" + currentWorldMinute.ToString(CultureInfo.InvariantCulture) + "}";
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