// GTA Việt Life: nói chuyện bằng giọng tiếng Việt với NPC.
// Chép file này (và GtaVietLife.ini nếu muốn đổi phím) vào thư mục "scripts" của game.
// Cần ScriptHookVDotNet (bản Enhanced nếu chạy GTA V Enhanced) và chương trình cầu nối Python đang chạy.
//
// Phím mặc định:
//   Giữ N : nói chuyện với người gần nhất (trong 5 m), thả ra để gửi.
//   K     : "ghi nhớ" người gần nhất (NPC có tên, nhớ cuộc trò chuyện lâu dài, có chấm xanh trên bản đồ).
//
// Chữ hiện trong game viết không dấu vì font GTA không có dấu tiếng Việt;
// phụ đề có dấu do cầu nối hiển thị trên màn hình.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLife : Script
{
    string server = "http://127.0.0.1:5577";
    Keys talkKey = Keys.N;
    Keys markKey = Keys.K;
    float talkRadius = 5f;

    Ped target;
    bool recording;
    bool waiting;
    Ped speakingPed;
    int speakUntil;
    int nextFacial;
    Ped releasePed;
    int releaseAt;

    readonly Dictionary<int, string> namedKeys = new Dictionary<int, string>();
    readonly Dictionary<int, Blip> namedBlips = new Dictionary<int, Blip>();
    readonly HashSet<int> followers = new HashSet<int>();
    readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

    public GtaVietLife()
    {
        // Chỉ đọc cổng từ ini (giá trị có dấu ":" trong ini bị cắt mất)
        int port = Settings.GetValue("Bridge", "Port", 5577);
        server = "http://127.0.0.1:" + port;
        talkKey = Settings.GetValue("Keys", "Talk", talkKey);
        markKey = Settings.GetValue("Keys", "Remember", markKey);
        talkRadius = Settings.GetValue("Talk", "Radius", talkRadius);

        Tick += OnTick;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Aborted += OnAborted;

        Function.Call(Hash.REQUEST_ANIM_DICT, "mp_facial");
        Task.Run(() =>
        {
            try { Get("/ping"); Notify("~g~GTA Viet Life~s~: da ket noi cau noi AI."); }
            catch (Exception ex) { Log("ping loi: " + ex); Notify("~r~GTA Viet Life~s~: chua noi duoc cau noi AI. Bam N de thu lai. (" + Short(ex) + ")"); }
        });
    }

    // ----------------------------------------------------------------- vòng lặp

    void OnTick(object sender, EventArgs e)
    {
        Action a;
        while (mainThread.TryDequeue(out a))
        {
            try { a(); } catch (Exception ex) { GTA.UI.Notification.Show("GTA Viet Life loi: " + ex.Message); }
        }

        int now = Game.GameTime;

        // Cử động miệng trong lúc NPC đang nói
        if (speakingPed != null)
        {
            if (!speakingPed.Exists() || now > speakUntil) speakingPed = null;
            else if (now > nextFacial)
            {
                Function.Call(Hash.PLAY_FACIAL_ANIM, speakingPed, "mic_chatter", "mp_facial");
                nextFacial = now + 900;
            }
        }

        // Nói xong thì để NPC đi tiếp việc của mình
        if (releasePed != null && now > releaseAt)
        {
            Unhold(releasePed);
            releasePed = null;
        }

        // Dấu hiệu đang thu âm / đang chờ
        if (recording) GTA.UI.Screen.ShowSubtitle("~b~Dang nghe...~s~ (tha phim de gui)", 100);
        else if (waiting) GTA.UI.Screen.ShowSubtitle("~y~...~s~", 100);
    }

    void OnAborted(object sender, EventArgs e)
    {
        foreach (var b in namedBlips.Values) if (b != null && b.Exists()) b.Delete();
    }

    // ----------------------------------------------------------------- phím

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == talkKey)
        {
            if (recording || waiting) return; // phím giữ sẽ lặp KeyDown
            Ped p = FindNearestPed();
            if (p == null) { Notify("Khong co ai o gan de noi chuyen."); return; }
            target = p;
            recording = true;
            Hold(p);
            Task.Run(() => { try { Post("/start", null); } catch (Exception ex) { Log("/start loi: " + ex); Notify("~r~Cau noi AI loi:~s~ " + Short(ex)); } });
        }
        else if (e.KeyCode == markKey)
        {
            Ped p = FindNearestPed();
            if (p == null) { Notify("Khong co ai o gan."); return; }
            Remember(p);
            Notify("Da ghi nho nguoi nay. Ho se nho cuoc tro chuyen voi Sep.");
        }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != talkKey || !recording) return;
        recording = false;
        Ped p = target;
        if (p == null || !p.Exists()) { Task.Run(() => { try { Post("/stop", new NameValueCollection()); } catch { } }); return; }

        waiting = true;
        NameValueCollection ctx = BuildContext(p);
        Task.Run(() =>
        {
            string resp;
            try { resp = Post("/stop", ctx); }
            catch (Exception ex)
            {
                Log("/stop loi: " + ex);
                mainThread.Enqueue(() => { waiting = false; Notify("~r~Khong goi duoc cau noi AI:~s~ " + Short(ex)); });
                return;
            }
            mainThread.Enqueue(() => HandleReply(p, resp));
        });
    }

    // ----------------------------------------------------------------- giữ / thả NPC

    // Giữ NPC đứng yên, quay mặt về người chơi, không tự nói tiếng Anh của game, không bỏ đi
    void Hold(Ped p)
    {
        Ped me = Game.Player.Character;
        Function.Call((Hash)0x9D64D7405520E3D3, p, true);   // STOP_PED_SPEAKING: NPC không tự lẩm bẩm theo lời thoại của game
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, true);
        if (followers.Contains(p.Handle)) { p.Task.LookAt(me, 15000); return; }
        p.Task.ClearAll();
        Vector3 d = me.Position - p.Position;
        p.Heading = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);   // quay mặt về người chơi ngay
        TaskSequence seq = new TaskSequence();
        seq.AddTask.StandStill(120000);
        seq.Close();
        p.Task.PerformSequence(seq);
        seq.Dispose();
        Function.Call(Hash.SET_PED_KEEP_TASK, p, true);
        p.IsPositionFrozen = true;                          // đứng im hẳn cho tới khi nói xong
        p.Task.LookAt(me, 60000);
    }

    void Unhold(Ped p)
    {
        if (p == null || !p.Exists()) return;
        Function.Call((Hash)0x9D64D7405520E3D3, p, false);
        p.IsPositionFrozen = false;
        if (followers.Contains(p.Handle)) return;
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, false);
        if (p.IsAlive) p.Task.WanderAround();
    }

    // ----------------------------------------------------------------- xử lý

    NameValueCollection BuildContext(Ped p)
    {
        Ped me = Game.Player.Character;
        Vector3 pos = p.Position;
        TimeSpan t = World.CurrentTimeOfDay;
        var c = new NameValueCollection();
        c["key"] = KeyFor(p);
        c["gender"] = p.Gender == Gender.Female ? "female" : "male";
        c["pedtype"] = Function.Call<int>(Hash.GET_PED_TYPE, p).ToString();
        c["model"] = ((PedHash)p.Model.Hash).ToString();
        c["street"] = World.GetStreetName(pos);
        c["zone"] = World.GetZoneLocalizedName(pos);
        c["hour"] = t.Hours.ToString();
        c["minute"] = t.Minutes.ToString("00");
        c["weather"] = World.Weather.ToString();
        c["in_vehicle"] = me.IsInVehicle() ? "1" : "0";
        c["wanted"] = Game.Player.WantedLevel.ToString();
        c["money"] = Game.Player.Money.ToString();
        c["following"] = followers.Contains(p.Handle) ? "1" : "0";
        c["weapon"] = me.Weapons.Current != null && me.Weapons.Current.Hash != WeaponHash.Unarmed ? "1" : "0";
        return c;
    }

    void HandleReply(Ped p, string resp)
    {
        waiting = false;
        string[] lines = (resp ?? "").Replace("\r", "").Split('\n');
        string action = lines.Length > 0 ? lines[0].Trim() : "none";
        int ms = 0;
        if (lines.Length > 1) int.TryParse(lines[1].Trim(), out ms);
        if (p == null || !p.Exists() || !p.IsAlive || ms <= 0) { ReleaseLater(p, 500); return; }

        Ped me = Game.Player.Character;
        speakingPed = p;
        speakUntil = Game.GameTime + ms + 5000;
        nextFacial = Game.GameTime + 1500;

        switch (action)
        {
            case "follow":
                Remember(p);
                if (!followers.Contains(p.Handle))
                {
                    me.PedGroup.Add(p, false);
                    p.NeverLeavesGroup = true;
                    p.Weapons.Give(WeaponHash.Pistol, 250, false, true);
                    followers.Add(p.Handle);
                    Notify("~g~" + Name(lines) + "~s~ dang di theo Sep.");
                }
                break;
            case "unfollow":
                if (followers.Remove(p.Handle))
                {
                    p.LeaveGroup();
                    Notify(Name(lines) + " da roi nhom.");
                }
                ReleaseLater(p, ms + 6000);
                break;
            case "flee":
                speakingPed = null;
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, false);
                p.Task.FleeFrom(me);
                break;
            case "fight":
                speakingPed = null;
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, false);
                if (followers.Remove(p.Handle)) p.LeaveGroup();
                p.Task.Combat(me);
                break;
            default:
                if (!followers.Contains(p.Handle)) ReleaseLater(p, ms + 7000);
                break;
        }
    }

    // ----------------------------------------------------------------- tiện ích

    Ped FindNearestPed()
    {
        Ped me = Game.Player.Character;
        Ped best = null;
        float bestD = talkRadius;
        foreach (Ped p in World.GetNearbyPeds(me, talkRadius))
        {
            if (p == null || !p.Exists() || p == me || !p.IsHuman || !p.IsAlive || p.IsPlayer) continue;
            float d = p.Position.DistanceTo(me.Position);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    string KeyFor(Ped p)
    {
        string k;
        return namedKeys.TryGetValue(p.Handle, out k) ? k : "tmp_" + p.Handle;
    }

    void Remember(Ped p)
    {
        if (namedKeys.ContainsKey(p.Handle)) return;
        namedKeys[p.Handle] = "npc_" + DateTime.Now.Ticks;
        p.IsPersistent = true;
        Blip b = p.AddBlip();
        b.Color = BlipColor.Blue;
        b.Scale = 0.7f;
        namedBlips[p.Handle] = b;
    }

    void ReleaseLater(Ped p, int ms)
    {
        if (p == null) return;
        releasePed = p;
        releaseAt = Game.GameTime + ms;
    }

    static string Name(string[] lines)
    {
        return lines.Length > 2 && lines[2].Trim().Length > 0 ? lines[2].Trim() : "NPC";
    }

    void Notify(string msg)
    {
        // Có thể bị gọi từ luồng phụ → chuyển về luồng game
        mainThread.Enqueue(() => GTA.UI.Notification.Show(msg));
    }

    static void Log(string msg)
    {
        try { System.IO.File.AppendAllText("GtaVietLife.log", DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine); }
        catch { }
    }

    static string Short(Exception ex)
    {
        string m = ex.Message ?? ex.GetType().Name;
        return m.Length > 90 ? m.Substring(0, 90) : m;
    }

    string Post(string path, NameValueCollection data)
    {
        using (var wc = new WebClient())
        {
            wc.Proxy = null; // khong di qua proxy he thong (localhost)
            wc.Encoding = Encoding.UTF8;
            byte[] r = wc.UploadValues(server + path, "POST", data ?? new NameValueCollection());
            return Encoding.UTF8.GetString(r);
        }
    }

    string Get(string path)
    {
        using (var wc = new WebClient())
        {
            wc.Proxy = null;
            wc.Encoding = Encoding.UTF8;
            return wc.DownloadString(server + path);
        }
    }
}
