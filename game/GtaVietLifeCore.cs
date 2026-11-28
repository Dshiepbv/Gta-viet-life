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
    readonly GvlSaveFile store = new GvlSaveFile(SaveFile, GvlSaveFile.LooksKeyValue, Log);
    static readonly string[] Modules = { "Personal", "Legal", "Body", "Social", "Life", "Economy" };
    static readonly object OutboxLock = new object();
    static readonly object SaveLock = new object();

    const string LegacyClockId = "legacy";
    string clockId = "";
    string server = "http://127.0.0.1:5577";
    string hostServer = "http://127.0.0.1:5580";
    string hostSessionId = Guid.NewGuid().ToString("N");
    string hostToken = "";
    readonly object HostLock = new object();
    volatile bool hostConnected;
    bool hostConnecting, residentSyncing, playerSyncing;
    long nextHostHeartbeatTicks;
    int nextResidentSync, nextEngineCommandPoll, nextWorldCommandPoll, nextWalletBaselineSync, nextWorldPostRetry;
    bool engineCommandPolling, worldCommandPolling;
    readonly object EngineCommandLock = new object();
    readonly object WorldPostQueueLock = new object();
    readonly GvlPostQueue worldPostQueue = new GvlPostQueue();
    volatile bool worldOutboxDirty;
    // Event tien cua engine khong bao gio bi bo vi loi 5xx; bridge chi la ban sao nen co gioi han.
    readonly GvlBatchIsolation engineBatch = new GvlBatchIsolation(false);
    readonly GvlBatchIsolation bridgeBatch = new GvlBatchIsolation(true);
    long worldPostNotBeforeTicks;
    bool worldPostWorker;
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
        Set("GVL.Engine.WorldCommands", new Queue<Dictionary<string, object>>());
        Set("GVL.Engine.WorldPost", new Action<string, string>(PostWorldRequest));
        Set("GVL.Engine.WorldAck", new Action<string, string, string>(ReportWorldCommand));
        Set("GVL.Engine.Context", new Func<string, string, string>(GetNpcDialogueContext));
        Set("GVL.Engine.RecordDialogue", new Action<string, string>(RecordNpcDialogue));
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
        if (hostConnected && now > nextWalletBaselineSync)
        {
            nextWalletBaselineSync = now + 10000;
            SyncPlayerProfile();
            SyncPlayerWalletBaseline();
        }
        if (hostConnected && now > nextEngineCommandPoll) { nextEngineCommandPoll = now + 2000; PollEngineCommands(); }
        if (hostConnected && now > nextWorldCommandPoll) { nextWorldCommandPoll = now + 2000; PollEngineWorldCommands(); }
        if (hostConnected && now > nextWorldPostRetry) { nextWorldPostRetry = now + 5000; StartWorldPostWorker(); }

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
        if (worldOutboxDirty) { worldOutboxDirty = false; Save(); }
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
            int n = GvlSendRules.BatchCount(outbox, bridgeBatch.Limit, GvlSendRules.MaxBatchBytes);
            batch = outbox.GetRange(0, n).ToArray();
        }
        Task.Run(delegate
        {
            bool remove = false;
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
                remove = true;
                lock (OutboxLock) bridgeBatch.Succeeded(batch.Length);
            }
            catch (Exception ex)
            {
                int status = HttpStatusOf(ex);
                bool drop;
                lock (OutboxLock) drop = bridgeBatch.Failed(status, batch.Length);
                if (drop) { DeadLetter("bridge.gvl_event", status, ex.Message, string.Join("\n", batch)); remove = true; }
                Log("forward event loi (HTTP " + status + "): " + ex.Message);
            }
            finally
            {
                if (remove)
                {
                    lock (OutboxLock) RemoveSentHead(outbox, batch);
                    Save();
                }
                lock (OutboxLock) forwarding = false;
            }
        });
    }

    // Chay tren luong script: Game.Player.Money khong duoc goi trong Task.Run.
    void SyncPlayerWalletBaseline()
    {
        try
        {
            object seqValue = Get("GVL.EventSeq");
            long sequence = seqValue is long ? (long)seqValue : 0;
            string source = GvlSource.Id(clockId);
            int balance = Math.Max(0, Game.Player.Money);
            string requestId = Guid.NewGuid().ToString("N");
            string body = "{\"request_id\":\"" + requestId +
                "\",\"account_id\":\"legacy:player_cash\",\"owner_id\":\"player.main\",\"opening_balance\":" +
                balance.ToString(CultureInfo.InvariantCulture) + ",\"as_of_source\":\"" + source + "\",\"as_of_sequence\":" +
                sequence.ToString(CultureInfo.InvariantCulture) + ",\"allow_negative\":true,\"currency\":\"USD\"}";
            PostWorldRequest("/engine/v1/world/accounts/ensure", body);
            object bankValue = Get("GVL.Bank");
            if (!(bankValue is int)) return;   // chua co so ngan hang thi khong mo so du 0
            int bank = Math.Max(0, (int)bankValue);
            string bankBody = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
                "\",\"account_id\":\"legacy:player_bank\",\"owner_id\":\"player.main\",\"opening_balance\":" +
                bank.ToString(CultureInfo.InvariantCulture) + ",\"as_of_source\":\"" + source + "\",\"as_of_sequence\":" +
                sequence.ToString(CultureInfo.InvariantCulture) + ",\"allow_negative\":true,\"currency\":\"USD\"}";
            PostWorldRequest("/engine/v1/world/accounts/ensure", bankBody);
        }
        catch (Exception ex) { Log("dong bo so du tien mat voi life engine loi: " + ex.Message); }
    }

    // A separate durable outbox feeds the canonical Life Engine journal.
    // It is independent of the legacy bridge acknowledgement so neither consumer can lose events.
    void ForwardEngineEvents()
    {
        string[] batch;
        lock (OutboxLock)
        {
            if (engineEventForwarding || engineOutbox.Count == 0 || !hostConnected || string.IsNullOrEmpty(hostToken)) return;
            engineEventForwarding = true;
            batch = engineOutbox.GetRange(0, GvlSendRules.BatchCount(engineOutbox, engineBatch.Limit, GvlSendRules.MaxBatchBytes)).ToArray();
        }
        Task.Run(delegate
        {
            bool ok = false;
            bool dropped = false;
            try
            {
                List<Dictionary<string, object>> events = new List<Dictionary<string, object>>();
                foreach (string line in batch)
                {
                    string[] parts = (line ?? "").Split(new[] { '\t' }, 3);
                    long seq;
                    if (parts.Length < 3 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out seq)) continue;
                    events.Add(new Dictionary<string, object> {
                        { "sequence", seq }, { "event_type", parts[1] }, { "data", parts[2] },
                        { "sim_minute", currentWorldMinute }
                    });
                }
                if (events.Count == 0) ok = true;
                else
                {
                    Dictionary<string, object> request = new Dictionary<string, object> {
                        { "request_id", Guid.NewGuid().ToString("N") },
                        { "source_id", GvlSource.Id(clockId) }, { "events", events }
                    };
                    JavaScriptSerializer json = new JavaScriptSerializer();
                    using (WebClient wc = new HostWebClient())
                    {
                        wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                        wc.Headers["X-GVL-Token"] = hostToken;
                        string response = wc.UploadString(hostServer + "/engine/v1/world/events", "POST", json.Serialize(request));
                        ok = response.IndexOf("\"accepted\"", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (ok && response.IndexOf("\"rejected\":[{", StringComparison.Ordinal) >= 0)
                            DeadLetter("engine.world.events", 200, response, string.Join("\n", batch));
                    }
                }
                if (ok) lock (OutboxLock) engineBatch.Succeeded(batch.Length);
            }
            catch (Exception ex)
            {
                int status = HttpStatusOf(ex);
                lock (OutboxLock) dropped = engineBatch.Failed(status, batch.Length);
                if (dropped) DeadLetter("engine.world.events", status, ex.Message, string.Join("\n", batch));
                Log("forward event vao life engine loi (HTTP " + status + "): " + ex.Message);
            }
            finally
            {
                if (ok || dropped)
                {
                    lock (OutboxLock) RemoveSentHead(engineOutbox, batch);
                    Save();
                }
                lock (OutboxLock) engineEventForwarding = false;
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
            string worldEncoded;
            lock (WorldPostQueueLock) worldEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(worldPostQueue.Serialize()));
            string[] lines = {
                "day=" + gameDay.ToString(CultureInfo.InvariantCulture),
                "seq=" + lastSeenSeq.ToString(CultureInfo.InvariantCulture),
                "outbox=" + encoded,
                "engine_outbox=" + engineEncoded,
                "world_outbox=" + worldEncoded,
                "clock_id=" + clockId
            };
            store.Write(lines);
        }
        catch (Exception ex) { Log("save loi: " + ex.Message); }
    }

    void Load()
    {
        bool existed = File.Exists(SaveFile) || File.Exists(SaveFile + ".bak");
        if (!store.Load(ParseSaveLine))
            Log("khong doc duoc " + SaveFile + "; giu nguyen file, hang doi moi KHONG duoc luu");
        // Mat file thi bo dem ngay va seq quay ve 0: dung dinh danh moi de engine khong coi la trung.
        // File do ban cu ghi khong co clock_id va tiep tuc dung nguon cu.
        if (clockId.Length == 0)
            clockId = existed && !store.Blocked ? LegacyClockId : Guid.NewGuid().ToString("N").Substring(0, 12);
        object current = Get("GVL.EventSeq");
        long currentSeq = current is long ? (long)current : 0;
        Set("GVL.EventSeq", Math.Max(lastSeenSeq, currentSeq));
        Set("GVL.EventSource", GvlSource.Id(clockId));
    }

    void ParseSaveLine(string line)
    {
        int i = line.IndexOf('=');
        if (i < 0) return;
        string key = line.Substring(0, i), value = line.Substring(i + 1);
        if (key == "day") gameDay = Math.Max(0, long.Parse(value, CultureInfo.InvariantCulture));
        else if (key == "seq") lastSeenSeq = Math.Max(0, long.Parse(value, CultureInfo.InvariantCulture));
        else if (key == "clock_id") clockId = value.Trim();
        else if (key == "world_outbox" && value.Length > 0)
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            lock (WorldPostQueueLock) worldPostQueue.Restore(decoded);
        }
        else if ((key == "outbox" || key == "engine_outbox") && value.Length > 0)
        {
            string[] items = Encoding.UTF8.GetString(Convert.FromBase64String(value))
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lock (OutboxLock) (key == "outbox" ? outbox : engineOutbox).AddRange(items);
        }
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
            catch (Exception ex) { hostConnected = false; Set("GVL.Engine.Connected", false); Log("engine host heartbeat loi: " + ex.Message); }
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
                "\",\"clock_id\":\"" + clockId + "\",\"world_minute\":" + currentWorldMinute.ToString(CultureInfo.InvariantCulture) + ",\"adapter\":{\"id\":\"gta5.shvdn\",\"version\":\"0.1.0\",\"base_game\":\"" + gameEdition + "\",\"base_game_build\":\"" + gameVersion +
                "\"},\"capabilities\":[\"adapter.session\",\"adapter.command_poll\",\"adapter.actor_activity\",\"adapter.dialogue_context\",\"adapter.event_outbox\",\"adapter.world_interaction\",\"adapter.space_transition\",\"adapter.object_interaction\"]}";
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
            Set("GVL.Engine.Connected", true);
            Log("engine host handshake thanh cong, session=" + hostSessionId);
        }
        catch (Exception ex) { hostConnected = false; Set("GVL.Engine.Connected", false); Log("engine host handshake chua thanh cong: " + ex.Message); }
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
    void SyncPlayerProfile()
    {
        lock (HostLock)
        {
            if (playerSyncing || !hostConnected || string.IsNullOrEmpty(hostToken)) return;
            playerSyncing = true;
        }
        Task.Run(delegate
        {
            try
            {
                string playerName = Settings.GetValue("Player", "Name", "Player");
                string occupation = Settings.GetValue("Player", "Occupation", "citizen");
                string home = Settings.GetValue("Player", "HomeLocation", "player.home");
                int age = Settings.GetValue("Player", "Age", 30);
                age = Math.Max(18, Math.Min(100, age));
                string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\",\"npc\":{" +
                    "\"npc_id\":\"player.main\",\"display_name\":\"" + JsonEscape(playerName) +
                    "\",\"home_location_id\":\"" + JsonEscape(home) + "\",\"occupation\":\"" +
                    JsonEscape(occupation) + "\",\"actor_kind\":\"player\",\"age_years\":" +
                    age.ToString(CultureInfo.InvariantCulture) + ",\"life_goal\":\"career\",\"schedule\":[]}}";
                using (WebClient wc = new HostWebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    wc.Headers["X-GVL-Token"] = hostToken;
                    try { wc.UploadString(hostServer + "/engine/v1/simulation/actors", "POST", body); }
                    catch (WebException ex)
                    {
                        // 4xx: ho so bi tu choi nhung host van song, van phai dong bo so du.
                        int status = HttpStatusOf(ex);
                        if (status < 400 || status >= 500) throw;
                        Log("host tu choi ho so nguoi choi (HTTP " + status + "): " + ex.Message);
                    }
                }
            }
            catch (Exception ex) { Log("dong bo nhan vat nguoi choi vao life engine loi: " + ex.Message); }
            finally { lock (HostLock) playerSyncing = false; }
        });
    }

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
                    string leisure = "residents.leisure";
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
                        try { wc.UploadString(hostServer + "/engine/v1/simulation/npcs", "POST", body); }
                        catch (WebException ex)
                        {
                            int status = HttpStatusOf(ex);
                            if (status < 400 || status >= 500) throw;
                            Log("host tu choi cu dan " + npcId + " (HTTP " + status + "): " + ex.Message);
                        }
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

    void PostWorldRequest(string path, string body)
    {
        if (!GvlSendRules.IsEnginePostPath(path))
        {
            Log("tu choi gui toi duong dan engine khong duoc phep: " + path);
            return;
        }
        if (string.IsNullOrEmpty(body)) return;
        // Ngoai tuyen van nhan vao hang; worker chi gui khi da ket noi.
        GvlPost evicted;
        lock (WorldPostQueueLock) evicted = worldPostQueue.Add(path, body);
        if (evicted != null) DeadLetter(evicted.Path, 0, "hang doi day", evicted.Body);
        if (GvlPostQueue.IsDurable(path)) worldOutboxDirty = true;
        StartWorldPostWorker();
    }

    void StartWorldPostWorker()
    {
        lock (WorldPostQueueLock)
        {
            if (worldPostWorker || worldPostQueue.Count == 0 || !hostConnected || string.IsNullOrEmpty(hostToken)) return;
            if (Stopwatch.GetTimestamp() < worldPostNotBeforeTicks) return;   // gian cach giua cac lan thu lai
            worldPostWorker = true;
        }
        Task.Run(delegate
        {
            while (true)
            {
                GvlPost item;
                lock (WorldPostQueueLock)
                {
                    item = worldPostQueue.Head();
                    if (item == null || !hostConnected) { worldPostWorker = false; return; }
                }
                try
                {
                    using (WebClient wc = new HostWebClient())
                    {
                        wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                        wc.Headers["X-GVL-Token"] = hostToken;
                        wc.UploadString(hostServer + item.Path, "POST", item.Body);
                    }
                    lock (WorldPostQueueLock) worldPostQueue.Remove(item);
                    if (GvlPostQueue.IsDurable(item.Path)) worldOutboxDirty = true;
                }
                catch (Exception ex)
                {
                    int status = HttpStatusOf(ex);
                    bool drop;
                    lock (WorldPostQueueLock)
                    {
                        drop = worldPostQueue.Failed(item, status);
                        if (!drop)
                        {
                            worldPostWorker = false;
                            worldPostNotBeforeTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 4L;
                        }
                    }
                    if (GvlPostQueue.IsDurable(item.Path)) worldOutboxDirty = true;
                    if (drop)
                    {
                        DeadLetter(item.Path, status, ex.Message, item.Body);
                        continue;
                    }
                    Log("gui toi engine loi (HTTP " + status + "); se thu lai: " + item.Path + " " + ex.Message);
                    return;
                }
            }
        });
    }

    static int HttpStatusOf(Exception ex)
    {
        WebException web = ex as WebException;
        HttpWebResponse response = web == null ? null : web.Response as HttpWebResponse;
        return response == null ? 0 : (int)response.StatusCode;
    }

    static void RemoveSentHead(List<string> queue, string[] sent)
    {
        for (int i = 0; i < sent.Length && queue.Count > 0; i++)
        {
            if (queue[0] != sent[i]) break;
            queue.RemoveAt(0);
        }
    }

    // Yeu cau bi tu choi vinh vien duoc giu lai de kiem tra, khong chan hang doi.
    static void DeadLetter(string channel, int status, string error, string content)
    {
        try
        {
            File.AppendAllText("GtaVietLifeCore.deadletter.log",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t" + channel +
                "\tHTTP " + status + "\t" + (error ?? "").Replace('\r', ' ').Replace('\n', ' ') + "\t" +
                (content ?? "").Replace('\r', ' ').Replace('\n', '|') + Environment.NewLine);
        }
        catch { }
        Log("bo qua yeu cau bi tu choi (HTTP " + status + "): " + channel);
    }

    void PollEngineWorldCommands()
    {
        lock (HostLock)
        {
            if (worldCommandPolling || !hostConnected || string.IsNullOrEmpty(hostToken)) return;
            worldCommandPolling = true;
        }
        Task.Run(delegate
        {
            try
            {
                using (WebClient wc = new HostWebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    wc.Headers["X-GVL-Token"] = hostToken;
                    string response = wc.DownloadString(hostServer + "/engine/v1/world/commands");
                    JavaScriptSerializer json = new JavaScriptSerializer();
                    Dictionary<string, object> root = json.Deserialize<Dictionary<string, object>>(response);
                    object raw;
                    if (root == null || !root.TryGetValue("commands", out raw)) return;
                    object[] commands = raw as object[];
                    if (commands == null) return;
                    Queue<Dictionary<string, object>> queue = Get("GVL.Engine.WorldCommands") as Queue<Dictionary<string, object>>;
                    if (queue == null) { queue = new Queue<Dictionary<string, object>>(); Set("GVL.Engine.WorldCommands", queue); }
                    foreach (object item in commands)
                    {
                        Dictionary<string, object> command = item as Dictionary<string, object>;
                        if (command == null) continue;
                        Dictionary<string, object> payload = command.ContainsKey("payload") ? command["payload"] as Dictionary<string, object> : null;
                        object adapterValue;
                        if (payload == null || !payload.TryGetValue("adapter_id", out adapterValue) ||
                            Convert.ToString(adapterValue, CultureInfo.InvariantCulture) != "gta5.shvdn") continue;
                        lock (queue)
                        {
                            bool exists = false;
                            foreach (Dictionary<string, object> queued in queue)
                            {
                                object oldId;
                                if (queued.TryGetValue("command_id", out oldId) &&
                                    Convert.ToString(oldId, CultureInfo.InvariantCulture) == Convert.ToString(command["command_id"], CultureInfo.InvariantCulture))
                                { exists = true; break; }
                            }
                            if (!exists) queue.Enqueue(command);
                        }
                    }
                }
            }
            catch (Exception ex) { Log("poll world commands loi: " + ex.Message); }
            finally { lock (HostLock) worldCommandPolling = false; }
        });
    }

    void ReportWorldCommand(string commandId, string status, string reason)
    {
        if (string.IsNullOrEmpty(commandId) || string.IsNullOrEmpty(hostToken)) return;
        if (status != "completed" && status != "failed" && status != "unsupported" &&
            status != "rejected" && status != "cancelled") status = "failed";
        Task.Run(delegate
        {
            try
            {
                bool success = status == "completed";
                string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
                    "\",\"command_id\":\"" + JsonEscape(commandId) +
                    "\",\"result\":{\"success\":" + (success ? "true" : "false") +
                    ",\"status\":\"" + status + "\",\"reason\":\"" + JsonEscape(reason ?? "") +
                    "\",\"adapter_id\":\"gta5.shvdn\"}}";
                using (WebClient wc = new HostWebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    wc.Headers["X-GVL-Token"] = hostToken;
                    wc.UploadString(hostServer + "/engine/v1/world/commands/ack", "POST", body);
                }
            }
            catch (Exception ex) { Log("bao ket qua world command loi: " + ex.Message); }
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
    void RecordNpcDialogue(string npcId, string dialogueJson)
    {
        if (string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(dialogueJson) ||
            dialogueJson.Length > 10000 || string.IsNullOrEmpty(hostToken)) return;
        Task.Run(delegate
        {
            try
            {
                JavaScriptSerializer json = new JavaScriptSerializer();
                Dictionary<string, object> dialogue = json.Deserialize<Dictionary<string, object>>(dialogueJson);
                if (dialogue == null) return;
                Dictionary<string, object> request = new Dictionary<string, object> {
                    { "request_id", Guid.NewGuid().ToString("N") },
                    { "npc_id", npcId }, { "player_id", "player.main" }, { "dialogue", dialogue }
                };
                using (WebClient wc = new HostWebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    wc.Headers["X-GVL-Token"] = hostToken;
                    wc.UploadString(hostServer + "/engine/v1/simulation/memory", "POST", json.Serialize(request));
                }
            }
            catch (Exception ex) { Log("ghi ky uc hoi thoai NPC vao life engine loi: " + ex.Message); }
        });
    }

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

// <gvl-testable:outbox>
public static class GvlSource
{
    // Nguon event rieng cho moi lan tao lai bo dem; ban cu giu nguyen "gta5.shvdn".
    public static string Id(string clockId)
    {
        return string.IsNullOrEmpty(clockId) || clockId == "legacy" ? "gta5.shvdn" : "gta5.shvdn:" + clockId;
    }
}

// Logic gui lai thuan C#, khong goi API GTA; tests/test_csharp_logic.py bien dich doan nay va chay that.
public static class GvlSendRules
{
    public const int MaxBatchEvents = 100;
    public const int MaxBatchBytes = 40000;   // host gioi han than yeu cau 65536 byte
    public const int MaxNotFoundAttempts = 12;
    public const int MaxServerErrorAttempts = 20;

    public static bool IsEnginePostPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.StartsWith("/engine/v1/world/", StringComparison.Ordinal) ||
            path == "/engine/v1/simulation/actors/observe" ||
            path == "/engine/v1/simulation/actors/work-completed";
    }

    // status = 0 khi khong co phan hoi HTTP. attempts tinh ca lan vua that bai.
    public static bool ShouldDrop(int status, int attempts)
    {
        if (status == 401 || status == 403 || status == 408 || status == 429) return false;
        if (status == 502 || status == 503 || status == 504) return false;
        if (status == 404) return attempts >= MaxNotFoundAttempts;   // co the chua dang ky xong
        if (status >= 400 && status < 500) return true;
        if (status >= 500) return attempts >= MaxServerErrorAttempts;
        return false;
    }

    public static int BatchCount(List<string> lines, int limit, int maxBytes)
    {
        int count = 0, bytes = 0;
        while (count < lines.Count && count < limit)
        {
            bytes += Encoding.UTF8.GetByteCount(lines[count] ?? "") + 96;
            if (count > 0 && bytes > maxBytes) break;
            count++;
        }
        return count;
    }
}

// Khi ca lo bi tu choi vinh vien: gui lai tung event de chi loai dung event loi.
public sealed class GvlBatchIsolation
{
    readonly bool dropServerErrors;
    int singles;
    int attempts;

    public GvlBatchIsolation(bool dropServerErrors) { this.dropServerErrors = dropServerErrors; }

    public int Limit { get { return singles > 0 ? 1 : GvlSendRules.MaxBatchEvents; } }

    public void Succeeded(int sent)
    {
        attempts = 0;
        if (singles > 0) singles = Math.Max(0, singles - sent);
    }

    // true: nguoi goi phai loai dung lo vua gui (chi xay ra khi lo co 1 event).
    public bool Failed(int status, int sent)
    {
        if (status >= 500 && !dropServerErrors) return false;
        if (status == 404 || status >= 500) attempts++;
        if (!GvlSendRules.ShouldDrop(status, attempts)) return false;
        attempts = 0;
        if (sent > 1) { singles = sent; return false; }
        if (singles > 0) singles--;
        return true;
    }
}

public sealed class GvlPost
{
    public string Path;
    public string Body;
    public string Key;
    public int Attempts;
}

// Khong tu khoa: nguoi goi giu khoa khi truy cap tu nhieu luong.
public sealed class GvlPostQueue
{
    public const int Capacity = 2000;
    const string WorkPath = "/engine/v1/simulation/actors/work-completed";
    readonly List<GvlPost> items = new List<GvlPost>();

    public int Count { get { return items.Count; } }

    // Chi bao cao ca lam khong tai tao duoc; danh muc va so lieu duoc script gui lai khi nap.
    public static bool IsDurable(string path) { return path == WorkPath; }

    public static string CoalesceKey(string path, string body)
    {
        if (path == "/engine/v1/simulation/actors/observe") return path + "|" + Field(body, "actor_id");
        if (path == "/engine/v1/world/businesses/sync") return path + "|" + Field(body, "business_id");
        if (path == "/engine/v1/world/accounts/ensure") return path + "|" + Field(body, "account_id");
        return null;
    }

    static string Field(string body, string name)
    {
        string marker = "\"" + name + "\":\"";
        int start = (body ?? "").IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "";
        start += marker.Length;
        int end = body.IndexOf('"', start);
        return end < 0 ? "" : body.Substring(start, end - start);
    }

    // Tra ve muc bi day ra khi hang day, nguoc lai null.
    public GvlPost Add(string path, string body)
    {
        GvlPost post = new GvlPost();
        post.Path = path; post.Body = body; post.Key = CoalesceKey(path, body);
        if (post.Key != null)
            for (int i = 0; i < items.Count; i++)
                if (items[i].Key == post.Key) { items[i] = post; return null; }
        items.Add(post);
        if (items.Count <= Capacity) return null;
        int victim = 0;
        for (int i = 0; i < items.Count; i++)
            if (!IsDurable(items[i].Path)) { victim = i; break; }
        GvlPost evicted = items[victim];
        items.RemoveAt(victim);
        return evicted;
    }

    public GvlPost Head() { return items.Count > 0 ? items[0] : null; }

    public void Remove(GvlPost post) { items.Remove(post); }

    // true: muc bi loai vinh vien (da go khoi hang). false: giu lai de thu sau.
    public bool Failed(GvlPost post, int status)
    {
        if (status == 404 || status >= 500) post.Attempts++;
        if (!GvlSendRules.ShouldDrop(status, post.Attempts)) return false;
        items.Remove(post);
        return true;
    }

    public string Serialize()
    {
        StringBuilder b = new StringBuilder();
        foreach (GvlPost post in items)
            if (IsDurable(post.Path)) b.Append(post.Attempts).Append('\t').Append(post.Path).Append('\t').Append(post.Body).Append('\n');
        return b.ToString();
    }

    public void Restore(string data)
    {
        foreach (string line in (data ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(new[] { '\t' }, 3);
            int attempts;
            if (parts.Length != 3 || !int.TryParse(parts[0], out attempts) || !GvlSendRules.IsEnginePostPath(parts[1])) continue;
            GvlPost post = new GvlPost();
            post.Attempts = attempts; post.Path = parts[1]; post.Body = parts[2];
            post.Key = CoalesceKey(post.Path, post.Body);
            items.Add(post);
        }
    }
}
// </gvl-testable:outbox>
