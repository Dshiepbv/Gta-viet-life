// GTA Việt Life: Đời sống 2 (sức khoẻ, căng thẳng, nhà cửa, giải trí, thế giới biến động, bản tin).
// File độc lập: chép vào "scripts" cạnh GtaVietLifeWorld.cs. Lỗi ở file này không làm hỏng các file khác.
// Nói chuyện với file Đời sống/Cá nhân qua AppDomain data "GVL.*" (cùng AppDomain của SHVDN):
//   đọc  GVL.Needs (float[3] đói/khát/năng lượng), GVL.Hygiene (float), GVL.Home (int[] {coNha, dangTrongNha}), GVL.Slept (int, tăng mỗi lần ngủ)
//   ghi  GVL.NeedsAdd (float[3], cộng dồn), GVL.HygAdd (float), GVL.RepAdd (int), GVL.LifeOn (bool), GVL.FridgeReq (World đặt true khi Sếp mở tủ lạnh)
//   công bố GVL.Life (string[] trạng thái) cho file khác muốn biết Sếp đang ốm/say/căng thẳng.
// Phím Y: menu Đời sống 2. Mọi thứ chỉ chạy khi đang chơi: tắt game thì không ốm thêm, không tăng căng thẳng, đồ ăn không hỏng.
// Chữ trong game viết không dấu (font GTA không có dấu).

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeDoiSong : Script
{
    const string SaveFile = "GtaVietLifeDoiSong.sav";
    string server = "http://127.0.0.1:5577";
    Keys menuKey = Keys.Y;   // L la menu Kinh te, J Doi song, I Ca nhan
    Keys navUp = Keys.Oemcomma, navDown = Keys.OemPeriod, navOk = Keys.Return;

    // bat/tat tung module trong GtaVietLifeDoiSong.ini, muc [Modules]
    bool mStress = true, mWeather = true, mIllness = true, mDrunk = true, mInjury = true, mFatigue = true,
         mBills = true, mFridge = true, mLeisure = true, mEvents = true, mNews = true;

    // ------------------------------------------------------------ trang thai
    long gmin;              // so phut game da troi qua LUC DANG CHOI (dong ho rieng cua file nay)
    float stress = 20f;     // 0..100
    float wet;              // 0..100 do uot (dam mua, boi)
    long coldUntil;         // dang cam den phut game nay
    long poisonUntil;       // ngo doc thuc an den phut nay
    float drunk;            // 0..100
    bool injured;           // gay chan / chan thuong nang: di tap tenh, khong chay duoc
    long awakeMin;          // so phut thuc lien tuc
    long billDueMin;        // moc tinh hoa don dien nuoc tiep theo
    int billOwed;           // tien dien nuoc dang no
    long billOverdueSince;  // no tu phut nay (0 = khong no)
    bool powerCut;
    readonly List<Food> fridge = new List<Food>();
    int lastNewsSlot = -1;

    class Food
    {
        public string Name; public float H, T, E; public long Expire;
        public Food(string n, float h, float t, float e, long exp) { Name = n; H = h; T = t; E = e; Expire = exp; }
    }

    // ------------------------------------------------------------ noi bo
    readonly Random rnd = new Random();
    int lastMinuteOfDay = -1;
    int lastHealth = -1;
    int lastSlept = -1;
    float lastHunger = -1f;
    bool eatingFromFridge;
    int nextSave, nextState, nextWarn, nextFaintCheck, nextEvent;
    bool lightsOff;
    long blackoutUntil;
    string clipSet = "";
    string tcMod = "";

    // menu
    bool menuOpen;
    string menuTitle = "";
    List<string> menuLabels = new List<string>();
    List<Action> menuActions = new List<Action>();
    Action menuBack;
    int menuSel = -1;
    readonly Queue<Action> pendingActs = new Queue<Action>();

    public GtaVietLifeDoiSong()
    {
        server = "http://127.0.0.1:" + Settings.GetValue("Bridge", "Port", 5577);
        menuKey = Settings.GetValue("Keys", "Menu", menuKey);
        navUp = Settings.GetValue("Keys", "Up", navUp);
        navDown = Settings.GetValue("Keys", "Down", navDown);
        navOk = Settings.GetValue("Keys", "Ok", navOk);
        mStress = Settings.GetValue("Modules", "Stress", true);
        mWeather = Settings.GetValue("Modules", "Weather", true);
        mIllness = Settings.GetValue("Modules", "Illness", true);
        mDrunk = Settings.GetValue("Modules", "Drunk", true);
        mInjury = Settings.GetValue("Modules", "Injury", true);
        mFatigue = Settings.GetValue("Modules", "Fatigue", true);
        mBills = Settings.GetValue("Modules", "Bills", true);
        mFridge = Settings.GetValue("Modules", "Fridge", true);
        mLeisure = Settings.GetValue("Modules", "Leisure", true);
        mEvents = Settings.GetValue("Modules", "Events", true);
        mNews = Settings.GetValue("Modules", "News", true);
        Load();
        if (billDueMin <= 0) billDueMin = gmin + 7 * 1440;
        nextEvent = Game.GameTime + 240000;
        BusSet("GVL.LifeOn", mFridge);
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
    }

    // ------------------------------------------------------------ bus chung voi cac file khac
    static object Bus(string k) { try { return AppDomain.CurrentDomain.GetData(k); } catch { return null; } }
    static void BusSet(string k, object v) { try { AppDomain.CurrentDomain.SetData(k, v); } catch { } }
    static void AddNeeds(float h, float t, float e)
    {
        float[] cur = Bus("GVL.NeedsAdd") as float[];
        if (cur == null || cur.Length < 3) cur = new float[3];
        BusSet("GVL.NeedsAdd", new float[] { cur[0] + h, cur[1] + t, cur[2] + e });
    }
    static void AddHyg(float v) { object o = Bus("GVL.HygAdd"); BusSet("GVL.HygAdd", (o is float ? (float)o : 0f) + v); }
    static void AddRep(int v) { object o = Bus("GVL.RepAdd"); BusSet("GVL.RepAdd", (o is int ? (int)o : 0) + v); }
    static float[] Needs() { return Bus("GVL.Needs") as float[]; }
    static float Hygiene() { object o = Bus("GVL.Hygiene"); return o is float ? (float)o : 80f; }
    static bool HasHome() { int[] h = Bus("GVL.Home") as int[]; return h != null && h.Length > 0 && h[0] == 1; }
    static bool InsideHome() { int[] h = Bus("GVL.Home") as int[]; return h != null && h.Length > 1 && h[1] == 1; }
    static bool OtherMenuOpen()
    {
        object a = Bus("GVL.WorldMenu"), b = Bus("GVL.PersonalMenu");
        return (a is bool && (bool)a) || (b is bool && (bool)b);
    }

    // tra tien qua cong chung GVL.Pay cua Core (Kinh te ghi de de tru tai khoan, ghi so); khong co Core thi tru tien mat
    bool Spend(int amount, string cat, string note)
    {
        bool ok;
        Func<int, string, string, bool> f = Bus("GVL.Pay") as Func<int, string, string, bool>;
        if (f != null) { try { ok = f(amount, cat, note); } catch { ok = false; } }
        else if (Game.Player.Money < amount) ok = false;
        else { Game.Player.Money = Game.Player.Money - amount; ok = true; }
        if (!ok) Notify("Khong du tien ($" + amount + ").");
        return ok;
    }

    static void Earn(int amount, string cat, string note)
    {
        Action<int, string, string> f = Bus("GVL.Earn") as Action<int, string, string>;
        if (f != null) { try { f(amount, cat, note); return; } catch { } }
        Game.Player.Money = Game.Player.Money + amount;
    }

    // bus su kien chung (docs/CORE.md): Doi song phat loai "life.*"
    static void Emit(string type, string data)
    {
        try
        {
            AppDomain d = AppDomain.CurrentDomain;
            List<string> list = d.GetData("GVL.Events") as List<string>;
            if (list == null) { list = new List<string>(); d.SetData("GVL.Events", list); }
            lock (list)
            {
                object o = d.GetData("GVL.EventSeq");
                long seq = (o is long ? (long)o : 0) + 1;
                d.SetData("GVL.EventSeq", seq);
                list.Add(seq + "\t" + type + "\t" + (data ?? ""));
            }
        }
        catch { }
    }

    static bool ModOn() { object o = Bus("GVL.Mod.Life"); return !(o is bool) || (bool)o; }

    // ------------------------------------------------------------ vong lap
    void OnTick(object sender, EventArgs e)
    {
        Ped me = Game.Player.Character;
        if (me == null || !me.Exists()) return;
        int now = Game.GameTime;
        while (pendingActs.Count > 0)
        {
            Action a = pendingActs.Dequeue();
            try { a(); } catch (Exception ex) { Log("viec cho loi: " + ex); Notify("Loi: " + ex.Message); }
        }
        if (!ModOn()) { if (menuOpen) CloseMenu(); return; }
        try
        {
            int dmin = Clock();
            if (dmin > 0) PerMinutes(me, dmin);
            WatchSleep();
            WatchEating();
            if (mInjury) WatchInjury(me);
            if (mFridge && Bus("GVL.FridgeReq") is bool && (bool)Bus("GVL.FridgeReq")) { BusSet("GVL.FridgeReq", false); EatFromFridge(-1); }
            ApplyEffects(me, now);
            if (mEvents) UpdateEvent(me, now);
            if (mNews) AutoNews();
            Publish(now);
        }
        catch (Exception ex) { Log("tick loi: " + ex.Message); }
        if (now > nextSave) { nextSave = now + 30000; Save(); }
        DrawStatus();
        if (menuOpen) DrawMenu();
    }

    void OnAborted(object sender, EventArgs e)
    {
        Save();
        BusSet("GVL.LifeOn", false);
        ClearClip(Game.Player.Character);
        SetTc("", 0f);
        SetLights(false);
        try { Function.Call((Hash)0x0EF93E9F3D08C178, true); } catch { }   // STOP_GAMEPLAY_CAM_SHAKING
        EndEvent(false);
    }

    // dong ho rieng: tra ve so phut game vua troi qua. Nhay gio (ngu, tua ca lam) toi 12 tieng van tinh.
    int Clock()
    {
        TimeSpan t = World.CurrentTimeOfDay;
        int m = t.Hours * 60 + t.Minutes;
        if (lastMinuteOfDay < 0) { lastMinuteOfDay = m; return 0; }
        int d = (m - lastMinuteOfDay + 1440) % 1440;
        lastMinuteOfDay = m;
        if (d == 0 || d > 720) return 0;
        gmin += d;
        return d;
    }

    // ------------------------------------------------------------ moi phut game
    void PerMinutes(Ped me, int d)
    {
        bool jump = d > 30;   // tua gio (ngu/lam ca/nha nghi): khong tinh mua, cang thang theo kieu dang thuc
        awakeMin += d;
        float[] nd = Needs();
        int wanted = Game.Player.WantedLevel;

        // --- cang thang
        if (mStress && !jump)
        {
            float ds = 0.012f;                                     // cuoc song von met moi (~+17/ngay)
            if (wanted > 0) ds += 0.25f * wanted;
            if (me.Health < me.MaxHealth / 2) ds += 0.08f;
            if (nd != null && (nd[0] < 20f || nd[1] < 20f)) ds += 0.05f;
            if (awakeMin > 18 * 60) ds += 0.05f;
            if (Hygiene() < 20f) ds += 0.02f;
            if (Sick()) ds += 0.03f;
            if (InsideHome() && !powerCut) ds -= 0.06f;            // o nha thu gian
            if (DayOfWeek() == 0 || DayOfWeek() == 6) ds -= 0.008f; // cuoi tuan nhe nguoi hon
            stress = Clamp(stress + ds * d);
            if (stress > 90f) AddNeeds(0f, 0f, -0.02f * d);        // qua tai: nhanh kiet suc
        }

        // --- thoi tiet: mua uot nguoi -> de cam; nang gat khat nhanh
        if (mWeather && !jump)
        {
            Weather w = World.Weather;
            bool rain = w == Weather.Raining || w == Weather.ThunderStorm;
            bool swim = false;
            try { swim = Function.Call<bool>((Hash)0x9DE327631295B4C2, me); } catch { }   // IS_PED_SWIMMING
            if ((rain && !Sheltered(me)) || swim) wet = Clamp(wet + (swim ? 6f : 1.5f) * d);
            else wet = Clamp(wet - (Sheltered(me) ? 1.2f : 0.5f) * d);
            int h = World.CurrentTimeOfDay.Hours;
            if (w == Weather.ExtraSunny && h >= 11 && h < 16 && !Sheltered(me)) AddNeeds(0f, -0.03f * d, 0f);
            if (mIllness && wet > 70f && !Sick() && rnd.NextDouble() < 0.004 * d)
            {
                coldUntil = gmin + 2 * 1440;
                Emit("life.sick", "kind=cold");
                Notify("~y~Sep bi cam~s~ vi dam mua lau: ho, sot, chay cham. Mua thuoc hoac nghi ngoi (" + MK + ").");
                PostEvent("Có người dầm mưa cả buổi giờ cảm sốt, ho sù sụ.");
            }
        }

        // --- benh: tu khoi theo thoi gian, nhanh hon neu ngu du
        if (Cold() && gmin >= coldUntil) { coldUntil = 0; Notify("~g~Het cam roi.~s~"); }
        if (Poisoned())
        {
            AddNeeds(0f, -0.04f * d, -0.03f * d);
            if (!jump && rnd.NextDouble() < 0.03 * d && me.Health > 130) me.Health = me.Health - 3;
        }
        else if (poisonUntil > 0) { poisonUntil = 0; Notify("~g~Bung da on lai.~s~"); }

        // --- say: tinh dan
        if (drunk > 0f) drunk = Math.Max(0f, drunk - 0.25f * d);

        // --- hoa don dien nuoc (moi 7 ngay game neu co nha)
        if (mBills && HasHome())
        {
            if (gmin >= billDueMin)
            {
                billDueMin = gmin + 7 * 1440;
                int bill = 90 + rnd.Next(0, 61);
                bool paid = billOwed == 0 && AutoPay(bill);
                Emit("life.bill", "amount=" + bill + ";paid=" + (paid ? 1 : 0));
                if (paid) { Notify("Tien dien nuoc + internet tuan nay: ~r~-$" + bill + "~s~ (tu tru)."); }
                else { billOwed += bill; if (billOverdueSince == 0) billOverdueSince = gmin; Notify("~r~Chua tra duoc tien dien nuoc $" + bill + "~s~. Qua 3 ngay se bi cat dien. " + MK + " > Nha cua."); }
            }
            if (billOwed > 0 && !powerCut && gmin - billOverdueSince > 3 * 1440)
            {
                powerCut = true;
                Emit("life.powercut", "owed=" + billOwed);
                Notify("~r~Nha bi cat dien!~s~ Nha toi om, tu lanh khong lanh, do an mau hong. Tra no de co dien lai.");
                PostEvent("Có nhà trong khu bị cắt điện vì nợ tiền điện, tối om om.");
            }
        }
        else if (!HasHome()) { billDueMin = gmin + 7 * 1440; }

        // --- tu lanh: do an hong theo thoi gian (cat dien thi hong nhanh gap 3)
        if (mFridge && powerCut) foreach (Food f in fridge) f.Expire -= 2 * d;
        for (int i = fridge.Count - 1; i >= 0; i--) if (gmin > fridge[i].Expire + 3 * 1440) fridge.RemoveAt(i);   // hong qua lau thi vut
    }

    // tu tru hoa don: khong bao "khong du tien" (da co thong bao rieng)
    static bool AutoPay(int amount)
    {
        Func<int, string, string, bool> f = Bus("GVL.Pay") as Func<int, string, string, bool>;
        if (f != null) { try { return f(amount, "bills", "dien nuoc tu tru"); } catch { return false; } }
        if (Game.Player.Money < amount) return false;
        Game.Player.Money = Game.Player.Money - amount;
        return true;
    }

    static bool Sheltered(Ped me)
    {
        if (me.IsInVehicle())
        {
            try { if (!Function.Call<bool>((Hash)0x94495889E22C6479, me)) return true; } catch { return true; }   // IS_PED_ON_ANY_BIKE: xe may thi van uot
            return false;
        }
        try { return Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me) != 0; } catch { return false; }
    }

    bool Cold() { return coldUntil > 0 && gmin < coldUntil; }
    bool Poisoned() { return poisonUntil > 0 && gmin < poisonUntil; }
    bool Sick() { return Cold() || Poisoned(); }
    // 0 = Chu nhat .. 6 = Thu bay. Uu tien dong ho chung cua Core (GVL.Clock[3]: 0=T2..6=CN), khong co thi hoi game.
    static int DayOfWeek()
    {
        long[] c = Bus("GVL.Clock") as long[];
        if (c != null && c.Length > 3) return (int)((c[3] + 1) % 7);
        try { return Function.Call<int>((Hash)0xD972E4BD7AEB235F); } catch { return 1; }   // GET_CLOCK_DAY_OF_WEEK
    }
    static readonly string[] DayName = { "Chu nhat", "Thu hai", "Thu ba", "Thu tu", "Thu nam", "Thu sau", "Thu bay" };
    static readonly string[] DayNameVi = { "Chủ nhật", "Thứ hai", "Thứ ba", "Thứ tư", "Thứ năm", "Thứ sáu", "Thứ bảy" };

    // ngu: file World tang GVL.Slept moi lan ngu xong (nha, duong, nha nghi)
    void WatchSleep()
    {
        object o = Bus("GVL.Slept");
        int s = o is int ? (int)o : -1;
        if (s < 0) return;
        if (lastSlept < 0) { lastSlept = s; return; }
        if (s == lastSlept) return;
        lastSlept = s;
        awakeMin = 0;
        if (mStress) stress = Clamp(stress - 30f);
        if (Cold()) coldUntil = Math.Max(gmin, coldUntil - 12 * 60);   // ngu nghi giup mau khoi
        drunk = 0f;
    }

    // an quan/via he (World "An com", tui do cua file Ca nhan): doi tang vot ma khong phai an tu lanh -> co rui ro ngo doc
    void WatchEating()
    {
        float[] nd = Needs();
        if (nd == null) return;
        float h = nd[0];
        if (lastHunger >= 0f && h - lastHunger >= 25f)
        {
            if (eatingFromFridge) eatingFromFridge = false;
            else if (mIllness && !Poisoned())
            {
                double p = Hygiene() < 30f ? 0.08 : 0.035;
                if (rnd.NextDouble() < p)
                {
                    poisonUntil = gmin + 4 * 60;
                    Emit("life.sick", "kind=poison");
                    Notify("~r~Sep bi ngo doc thuc an!~s~ Dau bung, mat nuoc, mat mo. Uong thuoc hoac vao vien.");
                    PostEvent("Có người ăn quán vỉa hè xong bị đau bụng, nằm vật ra.");
                }
            }
        }
        lastHunger = h;
    }

    // chan thuong: mat nhieu mau trong mot nhip do tai nan xe / nga cao
    void WatchInjury(Ped me)
    {
        int hp = me.Health;
        if (!me.IsAlive) { lastHealth = -1; if (injured || Sick()) { injured = false; coldUntil = 0; poisonUntil = 0; } return; }
        if (lastHealth >= 0 && lastHealth - hp >= 40 && !injured)
        {
            bool crash = me.IsInVehicle(), fall = false;
            try { fall = Function.Call<bool>((Hash)0x47E4E977581C5B55, me) || Function.Call<bool>((Hash)0xFB92A102F1C4DFA3, me); } catch { }   // IS_PED_RAGDOLL, IS_PED_FALLING
            if ((crash || fall) && rnd.NextDouble() < 0.45)
            {
                injured = true;
                Emit("life.injury", "kind=leg");
                Notify("~r~Sep bi gay chan!~s~ Di tap tenh, khong chay duoc. Vao benh vien bo bot (" + MK + " > Suc khoe).");
                PostEvent("Có vụ tai nạn, một gã bị gãy chân phải đi cà nhắc.");
            }
        }
        lastHealth = hp;
    }

    // ------------------------------------------------------------ hieu ung len nhan vat
    void ApplyEffects(Ped me, int now)
    {
        bool noSprint = injured || Cold() || Poisoned();
        if (noSprint) { try { Function.Call((Hash)0xFE99B66D079CF6BC, 0, 21, true); } catch { } }   // DISABLE_CONTROL_ACTION: Sprint

        string want = injured ? "move_m@injured" : drunk > 55f ? "move_m@drunk@verydrunk" : drunk > 25f ? "move_m@drunk@moderatedrunk" : (Cold() || Poisoned()) ? "move_m@sad@a" : "";
        if (!me.IsInVehicle()) SetClip(me, want);

        try { Function.Call((Hash)0x95D2D383D5396B8A, me, drunk > 25f); } catch { }   // SET_PED_IS_DRUNK
        bool shake = drunk > 40f || (mStress && stress > 92f);
        if (shake != shaking)
        {
            shaking = shake;
            try
            {
                if (shake) Function.Call((Hash)0xFD55E49555E017CF, "DRUNK_SHAKE", drunk > 40f ? Math.Min(2f, drunk / 40f) : 0.4f);   // SHAKE_GAMEPLAY_CAM
                else Function.Call((Hash)0x0EF93E9F3D08C178, false);
            }
            catch { }
        }

        // man hinh: say > ngo doc > thuc qua lau
        float awakeH = awakeMin / 60f;
        if (drunk > 25f) SetTc("Drunk", Math.Min(1f, drunk / 80f));
        else if (Poisoned()) SetTc("drug_wobbly", 0.6f);
        else if (mFatigue && awakeH > 20f) SetTc("hud_def_blur", Math.Min(1f, (awakeH - 20f) / 8f + 0.2f));
        else SetTc("", 0f);

        // say ma lai xe gan canh sat -> bi bat
        if (drunk > 40f && me.IsInVehicle() && Game.Player.WantedLevel == 0 && now % 5000 < 20)
        {
            bool cop = false;
            foreach (Ped p in World.GetNearbyPeds(me, 35f)) { try { if (Function.Call<int>(Hash.GET_PED_TYPE, p) == 6) { cop = true; break; } } catch { } }
            if (cop && rnd.Next(100) < 35) { Game.Player.WantedLevel = 1; Notify("~r~Canh sat phat hien Sep lai xe khi say!~s~"); AddRep(-2); }
        }

        // thuc qua 26 tieng: co the ngat xiu
        if (mFatigue && awakeH > 26f && now > nextFaintCheck && !me.IsInVehicle())
        {
            nextFaintCheck = now + 45000;
            if (rnd.Next(100) < 20)
            {
                try { Function.Call((Hash)0xAE99FB955581844A, me, 6000, 8000, 0, true, true, false); } catch { }   // SET_PED_TO_RAGDOLL
                AddNeeds(0f, 0f, 15f);
                Notify("~r~Sep thuc qua lau nen ngat xiu!~s~ Ve ngu di.");
                awakeMin -= 90;
            }
        }

        // den: cat dien (khi o trong nha) hoac mat dien khu vuc (ban dem)
        bool off = (powerCut && InsideHome()) || (blackoutUntil > 0 && gmin < blackoutUntil);
        if (blackoutUntil > 0 && gmin >= blackoutUntil) { blackoutUntil = 0; Notify("Dien da co lai."); }
        SetLights(off);

        if (now > nextWarn)
        {
            nextWarn = now + 120000;
            if (mStress && stress > 75f) Notify("~y~Sep dang cang thang (" + (int)stress + "%)~s~. Di choi, ngu, ve nha nghi ngoi (" + MK + " > Giai tri).");
            else if (mFatigue && awakeH > 20f) Notify("~y~Sep da thuc " + (int)awakeH + " tieng~s~, mat bat dau mo. Ngu di.");
        }
    }

    bool shaking;

    void SetClip(Ped me, string cs)
    {
        if (cs == clipSet) return;
        try
        {
            if (cs.Length == 0) { ClearClip(me); return; }
            Function.Call((Hash)0x6EA47DAE7FAD0EED, cs);                                 // REQUEST_ANIM_SET
            if (!Function.Call<bool>((Hash)0xC4EA073D86FB29B0, cs)) return;              // HAS_ANIM_SET_LOADED (thu lai nhip sau)
            Function.Call((Hash)0xAF8A94EDE7712BEF, me, cs, 1.0f);                       // SET_PED_MOVEMENT_CLIPSET
            clipSet = cs;
        }
        catch (Exception ex) { Log("clipset loi: " + ex.Message); clipSet = cs; }
    }

    void ClearClip(Ped me)
    {
        if (clipSet.Length == 0) return;
        clipSet = "";
        try { if (me != null && me.Exists()) Function.Call((Hash)0xAA74EC0CB0AAEA2C, me, 1.0f); } catch { }   // RESET_PED_MOVEMENT_CLIPSET
    }

    float tcStrength = -1f;
    void SetTc(string name, float strength)
    {
        if (name == tcMod && Math.Abs(strength - tcStrength) < 0.05f) return;
        try
        {
            if (name.Length == 0) Function.Call((Hash)0x0F07E7745A236711);              // CLEAR_TIMECYCLE_MODIFIER
            else
            {
                if (name != tcMod) Function.Call((Hash)0x2C933ABF17A1DF41, name);        // SET_TIMECYCLE_MODIFIER
                Function.Call((Hash)0x82E7FFCD5B2326B3, strength);                        // SET_TIMECYCLE_MODIFIER_STRENGTH
            }
        }
        catch { }
        tcMod = name; tcStrength = strength;
    }

    void SetLights(bool off)
    {
        if (off == lightsOff) return;
        lightsOff = off;
        try { Function.Call((Hash)0x1268615ACE24D504, off); } catch { }   // SET_ARTIFICIAL_LIGHTS_STATE (true = tat den)
    }

    // ------------------------------------------------------------ y te
    static readonly Vector3[] Hospitals = { new Vector3(298.0f, -584.0f, 43.3f), new Vector3(1839.0f, 3672.0f, 34.3f), new Vector3(-247.0f, 6331.0f, 32.4f), new Vector3(-449.0f, -340.0f, 34.5f) };
    int NearHospital(Ped me) { for (int i = 0; i < Hospitals.Length; i++) if (me.Position.DistanceTo(Hospitals[i]) < 60f) return i; return -1; }

    int TreatCost() { return (injured ? 400 : 0) + (Cold() ? 80 : 0) + (Poisoned() ? 120 : 0) + (stress > 80f ? 60 : 0); }

    void Hospital()
    {
        Ped me = Game.Player.Character;
        if (NearHospital(me) < 0)
        {
            int bi = 0; float bd = 1e9f;
            for (int i = 0; i < Hospitals.Length; i++) { float dd = me.Position.DistanceTo2D(Hospitals[i]); if (dd < bd) { bd = dd; bi = i; } }
            Function.Call(Hash.SET_NEW_WAYPOINT, Hospitals[bi].X, Hospitals[bi].Y);
            Notify("Da dat diem den: ~y~benh vien gan nhat~s~ (" + (int)bd + " m). Toi noi roi mo lai muc nay.");
            CloseMenu();
            return;
        }
        int cost = TreatCost();
        if (cost == 0) { Notify("Bac si kham: Sep khoe re, khong can dieu tri."); return; }
        if (!Spend(cost, "medical", "vien phi")) return;
        Emit("life.hospital", "amount=" + cost + ";injury=" + (injured ? 1 : 0));
        CloseMenu();
        Fade(injured ? 180 : 90, delegate
        {
            bool wasInj = injured;
            injured = false; coldUntil = 0; poisonUntil = 0;
            if (stress > 80f) stress = 60f;
            Game.Player.Character.Health = Game.Player.Character.MaxHealth;
            Notify("~g~Da dieu tri xong~s~ (-$" + cost + ")." + (wasInj ? " Chan da bo bot, di lai binh thuong." : ""));
        });
    }

    void BuyMedicine()
    {
        if (!Cold() && !Poisoned()) { Notify("Sep dang khoe, khong can thuoc."); return; }
        if (!Spend(35, "medical", "thuoc")) return;
        if (Cold()) coldUntil = gmin + Math.Max(60, (coldUntil - gmin) / 3);
        if (Poisoned()) poisonUntil = gmin + Math.Max(30, (poisonUntil - gmin) / 3);
        Notify("Shipper giao thuoc toi (-$35). Uong thuoc roi, se do nhanh.");
    }

    // ------------------------------------------------------------ tu lanh + di cho
    class Grocery { public string Name; public int Price; public float H, T, E; public int Days; public Grocery(string n, int p, float h, float t, float e, int d) { Name = n; Price = p; H = h; T = t; E = e; Days = d; } }
    static readonly Grocery[] Groceries = {
        new Grocery("Gao + thit (nau com)", 8, 45f, 5f, 5f, 3),
        new Grocery("Trung ga", 3, 20f, 0f, 3f, 6),
        new Grocery("Rau cu", 4, 15f, 8f, 0f, 2),
        new Grocery("Mi goi", 2, 25f, -5f, 0f, 60),
        new Grocery("Sua tuoi", 3, 5f, 20f, 3f, 4),
        new Grocery("Trai cay", 5, 10f, 15f, 2f, 4),
        new Grocery("Thung nuoc suoi", 5, 0f, 35f, 0f, 90),
        new Grocery("Ca phe goi", 3, 0f, 10f, 15f, 60) };
    const int FridgeCap = 20;
    // toa do uoc luong cac cua hang 24/7, chua kiem chung trong game
    static readonly Vector3[] Stores = { new Vector3(25.7f, -1347.3f, 29.5f), new Vector3(-48.5f, -1757.5f, 29.4f), new Vector3(1135.8f, -982.3f, 46.4f), new Vector3(373.9f, 325.9f, 103.6f), new Vector3(-707.5f, -914.3f, 19.2f), new Vector3(1961.5f, 3740.7f, 32.3f), new Vector3(1729.2f, 6414.1f, 35.0f) };
    bool NearStore(Ped me) { foreach (Vector3 s in Stores) if (me.Position.DistanceTo(s) < 30f) return true; return false; }

    void ShowGrocery()
    {
        Ped me = Game.Player.Character;
        bool store = NearStore(me);
        int fee = store ? 0 : 5;
        Begin("DI CHO  (tu lanh " + fridge.Count + "/" + FridgeCap + (store ? ", dang o cua hang" : ", dat ship +$5/mon") + ")", delegate { ShowHomeMenu(); });
        if (!store && !HasHome()) { Info("Chua co nha de cat do: thue nha truoc"); Item("Quay lai", delegate { ShowHomeMenu(); }); return; }
        foreach (Grocery g0 in Groceries)
        {
            Grocery g = g0;
            Item(g.Name + "  ($" + (g.Price + fee) + ", de duoc " + g.Days + " ngay)", delegate
            {
                if (fridge.Count >= FridgeCap) { Notify("Tu lanh day roi."); return; }
                if (!Spend(g.Price + fee, "shop", g.Name)) return;
                fridge.Add(new Food(g.Name, g.H, g.T, g.E, gmin + g.Days * 1440));
                Notify("Da mua ~g~" + g.Name + "~s~ cat vao tu lanh.");
                ShowGrocery();
            });
        }
        Item("Quay lai", delegate { ShowHomeMenu(); });
    }

    // idx = -1: tu chon mon sap het han nhat con an duoc
    void EatFromFridge(int idx)
    {
        if (fridge.Count == 0) { Notify("Tu lanh trong tron. Di cho (" + MK + " > Nha cua > Di cho)."); return; }
        if (idx < 0)
        {
            long best = long.MaxValue;
            for (int i = 0; i < fridge.Count; i++) if (fridge[i].Expire >= gmin && fridge[i].Expire < best) { best = fridge[i].Expire; idx = i; }
            if (idx < 0) { Notify("~r~Do trong tu lanh hong het roi.~s~ Vao menu " + MK + " neu van muon an (de ngo doc)."); return; }
        }
        Food f = fridge[idx];
        fridge.RemoveAt(idx);
        bool spoiled = f.Expire < gmin;
        eatingFromFridge = true;
        AddNeeds(f.H, f.T, f.E);
        Emit("life.eat", "src=fridge;food=" + f.Name + ";spoiled=" + (spoiled ? 1 : 0));
        if (spoiled && mIllness && rnd.NextDouble() < 0.6) { poisonUntil = gmin + 5 * 60; Notify("~r~An " + f.Name + " bi hong:~s~ ngo doc roi!"); }
        else Notify("Nau an o nha: ~g~" + f.Name + "~s~. Re hon an ngoai.");
    }

    void ShowFridge()
    {
        Begin("TU LANH  (" + fridge.Count + "/" + FridgeCap + (powerCut ? ", MAT DIEN" : "") + ")", delegate { ShowHomeMenu(); });
        if (fridge.Count == 0) Info("Trong tron");
        for (int i = 0; i < fridge.Count; i++)
        {
            int idx = i;
            Food f = fridge[i];
            long left = f.Expire - gmin;
            string st = left < 0 ? "DA HONG" : left < 1440 ? "con " + (left / 60) + " gio" : "con " + (left / 1440) + " ngay";
            Item(f.Name + "  (" + st + ")", delegate { if (!InsideHome() && HasHome()) { Notify("Phai o trong nha moi nau an duoc."); return; } EatFromFridge(idx); ShowFridge(); });
        }
        Item("Quay lai", delegate { ShowHomeMenu(); });
    }

    void ShowHomeMenu()
    {
        Begin("NHA CUA", delegate { ShowMain(); });
        if (!HasHome()) Info("Chua thue nha (menu Doi song cua file World)");
        if (mBills)
        {
            long left = billDueMin - gmin;
            Info("Hoa don dien nuoc tiep theo: " + (HasHome() ? "con " + Math.Max(0, left / 1440) + " ngay" : "-") + (billOwed > 0 ? "   NO $" + billOwed : "") + (powerCut ? "   DANG BI CAT DIEN" : ""));
            if (billOwed > 0) Item("Tra tien dien nuoc  ($" + billOwed + (powerCut ? ", co dien lai ngay" : "") + ")", delegate
            {
                if (!Spend(billOwed, "bills", "dien nuoc")) return;
                Emit("life.bill", "amount=" + billOwed + ";paid=1");
                Notify("Da tra $" + billOwed + " tien dien nuoc." + (powerCut ? " ~g~Co dien lai roi.~s~" : ""));
                billOwed = 0; billOverdueSince = 0; powerCut = false;
                ShowHomeMenu();
            });
        }
        if (mFridge)
        {
            Item("Tu lanh / nau an  (" + fridge.Count + " mon)", delegate { ShowFridge(); });
            Item("Di cho / sieu thi  (mua thuc pham bo tu lanh)", delegate { ShowGrocery(); });
        }
        Item("Quay lai", delegate { ShowMain(); });
    }

    // ------------------------------------------------------------ giai tri (giam cang thang)
    class Leisure
    {
        public string Name; public Vector3 Pos; public float R; public int Price, Minutes; public float Stress, Energy, Hyg, Drunk, Thirst;
        public Leisure(string n, float x, float y, float z, float r, int price, int min, float st, float en, float hy, float dr, float th)
        { Name = n; Pos = new Vector3(x, y, z); R = r; Price = price; Minutes = min; Stress = st; Energy = en; Hyg = hy; Drunk = dr; Thirst = th; }
    }
    // toa do uoc luong, chua kiem chung trong game
    static readonly Leisure[] Spots = {
        new Leisure("Nhau bia o quan bar (Bahama Mamas)", -1388f, -586f, 30.2f, 50f, 15, 60, 15f, -5f, 0f, 30f, 10f),
        new Leisure("Xem phim o rap (Vinewood)", 300f, 199f, 104.4f, 60f, 20, 120, 30f, -5f, 0f, 0f, 0f),
        new Leisure("Tap gym ngoai troi (Muscle Sands)", -1200f, -1570f, 4.6f, 60f, 10, 60, 20f, -15f, -20f, 0f, -10f),
        new Leisure("Dao bien Del Perro", -1700f, -950f, 7f, 180f, 0, 60, 20f, -3f, 0f, 0f, -5f),
        new Leisure("Ngoi cong vien Mirror Park", 1080f, -690f, 57.6f, 120f, 0, 60, 12f, 3f, 0f, 0f, 0f),
        new Leisure("Cau ca o cau tau Del Perro", -1850f, -1250f, 8.6f, 60f, 0, 120, 25f, -3f, 0f, 0f, -5f) };

    void ShowLeisure()
    {
        Ped me = Game.Player.Character;
        Begin("GIAI TRI  (cang thang " + (int)stress + "%)", delegate { ShowMain(); });
        foreach (Leisure l0 in Spots)
        {
            Leisure l = l0;
            float d = me.Position.DistanceTo2D(l.Pos);
            bool here = d < l.R;
            string sub = (l.Price > 0 ? "$" + l.Price + ", " : "mien phi, ") + (l.Minutes / 60) + " tieng, -" + (int)l.Stress + " cang thang" + (here ? ", DANG O DAY" : ", cach " + (int)d + "m");
            Item(l.Name + "  (" + sub + ")", delegate { DoLeisure(l); });
        }
        Item("Quay lai", delegate { ShowMain(); });
    }

    void DoLeisure(Leisure l)
    {
        Ped me = Game.Player.Character;
        if (me.Position.DistanceTo2D(l.Pos) > l.R)
        {
            Function.Call(Hash.SET_NEW_WAYPOINT, l.Pos.X, l.Pos.Y);
            Notify("Da dat diem den: ~y~" + l.Name + "~s~. Toi noi roi chon lai.");
            CloseMenu();
            return;
        }
        if (me.IsInVehicle()) { Notify("Xuong xe da."); return; }
        if (Game.Player.WantedLevel > 0) { Notify("Dang bi truy na, choi gi noi."); return; }
        if (l.Price > 0 && !Spend(l.Price, "leisure", l.Name)) return;
        CloseMenu();
        Fade(l.Minutes, delegate
        {
            stress = Clamp(stress - l.Stress);
            AddNeeds(0f, l.Thirst, l.Energy);
            Emit(l.Name.StartsWith("Tap gym") ? "life.gym" : "life.leisure", "minutes=" + l.Minutes + ";place=" + l.Name.Replace(";", " ").Replace("=", " "));
            if (l.Hyg != 0f) AddHyg(l.Hyg);
            if (l.Drunk > 0f && mDrunk) drunk = Math.Min(100f, drunk + l.Drunk);
            string extra = "";
            if (l.Name.StartsWith("Cau ca") && mFridge && rnd.Next(100) < 55 && fridge.Count < FridgeCap)
            {
                fridge.Add(new Food("Ca tuoi tu cau", 35f, 0f, 5f, gmin + 2 * 1440));
                extra = " Cau duoc ca, da bo tu lanh!";
            }
            Notify("~g~" + l.Name + "~s~: thu gian hon (cang thang " + (int)stress + "%)." + extra);
        });
    }

    void DrinkMore()
    {
        if (!Spend(10, "leisure", "bia")) return;
        drunk = Math.Min(100f, drunk + 25f);
        stress = Clamp(stress - 5f);
        Notify(drunk > 60f ? "~y~Say khuot roi~s~, dung lai xe!" : "Them mot ly (-$10).");
    }

    // ------------------------------------------------------------ su kien ngau nhien quanh Sep
    int evType;          // 0 khong co, 1 cuop giat, 2 nguoi bi nan, 3 chay
    Ped evA, evB;        // 1: nan nhan, ke cuop; 2: nguoi bi nan
    Blip evBlip;
    int evUntil;
    Vector3 evPos;

    void UpdateEvent(Ped me, int now)
    {
        if (evType == 0)
        {
            if (now < nextEvent) return;
            nextEvent = now + rnd.Next(360000, 720000);   // 6-12 phut thuc
            if (me.IsInVehicle() || Game.Player.WantedLevel > 0 || menuOpen) return;
            int h = World.CurrentTimeOfDay.Hours;
            int roll = rnd.Next(100);
            if (roll < 35) StartSnatch(me, now);
            else if (roll < 60) StartInjuredPed(me, now);
            else if (roll < 72) StartFire(me, now);
            else if (roll < 82 && (h >= 19 || h < 5) && blackoutUntil == 0)
            {
                blackoutUntil = gmin + 60 + rnd.Next(0, 61);
                Emit("life.event", "kind=blackout");
                Notify("~y~Mat dien ca khu!~s~ Den duong tat het.");
                PostEvent("Tối nay cả khu " + Zone(me.Position) + " mất điện, đường tối om.");
            }
            return;
        }
        if (evType == 1)
        {
            if (evB == null || !evB.Exists()) { EndEvent(false); return; }
            bool caught = !evB.IsAlive;
            if (!caught && me.Position.DistanceTo(evB.Position) < 2.2f) { try { caught = Function.Call<bool>((Hash)0x47E4E977581C5B55, evB); } catch { } }   // ke cuop nga (Sep xo/dam)
            if (caught)
            {
                Earn(50, "gift", "nan nhan cuop giat bieu");
                Emit("life.event", "kind=snatch;result=caught");
                AddRep(3); stress = Clamp(stress - 5f);
                Notify("~g~Sep da chan duoc ke cuop!~s~ Nan nhan cam on, bieu $50. Uy tin tang.");
                PostEvent("Có người dũng cảm chặn được tên cướp giật ở " + Zone(me.Position) + ", cả khu khen.");
                EndEvent(false);
                return;
            }
            if (evB.Position.DistanceTo(me.Position) > 160f || now > evUntil)
            {
                Notify("Ke cuop da tau thoat.");
                PostEvent("Lại có vụ cướp giật ở " + Zone(evPos) + ", tên cướp chạy mất.");
                EndEvent(true);
            }
            return;
        }
        if (evType == 2)
        {
            if (evA == null || !evA.Exists() || !evA.IsAlive) { EndEvent(false); return; }
            if (me.Position.DistanceTo(evA.Position) < 3f && !menuOpen) GTA.UI.Screen.ShowSubtitle("~y~" + MK + "~s~: goi cap cuu, so cuu nguoi bi nan", 100);
            try { if (!Function.Call<bool>((Hash)0x47E4E977581C5B55, evA)) Function.Call((Hash)0xAE99FB955581844A, evA, 4000, 6000, 0, true, true, false); } catch { }
            if (now > evUntil) { Notify("Nguoi bi nan da duoc nguoi khac dua di."); EndEvent(true); }
            return;
        }
        if (evType == 3 && now > evUntil) EndEvent(true);
    }

    Ped PickBystander(Ped me, float minD, float maxD)
    {
        foreach (Ped p in World.GetNearbyPeds(me, maxD))
        {
            if (p == null || !p.Exists() || p == me || !p.IsHuman || !p.IsAlive || p.IsPlayer || p.IsInVehicle() || p.IsPersistent) continue;
            float d = p.Position.DistanceTo(me.Position);
            if (d >= minD) return p;
        }
        return null;
    }

    void StartSnatch(Ped me, int now)
    {
        Ped victim = PickBystander(me, 15f, 50f);
        if (victim == null) return;
        Model m = new Model("g_m_y_mexgoon_01");
        m.Request(1000);
        if (!m.IsLoaded) return;
        Ped thief = World.CreatePed(m, victim.Position + new Vector3(1.5f, 0f, 0f));
        if (thief == null || !thief.Exists()) return;
        thief.IsPersistent = true;
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, thief, true);
        try { Function.Call((Hash)0xAE99FB955581844A, victim, 2500, 3500, 0, true, true, false); } catch { }
        thief.Task.FleeFrom(me);
        Emit("life.event", "kind=snatch");
        evType = 1; evA = victim; evB = thief; evUntil = now + 90000; evPos = victim.Position;
        evBlip = thief.AddBlip(); evBlip.Color = BlipColor.Red; evBlip.Name = "Ke cuop giat";
        Notify("~r~Cuop! Cuop!~s~ Co ke vua giat tui xach gan Sep. Duoi theo, xo nga no!");
    }

    void StartInjuredPed(Ped me, int now)
    {
        Ped p = PickBystander(me, 10f, 45f);
        if (p == null) return;
        p.IsPersistent = true;
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, true);
        try { Function.Call((Hash)0xAE99FB955581844A, p, 5000, 7000, 0, true, true, false); } catch { }
        if (p.Health > 130) p.Health = 130;
        evType = 2; evA = p; evUntil = now + 120000; evPos = p.Position;
        evBlip = p.AddBlip(); evBlip.Color = BlipColor.Yellow; evBlip.Name = "Nguoi bi nan";
        Notify("~y~Co nguoi nga quy ben duong~s~ (cham vang). Lai gan bam " + MK + " de so cuu, goi cap cuu.");
    }

    void HelpInjured()
    {
        if (evType != 2 || evA == null || !evA.Exists()) return;
        Ped p = evA;
        try { p.Task.ClearAllImmediately(); } catch { }
        p.Health = Math.Max(p.Health, 180);
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, false);
        AddRep(2); stress = Clamp(stress - 5f);
        Emit("life.event", "kind=help");
        Notify("~g~Sep da so cuu va goi cap cuu.~s~ Nguoi ta cam on rat nhieu. Uy tin tang.");
        PostEvent("Có người tốt bụng sơ cứu cho một người ngã quỵ ở " + Zone(p.Position) + ".");
        EndEvent(false);
    }

    void StartFire(Ped me, int now)
    {
        Vector3 c = Vector3.Zero;
        for (int i = 0; i < 5 && c == Vector3.Zero; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2;
            c = World.GetNextPositionOnStreet(me.Position + new Vector3((float)Math.Cos(a) * 60f, (float)Math.Sin(a) * 60f, 0f));
        }
        if (c == Vector3.Zero) return;
        try { Function.Call((Hash)0x6B83617E04503888, c.X, c.Y, c.Z, 12, false); } catch { return; }   // START_SCRIPT_FIRE
        evType = 3; evUntil = now + 60000; evPos = c;
        Emit("life.event", "kind=fire");
        Notify("~r~Chay!~s~ Co dam chay gan " + World.GetStreetName(c) + ".");
        PostEvent("Vừa có đám cháy ở " + World.GetStreetName(c) + ", khói bốc nghi ngút.");
    }

    void EndEvent(bool letGo)
    {
        try { if (evBlip != null && evBlip.Exists()) evBlip.Delete(); } catch { }
        evBlip = null;
        try
        {
            if (evB != null && evB.Exists()) { if (letGo || !evB.IsAlive) evB.MarkAsNoLongerNeeded(); else evB.Delete(); }
            if (evA != null && evA.Exists()) { Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, evA, false); evA.MarkAsNoLongerNeeded(); }
        }
        catch { }
        evA = null; evB = null; evType = 0;
    }

    static string Zone(Vector3 p) { try { return World.GetZoneLocalizedName(p); } catch { return "khu này"; } }

    // ------------------------------------------------------------ ban tin radio (cau noi AI tong hop tin don thanh ban tin co giong doc)
    void AutoNews()
    {
        int h = World.CurrentTimeOfDay.Hours;
        int slot = h == 7 ? 1 : h == 18 ? 2 : 0;
        if (slot == 0) return;
        int key = (int)(gmin / 1440) * 3 + slot;
        if (key == lastNewsSlot) return;
        lastNewsSlot = key;
        RequestNews();
    }

    void RequestNews()
    {
        NameValueCollection c = new NameValueCollection();
        c["hour"] = World.CurrentTimeOfDay.Hours.ToString();
        c["weekday"] = DayNameVi[Math.Max(0, Math.Min(6, DayOfWeek()))];
        c["weather"] = World.Weather.ToString();
        string url = server + "/news";
        Task.Run(delegate
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    byte[] r = wc.UploadValues(url, "POST", c);
                    Log("ban tin: " + Encoding.UTF8.GetString(r));
                }
            }
            catch (Exception ex) { Log("ban tin loi: " + ex.Message); }
        });
        Notify("~b~Radio:~s~ ban tin thoi su (nghe tieng va xem phu de tren man hinh).");
    }

    // ------------------------------------------------------------ cong bo trang thai
    int nextPublish;
    void Publish(int now)
    {
        if (now < nextPublish) return;
        nextPublish = now + 1000;
        BusSet("GVL.Life", new string[] { ((int)stress).ToString(), Cold() ? "1" : "0", Poisoned() ? "1" : "0", ((int)drunk).ToString(), injured ? "1" : "0", (awakeMin / 60).ToString(), powerCut ? "1" : "0" });
        BusSet("GVL.LifeOn", mFridge);
        if (now > nextState)
        {
            nextState = now + 15000;
            NameValueCollection c = new NameValueCollection();
            c["stress"] = mStress ? ((int)stress).ToString() : "";
            List<string> st = new List<string>();
            if (Cold()) st.Add("đang cảm sốt, ho sụt sịt");
            if (Poisoned()) st.Add("mặt tái mét, ôm bụng vì ngộ độc");
            if (drunk > 55f) st.Add("say khướt, nồng mùi rượu, nói lè nhè");
            else if (drunk > 25f) st.Add("ngà ngà say, có mùi bia");
            if (injured) st.Add("chân bó bột, đi cà nhắc");
            if (awakeMin > 20 * 60) st.Add("mắt thâm quầng vì thức trắng");
            if (wet > 60f) st.Add("người ướt sũng nước mưa");
            c["health_note"] = st.Count > 0 ? string.Join("; ", st.ToArray()) : "-";
            c["weekday"] = DayNameVi[Math.Max(0, Math.Min(6, DayOfWeek()))];
            Send("/state", c);
        }
    }

    void PostEvent(string text) { NameValueCollection c = new NameValueCollection(); c["text"] = text; Send("/event", c); }

    void Send(string path, NameValueCollection data)
    {
        string url = server + path;
        Task.Run(delegate
        {
            try { using (WebClient wc = new WebClient()) { wc.Proxy = null; wc.Encoding = Encoding.UTF8; wc.UploadValues(url, "POST", data); } }
            catch { }
        });
    }

    // ------------------------------------------------------------ tua gio (man hinh den)
    Action fadeDone; int fadeAt; int fadeMin;
    void Fade(int minutes, Action done)
    {
        fadeDone = done; fadeMin = minutes;
        GTA.UI.Screen.FadeOut(700);
        fadeAt = Game.GameTime + 900;
        pendingActs.Enqueue(FadeWait);
    }

    void FadeWait()
    {
        if (Game.GameTime < fadeAt) { pendingActs.Enqueue(FadeWait); return; }
        TimeSpan t = World.CurrentTimeOfDay;
        int m = (t.Hours * 60 + t.Minutes + fadeMin) % 1440;
        World.CurrentTimeOfDay = new TimeSpan(m / 60, m % 60, 0);
        Action d = fadeDone; fadeDone = null;
        try { if (d != null) d(); } finally { GTA.UI.Screen.FadeIn(700); }
    }

    // ------------------------------------------------------------ menu
    string MK { get { return menuKey.ToString(); } }

    void ShowMain()
    {
        Begin("DOI SONG 2  (" + DayName[Math.Max(0, Math.Min(6, DayOfWeek()))] + ")", null);
        Item("Suc khoe & y te...", delegate { ShowHealth(); });
        if (mFridge || mBills) Item("Nha cua: dien nuoc, tu lanh, di cho...", delegate { ShowHomeMenu(); });
        if (mLeisure) Item("Giai tri: bar, rap phim, gym, bien, cau ca...", delegate { ShowLeisure(); });
        if (mDrunk && drunk > 0f) Item("Lam them ly nua  ($10)", delegate { DrinkMore(); ShowMain(); });
        if (evType == 2 && evA != null && evA.Exists() && Game.Player.Character.Position.DistanceTo(evA.Position) < 4f) Item("So cuu nguoi bi nan, goi cap cuu", delegate { CloseMenu(); HelpInjured(); });
        if (mNews) Item("Nghe ban tin radio", delegate { CloseMenu(); RequestNews(); });
        Item("Dong", delegate { CloseMenu(); });
    }

    void ShowHealth()
    {
        Begin("SUC KHOE", delegate { ShowMain(); });
        List<string> st = new List<string>();
        if (Cold()) st.Add("cam sot (con " + (coldUntil - gmin) / 60 + "h)");
        if (Poisoned()) st.Add("ngo doc (con " + (poisonUntil - gmin) / 60 + "h)");
        if (injured) st.Add("gay chan");
        if (drunk > 0f) st.Add("say " + (int)drunk + "%");
        Info("Tinh trang: " + (st.Count > 0 ? string.Join(", ", st.ToArray()) : "khoe manh"));
        Info("Cang thang " + (int)stress + "%   Thuc lien tuc " + (awakeMin / 60) + " tieng   Do uot " + (int)wet + "%");
        Item("Vao benh vien kham/dieu tri  (" + (TreatCost() > 0 ? "$" + TreatCost() : "mien phi kham") + ", gan nhat se dan duong)", delegate { Hospital(); });
        if (Cold() || Poisoned()) Item("Dat thuoc giao tan noi  ($35, khoi nhanh gap 3)", delegate { BuyMedicine(); ShowHealth(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    void Begin(string title, Action back) { menuTitle = title; menuLabels = new List<string>(); menuActions = new List<Action>(); menuBack = back; menuSel = -1; }
    void Item(string label, Action act) { menuLabels.Add(label); menuActions.Add(act); }
    void Info(string label) { menuLabels.Add(label); menuActions.Add(null); }
    void CloseMenu() { menuOpen = false; BusSet("GVL.LifeMenu", false); }

    int NextActionable(int from, int dir)
    {
        int n = menuActions.Count;
        if (n == 0) return 0;
        for (int k = 1; k <= n; k++) { int j = ((from + dir * k) % n + n) % n; if (menuActions[j] != null) return j; }
        return from;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == menuKey)
        {
            if (!menuOpen)
            {
                // dang dung canh nguoi bi nan: phim L la so cuu luon
                if (evType == 2 && evA != null && evA.Exists() && Game.Player.Character.Position.DistanceTo(evA.Position) < 3f) { pendingActs.Enqueue(HelpInjured); return; }
                if (OtherMenuOpen()) return;
                menuOpen = true; BusSet("GVL.LifeMenu", true); ShowMain();
            }
            else if (menuBack != null) menuBack();
            else CloseMenu();
            return;
        }
        if (!menuOpen) return;
        if (e.KeyCode == Keys.Back) { if (menuBack != null) menuBack(); else CloseMenu(); return; }
        if (menuSel < 0 || menuSel >= menuActions.Count || menuActions[menuSel] == null) menuSel = NextActionable(-1, 1);
        if (e.KeyCode == navUp) { menuSel = NextActionable(menuSel, -1); return; }
        if (e.KeyCode == navDown) { menuSel = NextActionable(menuSel, 1); return; }
        if (e.KeyCode == navOk) { if (menuSel >= 0 && menuSel < menuActions.Count && menuActions[menuSel] != null) pendingActs.Enqueue(menuActions[menuSel]); return; }
        int num = -1;
        if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) num = (int)e.KeyCode - (int)Keys.D1;
        else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) num = (int)e.KeyCode - (int)Keys.NumPad1;
        if (num >= 0)
        {
            int seen = 0;
            for (int ai = 0; ai < menuActions.Count; ai++)
            {
                if (menuActions[ai] == null) continue;
                if (seen == num) { menuSel = ai; pendingActs.Enqueue(menuActions[ai]); break; }
                seen++;
            }
        }
    }

    // ------------------------------------------------------------ ve
    void DrawText(string s, float x, float y, float scale, Color c)
    {
        GTA.UI.TextElement t = new GTA.UI.TextElement(s, new PointF(x, y), scale, c);
        t.Outline = true;
        t.Draw();
    }

    // dong trang thai nho o goc phai tren: cang thang + benh + say
    void DrawStatus()
    {
        List<string> p = new List<string>();
        if (mStress) p.Add("Cang thang " + (int)stress + "%");
        if (Cold()) p.Add("Cam");
        if (Poisoned()) p.Add("Ngo doc");
        if (injured) p.Add("Gay chan");
        if (drunk > 25f) p.Add("Say");
        if (mFatigue && awakeMin > 18 * 60) p.Add("Thuc " + (awakeMin / 60) + "h");
        if (powerCut) p.Add("Cat dien");
        if (p.Count == 0) return;
        Color c = stress > 75f || injured || Poisoned() ? Color.FromArgb(255, 255, 120, 110) : Color.FromArgb(255, 230, 230, 230);
        DrawText(string.Join(" | ", p.ToArray()), 900f, 14f, 0.34f, c);
    }

    void DrawMenu()
    {
        if (menuSel < 0 || menuSel >= menuActions.Count || menuActions[menuSel] == null) menuSel = NextActionable(-1, 1);
        float x = 40f, y = 150f, w = 620f, pad = 14f;
        int total = menuLabels.Count;
        const int maxRows = 11;
        int first = 0;
        if (total > maxRows) first = Math.Max(0, Math.Min(total - maxRows, menuSel - maxRows / 2));
        int last = Math.Min(total, first + maxRows);
        float h = 52f;
        for (int i = first; i < last; i++) { string m0, d0; SplitLabel(menuLabels[i], out m0, out d0); h += d0.Length > 0 ? 46f : 30f; }
        h += 30f;
        Color accent = Color.FromArgb(255, 120, 220, 170);
        new GTA.UI.ContainerElement(new PointF(x - 2f, y - 2f), new SizeF(w + 4f, h + 4f), Color.FromArgb(230, 60, 170, 120)).Draw();
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, h), Color.FromArgb(235, 16, 18, 24)).Draw();
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, 40f), Color.FromArgb(240, 28, 44, 40)).Draw();
        DrawText(menuTitle, x + pad, y + 5f, 0.5f, accent);
        float cy = y + 48f;
        int shownNum = 0;
        for (int i = 0; i < first; i++) if (menuActions[i] != null) shownNum++;
        for (int i = first; i < last; i++)
        {
            string m, d; SplitLabel(menuLabels[i], out m, out d);
            float rh = d.Length > 0 ? 46f : 30f;
            bool act = menuActions[i] != null;
            bool sel = i == menuSel;
            if (sel) new GTA.UI.ContainerElement(new PointF(x + 6f, cy - 2f), new SizeF(w - 12f, rh - 2f), Color.FromArgb(235, 70, 190, 140)).Draw();
            Color mc = sel ? Color.FromArgb(255, 10, 20, 16) : (act ? Color.White : Color.FromArgb(255, 150, 156, 170));
            Color dc = sel ? Color.FromArgb(255, 20, 50, 36) : Color.FromArgb(255, 150, 156, 170);
            string prefix = act ? (shownNum + 1 <= 9 ? (shownNum + 1) + "  " : "    ") : "    ";
            if (act) shownNum++;
            DrawText(prefix + m, x + pad, cy, 0.4f, mc);
            if (d.Length > 0) DrawText(d, x + pad + 26f, cy + 22f, 0.31f, dc);
            cy += rh;
        }
        DrawText("dau , va dau . : chon  -  Enter hoac phim so: chay  -  Backspace: quay lai  -  " + MK + ": dong", x + pad, y + h - 26f, 0.29f, Color.FromArgb(255, 150, 156, 170));
    }

    static void SplitLabel(string label, out string main, out string detail)
    {
        int i = label.IndexOf("  (");
        if (i > 0 && label.EndsWith(")")) { main = label.Substring(0, i); detail = label.Substring(i + 3, label.Length - i - 4); }
        else { main = label; detail = ""; }
    }

    // ------------------------------------------------------------ luu / nap
    void Save()
    {
        try
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<string> l = new List<string>();
            l.Add("gmin=" + gmin);
            l.Add("stress=" + stress.ToString(ci));
            l.Add("wet=" + wet.ToString(ci));
            l.Add("cold=" + coldUntil);
            l.Add("poison=" + poisonUntil);
            l.Add("drunk=" + drunk.ToString(ci));
            l.Add("injured=" + (injured ? 1 : 0));
            l.Add("awake=" + awakeMin);
            l.Add("billdue=" + billDueMin);
            l.Add("billowed=" + billOwed);
            l.Add("billsince=" + billOverdueSince);
            l.Add("powercut=" + (powerCut ? 1 : 0));
            l.Add("news=" + lastNewsSlot);
            foreach (Food f in fridge) l.Add("food=" + f.Name.Replace(",", " ") + "," + f.H.ToString(ci) + "," + f.T.ToString(ci) + "," + f.E.ToString(ci) + "," + f.Expire);
            File.WriteAllLines(SaveFile, l.ToArray());
        }
        catch (Exception ex) { Log("save loi: " + ex.Message); }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(SaveFile)) return;
            CultureInfo ci = CultureInfo.InvariantCulture;
            foreach (string line in File.ReadAllLines(SaveFile))
            {
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string k = line.Substring(0, i), v = line.Substring(i + 1);
                try
                {
                    switch (k)
                    {
                        case "gmin": gmin = long.Parse(v); break;
                        case "stress": stress = float.Parse(v, ci); break;
                        case "wet": wet = float.Parse(v, ci); break;
                        case "cold": coldUntil = long.Parse(v); break;
                        case "poison": poisonUntil = long.Parse(v); break;
                        case "drunk": drunk = float.Parse(v, ci); break;
                        case "injured": injured = v == "1"; break;
                        case "awake": awakeMin = long.Parse(v); break;
                        case "billdue": billDueMin = long.Parse(v); break;
                        case "billowed": billOwed = int.Parse(v); break;
                        case "billsince": billOverdueSince = long.Parse(v); break;
                        case "powercut": powerCut = v == "1"; break;
                        case "news": lastNewsSlot = int.Parse(v); break;
                        case "food":
                            {
                                string[] q = v.Split(',');
                                if (q.Length >= 5) fridge.Add(new Food(q[0], float.Parse(q[1], ci), float.Parse(q[2], ci), float.Parse(q[3], ci), long.Parse(q[4])));
                                break;
                            }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex) { Log("load loi: " + ex.Message); }
    }

    static float Clamp(float v) { return v < 0f ? 0f : v > 100f ? 100f : v; }

    void Notify(string msg) { try { GTA.UI.Notification.Show(msg); } catch { } }

    static void Log(string msg)
    {
        try { File.AppendAllText("GtaVietLifeDoiSong.log", DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine); } catch { }
    }
}
