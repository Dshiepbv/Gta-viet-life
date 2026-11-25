// GTA Việt Life: lớp "Cá nhân" (hồ sơ nhân vật, kỹ năng, túi đồ, trang phục, garage xe riêng, sổ thu chi, bảng phím).
// File độc lập: chép vào thư mục "scripts" cùng GtaVietLife.cs và GtaVietLifeWorld.cs. Lỗi ở đây không làm hỏng hai file kia.
//
// Trụ cột: hồ sơ, kỹ năng, túi đồ, trang phục, garage, sổ thu chi + pháp lý (giấy tờ, phạt, tù, kiện), thể chất (cân nặng, cơ, tóc râu),
//          quan hệ (điện thoại, SMS, hẹn hò, kết hôn, quà, thú cưng, mạng xã hội). Lõi dùng chung: GtaVietLifeCore.cs, docs/CORE.md.
// Phím:   I = mở/đóng menu Cá nhân; dau , / dau . = chọn; Enter hoặc phím số = chạy; Backspace = quay lại.
//         O = ăn/uống nhanh từ túi (tự chọn món hợp với nhu cầu đang thấp nhất).
// Dữ liệu lưu trong GtaVietLifePersonal.sav (cạnh game).
// Nói chuyện với file Đời sống (đói/khát/năng lượng) qua AppDomain data "GVL.*" (cùng một AppDomain của SHVDN).
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

public class GtaVietLifePersonal : Script
{
    // ------------------------------------------------------------ cấu hình
    const string SaveFile = "GtaVietLifePersonal.sav";
    const int BagSmall = 10, BagBig = 25, BackpackPrice = 250;
    const int ShipFee = 3;
    const int CallCarFee = 50;
    const int MaxGarage = 6;

    string server = "http://127.0.0.1:5577";
    Keys menuKey = Keys.I;
    Keys quickKey = Keys.O;
    Keys navUp = Keys.Oemcomma, navDown = Keys.OemPeriod, navOk = Keys.Return;
    Keys worldMenuKey = Keys.J;
    Keys talkKey = Keys.N;

    // ------------------------------------------------------------ hồ sơ
    string playerName = "";
    string hometown = "";
    static readonly string[] Hometowns = { "Ha Noi", "Sai Gon", "Hai Phong", "Da Nang", "Hue", "Nghe An", "Can Tho", "Mien Tay", "Tay Nguyen", "Viet kieu" };
    static readonly string[] HometownVi = { "Hà Nội", "Sài Gòn", "Hải Phòng", "Đà Nẵng", "Huế", "Nghệ An", "Cần Thơ", "miền Tây", "Tây Nguyên", "Việt kiều" };

    // ------------------------------------------------------------ kỹ năng (0..100)
    const int SkStam = 0, SkDrive = 1, SkShoot = 2, SkStrength = 3, SkTalk = 4;
    static readonly string[] SkillName = { "The luc", "Lai xe", "Ban sung", "Suc manh", "Giao tiep" };
    static readonly string[] SkillVi = { "thể lực", "lái xe", "bắn súng", "sức mạnh", "giao tiếp" };
    static readonly string[] SkillHow = {
        "chay, boi, dap xe. Cao: doi/khat/met cham hon (toi -30%)",
        "lai xe tren 35 km/h khong dam. Cao: xe de lai hon (chi so game)",
        "ban sung. Cao: ngam vung, it giat (chi so game)",
        "danh tay khong. Cao: dam manh hon (chi so game)",
        "giu N noi chuyen voi nguoi gan. Cao: mua do re hon (toi -15%), NPC ne hon"
    };
    static readonly string[] StatSuffix = { "_STAMINA", "_WHEELIE_ABILITY", "_SHOOTING_ABILITY", "_STRENGTH", null };
    readonly float[] skill = new float[5];
    readonly int[] skillShown = new int[5];   // moc 10 diem da bao

    // ------------------------------------------------------------ túi đồ
    class ItemDef
    {
        public string Id, Name; public int Price; public float Hunger, Thirst, Energy; public int Health;
        public ItemDef(string id, string n, int p, float h, float t, float e, int hp) { Id = id; Name = n; Price = p; Hunger = h; Thirst = t; Energy = e; Health = hp; }
    }
    static readonly ItemDef[] Items = {
        new ItemDef("banhmi", "Banh mi", 6, 25f, 0f, 0f, 5),
        new ItemDef("comhop", "Com hop", 12, 45f, 5f, 5f, 10),
        new ItemDef("pho", "Pho bo (hop giu nhiet)", 15, 50f, 15f, 5f, 10),
        new ItemDef("nuocsuoi", "Nuoc suoi", 3, 0f, 35f, 0f, 0),
        new ItemDef("trada", "Tra da", 2, 0f, 25f, 3f, 0),
        new ItemDef("caphe", "Ca phe sua da", 5, 0f, 10f, 18f, 0),
        new ItemDef("tangluc", "Nuoc tang luc", 8, 0f, 5f, 30f, 0),
        new ItemDef("bangcuu", "Bang cuu thuong", 25, 0f, 0f, 0f, 40),
        new ItemDef("qua_hoa", "Bo hoa (qua tang)", 20, 0f, 0f, 0f, 0),
        new ItemDef("qua_socola", "Hop socola (qua tang)", 15, 0f, 0f, 0f, 0),
        new ItemDef("qua_nuochoa", "Nuoc hoa (qua tang)", 120, 0f, 0f, 0f, 0)
    };
    readonly Dictionary<string, int> bag = new Dictionary<string, int>();
    bool hasBackpack;
    int BagCap { get { return hasBackpack ? BagBig : BagSmall; } }

    // ------------------------------------------------------------ trang phục
    static readonly string[] OutfitSlotName = { "Di lam", "Di choi", "The thao", "Lich su" };
    static readonly string[] OutfitSlotVi = { "đồ đi làm", "đồ đi chơi", "đồ thể thao", "đồ lịch sự" };
    static readonly int[] PropIds = { 0, 1, 2, 6, 7 };
    readonly string[] outfit = new string[4];   // "model;c0d,c0t,c0p;...;p0d,p0t;..."
    int wearing = -1;

    // ------------------------------------------------------------ garage xe riêng
    class CarRec { public int Hash; public int Prim, Sec; public string Plate = ""; public string Label = ""; public bool Impounded, InEconPound; }
    readonly List<CarRec> garage = new List<CarRec>();
    Vehicle calledCar;
    int calledIdx = -1;

    // ------------------------------------------------------------ sổ thu chi
    int lastMoney = int.MinValue;
    int lastModel;
    int inToday, outToday;
    readonly List<int[]> moneyHist = new List<int[]>();   // [ngay, thu, chi]
    int dayNo;
    int lastHour = -1;

    // ------------------------------------------------------------ nội bộ
    readonly Queue<Action> pending = new Queue<Action>();
    int nextSkillTick, nextStatSync, nextSave, nextStatePost;
    int lastShotGain, nextPublish;
    int talkDownAt;
    float lastSpeed;

    // menu
    bool menuOpen;
    string menuTitle = "";
    List<string> menuLabels = new List<string>();
    List<Action> menuActions = new List<Action>();
    Action menuBack;
    int menuSel = -1;

    public GtaVietLifePersonal()
    {
        int port = Settings.GetValue("Bridge", "Port", 5577);
        server = "http://127.0.0.1:" + port;
        menuKey = Settings.GetValue("Keys", "Personal", menuKey);
        quickKey = Settings.GetValue("Keys", "QuickEat", quickKey);
        navUp = Settings.GetValue("Keys", "Up", navUp);
        navDown = Settings.GetValue("Keys", "Down", navDown);
        navOk = Settings.GetValue("Keys", "Ok", navOk);
        worldMenuKey = Settings.GetValue("Keys", "WorldMenu", worldMenuKey);
        talkKey = Settings.GetValue("Keys", "Talk", talkKey);

        payKey = Settings.GetValue("Keys", "PayCompensation", payKey);
        autoKeep = Settings.GetValue("Look", "AutoKeep", true);
        ResetPreset();   // gia tri mac dinh; Load() ghi de neu da luu
        Load();
        MakeHallBlip();
        PublishPersonal();
        Tick += OnTick;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Aborted += OnAborted;
        Notify("~g~GTA Viet Life: Ca nhan~s~ da bat. Bam ~y~" + menuKey + "~s~ mo menu, ~y~" + quickKey + "~s~ an/uong nhanh tu tui.");
    }

    // ------------------------------------------------------------ cầu nối giữa các script (cùng AppDomain)
    static object Bus(string k) { try { return AppDomain.CurrentDomain.GetData(k); } catch { return null; } }
    static void BusSet(string k, object v) { try { AppDomain.CurrentDomain.SetData(k, v); } catch { } }

    // cong/tru do/khat/nang luong: file Doi song doc "GVL.NeedsAdd" moi tick roi xoa
    static void AddNeeds(float h, float t, float e)
    {
        float[] cur = Bus("GVL.NeedsAdd") as float[];
        if (cur == null || cur.Length < 3) cur = new float[3];
        BusSet("GVL.NeedsAdd", new float[] { cur[0] + h, cur[1] + t, cur[2] + e });
    }

    // [doi, khat, nang luong] do file Doi song cong bo; null neu file do chua chay
    static float[] Needs() { return Bus("GVL.Needs") as float[]; }

    bool EconMenuOpen() { object o = Bus("GVL.EconMenu"); return o is bool && (bool)o; }
    bool WorldMenuOpen() { object o = Bus("GVL.WorldMenu"); return o is bool && (bool)o; }

    // ------------------------------------------------------------ vòng lặp
    void OnTick(object sender, EventArgs e)
    {
        Ped me = Game.Player.Character;
        if (me == null || !me.Exists()) return;
        int now = Game.GameTime;

        while (pending.Count > 0)
        {
            Action a = pending.Dequeue();
            try { a(); } catch (Exception ex) { Log("viec cho loi: " + ex); Notify("Loi: " + Short(ex)); }
        }

        // the luc cao => nhu cau giam cham hon (file Doi song nhan he so nay)
        Action na;
        while (fromNet.TryDequeue(out na)) { try { na(); } catch (Exception ex) { Log("mang loi: " + ex.Message); } }
        if (pendingDelete != null && now > pendingDeleteAt) { if (pendingDelete.Exists() && !me.IsInVehicle()) pendingDelete.Delete(); pendingDelete = null; }
        PollBus();
        try { UpdateLegal(me, now); } catch (Exception ex) { Log("phap ly loi: " + ex.Message); }
        try { KeepLook(me, now); } catch (Exception ex) { Log("giu nhan vat loi: " + ex.Message); }
        try { UpdateBody(me, now); } catch (Exception ex) { Log("the chat loi: " + ex.Message); }
        try { UpdatePet(me, now); } catch (Exception ex) { Log("thu cung loi: " + ex.Message); }
        if (now > nextPublish) { nextPublish = now + 5000; PublishPersonal(); }

        BusSet("GVL.DrainMul", (1f - 0.30f * skill[SkStam] / 100f) * (ModOn("Body") ? WeightDrain() : 1f));
        BusSet("GVL.PersonalMenu", menuOpen);
        if (menuOpen && (WorldMenuOpen() || EconMenuOpen())) menuOpen = false;

        TrackMoney(me, now);
        if (now > nextSkillTick) { nextSkillTick = now + 1000; TrainSkills(me, now); }
        else TrackShots(me, now);
        if (now > nextStatSync) { nextStatSync = now + 20000; SyncGameStats(me); }
        if (now > nextStatePost) { nextStatePost = now + 20000; PostState(); }
        if (now > nextSave) { nextSave = now + 60000; Save(); }
        UpdateCalledCar(me);

        if (menuOpen) DrawMenu();
    }

    void OnAborted(object sender, EventArgs e)
    {
        Save();
        BusSet("GVL.PersonalMenu", false);
        BusSet("GVL.DrainMul", 1f);
        BusSet("GVL.PhoneCall", "");
        if (petPed != null && petPed.Exists()) petPed.Delete();
        if (hallBlip != null && hallBlip.Exists()) hallBlip.Delete();
        if (calledCar != null && calledCar.Exists())
        {
            if (calledCar.AttachedBlip != null && calledCar.AttachedBlip.Exists()) calledCar.AttachedBlip.Delete();
            calledCar.IsPersistent = false;
        }
    }

    // ------------------------------------------------------------ kỹ năng
    void Gain(int sk, float amount)
    {
        float v = skill[sk];
        if (v >= 100f) return;
        amount *= Math.Max(0.15f, 1.2f - v / 100f);   // cang cao cang kho len
        skill[sk] = Math.Min(100f, v + amount);
        int lv = (int)(skill[sk] / 10f);
        if (lv > skillShown[sk])
        {
            skillShown[sk] = lv;
            Notify("~b~Ky nang " + SkillName[sk] + " len " + (lv * 10) + "/100~s~");
            nextStatSync = 0;
        }
    }

    void TrainSkills(Ped me, int now)
    {
        if (!me.IsAlive) return;
        if (me.IsInVehicle())
        {
            Vehicle v = me.CurrentVehicle;
            if (v != null && v.Exists() && v.Driver == me)
            {
                float sp = v.Speed;
                bool bike = Function.Call<bool>(Hash.IS_THIS_MODEL_A_BICYCLE, v.Model.Hash);
                bool crashed = lastSpeed - sp > 7f;   // giam toc dot ngot = dam
                if (bike && sp > 3f) Gain(SkStam, 0.03f);
                else if (!crashed && sp > 10f) Gain(SkDrive, 0.012f * Math.Min(3f, sp / 10f));
                lastSpeed = sp;
            }
            return;
        }
        lastSpeed = 0f;
        if (me.IsSwimming) Gain(SkStam, 0.04f);
        else if (me.IsSprinting) Gain(SkStam, 0.03f);
        else if (me.IsRunning) Gain(SkStam, 0.015f);
        if (me.IsInMeleeCombat) Gain(SkStrength, 0.06f);
    }

