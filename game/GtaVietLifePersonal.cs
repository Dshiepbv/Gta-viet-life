// GTA Việt Life: lớp "Cá nhân" (hồ sơ nhân vật, kỹ năng, túi đồ, trang phục, garage xe riêng, sổ thu chi, bảng phím).
// File độc lập: chép vào thư mục "scripts" cùng GtaVietLife.cs và GtaVietLifeWorld.cs. Lỗi ở đây không làm hỏng hai file kia.
//
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
        new ItemDef("bangcuu", "Bang cuu thuong", 25, 0f, 0f, 0f, 40)
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
    class CarRec { public int Hash; public int Prim, Sec; public string Plate = ""; public string Label = ""; }
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
    int lastShotGain;
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

        Load();
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
        BusSet("GVL.DrainMul", 1f - 0.30f * skill[SkStam] / 100f);
        BusSet("GVL.PersonalMenu", menuOpen);
        if (menuOpen && WorldMenuOpen()) menuOpen = false;

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
        if (Game.Player.Money < cost) { Notify("Khong du tien ($" + cost + ")."); return; }
        Game.Player.Money = Game.Player.Money - cost;
        bag[d.Id] = Have(d.Id) + qty;
        Notify("Da mua " + qty + " " + d.Name + " (-$" + cost + ", gom ship $" + ShipFee + "). Tui: " + BagCount() + "/" + BagCap);
        Save();
    }

    void Use(ItemDef d)
    {
        if (Have(d.Id) <= 0) { Notify("Het " + d.Name + "."); return; }
        Ped me = Game.Player.Character;
        if (d.Health > 0 && d.Hunger == 0f && d.Thirst == 0f && me.Health >= me.MaxHealth) { Notify("Mau dang day, chua can bang."); return; }
        bag[d.Id] = Have(d.Id) - 1;
        if (bag[d.Id] <= 0) bag.Remove(d.Id);
        if (d.Hunger != 0f || d.Thirst != 0f || d.Energy != 0f) AddNeeds(d.Hunger, d.Thirst, d.Energy);
        if (d.Health > 0) me.Health = Math.Min(me.MaxHealth, me.Health + d.Health);
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
        if (calledCar != null && calledCar.Exists() && calledIdx == idx && calledCar.IsAlive && calledCar.Position.DistanceTo(me.Position) < 150f)
        { Notify("Xe dang o gan day (cham xanh)."); return; }
        if (Game.Player.Money < CallCarFee) { Notify("Khong du $" + CallCarFee + " phi giao xe."); return; }
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
        Game.Player.Money = Game.Player.Money - CallCarFee;
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
        if (c.Count == 0) return;
        Task.Run(delegate
        {
            try { using (WebClient wc = new WebClient()) { wc.Proxy = null; wc.Encoding = Encoding.UTF8; wc.UploadValues(server + "/state", "POST", c); } }
            catch { }
        });
    }

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
        Item("Ky nang...  (" + SkillSummary() + ")", delegate { ShowSkills(); });
        Item("Tui do...  (" + BagCount() + "/" + BagCap + " mon)", delegate { ShowBag(); });
        Item("Trang phuc...  (" + (wearing >= 0 ? "dang mac: " + OutfitSlotName[wearing] : "chua luu bo nao") + ")", delegate { ShowOutfits(); });
        Item("Garage xe rieng...  (" + garage.Count + "/" + MaxGarage + " xe)", delegate { ShowGarage(); });
        Item("So thu chi ca nhan...", delegate { ShowLedger(); });
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
            Item("Dung " + d.Name + "  x" + c + "|" + Effect(d), delegate { Use(dd); ShowBag(); });
        }
        if (!any) Info("(Tui trong)");
        Item("Mua do (ship tan noi, +$" + ShipFee + "/don)...", delegate { ShowShop(); });
        if (!hasBackpack) Item("Mua ba lo  (-$" + BackpackPrice + ", tui len " + BagBig + " mon)", delegate
        {
            if (Game.Player.Money < BackpackPrice) Notify("Khong du tien ($" + BackpackPrice + ").");
            else { Game.Player.Money = Game.Player.Money - BackpackPrice; hasBackpack = true; Notify("Da co ba lo: tui chua " + BagBig + " mon."); Save(); }
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
        Info("Trong menu: dau , va dau . chon, Enter hoac so 1-9 chay, Backspace quay lai");
        Info("Doi phim: GtaVietLifePersonal.ini (Personal, QuickEat), GtaVietLife.ini (Talk, Remember)");
        Item("Quay lai", delegate { ShowMain(); });
    }

    void CloseMenu() { menuOpen = false; BusSet("GVL.PersonalMenu", false); }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == talkKey && talkDownAt == 0) talkDownAt = Game.GameTime;
        if (e.KeyCode == worldMenuKey && menuOpen) { CloseMenu(); return; }
        if (WorldMenuOpen()) return;
        if (e.KeyCode == quickKey && !menuOpen) { pending.Enqueue(QuickEat); return; }
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
            foreach (CarRec c in garage) l.Add("car=" + c.Hash + "," + c.Prim + "," + c.Sec + "," + c.Plate.Replace(",", "") + "," + c.Label.Replace(",", ""));
            l.Add("day=" + dayNo);
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
                    else if (k == "car") { string[] q = v.Split(','); CarRec c = new CarRec(); c.Hash = int.Parse(q[0]); c.Prim = int.Parse(q[1]); c.Sec = int.Parse(q[2]); c.Plate = q.Length > 3 ? q[3] : ""; c.Label = q.Length > 4 ? q[4] : "Xe"; garage.Add(c); }
                    else if (k == "day") dayNo = int.Parse(v);
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
