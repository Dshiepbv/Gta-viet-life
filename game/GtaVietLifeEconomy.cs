// GTA Việt Life: Kinh tế (Trụ 3 + Trụ 6 của brief "Second Life").
//   - Ngân hàng: tài khoản tách khỏi tiền mặt, gửi/rút ở ATM hoặc chi nhánh, lãi tiết kiệm, sổ cái thu chi.
//   - Vay: tín chấp / thế chấp (tài sản BĐS từ file Đời sống), trả góp mỗi 7 ngày game, điểm tín dụng, siết nợ.
//   - Xe: xăng (hết xăng chết máy), nhớt, hỏng máy, gọi cứu hộ, kéo về gara, đỗ sai bị cẩu về bãi giữ xe.
// File độc lập: chép vào "scripts" cạnh các file khác. Mỗi module bật/tắt trong GtaVietLifeEconomy.ini.
// Chữ trong game viết không dấu (font GTA không có dấu).
//
// BUS (AppDomain data, theo docs/CORE.md của lõi GtaVietLifeCore.cs):
//   GVL.Pay  Func<int,string,string,bool>  file này ĐĂNG KÝ ĐÈ bản của lõi: trừ tài khoản trước, thiếu thì tiền mặt, ghi sổ cái.
//   GVL.Earn Action<int,string,string>     file này đăng ký: tiền vào tài khoản, ghi sổ cái.
//   Sự kiện phát ra: ledger.expense / ledger.income, econ.loan, econ.repay, econ.late, econ.foreclose, econ.impound, econ.release, econ.fuel, econ.repair.
//   GVL.Bank     (int)            số dư tài khoản, cập nhật mỗi tick.
//   GVL.Credit   (int)            điểm tín dụng 300..850.
//   GVL.EconMenu (bool)           menu Kinh tế đang mở.
//   Đọc: GVL.Assets (int, tổng giá trị BĐS do file Đời sống công bố), GVL.Record (int tiền án: >0 thì không cho vay),
//        GVL.Jailed (bool), GVL.Mod.Economy (bool, tắt cả file). Ghi: GVL.Seize (int, số tiền cần phát mãi từ BĐS thế chấp).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeEconomy : Script
{
    const string SaveFile = "GtaVietLifeEconomy.sav";
    const double GameDaySeconds = 2880.0;   // 1 ngày game = 48 phút thực (giống file Đời sống)
    const int MaxOfflineDays = 14;

    Keys menuKey = Keys.L;
    Keys navUp = Keys.Oemcomma, navDown = Keys.OemPeriod, navOk = Keys.Return;
    bool modBank = true, modFuel = true, modWear = true, modImpound = true;
    float fuelPrice = 2f, fuelMul = 1f;

    // ------------------------------------------------------------ sổ cái
    int bank;
    int bankDebt;          // nợ ngân hàng phát sinh (khoản phải trả mà không đủ tiền)
    int credit = 600;
    int dayNo;
    readonly List<string> ledger = new List<string>();   // "ngay|tk|sotien|nhom|ghichu"

    // ------------------------------------------------------------ vay
    class Loan { public int Kind; public int Principal, Left, Periods, Due, Late; public float Rate; }   // Kind 0 tín chấp, 1 thế chấp
    readonly List<Loan> loans = new List<Loan>();
    int badUntil;          // ngày game hết bị cấm vay (nợ xấu)

    // ------------------------------------------------------------ xe
    class Car { public float Fuel = -1f, Cap = 50f, Oil = 100f, Km; public string Key = ""; }
    readonly Dictionary<string, Car> cars = new Dictionary<string, Car>();
    Vehicle curVeh; Car cur; Vector3 lastVehPos; int nextVehTick, nextPumpCheck, nextStall, stallUntil;
    bool nearPump, nearGarage, nearBank, nearAtm, nearImpound;
    int cans;              // bình xăng dự phòng 10 L
    int rescueAt;          // thời điểm xe cứu hộ tới (đổ xăng)
    int lastWarnOil, lastWarnFuel;

    // xe bị cẩu
    class Impounded { public int Hash, Prim, Sec; public string Plate = "", Key = ""; public int Day; }
    readonly List<Impounded> impounded = new List<Impounded>();
    Vehicle parkedVeh; int parkedSince; bool parkedIllegal;

    static readonly string[] PumpModels = { "prop_gas_pump_1a", "prop_gas_pump_1b", "prop_gas_pump_1c", "prop_gas_pump_1d", "prop_gas_pump_old2", "prop_gas_pump_old3", "prop_vintage_pump" };
    static readonly string[] AtmModels = { "prop_atm_01", "prop_atm_02", "prop_atm_03", "prop_fleeca_atm" };
    // toa do gan dung (doi chieu cham tren ban do), chua kiem chung tung diem trong game
    static readonly Vector3[] Banks = {
        new Vector3(150.3f, -1040.2f, 29.4f), new Vector3(-1213.0f, -330.8f, 37.8f), new Vector3(-2962.6f, 482.6f, 15.7f),
        new Vector3(314.2f, -278.6f, 54.2f), new Vector3(-351.5f, -49.5f, 49.0f), new Vector3(1175.1f, 2706.6f, 38.1f),
        new Vector3(-112.2f, 6469.3f, 31.6f), new Vector3(241.7f, 225.3f, 106.3f) };
    static readonly Vector3[] Garages = {
        new Vector3(-365.4f, -131.8f, 38.7f), new Vector3(731.8f, -1088.8f, 22.2f), new Vector3(-1155.0f, -2007.0f, 13.2f),
        new Vector3(1175.0f, 2640.0f, 37.8f), new Vector3(110.0f, 6626.0f, 31.8f) };
    static readonly Vector3 ImpoundLot = new Vector3(409.5f, -1623.0f, 29.3f);
    const int ImpoundFee = 250, ImpoundPerDay = 25, TowFee = 200, RescueFee = 120, CanPrice = 30, OilChange = 40;

    long lastSeen; bool caughtUp; int lastDayMinute = -1;
    readonly List<Blip> blips = new List<Blip>();
    readonly Queue<Action> pending = new Queue<Action>();
    readonly Random rnd = new Random();

    // menu
    bool menuOpen; string menuTitle = ""; List<string> menuLabels = new List<string>(); List<Action> menuActions = new List<Action>(); Action menuBack; int menuSel = -1;

    public GtaVietLifeEconomy()
    {
        menuKey = Settings.GetValue("Keys", "Economy", menuKey);
        navUp = Settings.GetValue("Keys", "Up", navUp);
        navDown = Settings.GetValue("Keys", "Down", navDown);
        navOk = Settings.GetValue("Keys", "Ok", navOk);
        modBank = Settings.GetValue("Modules", "Bank", 1) == 1;
        modFuel = Settings.GetValue("Modules", "Fuel", 1) == 1;
        modWear = Settings.GetValue("Modules", "Wear", 1) == 1;
        modImpound = Settings.GetValue("Modules", "Impound", 1) == 1;
        fuelPrice = ReadF("Fuel", "PricePerLiter", fuelPrice);
        fuelMul = ReadF("Fuel", "ConsumeMul", fuelMul);
        Load();
        payFn = BusPay; earnFn = BusEarn; payCashFn = BusPayCash; earnCashFn = BusEarnCash;
        try
        {
            if (modBank) foreach (Vector3 b in Banks) AddBlip(b, BlipColor.Green, "Ngan hang (gui/rut, vay)");
            if (modImpound) AddBlip(ImpoundLot, BlipColor.Red, "Bai giu xe (lay xe bi cau)");
        }
        catch (Exception ex) { Log("blip loi: " + ex.Message); }
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
        Notify("~g~GTA Viet Life: Kinh te~s~ da bat. Bam ~y~" + menuKey + "~s~: ngan hang, vay, xe.");
    }

    void AddBlip(Vector3 p, BlipColor c, string name)
    {
        Blip b = World.CreateBlip(p); b.Color = c; b.Name = name; b.IsShortRange = true; blips.Add(b);
    }

    // ------------------------------------------------------------ bus
    static object Bus(string k) { try { return AppDomain.CurrentDomain.GetData(k); } catch { return null; } }
    static void BusSet(string k, object v) { try { AppDomain.CurrentDomain.SetData(k, v); } catch { } }
    static bool BusFlag(string k) { object o = Bus(k); return o is bool && (bool)o; }

    // cong tra tien chung cho moi file (dang ky de len ban mac dinh cua loi)
    bool BusPay(int amount, string cat, string note)
    {
        if (amount <= 0) return true;
        if (!ModOn()) { if (Game.Player.Money < amount) return false; Game.Player.Money = Game.Player.Money - amount; return true; }
        if (Game.Player.Money + Math.Max(0, modBank ? bank : 0) < amount) return false;
        Charge(amount, cat, note, true);
        return true;
    }
    void BusEarn(int amount, string cat, string note)
    {
        if (amount <= 0) return;
        if (modBank && ModOn()) Deposit(amount, cat, note); else Cash(amount, cat, note);
    }
    bool BusPayCash(int amount, string cat, string note)
    {
        if (amount <= 0) return true;
        if (Game.Player.Money < amount) return false;
        Game.Player.Money = Game.Player.Money - amount;
        if (ModOn()) Post("cash", -amount, cat, note);
        return true;
    }
    void BusEarnCash(int amount, string cat, string note)
    {
        if (amount <= 0) return;
        if (ModOn()) Cash(amount, cat, note); else Game.Player.Money = Game.Player.Money + amount;
    }
    static bool ModOn() { object o = Bus("GVL.Mod.Economy"); return !(o is bool) || (bool)o; }
    void RegisterPay()
    {
        object cur = Bus("GVL.Pay");
        if (cur != null && Bus("GVL.PayOwner") as string == "economy" && cur == payFn) return;
        BusSet("GVL.Pay", payFn); BusSet("GVL.Earn", earnFn);
        BusSet("GVL.PayCash", payCashFn); BusSet("GVL.EarnCash", earnCashFn); BusSet("GVL.PayOwner", "economy");
    }
    Func<int, string, string, bool> payFn, payCashFn; Action<int, string, string> earnFn, earnCashFn;

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
    static string Clean(string s) { return (s ?? "").Replace(";", ",").Replace("\t", " ").Replace("=", "-"); }

    // ghi so cai (moi khoan tien cua file nay deu qua day)
    void Post(string acct, int amt, string cat, string note)
    {
        ledger.Add(dayNo + "|" + acct + "|" + amt + "|" + cat + "|" + note.Replace("|", "/"));
        while (ledger.Count > 200) ledger.RemoveAt(0);
        if (acct != "no") Emit(amt >= 0 ? "ledger.income" : "ledger.expense", "amount=" + Math.Abs(amt) + ";cat=" + Clean(cat) + ";note=" + Clean(note) + ";acct=" + acct);
    }
    void Cash(int amt, string cat, string note) { Game.Player.Money = Game.Player.Money + amt; Post("cash", amt, cat, note); }
    void Deposit(int amt, string cat, string note) { bank += amt; Post("bank", amt, cat, note); }

    // tra mot khoan: uu tien tai khoan (neu preferBank), roi tien mat, con thieu thi ghi no ngan hang. Tra ve so da tra ngay.
    int Charge(int amt, string cat, string note, bool preferBank)
    {
        int left = amt;
        if (preferBank && modBank) { int b = Math.Min(Math.Max(0, bank), left); if (b > 0) { bank -= b; left -= b; Post("bank", -b, cat, note); } }
        int c = Math.Min(Game.Player.Money, left);
        if (c > 0) { Game.Player.Money = Game.Player.Money - c; left -= c; Post("cash", -c, cat, note); }
        if (!preferBank && left > 0 && modBank) { int b = Math.Min(Math.Max(0, bank), left); if (b > 0) { bank -= b; left -= b; Post("bank", -b, cat, note); } }
        if (left > 0) { bankDebt += left; Post("no", -left, cat, note + " (ghi no)"); Notify("~r~Khong du tien " + note + "~s~, ghi no ngan hang $" + left + "."); credit = Math.Max(300, credit - 5); }
        return amt - left;
    }

    // tra bang tien mat tai quay (xang, cuu ho...): thieu thi tu dong quet the (tai khoan) neu bat ngan hang
    bool PayAtCounter(int amt, string cat, string note)
    {
        if (Game.Player.Money >= amt) { Game.Player.Money = Game.Player.Money - amt; Post("cash", -amt, cat, note); return true; }
        if (modBank && bank + Game.Player.Money >= amt)
        {
            int c = Game.Player.Money; if (c > 0) { Game.Player.Money = 0; Post("cash", -c, cat, note); }
            bank -= amt - c; Post("bank", -(amt - c), cat, note + " (quet the)");
            return true;
        }
        Notify("~r~Khong du tien~s~ (can $" + amt + ")."); return false;
    }

    // ------------------------------------------------------------ vòng lặp
    void OnTick(object sender, EventArgs e)
    {
        Ped me = Game.Player.Character;
        if (me == null || !me.Exists()) return;
        int now = Game.GameTime;
        while (pending.Count > 0) { Action a = pending.Dequeue(); try { a(); } catch (Exception ex) { Log("viec cho loi: " + ex); } }
        if (!ModOn()) { if (menuOpen) CloseMenu(); return; }
        RegisterPay();
        if (!caughtUp) CatchUp();
        UpdateDay();
        try { if (modFuel || modWear) UpdateVehicle(me, now); } catch (Exception ex) { Log("xe loi: " + ex.Message); }
        try { if (modImpound) UpdateParking(me, now); } catch (Exception ex) { Log("cau xe loi: " + ex.Message); }
        if (now > nextPumpCheck) { nextPumpCheck = now + 1000; ScanPlaces(me); }
        if (rescueAt > 0 && now > rescueAt) { rescueAt = 0; FinishRescue(); }
        BusSet("GVL.Bank", bank);
        BusSet("GVL.Credit", credit);
        if (now > nextSave) { nextSave = now + 60000; Save(); }
        if (BusFlag("GVL.WorldMenu") || BusFlag("GVL.PersonalMenu")) { if (menuOpen) CloseMenu(); }
        DrawHud(me);
        if (menuOpen) DrawMenu();
    }
    int nextSave;

    void OnAborted(object sender, EventArgs e)
    {
        Save();
        foreach (Blip b in blips) if (b != null && b.Exists()) b.Delete();
        BusSet("GVL.EconMenu", false);
    }

    // ngay moi theo dong ho game: lai tiet kiem, tra gop, diem tin dung
    void UpdateDay()
    {
        TimeSpan t = World.CurrentTimeOfDay;
        int m = t.Hours * 60 + t.Minutes;
        if (lastDayMinute >= 0 && m < lastDayMinute && lastDayMinute - m > 600) DailyTick(false);
        lastDayMinute = m;
    }

    string DailyTick(bool offline)
    {
        dayNo++;
        string r = "";
        if (modBank && bank > 0)
        {
            int it = (int)(bank * 0.0003f);
            if (it > 0) { Deposit(it, "lai", "lai tiet kiem"); }
        }
        if (bankDebt > 0)
        {
            int fee = Math.Max(1, bankDebt / 50);   // 2%/ngay tren no phat sinh
            bankDebt += fee;
            int pay = Math.Min(bankDebt, Math.Max(0, bank));
            if (pay > 0) { bank -= pay; bankDebt -= pay; Post("bank", -pay, "no", "tu dong tru no ngan hang"); }
            if (bankDebt > 0) r += " No ngan hang con $" + bankDebt + ".";
        }
        for (int i = loans.Count - 1; i >= 0; i--)
        {
            Loan l = loans[i];
            l.Left += (int)Math.Ceiling(l.Left * l.Rate);   // lai tinh tren du no moi ngay
            if (dayNo < l.Due) continue;
            int inst = l.Periods <= 1 ? l.Left : (int)Math.Ceiling(l.Left / (double)l.Periods);
            int paid = 0;
            int b = Math.Min(Math.Max(0, bank), inst); if (b > 0) { bank -= b; paid += b; Post("bank", -b, "vay", "tra gop khoan vay"); }
            if (paid < inst && offline) { int c = Math.Min(Game.Player.Money, inst - paid); if (c > 0) { Game.Player.Money = Game.Player.Money - c; paid += c; Post("cash", -c, "vay", "tra gop (tien mat)"); } }
            l.Left -= paid;
            if (paid >= inst)
            {
                l.Periods--; l.Late = 0; l.Due = dayNo + 7; credit = Math.Min(850, credit + 8);
                r += " Tra gop $" + paid + ", con no $" + l.Left + ".";
                Emit("econ.repay", "amount=" + paid + ";left=" + l.Left);
                if (l.Left <= 0 || l.Periods <= 0) { loans.RemoveAt(i); credit = Math.Min(850, credit + 25); r += " ~g~Da tat toan khoan vay!~s~"; }
            }
            else
            {
                l.Late++; l.Due = dayNo + 1; credit = Math.Max(300, credit - 35);
                int pen = (int)(inst * 0.05f); l.Left += pen;
                r += " ~r~Tre han tra gop (" + l.Late + "/3), phat $" + pen + ".~s~";
                Emit("econ.late", "late=" + l.Late + ";penalty=" + pen);
                if (l.Late >= 3) { r += Foreclose(l); loans.RemoveAt(i); }
            }
        }
        if (bankDebt == 0 && loans.Count == 0 && credit < 650 && dayNo % 3 == 0) credit++;   // khong no thi diem hoi dan
        if (r.Length > 0 && !offline) Notify("~b~Ngan hang:~s~" + r);
        Save();
        return r;
    }

    // siet no: lay tai khoan, tien mat, roi yeu cau file Doi song ban tai san the chap; phan con lai thanh no xau
    string Foreclose(Loan l)
    {
        int need = l.Left;
        int b = Math.Min(Math.Max(0, bank), need); bank -= b; need -= b; if (b > 0) Post("bank", -b, "siet no", "ngan hang siet tai khoan");
        int c = Math.Min(Game.Player.Money, need); Game.Player.Money = Game.Player.Money - c; need -= c; if (c > 0) Post("cash", -c, "siet no", "siet tien mat");
        if (need > 0 && l.Kind == 1) { BusSet("GVL.Seize", need + (Bus("GVL.Seize") is int ? (int)Bus("GVL.Seize") : 0)); Post("no", -need, "siet no", "yeu cau phat mai tai san the chap"); need = 0; }
        if (need > 0) bankDebt += need;
        credit = 350; badUntil = dayNo + 30;
        Emit("econ.foreclose", "amount=" + l.Left + ";kind=" + (l.Kind == 1 ? "thechap" : "tinchap"));
        return " ~r~Qua 3 ky khong tra: ngan hang siet no!~s~" + (need > 0 ? " Con no xau $" + need + "." : "") + " Diem tin dung 350, cam vay 30 ngay.";
    }

    void CatchUp()
    {
        caughtUp = true;
        if (lastSeen <= 0) return;
        long el = NowEpoch() - lastSeen;
        int days = (int)Math.Min(MaxOfflineDays, el / GameDaySeconds);
        if (days < 1) return;
        int b0 = bank, d0 = bankDebt + Sum();
        string all = "";
        for (int i = 0; i < days; i++) all += DailyTick(true);
        Log("[offline] " + days + " ngay:" + all);
        Notify("~b~Ngan hang luc Sep tat may~s~ (" + days + " ngay game): tai khoan " + (bank >= b0 ? "+$" : "-$") + Math.Abs(bank - b0) + ", du no $" + (bankDebt + Sum()) + (bankDebt + Sum() > d0 ? " (tang)" : "") + ".");
    }
    int Sum() { int s = 0; foreach (Loan l in loans) s += l.Left; return s; }
    static long NowEpoch() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }

    // ------------------------------------------------------------ dia diem gan: ATM, ngan hang, cay xang, gara, bai giu xe
    void ScanPlaces(Ped me)
    {
        Vector3 p = me.Position;
        nearAtm = false; nearPump = false;
        try
        {
            if (modBank) nearAtm = World.GetNearbyProps(p, 2.5f, ToModels(AtmModels)).Length > 0;
            if (modFuel) nearPump = World.GetNearbyProps(p, 7f, ToModels(PumpModels)).Length > 0;
        }
        catch { }
        nearBank = false; foreach (Vector3 b in Banks) if (p.DistanceTo2D(b) < 12f) nearBank = true;
        nearGarage = false; foreach (Vector3 g in Garages) if (p.DistanceTo2D(g) < 30f) nearGarage = true;
        nearImpound = p.DistanceTo2D(ImpoundLot) < 30f;
    }
    static Model[] modelCache;
    static Model[] atmCache;
    static Model[] ToModels(string[] names)
    {
        if (names == PumpModels && modelCache != null) return modelCache;
        if (names == AtmModels && atmCache != null) return atmCache;
        Model[] m = new Model[names.Length];
        for (int i = 0; i < names.Length; i++) m[i] = new Model(names[i]);
        if (names == PumpModels) modelCache = m; else atmCache = m;
        return m;
    }

    // ------------------------------------------------------------ xe: xang, nhot, hong may
    static bool Motorized(Vehicle v)
    {
        try { Model m = v.Model; return (m.IsCar || m.IsBike || m.IsQuadBike) && !m.IsBicycle; } catch { return false; }
    }
    static string VehKey(Vehicle v)
    {
        string plate = "";
        try { plate = (v.Mods.LicensePlate ?? "").Trim(); } catch { }
        return v.Model.Hash + ":" + plate;
    }
    static void Spec(Vehicle v, out float cap, out float perKm)
    {
        VehicleClass c = VehicleClass.Sedans;
        try { c = v.ClassType; } catch { }
        switch (c)
        {
            case VehicleClass.Motorcycles: cap = 12f; perKm = 0.25f; break;
            case VehicleClass.Compacts: cap = 40f; perKm = 0.6f; break;
            case VehicleClass.SUVs: case VehicleClass.OffRoad: case VehicleClass.Vans: cap = 75f; perKm = 1.0f; break;
            case VehicleClass.Sports: case VehicleClass.Super: case VehicleClass.Muscle: cap = 60f; perKm = 1.2f; break;
            case VehicleClass.Industrial: case VehicleClass.Commercial: case VehicleClass.Utility: cap = 150f; perKm = 2.0f; break;
            default: cap = 50f; perKm = 0.8f; break;
        }
    }

    Car CarOf(Vehicle v)
    {
        string k = VehKey(v);
        Car c;
        if (!cars.TryGetValue(k, out c))
        {
            c = new Car { Key = k };
            float cap, pk; Spec(v, out cap, out pk);
            c.Cap = cap; c.Fuel = cap * (0.3f + (float)rnd.NextDouble() * 0.6f); c.Oil = 40f + (float)rnd.NextDouble() * 60f;
            cars[k] = c;
            while (cars.Count > 30) { foreach (string kk in cars.Keys) { cars.Remove(kk); break; } }
        }
        return c;
    }

    void UpdateVehicle(Ped me, int now)
    {
        Vehicle v = me.CurrentVehicle;
        bool driving = v != null && v.Exists() && v.Driver == me && Motorized(v);
        if (!driving) { curVeh = null; cur = null; return; }
        if (curVeh != v) { curVeh = v; cur = CarOf(v); lastVehPos = v.Position; parkedVeh = null; }
        if (now < nextVehTick) { if (cur.Fuel <= 0f || now < stallUntil) KillEngine(v); return; }
        nextVehTick = now + 500;
        float dist = v.Position.DistanceTo(lastVehPos);
        lastVehPos = v.Position;
        if (dist > 60f) dist = 0f;   // dich chuyen tuc thoi (teleport) khong tinh
        float km = dist / 1000f;
        cur.Km += km;
        if (modFuel)
        {
            float cap, pk; Spec(v, out cap, out pk);
            float speedK = 1f + Math.Max(0f, v.Speed - 25f) / 25f;   // chay qua 90 km/h ton xang hon
            float idle = v.IsEngineRunning ? 0.0004f : 0f;           // no may dung yen van ton chut it
            cur.Fuel = Math.Max(0f, cur.Fuel - (km * pk * speedK + idle) * fuelMul);
            if (cur.Fuel <= 0f) { KillEngine(v); if (now > lastWarnFuel) { lastWarnFuel = now + 20000; Notify("~r~Het xang!~s~ Bam " + menuKey + " de dung binh du phong hoac goi cuu ho."); } }
            else if (cur.Fuel < cur.Cap * 0.12f && now > lastWarnFuel) { lastWarnFuel = now + 90000; Notify("~y~Sap het xang~s~ (" + (int)cur.Fuel + " L). Ghe cay xang gan nhat."); }
        }
        if (modWear)
        {
            cur.Oil = Math.Max(0f, cur.Oil - km * 0.7f);   // ~140 km game thi can thay nhot
            if (cur.Oil < 1f && v.Speed > 3f) v.EngineHealth = Math.Max(50f, v.EngineHealth - 1.5f);
            if (cur.Oil < 15f && now > lastWarnOil) { lastWarnOil = now + 120000; Notify("~y~Nhot xe con " + (int)cur.Oil + "%~s~. Den gara thay nhot (bam " + menuKey + " khi o gara), het nhot se hong may."); }
            float eh = v.EngineHealth;
            if (eh < 100f) { KillEngine(v); if (now > lastWarnOil) { lastWarnOil = now + 30000; Notify("~r~Xe hong may, khong no duoc.~s~ Bam " + menuKey + " goi xe keo ve gara."); } }
            else if (eh < 300f && now > nextStall && v.Speed > 2f)
            {
                nextStall = now + 40000 + rnd.Next(0, 60000);
                stallUntil = now + 3000;
                Notify("~y~Xe chet may~s~ (may yeu, nen sua).");
            }
            if (now < stallUntil) KillEngine(v);
        }
    }

    static void KillEngine(Vehicle v)
    {
        try { Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, false, true, true); } catch { }
    }

    void Refuel(float liters)
    {
        Vehicle v = Game.Player.Character.CurrentVehicle ?? Game.Player.Character.LastVehicle;
        if (v == null || !v.Exists() || !Motorized(v)) { Notify("Khong co xe de do xang."); return; }
        Car c = CarOf(v);
        float add = Math.Min(liters, c.Cap - c.Fuel);
        if (add < 0.5f) { Notify("Binh da day."); return; }
        int cost = (int)Math.Ceiling(add * fuelPrice);
        if (!PayAtCounter(cost, "xang", "do " + (int)add + " L xang")) return;
        c.Fuel += add;
        Emit("econ.fuel", "liters=" + (int)add + ";cost=" + cost);
        Notify("~g~Da do " + (int)add + " L~s~ (-$" + cost + "). Binh: " + (int)c.Fuel + "/" + (int)c.Cap + " L.");
    }

    void UseCan()
    {
        Vehicle v = Game.Player.Character.CurrentVehicle ?? Game.Player.Character.LastVehicle;
        if (cans <= 0 || v == null || !v.Exists()) { Notify("Khong co binh xang hoac khong co xe."); return; }
        Car c = CarOf(v);
        cans--; c.Fuel = Math.Min(c.Cap, c.Fuel + 10f);
        Notify("Da do binh du phong +10 L. Con " + cans + " binh.");
    }

    void CallRescue()
    {
        if (rescueAt > 0) { Notify("Xe cuu ho dang toi."); return; }
        if (!PayAtCounter(RescueFee, "cuu ho", "goi cuu ho do xang")) return;
        rescueAt = Game.GameTime + 25000;
        Notify("Da goi ~y~cuu ho~s~ (-$" + RescueFee + "). Khoang 25 giay nua toi, se do 8 L.");
    }
    void FinishRescue()
    {
        Vehicle v = Game.Player.Character.CurrentVehicle ?? Game.Player.Character.LastVehicle;
        if (v == null || !v.Exists()) return;
        Car c = CarOf(v);
        c.Fuel = Math.Min(c.Cap, c.Fuel + 8f);
        Notify("~g~Cuu ho da toi~s~, do them 8 L xang.");
    }

    // keo xe ve gara gan nhat (toi man hinh, dua ca xe lan Sep toi gara)
    void TowToGarage()
    {
        Ped me = Game.Player.Character;
        Vehicle v = me.CurrentVehicle ?? me.LastVehicle;
        if (v == null || !v.Exists()) { Notify("Khong co xe de keo."); return; }
        Vector3 best = Garages[0]; float bd = 1e9f;
        foreach (Vector3 g in Garages) { float d = v.Position.DistanceTo2D(g); if (d < bd) { bd = d; best = g; } }
        int fee = TowFee + (int)(bd / 100f) * 5;
        if (!PayAtCounter(fee, "cuu ho", "keo xe ve gara")) return;
        GTA.UI.Screen.FadeOut(800); Wait(900);
        Vector3 spot = World.GetNextPositionOnStreet(best + new Vector3(6f, 6f, 0f));
        if (spot == Vector3.Zero) spot = best;
        v.Position = spot;
        if (!me.IsInVehicle(v)) me.Position = spot + new Vector3(2.5f, 0f, 0f);
        lastVehPos = v.Position;
        Wait(500); GTA.UI.Screen.FadeIn(800);
        Notify("~g~Xe da duoc keo ve gara~s~ (-$" + fee + "). Bam " + menuKey + " de sua / thay nhot.");
    }

    void RepairAtGarage()
    {
        Vehicle v = Game.Player.Character.CurrentVehicle ?? Game.Player.Character.LastVehicle;
        if (v == null || !v.Exists()) { Notify("Khong co xe."); return; }
        int cost = 30 + (int)((1000f - Math.Max(0f, v.EngineHealth)) * 0.6f);
        if (!PayAtCounter(cost, "sua xe", "sua may xe")) return;
        Function.Call(Hash.SET_VEHICLE_FIXED, v);
        v.EngineHealth = 1000f;
        Emit("econ.repair", "kind=engine;cost=" + cost);
        Notify("~g~Da sua may~s~ (-$" + cost + ").");
    }

    void ChangeOil()
    {
        Vehicle v = Game.Player.Character.CurrentVehicle ?? Game.Player.Character.LastVehicle;
        if (v == null || !v.Exists()) { Notify("Khong co xe."); return; }
        if (!PayAtCounter(OilChange, "sua xe", "thay nhot")) return;
        CarOf(v).Oil = 100f;
        Notify("~g~Da thay nhot~s~ (-$" + OilChange + ").");
    }

    // ------------------------------------------------------------ do sai cho -> bi cau ve bai giu xe
    void UpdateParking(Ped me, int now)
    {
        if (me.IsInVehicle())
        {
            Vehicle v = me.CurrentVehicle;
            if (v != null && v.Driver == me && Motorized(v)) { parkedVeh = v; parkedSince = 0; }
            return;
        }
        if (parkedVeh == null || !parkedVeh.Exists()) { parkedVeh = null; return; }
        if (parkedSince == 0)
        {
            parkedSince = now;
            Vector3 p = parkedVeh.Position;
            bool city = p.Y < 1200f;   // chi trong thanh pho
            bool onRoad = false;
            try { onRoad = Function.Call<bool>(Hash.IS_POINT_ON_ROAD, p.X, p.Y, p.Z, parkedVeh); } catch { }
            parkedIllegal = city && onRoad;
            if (parkedIllegal) Notify("~y~Xe dang do giua long duong~s~: di xa qua 3 phut se bi cau ve bai giu xe.");
            return;
        }
        if (!parkedIllegal) return;
        if (parkedVeh.Driver != null && parkedVeh.Driver.Exists()) { parkedVeh = null; return; }
        if (now - parkedSince < 180000 || me.Position.DistanceTo(parkedVeh.Position) < 80f) return;
        Impounded im = new Impounded { Hash = parkedVeh.Model.Hash, Day = dayNo, Key = VehKey(parkedVeh) };
        try { im.Prim = (int)parkedVeh.Mods.PrimaryColor; im.Sec = (int)parkedVeh.Mods.SecondaryColor; im.Plate = parkedVeh.Mods.LicensePlate ?? ""; } catch { }
        impounded.Add(im);
        while (impounded.Count > 5) impounded.RemoveAt(0);
        try { parkedVeh.Delete(); } catch { }
        parkedVeh = null;
        Notify("~r~Xe cua Sep bi cau~s~ ve bai giu xe Davis vi do sai cho. Phi $" + ImpoundFee + " + $" + ImpoundPerDay + "/ngay.");
        Log("xe bi cau: " + im.Key);
        Emit("econ.impound", "plate=" + Clean(im.Plate.Trim()) + ";fee=" + ImpoundFee);
        Save();
    }

    void ReleaseImpound(int i)
    {
        Impounded im = impounded[i];
        int fee = ImpoundFee + ImpoundPerDay * Math.Max(0, dayNo - im.Day);
        if (!PayAtCounter(fee, "impound", "phi bai giu xe")) return;
        Model m = new Model(im.Hash);
        if (!m.Request(3000)) { Notify("Khong nap duoc mau xe, thu lai."); Cash(fee, "hoan tien", "hoan phi giu xe"); return; }
        Vector3 spot = World.GetNextPositionOnStreet(ImpoundLot + new Vector3(10f, 0f, 0f));
        Vehicle v = World.CreateVehicle(m, spot == Vector3.Zero ? ImpoundLot : spot);
        if (v == null) { Notify("Loi tao xe, da hoan tien."); Cash(fee, "hoan tien", "hoan phi giu xe"); return; }
        try { v.Mods.PrimaryColor = (VehicleColor)im.Prim; v.Mods.SecondaryColor = (VehicleColor)im.Sec; if (im.Plate.Length > 0) v.Mods.LicensePlate = im.Plate; } catch { }
        v.PlaceOnGround();
        impounded.RemoveAt(i);
        Notify("~g~Da lay xe ra~s~ (-$" + fee + ").");
        Emit("econ.release", "fee=" + fee);
        Save();
    }

    // ------------------------------------------------------------ ngan hang + vay
    int LoanLimit(int kind)
    {
        if (dayNo < badUntil) return 0;
        object rec = Bus("GVL.Record"); if (rec is int && (int)rec > 0) return 0;   // co tien an: ngan hang tu choi
        int baseL = Math.Max(0, (credit - 500) * 60);
        if (kind == 1) { object a = Bus("GVL.Assets"); int assets = a is int ? (int)a : 0; return Math.Max(0, assets * 6 / 10 - LoanSumKind(1)); }
        return Math.Max(0, baseL - LoanSumKind(0));
    }
    int LoanSumKind(int k) { int s = 0; foreach (Loan l in loans) if (l.Kind == k) s += l.Left; return s; }

    void TakeLoan(int kind, int amount)
    {
        int lim = LoanLimit(kind);
        if (amount > lim) { Notify("Vuot han muc ($" + lim + ")."); return; }
        Loan l = new Loan { Kind = kind, Principal = amount, Left = amount, Periods = kind == 1 ? 8 : 4, Due = dayNo + 7, Rate = kind == 1 ? 0.0012f : 0.0025f };
        loans.Add(l);
        Deposit(amount, "vay", kind == 1 ? "giai ngan vay the chap" : "giai ngan vay tin chap");
        Emit("econ.loan", "amount=" + amount + ";kind=" + (kind == 1 ? "thechap" : "tinchap"));
        Notify("~g~Da giai ngan $" + amount + "~s~ vao tai khoan. Tra gop " + l.Periods + " ky, moi 7 ngay game, tu tru tai khoan.");
        Save();
    }

    void PayLoanEarly(int i)
    {
        Loan l = loans[i];
        int pay = Math.Min(l.Left, Math.Max(0, bank));
        if (pay <= 0) { Notify("Tai khoan khong co tien."); return; }
        bank -= pay; l.Left -= pay; Post("bank", -pay, "vay", "tra no truoc han");
        if (l.Left <= 0) { loans.RemoveAt(i); credit = Math.Min(850, credit + 20); Notify("~g~Da tat toan khoan vay.~s~"); }
        else Notify("Da tra $" + pay + ", con no $" + l.Left + ".");
    }

    // ------------------------------------------------------------ menu
    void ShowMain()
    {
        Begin("KINH TE  (" + menuKey + "/Backspace: dong)", null);
        if (modBank)
        {
            Info("Tien mat $" + Game.Player.Money + "   Tai khoan $" + bank + "   Diem tin dung " + credit + (bankDebt > 0 ? "   No NH $" + bankDebt : "") + (loans.Count > 0 ? "   Du no vay $" + Sum() : ""));
            if (nearAtm || nearBank) Item((nearBank ? "Ngan hang" : "Cay ATM") + ": gui / rut tien...", delegate { ShowAtm(); });
            else Info("Gui/rut tien: den cay ATM hoac chi nhanh ngan hang (cham xanh la tren ban do)");
            Item("Vay von, tra no...", delegate { ShowLoans(); });
            Item("So cai thu chi (20 dong gan nhat)", delegate { ShowLedger(); });
        }
        if (modFuel || modWear)
        {
            Vehicle v = Game.Player.Character.CurrentVehicle ?? Game.Player.Character.LastVehicle;
            if (v != null && v.Exists() && Motorized(v))
            {
                Car c = CarOf(v);
                Info("Xe: xang " + (int)c.Fuel + "/" + (int)c.Cap + " L, nhot " + (int)c.Oil + "%, may " + (int)(Math.Max(0f, v.EngineHealth) / 10f) + "%, da chay " + c.Km.ToString("0.0", CultureInfo.InvariantCulture) + " km");
                if (modFuel && nearPump)
                {
                    Item("Do day binh  (~$" + (int)Math.Ceiling((c.Cap - c.Fuel) * fuelPrice) + ", $" + fuelPrice.ToString("0.##", CultureInfo.InvariantCulture) + "/L)", delegate { Refuel(999f); ShowMain(); });
                    Item("Do 10 L  ($" + (int)Math.Ceiling(10f * fuelPrice) + ")", delegate { Refuel(10f); ShowMain(); });
                    Item("Mua binh xang du phong 10 L  ($" + (CanPrice + (int)(10f * fuelPrice)) + ", dang co " + cans + ")", delegate { if (PayAtCounter(CanPrice + (int)(10f * fuelPrice), "xang", "binh xang du phong")) cans++; ShowMain(); });
                }
                if (modFuel && cans > 0) Item("Do binh xang du phong (+10 L, con " + cans + ")", delegate { UseCan(); ShowMain(); });
                if (modFuel && c.Fuel < 3f) Item("Goi cuu ho do xang  ($" + RescueFee + ")", delegate { CallRescue(); ShowMain(); });
                if (modWear && nearGarage)
                {
                    Item("Thay nhot  ($" + OilChange + ")", delegate { ChangeOil(); ShowMain(); });
                    Item("Sua may  (~$" + (30 + (int)((1000f - Math.Max(0f, v.EngineHealth)) * 0.6f)) + ")", delegate { RepairAtGarage(); ShowMain(); });
                }
                if (modWear) Item("Goi xe keo ve gara gan nhat  (tu $" + TowFee + ")", delegate { CloseMenu(); pending.Enqueue(TowToGarage); });
            }
        }
        if (modImpound && impounded.Count > 0)
        {
            if (nearImpound)
                for (int i = 0; i < impounded.Count; i++)
                {
                    int ii = i; Impounded im = impounded[i];
                    Item("Lay xe bi cau " + (im.Plate.Length > 0 ? "[" + im.Plate.Trim() + "]" : "#" + (i + 1)) + "  ($" + (ImpoundFee + ImpoundPerDay * Math.Max(0, dayNo - im.Day)) + ")", delegate { CloseMenu(); pending.Enqueue(delegate { ReleaseImpound(ii); }); });
                }
            else Item(impounded.Count + " xe dang o bai giu xe Davis - dat diem den", delegate { Function.Call(Hash.SET_NEW_WAYPOINT, ImpoundLot.X, ImpoundLot.Y); Notify("Da dat diem den bai giu xe."); });
        }
        Item("Dong", delegate { CloseMenu(); });
    }

    void ShowAtm()
    {
        Begin(nearBank ? "NGAN HANG" : "CAY ATM", delegate { ShowMain(); });
        Info("Tien mat $" + Game.Player.Money + "   Tai khoan $" + bank + (nearBank ? "" : "   (ATM rut mat phi $2)"));
        int[] amts = { 100, 500, 1000, 5000 };
        foreach (int a in amts)
        {
            int aa = a;
            Item("Rut $" + a, delegate
            {
                int fee = nearBank ? 0 : 2;
                if (bank < aa + fee) Notify("Tai khoan khong du.");
                else { bank -= aa + fee; Post("bank", -(aa + fee), "rut", "rut tien mat"); Cash(aa, "rut", "rut tu tai khoan"); }
                ShowAtm();
            });
        }
        Item("Gui het tien mat ($" + Game.Player.Money + ")", delegate
        {
            int c = Game.Player.Money;
            if (c > 0) { Game.Player.Money = 0; Post("cash", -c, "gui", "gui vao tai khoan"); Deposit(c, "gui", "nop tien mat"); }
            ShowAtm();
        });
        Item("Gui $500", delegate
        {
            if (Game.Player.Money >= 500) { Game.Player.Money = Game.Player.Money - 500; Post("cash", -500, "gui", "gui vao tai khoan"); Deposit(500, "gui", "nop tien mat"); }
            else Notify("Khong du tien mat.");
            ShowAtm();
        });
        if (bankDebt > 0) Item("Tra no ngan hang ($" + bankDebt + ")", delegate
        {
            int p = Math.Min(bankDebt, Math.Max(0, bank)); bank -= p; bankDebt -= p; if (p > 0) Post("bank", -p, "no", "tra no ngan hang");
            ShowAtm();
        });
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowLoans()
    {
        Begin("VAY VON", delegate { ShowMain(); });
        object rec = Bus("GVL.Record");
        if (rec is int && (int)rec > 0) Info("~ Co " + (int)rec + " tien an: ngan hang tu choi cho vay moi.");
        Info("Diem tin dung " + credit + (dayNo < badUntil ? "  ~ dang bi cam vay toi ngay " + badUntil : "") + ". Tra dung han +diem, tre han -35 diem, 3 ky khong tra thi bi siet no.");
        for (int i = 0; i < loans.Count; i++)
        {
            int ii = i; Loan l = loans[i];
            Item((l.Kind == 1 ? "The chap" : "Tin chap") + ": vay $" + l.Principal + ", con no $" + l.Left + ", " + l.Periods + " ky, ky toi sau " + Math.Max(0, l.Due - dayNo) + " ngay" + (l.Late > 0 ? ", TRE " + l.Late + "/3" : "") + "  - bam de tra truoc", delegate { PayLoanEarly(ii); ShowLoans(); });
        }
        if (!nearBank) { Info("Muon vay moi: den chi nhanh ngan hang (cham xanh la)."); Item("Quay lai", delegate { ShowMain(); }); return; }
        int l0 = LoanLimit(0), l1 = LoanLimit(1);
        Info("Tin chap: lai 0,25%/ngay, 4 ky. Han muc $" + l0 + " (theo diem tin dung).");
        foreach (int a in new[] { 1000, 3000, 10000 }) { int aa = a; if (aa <= l0) Item("Vay tin chap $" + aa, delegate { TakeLoan(0, aa); ShowLoans(); }); }
        Info("The chap bat dong san: lai 0,12%/ngay, 8 ky, toi da 60% gia tri BDS. Han muc $" + l1 + (Bus("GVL.Assets") == null ? " (chua doc duoc tai san tu file Doi song)" : ""));
        foreach (int a in new[] { 10000, 30000, 100000 }) { int aa = a; if (aa <= l1) Item("Vay the chap $" + aa, delegate { TakeLoan(1, aa); ShowLoans(); }); }
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowLedger()
    {
        Begin("SO CAI THU CHI", delegate { ShowMain(); });
        for (int i = ledger.Count - 1, n = 0; i >= 0 && n < 20; i--, n++)
        {
            string[] p = ledger[i].Split('|');
            if (p.Length < 5) continue;
            int amt; int.TryParse(p[2], out amt);
            Info("Ngay " + p[0] + "  " + (p[1] == "bank" ? "TK " : p[1] == "cash" ? "TM " : "NO ") + (amt >= 0 ? "+$" : "-$") + Math.Abs(amt) + "  " + p[3] + ": " + p[4]);
        }
        Item("Quay lai", delegate { ShowMain(); });
    }

    void Begin(string title, Action back) { menuTitle = title; menuLabels = new List<string>(); menuActions = new List<Action>(); menuBack = back; menuSel = -1; }
    void Item(string label, Action act) { menuLabels.Add(label); menuActions.Add(act); }
    void Info(string label) { menuLabels.Add(label); menuActions.Add(null); }
    void CloseMenu() { menuOpen = false; BusSet("GVL.EconMenu", false); }
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
            if (BusFlag("GVL.WorldMenu") || BusFlag("GVL.PersonalMenu")) return;
            if (!menuOpen) { menuOpen = true; BusSet("GVL.EconMenu", true); pending.Enqueue(ShowMain); }
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

    // ------------------------------------------------------------ ve
    void DrawText(string s, float x, float y, float scale, Color c)
    {
        GTA.UI.TextElement t = new GTA.UI.TextElement(s, new PointF(x, y), scale, c);
        t.Outline = true;
        t.Draw();
    }

    void DrawHud(Ped me)
    {
        if (cur == null || curVeh == null || !(modFuel || modWear)) return;
        float x = 1040f, y = 650f;
        float f = cur.Cap > 0 ? cur.Fuel / cur.Cap : 0f;
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(220f, 46f), Color.FromArgb(170, 10, 12, 16)).Draw();
        if (modFuel)
        {
            new GTA.UI.ContainerElement(new PointF(x + 8f, y + 28f), new SizeF(204f, 8f), Color.FromArgb(200, 40, 46, 56)).Draw();
            Color fc = f < 0.12f ? Color.FromArgb(255, 255, 80, 80) : f < 0.3f ? Color.FromArgb(255, 255, 200, 80) : Color.FromArgb(255, 80, 220, 130);
            new GTA.UI.ContainerElement(new PointF(x + 8f, y + 28f), new SizeF(204f * Math.Max(0f, Math.Min(1f, f)), 8f), fc).Draw();
        }
        string s = (modFuel ? "Xang " + (int)cur.Fuel + "/" + (int)cur.Cap + " L" : "") + (modWear ? "   Nhot " + (int)cur.Oil + "%" : "");
        if (nearPump && modFuel) s += "   [" + menuKey + "] do xang";
        DrawText(s, x + 8f, y + 3f, 0.32f, Color.White);
    }

    void DrawMenu()
    {
        if (menuSel < 0 || menuSel >= menuActions.Count || menuActions[menuSel] == null) menuSel = NextActionable(-1, 1);
        float x = 660f, y = 150f, w = 600f, pad = 14f;
        int total = menuLabels.Count;
        const int maxRows = 12;
        int first = 0;
        if (total > maxRows) first = Math.Max(0, Math.Min(total - maxRows, menuSel - maxRows / 2));
        int last = Math.Min(total, first + maxRows);
        float h = 52f + (last - first) * 30f + 30f;
        Color accent = Color.FromArgb(255, 120, 230, 150);
        new GTA.UI.ContainerElement(new PointF(x - 2f, y - 2f), new SizeF(w + 4f, h + 4f), Color.FromArgb(230, 40, 150, 90)).Draw();
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, h), Color.FromArgb(235, 14, 20, 18)).Draw();
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, 40f), Color.FromArgb(240, 24, 48, 36)).Draw();
        DrawText(menuTitle, x + pad, y + 5f, 0.5f, accent);
        float cy = y + 48f;
        int shownNum = 0;
        for (int i = 0; i < first; i++) if (menuActions[i] != null) shownNum++;
        for (int i = first; i < last; i++)
        {
            bool act = menuActions[i] != null, sel = i == menuSel;
            if (sel) new GTA.UI.ContainerElement(new PointF(x + 6f, cy - 2f), new SizeF(w - 12f, 28f), Color.FromArgb(235, 60, 180, 110)).Draw();
            string prefix = act ? (shownNum + 1 <= 9 ? (shownNum + 1) + "  " : "    ") : "    ";
            if (act) shownNum++;
            string lab = menuLabels[i];
            if (lab.Length > 95) lab = lab.Substring(0, 95) + "...";
            DrawText(prefix + lab, x + pad, cy, act ? 0.38f : 0.32f, sel ? Color.FromArgb(255, 8, 20, 12) : act ? Color.White : Color.FromArgb(255, 160, 170, 165));
            cy += 30f;
        }
        if (total > maxRows) DrawText("(" + (menuSel + 1) + "/" + total + ")", x + w - 90f, y + 8f, 0.32f, Color.FromArgb(255, 180, 190, 185));
        DrawText("dau , va dau . : chon  -  Enter/so: chay  -  Backspace: quay lai  -  " + menuKey + ": dong", x + pad, y + h - 26f, 0.29f, Color.FromArgb(255, 150, 160, 155));
    }

    // ------------------------------------------------------------ luu / nap
    void Save()
    {
        try
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<string> l = new List<string>();
            l.Add("seen=" + NowEpoch());
            l.Add("bank=" + bank); l.Add("bankdebt=" + bankDebt); l.Add("credit=" + credit); l.Add("day=" + dayNo); l.Add("bad=" + badUntil); l.Add("cans=" + cans);
            foreach (Loan o in loans) l.Add("loan=" + o.Kind + "," + o.Principal + "," + o.Left + "," + o.Periods + "," + o.Due + "," + o.Late + "," + o.Rate.ToString(ci));
            foreach (Car c in cars.Values) l.Add("car=" + c.Key.Replace(",", ".") + "," + c.Fuel.ToString("0.0", ci) + "," + c.Cap.ToString("0", ci) + "," + c.Oil.ToString("0.0", ci) + "," + c.Km.ToString("0.00", ci));
            foreach (Impounded im in impounded) l.Add("imp=" + im.Hash + "," + im.Prim + "," + im.Sec + "," + im.Day + "," + im.Plate.Replace(",", " ") + "," + im.Key.Replace(",", "."));
            int start = Math.Max(0, ledger.Count - 60);
            for (int i = start; i < ledger.Count; i++) l.Add("led=" + ledger[i]);
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
                    string[] q = v.Split(',');
                    switch (k)
                    {
                        case "seen": lastSeen = long.Parse(v); break;
                        case "bank": bank = int.Parse(v); break;
                        case "bankdebt": bankDebt = int.Parse(v); break;
                        case "credit": credit = Math.Max(300, Math.Min(850, int.Parse(v))); break;
                        case "day": dayNo = int.Parse(v); break;
                        case "bad": badUntil = int.Parse(v); break;
                        case "cans": cans = int.Parse(v); break;
                        case "loan": loans.Add(new Loan { Kind = int.Parse(q[0]), Principal = int.Parse(q[1]), Left = int.Parse(q[2]), Periods = int.Parse(q[3]), Due = int.Parse(q[4]), Late = int.Parse(q[5]), Rate = float.Parse(q[6], ci) }); break;
                        case "car": { Car c = new Car { Key = q[0], Fuel = float.Parse(q[1], ci), Cap = float.Parse(q[2], ci), Oil = float.Parse(q[3], ci), Km = float.Parse(q[4], ci) }; cars[c.Key] = c; break; }
                        case "imp": impounded.Add(new Impounded { Hash = int.Parse(q[0]), Prim = int.Parse(q[1]), Sec = int.Parse(q[2]), Day = int.Parse(q[3]), Plate = q[4], Key = q.Length > 5 ? q[5] : "" }); break;
                        case "led": ledger.Add(v); break;
                    }
                }
                catch (Exception ex) { Log("dong luu hong (" + k + "): " + ex.Message); }
            }
        }
        catch (Exception ex) { Log("load loi: " + ex.Message); }
    }

    // ------------------------------------------------------------ tien ich
    float ReadF(string section, string key, float def)
    {
        string s = Settings.GetValue(section, key, "");
        float f;
        if (string.IsNullOrEmpty(s)) return def;
        return float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : def;
    }
    void Notify(string msg) { GTA.UI.Notification.Show(msg); }
    static void Log(string msg)
    {
        try { File.AppendAllText("GtaVietLifeEconomy.log", DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine); }
        catch { }
    }
}