    void TrackShots(Ped me, int now)
    {
        if (now - lastShotGain < 200) return;
        if (me.IsShooting) { lastShotGain = now; Gain(SkShoot, 0.05f); }
    }

    // ghi ky nang vao chi so goc cua game (chi nang len, khong ha chi so game da co)
    void SyncGameStats(Ped me)
    {
        string pre = CharPrefix(me);
        if (pre == null) return;
        for (int i = 0; i < StatSuffix.Length; i++)
        {
            if (StatSuffix[i] == null) continue;
            try
            {
                int h = Game.GenerateHash(pre + StatSuffix[i]);
                OutputArgument o = new OutputArgument();
                Function.Call<bool>(Hash.STAT_GET_INT, h, o, -1);
                int cur = o.GetResult<int>();
                int want = (int)skill[i];
                if (want > cur) Function.Call(Hash.STAT_SET_INT, h, want, true);
            }
            catch (Exception ex) { Log("stat loi: " + ex.Message); return; }
        }
    }

    static string CharPrefix(Ped me)
    {
        int m = me.Model.Hash;
        if (m == Game.GenerateHash("player_zero")) return "SP0";
        if (m == Game.GenerateHash("player_one")) return "SP1";
        if (m == Game.GenerateHash("player_two")) return "SP2";
        return null;
    }

    float Discount { get { return 0.15f * skill[SkTalk] / 100f; } }

    // ------------------------------------------------------------ sổ thu chi (theo doi tien cua nhan vat dang choi)
    void TrackMoney(Ped me, int now)
    {
        int money = Game.Player.Money;
        int model = me.Model.Hash;
        if (lastMoney == int.MinValue || model != lastModel) { lastMoney = money; lastModel = model; return; }
        int d = money - lastMoney;
        lastMoney = money;
        if (d > 0) inToday += d; else if (d < 0) outToday -= d;
        int hour = World.CurrentTimeOfDay.Hours;
        if (lastHour >= 0 && hour < lastHour && lastHour - hour > 12)
        {
            dayNo++;
            moneyHist.Add(new int[] { dayNo, inToday, outToday });
            while (moneyHist.Count > 7) moneyHist.RemoveAt(0);
            inToday = 0; outToday = 0;
            if (Bus("GVL.Clock") == null) OnNewDay();   // khong co Core: tu sang ngay
        }
        lastHour = hour;
    }

    // ------------------------------------------------------------ túi đồ
    int BagCount() { int n = 0; foreach (int c in bag.Values) n += c; return n; }
    int Have(string id) { int c; return bag.TryGetValue(id, out c) ? c : 0; }
    static ItemDef Def(string id) { foreach (ItemDef d in Items) if (d.Id == id) return d; return null; }
    int PriceOf(ItemDef d) { return Math.Max(1, (int)Math.Round(d.Price * (1f - Discount))); }

    void Buy(ItemDef d, int qty)
    {
        int room = BagCap - BagCount();
        if (room <= 0) { Notify("Tui da day (" + BagCap + " mon)." + (hasBackpack ? "" : " Mua ba lo de dung " + BagBig + " mon.")); return; }
        qty = Math.Min(qty, room);
        int cost = PriceOf(d) * qty + ShipFee;
        if (!Pay(cost, "shop", d.Name)) { Notify("Khong du tien ($" + cost + ")."); return; }
        bag[d.Id] = Have(d.Id) + qty;
        Notify("Da mua " + qty + " " + d.Name + " (-$" + cost + ", gom ship $" + ShipFee + "). Tui: " + BagCount() + "/" + BagCap);
        Save();
    }

    void Use(ItemDef d)
    {
        if (Have(d.Id) <= 0) { Notify("Het " + d.Name + "."); return; }
        if (d.Id.StartsWith("qua_")) { Notify("Day la qua tang: Dien thoai > Danh ba > Tang qua."); return; }
        Ped me = Game.Player.Character;
        if (d.Health > 0 && d.Hunger == 0f && d.Thirst == 0f && me.Health >= me.MaxHealth) { Notify("Mau dang day, chua can bang."); return; }
        bag[d.Id] = Have(d.Id) - 1;
        if (bag[d.Id] <= 0) bag.Remove(d.Id);
        if (d.Hunger != 0f || d.Thirst != 0f || d.Energy != 0f) AddNeeds(d.Hunger, d.Thirst, d.Energy);
        if (d.Health > 0) me.Health = Math.Min(me.MaxHealth, me.Health + d.Health);
        float fat; if (FatKg.TryGetValue(d.Id, out fat)) kg += fat;
        if (!me.IsInVehicle())
        {
            bool drink = d.Thirst > d.Hunger;
            try { me.Task.PlayAnimation(drink ? "mp_player_intdrink" : "mp_player_inteat@burger", drink ? "loop_bottle" : "mp_player_int_eat_burger", 8f, 2500, AnimationFlags.UpperBodyOnly | AnimationFlags.AllowRotation); } catch { }
        }
        string fx = "";
        if (d.Hunger > 0) fx += " an +" + (int)d.Hunger;
        if (d.Thirst > 0) fx += " uong +" + (int)d.Thirst;
        if (d.Energy > 0) fx += " nang luong +" + (int)d.Energy;
        if (d.Health > 0) fx += " mau +" + d.Health;
        Notify("Da dung " + d.Name + ":" + fx + (Needs() == null && d.Health == 0 ? " ~y~(chua thay file Doi song)~s~" : ""));
        Save();
    }

    // O: chon mon hop nhat voi nhu cau dang thap nhat
    void QuickEat()
    {
        float[] n = Needs();
        Ped me = Game.Player.Character;
        float h = n != null ? n[0] : 50f, t = n != null ? n[1] : 50f, en = n != null ? n[2] : 50f;
        float hpPct = 100f * me.Health / Math.Max(1, me.MaxHealth);
        ItemDef best = null; float bestScore = 0f;
        foreach (ItemDef d in Items)
        {
            if (Have(d.Id) <= 0) continue;
            float s = d.Hunger * (100f - h) + d.Thirst * (100f - t) + d.Energy * (100f - en) + d.Health * (hpPct < 60f ? 100f - hpPct : 0f);
            if (s > bestScore) { bestScore = s; best = d; }
        }
        if (best == null) { Notify(BagCount() == 0 ? "Tui trong. Bam " + menuKey + " > Tui do de mua." : "Chua can dung gi luc nay."); return; }
        Use(best);
    }

