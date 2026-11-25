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
    int banterEvery = 45;          // giay giua hai cuoc NPC tu noi chuyen voi nhau (0 = tat)

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
        banterEvery = Settings.GetValue("Talk", "BanterEvery", banterEvery);

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

    int nextMute;

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

        // Tắt hẳn tiếng thoại tiếng Anh có sẵn của game ở mọi NPC gần đó
        if (now > nextMute)
        {
            nextMute = now + 500;
            try
            {
                foreach (Ped np in World.GetNearbyPeds(Game.Player.Character, 30f))
                {
                    if (np == null || !np.Exists() || np == Game.Player.Character) continue;
                    Function.Call((Hash)0x9D64D7405520E3D3, np, true);               // STOP_PED_SPEAKING
                    Function.Call((Hash)0xB8BEC0CA6F0EDB0F, np);                     // STOP_CURRENT_PLAYING_AMBIENT_SPEECH
                    Function.Call((Hash)0xA9A41C1E940FB0E8, np, true);               // DISABLE_PED_PAIN_AUDIO
                }
            }
            catch { }
        }

        UpdateBanter(now);

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

    // ----------------------------------------------------------------- NPC noi chuyen voi nhau
    readonly Random rnd = new Random();
    int nextBanter = 20000;
    Ped banA, banB;
    bool banWaiting;
    int banStart, banEnd;
    readonly List<KeyValuePair<bool, int>> banPlan = new List<KeyValuePair<bool, int>>();   // (A noi?, ms)
    int banFacial;

    bool FreePed(Ped p, Ped me)
    {
        return p != null && p.Exists() && p != me && p.IsHuman && p.IsAlive && !p.IsPlayer && !p.IsInVehicle()
               && !followers.Contains(p.Handle) && p != target && p != speakingPed && p != releasePed
               && !p.IsInCombat && !p.IsRunning && !p.IsSprinting;
    }

    void UpdateBanter(int now)
    {
        if (banA != null)
        {
            bool gone = banA == null || !banA.Exists() || !banA.IsAlive || banB == null || !banB.Exists() || !banB.IsAlive;
            if (gone || recording || (!banWaiting && now > banEnd) || (banWaiting && now - banStart > 25000)) { EndBanter(); return; }
            if (banWaiting || now < banStart) return;
            // ai dang noi thi mấp máy miệng
            int t = now - banStart, acc = 0;
            for (int i = 0; i < banPlan.Count; i++)
            {
                int ms = banPlan[i].Value;
                if (t < acc + ms)
                {
                    if (now > banFacial)
                    {
                        Function.Call(Hash.PLAY_FACIAL_ANIM, banPlan[i].Key ? banA : banB, "mic_chatter", "mp_facial");
                        banFacial = now + 900;
                    }
                    break;
                }
                acc += ms + 500;
            }
            return;
        }
        if (banterEvery <= 0 || now < nextBanter || recording || waiting || speakingPed != null) return;
        nextBanter = now + banterEvery * 1000 + rnd.Next(0, 20000);
        Ped me = Game.Player.Character;
        if (me.IsInVehicle()) return;
        Ped[] near = World.GetNearbyPeds(me, 25f);
        for (int i = 0; i < near.Length; i++)
        {
            if (!FreePed(near[i], me)) continue;
            for (int j = i + 1; j < near.Length; j++)
            {
                if (!FreePed(near[j], me)) continue;
                if (near[i].Position.DistanceTo(near[j].Position) > 4.5f) continue;
                StartBanter(near[i], near[j]);
                return;
            }
        }
    }

    void Face(Ped p, Ped other)
    {
        Vector3 d = other.Position - p.Position;
        p.Heading = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);
    }

    void StartBanter(Ped a, Ped b)
    {
        banA = a; banB = b; banWaiting = true; banStart = Game.GameTime; banPlan.Clear();
        foreach (Ped p in new[] { a, b })
        {
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, true);
            p.Task.ClearAll();
        }
        // B buoc lai gan A, hai nguoi quay mat vao nhau
        Vector3 dir = b.Position - a.Position; dir.Z = 0f;
        if (dir.Length() > 1.6f) { dir.Normalize(); b.Task.GoTo(a.Position + dir * 1.3f); }
        Face(a, b); Face(b, a);
        a.Task.LookAt(b, 40000); b.Task.LookAt(a, 40000);
        NameValueCollection c = new NameValueCollection();
        c["keyA"] = KeyFor(a); c["genderA"] = a.Gender == Gender.Female ? "female" : "male"; c["pedtypeA"] = Function.Call<int>(Hash.GET_PED_TYPE, a).ToString();
        c["keyB"] = KeyFor(b); c["genderB"] = b.Gender == Gender.Female ? "female" : "male"; c["pedtypeB"] = Function.Call<int>(Hash.GET_PED_TYPE, b).ToString();
        c["hour"] = World.CurrentTimeOfDay.Hours.ToString();
        c["weather"] = World.Weather.ToString();
        c["street"] = World.GetStreetName(a.Position);
        Task.Run(() =>
        {
            string resp = "0";
            try { resp = Post("/banter", c); } catch (Exception ex) { Log("/banter loi: " + ex.Message); }
            mainThread.Enqueue(() => OnBanterReply(resp));
        });
    }

    void OnBanterReply(string resp)
    {
        if (banA == null) return;
        string[] lines = (resp ?? "0").Replace("\r", "").Split('\n');
        int n = 0;
        int.TryParse(lines[0].Trim(), out n);
        if (n <= 0) { EndBanter(); return; }
        banPlan.Clear();
        int total = 0;
        for (int i = 1; i < lines.Length && banPlan.Count < n; i++)
        {
            string[] w = lines[i].Split('|');
            int ms;
            if (w.Length < 2 || !int.TryParse(w[1].Trim(), out ms)) continue;
            banPlan.Add(new KeyValuePair<bool, int>(w[0].Trim() == "A", ms));
            total += ms + 500;
        }
        if (banA.Exists() && banB.Exists()) { Face(banA, banB); Face(banB, banA); }
        banWaiting = false;
        banStart = Game.GameTime + 1200;   // cho giong doc kip tao
        banEnd = banStart + total + 1500;
    }

    void EndBanter()
    {
        foreach (Ped p in new[] { banA, banB })
        {
            if (p == null || !p.Exists()) continue;
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, false);
            p.Task.ClearLookAt();
            if (p.IsAlive) p.Task.WanderAround();
        }
        banA = null; banB = null; banWaiting = false; banPlan.Clear();
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