    // ------------------------------------------------------------ trang phục
    string Capture(Ped me)
    {
        StringBuilder b = new StringBuilder();
        b.Append(me.Model.Hash);
        for (int c = 0; c < 12; c++)
        {
            int dr = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, me, c);
            int tx = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, me, c);
            int pl = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, me, c);
            b.Append(";" + dr + "," + tx + "," + pl);
        }
        foreach (int p in PropIds)
        {
            int dr = Function.Call<int>(Hash.GET_PED_PROP_INDEX, me, p, 1);
            int tx = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, me, p);
            b.Append(";" + dr + "," + tx);
        }
        return b.ToString();
    }

    bool Apply(Ped me, string s)
    {
        string[] q = s.Split(';');
        if (q.Length < 13 + PropIds.Length) return false;
        if (int.Parse(q[0]) != me.Model.Hash) return false;
        for (int c = 0; c < 12; c++)
        {
            string[] v = q[1 + c].Split(',');
            Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me, c, int.Parse(v[0]), int.Parse(v[1]), int.Parse(v[2]));
        }
        for (int i = 0; i < PropIds.Length; i++)
        {
            string[] v = q[13 + i].Split(',');
            int dr = int.Parse(v[0]);
            if (dr < 0) Function.Call(Hash.CLEAR_PED_PROP, me, PropIds[i], 0);
            else Function.Call(Hash.SET_PED_PROP_INDEX, me, PropIds[i], dr, int.Parse(v[1]), true, 0);
        }
        return true;
    }

    void SaveOutfit(int slot)
    {
        outfit[slot] = Capture(Game.Player.Character);
        wearing = slot;
        Notify("Da luu bo do ~g~" + OutfitSlotName[slot] + "~s~ (dang mac). Doi do o tiem quan ao cua game roi luu vao o khac.");
        Save();
    }

    void WearOutfit(int slot)
    {
        if (string.IsNullOrEmpty(outfit[slot])) { Notify("O " + OutfitSlotName[slot] + " chua co do."); return; }
        Ped me = Game.Player.Character;
        if (me.IsInVehicle()) { Notify("Xuong xe roi moi thay do."); return; }
        bool ok = false;
        try { ok = Apply(me, outfit[slot]); } catch (Exception ex) { Log("mac do loi: " + ex.Message); }
        if (!ok) { Notify("Bo do nay luu cho nhan vat khac."); return; }
        wearing = slot;
        Notify("Da mac ~g~" + OutfitSlotName[slot] + "~s~.");
        nextStatePost = 0;
        Save();
    }

    // ------------------------------------------------------------ garage xe riêng
    void AddCurrentCar()
    {
        Ped me = Game.Player.Character;
        Vehicle v = me.IsInVehicle() ? me.CurrentVehicle : null;
        if (v == null || !v.Exists()) { Notify("Ngoi tren xe can ghi roi chon lai."); return; }
        int h = v.Model.Hash;
        foreach (CarRec c in garage) if (c.Hash == h && c.Plate == SafePlate(v)) { Notify("Xe nay da co trong garage."); return; }
        if (garage.Count >= MaxGarage) { Notify("Garage day (" + MaxGarage + " xe). Xoa bot truoc."); return; }
        CarRec r = new CarRec();
        r.Hash = h;
        r.Prim = (int)v.Mods.PrimaryColor; r.Sec = (int)v.Mods.SecondaryColor;
        r.Plate = SafePlate(v);
        r.Label = CarLabel(v);
        garage.Add(r);
        if (calledCar != null && calledCar.Exists() && calledCar != v) ReleaseCalled();
        calledCar = v; calledIdx = garage.Count - 1;
        v.IsPersistent = true;
        MarkCar(v);
        Notify("Da ghi ~g~" + r.Label + "~s~ (bien " + r.Plate + ") vao garage. Goi xe toi bat cu dau: $" + CallCarFee + ".");
        Save();
    }

    static string SafePlate(Vehicle v) { try { return (v.Mods.LicensePlate ?? "").Trim(); } catch { return ""; } }

    static string CarLabel(Vehicle v)
    {
        try
        {
            string code = Function.Call<string>(Hash.GET_DISPLAY_NAME_FROM_VEHICLE_MODEL, v.Model.Hash);
            string lbl = Game.GetLocalizedString(code);
            if (!string.IsNullOrEmpty(lbl) && lbl != "NULL") return lbl;
            return code;
        }
        catch { return "Xe"; }
    }

    void MarkCar(Vehicle v)
    {
        try
        {
            if (v.AttachedBlip != null && v.AttachedBlip.Exists()) v.AttachedBlip.Delete();
            Blip b = v.AddBlip();
            b.Sprite = Function.Call<bool>(Hash.IS_THIS_MODEL_A_BIKE, v.Model.Hash) ? BlipSprite.PersonalVehicleBike : BlipSprite.PersonalVehicleCar;
            b.Color = BlipColor.Blue;
            b.Name = "Xe cua Sep";
            b.IsShortRange = true;
        }
        catch (Exception ex) { Log("blip xe loi: " + ex.Message); }
    }

    void ReleaseCalled()
    {
        if (calledCar == null || !calledCar.Exists()) { calledCar = null; return; }
        if (calledCar.AttachedBlip != null && calledCar.AttachedBlip.Exists()) calledCar.AttachedBlip.Delete();
        calledCar.IsPersistent = false;
        calledCar = null;
        calledIdx = -1;
    }

    // chay trong vong lap chinh (CreateVehicle khong dung duoc trong su kien phim)
    void CallCar(int idx)
    {
        if (idx < 0 || idx >= garage.Count) return;
        Ped me = Game.Player.Character;
        if (me.IsInVehicle()) { Notify("Sep dang o tren xe roi."); return; }
        CarRec r = garage[idx];
        if (r.Impounded) { Notify(r.InEconPound ? "Xe dang o bai giu xe Davis. Lay o menu Kinh te (phim L)." : "Xe dang bi giu o bai. Lay ra o menu Giay to (-$200)."); return; }
        if (jailUntil > 0) { Notify("Dang o tu."); return; }
        if (calledCar != null && calledCar.Exists() && calledIdx == idx && calledCar.IsAlive && calledCar.Position.DistanceTo(me.Position) < 150f)
        { Notify("Xe dang o gan day (cham xanh)."); return; }
        if (Game.Player.Money < CallCarFee && !(Bus("GVL.Bank") is int && (int)Bus("GVL.Bank") + Game.Player.Money >= CallCarFee)) { Notify("Khong du $" + CallCarFee + " phi giao xe."); return; }
        Model m = new Model(r.Hash);
        if (!m.IsInCdImage || !m.IsVehicle) { Notify("Model xe khong con trong game."); return; }
        if (!m.Request(3000)) { Notify("Chua nap duoc xe, thu lai."); return; }
        Vector3 fwd = me.ForwardVector;
        Vector3 pos = World.GetNextPositionOnStreet(me.Position + fwd * 25f);
        if (pos.DistanceTo(me.Position) > 80f) pos = me.Position + fwd * 6f;
        if (calledCar != null && calledCar.Exists() && calledCar != me.CurrentVehicle) { if (calledCar.AttachedBlip != null && calledCar.AttachedBlip.Exists()) calledCar.AttachedBlip.Delete(); calledCar.Delete(); }
        Vehicle v = World.CreateVehicle(m, pos, me.Heading);
        m.MarkAsNoLongerNeeded();
        if (v == null || !v.Exists()) { Notify("Khong tao duoc xe o day, ra cho rong hon."); return; }
        if (!Pay(CallCarFee, "car", "giao xe rieng")) { v.Delete(); Notify("Khong du $" + CallCarFee + " phi giao xe."); return; }
        try
        {
            v.Mods.PrimaryColor = (VehicleColor)r.Prim; v.Mods.SecondaryColor = (VehicleColor)r.Sec;
            if (r.Plate.Length > 0) v.Mods.LicensePlate = r.Plate;
        }
        catch { }
        Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v, 5f);
        v.IsPersistent = true;
        calledCar = v; calledIdx = idx;
        MarkCar(v);
        Notify("~g~" + r.Label + "~s~ da duoc giao toi (-$" + CallCarFee + "). Cham xanh tren ban do.");
    }

    void UpdateCalledCar(Ped me)
    {
        if (calledCar == null) return;
        if (!calledCar.Exists()) { calledCar = null; calledIdx = -1; return; }
        if (!calledCar.IsAlive) { Notify("~r~Xe cua Sep bi hong.~s~ Goi lai tu garage (" + menuKey + ")."); ReleaseCalled(); }
    }

    // ------------------------------------------------------------ cầu nối AI: cho NPC biết Sếp là ai
    void PostState()
    {
        NameValueCollection c = new NameValueCollection();
        if (playerName.Length > 0) c["player_name"] = playerName;
        int hi = Array.IndexOf(Hometowns, hometown);
        if (hi >= 0) c["hometown"] = HometownVi[hi];
        if (wearing >= 0) c["outfit"] = OutfitSlotVi[wearing];
        List<string> good = new List<string>();
        for (int i = 0; i < skill.Length; i++) if (skill[i] >= 40f) good.Add(SkillVi[i] + (skill[i] >= 75f ? " rất giỏi" : " khá"));
        if (good.Count > 0) c["skills"] = string.Join(", ", good.ToArray());
        if (ModOn("Body")) { string look = LookWords(); if (look.Length > 0) c["look"] = look; }
        if (ModOn("Legal")) { c["record"] = record.ToString(); c["jailed"] = jailUntil > 0 ? "1" : "0"; }
        if (spouse.Length > 0) c["spouse"] = spouse;
        if (c.Count == 0) return;
        Task.Run(delegate
        {
            try { using (WebClient wc = new WebClient()) { wc.Proxy = null; wc.Encoding = Encoding.UTF8; wc.UploadValues(server + "/state", "POST", c); } }
            catch { }
        });
    }

    // ============================================================ LÕI: bus su kien, tra tien (xem docs/CORE.md)
    static bool ModOn(string name) { object o = Bus("GVL.Mod." + name); return !(o is bool) || (bool)o; }

    static void Emit(string type, string data)
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

    static List<string[]> Poll(ref long last)
    {
        List<string[]> res = new List<string[]>();
        List<string> list = AppDomain.CurrentDomain.GetData("GVL.Events") as List<string>;
        if (list == null) return res;
        lock (list)
            foreach (string line in list)
            {
                string[] p = line.Split('\t');
                long seq;
                if (p.Length == 3 && long.TryParse(p[0], out seq) && seq > last) { res.Add(p); last = seq; }
            }
        return res;
    }

    static string Field(string data, string key)
    {
        foreach (string kv in (data ?? "").Split(';')) { int i = kv.IndexOf('='); if (i > 0 && kv.Substring(0, i) == key) return kv.Substring(i + 1); }
        return "";
    }

    static string Clean(string s) { return (s ?? "").Replace(";", ",").Replace("\t", " ").Replace("=", "-").Replace("\n", " "); }

    // ghi no chung (Kinh te giu so no); tra false neu khong co Kinh te
    static bool AddDebt(int amount, string cat, string note)
    {
        Action<int, string, string> f = Bus("GVL.AddDebt") as Action<int, string, string>;
        if (f == null) return false;
        try { f(amount, cat, note); return true; } catch { return false; }
    }

    // giao xe cho bai giu xe cua Kinh te (Davis); tra false neu khong co Kinh te
    static bool SendToImpound(CarRec c)
    {
        Action<int, int, int, string> f = Bus("GVL.ImpoundCar") as Action<int, int, int, string>;
        if (f == null) return false;
        try { f(c.Hash, c.Prim, c.Sec, c.Plate); return true; } catch { return false; }
    }

    static bool Pay(int amount, string cat, string note)
    {
        if (amount <= 0) return true;
        Func<int, string, string, bool> f = Bus("GVL.Pay") as Func<int, string, string, bool>;
        if (f != null) { try { return f(amount, cat, note); } catch { } }
        if (Game.Player.Money < amount) return false;
        Game.Player.Money = Game.Player.Money - amount;
        return true;
    }

    long GameDay() { long[] c = Bus("GVL.Clock") as long[]; return c != null && c.Length > 1 ? c[1] : dayNo; }
    static long Epoch() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }

    long busLast;
    void PollBus()
    {
        foreach (string[] ev in Poll(ref busLast))
        {
            string type = ev[1], data = ev[2];
            if (type == "core.day") OnNewDay();
            else if (type == "econ.release")
            {
                string rp = Field(data, "plate"), rh = Field(data, "hash");
                int rhash; bool hasHash = int.TryParse(rh, out rhash);
                foreach (CarRec c in garage)
                {
                    if (!c.InEconPound) continue;
                    bool match = (rp.Length == 0 && !hasHash) || (c.Plate == rp && (!hasHash || c.Hash == rhash));   // thieu truong thi mo het
                    if (match) { c.Impounded = false; c.InEconPound = false; }
                }
                Save();
            }
            else if (type == "life.gym") { float min; float.TryParse(Field(data, "minutes"), NumberStyles.Float, CultureInfo.InvariantCulture, out min); TrainMuscle(Math.Max(10f, min) / 30f); }
        }
    }

    long lastDayHandled = -1;
    void OnNewDay()
    {
        long day = GameDay();
        if (day == lastDayHandled) return;
        lastDayHandled = day;
        hairDays++; beardDays++;
        if (day - lastTrainDay > 3) muscle = Math.Max(0f, muscle - 0.4f);
        if (age > 0 && day % 60 == 0) age++;
        if (suitAmount > 0 && day >= suitDue) Court();
        PublishPersonal();
    }

    // ============================================================ TRỤ 2: PHÁP LÝ
    static readonly Vector3 CityHall = new Vector3(-544.9f, -204.4f, 38.2f);
    static readonly Vector3 PrisonYard = new Vector3(1641.6f, 2570.5f, 45.6f);
    static readonly Vector3 PrisonGate = new Vector3(1849.2f, 2586.0f, 45.7f);
    static readonly string[] LicName = { "ID", "A1", "B2", "C", "GUN" };
    static readonly string[] LicLabel = { "Can cuoc cong dan", "Bang lai A1 (xe may)", "Bang lai B2 (o to)", "Bang lai C (xe tai)", "Giay phep so huu sung" };
    static readonly int[] LicFee = { 20, 50, 150, 300, 500 };
    readonly HashSet<string> lic = new HashSet<string>();
    int record;
    long jailUntil;          // giay that; 0 = khong o tu
    int arrestStars, lastWanted;
    bool wasArrested;
    int nextStopCheck, stopCooldown, gunCooldown, crashCooldown;
    int suitAmount; long suitDue; string suitVictim = "";
    int crashOffer, crashOfferUntil;
    Keys payKey = Keys.B;   // Y la menu Doi song
    Blip hallBlip;
    float prevSpeedLegal;

    bool Has(string l) { return lic.Contains(l); }

    void MakeHallBlip()
    {
        try
        {
            if (hallBlip != null && hallBlip.Exists()) return;
            hallBlip = World.CreateBlip(CityHall);
            hallBlip.Sprite = BlipSprite.Information;
            hallBlip.Color = BlipColor.White;
            hallBlip.IsShortRange = true;
            hallBlip.Name = "Phong hanh chinh (giay to, bang lai)";
        }
        catch (Exception ex) { Log("blip hanh chinh loi: " + ex.Message); }
    }

    // ---- thi sat hach: 3 cau, dung 2 la dau
    class Quiz { public string Q; public string[] A; public int Right; public Quiz(string q, int r, params string[] a) { Q = q; Right = r; A = a; } }
    static readonly Quiz[] Road = {
        new Quiz("Den vang bat dau sang khi xe sap toi vach dung?", 1, "Tang ga vuot qua", "Dung lai truoc vach (tru khi da qua vach)", "Bam coi xin duong"),
        new Quiz("Toc do toi da xe may trong khu dong dan cu (duong doi)?", 2, "70 km/h", "40 km/h", "60 km/h"),
        new Quiz("Noi do con co nong do con khi lai xe la?", 0, "Bang 0, khong duoc uong", "Duoi 0,5 mg/l", "Tuy suc khoe"),
        new Quiz("Gap xe uu tien (cuu thuong) dang hu coi?", 1, "Chay nhanh hon", "Giam toc, tranh sang phai nhuong duong", "Giu nguyen lan"),
        new Quiz("Re trai tai nga tu co den xanh?", 2, "Re ngay khong can nhin", "Uu tien hon xe di thang", "Nhuong xe di thang va nguoi di bo"),
        new Quiz("Duoc dung dien thoai cam tay khi dang lai?", 0, "Khong", "Duoc neu cham", "Duoc khi dung den do"),
        new Quiz("Xe may cho toi da bao nhieu nguoi lon?", 1, "3 nguoi", "2 nguoi (tru truong hop dac biet)", "Khong gioi han"),
        new Quiz("Vach ke lien mau vang giua duong nghia la?", 2, "Duoc vuot thoai mai", "Chi danh cho xe tai", "Khong duoc de vach, khong vuot")
    };
    static readonly Quiz[] Guns = {
        new Quiz("Khi khong su dung, sung phai?", 1, "De trong tui quan", "Thao dan, cat trong hop co khoa", "De tren xe"),
        new Quiz("Duoc mang sung vao noi nao?", 2, "Quan bar", "Truong hoc", "Chi noi giay phep cho phep, khong noi cong cong dong nguoi"),
        new Quiz("Chia sung vao nguoi khac de dua?", 0, "Tuyet doi khong", "Duoc neu khong co dan", "Duoc neu la ban be")
    };
    int quizLic = -1, quizStep, quizRight;
    int[] quizOrder;

    void StartQuiz(int li)
    {
        if (li != 0 && !Has("ID")) { Notify("Can co Can cuoc cong dan truoc."); return; }
        if (li == 4 && record > 0) { Notify("~r~Co tien an~s~: khong duoc cap giay phep sung."); return; }
        if (Game.Player.Character.Position.DistanceTo(CityHall) > 60f) { Notify("Den ~b~Phong hanh chinh~s~ (chu i tren ban do) de lam giay to."); return; }
        if (!Pay(LicFee[li], "license", LicLabel[li])) { Notify("Khong du $" + LicFee[li] + " le phi."); return; }
        if (li == 0) { GrantLicense(0); ShowLegal(); return; }
        Quiz[] pool = li == 4 ? Guns : Road;
        List<int> idx = new List<int>(); for (int i = 0; i < pool.Length; i++) idx.Add(i);
        for (int i = idx.Count - 1; i > 0; i--) { int j = rndL.Next(i + 1); int t = idx[i]; idx[i] = idx[j]; idx[j] = t; }
        quizOrder = idx.GetRange(0, 3).ToArray();
        quizLic = li; quizStep = 0; quizRight = 0;
        ShowQuiz();
    }
    readonly Random rndL = new Random();

    void ShowQuiz()
    {
        Quiz[] pool = quizLic == 4 ? Guns : Road;
        if (quizStep >= 3)
        {
            bool pass = quizRight >= 2;
            if (pass) GrantLicense(quizLic); else Notify("~r~Truot~s~ (" + quizRight + "/3). Thi lai phai nop le phi lan nua.");
            quizLic = -1; ShowLegal(); return;
        }
        Quiz q = pool[quizOrder[quizStep]];
        Begin("SAT HACH " + LicName[quizLic] + "  cau " + (quizStep + 1) + "/3", delegate { quizLic = -1; ShowLegal(); });
        Info(q.Q);
        for (int i = 0; i < q.A.Length; i++)
        {
            int k = i;
            Item(q.A[k], delegate { if (k == q.Right) quizRight++; quizStep++; ShowQuiz(); });
        }
    }

    void GrantLicense(int li)
    {
        lic.Add(LicName[li]);
        Notify("~g~Da cap: " + LicLabel[li] + "~s~");
        Emit("legal.license", "type=" + LicName[li]);
        PublishPersonal(); Save();
    }

    string NeededLicense(Vehicle v)
    {
        int h = v.Model.Hash;
        if (Function.Call<bool>(Hash.IS_THIS_MODEL_A_BICYCLE, h)) return null;
        if (Function.Call<bool>(Hash.IS_THIS_MODEL_A_BIKE, h)) return "A1";
        if (!Function.Call<bool>(Hash.IS_THIS_MODEL_A_CAR, h)) return null;   // thuyen, may bay: bo qua
        int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v);
        return cls == 10 || cls == 20 ? "C" : "B2";
    }

    static bool IsCop(Ped p)
    {
        if (p == null || !p.Exists() || !p.IsAlive || p.IsPlayer) return false;
        int t = Function.Call<int>(Hash.GET_PED_TYPE, p);
        return t == 6 || t == 27;
    }

    Ped NearCop(Ped me, float r)
    {
        foreach (Ped p in World.GetNearbyPeds(me, r)) if (IsCop(p)) return p;
        return null;
    }

    void UpdateLegal(Ped me, int now)
    {
        if (!ModOn("Legal")) return;
        // ---- bi bat => vao tu
        int w = Game.Player.WantedLevel;
        if (w > 0) lastWanted = w;
        if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player, true)) { if (!wasArrested) { wasArrested = true; arrestStars = Math.Max(1, lastWanted); } }
        else if (wasArrested && me.IsAlive && !GTA.UI.Screen.IsFadedOut) { wasArrested = false; GoToJail(arrestStars); }
        if (jailUntil > 0) { UpdateJail(me); return; }
        if (w == 0 && !Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player, true)) lastWanted = 0;

        // ---- canh sat kiem tra giay to khi dang lai
        if (now > nextStopCheck)
        {
            nextStopCheck = now + 5000;
            Vehicle v = me.IsInVehicle() ? me.CurrentVehicle : null;
            if (v != null && v.Exists() && v.Driver == me && w == 0 && now > stopCooldown && NearCop(me, 45f) != null && rndL.Next(100) < 30)
            {
                stopCooldown = now + 6 * 60000;
                PoliceStop(me, v);
            }
            // mang sung khong giay phep ngay truoc mat canh sat
            if (!me.IsInVehicle() && now > gunCooldown && !Has("GUN") && me.Weapons.Current != null && IsFirearm(me.Weapons.Current.Hash) && NearCop(me, 22f) != null)
            {
                gunCooldown = now + 120000;
                if (Game.Player.WantedLevel < 2) Game.Player.WantedLevel = 2;
                Notify("~r~Canh sat: Mang sung khong giay phep!~s~ Bo sung xuong (phim cat sung) hoac bi bat.");
                Emit("legal.fine", "amount=0;reason=sung khong phep;paid=0");
            }
        }

        // ---- va cham xe cua nguoi dan
        Vehicle mv = me.IsInVehicle() ? me.CurrentVehicle : null;
        if (mv != null && mv.Exists() && mv.Driver == me)
        {
            float sp = mv.Speed;
            float drop = prevSpeedLegal - sp;
            prevSpeedLegal = sp;
            if (drop > 4f && now > crashCooldown && Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, mv))
            {
                Vehicle victim = null;
                foreach (Vehicle ov in World.GetNearbyVehicles(me.Position, 7f))
                {
                    if (ov == null || !ov.Exists() || ov == mv) continue;
                    Ped d = ov.Driver;
                    if (d != null && d.Exists() && !d.IsPlayer && d.IsHuman) { victim = ov; break; }
                }
                if (victim != null)
                {
                    crashCooldown = now + 20000;
                    crashOffer = Math.Min(2500, 120 + (int)(drop * 45f));
                    crashOfferUntil = now + 15000;
                    Notify("~o~Va cham xe nguoi dan!~s~ Bam ~y~" + payKey + "~s~ trong 15 giay de boi thuong ~g~$" + crashOffer + "~s~ tai cho, khong thi ho kien ra toa (gap doi + an phi). Co the xuong xe giu N de thuong luong.");
                    Emit("legal.crash", "amount=" + crashOffer + ";victim=" + Clean(CarLabel(victim)));
                    PostEvent("Có người vừa đâm vào xe của một người dân ở " + SafeStreet(me.Position) + ".");
                }
            }
        }
        else prevSpeedLegal = 0f;
        if (crashOffer > 0 && now > crashOfferUntil)
        {
            suitAmount += crashOffer * 2 + 150; suitDue = GameDay() + 1; suitVictim = "chu xe bi dam";
            Notify("~r~Nguoi dan da kien Sep~s~. Toa xu vao ngay mai: $" + suitAmount + ".");
            crashOffer = 0; Save();
        }
    }

    static bool IsFirearm(WeaponHash h)
    {
        int g = Function.Call<int>(Hash.GET_WEAPONTYPE_GROUP, (int)h);
        // nhom can chien / tay khong / nem / binh cuu hoa: khong tinh
        return h != WeaponHash.Unarmed && g != Game.GenerateHash("GROUP_MELEE") && g != Game.GenerateHash("GROUP_UNARMED") && g != Game.GenerateHash("GROUP_THROWN") && g != Game.GenerateHash("GROUP_FIREEXTINGUISHER") && g != Game.GenerateHash("GROUP_PETROLCAN");
    }

    void PoliceStop(Ped me, Vehicle v)
    {
        string need = NeededLicense(v);
        List<string> miss = new List<string>();
        int fine = 0;
        if (!Has("ID")) { miss.Add("CCCD"); fine += 100; }
        if (need != null && !Has(need)) { miss.Add("bang " + need); fine += need == "A1" ? 250 : need == "B2" ? 400 : 600; }
        if (miss.Count == 0) { Notify("~b~CSGT kiem tra giay to~s~: day du. Chuc Sep thuong lo binh an."); Emit("legal.stop", "result=ok"); return; }
        bool paid = Pay(fine, "fine", "thieu " + string.Join(", ", miss.ToArray()));
        string res = "fine";
        bool debted = !paid && AddDebt(fine, "fine", "phat thieu giay to");
        string msg = "~r~CSGT kiem tra:~s~ thieu " + string.Join(", ", miss.ToArray()) + ". Phat $" + fine + (paid ? "." : debted ? " ~r~(khong du tien, ghi vao so no)~s~." : " ~r~(khong du tien, ghi vao ho so)~s~.");
        if (!paid && !debted) record++;
        if (need != null && !Has(need))
        {
            res = "impound";
            bool viaEcon = false;
            foreach (CarRec c in garage)
                if (c.Hash == v.Model.Hash && c.Plate == SafePlate(v)) { c.Impounded = true; if (SendToImpound(c)) { c.InEconPound = true; viaEcon = true; } }
            msg += viaEcon ? " ~r~Xe bi giu o bai Davis~s~ (lay o menu Kinh te, phim L)." : " ~r~Tam giu xe~s~ 1 ngay.";
            me.Task.LeaveVehicle(v, false);
            Vehicle vv = v;
            pendingDelete = vv; pendingDeleteAt = Game.GameTime + 4000;
            if (calledCar == v) { calledCar = null; calledIdx = -1; }
        }
        Notify(msg);
        Emit("legal.stop", "result=" + res);
        Emit("legal.fine", "amount=" + fine + ";reason=" + Clean(string.Join(" ", miss.ToArray())) + ";paid=" + (paid ? 1 : 0));
        PublishPersonal(); Save();
    }
    Vehicle pendingDelete; int pendingDeleteAt;

    void GoToJail(int stars)
    {
        int minutes = Math.Max(2, stars * 3);
        record++;
        jailUntil = Epoch() + minutes * 60;
        Ped me = Game.Player.Character;
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, PrisonYard.X, PrisonYard.Y, PrisonYard.Z, false, false, false);
        Game.Player.WantedLevel = 0;
        Notify("~r~Bi tam giam " + minutes + " phut (thoi gian that)~s~. Tien an: " + record + ". Bam " + menuKey + " > Giay to de nop tien bao lanh ($" + Bail() + ").");
        Emit("legal.arrest", "stars=" + stars);
        Emit("legal.jail", "minutes=" + minutes);
        PostEvent("Nghe nói có người vừa bị bắt giam ở trại Bolingbroke.");
        PublishPersonal(); Save();
    }

    int Bail() { return 400 + 300 * Math.Max(1, arrestStars); }

    void UpdateJail(Ped me)
    {
        long left = jailUntil - Epoch();
        if (left <= 0) { Release(me, "Mãn hạn"); return; }
        Game.Player.WantedLevel = 0;
        if (me.Position.DistanceTo(PrisonYard) > 140f)
        {
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, PrisonYard.X, PrisonYard.Y, PrisonYard.Z, false, false, false);
            jailUntil += 60;
            Notify("~r~Tron trai khong thanh~s~: them 1 phut.");
        }
        GTA.UI.Screen.ShowSubtitle("~r~Dang o tu~s~: con " + (left / 60) + ":" + (left % 60).ToString("00") + "   (bao lanh: " + menuKey + " > Giay to)", 300);
    }

    void Release(Ped me, string why)
    {
        jailUntil = 0;
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, PrisonGate.X, PrisonGate.Y, PrisonGate.Z, false, false, false);
        Notify("~g~Ra tu~s~ (" + Ascii(why) + "). Ho so co " + record + " tien an.");
        Emit("legal.release", "why=" + Clean(why));
        PublishPersonal(); Save();
    }

    void Court()
    {
        int amt = suitAmount;
        suitAmount = 0;
        if (Pay(amt, "court", "boi thuong dan su + an phi"))
        {
            Notify("~o~Toa an xu~s~: Sep boi thuong cho " + suitVictim + " $" + amt + " (gom an phi).");
            Emit("legal.court", "amount=" + amt + ";paid=1");
        }
        else if (AddDebt(amt, "court", "an phi + boi thuong dan su"))
        {
            Notify("~r~Toa an xu~s~: khong du $" + amt + " thi hanh an, ghi vao so no.");
            Emit("legal.court", "amount=" + amt + ";paid=0");
        }
        else
        {
            record++;
            Notify("~r~Toa an xu~s~: khong du $" + amt + " thi hanh an. Ghi them 1 tien an.");
            Emit("legal.court", "amount=" + amt + ";paid=0");
        }
        Save();
    }

    void ShowLegal()
    {
        Begin("GIAY TO & PHAP LY", delegate { ShowMain(); });
        List<string> have = new List<string>();
        for (int i = 0; i < LicName.Length; i++) if (Has(LicName[i])) have.Add(LicName[i]);
        Info("Dang co: " + (have.Count > 0 ? string.Join(", ", have.ToArray()) : "chua co giay to nao") + "   Tien an: " + record);
        if (suitAmount > 0) Info("~o~Vu kien dang cho xu: $" + suitAmount + " (ngay game " + suitDue + ")");
        if (jailUntil > 0)
        {
            Item("Nop tien bao lanh ra tu ngay  (-$" + Bail() + ")", delegate
            {
                if (Pay(Bail(), "bail", "bao lanh")) { Release(Game.Player.Character, "nộp bảo lãnh"); CloseMenu(); }
                else Notify("Khong du $" + Bail() + ".");
            });
        }
        bool near = Game.Player.Character.Position.DistanceTo(CityHall) < 60f;
        Info(near ? "Dang o Phong hanh chinh: lam giay to duoc." : "Lam giay to: den Phong hanh chinh (chu i trang, Rockford Hills).");
        for (int i = 0; i < LicName.Length; i++)
        {
            if (Has(LicName[i])) continue;
            int k = i;
            Item((k == 0 ? "Lam " : "Thi ") + LicLabel[k] + "  (-$" + LicFee[k] + ")" + (k == 0 ? "" : "|3 cau trac nghiem, dung 2 la dau" + (k == 4 ? ", khong duoc co tien an" : "")), delegate { StartQuiz(k); });
        }
        foreach (CarRec c in garage)
        {
            if (!c.Impounded) continue;
            if (c.InEconPound) { Info("Xe " + c.Label + " dang o bai Davis: lay o menu Kinh te (phim L)."); continue; }
            CarRec cc = c;
            Item("Lay xe o bai giu: " + c.Label + "  (-$200)", delegate { if (Pay(200, "impound", c.Label)) { cc.Impounded = false; Notify("Da lay xe ra. Goi xe tu Garage."); Save(); } else Notify("Khong du $200."); ShowLegal(); });
        }
        Info("Luat: lai xe thieu bang bi phat + giu xe; cam sung truoc canh sat ma khong giay phep bi truy na; dam xe dan phai boi thuong (" + payKey + ").");
        Item("Quay lai", delegate { ShowMain(); });
    }

    // ============================================================ TRỤ 1: THỂ CHẤT & NGOẠI HÌNH
    float kg = 68f, muscle = 20f;
    int hairDays, beardDays, age = 25;
    long lastTrainDay;
    int nextBodyTick;
    static readonly Vector3[] Barbers = {
        new Vector3(-814.3f, -183.8f, 37.6f), new Vector3(136.8f, -1708.4f, 29.3f), new Vector3(-1282.6f, -1116.8f, 7.0f),
        new Vector3(1931.5f, 3729.7f, 32.8f), new Vector3(1212.8f, -472.9f, 66.2f), new Vector3(-32.9f, -152.3f, 57.1f), new Vector3(-278.1f, 6228.5f, 31.7f)
    };
    static readonly Dictionary<string, float> FatKg = new Dictionary<string, float> { { "banhmi", 0.03f }, { "comhop", 0.05f }, { "pho", 0.04f }, { "tangluc", 0.02f }, { "caphe", 0.01f } };

    void UpdateBody(Ped me, int now)
    {
        if (!ModOn("Body") || now < nextBodyTick) return;
        nextBodyTick = now + 1000;   // moi giay that ~ 2 phut game
        float[] n = Needs();
        float h = n != null ? n[0] : 60f;
        if (h > 85f) kg += 0.0012f; else if (h < 30f) kg -= 0.0016f;
        if (!me.IsInVehicle()) { if (me.IsSprinting || me.IsSwimming) kg -= 0.0006f; else if (me.IsRunning) kg -= 0.0003f; }
        if (me.IsInMeleeCombat) TrainMuscle(0.01f);
        kg = Math.Max(45f, Math.Min(130f, kg));
    }

    void TrainMuscle(float units)
    {
        muscle = Math.Min(100f, muscle + 1.5f * units * Math.Max(0.1f, 1f - muscle / 120f));
        lastTrainDay = GameDay();
        Gain(SkStrength, 0.5f * units);
    }

    float WeightDrain() { return kg > 95f ? 1.2f : kg > 88f ? 1.1f : 1f; }

    void PushUps()
    {
        Ped me = Game.Player.Character;
        if (me.IsInVehicle()) { Notify("Xuong xe roi moi tap."); return; }
        float[] n = Needs();
        if (n != null && n[2] < 15f) { Notify("Met qua, nghi ngoi truoc da."); return; }
        try { me.Task.PlayAnimation("amb@world_human_push_ups@male@base", "base", 8f, 8000, AnimationFlags.None); } catch { }
        AddNeeds(-3f, -6f, -10f);
        TrainMuscle(1f);
        kg -= 0.05f;
        Notify("Chong day 30 phut: co bap " + (int)muscle + "/100, can nang " + kg.ToString("0.0", CultureInfo.InvariantCulture) + " kg.");
        Save();
    }

    int NearBarber() { Vector3 p = Game.Player.Character.Position; for (int i = 0; i < Barbers.Length; i++) if (p.DistanceTo(Barbers[i]) < 30f) return i; return -1; }

    void Barber(bool hair, bool beard)
    {
        if (NearBarber() < 0) { Notify("Den tiem cat toc (bieu tuong keo tren ban do) roi chon lai."); return; }
        int cost = (hair ? 25 : 0) + (beard ? 15 : 0) - (hair && beard ? 5 : 0);
        if (!Pay(cost, "barber", hair && beard ? "cat toc + cao rau" : hair ? "cat toc" : "cao rau")) { Notify("Khong du $" + cost + "."); return; }
        if (hair) hairDays = 0;
        if (beard) { beardDays = 0; ApplyBeard(Game.Player.Character); }
        Notify("~g~Gon gang roi~s~ (-$" + cost + "). NPC se thay Sep sach se hon.");
        Emit("body.haircut", "kind=" + (hair && beard ? "ca_hai" : hair ? "toc" : "rau"));
        nextStatePost = 0; Save();
    }

    // rau chi mo phong duoc tren nhan vat freemode (mp_m_freemode_01); Michael/Franklin/Trevor chi doi cach NPC nhin
    void ApplyBeard(Ped me)
    {
        try
        {
            if (!IsFreemode(me)) return;
            float op = Math.Min(beardMax, beardDays / 10f);
            if (beardDays <= 0 || op <= 0.05f || beardStyle == 0) NatOverlay(me, 1, 255, 0f);
            else { NatOverlay(me, 1, beardStyle, op); NatOverlayTint(me, 1, 1, hairColor, hairColor); }
        }
        catch { }
    }

    string LookWords()
    {
        List<string> p = new List<string>();
        if (kg > 95f) p.Add("bụng phệ, khá béo"); else if (kg > 85f) p.Add("hơi đẫy đà"); else if (kg < 55f) p.Add("gầy gò");
        if (muscle > 70f) p.Add("vạm vỡ, cơ bắp"); else if (muscle > 45f) p.Add("người săn chắc");
        if (hairDays > 25) p.Add("tóc tai bù xù lâu chưa cắt");
        if (beardDays > 7) p.Add("râu ria lởm chởm");
        return string.Join(", ", p.ToArray());
    }

    void ShowBody()
    {
        Begin("THE CHAT & NGOAI HINH", delegate { ShowMain(); });
        Info("Tuoi " + age + "   Can nang " + kg.ToString("0.0", CultureInfo.InvariantCulture) + " kg   Co bap " + (int)muscle + "/100   The luc " + (int)skill[SkStam] + "/100");
        Info("Toc: " + hairDays + " ngay chua cat   Rau: " + beardDays + " ngay chua cao");
        Info("An no lien tuc (>85) thi tang can; chay, boi, tap thi giam. Tren 88 kg doi/khat/met nhanh hon.");
        Item("Chong day / tap tai cho  (30 phut game)|co bap +, ton nang luong, doi, khat", delegate { pending.Enqueue(PushUps); CloseMenu(); });
        bool nb = NearBarber() >= 0;
        if (!nb) Info("Cat toc/cao rau: den tiem cat toc cua game (bieu tuong keo).");
        Item("Cat toc  ($25)", delegate { Barber(true, false); ShowBody(); });
        Item("Cao rau  ($15)", delegate { Barber(false, true); ShowBody(); });
        Item("Ca hai  ($35)", delegate { Barber(true, true); ShowBody(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    // ============================================================ NHÂN VẬT TUỲ BIẾN (mặt, tóc, râu): dùng mẫu freemode của GTA Online trong chơi đơn
    const string FreemodeName = "mp_m_freemode_01";
    bool customOn, autoKeep = true;
    int prevModelHash;
    int shapeA = 17, shapeB = 18, skinA = 17, skinB = 18;
    float shapeMix = 0.5f, skinMix = 0.5f;
    readonly float[] face = new float[20];
    int hairStyle = 19, hairColor = 0, hairHi = 0, eyeColor = 5, browStyle = 0, beardStyle = 1;
    float beardMax = 0.7f;
    int nextLookCheck;
    static readonly string[] FaceName = {
        "Do rong mui", "Do cao dinh mui", "Do dai mui", "Do cao song mui", "Do ha dau mui", "Do lech song mui",
        "Do cao long may", "Do sau long may", "Do cao xuong gò ma", "Do rong xuong go ma", "Do rong ma",
        "Do mo mat (+ la hep)", "Do day moi", "Do rong ham", "Do dai ham", "Do ha cam", "Do dai cam", "Do rong cam", "Lum cam", "Do day co" };

    static bool IsFreemode(Ped p) { return p != null && p.Exists() && p.Model.Hash == Game.GenerateHash(FreemodeName); }

    // cac native goi bang ma hash de khong phu thuoc ten enum cua tung ban SHVDN
    static void NatHeadBlend(Ped p, int sa, int sb, int ka, int kb, float sm, float km)
    { Function.Call((Hash)0x9414E18B9434C2FE, p, sa, sb, 0, ka, kb, 0, sm, km, 0f, false); }
    static void NatFace(Ped p, int i, float v) { Function.Call((Hash)0x71A5C1DBA060049E, p, i, v); }
    static void NatHair(Ped p, int c, int h) { Function.Call((Hash)0x4CFFC65454C93A49, p, c, h); }
    static void NatEye(Ped p, int c) { Function.Call((Hash)0x50B56988B170AFDF, p, c); }
    static void NatOverlay(Ped p, int id, int idx, float op) { Function.Call((Hash)0x48F44967FA05CC1E, p, id, idx, op); }
    static void NatOverlayTint(Ped p, int id, int type, int c1, int c2) { Function.Call((Hash)0x497BF74A7B9CB952, p, id, type, c1, c2); }
    static void NatDefaultClothes(Ped p) { Function.Call((Hash)0x45EEE61580806D63, p); }

    void ApplyLook(Ped me)
    {
        if (!IsFreemode(me)) return;
        try
        {
            NatHeadBlend(me, shapeA, shapeB, skinA, skinB, shapeMix, skinMix);
            for (int i = 0; i < face.Length; i++) NatFace(me, i, Math.Max(-1f, Math.Min(1f, face[i])));
            Function.Call(Hash.SET_PED_COMPONENT_VARIATION, me, 2, hairStyle, 0, 0);
            NatHair(me, hairColor, hairHi);
            NatEye(me, eyeColor);
            NatOverlay(me, 2, browStyle, 1f); NatOverlayTint(me, 2, 1, hairColor, hairColor);
            ApplyBeard(me);
        }
        catch (Exception ex) { Log("ap dung mat loi: " + ex.Message); }
    }

    // mau dung san: dung theo anh Sep (toc den ruou bay, hai ben ngan, long may thang, rau mo quanh moi/cam)
    void ResetPreset()
    {
        shapeA = 17; shapeB = 18; skinA = 17; skinB = 18; shapeMix = 0.5f; skinMix = 0.5f;
        float[] f = { 0.2f, -0.2f, -0.2f, -0.3f, 0f, 0f, -0.1f, 0.1f, -0.1f, 0.2f, 0.1f, 0.5f, -0.1f, -0.2f, 0f, 0f, 0f, -0.3f, 0f, 0.1f };
        Array.Copy(f, face, f.Length);
        hairStyle = 19; hairColor = 0; hairHi = 0; eyeColor = 5; browStyle = 0; beardStyle = 1; beardMax = 0.7f;
        if (beardDays < 4) beardDays = 4;
    }

    void BecomeCustom(bool preset)
    {
        Ped me = Game.Player.Character;
        if (jailUntil > 0) { Notify("Dang o tu, khong doi nhan vat duoc."); return; }
        if (me.IsInVehicle()) { Notify("Xuong xe roi moi doi nhan vat."); return; }
        if (preset) ResetPreset();
        if (!IsFreemode(me))
        {
            Model m = new Model(FreemodeName);
            if (!m.IsInCdImage || !m.Request(5000)) { Notify("~r~Khong nap duoc mau nhan vat tuy bien~s~ (ban game/SHVDN nay khong ho tro?)."); return; }
            if (!customOn) prevModelHash = me.Model.Hash;
            Game.Player.ChangeModel(m);
            m.MarkAsNoLongerNeeded();
            me = Game.Player.Character;
            NatDefaultClothes(me);
        }
        customOn = true;
        ApplyLook(me);
        Notify("~g~Da tao nhan vat theo anh Sep.~s~ Chinh them o menu Nhan vat cua Sep. Do mac: thay o tiem quan ao roi luu o 'Trang phuc'.");
        Emit("body.look", "kind=custom");
        PublishPersonal(); Save();
    }

    void BackToOriginal()
    {
        Ped me = Game.Player.Character;
        if (me.IsInVehicle()) { Notify("Xuong xe roi moi doi nhan vat."); return; }
        int h = prevModelHash != 0 ? prevModelHash : Game.GenerateHash("player_zero");
        Model m = new Model(h);
        if (!m.IsInCdImage || !m.Request(5000)) { Notify("Khong nap duoc nhan vat goc."); return; }
        Game.Player.ChangeModel(m);
        m.MarkAsNoLongerNeeded();
        customOn = false;
        Notify("Da ve nhan vat goc cua game.");
        Save();
    }

    // chuyen nhan vat (Michael/Franklin/Trevor) hoac nhiem vu co the doi model: dua ve nhan vat tuy bien
    void KeepLook(Ped me, int now)
    {
        if (!customOn || !autoKeep || now < nextLookCheck) return;
        nextLookCheck = now + 4000;
        if (IsFreemode(me) || !me.IsAlive || me.IsInVehicle() || jailUntil > 0) return;
        if (Function.Call<bool>((Hash)0x991251AFC3981F84)) return;   // IS_CUTSCENE_ACTIVE
        BecomeCustom(false);
    }


    void ShowLook()
    {
        Begin("NHAN VAT CUA SEP", delegate { ShowMain(); });
        Info(customOn ? "Dang dung nhan vat tuy bien (mat, toc, rau chinh duoc)." : "Dang dung nhan vat goc cua game (Michael/Franklin/Trevor).");
        Item("Tao nhan vat giong anh Sep (mau dung san)|doi sang nhan vat tuy bien: toc den hai ben ngan, long may thang, rau mo", delegate { CloseMenu(); BecomeCustom(true); });
        if (customOn) Item("Chinh khuon mat (20 thanh)...", delegate { ShowFace(); });
        if (customOn) Item("Chinh toc, may, mat, rau, khuon va nen da...", delegate { ShowHair(); });
        if (customOn) Item("Dat lai mau dung san", delegate { ResetPreset(); ApplyLook(Game.Player.Character); Save(); ShowLook(); });
        if (customOn) Item("Ve nhan vat goc cua game", delegate { CloseMenu(); BackToOriginal(); });
        Info("Mau mat la dung tay theo anh (game khong nhap anh thang duoc). Chinh tung thanh cho giong hon: Enter de tang, het thang thi quay ve dau.");
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ReShow(Action show) { int keep = menuSel; show(); menuSel = keep; }

    void ShowFace()
    {
        Begin("CHINH KHUON MAT  (Enter: +0.2, het thi quay lai -1)", delegate { ShowLook(); });
        for (int i = 0; i < face.Length; i++)
        {
            int k = i;
            Item(FaceName[k] + ":  " + face[k].ToString("0.0", CultureInfo.InvariantCulture), delegate
            {
                face[k] = (float)Math.Round(face[k] + 0.2f, 1);
                if (face[k] > 1.01f) face[k] = -1f;
                ApplyLook(Game.Player.Character); Save();
                ReShow(ShowFace);
            });
        }
        Item("Quay lai", delegate { ShowLook(); });
    }

    void ShowHair()
    {
        Begin("TOC, MAY, MAT, RAU  (Enter: doi gia tri tiep theo)", delegate { ShowLook(); });
        Item("Kieu toc: " + hairStyle + " / 22", delegate { hairStyle = (hairStyle + 1) % 23; HairChanged(); });
        Item("Mau toc: " + hairColor + " / 63  (0 = den)", delegate { hairColor = (hairColor + 1) % 64; HairChanged(); });
        Item("Mau nhuom (highlight): " + hairHi + " / 63", delegate { hairHi = (hairHi + 1) % 64; HairChanged(); });
        Item("Mau mat: " + eyeColor + " / 31  (5 = nau dam)", delegate { eyeColor = (eyeColor + 1) % 32; HairChanged(); });
        Item("Kieu long may: " + browStyle + " / 33", delegate { browStyle = (browStyle + 1) % 34; HairChanged(); });
        Item("Kieu rau: " + beardStyle + " / 28  (0 = nhan, 1 = mo)", delegate { beardStyle = (beardStyle + 1) % 29; HairChanged(); });
        Item("Do dam rau toi da: " + beardMax.ToString("0.0", CultureInfo.InvariantCulture), delegate { beardMax = (float)Math.Round(beardMax + 0.1f, 1); if (beardMax > 1.01f) beardMax = 0.1f; HairChanged(); });
        Item("Khuon mat cha (so): " + shapeA + " / 44", delegate { shapeA = (shapeA + 1) % 45; HairChanged(); });
        Item("Khuon mat me (so): " + shapeB + " / 44", delegate { shapeB = (shapeB + 1) % 45; HairChanged(); });
        Item("Pha khuon cha-me: " + shapeMix.ToString("0.0", CultureInfo.InvariantCulture) + "  (0 = cha, 1 = me)", delegate { shapeMix = (float)Math.Round(shapeMix + 0.1f, 1); if (shapeMix > 1.01f) shapeMix = 0f; HairChanged(); });
        Item("Mau da cha (so): " + skinA + " / 44", delegate { skinA = (skinA + 1) % 45; HairChanged(); });
        Item("Mau da me (so): " + skinB + " / 44", delegate { skinB = (skinB + 1) % 45; HairChanged(); });
        Item("Pha mau da: " + skinMix.ToString("0.0", CultureInfo.InvariantCulture), delegate { skinMix = (float)Math.Round(skinMix + 0.1f, 1); if (skinMix > 1.01f) skinMix = 0f; HairChanged(); });
        Item("Quay lai", delegate { ShowLook(); });
    }

    void HairChanged() { ApplyLook(Game.Player.Character); Save(); ReShow(ShowHair); }

    // ============================================================ TRỤ 4: QUAN HỆ, ĐIỆN THOẠI, THÚ CƯNG
    readonly System.Collections.Concurrent.ConcurrentQueue<Action> fromNet = new System.Collections.Concurrent.ConcurrentQueue<Action>();
    readonly List<string[]> contacts = new List<string[]>();   // key, ten, cap, thien cam, trang thai
    string spouse = "";
    string callKey = "", callName = "";

    void Net(string path, NameValueCollection data, Action<string> done)
    {
        string url = server + path;
        Task.Run(delegate
        {
            string r = null;
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null; wc.Encoding = Encoding.UTF8;
                    r = data == null ? wc.DownloadString(url) : Encoding.UTF8.GetString(wc.UploadValues(url, "POST", data));
                }
            }
            catch (Exception ex) { Log(path + " loi: " + ex.Message); }
            string rr = r;
            fromNet.Enqueue(delegate { done(rr); });
        });
    }

    void ShowPhone()
    {
        Begin("DIEN THOAI", delegate { ShowMain(); });
        if (callKey.Length > 0) Item("Cup may (dang goi " + callName + ")", delegate { HangUp(); ShowPhone(); });
        Item("Danh ba...", delegate { LoadContacts(); });
        Item("Mang xa hoi: bang tin thanh pho", delegate { LoadFeed(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    void LoadContacts()
    {
        Begin("DANH BA", delegate { ShowPhone(); });
        Info("Dang tai...");
        Net("/contacts", null, delegate (string r)
        {
            if (!menuOpen || menuTitle != "DANH BA") return;
            contacts.Clear();
            Begin("DANH BA", delegate { ShowPhone(); });
            if (r == null) { Info("~r~Chua noi duoc cau noi AI~s~ (start_bridge.bat)."); Item("Quay lai", delegate { ShowPhone(); }); return; }
            foreach (string line in r.Replace("\r", "").Split('\n'))
            {
                string[] q = line.Split('|');
                if (q.Length < 5) continue;
                contacts.Add(q);
                string[] cq = q;
                Item(q[1] + "  -  " + q[2] + (q[4].Length > 0 ? " (" + q[4] + ")" : "") + "|thien cam " + q[3] + "/100", delegate { ShowContact(cq); });
            }
            if (contacts.Count == 0) Info("Chua co ai. Bam K canh mot nguoi de ghi nho, noi chuyen de co so dien thoai.");
            Item("Quay lai", delegate { ShowPhone(); });
        });
    }

    void ShowContact(string[] c)
    {
        string key = c[0], name = c[1];
        Begin(name.ToUpper() + "  (" + c[2] + ", " + c[3] + "/100)", delegate { LoadContacts(); });
        Item("Goi dien|giu N de noi vao dien thoai, " + menuKey + " > Dien thoai > Cup may de ket thuc", delegate
        {
            callKey = key; callName = name; BusSet("GVL.PhoneCall", key);
            Emit("social.call", "key=" + key + ";name=" + Clean(name));
            Notify("~b~Dang goi " + name + "...~s~ Giu ~y~N~s~ de noi.");
            CloseMenu();
        });
        Item("Nhan tin SMS", delegate
        {
            menuOpen = false;
            string s = Game.GetUserInput(WindowTitle.EnterMessage60, "", 60);
            menuOpen = true;
            if (string.IsNullOrEmpty(s) || s.Trim().Length == 0) { ShowContact(c); return; }
            NameValueCollection d = new NameValueCollection(); d["key"] = key; d["text"] = s.Trim();
            Notify("Da gui: " + s.Trim());
            Emit("social.sms", "key=" + key + ";name=" + Clean(name));
            Net("/sms", d, delegate (string r) { Notify(r == null ? "~r~Tin nhan khong gui duoc.~s~" : "~b~SMS tu " + name + ":~s~ " + FirstLine(r)); });
            ShowContact(c);
        });
        Item("Tang qua (ship tan noi)...", delegate { ShowGiftPick(c); });
        if (c[4] == "") Item("Ru di hen ho / to tinh|can thien cam 60+", delegate { Relation(c, "date", 0); });
        if (c[4] == "hen ho") Item("Cau hon  (nhan cuoi -$1500 neu dong y)|can 85+ va dang hen ho", delegate { Relation(c, "marry", 0); });
        if (c[4] == "hen ho" || c[4] == "vo chong") Item("Chia tay", delegate { Relation(c, "breakup", 0); });
        Item("Quay lai", delegate { LoadContacts(); });
    }

    void ShowGiftPick(string[] c)
    {
        Begin("TANG QUA CHO " + c[1].ToUpper(), delegate { ShowContact(c); });
        bool any = false;
        foreach (ItemDef d in Items)
        {
            if (!d.Id.StartsWith("qua_") || Have(d.Id) <= 0) continue;
            any = true;
            ItemDef dd = d;
            Item("Tang " + d.Name + "  (co " + Have(d.Id) + ")", delegate
            {
                bag[dd.Id] = Have(dd.Id) - 1; if (bag[dd.Id] <= 0) bag.Remove(dd.Id);
                Relation(c, "gift", dd.Price);
            });
        }
        if (!any) Info("Chua co qua. Mua Hoa, Socola, Nuoc hoa trong Tui do > Mua do.");
        Item("Quay lai", delegate { ShowContact(c); });
    }

    void Relation(string[] c, string action, int value)
    {
        NameValueCollection d = new NameValueCollection(); d["key"] = c[0]; d["action"] = action; d["value"] = value.ToString();
        Notify("Dang cho " + c[1] + " tra loi...");
        CloseMenu();
        Net("/relation", d, delegate (string r)
        {
            if (r == null) { Notify("~r~Chua noi duoc cau noi AI.~s~"); return; }
            string[] q = r.Replace("\r", "").Split('\n');
            bool ok = q[0].Trim() == "ok";
            string reply = q.Length > 1 ? q[1] : "";
            Notify((ok ? "~g~" : "~o~") + c[1] + ":~s~ " + reply);
            if (action == "marry" && ok)
            {
                if (Pay(1500, "date", "nhan cuoi")) { spouse = c[0]; Emit("social.marry", "key=" + c[0] + ";name=" + Clean(c[1])); Notify("~g~Chuc mung Sep da ket hon!~s~ Vo/chong se chia se chi phi sinh hoat (Doi song)."); }
                else Notify("Khong du tien mua nhan cuoi, hen dip khac.");
            }
            else if (action == "date" && ok) Emit("social.date", "key=" + c[0] + ";name=" + Clean(c[1]));
            else if (action == "breakup") { if (spouse == c[0]) spouse = ""; Emit("social.breakup", "key=" + c[0] + ";name=" + Clean(c[1])); }
            else if (action == "gift") Emit("social.gift", "key=" + c[0] + ";name=" + Clean(c[1]) + ";value=" + value);
            PublishPersonal(); Save();
        });
    }

    void HangUp()
    {
        if (callKey.Length == 0) return;
        Notify("Da cup may voi " + callName + ".");
        callKey = ""; callName = ""; BusSet("GVL.PhoneCall", "");
    }

    void LoadFeed()
    {
        Begin("MANG XA HOI", delegate { ShowPhone(); });
        Info("Dang tai...");
        Net("/feed", null, delegate (string r)
        {
            if (!menuOpen || menuTitle != "MANG XA HOI") return;
            Begin("MANG XA HOI", delegate { ShowPhone(); });
            if (r == null) Info("~r~Chua noi duoc cau noi AI.~s~");
            else
            {
                int n = 0;
                foreach (string line in r.Replace("\r", "").Split('\n')) { if (line.Trim().Length == 0) continue; Info(line.Trim()); n++; }
                if (n == 0) Info("Chua co tin gi moi.");
            }
            Item("Quay lai", delegate { ShowPhone(); });
        });
    }

    static string FirstLine(string r) { string[] q = (r ?? "").Replace("\r", "").Split('\n'); return q.Length > 0 ? q[0] : ""; }

    // ---- thu cung
    static readonly string[] PetModels = { "a_c_retriever", "a_c_shepherd", "a_c_husky", "a_c_pug", "a_c_westy", "a_c_poodle" };
    static readonly string[] PetNames = { "Cho vang (Retriever)", "Becgie", "Husky", "Pug", "Westie", "Poodle" };
    int petModel = -1; string petName = "";
    Ped petPed;
    int nextPetCheck;

    void UpdatePet(Ped me, int now)
    {
        if (petModel < 0 || now < nextPetCheck || jailUntil > 0) return;
        nextPetCheck = now + 3000;
        if (petPed != null && petPed.Exists())
        {
            if (!petPed.IsAlive) { Notify("~r~" + petName + " bi thuong nang~s~. Goi lai trong menu Thu cung ($100 thu y)."); petPed.MarkAsNoLongerNeeded(); petPed = null; petModel = -petModel - 2; Save(); return; }
            if (petPed.Position.DistanceTo(me.Position) > 90f) { petPed.Delete(); petPed = null; }
            else if (!me.IsInVehicle() && petPed.Position.DistanceTo(me.Position) > 6f && !petPed.IsInGroup) me.PedGroup.Add(petPed, false);
            return;
        }
        if (!me.IsInVehicle()) SpawnPet(me);
    }

    void SpawnPet(Ped me)
    {
        if (petModel < 0) return;
        Model m = new Model(PetModels[petModel]);
        if (!m.Request(2000)) return;
        Ped p = World.CreatePed(m, me.Position + me.RightVector * 1.5f);
        m.MarkAsNoLongerNeeded();
        if (p == null || !p.Exists()) return;
        p.IsPersistent = true;
        me.PedGroup.Add(p, false);
        p.NeverLeavesGroup = true;
        Function.Call(Hash.SET_PED_CAN_BE_TARGETTED, p, false);
        Blip b = p.AddBlip(); b.Color = BlipColor.Green; b.Scale = 0.6f; b.Name = petName;
        petPed = p;
    }

    void ShowPet()
    {
        Begin("THU CUNG", delegate { ShowMain(); });
        if (petModel >= 0)
        {
            Info(petName + " (" + PetNames[petModel] + ") dang di theo Sep. Len xe thi o nha, xuong xe tu chay toi.");
            Item("Cho an  (-$5)", delegate { if (Pay(5, "pet", "thuc an thu cung")) Notify(petName + " vay duoi mung ro."); ShowPet(); });
            Item("Doi ten", delegate { menuOpen = false; string s = Game.GetUserInput(WindowTitle.EnterMessage20, petName, 20); menuOpen = true; if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0) { petName = s.Trim(); if (petPed != null && petPed.Exists() && petPed.AttachedBlip != null) petPed.AttachedBlip.Name = petName; Save(); } ShowPet(); });
            Item("Gui di nhan nuoi (bo thu cung)", delegate { if (petPed != null && petPed.Exists()) petPed.Delete(); petPed = null; petModel = -1; Notify("Da gui thu cung cho nguoi khac nuoi."); Save(); ShowPet(); });
        }
        else if (petModel <= -2)
        {
            Item("Chua tri thu y cho " + petName + "  (-$100)", delegate { if (Pay(100, "pet", "thu y")) { petModel = -petModel - 2; Notify(petName + " da khoe lai."); Save(); } else Notify("Khong du $100."); ShowPet(); });
        }
        else
        {
            for (int i = 0; i < PetModels.Length; i++)
            {
                int k = i;
                Item("Nuoi " + PetNames[k] + "  (-$300)", delegate
                {
                    if (!Pay(300, "pet", "mua thu cung")) { Notify("Khong du $300."); ShowPet(); return; }
                    petModel = k; petName = PetNames[k].Split(' ')[0];
                    Emit("social.pet", "model=" + PetModels[k]);
                    Notify("~g~Da nhan nuoi " + PetNames[k] + "~s~. No se chay theo Sep.");
                    Save(); ShowPet();
                });
            }
        }
        Item("Quay lai", delegate { ShowMain(); });
    }

    // ---- cong bo ho so cho cac module khac (docs/CORE.md)
    void PublishPersonal()
    {
        BusSet("GVL.Profile", new string[] { playerName, hometown, age.ToString(), wearing >= 0 ? OutfitSlotName[wearing] : "" });
        BusSet("GVL.Body", new float[] { kg, muscle, skill[SkStam], hairDays, beardDays });
        BusSet("GVL.Licenses", string.Join(",", new List<string>(lic).ToArray()));
        BusSet("GVL.Record", record);
        BusSet("GVL.Jailed", jailUntil > 0);
        BusSet("GVL.Spouse", spouse);
    }

    // chuyen chu co dau sang khong dau cho font GTA
    static string Ascii(string s)
    {
        string n = (s ?? "").Normalize(NormalizationForm.FormD);
        StringBuilder b = new StringBuilder();
        foreach (char ch in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            b.Append(ch == 'đ' ? 'd' : ch == 'Đ' ? 'D' : ch);
        }
        return b.ToString();
    }

    void PostEvent(string text)
    {
        NameValueCollection c = new NameValueCollection(); c["text"] = text;
        Net("/event", c, delegate (string r) { });
    }

    string SafeStreet(Vector3 pos) { try { return World.GetStreetName(pos); } catch { return "đâu đó"; } }

    // ------------------------------------------------------------ menu
    void Begin(string title, Action back) { menuTitle = title; menuLabels = new List<string>(); menuActions = new List<Action>(); menuBack = back; menuSel = -1; }
    void Item(string label, Action act) { menuLabels.Add(label); menuActions.Add(act); }
    void Info(string label) { menuLabels.Add(label); menuActions.Add(null); }

    int NextActionable(int from, int dir)
    {
        int n = menuActions.Count;
        if (n == 0) return 0;
        for (int k = 1; k <= n; k++) { int j = ((from + dir * k) % n + n) % n; if (menuActions[j] != null) return j; }
        return from;
    }

    void ShowMain()
    {
        Begin("CA NHAN  (" + menuKey + "/Backspace: dong)", null);
        Info("Ten: " + (playerName.Length > 0 ? playerName : "(chua dat)") + "   Que: " + (hometown.Length > 0 ? hometown : "(chua chon)") + "   Tien: $" + Game.Player.Money);
        Item("Ho so nhan vat...", delegate { ShowProfile(); });
        Item("Nhan vat cua Sep: mat, toc, rau...  (" + (customOn ? "tuy bien" : "goc cua game") + ")", delegate { ShowLook(); });
        Item("Ky nang...  (" + SkillSummary() + ")", delegate { ShowSkills(); });
        Item("Tui do...  (" + BagCount() + "/" + BagCap + " mon)", delegate { ShowBag(); });
        Item("Trang phuc...  (" + (wearing >= 0 ? "dang mac: " + OutfitSlotName[wearing] : "chua luu bo nao") + ")", delegate { ShowOutfits(); });
        Item("Garage xe rieng...  (" + garage.Count + "/" + MaxGarage + " xe)", delegate { ShowGarage(); });
        Item("So thu chi ca nhan...", delegate { ShowLedger(); });
        if (ModOn("Legal")) Item("Giay to & phap ly...  (" + lic.Count + " giay to, " + record + " tien an" + (jailUntil > 0 ? ", DANG O TU" : "") + ")", delegate { ShowLegal(); });
        if (ModOn("Body")) Item("The chat & ngoai hinh...  (" + kg.ToString("0", CultureInfo.InvariantCulture) + " kg, co " + (int)muscle + ")", delegate { ShowBody(); });
        if (ModOn("Social")) Item("Dien thoai: danh ba, goi, SMS, hen ho, mang xa hoi..." + (callKey.Length > 0 ? "  (dang goi)" : ""), delegate { ShowPhone(); });
        if (ModOn("Social")) Item("Thu cung..." + (petModel >= 0 ? "  (" + petName + ")" : ""), delegate { ShowPet(); });
        Item("Phim dieu khien & giong noi...", delegate { ShowKeys(); });
        Item("Luu tien trinh ngay", delegate { Save(); Notify("Da luu ho so ca nhan."); });
    }

    string SkillSummary()
    {
        int best = 0;
        for (int i = 1; i < skill.Length; i++) if (skill[i] > skill[best]) best = i;
        return skill[best] < 1f ? "moi bat dau" : "gioi nhat: " + SkillName[best] + " " + (int)skill[best];
    }

    void ShowProfile()
    {
        Begin("HO SO NHAN VAT", delegate { ShowMain(); });
        Info("Ten nay va que quan duoc gui cho NPC de ho goi dung ten Sep.");
        Item("Dat ten  (hien tai: " + (playerName.Length > 0 ? playerName : "chua co") + ")", delegate
        {
            menuOpen = false;
            string s = Game.GetUserInput(WindowTitle.EnterMessage20, playerName, 20);
            if (s != null && s.Trim().Length > 0) { playerName = s.Trim(); Notify("Ten moi: ~g~" + playerName); nextStatePost = 0; Save(); }
            menuOpen = true; ShowProfile();
        });
        Item("Chon que quan...  (" + (hometown.Length > 0 ? hometown : "chua chon") + ")", delegate { ShowHometowns(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowHometowns()
    {
        Begin("QUE QUAN", delegate { ShowProfile(); });
        for (int i = 0; i < Hometowns.Length; i++)
        {
            int k = i;
            Item((hometown == Hometowns[k] ? "> " : "") + Hometowns[k], delegate { hometown = Hometowns[k]; nextStatePost = 0; Save(); ShowProfile(); });
        }
    }

    void ShowSkills()
    {
        Begin("KY NANG  (tu tang khi Sep lam)", delegate { ShowMain(); });
        for (int i = 0; i < skill.Length; i++)
            Info(SkillName[i] + ": " + Bar(skill[i]) + " " + (int)skill[i] + "/100|" + SkillHow[i]);
        Info("Hien tai: nhu cau giam cham " + (int)(30f * skill[SkStam] / 100f) + "%, mua do re " + (int)(Discount * 100f) + "%.");
        Item("Quay lai", delegate { ShowMain(); });
    }

    static string Bar(float v)
    {
        int n = (int)Math.Round(v / 10f);
        return "[" + new string('|', n) + new string('.', 10 - n) + "]";
    }

    void ShowBag()
    {
        Begin("TUI DO  (" + BagCount() + "/" + BagCap + ")", delegate { ShowMain(); });
        float[] n = Needs();
        if (n != null) Info("An " + (int)n[0] + "  Uong " + (int)n[1] + "  Nang luong " + (int)n[2] + "  Mau " + Game.Player.Character.Health + "/" + Game.Player.Character.MaxHealth);
        bool any = false;
        foreach (ItemDef d in Items)
        {
            int c = Have(d.Id);
            if (c <= 0) continue;
            any = true;
            ItemDef dd = d;
            if (d.Id.StartsWith("qua_")) { Info(d.Name + "  x" + c + "  (tang qua dien thoai)"); continue; }
            Item("Dung " + d.Name + "  x" + c + "|" + Effect(d), delegate { Use(dd); ShowBag(); });
        }
        if (!any) Info("(Tui trong)");
        Item("Mua do (ship tan noi, +$" + ShipFee + "/don)...", delegate { ShowShop(); });
        if (!hasBackpack) Item("Mua ba lo  (-$" + BackpackPrice + ", tui len " + BagBig + " mon)", delegate
        {
            if (!Pay(BackpackPrice, "shop", "ba lo")) Notify("Khong du tien ($" + BackpackPrice + ").");
            else { hasBackpack = true; Notify("Da co ba lo: tui chua " + BagBig + " mon."); Save(); }
            ShowBag();
        });
        Item("Quay lai", delegate { ShowMain(); });
    }

    static string Effect(ItemDef d)
    {
        List<string> p = new List<string>();
        if (d.Hunger > 0) p.Add("an +" + (int)d.Hunger);
        if (d.Thirst > 0) p.Add("uong +" + (int)d.Thirst);
        if (d.Energy > 0) p.Add("nang luong +" + (int)d.Energy);
        if (d.Health > 0) p.Add("mau +" + d.Health);
        return string.Join(", ", p.ToArray());
    }

    void ShowShop()
    {
        Begin("MUA DO" + (Discount > 0.005f ? "  (giao tiep: -" + (int)(Discount * 100f) + "%)" : ""), delegate { ShowBag(); });
        foreach (ItemDef d in Items)
        {
            ItemDef dd = d;
            Item(d.Name + "  $" + PriceOf(d) + "  (co " + Have(d.Id) + ")|" + Effect(d) + "  -  mua 3: $" + (PriceOf(d) * 3 + ShipFee), delegate { Buy(dd, 3); ShowShop(); });
        }
        Item("Quay lai", delegate { ShowBag(); });
    }

    void ShowOutfits()
    {
        Begin("TRANG PHUC", delegate { ShowMain(); });
        Info("Thay do o tiem quan ao cua game, roi luu vao 1 trong 4 o de doi nhanh.");
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            bool has = !string.IsNullOrEmpty(outfit[k]);
            if (has) Item("Mac: " + OutfitSlotName[k] + (wearing == k ? "  (dang mac)" : ""), delegate { pending.Enqueue(delegate { WearOutfit(k); }); ShowOutfits(); });
            Item("Luu do dang mac vao o: " + OutfitSlotName[k] + (has ? "  (ghi de)" : ""), delegate { SaveOutfit(k); ShowOutfits(); });
        }
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowGarage()
    {
        Begin("GARAGE XE RIENG", delegate { ShowMain(); });
        for (int i = 0; i < garage.Count; i++)
        {
            int k = i;
            CarRec r = garage[k];
            bool here = calledIdx == k && calledCar != null && calledCar.Exists();
            Item("Goi xe: " + r.Label + "  [" + r.Plate + "]" + (here ? "  (dang ngoai duong)" : "") + "|giao toi cho Sep: -$" + CallCarFee, delegate { CloseMenu(); pending.Enqueue(delegate { CallCar(k); }); });
        }
        if (garage.Count == 0) Info("(Chua co xe. Ngoi len xe minh thich roi chon muc duoi.)");
        Item("Ghi xe dang ngoi vao garage", delegate { AddCurrentCar(); ShowGarage(); });
        if (garage.Count > 0) Item("Xoa xe khoi garage...", delegate { ShowGarageRemove(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowGarageRemove()
    {
        Begin("XOA XE", delegate { ShowGarage(); });
        for (int i = 0; i < garage.Count; i++)
        {
            int k = i;
            Item("Xoa: " + garage[k].Label + "  [" + garage[k].Plate + "]", delegate
            {
                if (calledIdx == k) ReleaseCalled(); else if (calledIdx > k) calledIdx--;
                Notify("Da xoa " + garage[k].Label + " khoi garage.");
                garage.RemoveAt(k); Save(); ShowGarage();
            });
        }
        Item("Quay lai", delegate { ShowGarage(); });
    }

    void ShowLedger()
    {
        Begin("SO THU CHI CA NHAN", delegate { ShowMain(); });
        Info("Hom nay: thu +$" + inToday + "   chi -$" + outToday + "   rong " + Signed(inToday - outToday));
        int ti = 0, to = 0;
        for (int i = moneyHist.Count - 1; i >= 0; i--)
        {
            int[] h = moneyHist[i];
            ti += h[1]; to += h[2];
            Info("Ngay " + h[0] + ": thu +$" + h[1] + "   chi -$" + h[2] + "   rong " + Signed(h[1] - h[2]));
        }
        if (moneyHist.Count > 0) Info(moneyHist.Count + " ngay qua: thu +$" + ti + ", chi -$" + to + ", rong " + Signed(ti - to));
        Info("Tinh moi thay doi tien cua nhan vat dang choi (luong, quan, mua sam, phat...).");
        Item("Quay lai", delegate { ShowMain(); });
    }

    static string Signed(int v) { return (v >= 0 ? "+$" : "-$") + Math.Abs(v); }

    void ShowKeys()
    {
        Begin("PHIM DIEU KHIEN", delegate { ShowMain(); });
        Info(menuKey + ": menu Ca nhan     " + quickKey + ": an/uong nhanh tu tui");
        Info(worldMenuKey + ": menu Doi song (an, ngu, nha, viec, kinh doanh)");
        Info("Giu " + talkKey + ": noi chuyen voi nguoi gan (<=5 m), tha de gui|can chay start_bridge.bat truoc; mic chon bang pick_mic.bat");
        Info("K: ghi nho nguoi gan nhat (NPC co ten, nho chuyen)");
        Info("E: ngoi ghe quan / vao-ra nha / dung giuong, tu lanh   U: bat-tat bang kho");
        Info("F8: thoat ket canh quan   F9: xoay cho dung nhan vien");
        Info(payKey + ": boi thuong tai cho khi dam xe nguoi dan (trong 15 giay)");
        Info("Dang goi dien (Dien thoai > Danh ba > Goi): giu N la noi vao dien thoai");
        Info("Trong menu: dau , va dau . chon, Enter hoac so 1-9 chay, Backspace quay lai");
        Info("Doi phim: GtaVietLifePersonal.ini (Personal, QuickEat), GtaVietLife.ini (Talk, Remember)");
        Item("Quay lai", delegate { ShowMain(); });
    }

    void CloseMenu() { menuOpen = false; BusSet("GVL.PersonalMenu", false); }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == talkKey && talkDownAt == 0) talkDownAt = Game.GameTime;
        if (e.KeyCode == worldMenuKey && menuOpen) { CloseMenu(); return; }
        if (WorldMenuOpen() || EconMenuOpen()) return;
        if (e.KeyCode == quickKey && !menuOpen) { pending.Enqueue(QuickEat); return; }
        if (e.KeyCode == payKey && crashOffer > 0 && !menuOpen)
        {
            int amt = crashOffer; crashOffer = 0;
            pending.Enqueue(delegate
            {
                if (Pay(amt, "court", "boi thuong va cham tai cho")) { Notify("Da boi thuong $" + amt + ". Hai ben vui ve."); Emit("legal.fine", "amount=" + amt + ";reason=boi thuong va cham;paid=1"); }
                else { suitAmount += amt * 2 + 150; suitDue = GameDay() + 1; suitVictim = "chu xe bi dam"; Notify("~r~Khong du tien~s~, nguoi ta kien ra toa: $" + suitAmount + "."); }
                Save();
            });
            return;
        }
        if (e.KeyCode == menuKey)
        {
            if (!menuOpen) { menuOpen = true; BusSet("GVL.PersonalMenu", true); ShowMain(); }
            else if (menuBack != null) menuBack();
            else CloseMenu();
            return;
        }
        if (!menuOpen) return;
        if (e.KeyCode == Keys.Back) { if (menuBack != null) menuBack(); else CloseMenu(); return; }
        if (menuSel < 0 || menuSel >= menuActions.Count || menuActions[menuSel] == null) menuSel = NextActionable(-1, 1);
        if (e.KeyCode == navUp) { menuSel = NextActionable(menuSel, -1); return; }
        if (e.KeyCode == navDown) { menuSel = NextActionable(menuSel, 1); return; }
        if (e.KeyCode == navOk) { if (menuSel >= 0 && menuSel < menuActions.Count && menuActions[menuSel] != null) pending.Enqueue(menuActions[menuSel]); return; }
        int num = -1;
        if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) num = (int)e.KeyCode - (int)Keys.D1;
        else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) num = (int)e.KeyCode - (int)Keys.NumPad1;
        if (num >= 0)
        {
            int seen = 0;
            for (int ai = 0; ai < menuActions.Count; ai++)
            {
                if (menuActions[ai] == null) continue;
                if (seen == num) { menuSel = ai; pending.Enqueue(menuActions[ai]); break; }
                seen++;
            }
        }
    }

    void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != talkKey || talkDownAt == 0) return;
        int held = Game.GameTime - talkDownAt;
        talkDownAt = 0;
        if (held < 800) return;
        Ped me = Game.Player.Character;
        foreach (Ped p in World.GetNearbyPeds(me, 5f))
            if (p != null && p.Exists() && p != me && p.IsHuman && p.IsAlive) { Gain(SkTalk, 0.6f); break; }
    }

    // ------------------------------------------------------------ vẽ menu (cung kieu menu Doi song)
    void DrawText(string s, float x, float y, float scale, Color c)
    {
        GTA.UI.TextElement t = new GTA.UI.TextElement(s, new PointF(x, y), scale, c);
        t.Outline = true;
        t.Draw();
    }

    void DrawMenu()
    {
        if (menuSel < 0 || menuSel >= menuActions.Count || menuActions[menuSel] == null) menuSel = NextActionable(-1, 1);
        float x = 660f, y = 150f, w = 580f, pad = 14f;
        int total = menuLabels.Count;
        const int maxRows = 11;
        int first = 0;
        if (total > maxRows) first = Math.Max(0, Math.Min(total - maxRows, menuSel - maxRows / 2));
        int last = Math.Min(total, first + maxRows);
        float h = 52f;
        for (int i = first; i < last; i++) h += menuLabels[i].IndexOf('|') >= 0 ? 46f : 30f;
        h += 30f;
        Color accent = Color.FromArgb(255, 110, 190, 255);
        new GTA.UI.ContainerElement(new PointF(x - 2f, y - 2f), new SizeF(w + 4f, h + 4f), Color.FromArgb(230, 50, 120, 200)).Draw();
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, h), Color.FromArgb(235, 16, 18, 24)).Draw();
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, 40f), Color.FromArgb(240, 30, 40, 58)).Draw();
        DrawText(menuTitle, x + pad, y + 5f, 0.5f, accent);
        float cy = y + 48f;
        int shownNum = 0;
        for (int i = 0; i < first; i++) if (menuActions[i] != null) shownNum++;
        for (int i = first; i < last; i++)
        {
            string lab = menuLabels[i];
            int bar = lab.IndexOf('|');
            string m = bar >= 0 ? lab.Substring(0, bar) : lab;
            string d = bar >= 0 ? lab.Substring(bar + 1) : "";
            float rh = d.Length > 0 ? 46f : 30f;
            bool act = menuActions[i] != null;
            bool sel = i == menuSel;
            if (sel) new GTA.UI.ContainerElement(new PointF(x + 6f, cy - 2f), new SizeF(w - 12f, rh - 2f), Color.FromArgb(235, 70, 150, 230)).Draw();
            Color mc = sel ? Color.FromArgb(255, 10, 16, 28) : (act ? Color.White : Color.FromArgb(255, 150, 156, 170));
            Color dc = sel ? Color.FromArgb(255, 20, 40, 70) : Color.FromArgb(255, 150, 156, 170);
            string prefix = act ? (shownNum + 1 <= 9 ? (shownNum + 1) + "  " : "    ") : "    ";
            if (act) shownNum++;
            DrawText(prefix + m, x + pad, cy, 0.4f, mc);
            if (d.Length > 0) DrawText(d, x + pad + 26f, cy + 22f, 0.31f, dc);
            cy += rh;
        }
        if (total > maxRows) DrawText((first > 0 ? "^ " : "  ") + "(" + (menuSel + 1) + "/" + total + ")" + (last < total ? " v" : ""), x + w - 110f, y + 8f, 0.32f, Color.FromArgb(255, 180, 184, 196));
        DrawText("dau , va dau . : chon  -  Enter/so: chay  -  Backspace: quay lai  -  " + menuKey + ": dong", x + pad, y + h - 26f, 0.29f, Color.FromArgb(255, 150, 156, 170));
    }

    // ------------------------------------------------------------ lưu / nạp
    void Save()
    {
        try
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<string> l = new List<string>();
            l.Add("name=" + playerName.Replace("\n", " "));
            l.Add("hometown=" + hometown);
            for (int i = 0; i < skill.Length; i++) l.Add("skill" + i + "=" + skill[i].ToString(ci));
            foreach (KeyValuePair<string, int> kv in bag) l.Add("item=" + kv.Key + "," + kv.Value);
            l.Add("backpack=" + (hasBackpack ? 1 : 0));
            for (int i = 0; i < outfit.Length; i++) if (!string.IsNullOrEmpty(outfit[i])) l.Add("outfit" + i + "=" + outfit[i]);
            l.Add("wearing=" + wearing);
            foreach (CarRec c in garage) l.Add("car=" + c.Hash + "," + c.Prim + "," + c.Sec + "," + c.Plate.Replace(",", "") + "," + c.Label.Replace(",", "") + "," + (c.Impounded ? (c.InEconPound ? 2 : 1) : 0));
            l.Add("day=" + dayNo);
            l.Add("age=" + age);
            l.Add("kg=" + kg.ToString(ci));
            l.Add("muscle=" + muscle.ToString(ci));
            l.Add("hairDays=" + hairDays);
            l.Add("beardDays=" + beardDays);
            l.Add("trainDay=" + lastTrainDay);
            l.Add("lastDay=" + lastDayHandled);
            l.Add("lic=" + string.Join(",", new List<string>(lic).ToArray()));
            l.Add("record=" + record);
            l.Add("jailUntil=" + jailUntil);
            l.Add("arrestStars=" + arrestStars);
            l.Add("suit=" + suitAmount + "," + suitDue);
            l.Add("spouse=" + spouse);
            l.Add("pet=" + petModel + "," + petName.Replace(",", " "));
            l.Add("custom=" + (customOn ? 1 : 0) + "," + prevModelHash + "," + shapeA + "," + shapeB + "," + skinA + "," + skinB + "," + shapeMix.ToString(ci) + "," + skinMix.ToString(ci) + "," + hairStyle + "," + hairColor + "," + hairHi + "," + eyeColor + "," + browStyle + "," + beardStyle + "," + beardMax.ToString(ci));
            l.Add("face=" + string.Join(",", Array.ConvertAll(face, x => x.ToString(ci))));
            l.Add("in=" + inToday);
            l.Add("out=" + outToday);
            foreach (int[] h in moneyHist) l.Add("mhist=" + h[0] + "," + h[1] + "," + h[2]);
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
                    if (k == "name") playerName = v;
                    else if (k == "hometown") hometown = v;
                    else if (k.StartsWith("skill") && k.Length == 6) { int s = k[5] - '0'; if (s >= 0 && s < skill.Length) { skill[s] = Math.Max(0f, Math.Min(100f, float.Parse(v, ci))); skillShown[s] = (int)(skill[s] / 10f); } }
                    else if (k == "item") { string[] q = v.Split(','); if (Def(q[0]) != null) bag[q[0]] = int.Parse(q[1]); }
                    else if (k == "backpack") hasBackpack = v == "1";
                    else if (k.StartsWith("outfit") && k.Length == 7) { int s = k[6] - '0'; if (s >= 0 && s < outfit.Length) outfit[s] = v; }
                    else if (k == "wearing") wearing = int.Parse(v);
                    else if (k == "car") { string[] q = v.Split(','); CarRec c = new CarRec(); c.Hash = int.Parse(q[0]); c.Prim = int.Parse(q[1]); c.Sec = int.Parse(q[2]); c.Plate = q.Length > 3 ? q[3] : ""; c.Label = q.Length > 4 ? q[4] : "Xe"; c.Impounded = q.Length > 5 && (q[5] == "1" || q[5] == "2"); c.InEconPound = q.Length > 5 && q[5] == "2"; garage.Add(c); }
                    else if (k == "day") dayNo = int.Parse(v);
                    else if (k == "age") age = int.Parse(v);
                    else if (k == "kg") kg = float.Parse(v, ci);
                    else if (k == "muscle") muscle = float.Parse(v, ci);
                    else if (k == "hairDays") hairDays = int.Parse(v);
                    else if (k == "beardDays") beardDays = int.Parse(v);
                    else if (k == "trainDay") lastTrainDay = long.Parse(v);
                    else if (k == "lastDay") lastDayHandled = long.Parse(v);
                    else if (k == "lic") { foreach (string x in v.Split(',')) if (x.Length > 0) lic.Add(x); }
                    else if (k == "record") record = int.Parse(v);
                    else if (k == "jailUntil") jailUntil = long.Parse(v);
                    else if (k == "arrestStars") arrestStars = int.Parse(v);
                    else if (k == "suit") { string[] q = v.Split(','); suitAmount = int.Parse(q[0]); suitDue = long.Parse(q[1]); }
                    else if (k == "spouse") spouse = v;
                    else if (k == "custom")
                    {
                        string[] q = v.Split(',');
                        customOn = q[0] == "1"; prevModelHash = int.Parse(q[1]); shapeA = int.Parse(q[2]); shapeB = int.Parse(q[3]); skinA = int.Parse(q[4]); skinB = int.Parse(q[5]);
                        shapeMix = float.Parse(q[6], ci); skinMix = float.Parse(q[7], ci); hairStyle = int.Parse(q[8]); hairColor = int.Parse(q[9]); hairHi = int.Parse(q[10]);
                        eyeColor = int.Parse(q[11]); browStyle = int.Parse(q[12]); beardStyle = int.Parse(q[13]); beardMax = float.Parse(q[14], ci);
                    }
                    else if (k == "face") { string[] q = v.Split(','); for (int fi = 0; fi < face.Length && fi < q.Length; fi++) face[fi] = float.Parse(q[fi], ci); }
                    else if (k == "pet") { string[] q = v.Split(','); petModel = int.Parse(q[0]); petName = q.Length > 1 ? q[1] : ""; }
                    else if (k == "in") inToday = int.Parse(v);
                    else if (k == "out") outToday = int.Parse(v);
                    else if (k == "mhist") { string[] q = v.Split(','); moneyHist.Add(new int[] { int.Parse(q[0]), int.Parse(q[1]), int.Parse(q[2]) }); }
                }
                catch (Exception ex) { Log("load dong loi: " + line + " - " + ex.Message); }
            }
        }
        catch (Exception ex) { Log("load loi: " + ex.Message); }
    }

    // ------------------------------------------------------------ tiện ích
    void Notify(string msg) { GTA.UI.Notification.Show(msg); }

    static void Log(string msg)
    {
        try { File.AppendAllText("GtaVietLifePersonal.log", DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine); }
        catch { }
    }

    static string Short(Exception ex)
    {
        string m = ex.Message ?? ex.GetType().Name;
        return m.Length > 90 ? m.Substring(0, 90) : m;
    }
}
