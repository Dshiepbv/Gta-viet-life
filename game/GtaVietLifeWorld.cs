// GTA Việt Life: lớp "đời thật" (nhu cầu, nhà ở, việc làm, kinh doanh, lịch cư dân, luật và hậu quả).
// File độc lập với GtaVietLife.cs (nói chuyện): chép cả hai vào thư mục "scripts".
// Lỗi ở file này không làm hỏng phần nói chuyện.
//
// Phím:   M = mở/đóng menu đời sống; số 1..9 = chọn mục; M hoặc Backspace = quay lại/đóng.
// Dữ liệu lưu trong GtaVietLifeWorld.sav (cạnh game). Gửi trạng thái cho cầu nối AI để NPC biết hoàn cảnh của Sếp.
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

public class GtaVietLifeWorld : Script
{
    // ------------------------------------------------------------ cấu hình
    const string SaveFile = "GtaVietLifeWorld.sav";
    const int FoodPrice = 15;
    const int DrinkPrice = 5;
    const int RentPerDay = 150;
    const int MoveFar = 400;
    const int BizPrice = 5000;
    const int StockPrice = 500;
    const int StockPack = 50;
    const int SalePrice = 22;
    const int WagePerDay = 100;
    const int MaxStaff = 4;
    const int MaxVisibleStaff = 2;   // chi 2 nhan vien hien ra (thu ngan + phuc vu ban); nguoi con lai lam ca khac, van tinh luong va khach
    const int FinePerStar = 150;

    string server = "http://127.0.0.1:5577";
    Keys menuKey = Keys.J;   // M la Interaction Menu cua game, F-key can Fn tren laptop
    Keys navUp = Keys.Oemcomma, navDown = Keys.OemPeriod, navOk = Keys.Return;   // mui ten trung dien thoai, [ ] la phim reload cua SHVDN
    Keys stockKey = Keys.U;   // bat/tat bang kho tren man hinh
    bool showStock = true;
    string MK { get { return menuKey.ToString(); } }
    Keys sitKey = Keys.E;
    int sitIdx = -1;
    int safeUntil;
    int nextStaffSpawn;   // lan kiem tra bu nhan vien
    Prop sitBench;
    readonly List<Prop> benchProps = new List<Prop>();
    readonly Queue<Action> pendingActs = new Queue<Action>();   // viec can goi AddProp/CreatePed phai chay trong vong lap chinh, khong phai trong su kien phim

    // ------------------------------------------------------------ trạng thái đời sống
    float hunger = 80, thirst = 80, energy = 90;
    int rep = 0;
    int debt = 0;

    bool hasHome;
    Vector3 home;
    int homeIdx = -1;
    int rentPerDay = RentPerDay;
    string homeName = "";
    float homeEnergy = 100f;
    int fadeAction;
    int nextTraffic;

    class HomeInfo
    {
        public string Name; public Vector3 Pos; public int Rent; public float Energy; public string Note;
        public HomeInfo(string n, float x, float y, float z, int rent, float energy, string note) { Name = n; Pos = new Vector3(x, y, z); Rent = rent; Energy = energy; Note = note; }
    }
    static readonly HomeInfo[] Homes = {
        new HomeInfo("Trailer o Sandy Shores", 1972.7f, 3817.0f, 33.4f, 80, 80f, "re, ngu hoi it"),
        new HomeInfo("Nha nho o Strawberry", -14.4f, -1436.5f, 31.1f, 200, 95f, "kha tot"),
        new HomeInfo("Nha o Vinewood Hills", 7.1f, 536.6f, 176.0f, 450, 100f, "view dep, ngu day suc"),
        new HomeInfo("Biet thu Rockford Hills", -813.6f, 176.0f, 72.2f, 900, 100f, "sang trong")
    };
    // noi lam viec: tho phu o LS Customs (Burton)
    static readonly Vector3 WorkSpot = new Vector3(-365.4f, -131.8f, 38.7f);
    const int WorkPay = 130;   // cho 4 gio lam

    bool jobGiaoHang;
    bool delivering;
    Vector3 dest;
    Blip destBlip;
    int deadlineMs;
    int payBase;
    int deliveriesToday, earnedToday, deliveriesTotal;

    bool hasBiz;
    Vector3 bizPos;
    int bizStock, bizStaff, bizCash;
    int bizType, bizRev, bizCogs;   // loai hinh kinh doanh (xem Types), doanh thu va gia von trong ngay cua quan dang chay
    readonly List<Ped> staffPeds = new List<Ped>();
    readonly List<float> chairTableTop = new List<float>();   // do cao mat ban cua tung ghe
    readonly List<Vector3> staffSlots = new List<Vector3>();
    int nextStaffCheck;
    class StaffState
    {
        public int Mode;        // 0 dung o quay, 1 dang di toi ban, 2 dang lam viec, 3 quay ve
        public int T;
        public int Until;
        public int Type;        // 1 = bung nuoc, 2 = don ban
        public int Chair = -1;
        public Cust Target;
        public Prop Cup;
        public int NextPatrol;
    }
    class StaffTask { public int Type; public int Chair; public Cust Target; }
    readonly List<StaffState> staffStates = new List<StaffState>();
    readonly List<StaffTask> staffTasks = new List<StaffTask>();
    readonly List<Prop> chairProps = new List<Prop>();
    readonly List<bool> chairBusy = new List<bool>();
    readonly List<float> chairFace = new List<float>();
    readonly List<Vector3> chairTable = new List<Vector3>();
    readonly List<Prop> tableCups = new List<Prop>();
    float seatZ = 0.55f;
    float ChairDist = 1.55f;   // ghe cach tam ban (ban dai nen ghe ra xa hon cho thoang)
    float staffZ = 0f;
    float staffBack = 1.45f;
    float standHalf = 0f;   // nua kich thuoc quay hien tai, de nhan vien khong dung de len quay lon
    int staffAngle = 0;   // 0..3: nhan vien dung phia nao cua quay (F9 de xoay cho dung)
    string standModel = "prop_hotdogstand_01";
    float cupX = 0.06f, cupY = 0f, cupZ = 0f, cupTable = 0.02f;   // cupTable: cao hon mat ban bao nhieu (m)
    bool bizIndoor;   // quan mo ben trong nha co noi that
    float bizGround;   // do cao mat dat that tai quan (do luc dat quan, truoc khi dung props)
    float standYaw = -90f;   // xoay quay de mat quay (bang menu, dung cu) huong ve phia khach; neu nguoc thi doi thanh 90
    class Cust
    {
        public Ped P;
        public int Stage;      // 0 = di toi quay, 1 = dang cho/mua, 2 = ngoi o ghe
        public int T;
        public int Chair = -1;
        public int Qty = 1;
        public Prop Cup;
    }
    readonly List<Cust> customers = new List<Cust>();
    int nextCustomer;
    int soldToday;
    int bizLevel = 1;
    float bizHeading;
    readonly List<Prop> bizProps = new List<Prop>();

    // ------------------------------------------------------------ nội bộ
    readonly Random rnd = new Random();
    int lastMinuteOfDay = -1;
    int dayCounter;
    readonly List<int[]> dayHist = new List<int[]>();   // lich su: ngay, doanh thu, gia von, luong, thue, mat bang, loi nhuan
    int dRev, dCogs, dWages, dTax, dRentSum;
    int lastDayTick;
    int prevWanted;
    int nextNeedHurt, nextWarn, nextStatePost, nextSave, nextResidentCheck;
    float minuteAccum;
    int sleepAt;

    // menu
    bool menuOpen;
    string menuTitle = "";
    List<string> menuLabels = new List<string>();
    List<Action> menuActions = new List<Action>();
    Action menuBack;


    public GtaVietLifeWorld()
    {
        int port = Settings.GetValue("Bridge", "Port", 5577);
        server = "http://127.0.0.1:" + port;
        menuKey = Settings.GetValue("Keys", "Menu", menuKey);
        stockKey = Settings.GetValue("Keys", "Stock", stockKey);
        navUp = Settings.GetValue("Keys", "Up", navUp);
        navDown = Settings.GetValue("Keys", "Down", navDown);
        navOk = Settings.GetValue("Keys", "Ok", navOk);
        sitKey = Settings.GetValue("Keys", "Sit", sitKey);
        seatZ = ReadF("Business", "SeatZ", seatZ);
        ChairDist = ReadF("Business", "ChairDist", ChairDist);
        staffZ = ReadF("Business", "StaffZ", staffZ);
        staffBack = ReadF("Business", "StaffBack", staffBack);
        standYaw = ReadF("Business", "StandYaw", standYaw);
        standModel = Settings.GetValue("Business", "StandModel", standModel);
        cupX = ReadF("Business", "CupHandX", cupX);
        cupY = ReadF("Business", "CupHandY", cupY);
        cupZ = ReadF("Business", "CupHandZ", cupZ);
        cupTable = ReadF("Business", "CupTableOffset", cupTable);

        Load();
        InitShops();
        MakeHomeBlips();

        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
        Notify("~g~GTA Viet Life: Doi song~s~ da bat. Bam ~y~" + MK + "~s~ de mo menu.");
        foreach (Shop sh in shops) MakeShopBlip(sh);
        foreach (Owned o in owned) MakeEstateBlip(o);
        if (hasBiz) { BuildBizProps(); }
        for (int i = 0; i < Math.Min(bizStaff, MaxVisibleStaff); i++) SpawnStaff();
    }

    // ------------------------------------------------------------ vòng lặp
    void OnTick(object sender, EventArgs e)
    {
        Ped me = Game.Player.Character;
        if (me == null || !me.Exists()) return;
        int now = Game.GameTime;

        SyncPersonalBus();
        TickEnterHome(me, now);
        SyncEngineWorldCatalog();
        if (now > nextEconBus) { nextEconBus = now + 2000; EconBus(); }
        if (!caughtUp) { ApplyPhoneCmds(); CatchUpOffline(); }
        else if (now > nextCmdCheck) { nextCmdCheck = now + 10000; ApplyPhoneCmds(); }
        if (now > nextSeenSave) { nextSeenSave = now + 120000; Save(); }
        try { Function.Call(Hash.SET_MINIMAP_HIDE_FOW, true); } catch { }   // mo toan bo ban do, het suong mu

        if (safeUntil > 0 && now > safeUntil) { safeUntil = 0; Function.Call(Hash.SET_ENTITY_INVINCIBLE, me, false); }

        if (rebuildAt > 0 && now > rebuildAt && hasBiz) { rebuildAt = 0; BuildBizProps(); }
        if (shops.Count > 0 && (shops.Count > 1 || activeShop < 0) && now > nextShopCheck)
        {
            nextShopCheck = now + 2000;
            int bi = -1; float bd = 1e9f;
            for (int si = 0; si < shops.Count; si++) { float dd = me.Position.DistanceTo2D(shops[si].Pos); if (dd < bd) { bd = dd; bi = si; } }
            if (bi >= 0 && bi != activeShop && bd < 90f && (activeShop < 0 || me.Position.DistanceTo2D(bizPos) > 90f)) ActivateShop(bi);
        }
        if (shops.Count > 1 || (shops.Count == 1 && activeShop < 0))
        {
            // quan o xa van ban hang: moi quan chot don theo nhip rieng, bao doanh so ngay
            for (int ri = 0; ri < shops.Count; ri++)
            {
                if (ri == activeShop) continue;
                Shop rs = shops[ri];
                if (rs.NextSale == 0) { rs.NextSale = now + rnd.Next(15000, 45000); continue; }
                if (now < rs.NextSale) continue;
                // nhip ban o xa = nhip khi Sep dung o quan (khong phu thuoc khoang cach)
                rs.NextSale = now + (int)((Math.Max(7000, 26000 - rs.Staff * 3000 - rs.Level * 2500 + rnd.Next(0, 6000)) + 4000) / Types[TIdx(rs.Type)].Traffic);   // cung nhip nhu khi Sep dung o quan
                if (rs.Staff <= 0)
                {
                    if (!rs.OutNotified) { rs.OutNotified = true; Notify("~y~Quan #" + (ri + 1) + " chua co nhan vien~s~ nen khong ban duoc. Den quan thue nhan vien."); }
                    continue;
                }
                if (rs.Stock <= 0)
                {
                    if (!rs.OutNotified) { rs.OutNotified = true; Notify("~r~Quan #" + (ri + 1) + " het hang!~s~ Den nhap hang de tiep tuc ban."); }
                    continue;
                }
                rs.OutNotified = false;
                int rq = Math.Min(rs.Stock, rnd.Next(1, Types[TIdx(rs.Type)].Channel == 1 ? 7 : 4));
                rs.Stock -= rq;
                rs.SoldToday += rq;
                int rpay, rcogs;
                SellUnits(rs.Type, rs.Level, rq, out rpay, out rcogs);
                rs.RevToday += rpay; rs.CogsToday += rcogs;
                EarnCash(rpay, "business", "ban hang quan #" + (ri + 1));
                Notify("~g~Quan #" + (ri + 1) + ": +$" + rpay + "~s~ ban " + rq + " " + UW(rs.Type) + " (kho con " + rs.Stock + ", " + ZoneName(rs.Pos) + ").");
                if (rs.Stock <= 5 && rs.Stock > 0) Notify("~y~Quan #" + (ri + 1) + " sap het hang~s~ (con " + rs.Stock + " " + UW(rs.Type) + ").");
            }
        }
        try { UpdateRide(me, now); } catch (Exception ex) { Log("chuyen xe loi: " + ex.Message); }
        if (hasBiz && now > nextStaffSpawn)
        {
            nextStaffSpawn = now + 3000;
            if (staffPeds.Count < Math.Min(bizStaff, MaxVisibleStaff) && me.Position.DistanceTo(bizPos) < 80f) SpawnStaff();
        }

        while (pendingActs.Count > 0)
        {
            Action pa = pendingActs.Dequeue();
            try { pa(); } catch (Exception ex) { Log("viec cho loi: " + ex); Notify("Loi: " + Short(ex)); }
        }

        UpdateCityRhythm();
        SyncLifeBus();
        if (sleepAt > 0 && now > sleepAt) { sleepAt = 0; FinishSleep(); }
        if (sleepAt > 0) return;
        UpdateClock();
        ProcessEngineCommands(now);
        ProcessEngineWorldCommands(now);
        CompletePendingWorldTransition(now);
        UpdateNeeds(me, now);
        UpdateWanted(me);
        UpdateDelivery(me, now);
        UpdateResidents(now);
        UpdateStaff(now);
        UpdateCustomers(now, me);
        UpdatePlayerSeat(me);
        UpdateTraffic(me, now);

        if (now > nextStatePost) { nextStatePost = now + 15000; PostState(); }
        if (now > nextSave) { nextSave = now + 30000; Save(); }

        if (hasBiz && me.Position.DistanceTo(bizPos) < 60f)
            World.DrawMarker(MarkerType.VerticalCylinder, bizPos + new Vector3(0f, 0f, 0.1f), Vector3.Zero, Vector3.Zero, new Vector3(1.2f, 1.2f, 0.8f), Color.FromArgb(120, 60, 220, 90));
        DrawHud();
        if (menuOpen) DrawMenu();
    }

    void OnAborted(object sender, EventArgs e)
    {
        Save();
        foreach (Ped p in staffPeds) if (p != null && p.Exists()) p.Delete();
        foreach (Cust c in customers) { DeleteProp(c.Cup); if (c.P != null && c.P.Exists()) c.P.Delete(); }
        foreach (StaffState st in staffStates) DeleteProp(st.Cup);
        foreach (Prop tc in tableCups) DeleteProp(tc);
        EndRide(false);
        ClearBizProps();
        foreach (Shop sh in shops) if (sh.Blip != null && sh.Blip.Exists()) sh.Blip.Delete();
        foreach (Owned o in owned) if (o.Blip != null && o.Blip.Exists()) o.Blip.Delete();
        foreach (Blip b in estateListBlips) if (b != null && b.Exists()) b.Delete();
        if (destBlip != null && destBlip.Exists()) destBlip.Delete();
        foreach (Resident r in residents) DespawnRes(r);   // lan nap sau tu hien lai tu file luu
    }

    // ------------------------------------------------------------ đồng hồ, nhu cầu, ngày mới
    void UpdateClock()
    {
        TimeSpan t = World.CurrentTimeOfDay;
        int m = t.Hours * 60 + t.Minutes;
        if (lastMinuteOfDay < 0) { lastMinuteOfDay = m; return; }
        int delta = (m - lastMinuteOfDay + 1440) % 1440;
        lastMinuteOfDay = m;
        if (delta == 0 || delta > 180) return; // đứng yên hoặc nhảy giờ (ngủ, console)
        if (m < delta) dayCounter++; // vừa qua nửa đêm
        minuteAccum += delta;
        hunger = Clamp(hunger - 0.06f * delta * drainMul);   // drainMul: ky nang The luc tu file Ca nhan
        thirst = Clamp(thirst - 0.09f * delta * drainMul);
        energy = Clamp(energy - 0.05f * delta * drainMul);
        hygiene = Clamp(hygiene - 0.035f * delta);   // ~50%/ngay game
        if (dayCounter > lastDayTick) { lastDayTick = dayCounter; DailyTick(); }
    }

    // ---- cau noi voi file Ca nhan (GtaVietLifePersonal.cs): cung AppDomain cua SHVDN nen dung AppDomain data
    float drainMul = 1f;
    readonly float[] needsPub = new float[3];
    void SyncPersonalBus()
    {
        try
        {
            AppDomain d = AppDomain.CurrentDomain;
            float[] add = d.GetData("GVL.NeedsAdd") as float[];
            if (add != null && add.Length >= 3)
            {
                d.SetData("GVL.NeedsAdd", null);
                hunger = Clamp(hunger + add[0]); thirst = Clamp(thirst + add[1]); energy = Clamp(energy + add[2]);
            }
            object dm = d.GetData("GVL.DrainMul");
            drainMul = dm is float ? Math.Max(0.5f, Math.Min(1f, (float)dm)) : 1f;
            needsPub[0] = hunger; needsPub[1] = thirst; needsPub[2] = energy;
            d.SetData("GVL.Needs", needsPub);
            d.SetData("GVL.WorldMenu", menuOpen);
        }
        catch { }
    }

    static bool PersonalMenuOpen()
    {
        try { object o = AppDomain.CurrentDomain.GetData("GVL.PersonalMenu"); return o is bool && (bool)o; } catch { return false; }
    }

    void AdvanceTime(int minutes)
    {
        TimeSpan t = World.CurrentTimeOfDay;
        int m = t.Hours * 60 + t.Minutes + minutes;
        if (m >= 1440) { dayCounter++; m -= 1440; }
        World.CurrentTimeOfDay = new TimeSpan(m / 60, m % 60, 0);
        lastMinuteOfDay = m;
        if (dayCounter > lastDayTick) { lastDayTick = dayCounter; DailyTick(); }
    }

    void UpdateNeeds(Ped me, int now)
    {
        if (!me.IsAlive) return;
        float low = Math.Min(hunger, Math.Min(thirst, energy));
        if (low <= 0f && now > nextNeedHurt)
        {
            nextNeedHurt = now + 3000;
            if (me.Health > 120) me.Health = me.Health - 4;
        }
        if (low < 15f && now > nextWarn)
        {
            nextWarn = now + 60000;
            string what = hunger < 15f ? "doi" : thirst < 15f ? "khat" : "kiet suc, can ngu";
            Notify("~r~Sep dang " + what + "!~s~ Bam " + MK + " de an/uong/ngu.");
        }
        if (hygiene < 15f && now > nextHygWarn)
        {
            nextHygWarn = now + 180000;
            Notify("~y~Nguoi Sep bat dau co mui~s~, nguoi ta de y roi. " + MK + " > Dich vu > Tam rua.");
        }
    }

    void DailyTick()
    {
        int shipDay = earnedToday;   // tien giao hang cua ngay vua qua (dua vao lich su loi nhuan)
        deliveriesToday = 0;
        earnedToday = 0;
        dRev = 0; dCogs = 0; dWages = 0; dTax = 0; dRentSum = 0;
        string report = "~b~Qua ngay moi.~s~";

        if (hasHome)
        {
            if (PayCash(rentPerDay, "housing", "tien nha")) { report += " Tien nha -$" + rentPerDay + "."; }
            else { debt += rentPerDay; report += " ~r~Thieu tien nha, ghi no $" + rentPerDay + ".~s~"; }
        }
        if (debt > 0)
        {
            debt += debt / 20; // lãi 5% mỗi ngày
            int pay = Math.Min(Game.Player.Money, Math.Max(0, Game.Player.Money * 3 / 10));
            pay = Math.Min(pay, debt);
            if (pay > 0 && PayCash(pay, "debt", "chu no thu hoi")) { debt -= pay; report += " Chu no lay $" + pay + "."; }
            if (debt > 3000) { rep -= 2; report += " ~r~No qua nhieu, mat uy tin!~s~"; PostEvent("Có gã chơi nổi nhưng nợ nần chồng chất, người ta bắt đầu ngán."); }
        }
        if (catching && jobGiaoHang) { int sg; report += OfflineDelivery(out sg); shipDay += sg; }
        int estNet;
        report += DailyEstates(out estNet);
        if (shops.Count > 0) SyncOut();
        report += DailyB2B();
        if (shops.Count > 0)
        {
            SyncOut();
            for (int i = shops.Count - 1; i >= 0; i--)
            {
                Shop sh = shops[i];
                string nm = "Quan " + (i + 1);
                int target = sh.Staff <= 0 ? 0 : (int)((15 + (sh.Level - 1) * 8 + sh.Staff * 12 + rnd.Next(0, 11) + Math.Max(0, rep / 5)) * ModeTraffic[sh.Mode] * ZoneMult(sh.Pos) * (sh.Indoor ? 1.15f : 1f) * Types[TIdx(sh.Type)].Traffic * (catching ? OfflineFactor : 1f));
                int remain = Math.Max(0, target - sh.SoldToday);   // khach thay tan mat da ban theo thoi gian thuc
                int sold = Math.Min(sh.Stock, remain);
                sh.Stock -= sold;
                int revenue, cogsNew;
                SellUnits(sh.Type, sh.Level, sold, out revenue, out cogsNew);
                int totRev = sh.RevToday + revenue, totCogs = sh.CogsToday + cogsNew;
                int tax = TaxOf(sh.Type, totRev);
                EarnCash(revenue, "business", "doanh thu " + nm);
                int wages = sh.Staff * WagePerDay;
                if (!PayCash(wages, "payroll", "luong " + nm)) { debt += wages; report += " ~r~Thieu tien tra luong " + nm + ", ghi no.~s~"; }
                if (!PayCash(tax, "tax", "thue " + nm)) { debt += tax; report += " ~r~Thieu tien nop thue " + nm + ", ghi no.~s~"; }
                int profit = totRev - totCogs - wages - tax - sh.Rent;
                sh.LastProfit = profit; sh.LastRev = totRev;
                dRev += totRev; dCogs += totCogs; dWages += wages; dTax += tax; dRentSum += sh.Rent;
                if (sold + sh.SoldToday >= 10) rep += 1;
                report += " " + nm + " [" + Types[TIdx(sh.Type)].Name + "]: ban " + (sold + sh.SoldToday) + " " + UW(sh.Type) + ", doanh thu $" + totRev + ", gia von $" + totCogs + ", luong -$" + wages + ", thue -$" + tax;
                sh.SoldToday = 0; sh.RevToday = 0; sh.CogsToday = 0;
                if (sh.Rent > 0)
                {
                    if (PayCash(sh.Rent, "rent", "mat bang " + nm)) { sh.Overdue = 0; report += ", " + FeeWord(sh.Mode) + " -$" + sh.Rent; }
                    else
                    {
                        sh.Overdue++;
                        report += ". ~r~Thieu tien " + FeeWord(sh.Mode) + " (" + sh.Overdue + "/3 ngay)~s~";
                        if (sh.Overdue >= 3)
                        {
                            report += " ~r~Bi thu hoi mat bang " + nm + "!~s~";
                            RemoveShop(i);
                            continue;
                        }
                    }
                }
                if (sh.Mode == 2)
                {
                    sh.DaysLeft--;
                    if (sh.DaysLeft <= 0) { sh.DaysLeft = 30; report += ", gia han hop dong 30 ngay"; }
                }
                report += ". Loi nhuan ngay $" + profit + ".";
                if (sh.Stock == 0) report += " ~y~" + nm + " het hang!~s~";
            }
            if (activeShop >= 0) SyncIn(activeShop);
        }
        // cot cuoi = loi nhuan rong ca ngay: quan + bat dong san + giao hang
        dayHist.Add(new int[] { dayCounter, dRev, dCogs, dWages, dTax, dRentSum, dRev - dCogs - dWages - dTax - dRentSum + estNet + shipDay });
        while (dayHist.Count > 30) dayHist.RemoveAt(0);
        if (catching) { catchLog.Add(report); return; }
        Notify(report);
        Save();
    }

    // ------------------------------------------------------------ luật và hậu quả
    void UpdateWanted(Ped me)
    {
        int w = Game.Player.WantedLevel;
        if (w >= 2 && prevWanted < 2)
        {
            rep -= 3;
            PostEvent("Có gã vừa gây chuyện lớn, cảnh sát đang rượt gần " + SafeStreet(me.Position) + ".");
        }
        if (prevWanted > 0 && w == 0 && me.IsAlive)
        {
            int fine = prevWanted * FinePerStar;
            if (PayCash(fine, "fine", "vi pham giao thong")) { Notify("~r~Bi phat $" + fine + "~s~ vi vi pham (" + prevWanted + " sao)."); }
            else { debt += fine; Notify("~r~Thieu tien nop phat, ghi no $" + fine + ".~s~"); }
            rep -= prevWanted;
        }
        prevWanted = w;
    }

    // ------------------------------------------------------------ việc làm: giao hàng
    void StartDelivery()
    {
        int hour = World.CurrentTimeOfDay.Hours;
        if (hour < 6 || hour >= 22) { Notify("Ngoai gio lam (ca 6h-22h)."); return; }
        if (delivering) { Notify("Dang co don roi."); return; }
        Ped me = Game.Player.Character;
        Vector3 d = Vector3.Zero;
        for (int i = 0; i < 8; i++)
        {
            double ang = rnd.NextDouble() * Math.PI * 2;
            float dist = 600f + (float)rnd.NextDouble() * 900f;
            Vector3 raw = me.Position + new Vector3((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist, 0f);
            d = World.GetNextPositionOnStreet(raw);
            if (d != Vector3.Zero) break;
        }
        if (d == Vector3.Zero) { Notify("Khong tim duoc diem giao, thu lai."); return; }
        dest = d;
        float len = me.Position.DistanceTo2D(dest);
        payBase = 60 + (int)(len / 8f);
        deadlineMs = Game.GameTime + (int)(len / 7f * 1000f) + 30000;
        delivering = true;
        if (destBlip != null && destBlip.Exists()) destBlip.Delete();
        destBlip = World.CreateBlip(dest);
        destBlip.Color = BlipColor.Yellow;
        Function.Call(Hash.SET_NEW_WAYPOINT, dest.X, dest.Y);
        Notify("~y~Don giao hang~s~: " + (int)len + " m, thuong $" + payBase + ". Dung han de du tien.");
    }

    void EndDelivery(bool success, bool late)
    {
        delivering = false;
        if (destBlip != null && destBlip.Exists()) destBlip.Delete();
        destBlip = null;
        Function.Call(Hash.SET_WAYPOINT_OFF);
        if (!success) { Notify("Da huy don."); return; }
        int pay = late ? payBase / 2 : payBase;
        EarnCash(pay, "delivery", "hoan thanh don giao hang");
        deliveriesToday++;
        deliveriesTotal++;
        earnedToday += pay;
        if (!late) rep += 1;
        Notify((late ? "~y~Giao tre~s~, nhan $" : "~g~Giao xong!~s~ Nhan $") + pay + ". Hom nay " + deliveriesToday + " don, $" + earnedToday + ".");
        if (deliveriesTotal % 5 == 0) PostEvent("Có gã chạy giao hàng rất chăm, ai cũng biết mặt.");
    }

    void UpdateDelivery(Ped me, int now)
    {
        if (!delivering) return;
        if (me.Position.DistanceTo2D(dest) < 25f) { EndDelivery(true, now > deadlineMs); return; }
        if (now > deadlineMs + 120000) { Notify("Qua tre, don bi huy."); rep -= 1; EndDelivery(false, true); }
    }

    // ------------------------------------------------------------ kinh doanh
    // ====== san pham, loai hinh va kenh ban: GT (tap hoa, quan via he) / MT (chuoi, sieu thi) / B2C / B2B ======
    class Prod
    {
        public string Name; public int Cost, Price, Sector;
        public Prod(string n, int c, int p, int s) { Name = n; Cost = c; Price = p; Sector = s; }
    }
    static readonly string[] SectorName = { "F&B", "FMCG", "Dich vu" };
    static readonly Prod[] Catalog = {
        new Prod("Tra da", 3, 10, 0), new Prod("Ca phe sua", 8, 22, 0), new Prod("Nuoc ep", 9, 24, 0), new Prod("Banh mi", 7, 18, 0),
        new Prod("Pho bo", 14, 36, 0), new Prod("Com tam", 15, 38, 0), new Prod("Ga ran", 16, 40, 0), new Prod("Burger", 15, 38, 0),
        new Prod("Bia hoi", 10, 26, 0), new Prod("Banh ngot", 9, 24, 0),
        new Prod("Mi goi", 3, 6, 1), new Prod("Nuoc ngot lon", 6, 11, 1), new Prod("Nuoc suoi", 3, 7, 1), new Prod("Snack", 4, 9, 1),
        new Prod("Sua hop", 7, 13, 1), new Prod("Dau goi", 13, 24, 1), new Prod("Giay ve sinh", 9, 17, 1), new Prod("Xa phong", 6, 12, 1),
        new Prod("Gao 5kg", 28, 40, 1), new Prod("Dau an 1L", 16, 24, 1),
        // dich vu (20..27): "gia von" = vat tu tieu hao cho 1 luot (nuoc, hoa chat, dau nhot, dau goi...)
        new Prod("Rua xe may", 3, 12, 2), new Prod("Rua o to", 8, 30, 2), new Prod("Thay nhot", 22, 40, 2), new Prod("Va lop / bom lop", 2, 9, 2),
        new Prod("Cat toc nam", 3, 18, 2), new Prod("Goi dau thu gian", 5, 25, 2), new Prod("Giat say (kg)", 3, 10, 2), new Prod("Ui do", 1, 6, 2) };

    class BizType
    {
        public string Name; public int Channel;   // 0 = GT (kenh truyen thong), 1 = MT (kenh hien dai)
        public float Capex, RentMul, Traffic, PriceMul;
        public int[] Items; public int MinMode; public string Note;
        public string Unit = "phan";   // dich vu tinh theo "luot"
    }
    static readonly BizType[] Types = {
        new BizType { Name = "Quan nuoc via he", Channel = 0, Capex = 1.0f, RentMul = 1.0f, Traffic = 1.0f, PriceMul = 1.19f, MinMode = 0, Items = new[] { 0, 1, 2, 3 }, Note = "F&B, von nho, bien loi nhuan cao" },
        new BizType { Name = "Tap hoa", Channel = 0, Capex = 1.3f, RentMul = 1.0f, Traffic = 0.9f, PriceMul = 1.05f, MinMode = 0, Items = new[] { 10, 11, 12, 13, 14, 17, 18 }, Note = "FMCG, nhap le, bien loi nhuan vua" },
        new BizType { Name = "Quan an", Channel = 0, Capex = 1.6f, RentMul = 1.2f, Traffic = 1.0f, PriceMul = 1.05f, MinMode = 1, Items = new[] { 3, 4, 5, 6, 8, 0 }, Note = "F&B, mon an chinh, can mat bang thue" },
        new BizType { Name = "Cua hang tien loi (MT)", Channel = 1, Capex = 3.0f, RentMul = 1.8f, Traffic = 1.5f, PriceMul = 0.97f, MinMode = 2, Items = new[] { 0, 1, 3, 7, 10, 11, 12, 13, 14 }, Note = "FMCG + F&B, mo 24/7, dong khach, thue VAT" },
        new BizType { Name = "Sieu thi mini (MT)", Channel = 1, Capex = 6.0f, RentMul = 3.0f, Traffic = 2.0f, PriceMul = 0.92f, MinMode = 2, Items = new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 }, Note = "FMCG da nganh, dong khach nhat, von lon, loi nhuan mong" },
        new BizType { Name = "Nha hang / ca phe chuoi (MT)", Channel = 1, Capex = 4.0f, RentMul = 2.2f, Traffic = 1.3f, PriceMul = 1.15f, MinMode = 2, Items = new[] { 1, 2, 4, 5, 7, 9 }, Note = "F&B cao cap, gia cao, thue VAT" },
        // DICH VU: kho = vat tu cho N luot phuc vu; khong ban si B2B duoc
        new BizType { Name = "Tiem rua xe", Channel = 0, Capex = 1.4f, RentMul = 1.1f, Traffic = 0.9f, PriceMul = 1.0f, MinMode = 1, Items = new[] { 20, 21, 23 }, Note = "Dich vu, vat tu re, bien loi nhuan cao; Sep lai xe toi duoc rua mien phi", Unit = "luot" },
        new BizType { Name = "Sua xe - thay nhot", Channel = 0, Capex = 1.8f, RentMul = 1.2f, Traffic = 0.7f, PriceMul = 1.05f, MinMode = 1, Items = new[] { 22, 23, 20 }, Note = "Dich vu, it khach nhung moi luot gia tri cao; Sep lai xe toi duoc sua mien phi", Unit = "luot" },
        new BizType { Name = "Tiem toc - goi dau", Channel = 0, Capex = 1.3f, RentMul = 1.0f, Traffic = 0.8f, PriceMul = 1.05f, MinMode = 1, Items = new[] { 24, 25 }, Note = "Dich vu, von nho, khach quen", Unit = "luot" },
        new BizType { Name = "Giat ui", Channel = 0, Capex = 1.2f, RentMul = 0.9f, Traffic = 0.8f, PriceMul = 1.0f, MinMode = 1, Items = new[] { 26, 27 }, Note = "Dich vu, gia re, khach deu dan cu", Unit = "luot" } };
    static bool IsService(int type) { return Catalog[Types[TIdx(type)].Items[0]].Sector == 2; }
    static string UW(int type) { return Types[TIdx(type)].Unit; }

    static int TIdx(int t) { return t < 0 || t >= Types.Length ? 0 : t; }
    const float LevelPriceStep = 0.27f;   // moi cap quan +27% gia ban (khop cong thuc cu 22$ + 6$/cap: cap 4 ~ 40$)
    static float AvgOf(BizType t, bool price)
    {
        float s = 0f; foreach (int i in t.Items) s += price ? Catalog[i].Price : Catalog[i].Cost;
        return s / t.Items.Length;
    }
    static int UnitCost(int type) { return (int)Math.Round(AvgOf(Types[TIdx(type)], false)); }
    static int UnitPrice(int type, int level) { BizType t = Types[TIdx(type)]; return (int)Math.Round(AvgOf(t, true) * t.PriceMul * (1f + LevelPriceStep * (level - 1))); }

    // ban qty don vi: moi don vi chon ngau nhien 1 mat hang trong danh muc cua loai hinh -> doanh thu va gia von
    void SellUnits(int type, int level, int qty, out int rev, out int cogs)
    {
        BizType t = Types[TIdx(type)];
        float lv = 1f + LevelPriceStep * (level - 1);
        float r = 0f; int c = 0;
        for (int i = 0; i < qty; i++)
        {
            Prod p = Catalog[t.Items[rnd.Next(t.Items.Length)]];
            r += p.Price * t.PriceMul * lv; c += p.Cost;
        }
        rev = (int)Math.Round(r); cogs = c;
    }

    // gia nhap 1 goi hang (StockPack don vi) tu nha phan phoi: mua nhieu duoc chiet khau, MT duoc them uu dai, giao tu xa +phi
    int PackCost(int type, int packs, bool remote)
    {
        BizType t = Types[TIdx(type)];
        float disc = packs >= 10 ? 0.88f : packs >= 5 ? 0.93f : 1f;
        if (t.Channel == 1) disc *= 0.95f;
        float b = StockPack * UnitCost(type) * packs * disc * (remote ? RemoteFee : 1f);
        return (int)Math.Round(b);
    }

    // thue theo thuc te: GT dong thue khoan co dinh moi ngay, MT ke khai VAT/TNDN ~8% doanh thu
    int TaxOf(int type, int rev) { return Types[TIdx(type)].Channel == 0 ? 8 : (int)(rev * 0.08f); }

    // ====== B2B: ban si hop dong cho cac cua hang/quan khac; cong no phai thu theo ky han, co rui ro vo no ======
    class Deal
    {
        public string Name; public int Channel, PerDay, PricePct, TermDays; public float Risk;
        public Deal(string n, int ch, int per, int pct, int term, float risk) { Name = n; Channel = ch; PerDay = per; PricePct = pct; TermDays = term; Risk = risk; }
    }
    static readonly Deal[] Deals = {
        new Deal("Tap hoa Co Lan (GT)", 0, 12, 80, 7, 0.04f),
        new Deal("Quan com Ba Tu (GT)", 0, 20, 78, 7, 0.05f),
        new Deal("Quan cafe Cay Me (GT)", 0, 15, 80, 5, 0.04f),
        new Deal("Nha hang Ngoc Lan (B2B F&B)", 0, 25, 75, 14, 0.02f),
        new Deal("Chuoi cua hang tien loi mini (MT)", 1, 40, 72, 30, 0.01f),
        new Deal("Sieu thi Co.op Mart (MT)", 1, 70, 68, 45, 0.01f) };
    readonly List<int[]> contracts = new List<int[]>();   // {chi so Deal, chi so quan lam kho}
    readonly List<int[]> receivables = new List<int[]>(); // {ngay den han, so tien, rui ro x1000}
    int b2bDay;

    int ReceivableTotal() { int t = 0; foreach (int[] r in receivables) t += r[1]; return t; }
    int ContractOf(int deal) { for (int i = 0; i < contracts.Count; i++) if (contracts[i][0] == deal) return i; return -1; }

    string DailyB2B()
    {
        b2bDay++;
        string rep2 = "";
        int deliveredRev = 0;
        for (int i = 0; i < contracts.Count; i++)
        {
            Deal d = Deals[contracts[i][0]];
            int depot = contracts[i][1];
            if (depot < 0 || depot >= shops.Count) continue;
            Shop sh = shops[depot];
            int q = Math.Min(d.PerDay, sh.Stock);
            if (IsService(sh.Type)) continue;
            if (q <= 0) { rep2 += " ~y~Hop dong " + d.Name + " khong giao duoc vi kho quan #" + (depot + 1) + " het hang.~s~"; continue; }
            int unit = UnitPrice(sh.Type, sh.Level) * d.PricePct / 100;
            int amount = q * unit;
            sh.Stock -= q;
            receivables.Add(new int[] { b2bDay + d.TermDays, amount, (int)(d.Risk * 1000f) });
            deliveredRev += amount;
        }
        if (deliveredRev > 0) rep2 += " B2B: giao hang, ghi cong no phai thu $" + deliveredRev + ".";
        int got = 0, bad = 0;
        for (int i = receivables.Count - 1; i >= 0; i--)
        {
            int[] r = receivables[i];
            if (r[0] > b2bDay) continue;
            if (rnd.Next(1000) < r[2]) bad += r[1]; else got += r[1];
            receivables.RemoveAt(i);
        }
        if (got > 0) { EarnCash(got, "receivable", "thu cong no B2B"); rep2 += " Thu cong no B2B +$" + got + "."; }
        if (bad > 0) rep2 += " ~r~Khach B2B vo no, mat $" + bad + ".~s~";
        return rep2;
    }

    void ShowB2B()
    {
        Begin("BAN SI B2B  (hop dong cung ung)", delegate { ShowBiz(); });
        Info("Cong no phai thu: $" + ReceivableTotal() + "   Hop dong dang chay: " + contracts.Count);
        Info("Hang lay tu kho mot quan, tinh gia si bang % gia le, doi tien theo ky han. GT: ky han ngan, rui ro cao hon. MT: ky han dai (30-45 ngay), rui ro thap.");
        if (shops.Count == 0) { Info("Chua co quan nao de lam kho."); Item("Quay lai", delegate { ShowBiz(); }); return; }
        int depot = activeShop >= 0 ? activeShop : 0;
        for (int i = 0; i < Deals.Length; i++)
        {
            int di = i; Deal d = Deals[i];
            int ci = ContractOf(di);
            string cond = d.PerDay + " phan/ngay, gia si " + d.PricePct + "% gia le, tra sau " + d.TermDays + " ngay, rui ro vo no " + (int)(d.Risk * 100f) + "%";
            if (ci >= 0)
                Item("[DANG GIAO tu quan #" + (contracts[ci][1] + 1) + "] " + d.Name + " - bam de huy", delegate { contracts.RemoveAt(ContractOf(di)); Notify("Da huy hop dong " + Deals[di].Name + "."); ShowB2B(); });
            else
                Item(d.Name + ": " + cond, delegate
                {
                    if (activeShop < 0) { Notify("Hay den gan mot quan de lam kho cho hop dong."); return; }
                    if (IsService(shops[activeShop].Type)) { Notify("Quan dich vu khong ban si duoc. Hay den gan quan ban hang (F&B/FMCG)."); return; }
                    if (rep < (Deals[di].Channel == 1 ? 10 : 0)) { Notify("Chua du uy tin de ky hop dong voi " + Deals[di].Name + " (can uy tin >= 10)."); return; }
                    contracts.Add(new int[] { di, activeShop });
                    Notify("~g~Da ky hop dong~s~ cung ung cho " + Deals[di].Name + " tu quan #" + (activeShop + 1) + ".");
                    ShowB2B();
                });
        }
        Item("Quay lai", delegate { ShowBiz(); });
    }

    // ====== giao hang luc tat may: neu Sep dang ky lam shipper, moi ngay offline van chay vai don (bang OfflineFactor so don luc choi) ======
    const int ShipFuel = 8;   // tien xang + hao mon xe moi don
    string OfflineDelivery(out int net)
    {
        net = 0;
        int n = (int)Math.Round((4 + rnd.Next(0, 5)) * OfflineFactor);   // luc choi ~4-8 don/ngay -> offline 2-5 don
        if (n <= 0) return "";
        int gross = 0;
        for (int i = 0; i < n; i++) gross += 75 + rnd.Next(0, 71);   // don online thuong $135-250 vi Sep tu chay xa; offline nhan don gan
        net = gross - n * ShipFuel;
        if (net >= 0) EarnCash(net, "delivery", "don giao hang online");
        else if (!PayCash(-net, "delivery", "chi phi giao hang online")) debt += -net;
        deliveriesTotal += n;
        if (n >= 4) rep += 1;
        return " Giao hang (offline): " + n + " don, +$" + net + " (da tru xang $" + (n * ShipFuel) + ").";
    }

    // ====== BAT DONG SAN: mua toa nha / can ho / van phong that tren ban do, cho thue dai han hoac ngan han ======
    class Estate
    {
        public string Name; public float X, Y; public int Kind, Units, Base;   // Base = tien thue 1 don vi/ngay o khu he so 1.0
        public Estate(string n, float x, float y, int kind, int units, int b) { Name = n; X = x; Y = y; Kind = kind; Units = units; Base = b; }
    }
    static readonly string[] EstateKind = { "Can ho", "Nha pho", "Van phong", "Nha nghi", "Kho xuong" };
    static readonly string[] EstateUnit = { "can", "can", "san", "phong", "kho" };
    // toa do gan dung cua cac toa nha co san trong game goc (doi chieu cham tren ban do; chua kiem chung tung diem trong game)
    static readonly Estate[] Estates = {
        new Estate("Eclipse Towers (Rockford Hills)", -773f, 310f, 0, 6, 70),
        new Estate("Tinsel Towers", -619f, 37f, 0, 6, 65),
        new Estate("Del Perro Heights", -1447f, -538f, 0, 5, 60),
        new Estate("Richards Majestic", -937f, -379f, 0, 5, 60),
        new Estate("Weazel Plaza", -914f, -455f, 0, 5, 58),
        new Estate("4 Integrity Way (Trung tam)", -47f, -586f, 0, 6, 62),
        new Estate("3 Alta Street", -269f, -956f, 0, 5, 55),
        new Estate("Day nha Grove Street", 100f, -1925f, 1, 3, 45),
        new Estate("Nha pho Mirror Park", 1265f, -575f, 1, 2, 80),
        new Estate("Nha pho Kenh Vespucci", -1080f, -1000f, 1, 2, 85),
        new Estate("Nha pho Paleto Bay", -160f, 6430f, 1, 2, 60),
        new Estate("Van phong Maze Bank Tower", -75f, -818f, 2, 4, 150),
        new Estate("Van phong Arcadius", -117f, -605f, 2, 4, 140),
        new Estate("Van phong Lombank West", -1581f, -558f, 2, 3, 130),
        new Estate("Van phong Maze Bank West", -1370f, -503f, 2, 3, 130),
        new Estate("Pink Cage Motel", 326f, -210f, 3, 10, 30),
        new Estate("Motor Motel (Harmony)", 1140f, 2665f, 3, 8, 30),
        new Estate("Kho cang Elysian Island", 200f, -2900f, 4, 2, 140),
        new Estate("Kho xuong Cypress Flats", 870f, -2200f, 4, 2, 120) };
    static readonly string[] RentLvName = { "Gia thap (de cho thue)", "Gia chuan", "Gia cao (cho lau hon)" };
    static readonly float[] RentLvMul = { 0.85f, 1.0f, 1.2f };
    static readonly float[] RentLvFill = { 1.7f, 1.0f, 0.5f };
    const float ShortMul = 1.7f;      // ngan han (homestay/thue theo ngay) dat hon 1.7 lan moi dem
    const float PayDays = 180f;       // gia mua ~ 180 ngay tien thue day phong
    const float UpkeepRate = 0.0011f; // bao tri + thue nha dat moi ngay theo gia tri
    const float SellRate = 0.92f;     // ban lai mat 8% (moi gioi + thue chuyen nhuong)
    const float BrokerFee = 1.03f;    // mua tu xa qua moi gioi +3%
    const int ManagerWage = 60;       // quan ly toa nha, luong/ngay

    class Owned
    {
        public int E; public int Occ; public int Mode;   // Mode 0 = dai han (hop dong thang), 1 = ngan han (theo dem)
        public int Level = 1, RentLv = 1; public bool Manager;
        public int Value, Paid, LastNet, LastGross, TotalNet;
        public Blip Blip;
    }
    readonly List<Owned> owned = new List<Owned>();
    readonly List<Blip> estateListBlips = new List<Blip>();

    Vector3 EstPos(int e) { return new Vector3(Estates[e].X, Estates[e].Y, 0f); }
    int OwnedIdx(int e) { for (int i = 0; i < owned.Count; i++) if (owned[i].E == e) return i; return -1; }
    int UnitRent(Owned o) { Estate es = Estates[o.E]; return (int)Math.Round(es.Base * ZoneMult(EstPos(o.E)) * RentLvMul[o.RentLv] * (1f + 0.15f * (o.Level - 1))); }
    int EstatePrice(int e) { Estate es = Estates[e]; return (int)Math.Round(es.Base * es.Units * ZoneMult(EstPos(e)) * PayDays / 100f) * 100; }
    int Upkeep(Owned o) { return (int)(o.Value * UpkeepRate) + (o.Manager ? ManagerWage : 0); }
    int RenoCost(Owned o) { return (int)(o.Value * 0.15f / 100f) * 100; }

    void MakeEstateBlip(Owned o)
    {
        try
        {
            if (o.Blip != null && o.Blip.Exists()) o.Blip.Delete();
            o.Blip = World.CreateBlip(EstPos(o.E));
            o.Blip.Color = BlipColor.Blue;
            o.Blip.Name = "BDS cua Sep: " + Estates[o.E].Name;
        }
        catch (Exception ex) { Log("blip bds loi: " + ex.Message); }
    }

    // chay moi ngay game (ca luc bu offline): thu tien thue, tim khach moi, khach bo di / bung tien, bao tri, bien dong gia
    string DailyEstates(out int net)
    {
        net = 0;
        if (owned.Count == 0) return "";
        int gross = 0, cost = 0, lost = 0, moved = 0, newT = 0;
        float repBoost = 1f + Math.Max(-0.3f, Math.Min(0.3f, rep / 200f));
        foreach (Owned o in owned)
        {
            Estate es = Estates[o.E];
            int ur = UnitRent(o), g = 0;
            if (o.Mode == 0)
            {
                // dai han: khach o theo hop dong, tra deu; moi ngay co the co khach moi, khach het hop dong khong gia han, khach bung tien
                int occ = o.Occ;
                for (int u = 0; u < es.Units - occ; u++)
                    if (rnd.NextDouble() < 0.18 * RentLvFill[o.RentLv] * (1f + 0.15f * (o.Level - 1)) * (o.Manager ? 1.4f : 1f) * repBoost) { o.Occ++; newT++; }
                for (int u = 0; u < occ; u++)
                {
                    double r = rnd.NextDouble();
                    if (r < (o.Manager ? 0.0025 : 0.006)) { o.Occ--; lost++; continue; }                         // bung tien, bo di
                    if (r < 0.03 * (o.RentLv == 2 ? 1.5 : 1.0) / (o.Manager ? 1.3 : 1.0)) { o.Occ--; moved++; }   // het hop dong, chuyen di (van tra ngay cuoi)
                    g += ur;
                }
            }
            else
            {
                // ngan han: moi dem lap day ngau nhien, gia cao hon, ton phi don phong 18%; luc tat may khach it hon (OfflineFactor)
                double p = 0.55 * Math.Sqrt(RentLvFill[o.RentLv]) * (1f + 0.1f * (o.Level - 1)) * (o.Manager ? 1.15f : 1f) * repBoost * (catching ? OfflineFactor : 1f);
                p = Math.Min(0.95, p);
                int occ = 0;
                for (int u = 0; u < es.Units; u++) if (rnd.NextDouble() < p) occ++;
                o.Occ = occ;
                g = (int)(occ * ur * ShortMul);
                cost += (int)(g * 0.18f);
            }
            int up = Upkeep(o);
            cost += up;
            gross += g;
            int n = g - up - (o.Mode == 1 ? (int)(g * 0.18f) : 0);
            o.LastGross = g; o.LastNet = n; o.TotalNet += n;
            // gia tri bien dong nhe: trung binh +0,03%/ngay
            o.Value = Math.Max(1000, o.Value + (int)(o.Value * (0.0003 + (rnd.NextDouble() - 0.5) * 0.003)));
        }
        net = gross - cost;
        EarnCash(gross, "rental", "thu tien thue bat dong san");
        if (!PayCash(cost, "maintenance", "bao tri bat dong san")) debt += cost;
        string r2 = " Bat dong san: thu thue $" + gross + ", bao tri/quan ly/don phong -$" + cost + ", rong $" + net + ".";
        if (newT > 0) r2 += " " + newT + " khach thue moi.";
        if (moved > 0) r2 += " " + moved + " khach het hop dong chuyen di.";
        if (lost > 0) r2 += " ~r~" + lost + " khach bung tien bo di.~s~";
        return r2;
    }

    void ShowEstates()
    {
        Begin("BAT DONG SAN  (mua, cho thue toa nha that)", delegate { ShowBiz(); });
        int val = 0, net = 0, units = 0, occ = 0;
        foreach (Owned o in owned) { val += o.Value; net += o.LastNet; units += Estates[o.E].Units; occ += o.Occ; }
        Info("Dang so huu: " + owned.Count + " noi, " + occ + "/" + units + " don vi co khach, tong gia tri $" + val + ", loi rong ngay qua $" + net);
        for (int i = 0; i < owned.Count; i++)
        {
            int oi = i; Owned o = owned[i]; Estate es = Estates[o.E];
            Item(es.Name + " [" + EstateKind[es.Kind] + "]  " + o.Occ + "/" + es.Units + " " + EstateUnit[es.Kind] + ", " + (o.Mode == 0 ? "dai han" : "ngan han") + ", ngay qua $" + o.LastNet, delegate { ShowOwned(oi); });
        }
        Item("Mua bat dong san...  (" + (Estates.Length - owned.Count) + " noi dang rao ban)", delegate { ShowEstateMarket(); });
        Item(estateListBlips.Count > 0 ? "Xoa danh dau BDS dang rao ban tren ban do" : "Danh dau BDS dang rao ban tren ban do", delegate
        {
            if (estateListBlips.Count > 0) { foreach (Blip b in estateListBlips) if (b != null && b.Exists()) b.Delete(); estateListBlips.Clear(); }
            else
                for (int e = 0; e < Estates.Length; e++)
                {
                    if (OwnedIdx(e) >= 0) continue;
                    try { Blip bl = World.CreateBlip(EstPos(e)); bl.Color = BlipColor.Yellow; bl.Name = "BDS rao ban: " + Estates[e].Name; estateListBlips.Add(bl); } catch { }
                }
            ShowEstates();
        });
        Info("Dai han: khach o theo hop dong, tien deu, lap day cham. Ngan han: gia dem x" + ShortMul.ToString("0.0", CultureInfo.InvariantCulture) + ", lap day ngau nhien, ton phi don phong. Khi tat may: dai han thu du, ngan han bang " + (int)(OfflineFactor * 100) + "%.");
        Item("Quay lai", delegate { ShowBiz(); });
    }

    void ShowEstateMarket()
    {
        Begin("BDS DANG RAO BAN", delegate { ShowEstates(); });
        Vector3 pp = Game.Player.Character.Position;
        Info("Den tan noi mua theo gia goc; mua tu xa qua moi gioi +" + (int)Math.Round((BrokerFee - 1f) * 100f) + "%. Toa do gan dung.");
        for (int e = 0; e < Estates.Length; e++)
        {
            if (OwnedIdx(e) >= 0) continue;
            int ei = e; Estate es = Estates[e];
            int rent = (int)Math.Round(es.Base * ZoneMult(EstPos(e)));
            Item(es.Name + " [" + EstateKind[es.Kind] + ", " + es.Units + " " + EstateUnit[es.Kind] + "]  $" + EstatePrice(e) + "  (thue ~$" + rent + "/" + EstateUnit[es.Kind] + "/ngay, " + ZoneName(EstPos(e)) + ", " + (int)pp.DistanceTo2D(EstPos(e)) + " m)", delegate { ShowBuyEstate(ei); });
        }
        Item("Quay lai", delegate { ShowEstates(); });
    }

    void ShowBuyEstate(int e)
    {
        Begin("MUA: " + Estates[e].Name, delegate { ShowEstateMarket(); });
        Estate es = Estates[e];
        bool near = Game.Player.Character.Position.DistanceTo2D(EstPos(e)) < 150f;
        int price = EstatePrice(e), pay = near ? price : (int)(price * BrokerFee);
        int rent = (int)Math.Round(es.Base * ZoneMult(EstPos(e)));
        Info(EstateKind[es.Kind] + ", " + es.Units + " " + EstateUnit[es.Kind] + " o " + ZoneName(EstPos(e)) + ". Gia $" + price + (near ? " (Sep dang o tai cho)" : ", mua tu xa $" + pay));
        Info("Day khach dai han: ~$" + (rent * es.Units) + "/ngay, bao tri + thue nha dat ~$" + (int)(price * UpkeepRate) + "/ngay. Hoan von ~" + (int)(PayDays * 1.15f) + " ngay neu day khach.");
        Item("Dat diem den toi day", delegate { Function.Call(Hash.SET_NEW_WAYPOINT, es.X, es.Y); Notify("Da dat diem den: ~y~" + es.Name + "~s~."); ShowBuyEstate(e); });
        Item("Mua cho thue DAI HAN  -$" + pay, delegate { BuyEstate(e, pay, 0); });
        Item("Mua cho thue NGAN HAN (theo dem)  -$" + pay, delegate { BuyEstate(e, pay, 1); });
        Item("Quay lai", delegate { ShowEstateMarket(); });
    }

    void BuyEstate(int e, int pay, int mode)
    {
        if (OwnedIdx(e) >= 0) { ShowEstates(); return; }
        if (Game.Player.Money < pay) { Notify("~r~Khong du tien~s~: can $" + pay + ", Sep co $" + Game.Player.Money + "."); ShowBuyEstate(e); return; }
        if (!PayCash(pay, "property", "mua bat dong san")) { Notify("Giao dich khong thanh cong."); return; }
        Owned o = new Owned { E = e, Mode = mode, Value = EstatePrice(e), Paid = pay, Occ = 0 };
        owned.Add(o);
        MakeEstateBlip(o);
        Notify("~g~Da mua " + Estates[e].Name + "~s~ (-$" + pay + "). Dang rao cho thue " + (mode == 0 ? "dai han" : "ngan han") + ".");
        Log("mua bds " + Estates[e].Name + " $" + pay);
        PostEvent("Nghe đồn có gã vừa mua " + Estates[e].Name + " để cho thuê.");
        Save();
        ShowOwned(owned.Count - 1);
    }

    void ShowOwned(int i)
    {
        if (i < 0 || i >= owned.Count) { ShowEstates(); return; }
        Owned o = owned[i]; Estate es = Estates[o.E];
        Begin(es.Name.ToUpper(), delegate { ShowEstates(); });
        Info(EstateKind[es.Kind] + " o " + ZoneName(EstPos(o.E)) + ": " + o.Occ + "/" + es.Units + " " + EstateUnit[es.Kind] + " co khach. Gia thue $" + UnitRent(o) + (o.Mode == 1 ? " x" + ShortMul.ToString("0.0", CultureInfo.InvariantCulture) + "/dem" : "/ngay"));
        Info("Gia tri hien tai $" + o.Value + " (mua $" + o.Paid + "). Ngay qua: thu $" + o.LastGross + ", rong $" + o.LastNet + ". Tong lai tu khi mua $" + o.TotalNet + ".");
        Item("Hinh thuc: " + (o.Mode == 0 ? "DAI HAN (hop dong thang)" : "NGAN HAN (theo dem)") + "  - bam de doi", delegate
        {
            o.Mode = 1 - o.Mode;
            if (o.Mode == 0) o.Occ = 0;   // chuyen sang dai han phai tim khach lai tu dau
            Notify("Da chuyen sang cho thue " + (o.Mode == 0 ? "dai han, dang tim khach" : "ngan han") + "."); ShowOwned(i);
        });
        Item("Muc gia: " + RentLvName[o.RentLv] + "  - bam de doi", delegate { o.RentLv = (o.RentLv + 1) % 3; ShowOwned(i); });
        Item(o.Manager ? "Cho quan ly toa nha nghi viec (dang tra $" + ManagerWage + "/ngay)" : "Thue quan ly toa nha  ($" + ManagerWage + "/ngay: de cho thue hon, it khach bung tien)", delegate { o.Manager = !o.Manager; ShowOwned(i); });
        if (o.Level < 3)
            Item("Sua sang len cap " + (o.Level + 1) + " (+15% gia thue, them khach)  -$" + RenoCost(o), delegate
            {
                int c = RenoCost(o);
                if (Spend(c)) { o.Level++; o.Value += c * 8 / 10; Notify("~g~Da sua sang " + es.Name + "~s~ len cap " + o.Level + "."); }
                ShowOwned(i);
            });
        else Info("Da sua sang toi da (cap 3).");
        Item("Dat diem den toi day", delegate { Function.Call(Hash.SET_NEW_WAYPOINT, es.X, es.Y); Notify("Da dat diem den: ~y~" + es.Name + "~s~."); ShowOwned(i); });
        Item("Ban lai  (+$" + (int)(o.Value * SellRate) + ", mat 8% moi gioi + thue)...", delegate
        {
            Begin("BAN " + es.Name.ToUpper() + "?", delegate { ShowOwned(i); });
            Info("Nhan $" + (int)(o.Value * SellRate) + ". Khach dang thue se chuyen sang chu moi.");
            Item("Dong y ban", delegate
            {
                int got = (int)(o.Value * SellRate);
                EarnCash(got, "property", "ban bat dong san");
                if (o.Blip != null && o.Blip.Exists()) o.Blip.Delete();
                owned.Remove(o);
                Notify("~g~Da ban " + es.Name + "~s~ +$" + got + " (lai/lo so voi gia mua: $" + (got - o.Paid + o.TotalNet) + " tinh ca tien thue da thu).");
                Save();
                ShowEstates();
            });
            Item("Thoi, giu lai", delegate { ShowOwned(i); });
        });
        Item("Quay lai", delegate { ShowEstates(); });
    }

    // cau noi voi file Kinh te (GtaVietLifeEconomy.cs): cong bo tong gia tri BDS de the chap, nhan lenh phat mai khi bi siet no
    int nextEconBus;
    void EconBus()
    {
        try
        {
            AppDomain d = AppDomain.CurrentDomain;
            int total = 0; foreach (Owned o in owned) total += o.Value;
            d.SetData("GVL.Assets", total);
            object sz = d.GetData("GVL.Seize");
            if (!(sz is int) || (int)sz <= 0) return;
            int need = (int)sz;
            d.SetData("GVL.Seize", 0);
            string sold = "";
            while (need > 0 && owned.Count > 0)
            {
                int bi = 0; for (int i = 1; i < owned.Count; i++) if (owned[i].Value > owned[bi].Value) bi = i;
                Owned o = owned[bi];
                int got = (int)(o.Value * 0.8f);   // phat mai re hon ban thuong
                need -= got;
                if (need < 0) EarnCash(-need, "property", "hoan tien du khi xu ly no");
                if (o.Blip != null && o.Blip.Exists()) o.Blip.Delete();
                owned.RemoveAt(bi);
                sold += (sold.Length > 0 ? ", " : "") + Estates[o.E].Name;
            }
            if (sold.Length > 0) { Notify("~r~Ngan hang phat mai tai san the chap:~s~ " + sold + "."); Log("phat mai: " + sold); Save(); }
        }
        catch (Exception ex) { Log("econ bus loi: " + ex.Message); }
    }

    // dich vu xe cua chinh Sep: lai xe toi tiem rua xe / sua xe cua minh thi duoc lam mien phi
    void ServeOwnCar()
    {
        Ped me = Game.Player.Character;
        Vehicle v = me.CurrentVehicle;
        if (v == null || !v.Exists()) { Notify("Lai xe toi day roi bam lai."); return; }
        try
        {
            Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, v, 0f);
            if (bizType == 7) Function.Call(Hash.SET_VEHICLE_FIXED, v);
            if (bizStock > 0) bizStock--;
            Notify("~g~Xe da duoc " + (bizType == 7 ? "sua va rua" : "rua") + " sach~s~ (tru 1 luot vat tu).");
        }
        catch (Exception ex) { Log("dich vu xe loi: " + ex.Message); }
    }

    // ====== chay 24/24: khi bat game lai, tinh phan quan da hoat dong luc tat may (ban cham hon luc choi) ======
    const float OfflineFactor = 0.6f;        // luc tat may ban bang 60% so khach moi ngay luc dang choi (Sep chon; neu nhanh qua thi giam)
    const double GameDaySeconds = 2880.0;    // 1 ngay trong game = 48 phut thuc
    const int MaxOfflineDays = 14;
    bool catching, caughtUp; long lastSeen; int nextSeenSave;
    readonly List<string> catchLog = new List<string>();

    static long NowEpoch() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }

    // ====== lenh giao dich tu dien thoai: dashboard.py ghi GtaVietLifeCmd.txt, game doc va ap dung ======
    int nextCmdCheck;
    readonly List<string[]> cmdResults = new List<string[]>();   // id, ok/fail, noi dung (hien lai tren dien thoai)

    void ApplyPhoneCmds()
    {
        const string F = "GtaVietLifeCmd.txt", P = "GtaVietLifeCmd.proc";
        try
        {
            if (!File.Exists(F)) return;
            if (File.Exists(P)) File.Delete(P);
            File.Move(F, P);
            string[] lines = File.ReadAllLines(P);
            File.Delete(P);
            if (lines.Length == 0) return;
            SyncOut();
            int okN = 0, failN = 0;
            foreach (string ln in lines)
            {
                string[] q = ln.Split('|');
                if (q.Length < 3) continue;
                string id = q[0], op = q[1], res;
                int idx, arg = 0;
                if (!int.TryParse(q[2], out idx) || idx < 0 || idx >= shops.Count) { res = "fail|Khong co quan nay"; }
                else
                {
                    if (q.Length > 3) int.TryParse(q[3], out arg);
                    Shop sh = shops[idx];
                    string nm = "Quan " + (idx + 1);
                    if (op == "restock")
                    {
                        int packs = arg == 10 ? 10 : arg == 5 ? 5 : 1;
                        int cost = PackCost(sh.Type, packs, true);
                        if (Game.Player.Money < cost) res = "fail|" + nm + ": thieu tien nhap hang ($" + cost + ")";
                        else if (PayCash(cost, "inventory", "nhap hang " + nm)) { sh.Stock += StockPack * packs; sh.OutNotified = false; res = "ok|" + nm + ": nhap +" + (StockPack * packs) + " phan (-$" + cost + ")"; } else res = "fail|" + nm + ": giao dich khong thanh cong";
                    }
                    else if (op == "hire")
                    {
                        if (sh.Staff >= MaxStaff) res = "fail|" + nm + ": da du nhan vien";
                        else { sh.Staff++; res = "ok|" + nm + ": thue them 1 nhan vien (luong $" + WagePerDay + "/ngay)"; }
                    }
                    else if (op == "fire")
                    {
                        if (sh.Staff <= 0) res = "fail|" + nm + ": khong con nhan vien";
                        else { sh.Staff--; res = "ok|" + nm + ": sa thai 1 nhan vien"; }
                    }
                    else if (op == "upgrade")
                    {
                        int cost = 2500 * sh.Level;
                        if (sh.Level >= CapOf(sh)) res = "fail|" + nm + ": da cap toi da";
                        else if (Game.Player.Money < cost) res = "fail|" + nm + ": thieu tien nang cap ($" + cost + ")";
                        else if (PayCash(cost, "investment", "nang cap " + nm)) { sh.Level++; res = "ok|" + nm + ": nang cap len cap " + sh.Level + " (-$" + cost + ")"; } else res = "fail|" + nm + ": giao dich khong thanh cong";
                    }
                    else if (op == "collect")
                    {
                        int c = sh.Cash;
                        if (c <= 0) res = "fail|" + nm + ": quy quan trong";
                        else { sh.Cash = 0; EarnCash(c, "business", "rut tien quan " + nm); res = "ok|" + nm + ": rut $" + c + " ve tui"; }
                    }
                    else res = "fail|Lenh la";
                }
                string[] rr = res.Split('|');
                if (rr[0] == "ok") okN++; else failN++;
                cmdResults.Add(new string[] { id, rr[0], rr[1] });
                Log("[dien thoai] " + op + " -> " + res);
            }
            while (cmdResults.Count > 10) cmdResults.RemoveAt(0);
            if (activeShop >= 0 && activeShop < shops.Count) { SyncIn(activeShop); rebuildAt = Game.GameTime + 500; }
            if (okN + failN > 0) Notify("~b~Lenh tu dien thoai:~s~ " + okN + " xong" + (failN > 0 ? ", ~r~" + failN + " that bai~s~" : "") + ".");
            Save();
        }
        catch (Exception ex) { Log("lenh dien thoai loi: " + ex.Message); }
    }

    void CatchUpOffline()
    {
        caughtUp = true;
        if (lastSeen <= 0 || (shops.Count == 0 && contracts.Count == 0 && receivables.Count == 0 && !hasHome && owned.Count == 0 && !jobGiaoHang)) return;
        long el = NowEpoch() - lastSeen;
        if (el < 600) return;
        int days = (int)Math.Min(MaxOfflineDays, el / GameDaySeconds);
        if (days < 1) return;
        int before = Game.Player.Money, debtBefore = debt, shopsBefore = shops.Count;
        catching = true; catchLog.Clear();
        try { for (int d = 0; d < days; d++) DailyTick(); }
        catch (Exception ex) { Log("catch-up loi: " + ex.Message); }
        catching = false;
        foreach (string r in catchLog) Log("[offline] " + r);
        int delta = Game.Player.Money - before;
        string hours = (el / 3600.0).ToString("0.0", CultureInfo.InvariantCulture);
        Notify("~b~Kinh doanh chay luc Sep tat may~s~ (" + hours + " gio thuc = " + days + " ngay game, ban bang 60% luc dang choi): tien " + (delta >= 0 ? "~g~+$" : "~r~-$") + Math.Abs(delta) + "~s~" + (debt > debtBefore ? ", no tang $" + (debt - debtBefore) : "") + (shops.Count < shopsBefore ? ", ~r~mat " + (shopsBefore - shops.Count) + " mat bang vi thieu tien thue~s~" : "") + ".");
        Save();
    }

    // ====== nhieu quan + cac hinh thuc thue mat bang ======
    class Shop
    {
        public Vector3 Pos; public float Heading, Ground;
        public int Level = 1, Stock, Staff, Cash, SoldToday;
        public int Mode, Rent, DaysLeft, Overdue, Paid;
        public int LastProfit, LastRev;
        public int Type, RevToday, CogsToday;   // loai hinh (Types), doanh thu va gia von hom nay
        public bool Indoor;
        public int NextSale;       // moc thoi gian ban hang tu dong cua quan o xa
        public bool OutNotified;
        public Blip Blip;
    }
    readonly List<Shop> shops = new List<Shop>();
    int activeShop = -1;
    int nextShopCheck;
    static readonly string[] ModeName = { "Via he", "Thue ki-ot theo ngay", "Hop dong thue 30 ngay", "Mua dut" };
    static readonly float[] ModeTraffic = { 0.8f, 1.0f, 1.1f, 1.2f };
    static readonly int[] ModeCap = { 2, 3, 4, 4 };
    static readonly int[] ModeSetup = { 400, 800, 2000, 25000 };
    static readonly int[] ModeRent = { 20, 90, 60, 0 };
    const float ShopGap = 150f;   // hai quan cach nhau it nhat the nay; quan tu dung len khi lai gan trong 90 m

    static string FeeWord(int mode) { return mode == 0 ? "phi via he" : "tien thue"; }

    static readonly Dictionary<string, KeyValuePair<string, float>> Zones = new Dictionary<string, KeyValuePair<string, float>> {
        { "DOWNT", new KeyValuePair<string, float>("Trung tam", 1.5f) }, { "LEGSQU", new KeyValuePair<string, float>("Quang truong Legion", 1.5f) },
        { "VINE", new KeyValuePair<string, float>("Vinewood", 1.4f) }, { "DTVINE", new KeyValuePair<string, float>("Trung tam Vinewood", 1.4f) },
        { "ROCKF", new KeyValuePair<string, float>("Rockford Hills", 1.4f) }, { "BEACH", new KeyValuePair<string, float>("Bai bien Vespucci", 1.5f) },
        { "VESPB", new KeyValuePair<string, float>("Vespucci", 1.4f) }, { "VCANA", new KeyValuePair<string, float>("Kenh Vespucci", 1.3f) },
        { "DELBE", new KeyValuePair<string, float>("Del Perro", 1.3f) }, { "DELPE", new KeyValuePair<string, float>("Del Perro", 1.3f) },
        { "MORN", new KeyValuePair<string, float>("Morningwood", 1.2f) }, { "PBOX", new KeyValuePair<string, float>("Pillbox", 1.2f) },
        { "MIRR", new KeyValuePair<string, float>("Mirror Park", 1.1f) }, { "STRAW", new KeyValuePair<string, float>("Strawberry", 1.0f) },
        { "EAST_V", new KeyValuePair<string, float>("East Vinewood", 1.0f) }, { "DAVIS", new KeyValuePair<string, float>("Davis", 0.9f) },
        { "SANDY", new KeyValuePair<string, float>("Sandy Shores", 0.6f) }, { "PALETO", new KeyValuePair<string, float>("Paleto Bay", 0.6f) },
        { "GRAPES", new KeyValuePair<string, float>("Grapeseed", 0.5f) } };

    string ZoneName(Vector3 p)
    {
        string code = "";
        try { code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, p.X, p.Y, p.Z); } catch { }
        KeyValuePair<string, float> z;
        if (code != null && Zones.TryGetValue(code, out z)) return z.Key;
        return string.IsNullOrEmpty(code) ? "Khu dan cu" : code;
    }
    float ZoneMult(Vector3 p)
    {
        string code = "";
        try { code = Function.Call<string>(Hash.GET_NAME_OF_ZONE, p.X, p.Y, p.Z); } catch { }
        KeyValuePair<string, float> z;
        if (code != null && Zones.TryGetValue(code, out z)) return z.Value;
        return 0.8f;
    }

    void MakeShopBlip(Shop sh)
    {
        try
        {
            if (sh.Blip != null && sh.Blip.Exists()) sh.Blip.Delete();
            sh.Blip = World.CreateBlip(sh.Pos);
            sh.Blip.Color = BlipColor.Green;
            sh.Blip.Name = "Quan cua Sep (" + ModeName[sh.Mode] + ")";
        }
        catch (Exception ex) { Log("blip loi: " + ex.Message); }
    }

    void SyncOut()
    {
        if (activeShop < 0 || activeShop >= shops.Count) return;
        Shop sh = shops[activeShop];
        sh.Pos = bizPos; sh.Heading = bizHeading; sh.Ground = bizGround; sh.Indoor = bizIndoor;
        sh.Level = bizLevel; sh.Stock = bizStock; sh.Staff = bizStaff; sh.Cash = bizCash; sh.SoldToday = soldToday;
        sh.Type = bizType; sh.RevToday = bizRev; sh.CogsToday = bizCogs;
    }

    void SyncIn(int i)
    {
        Shop sh = shops[i];
        activeShop = i; hasBiz = true;
        bizPos = sh.Pos; bizHeading = sh.Heading; bizGround = sh.Ground; bizIndoor = sh.Indoor;
        bizLevel = sh.Level; bizStock = sh.Stock; bizStaff = sh.Staff; bizCash = sh.Cash; soldToday = sh.SoldToday;
        bizType = sh.Type; bizRev = sh.RevToday; bizCogs = sh.CogsToday;
    }

    void ClearStaff()
    {
        foreach (Ped p in staffPeds) if (p != null && p.Exists()) p.Delete();
        staffPeds.Clear(); staffSlots.Clear(); staffStates.Clear(); staffTasks.Clear();
    }

    void ActivateShop(int i)
    {
        if (i < 0 || i >= shops.Count || i == activeShop) return;
        SyncOut();
        ClearStaff();
        ClearBizProps();
        SyncIn(i);
        BuildBizProps();
        for (int k = 0; k < Math.Min(bizStaff, MaxVisibleStaff); k++) SpawnStaff();
        Log("chuyen sang quan " + (i + 1));
    }

    void RemoveShop(int i)
    {
        Shop sh = shops[i];
        if (sh.Blip != null && sh.Blip.Exists()) sh.Blip.Delete();
        if (i == activeShop)
        {
            ClearStaff(); ClearBizProps();
            activeShop = -1; hasBiz = false;
        }
        else if (i < activeShop) activeShop--;
        shops.RemoveAt(i);
    }

    // ke thua du lieu 1 quan cu (khoa hasBiz/biz trong file luu) sang danh sach, chon quan gan nguoi choi nhat lam quan dang chay
    void InitShops()
    {
        if (shops.Count == 0 && hasBiz)
        {
            Shop sh = new Shop();
            sh.Pos = bizPos; sh.Heading = bizHeading; sh.Ground = bizGround; sh.Level = bizLevel; sh.Stock = bizStock; sh.Staff = bizStaff; sh.Cash = bizCash;
            sh.Mode = 3; sh.Paid = ModeSetup[3];
            shops.Add(sh);
        }
        if (shops.Count == 0) { hasBiz = false; activeShop = -1; return; }
        int best = 0; float bd = 1e9f;
        Vector3 pp = Game.Player.Character.Position;
        for (int i = 0; i < shops.Count; i++) { float d = pp.DistanceTo2D(shops[i].Pos); if (d < bd) { bd = d; best = i; } }
        SyncIn(best);
    }

    string menuNote = "";
    void Refuse(string msg) { menuNote = msg; Notify("~r~" + msg + "~s~"); Log("tu choi mo quan: " + msg); }

    bool IsIndoor(Ped p)
    {
        try { return Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, p) != 0; } catch { return false; }
    }

    // trong nha: dat quan truoc mat Sep 2,5 m, mat san = chan Sep (khong do mat dat ngoai duong)
    void PlaceBizIndoor()
    {
        Ped me = Game.Player.Character;
        bizPos = me.Position + me.ForwardVector * 2.5f;
        bizHeading = me.Heading + 180f;
        bizGround = me.Position.Z - 1.0f;
        Log("quan trong nha tai " + bizPos.X.ToString("0.0") + "," + bizPos.Y.ToString("0.0") + " san z=" + bizGround.ToString("0.00"));
    }

    int CapOf(Shop sh) { return sh.Indoor ? Math.Min(3, ModeCap[sh.Mode]) : ModeCap[sh.Mode]; }

    void OpenShop(int mode)
    {
        Ped me = Game.Player.Character;
        menuNote = "";
        if (me.IsInVehicle()) { Refuse("Xuong xe roi hay mo quan."); return; }
        foreach (Shop o in shops)
            if (me.Position.DistanceTo2D(o.Pos) < ShopGap) { Refuse("Khong mo duoc: dang cach quan #" + (shops.IndexOf(o) + 1) + " chi " + (int)me.Position.DistanceTo2D(o.Pos) + " m. Phai cach it nhat " + (int)ShopGap + " m - hay di xa hon hoac dung 'Tim mat bang'."); return; }
        float zm = ZoneMult(me.Position);
        bool indoor = IsIndoor(me);
        BizType ptype = Types[TIdx(pickType)];
        if (mode < ptype.MinMode) { Refuse(ptype.Name + " can mat bang thue (Thue ki-ot tro len), khong mo via he duoc."); return; }
        int setup = (int)(ModeSetup[mode] * (mode == 3 ? zm * (indoor ? 1.5f : 1f) : 1f) * ptype.Capex);
        if (Game.Player.Money < setup) { Refuse("Khong du tien: can $" + setup + ", Sep co $" + Game.Player.Money + "."); return; }
        if (!Spend(setup)) return;
        SyncOut();
        if (hasBiz) { ClearStaff(); ClearBizProps(); }
        Shop sh = new Shop();
        sh.Mode = mode; sh.Paid = setup; sh.Rent = (int)(ModeRent[mode] * zm * (indoor ? 1.3f : 1f) * ptype.RentMul); sh.Type = TIdx(pickType); sh.DaysLeft = mode == 2 ? 30 : 0;
        shops.Add(sh);
        activeShop = shops.Count - 1;
        hasBiz = true;
        bizIndoor = indoor;
        if (indoor) PlaceBizIndoor(); else PlaceBizOnFlatGround();
        bizLevel = 1; bizStock = 30; bizStaff = 0; bizCash = 0; soldToday = 0; bizType = TIdx(pickType); bizRev = 0; bizCogs = 0;
        SyncOut();
        MakeShopBlip(sh);
        BuildBizProps();
        Notify("~g~Da mo quan #" + shops.Count + "~s~ (" + ModeName[mode] + (indoor ? ", trong nha" : "") + ") o " + ZoneName(bizPos) + ". Tang 30 phan hang. Dung gan quan bam " + MK + " de quan ly.");
        PostEvent("Có gã vừa mở một quán nhỏ ở " + SafeStreet(bizPos) + ".");
    }

    // cac khu mat bang dang cho thue (toa do gan dung, den noi la chon hinh thuc va mo quan)
    static readonly object[][] Listings = {
        new object[] { "Quang truong Legion", 195f, -934f },
        new object[] { "Vinewood Boulevard", 300f, 190f },
        new object[] { "Bai bien Vespucci", -1340f, -1170f },
        new object[] { "Cau tau Del Perro", -1850f, -1235f },
        new object[] { "Pillbox Hill", 250f, -600f },
        new object[] { "Mirror Park", 1050f, -700f },
        new object[] { "Sandy Shores", 1960f, 3740f },
        new object[] { "Grapeseed", 1700f, 4800f },
        new object[] { "Paleto Bay", -100f, 6450f },
        new object[] { "Vanilla Unicorn (nha co noi that)", 127f, -1298f, true },
        new object[] { "Tequi-la-la (nha co noi that)", -564f, 275f, true },
        new object[] { "Yellow Jack Inn (nha co noi that)", 1991f, 3051f, true },
        // toa nha that cua game goc (cua hang co san tren ban do, toa do gan dung, doi chieu bang cham tren ban do)
        new object[] { "24/7 Strawberry (Innocence Blvd)", 24.5f, -1346.9f, "real" },
        new object[] { "24/7 Little Seoul", -707.4f, -914.2f, "real" },
        new object[] { "24/7 Vinewood (Clinton Ave)", 373.8f, 326.5f, "real" },
        new object[] { "24/7 Banham Canyon", -1820.5f, 792.7f, "real" },
        new object[] { "24/7 Chumash", -3039.1f, 585.2f, "real" },
        new object[] { "24/7 Chumash (Barbareno Rd)", -3243.9f, 1000.4f, "real" },
        new object[] { "24/7 Tataviam", 2557.4f, 382.3f, "real" },
        new object[] { "24/7 Harmony", 547.4f, 2671.7f, "real" },
        new object[] { "24/7 Sandy Shores", 1961.1f, 3740.7f, "real" },
        new object[] { "24/7 Mount Chiliad", 1729.5f, 6414.1f, "real" },
        new object[] { "LTD Gasoline Davis", -47.4f, -1757.5f, "real" },
        new object[] { "LTD Gasoline Mirror Park", 1163.4f, -323.8f, "real" },
        new object[] { "Los Santos Customs Burton", -361.8f, -132.6f, "real" },
        new object[] { "Los Santos Customs La Mesa (sua xe)", 731.8f, -1088.8f, "real" },
        new object[] { "Tiem rua xe Strawberry (rua xe)", 25.0f, -1392.0f, "real" },
        new object[] { "Tiem rua xe Little Seoul (rua xe)", -699.6f, -932.7f, "real" },
        new object[] { "Tiem toc Davis (cat toc)", 136.8f, -1708.4f, "real" },
        new object[] { "Tiem toc Rockford Hills (cat toc)", -814.3f, -183.8f, "real" },
        new object[] { "Tiem toc Hawick (cat toc)", -32.9f, -152.3f, "real" } };
    readonly List<Blip> listingBlips = new List<Blip>();

    void ShowListings()
    {
        Begin("TIM MAT BANG KINH DOANH", delegate { ShowOpenMenu(); });
        Vector3 pp = Game.Player.Character.Position;
        Info("Chon mot khu de dat diem den tren ban do, den noi roi mo quan (menu " + MK + " > Kinh doanh)");
        for (int i = 0; i < Listings.Length; i++)
        {
            string nm = (string)Listings[i][0];
            Vector3 lp = new Vector3((float)Listings[i][1], (float)Listings[i][2], 0f);
            float zm = ZoneMult(lp);
            bool taken = false;
            foreach (Shop sh in shops) if (sh.Pos.DistanceTo2D(lp) < ShopGap) taken = true;
            int idx = i;
            Item(nm + (taken ? " [da co quan cua Sep]" : "") + (Listings[i].Length > 3 ? (Listings[i][3] is bool ? " [vao ben trong roi mo quan]" : " [TOA NHA THAT cua game]") : "") + "  (" + ZoneName(lp) + ", khach x" + zm.ToString("0.0", CultureInfo.InvariantCulture) + ", thue $" + (int)(ModeRent[1] * zm) + "/ngay, mua dut $" + (int)(ModeSetup[3] * zm) + ", " + (int)pp.DistanceTo2D(lp) + " m)", delegate
            {
                Vector3 q = new Vector3((float)Listings[idx][1], (float)Listings[idx][2], 0f);
                Function.Call(Hash.SET_NEW_WAYPOINT, q.X, q.Y);
                Notify("Da dat diem den: ~y~" + (string)Listings[idx][0] + "~s~. Den noi roi mo quan.");
                ShowListings();
            });
        }
        Item("Danh dau tat ca mat bang tren ban do", delegate
        {
            foreach (Blip b in listingBlips) if (b != null && b.Exists()) b.Delete();
            listingBlips.Clear();
            for (int j = 0; j < Listings.Length; j++)
            {
                Blip bl = World.CreateBlip(new Vector3((float)Listings[j][1], (float)Listings[j][2], 0f));
                bl.Color = BlipColor.Yellow;
                bl.Name = "Mat bang cho thue: " + (string)Listings[j][0];
                if (Listings[j].Length > 3 && !(Listings[j][3] is bool)) bl.Sprite = BlipSprite.Store;
                listingBlips.Add(bl);
            }
            Notify("Da danh dau " + listingBlips.Count + " khu mat bang (cham vang tren ban do).");
            ShowListings();
        });
        Item("Xoa danh dau tren ban do", delegate
        {
            foreach (Blip b in listingBlips) if (b != null && b.Exists()) b.Delete();
            listingBlips.Clear();
            ShowListings();
        });
        Item("Quay lai", delegate { ShowOpenMenu(); });
    }

    void ShowOpenMenu()
    {
        Vector3 pp = Game.Player.Character.Position;
        float zm = ZoneMult(pp);
        Begin("MO QUAN MOI", delegate { ShowBiz(); });
        if (menuNote.Length > 0) { Info("!! " + menuNote); menuNote = ""; }
        Info("Dang dung o: " + ZoneName(pp) + "  (dong khach x" + zm.ToString("0.0", CultureInfo.InvariantCulture) + ")");
        Item("Tim mat bang cho thue o khu khac...", delegate { ShowListings(); });
        BizType pt = Types[TIdx(pickType)];
        Item("Loai hinh: " + pt.Name + " [" + (pt.Channel == 0 ? "GT" : "MT") + "]  von x" + pt.Capex.ToString("0.0", CultureInfo.InvariantCulture) + "  - bam de doi...", delegate { ShowTypeMenu(); });
        Info(pt.Note + ". Mat hang: " + ItemNames(pt));
        for (int m = 0; m < 4; m++)
        {
            int mode = m;
            int setup = (int)(ModeSetup[m] * (m == 3 ? zm : 1f) * pt.Capex);
            int rent = (int)(ModeRent[m] * zm * pt.RentMul);
            string cond = m == 0 ? "phi via he $" + rent + "/ngay, toi da cap 2"
                : m == 1 ? "thue $" + rent + "/ngay, huy luc nao cung duoc, toi da cap 3"
                : m == 2 ? "dat coc, thue $" + rent + "/ngay, huy giua chung mat coc, toi da cap 4"
                : "tra mot lan, khong tien thue, ban lai 70%, toi da cap 4";
            string lockNote = m < pt.MinMode ? "  [can mat bang thue tu 'Thue ki-ot' tro len]" : "";
            Item(ModeName[m] + "  -$" + setup + "  (" + cond + ")" + lockNote, delegate { OpenShop(mode); if (menuNote.Length > 0) ShowOpenMenu(); else ShowBiz(); });
        }
        Item("Quay lai", delegate { ShowBiz(); });
    }

    int pickType;
    static string ItemNames(BizType t)
    {
        List<string> n = new List<string>(); foreach (int i in t.Items) n.Add(Catalog[i].Name);
        return string.Join(", ", n.ToArray());
    }
    void ShowTypeMenu()
    {
        Begin("CHON LOAI HINH KINH DOANH", delegate { ShowOpenMenu(); });
        for (int i = 0; i < Types.Length; i++)
        {
            int idx = i; BizType t = Types[i];
            string sec = SectorName[Catalog[t.Items[0]].Sector];
            Item(t.Name + "  [" + (t.Channel == 0 ? "GT" : "MT") + ", " + sec + "]  von x" + t.Capex.ToString("0.0", CultureInfo.InvariantCulture) + (i == pickType ? "   <- dang chon" : ""), delegate { pickType = idx; ShowOpenMenu(); });
        }
        Info("GT = kenh truyen thong (tap hoa, quan), MT = kenh hien dai (chuoi, sieu thi: von lon, dong khach, nop VAT). Dich vu: kho la vat tu, tinh theo luot.");
        Item("Quay lai", delegate { ShowOpenMenu(); });
    }

    const float RemoteFee = 1.10f;   // phi giao hang khi dat tu xa

    string StockTag(Shop sh)
    {
        return sh.Stock <= 0 ? "HET HANG" : sh.Stock <= 10 ? "SAP HET (" + sh.Stock + ")" : sh.Stock + " " + UW(sh.Type);
    }

    // bang kho: tong quan tat ca quan, chon quan de dat hang tu xa
    void ShowStockBoard()
    {
        SyncOut();
        Begin("BANG KHO CAC QUAN", delegate { ShowBiz(); });
        if (shops.Count == 0) { Info("Chua co quan nao."); Item("Quay lai", delegate { ShowBiz(); }); return; }
        int low = 0, lowCost = 0;
        for (int i = 0; i < shops.Count; i++)
        {
            Shop sh = shops[i];
            if (sh.Stock <= 10) { low++; lowCost += PackCost(sh.Type, 1, true); }
            int idx = i;
            Item("#" + (i + 1) + " " + ZoneName(sh.Pos) + " [" + ModeName[sh.Mode] + "]  kho: " + StockTag(sh) + "  (nv " + sh.Staff + ", hom nay ban " + sh.SoldToday + ", quy $" + sh.Cash + (i == activeShop ? ", dang o gan" : "") + ")", delegate { ShowStockShop(idx); });
        }
        if (low > 0)
            Item("Nhap hang cho tat ca quan sap het (" + low + " quan, +" + StockPack + " phan moi quan)  -$" + lowCost, delegate
            {
                int n = 0;
                for (int i = 0; i < shops.Count; i++) if (shops[i].Stock <= 10 && RemoteBuy(i, 1, true)) n++;
                Notify("Da dat hang cho " + n + " quan.");
                ShowStockBoard();
            });
        Item("Quay lai", delegate { ShowBiz(); });
    }

    void ShowStockShop(int idx)
    {
        if (idx < 0 || idx >= shops.Count) { ShowStockBoard(); return; }
        SyncOut();
        Shop sh = shops[idx];
        Begin("KHO QUAN #" + (idx + 1) + " - " + ZoneName(sh.Pos), delegate { ShowStockBoard(); });
        Info("Kho: " + StockTag(sh) + "   Nhan vien: " + sh.Staff + "   Quy tien: $" + sh.Cash + "   Phi giao hang tu xa +" + (int)((RemoteFee - 1f) * 100f) + "%");
        BizType bt = Types[TIdx(sh.Type)];
        Info("Loai hinh: " + bt.Name + " (" + (bt.Channel == 0 ? "GT" : "MT") + ")   gia von TB $" + UnitCost(sh.Type) + "/phan, gia ban TB $" + UnitPrice(sh.Type, sh.Level) + "/phan");
        Item("Dat hang +" + StockPack + " phan  (-$" + PackCost(sh.Type, 1, true) + ")", delegate { RemoteBuy(idx, 1, false); ShowStockShop(idx); });
        Item("Dat 5 lo  +" + (StockPack * 5) + " phan, chiet khau 7%  (-$" + PackCost(sh.Type, 5, true) + ")", delegate { RemoteBuy(idx, 5, false); ShowStockShop(idx); });
        Item("Dat 10 lo  +" + (StockPack * 10) + " phan, chiet khau 12%  (-$" + PackCost(sh.Type, 10, true) + ")", delegate { RemoteBuy(idx, 10, false); ShowStockShop(idx); });
        Item("Dat diem den toi quan nay", delegate { Function.Call(Hash.SET_NEW_WAYPOINT, shops[idx].Pos.X, shops[idx].Pos.Y); Notify("Da dat diem den toi quan #" + (idx + 1) + "."); ShowStockShop(idx); });
        Item("Quay lai bang kho", delegate { ShowStockBoard(); });
    }

    // dat hang tu xa: tra tien + phi giao hang, hang cong thang vao kho quan do
    bool RemoteBuy(int idx, int packs, bool quiet)
    {
        if (idx < 0 || idx >= shops.Count) return false;
        int cost = PackCost(shops[idx].Type, packs, true);
        if (Game.Player.Money < cost) { if (!quiet) Notify("Khong du tien ($" + cost + ")."); return false; }
        if (!PayCash(cost, "inventory", "nhap hang")) return false;
        int add = StockPack * packs;
        if (idx == activeShop) bizStock += add;
        shops[idx].Stock += add;
        if (idx == activeShop) shops[idx].Stock = bizStock;
        shops[idx].OutNotified = false;
        if (!quiet) Notify("~g~Da dat hang quan #" + (idx + 1) + ": +" + add + " phan~s~ (-$" + cost + "). Kho: " + shops[idx].Stock);
        return true;
    }

    void ShowShopList()
    {
        Begin("CAC QUAN CUA SEP", delegate { ShowBiz(); });
        Vector3 pp = Game.Player.Character.Position;
        for (int i = 0; i < shops.Count; i++)
        {
            Shop sh = shops[i];
            if (i == activeShop) SyncOut();
            int idx = i;
            Item("#" + (i + 1) + " " + ModeName[sh.Mode] + " - " + ZoneName(sh.Pos) + "  cap " + sh.Level + ", kho " + sh.Stock + ", nv " + sh.Staff + "  (" + (int)pp.DistanceTo2D(sh.Pos) + " m)  [dat diem den]", delegate
            {
                Function.Call(Hash.SET_NEW_WAYPOINT, shops[idx].Pos.X, shops[idx].Pos.Y);
                Notify("Da dat diem den toi quan #" + (idx + 1) + ".");
                ShowShopList();
            });
        }
        Item("Quay lai", delegate { ShowBiz(); });
    }

    void ConfirmCloseShop()
    {
        if (activeShop < 0) { ShowBiz(); return; }
        Shop sh = shops[activeShop];
        int refund = sh.Mode == 3 ? sh.Paid * 7 / 10 : (sh.Mode == 2 && sh.DaysLeft <= 0 ? sh.Paid : 0);
        Begin("DONG QUAN NAY?", delegate { ShowBiz(); });
        Item("Dong quan #" + (activeShop + 1) + " va nhan lai $" + refund + (sh.Mode == 2 && refund == 0 ? " (mat tien coc)" : ""), delegate
        {
            EarnCash(refund + bizCash, "business", "dong quan va hoan coc");
            Notify("Da dong quan, nhan $" + (refund + bizCash) + ".");
            RemoveShop(activeShop);
            ShowBiz();
        });
        Item("Thoi, giu lai", delegate { ShowBiz(); });
    }

    // Tim cho bang phang gan nguoi choi (quay + khu sau quay + khu ban ghe), tranh bac thang va vach cao do
    void PlaceBizOnFlatGround()
    {
        Ped me = Game.Player.Character;
        float baseZ = me.Position.Z;
        float[] turns = { 0f, 90f, -90f, 180f };
        float[] dists = { 4f, 6f, 8f, 11f };
        float bestRange = 999f;
        Vector3 bestPos = me.Position + me.ForwardVector * 4f;
        float bestHead = me.Heading + 180f;
        float bestGround = me.Position.Z - 1f;
        foreach (float dist in dists)
        {
            foreach (float turn in turns)
            {
                float hd = me.Heading + turn;
                float r = hd * (float)Math.PI / 180f;
                Vector3 dir = new Vector3(-(float)Math.Sin(r), (float)Math.Cos(r), 0f);
                Vector3 rt = new Vector3(dir.Y, -dir.X, 0f);
                Vector3 c = me.Position + dir * dist;
                // mat quay huong ve nguoi choi: ban ghe o phia nguoi choi (-dir), nhan vien o phia sau quay (+dir)
                float lo = 999f, hi = -999f;
                Vector3[] pts = { c, c + rt * 1.8f, c - rt * 1.8f, c + dir * 1.2f, c + dir * 3f, c + dir * 1.2f + rt * 1.3f, c + dir * 1.2f - rt * 1.3f, c - dir * 3f, c - dir * 5.5f + rt * 2.5f, c - dir * 5.5f - rt * 2.5f, c - dir * 8f };
                bool bad = false;
                foreach (Vector3 q in pts)
                {
                    float z = World.GetGroundHeight(new Vector3(q.X, q.Y, baseZ + 0.5f));
                    if (z <= -100f) { bad = true; break; }
                    if (z < lo) lo = z;
                    if (z > hi) hi = z;
                }
                if (bad) continue;
                float range = hi - lo;
                if (range < bestRange) { bestRange = range; bestPos = c; bestHead = hd + 180f; bestGround = lo; }
                if (range < 0.25f) { bizPos = c; bizHeading = hd + 180f; bizGround = lo; return; }
            }
        }
        bizPos = bestPos;
        bizHeading = bestHead;
        bizGround = bestGround;
        Log("khong tim duoc cho that phang, chon cho chenh " + bestRange.ToString("0.00") + " m");
    }

    void RelocateBiz()
    {
        if (!hasBiz) return;
        int staff = bizStaff;
        foreach (Ped p in staffPeds) if (p != null && p.Exists()) p.Delete();
        staffPeds.Clear();
        staffSlots.Clear();
        staffStates.Clear();
        staffTasks.Clear();
        ClearBizProps();
        bizIndoor = IsIndoor(Game.Player.Character);
        if (bizIndoor) PlaceBizIndoor(); else PlaceBizOnFlatGround();
        CreateBizBlip();
        BuildBizProps();
        for (int i = 0; i < Math.Min(staff, MaxVisibleStaff); i++) SpawnStaff();
        Notify("Da doi quan ve cho Sep dang dung. Cham xanh tren ban do da cap nhat.");
    }

    void CreateBizBlip()
    {
        if (activeShop < 0) return;
        SyncOut();
        MakeShopBlip(shops[activeShop]);
    }

    // vi tri dung cua nhan vien thu k: phia sau quay (xoay theo staffAngle), lech sang hai ben
    Vector3 StaffSlot(int k)
    {
        float h = (bizHeading + 180f + staffAngle * 90f) * (float)Math.PI / 180f;
        Vector3 dir = new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);   // huong tu quay ra cho nhan vien
        Vector3 lat = new Vector3(dir.Y, -dir.X, 0f);
        Vector3 p0 = bizPos + dir * Math.Max(staffBack, standHalf + 0.65f) + lat * (k == 0 ? 0f : (k % 2 == 1 ? 1f : -1f) * 0.9f * ((k + 1) / 2));
        return new Vector3(p0.X, p0.Y, GroundForBiz() + 1.0f + staffZ);
    }

    void ReslotStaff()
    {
        for (int i = 0; i < staffPeds.Count && i < staffSlots.Count; i++)
        {
            Ped p = staffPeds[i];
            if (p == null || !p.Exists()) continue;
            Vector3 sl = StaffSlot(i);
            staffSlots[i] = sl;
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, p, sl.X, sl.Y, sl.Z, false, false, false);
            p.Heading = StaffFacing(sl);
        }
    }

    // nhan vien quay mat ra phia ban khach (phia truoc quay), khong quay vao quay
    float StaffFacing(Vector3 from)
    {
        return FaceHeading(from, bizPos);   // nguoi ban quay mat ve phia quay (dung sau quay phuc vu)
    }

    static float FaceHeading(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        return (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);
    }

    // cay cao tan rong (doi trong .ini: Tree=...); neu chua nap duoc thi thu lai nhu cac prop khac
    void AddTree(Vector3 pos, float heading)
    {
        string t = Settings.GetValue("Business", "Tree", "prop_tree_lficus_02");
        if (AddProp(t, pos, heading) == null && !buildMissing) AddProp("prop_bush_neat_01", pos, heading);
    }

    readonly List<int> fxHandles = new List<int>();
    // nuoc phun: hieu ung hat (particle) vong lap dat tai dinh dai phun nuoc. Doi trong .ini: FountainFxAsset / FountainFx / FountainFxZ / FountainFxPitch / FountainFxScale
    void StartFountainWater(Vector3 baseP)
    {
        try
        {
            string asset = Settings.GetValue("Business", "FountainFxAsset", "core");
            string fx = Settings.GetValue("Business", "FountainFx", "water_cannon_jet");
            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, asset);
            if (!Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, asset)) { buildMissing = true; return; }
            float z = baseP.Z + ReadF("Business", "FountainFxZ", 1.9f);
            float pitch = ReadF("Business", "FountainFxPitch", 90f);
            float sc = ReadF("Business", "FountainFxScale", 0.15f);
            Function.Call(Hash.USE_PARTICLE_FX_ASSET, asset);
            int hd = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_AT_COORD, fx, baseP.X, baseP.Y, z, pitch, 0f, 0f, sc, false, false, false, false);
            Log("nuoc phun " + asset + "/" + fx + " handle=" + hd);
            if (hd != 0) fxHandles.Add(hd);
        }
        catch (Exception ex) { Log("nuoc phun loi: " + ex.Message); }
    }

    void ClearBizProps()
    {
        foreach (int hd in fxHandles) { try { Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, hd, false); } catch { } }
        fxHandles.Clear();
        foreach (Prop p in bizProps) if (p != null && p.Exists()) p.Delete();
        bizProps.Clear();
        sitIdx = -1;
        sitBench = null;
        benchProps.Clear();
        chairProps.Clear();
        chairBusy.Clear();
        chairFace.Clear();
        chairTable.Clear();
        chairTableTop.Clear();
        foreach (Prop tc in tableCups) DeleteProp(tc);
        tableCups.Clear();
    }

    // Script.Wait() khong dung duoc trong su kien => khong cho model nap: yeu cau nap roi bao "chua san sang", thu lai sau
    bool Loaded(Model m)
    {
        if (m.IsLoaded) return true;
        Function.Call(Hash.REQUEST_MODEL, m.Hash);
        return false;
    }

    bool buildMissing;
    int buildRetry;
    int rebuildAt;

    Prop AddProp(string model, Vector3 pos, float heading) { return AddProp(model, pos, heading, true); }

    Prop AddProp(string model, Vector3 pos, float heading, bool onGround)
    {
        try
        {
            Model m = new Model(model);
            if (!m.IsValid) { Log("khong co model prop " + model); return null; }
            if (!Loaded(m)) { buildMissing = true; return null; }
            Prop p = World.CreateProp(m, pos, new Vector3(0f, 0f, heading), false, onGround);
            m.MarkAsNoLongerNeeded();
            if (p == null || !p.Exists()) return null;
            p.IsPersistent = true;
            Function.Call(Hash.FREEZE_ENTITY_POSITION, p, true);
            bizProps.Add(p);
            return p;
        }
        catch (Exception ex) { Log("AddProp loi: " + ex.Message); return null; }
    }

    // Quan: lv1 = quay + thung hang + ban nho; moi cap nang cap them bo ban ghe + o che
    void BuildBizProps()
    {
        buildMissing = false;
        BuildBizPropsCore();
        // model chua nap xong thi dung lai sau 1,5 giay cho du bo do
        if (buildMissing && buildRetry < 5) { buildRetry++; rebuildAt = Game.GameTime + 1500; }
        else { if (buildMissing) Log("van thieu model sau 5 lan thu"); buildRetry = 0; rebuildAt = 0; }
    }

    void BuildBizPropsCore()
    {
        ClearBizProps();
        foreach (Cust k in customers) k.Chair = -1;
        float h = bizHeading * (float)Math.PI / 180f;
        Vector3 right = new Vector3((float)Math.Cos(h), (float)Math.Sin(h), 0f);
        Vector3 fwd = new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
        // quay doi theo cap: xe hot dog -> quay burger -> quay nuoc ep -> quay hot dog + may pha cafe & tu lanh bar (doi trong .ini: Stand1..4, StandYaw1..4)
        string[] defStand = { standModel, standModel, standModel, standModel };   // prop_juicestand la qua cam khong lo, prop_burgerstand_01 chua kiem chung
        string stName = Settings.GetValue("Business", "Stand" + bizLevel, defStand[Math.Min(bizLevel, 4) - 1]);
        float stYaw = bizLevel == 1 ? standYaw : ReadF("Business", "StandYaw" + bizLevel, 0f);
        Prop stand = AddProp(stName, bizPos, bizHeading + stYaw);
        if (stand == null && stName != "prop_hotdogstand_01") { Log("model quay " + stName + " khong dung duoc, dung quay hot dog"); stYaw = standYaw; stand = AddProp("prop_hotdogstand_01", bizPos, bizHeading + stYaw); }
        standHalf = 0f; float standTop = GroundForBiz() + 1.0f;
        if (stand != null)
        {
            Function.Call(Hash.SET_ENTITY_COLLISION, stand, false, false);   // quay chi de trang tri, nhan vien khong bi day len mat quay
            try { Vector3 smn, smx; stand.Model.GetDimensions(out smn, out smx); standHalf = Math.Max(smx.X - smn.X, smx.Y - smn.Y) * 0.5f; standTop = stand.Position.Z + smx.Z; Log("quay " + stName + " kich thuoc " + (smx.X - smn.X).ToString("0.0") + "x" + (smx.Y - smn.Y).ToString("0.0") + "x" + smx.Z.ToString("0.0")); }
            catch { }
            if (bizLevel >= 3) AddProp("prop_coffee_mac_01", new Vector3(bizPos.X, bizPos.Y, standTop), bizHeading + stYaw, false);   // may pha cafe tren quay
        }
        string[] parasols = { "prop_parasol_01", "prop_parasol_03", "prop_parasol_04", "prop_parasol_05" };
        List<Vector3> tcs = new List<Vector3>();
        List<float> tcTop = new List<float>();
        // Bo cuc theo cap (x = sang phai, y = ra phia truoc, so voi quay): khu ban ghe o phia truoc 3-8 m, khu phu tro (may, cay) o sau quay
        // cap 1: 1 ban giua | cap 2: 2 ban doi xung | cap 3: 2 ban + 1 ban giua hang sau | cap 4: luoi 2x2 rong, nha mai che o giua
        float[,] tpos;
        if (bizIndoor) tpos = bizLevel <= 1 ? new float[,] { { 0f, 2.8f } } : new float[,] { { -2.2f, 2.8f }, { 2.2f, 2.8f } };
        else if (bizLevel <= 1) tpos = new float[,] { { 0f, 3.6f } };
        else if (bizLevel == 2) tpos = new float[,] { { -3.0f, 3.6f }, { 3.0f, 3.6f } };
        else if (bizLevel == 3) tpos = new float[,] { { -3.4f, 3.4f }, { 3.4f, 3.4f }, { 0f, 7.2f } };
        else tpos = new float[,] { { -4.4f, 3.4f }, { 4.4f, 3.4f }, { -4.4f, 7.2f }, { 4.4f, 7.2f } };
        int tables = tpos.GetLength(0);
        for (int l = 1; l <= tables; l++)
        {
            Vector3 c = bizPos + fwd * tpos[l - 1, 1] + right * tpos[l - 1, 0];
            tcs.Add(c);
            Prop tb = AddProp(Settings.GetValue("Business", "Table" + bizLevel, bizLevel >= 4 ? "prop_table_04" : (bizLevel >= 3 ? "prop_table_03b" : "prop_table_03")), c, bizHeading);
            if (tb == null && bizLevel >= 3) { Log("ban cap " + bizLevel + " khong dung duoc, dung ban 03"); tb = AddProp("prop_table_03", c, bizHeading); }
            float top = GroundForBiz() + 0.74f;   // du phong neu khong do duoc
            if (tb != null)
            {
                try { Vector3 dmn, dmx; tb.Model.GetDimensions(out dmn, out dmx); top = tb.Position.Z + dmx.Z; }
                catch { }
            }
            tcTop.Add(top);
            if (tb != null) Function.Call(Hash.SET_ENTITY_COLLISION, tb, false, false);   // ngoi vao ban khong bi vat ly hat len troi
            string chairM = Settings.GetValue("Business", "Chair" + bizLevel, bizLevel >= 4 ? "prop_table_04_chr" : (bizLevel >= 3 ? "prop_chair_01b" : "prop_chair_01a"));
            if (AddProp(chairM, c + right * ChairDist, bizHeading - 90f) == null && bizLevel >= 3) AddProp("prop_chair_01a", c + right * ChairDist, bizHeading - 90f);
            if (bizProps.Count > 0) { chairProps.Add(bizProps[bizProps.Count - 1]); chairBusy.Add(false); chairFace.Add(FaceHeading(c + right * ChairDist, c)); chairTable.Add(c); chairTableTop.Add(top); tableCups.Add(null); }
            if (AddProp(chairM, c - right * ChairDist, bizHeading + 90f) == null && bizLevel >= 3) AddProp("prop_chair_01a", c - right * ChairDist, bizHeading + 90f);
            if (bizProps.Count > 0) { chairProps.Add(bizProps[bizProps.Count - 1]); chairBusy.Add(false); chairFace.Add(FaceHeading(c - right * ChairDist, c)); chairTable.Add(c); chairTableTop.Add(top); tableCups.Add(null); }
            if (bizLevel < 4 && !bizIndoor) AddProp(parasols[Math.Min(bizLevel, 4) - 1], c, bizHeading);   // o che doi mau theo cap; cap 4 co nha mai che rieng
        }
        foreach (Prop chp in chairProps) if (chp != null && chp.Exists()) Function.Call(Hash.SET_ENTITY_COLLISION, chp, false, false);
        for (int ci = 0; ci < chairProps.Count; ci++)
            if (chairProps[ci] != null && chairProps[ci].Exists())
                Log("ghe " + ci + ": vi tri " + chairProps[ci].Position.X.ToString("0.0") + "," + chairProps[ci].Position.Y.ToString("0.0") + " cach ban " + chairProps[ci].Position.DistanceTo2D(chairTable[ci]).ToString("0.00") + " m, ChairDist=" + ChairDist.ToString("0.00"));   // nguoi ngoi vao ghe co va cham => bi hat len troi
        if (bizIndoor)
        {
            // trong nha: chi them chau cay va ghe quay bar, khong co do ngoai troi
            if (bizLevel >= 2) AddProp("prop_pot_plant_05a", bizPos + right * 2.2f + fwd * 0.6f, bizHeading);
            if (bizLevel >= 3) { AddProp("prop_bar_stool_01", bizPos - right * 1.4f + fwd * 1.0f, bizHeading); AddProp("prop_bar_stool_01", bizPos - right * 2.0f + fwd * 1.0f, bizHeading); }
            return;
        }
        // cap 2: chau cay hai ben quay
        if (bizLevel >= 2)
        {
            AddProp("prop_pot_plant_05a", bizPos + right * 2.2f + fwd * 0.6f, bizHeading);
            AddProp("prop_pot_plant_05a", bizPos - right * 2.2f + fwd * 0.6f, bizHeading);   // doi xung hai ben quay
        }
        // cap 3: den suoi, thung rac, ghe da
        if (bizLevel >= 3)
        {
            if (bizLevel == 3) AddProp("prop_patio_heater_01", bizPos + fwd * 5.3f, bizHeading);   // giua hai hang ban
            AddProp("prop_bin_01a", bizPos + right * 2.8f - fwd * 0.4f, bizHeading);
            Prop bn = AddProp("prop_bench_01a", bizPos - right * 6.0f + fwd * 1.5f, bizHeading + 90f);   // ghe dai sat hang rao ben trai
            if (bn != null) { Function.Call(Hash.SET_ENTITY_COLLISION, bn, false, false); benchProps.Add(bn); }
            AddProp("prop_pot_plant_05a", bizPos - right * 6.0f - fwd * 0.4f, bizHeading);   // chau cay canh dau ghe dai
        }
        // cap 4: ghe quay bar, hang cay xanh, them den suoi
        if (bizLevel >= 4)
        {
            AddProp("prop_bar_stool_01", bizPos - right * 1.4f + fwd * 1.0f, bizHeading);
            AddProp("prop_bar_stool_01", bizPos - right * 2.0f + fwd * 1.0f, bizHeading);
            AddProp("prop_bar_stool_01", bizPos - right * 0.8f + fwd * 1.0f, bizHeading);
            AddProp("prop_pot_plant_05a", bizPos + right * 4.4f + fwd * 0.8f, bizHeading);
            AddProp("prop_patio_heater_01", bizPos - right * 6.3f + fwd * 5.3f, bizHeading);
            AddProp("prop_patio_heater_01", bizPos + right * 6.3f + fwd * 5.3f, bizHeading);
        }
        // do sang hon theo cap: may pha ca phe, tu lanh, cay canh lon, hang rao hoa... (danh sach doi duoc trong .ini)
        float gz = GroundForBiz();
        // cap 3+: hang rao cay xanh hai ben, khong gian phan cach voi via he; ly ruou tren ban
        if (bizLevel >= 3)
        {
            // cay tan rong, thua hon hang bui cu: hai ben, mat tien (chua loi cong), cay cao bong rong
            for (int k = -3; k <= 5; k += 2)
            {
                AddTree(bizPos - right * 9.5f + fwd * (k * 2.0f), bizHeading);
                AddTree(bizPos + right * 9.5f + fwd * (k * 2.0f), bizHeading);
            }
            AddTree(bizPos - right * 6.6f + fwd * 10.4f, bizHeading);
            AddTree(bizPos + right * 6.6f + fwd * 10.4f, bizHeading);
            AddProp("prop_streetlight_01", bizPos - right * 3.4f + fwd * 10.0f, bizHeading);
            AddProp("prop_streetlight_01", bizPos + right * 3.4f + fwd * 10.0f, bizHeading);
            AddProp("prop_pot_plant_05a", bizPos - right * 2.4f + fwd * 10.4f, bizHeading);
            AddProp("prop_pot_plant_05a", bizPos + right * 2.4f + fwd * 10.4f, bizHeading);
            for (int ti = 0; ti < tcs.Count; ti++) AddProp("prop_wine_glass", new Vector3(tcs[ti].X + 0.2f, tcs[ti].Y + 0.1f, tcTop[ti] + 0.01f), bizHeading, false);
        }
        // cap 4: nha mai che (gazebo) phu khu ban ghe
        if (bizLevel >= 4)
        {
            Vector3 gc = bizPos + fwd * 5.2f;   // giua hai hang ban
            Prop gz1 = AddProp("prop_gazebo_01", gc, bizHeading);
            if (gz1 == null) gz1 = AddProp("prop_gazebo_02", gc, bizHeading);
            if (gz1 != null) Function.Call(Hash.SET_ENTITY_COLLISION, gz1, false, false);
        }
        // cap 4: dai phun nuoc, hang rao phia sau, ly champagne + chai ruou tren ban
        if (bizLevel >= 4)
        {
            foreach (float tx in new float[] { -8f, -4.2f, 4.2f, 8f }) AddTree(bizPos - fwd * 8.0f + right * tx, bizHeading);   // chua loi vao phia sau o giua
            Vector3 fp = bizPos - fwd * 5.0f;
            Prop fnt = AddProp("prop_fountain2", fp, bizHeading);
            if (fnt != null) StartFountainWater(new Vector3(fp.X, fp.Y, fnt.Position.Z));
            for (int ti = 0; ti < tcs.Count; ti++)
            {
                AddProp("prop_cs_champ_flute", new Vector3(tcs[ti].X - 0.2f, tcs[ti].Y - 0.1f, tcTop[ti] + 0.01f), bizHeading, false);
                AddProp("prop_wine_bot_01", new Vector3(tcs[ti].X, tcs[ti].Y + 0.25f, tcTop[ti] + 0.01f), bizHeading, false);
            }
        }
        int slotIdx = 0;
        for (int lv = 2; lv <= bizLevel; lv++)
        {
            string[] names = Settings.GetValue("Business", "Deco" + lv, DefaultDeco[lv - 2]).Split(',');
            foreach (string raw in names)
            {
                string nm = raw.Trim();
                if (nm.Length == 0) continue;
                if (slotIdx >= DecoSlots.GetLength(0)) break;
                AddProp(nm, bizPos + right * DecoSlots[slotIdx, 0] + fwd * DecoSlots[slotIdx, 1], bizHeading);
                slotIdx++;
            }
        }
    }

    // [right, fwd] so voi quay, cach xa quay >= 3 m de khong chong len nhau hay len cho nhan vien
    static readonly float[,] DecoSlots = {
        { -4.4f, -1.2f }, { 4.4f, -1.2f }, { -6.0f, -2.4f }, { 6.0f, -2.4f }, { -4.4f, -3.6f },
        { 4.4f, -3.6f }, { -3.2f, -5.0f }, { 3.2f, -5.0f }, { -6.0f, -4.4f }, { 6.0f, -4.4f } };
    static readonly string[] DefaultDeco = {
        "prop_vend_coffe_01,prop_watercooler",
        "prop_vend_soda_01,prop_vend_soda_02,prop_plant_int_03a,prop_plant_int_03a",
        "prop_streetlight_01,prop_streetlight_01,prop_patio_lounger1,prop_patio_lounger1" };

    static readonly string[] TierName = { "Ca phe via he", "Quan nuoc mai che", "Ca phe pho", "Ca phe sang trong kieu Au" };
    static readonly string[] TierPerk = {
        "1 ban",
        "2 ban, chau cay, binh nuoc",
        "3 ban, o che mau, den suoi, ghe da, cay canh",
        "4 ban, ghe quay bar, hang cay xanh, ghe nam thu gian, nhieu den suoi" };

    int Price() { return UnitPrice(bizType, bizLevel); }

    bool NearBiz()
    {
        return hasBiz && Game.Player.Character.Position.DistanceTo(bizPos) < 12f;
    }

    void SpawnStaff()
    {
        try
        {
            Model mdl = new Model(rnd.Next(2) == 0 ? "a_m_y_business_01" : "a_f_y_business_01");
            if (!Loaded(mdl)) return;
            float h = bizHeading * (float)Math.PI / 180f;
            Vector3 right = new Vector3((float)Math.Cos(h), (float)Math.Sin(h), 0f);
            Vector3 fwd = new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
            int k = staffPeds.Count;
            // nhan vien dung sau quay (k=0) hoac ben canh quay, quay mat ve phia khach
            Vector3 pos = StaffSlot(k);
            Ped p = World.CreatePed(mdl, pos);
            mdl.MarkAsNoLongerNeeded();
            if (p == null || !p.Exists()) return;
            // game co the day ped len mat quay (+1 m): ep xuong dung do cao mat dat, chan cham dat
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, p, pos.X, pos.Y, pos.Z, false, false, false);
            p.Heading = StaffFacing(pos);
            Log("nhan vien #" + staffPeds.Count + " z=" + pos.Z.ToString("0.00") + " nen=" + GroundForBiz().ToString("0.00") + " bizPosZ=" + bizPos.Z.ToString("0.00"));
            p.IsPersistent = true;
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, true);
            Function.Call(Hash.SET_PED_KEEP_TASK, p, true);
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, p, "WORLD_HUMAN_CLIPBOARD", 0, true);
            p.IsPositionFrozen = true;
            staffPeds.Add(p);
            staffSlots.Add(pos);
            StaffState nst = new StaffState();
            nst.NextPatrol = Game.GameTime + 4000 + rnd.Next(0, 6000);
            staffStates.Add(nst);
        }
        catch (Exception ex) { Log("SpawnStaff loi: " + ex.Message); }
    }

    void FireStaff()
    {
        if (staffPeds.Count == 0) return;
        Ped p = staffPeds[staffPeds.Count - 1];
        staffPeds.RemoveAt(staffPeds.Count - 1);
        staffSlots.RemoveAt(staffSlots.Count - 1);
        staffStates.RemoveAt(staffStates.Count - 1);
        if (p != null && p.Exists()) { p.IsPersistent = false; p.Task.WanderAround(); }
    }

    // ---- khach hang thoi gian thuc
    static readonly string[] CustModels = { "a_m_y_hipster_01", "a_f_y_hipster_01", "a_m_y_beach_01", "a_f_y_beach_01", "a_m_y_vinewood_01", "a_f_y_vinewood_01" };

    void SpawnCustomer(Ped me)
    {
        try
        {
            Vector3 raw = bizPos + new Vector3((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1), 0f) * 40f;
            Vector3 sp = World.GetSafeCoordForPed(raw, true);
            if (sp == Vector3.Zero || sp.DistanceTo(me.Position) < 15f) return;
            Model mdl = new Model(CustModels[rnd.Next(CustModels.Length)]);
            if (!Loaded(mdl)) return;
            Ped p = World.CreatePed(mdl, sp);
            mdl.MarkAsNoLongerNeeded();
            if (p == null || !p.Exists()) return;
            Cust c = new Cust();
            c.P = p;
            c.T = Game.GameTime;
            c.Qty = 1 + rnd.Next(2);
            // neu co ghe trong thi 50% ngoi lai
            for (int i = 0; i < chairProps.Count; i++)
                if (!chairBusy[i] && !ChairTaken(i) && !SpotOccupied(i) && rnd.Next(2) == 0) { c.Chair = i; chairBusy[i] = true; c.Qty += 1; break; }
            p.IsPersistent = true;
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, true);
            Vector3 dest2 = QueueSpot();
            Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, p, dest2.X, dest2.Y, dest2.Z, 1.0f, 25000, 1.5f, false, 0f);
            customers.Add(c);
            Log("sinh khach, cach quan " + (int)sp.DistanceTo(bizPos) + " m, ghe " + c.Chair);
        }
        catch (Exception ex) { Log("SpawnCustomer loi: " + ex.Message); }
    }

    // hai ghe trung vi tri (hoac nguoi dang ngoi gan do) thi coi nhu da co nguoi
    bool SpotOccupied(int idx)
    {
        if (idx < 0 || idx >= chairProps.Count || chairProps[idx] == null || !chairProps[idx].Exists()) return true;
        Vector3 cp = chairProps[idx].Position;
        for (int j = 0; j < chairProps.Count; j++)
        {
            if (j == idx || chairProps[j] == null || !chairProps[j].Exists()) continue;
            if (chairProps[j].Position.DistanceTo2D(cp) < 0.6f && (chairBusy[j] || ChairTaken(j))) return true;
        }
        foreach (Cust k in customers)
            if (k.Chair >= 0 && k.P != null && k.P.Exists() && k.P.Position.DistanceTo2D(cp) < 0.7f && k.Chair != idx) return true;
        return false;
    }

    bool ChairTaken(int idx)
    {
        foreach (Cust k in customers) if (k.Chair == idx) return true;
        foreach (StaffState st in staffStates) if (st.Chair == idx && st.Mode != 0) return true;
        return false;
    }

    Vector3 QueueSpot()
    {
        float h = bizHeading * (float)Math.PI / 180f;
        Vector3 right = new Vector3((float)Math.Cos(h), (float)Math.Sin(h), 0f);
        Vector3 fwd = new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
        return bizPos + fwd * 1.8f + right * (float)(rnd.NextDouble() * 1.2 - 0.6);
    }

    void Release(Cust c)
    {
        if (c.Chair >= 0 && c.Chair < chairBusy.Count)
        {
            chairBusy[c.Chair] = false;
            if (c.Stage == 2 && staffPeds.Count > 1) { StaffTask t = new StaffTask(); t.Type = 2; t.Chair = c.Chair; staffTasks.Add(t); }
        }
        DeleteProp(c.Cup);
        c.Cup = null;
        Ped p = c.P;
        if (p != null && p.Exists())
        {
            p.IsPersistent = false;
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, false);
            p.Task.ClearAll();
            SnapNearGround(p);
            p.Task.WanderAround();
            p.MarkAsNoLongerNeeded();
        }
    }

    void UpdateCustomers(int now, Ped me)
    {
        if (!hasBiz) return;
        bool near = me.Position.DistanceTo(bizPos) < 120f;
        // sinh khach moi
        if (near && bizStock > 0 && now > nextCustomer && customers.Count < 6)
        {
            int gap = 26000 - bizStaff * 3000 - bizLevel * 2500 + rnd.Next(0, 6000);
            gap = (int)(gap / Types[TIdx(bizType)].Traffic);
            nextCustomer = now + Math.Max(5000, gap);
            SpawnCustomer(me);
        }
        for (int i = customers.Count - 1; i >= 0; i--)
        {
            Cust c = customers[i];
            Ped p = c.P;
            if (p == null || !p.Exists() || !p.IsAlive || !near) { Release(c); customers.RemoveAt(i); continue; }
            if (c.Stage == 2 && c.Chair >= 0) KeepOnSeat(p, c.Chair);
            if (c.Stage == 0)
            {
                bool arrived = p.Position.DistanceTo(bizPos) < 3.2f;
                if (!arrived && now - c.T > 30000 && p.Position.DistanceTo(bizPos) < 10f) arrived = true;
                if (arrived || now - c.T > 30000)
                {
                    if (!arrived) { Log("khach khong toi duoc quay (cach " + (int)p.Position.DistanceTo(bizPos) + " m), bo"); Release(c); customers.RemoveAt(i); continue; }
                    bool served = bizStaff > 0;   // can co nhan vien; chu dung xa hay gan deu khong anh huong
                    if (!served)
                    {
                        if (now - c.T > 45000) { Notify("Khach bo di vi quan chua co nhan vien. Vao menu Kinh doanh de thue nhan vien."); Release(c); customers.RemoveAt(i); }
                        continue;
                    }
                    c.Stage = 1;
                    c.T = now;
                    p.Task.ClearAll();
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, p, "WORLD_HUMAN_STAND_IMPATIENT", 0, true);
                }
            }
            else if (c.Stage == 1 && now - c.T > 4000)
            {
                int qty = Math.Min(bizStock, c.Qty);
                if (qty <= 0) { Release(c); customers.RemoveAt(i); continue; }
                bizStock -= qty;
                if (bizStock == 0) Notify("~r~Het hang!~s~ Den quan bam " + MK + " > Kinh doanh > Nhap hang de tiep tuc ban.");
                else if (bizStock <= 5) Notify("~y~Kho sap het~s~ (con " + bizStock + " " + UW(bizType) + ").");
                soldToday += qty;
                int pay, cgs;
                SellUnits(bizType, bizLevel, qty, out pay, out cgs);
                bizRev += pay; bizCogs += cgs;
                EarnCash(pay, "business", "ban hang tai quan");
                Notify("~g~+$" + pay + "~s~ " + (IsService(bizType) ? "phuc vu " : "ban ") + qty + " " + UW(bizType) + " (kho con " + bizStock + ").");
                if (c.Chair >= 0 && c.Chair < chairProps.Count && chairProps[c.Chair] != null && chairProps[c.Chair].Exists())
                {
                    Prop ch = chairProps[c.Chair];
                    c.Stage = 2;
                    c.T = now;
                    p.Task.ClearAll();
                    Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, p, "PROP_HUMAN_SEAT_CHAIR", ch.Position.X, ch.Position.Y, ch.Position.Z + seatZ, c.Chair < chairFace.Count ? chairFace[c.Chair] : ch.Heading, 40000, true, true);
                    if (staffPeds.Count > 1) { StaffTask t = new StaffTask(); t.Type = 1; t.Chair = c.Chair; t.Target = c; staffTasks.Add(t); }
                    else PutCupOnTable(c.Chair, MakeCup());
                }
                else
                {
                    // mua mang di: nhan ly nuoc, dung uong mot luc roi di
                    c.Stage = 3;
                    c.T = now;
                    p.Task.ClearAll();
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, p, "WORLD_HUMAN_DRINKING", 0, true);
                }
            }
            else if (c.Stage == 3 && now - c.T > 6000)
            {
                Release(c);
                customers.RemoveAt(i);
            }
            else if (c.Stage == 2 && now - c.T > (staffPeds.Count > 1 ? 24000 : 14000))
            {
                Release(c);
                customers.RemoveAt(i);
            }
        }
    }

    // Giu nhan vien o dung cho (sau khi noi chuyen xong script kia co the cho ho di lang thang)
    float GroundForBiz()
    {
        return bizGround != 0f ? bizGround : bizPos.Z - 1f;   // save cu khong co: tam nguoi choi luc dat quan cao hon dat ~1 m
    }

    static float GroundZ(Vector3 v)
    {
        float z = World.GetGroundHeight(new Vector3(v.X, v.Y, v.Z + 0.5f));
        return z <= -100f ? v.Z - 1f : z;
    }

    static readonly string[] CupModels = { "prop_plastic_cup_02", "p_ing_coffeecup_01", "prop_cup_saucer_01", "prop_food_cups01", "prop_cs_paper_cups01" };
    string cupModel;

    Prop MakeCup()
    {
        try
        {
            if (cupModel == null)
            {
                foreach (string name in CupModels)
                {
                    Model t = new Model(name);
                    if (t.IsValid && Loaded(t)) { cupModel = name; Log("dung model ly: " + name); break; }
                }
                if (cupModel == null) return null;   // chua nap xong, lan sau thu lai
            }
            if (cupModel.Length == 0) return null;
            Model m = new Model(cupModel);
            if (!Loaded(m)) return null;
            Prop cup = World.CreateProp(m, Game.Player.Character.Position + new Vector3(0f, 0f, 3f), false, false);
            m.MarkAsNoLongerNeeded();
            return cup;
        }
        catch { return null; }
    }

    // Dat ly len ban truoc cho ngoi so idx
    void PutCupOnTable(int idx, Prop cup)
    {
        if (cup == null || !cup.Exists()) return;
        if (idx < 0 || idx >= chairProps.Count || chairProps[idx] == null || !chairProps[idx].Exists()) { DeleteProp(cup); return; }
        DeleteProp(tableCups[idx]);
        Function.Call(Hash.DETACH_ENTITY, cup, true, true);
        Vector3 cp = chairProps[idx].Position;
        Vector3 toward = chairTable[idx] - cp;
        toward.Z = 0f;
        toward.Normalize();
        // ly dat sat mep ban phia nguoi ngoi: cach tam ban 0,28 m (mat ban ~1 m vuong), cao bang mat ban
        Vector3 c0 = chairTable[idx];
        Vector3 pos = c0 - toward * 0.28f;
        pos.Z = (idx < chairTableTop.Count ? chairTableTop[idx] : cp.Z + 0.74f) + cupTable;
        cup.Position = pos;
        cup.Rotation = Vector3.Zero;
        Function.Call(Hash.FREEZE_ENTITY_POSITION, cup, true);
        tableCups[idx] = cup;
    }

    void AttachCup(Prop cup, Ped p)
    {
        if (cup == null || !cup.Exists() || p == null || !p.Exists()) return;
        int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, p, 28422); // tay phai
        Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, cup, p, bone, cupX, cupY, cupZ, 0.0f, 0.0f, 0.0f, true, true, false, false, 2, true);
    }

    static void DeleteProp(Prop p)
    {
        if (p != null && p.Exists()) p.Delete();
    }

    // Nhan vien: thu ngan (nguoi dau) dung o quay; nhung nguoi con lai bung nuoc cho khach ngoi va don ban
    void UpdateStaff(int now)
    {
        if (now < nextStaffCheck) return;
        nextStaffCheck = now + 500;
        for (int i = 0; i < staffPeds.Count; i++)
        {
            Ped p = staffPeds[i];
            StaffState st = staffStates[i];
            if (p == null || !p.Exists() || !p.IsAlive) continue;
            if (st.Mode == 0)
            {
                StaffTask t = null;
                if (i > 0 && staffTasks.Count > 0) { t = staffTasks[0]; staffTasks.RemoveAt(0); }
                else if (i > 0 && chairProps.Count > 0 && now > st.NextPatrol)
                {
                    st.NextPatrol = now + 9000 + rnd.Next(0, 8000);
                    t = new StaffTask();
                    t.Type = 2;
                    t.Chair = rnd.Next(chairProps.Count);
                    if (ChairTaken(t.Chair)) t = null;   // dang co khach ngoi, khong don luc do
                }
                if (t != null)
                {
                    if (t.Chair < 0 || t.Chair >= chairProps.Count || chairProps[t.Chair] == null || !chairProps[t.Chair].Exists()) continue;
                    st.Type = t.Type;
                    st.Chair = t.Chair;
                    st.Target = t.Target;
                    st.T = now;
                    st.Mode = 1;
                    p.IsPositionFrozen = false;
                    p.Task.ClearAll();
                    if (t.Type == 1) { st.Cup = MakeCup(); AttachCup(st.Cup, p); }
                    Vector3 cp = chairProps[t.Chair].Position;
                    Vector3 toward = chairTable[t.Chair] - cp;
                    toward.Z = 0f;
                    toward.Normalize();
                    Vector3 spot = cp + toward * 0.9f;
                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, p, spot.X, spot.Y, spot.Z, 1.0f, 15000, 1.0f, false, 0f);
                    continue;
                }
                if (p.IsPositionFrozen)
                {
                    // dang dung yen: neu bi day len cao (hoac lo lung) thi tra ve vi tri dung that
                    if (p.Position.DistanceTo2D(bizPos) < 8f)
                    {
                        if (Math.Abs(p.Position.Z - staffSlots[i].Z) > 0.15f || p.Position.DistanceTo2D(staffSlots[i]) > 0.3f)
                            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, p, staffSlots[i].X, staffSlots[i].Y, staffSlots[i].Z, false, false, false);
                        float want = StaffFacing(staffSlots[i]);
                        float diff = Math.Abs(((p.Heading - want + 540f) % 360f) - 180f);
                        if (diff > 25f) p.Heading = want;   // luon quay mat ra phia khach
                    }
                    continue;
                }
                // ve cho dung
                if (p.Position.DistanceTo(staffSlots[i]) > 1.5f) p.Position = staffSlots[i];
                p.Heading = StaffFacing(staffSlots[i]);
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, p, "WORLD_HUMAN_STAND_IMPATIENT", 0, true);
                p.IsPositionFrozen = true;
            }
            else if (st.Mode == 1)
            {
                Vector3 cp = st.Chair >= 0 && st.Chair < chairProps.Count && chairProps[st.Chair] != null && chairProps[st.Chair].Exists() ? chairProps[st.Chair].Position : bizPos;
                if (p.Position.DistanceTo2D(cp) < 2.2f || now - st.T > 15000)
                {
                    if (st.Type == 1)
                    {
                        // dua ly nuoc ra ban cho khach
                        Prop cupToPlace = st.Cup;
                        st.Cup = null;
                        Cust c = st.Target;
                        if (c != null && c.P != null && c.P.Exists() && c.Stage == 2) PutCupOnTable(st.Chair, cupToPlace);
                        else DeleteProp(cupToPlace);
                        p.Task.ClearAll();
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, p, "WORLD_HUMAN_CLIPBOARD", 0, true);
                        st.Until = now + 3500;
                    }
                    else
                    {
                        p.Task.ClearAll();
                        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, p, "WORLD_HUMAN_MAID_CLEAN", 0, true);
                        st.Until = now + 8000;
                    }
                    st.Mode = 2;
                }
            }
            else if (st.Mode == 2)
            {
                if (now > st.Until)
                {
                    if (st.Type == 2 && st.Chair >= 0 && st.Chair < tableCups.Count) { DeleteProp(tableCups[st.Chair]); tableCups[st.Chair] = null; }
                    p.Task.ClearAll();
                    Vector3 sl = staffSlots[i];
                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, p, sl.X, sl.Y, sl.Z, 1.0f, 20000, 0.8f, false, 0f);
                    st.Mode = 3;
                    st.T = now;
                }
            }
            else if (st.Mode == 3)
            {
                if (p.Position.DistanceTo2D(staffSlots[i]) < 1.3f || now - st.T > 20000) st.Mode = 0;
            }
        }
    }

    // (cu dan co lich: xem phan "DOI SONG" o cuoi file)

    // ------------------------------------------------------------ nhà, ăn, uống, ngủ
    bool PayCash(int amount, string cat, string note)
    {
        if (amount <= 0) return true;
        Func<int, string, string, bool> pay = null;
        try { pay = AppDomain.CurrentDomain.GetData("GVL.PayCash") as Func<int, string, string, bool>; } catch { }
        if (pay != null) { try { return pay(amount, cat, note); } catch (Exception ex) { Log("thanh toan tien mat loi: " + ex.Message); return false; } }
        if (Game.Player.Money < amount) return false;
        Game.Player.Money = Game.Player.Money - amount;
        return true;
    }

    void EarnCash(int amount, string cat, string note)
    {
        if (amount <= 0) return;
        Action<int, string, string> earn = null;
        try { earn = AppDomain.CurrentDomain.GetData("GVL.EarnCash") as Action<int, string, string>; } catch { }
        if (earn != null)
        {
            try { earn(amount, cat, note); }
            catch (Exception ex) { Log("ghi nhan thu tien mat loi: " + ex.Message); }
            return; // never apply a second credit after a delegate may have partially committed
        }
        Game.Player.Money = Game.Player.Money + amount;
    }

    bool Spend(int amount)
    {
        if (PayCash(amount, "life", "chi tieu doi song")) return true;
        Notify("Khong du tien ($" + amount + ")."); return false;
    }

    void Eat()
    {
        if (!Spend(FoodPrice)) return;
        hunger = Clamp(hunger + 40f);
        Game.Player.Character.Health = Math.Min(Game.Player.Character.MaxHealth, Game.Player.Character.Health + 10);
        Notify("Da an. No hon roi (-$" + FoodPrice + ").");
    }

    void Drink()
    {
        if (!Spend(DrinkPrice)) return;
        thirst = Clamp(thirst + 40f);
        Notify("Da uong nuoc (-$" + DrinkPrice + ").");
    }

    // doc so thap phan trong .ini bat ke may dung dau cham hay dau phay (may VN: "0.55" bi doc thanh 55!)
    float ReadF(string section, string key, float def)
    {
        try
        {
            string v = Settings.GetValue(section, key, def.ToString(System.Globalization.CultureInfo.InvariantCulture));
            v = v.Trim().Replace(',', '.');
            float f;
            if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) return f;
        }
        catch { }
        return def;
    }

    readonly List<Blip> homeBlips = new List<Blip>();
    void MakeHomeBlips()
    {
        try
        {
            foreach (Blip b in homeBlips) { if (b != null && b.Exists()) b.Delete(); }
            homeBlips.Clear();
            for (int i = 0; i < Homes.Length; i++)
            {
                Blip b = World.CreateBlip(Homes[i].Pos);
                b.Sprite = BlipSprite.Safehouse;
                b.Color = (homeIdx == i) ? BlipColor.Green : BlipColor.Yellow;
                b.IsShortRange = false;
                b.Name = (homeIdx == i ? "Nha cua Sep: " : "Nha cho thue: ") + Homes[i].Name + " ($" + Homes[i].Rent + "/ngay)";
                homeBlips.Add(b);
            }
        }
        catch (Exception ex) { Log("blip nha loi: " + ex.Message); }
    }

    // ---- vao trong nha: dich chuyen vao noi that, ra lai bang cung muc do
    // moi nha co danh sach noi that thu lan luot: cai nao nap duoc (co interior that) thi dung
    static readonly float[][] AptLow = { new float[] { 261.4586f, -998.8196f, -99.0086f } };
    static readonly float[][] AptMid = { new float[] { 347.2686f, -999.2955f, -99.1962f } };
    static readonly float[][] AptHigh = { new float[] { -786.8663f, 315.7642f, 217.6385f }, new float[] { -18.0786f, -583.6725f, 79.4657f } };
    static readonly float[][] Trev = { new float[] { 1973.0f, 3815.0f, 33.43f } };
    static readonly float[][] Mich = { new float[] { -802.3f, 175.0f, 72.8f } };
    static readonly float[][] Fran = { new float[] { 7.1197f, 536.6153f, 176.028f } };
    static float[][][] HomeCands { get { return new float[][][] { Concat(Trev, AptLow), Concat(AptLow), Concat(Fran, AptLow), Concat(Mich, AptLow) }; } }
    static float[][] Concat(params float[][][] arrs) { List<float[]> l = new List<float[]>(); foreach (float[][] a in arrs) l.AddRange(a); return l.ToArray(); }
    static readonly string[] AptIpls = { "apa_v_mp_h_01_a", "apa_v_mp_h_01_b", "apa_v_mp_h_01_c", "apa_v_mp_h_02_a", "apa_v_mp_h_02_b", "apa_v_mp_h_02_c", "apa_v_mp_h_03_a", "apa_v_mp_h_04_a", "apa_v_mp_h_05_a", "apa_v_mp_h_06_a" };
    float[][] curCands; int candIdx;
    bool insideHome; int enterMinAt; int doorCool;
    bool engineWorldCatalogSent;
    string pendingWorldCommandId = "", pendingWorldCommandAction = "";
    int pendingWorldCommandDeadline;
    Vector3 homeOutPos;
    Vector3 enterTarget; int enterUntil;
    bool enterIsExit;

    // diem cua chinh cua tung nha (toa do GTA V), dung de do cho ben trong nha that sau cua
    static readonly float[][] HomeDoorPos = {
        new float[] { 1972.7f, 3817.0f, 33.4f },
        new float[] { -14.8689f, -1441.1822f, 31.1932f },
        new float[] { 7.5139f, 539.5268f, 176.2028f },
        new float[] { -816.4f, 178.3f, 72.8274f } };

    // thu nhieu diem quanh cua; diem nao thuoc mot interior khac interior hien tai cua Sep thi do la ben trong nha that
    bool FindRealInterior(Ped me, int idx, out Vector3 result)
    {
        result = Vector3.Zero;
        float[] dp = HomeDoorPos[idx];
        int curInt = 0;
        try { curInt = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me); } catch { }
        float[] radii = { 3.5f, 2.2f, 5.0f };
        float[] dzs = { 0.5f, 1.0f, 0.0f };
        for (int ri = 0; ri < radii.Length; ri++)
            for (int a = 0; a < 16; a++)
            {
                double ang = a * Math.PI / 8.0;
                float x = dp[0] + (float)Math.Cos(ang) * radii[ri], y = dp[1] + (float)Math.Sin(ang) * radii[ri];
                foreach (float dz in dzs)
                {
                    int it = 0;
                    try { it = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, x, y, dp[2] + dz); } catch { }
                    if (it != 0 && it != curInt)
                    {
                        result = new Vector3(x, y, dp[2] + 0.6f);
                        Log("noi that that tim thay: interior " + it + " tai " + x.ToString("0.0") + "," + y.ToString("0.0") + "," + (dp[2] + dz).ToString("0.0") + " (hien tai " + curInt + ")");
                        return true;
                    }
                }
            }
        Log("khong do duoc noi that that sau cua (hien tai interior " + curInt + ")");
        return false;
    }

    void EnterHome()
    {
        if (!hasHome || homeIdx < 0) { Notify("Sep chua thue nha nao."); return; }
        Ped me = Game.Player.Character;
        if (me.IsInVehicle()) { Notify("Xuong xe da."); return; }
        if (me.Position.DistanceTo(Homes[homeIdx].Pos) > 25f) { Notify("Phai dung gan nha (25 m) moi vao duoc."); return; }
        foreach (string ip in AptIpls) { try { Function.Call(Hash.REQUEST_IPL, ip); } catch { } }
        curCands = HomeCands[homeIdx];
        candIdx = 0;
        float[] f = curCands[0];
        if (!insideHome)
        {
            int ci = 0;
            try { ci = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me); } catch { }
            if (ci == 0) homeOutPos = me.Position;
            else
            {
                // Sep dang o trong mot noi that san (vd da o san trong nha): thoat ra duong gan nha
                Vector3 st = Vector3.Zero;
                try { st = World.GetNextPositionOnStreet(Homes[homeIdx].Pos); } catch { }
                homeOutPos = st == Vector3.Zero ? Homes[homeIdx].Pos : st;
                Log("dang o trong noi that " + ci + ", diem ra = " + homeOutPos.X.ToString("0.0") + "," + homeOutPos.Y.ToString("0.0") + "," + homeOutPos.Z.ToString("0.0"));
            }
        }
        enterTarget = new Vector3(f[0], f[1], f[2]);
        enterIsExit = false;
        enterUntil = Game.GameTime + 5000; enterMinAt = Game.GameTime + 900; doorCool = Game.GameTime + 2500;
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, enterTarget.X, enterTarget.Y, enterTarget.Z, false, false, false);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, me, true);
        try { GTA.UI.Screen.FadeOut(200); } catch { }
        insideHome = true;
        Log("vao nha " + Homes[homeIdx].Name + " -> " + enterTarget.X + "," + enterTarget.Y + "," + enterTarget.Z);
    }

    void ExitHome()
    {
        Ped me = Game.Player.Character;
        if (homeOutPos == Vector3.Zero)
        {
            Vector3 st = Vector3.Zero;
            try { st = World.GetNextPositionOnStreet(Homes[Math.Max(0, homeIdx)].Pos); } catch { }
            homeOutPos = st == Vector3.Zero ? Homes[Math.Max(0, homeIdx)].Pos : st;
        }
        enterTarget = homeOutPos;
        Log("ra ngoai -> " + enterTarget.X + "," + enterTarget.Y + "," + enterTarget.Z);
        enterIsExit = true;
        enterUntil = Game.GameTime + 4000; enterMinAt = Game.GameTime + 600; doorCool = Game.GameTime + 2500;
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, enterTarget.X, enterTarget.Y, enterTarget.Z, false, false, false);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, me, true);
        try { GTA.UI.Screen.FadeOut(200); } catch { }
        insideHome = false;
    }

    // doi nap collision/noi that roi tha nguoi choi ra; neu vao nha ma khong co noi that thi tra ve cho cu
    void TickEnterHome(Ped me, int now)
    {
        // dong bo trang thai: neu co tinh 'dang o trong' nhung that ra da o xa noi that thi huy co (tranh E dich chuyen lung tung)
        if (insideHome && enterUntil <= 0 && me.Position.DistanceTo(enterTarget) > 60f) { insideHome = false; nearFurn = ""; Log("dong bo: khong con o trong nha"); }
        ScanFurniture(me, now);
        if (enterUntil <= 0 && hasHome && homeIdx >= 0 && !menuOpen && !me.IsInVehicle() && nearFurn.Length == 0)
        {
            if (!insideHome && me.Position.DistanceTo(Homes[homeIdx].Pos) < 12f)
                GTA.UI.Screen.ShowSubtitle("~y~" + sitKey + "~s~: vao nha " + homeName, 100);
            else if (insideHome)
                GTA.UI.Screen.ShowSubtitle("~y~" + sitKey + "~s~: ra ngoai", 100);
        }
        if (nearFurn.Length > 0 && enterUntil <= 0 && !menuOpen)
            GTA.UI.Screen.ShowSubtitle("~y~" + sitKey + "~s~: " + (nearFurn == "bed" ? "ngu (tua 8 gio)" : nearFurn == "fridge" ? (LifeFridgeOn() ? "mo tu lanh, nau an" : "mo tu lanh (an uong mien phi)") : "nghi tren sofa"), 100);
        if (enterUntil <= 0) return;
        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, enterTarget.X, enterTarget.Y, enterTarget.Z);
        Function.Call(Hash.SET_FOCUS_POS_AND_VEL, enterTarget.X, enterTarget.Y, enterTarget.Z, 0f, 0f, 0f);
        bool ready = false;
        try { ready = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, me); } catch { }
        if (now < enterMinAt) return;
        if (!ready && now < enterUntil) return;
        Function.Call(Hash.CLEAR_FOCUS);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, me, false);
        enterUntil = 0;
        try { GTA.UI.Screen.FadeIn(500); } catch { }
        if (!enterIsExit)
        {
            int it = 0;
            try { it = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me); } catch { }
            if ((it == 0 || !ready) && curCands != null && candIdx + 1 < curCands.Length)
            {
                candIdx++;
                float[] nf = curCands[candIdx];
                enterTarget = new Vector3(nf[0], nf[1], nf[2]);
                enterUntil = now + 4500; enterMinAt = now + 900;
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, enterTarget.X, enterTarget.Y, enterTarget.Z, false, false, false);
                Function.Call(Hash.FREEZE_ENTITY_POSITION, me, true);
                try { GTA.UI.Screen.FadeOut(100); } catch { }
                Log("thu noi that khac #" + candIdx + " -> " + enterTarget.X + "," + enterTarget.Y + "," + enterTarget.Z);
                return;
            }
            if (it == 0 || !ready)
            {
                Notify("~r~Noi that chua nap duoc, dua Sep ve cho cu.~s~");
                Log("noi that khong nap duoc: ready=" + ready + " interior=" + it);
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, homeOutPos.X, homeOutPos.Y, homeOutPos.Z, false, false, false);
                insideHome = false;
                return;
            }
            Notify("Da vao ~g~" + homeName + "~s~. Bam " + MK + " > 'Ra ngoai' de ra.");
        }
    }

    // ---- cua tu mo: moi canh cua (vat the mong, cao 1,8-3 m, rong 0,6-1,7 m) trong 2,2 m quanh Sep se tat va cham va xoay mo ra,
    // Sep di xa 4,5 m thi dong lai. Nhan dien bang kich thuoc model nen ap dung cho ca nha cua nguoi khac.
    readonly Dictionary<int, float> doorOrigYaw = new Dictionary<int, float>();
    readonly Dictionary<int, bool> doorModelIs = new Dictionary<int, bool>();
    readonly List<int> openDoors = new List<int>();
    readonly Dictionary<int, float> doorTargetYaw = new Dictionary<int, float>();
    static readonly string[] DoorModelNames = { "apa_heist_apart2_door","apa_mp_apa_yacht_door","apa_mp_apa_yacht_door2","apa_prop_heist_cutscene_doora","apa_prop_heist_cutscene_doorb","apa_prop_ss1_mpint_door_l","apa_prop_ss1_mpint_door_r","apa_v_ilev_fh_bedrmdoor","apa_v_ilev_fh_heistdoor1","apa_v_ilev_fh_heistdoor2","apa_v_ilev_ss_door2","apa_v_ilev_ss_door7","apa_v_ilev_ss_door8","prop_bh1_44_door_01l","prop_bh1_44_door_01r","prop_bh1_48_backdoor_l","prop_bh1_48_backdoor_r","prop_bhhotel_door_l","prop_bhhotel_door_r","prop_biolab_g_door","prop_bs_map_door_01","prop_casino_door_01l","prop_casino_door_01r","prop_ch1_07_door_01l","prop_ch1_07_door_01r","prop_ch1_07_door_02l","prop_ch1_07_door_02r","prop_ch2_05d_g_door","prop_ch2_07b_20_g_door","prop_ch2_09b_door","prop_ch3_01_trlrdoor_l","prop_ch3_01_trlrdoor_r","prop_ch3_04_door_01l","prop_ch3_04_door_01r","prop_ch3_04_door_02","prop_ch_025c_g_door_01","prop_cntrdoor_ld_l","prop_cntrdoor_ld_r","prop_com_ls_door_01","prop_container_door_mb_l","prop_container_door_mb_r","prop_control_rm_door_01","prop_cs4_05_tdoor","prop_cs4_11_door","prop_cs6_03_door_l","prop_cs6_03_door_r","prop_cub_door_lifeblurb","prop_damdoor_01","prop_door_01","prop_door_balcony_frame","prop_door_balcony_left","prop_door_balcony_right","prop_door_bell_01","prop_dt1_20_mp_door_l","prop_dt1_20_mp_door_r","prop_epsilon_door_l","prop_epsilon_door_r","prop_faceoffice_door_l","prop_faceoffice_door_r","prop_gascyl_ramp_door_01","prop_grumandoor_l","prop_grumandoor_r","prop_hanger_door_1","prop_hospital_door_l","prop_hospital_door_r","prop_hospitaldoors_start","prop_hw1_03_gardoor_01","prop_hw1_04_door_l1","prop_hw1_04_door_r1","prop_hw1_23_door","prop_id2_11_gdoor","prop_id_21_gardoor_01","prop_id_21_gardoor_02","prop_indus_meet_door_l","prop_indus_meet_door_r","prop_kt1_06_door_l","prop_kt1_06_door_r","prop_kt1_10_mpdoor_l","prop_kt1_10_mpdoor_r","prop_magenta_door","prop_map_door_01","prop_michael_door","prop_mine_doorng_l","prop_mine_doorng_r","prop_mineshaft_door","prop_motel_door_09","prop_out_door_speaker","prop_outdoor_fan_01","prop_paynspray_door_l","prop_paynspray_door_r","prop_pris_door_01_l","prop_pris_door_01_r","prop_pris_door_02","prop_pris_door_03","prop_ql_revolving_door","prop_ret_door","prop_ret_door_02","prop_ret_door_03","prop_ret_door_04","prop_ron_door_01","prop_sc1_12_door","prop_sc1_21_g_door_01","prop_secdoor_01","prop_shop_front_door_l","prop_shop_front_door_r","prop_showroom_door_l","prop_showroom_door_r","prop_sm1_11_doorl","prop_sm1_11_doorr","prop_sm_10_mp_door","prop_sm_27_door","prop_ss1_05_mp_door","prop_ss1_08_mp_door_l","prop_ss1_08_mp_door_r","prop_ss1_10_door_l","prop_ss1_10_door_r","prop_strip_door_01","prop_v_door_44","prop_wardrobe_door_01","v_ilev_247door","v_ilev_247door_r","v_ilev_abbmaindoor","v_ilev_abbmaindoor2","v_ilev_arm_secdoor","v_ilev_bk_door","v_ilev_bk_door2","v_ilev_bl_door_l","v_ilev_bl_door_r","v_ilev_bl_doorel_l","v_ilev_bl_doorel_r","v_ilev_bl_doorpool","v_ilev_bl_doorsl_l","v_ilev_bl_doorsl_r","v_ilev_bs_door","v_ilev_cd_door","v_ilev_cd_door2","v_ilev_cd_door3","v_ilev_cd_entrydoor","v_ilev_cd_secdoor","v_ilev_cd_secdoor2","v_ilev_cf_officedoor","v_ilev_ch_glassdoor","v_ilev_clothmiddoor","v_ilev_cm_door1","v_ilev_cor_darkdoor","v_ilev_cor_doorglassa","v_ilev_cor_doorglassb","v_ilev_cor_firedoor","v_ilev_cor_firedoorwide","v_ilev_cor_offdoora","v_ilev_cs_door","v_ilev_cs_door01","v_ilev_cs_door01_r","v_ilev_csr_door_l","v_ilev_csr_door_r","v_ilev_ct_door01","v_ilev_ct_door02","v_ilev_ct_door03","v_ilev_ct_doorl","v_ilev_ct_doorr","v_ilev_depboxdoor01","v_ilev_dev_door","v_ilev_deviantfrontdoor","v_ilev_door_orange","v_ilev_door_orangesolid","v_ilev_epsstoredoor","v_ilev_fa_backdoor","v_ilev_fa_dinedoor","v_ilev_fa_frontdoor","v_ilev_fa_roomdoor","v_ilev_fa_warddoorl","v_ilev_fa_warddoorr","v_ilev_fb_door01","v_ilev_fb_door02","v_ilev_fb_doorshortl","v_ilev_fb_doorshortr","v_ilev_fb_sl_door01","v_ilev_fh_door01","v_ilev_fh_door02","v_ilev_fh_door03","v_ilev_fh_door4","v_ilev_fh_door5","v_ilev_fh_frntdoor","v_ilev_fh_frontdoor","v_ilev_gasdoor","v_ilev_gasdoor_r","v_ilev_gc_door01","v_ilev_gc_door02","v_ilev_gc_door03","v_ilev_gc_door04","v_ilev_gendoor01","v_ilev_gendoor02","v_ilev_gtdoor","v_ilev_gtdoor02","v_ilev_hd_door_l","v_ilev_hd_door_r","v_ilev_housedoor1","v_ilev_j2_door","v_ilev_janitor_frontdoor","v_ilev_lester_doorfront","v_ilev_lester_doorveranda","v_ilev_lostdoor","v_ilev_losttoiletdoor","v_ilev_methdoorscuff","v_ilev_methtraildoor","v_ilev_ml_door1","v_ilev_mldoor02","v_ilev_mm_door","v_ilev_mm_doordaughter","v_ilev_mm_doorm_l","v_ilev_mm_doorm_r","v_ilev_mm_doorson","v_ilev_mm_doorw","v_ilev_moteldoorcso","v_ilev_mp_high_frontdoor","v_ilev_mp_low_frontdoor","v_ilev_mp_mid_frontdoor","v_ilev_ph_door002","v_ilev_ph_door01","v_ilev_ph_doorframe","v_ilev_ph_gendoor","v_ilev_ph_gendoor002","v_ilev_ph_gendoor003","v_ilev_ph_gendoor004","v_ilev_ph_gendoor005","v_ilev_ph_gendoor006","v_ilev_phroofdoor","v_ilev_po_door","v_ilev_ra_door1_l","v_ilev_ra_door1_r","v_ilev_ra_door2","v_ilev_ra_door3","v_ilev_ra_door4l","v_ilev_ra_door4r","v_ilev_rc_door1","v_ilev_rc_door1_st","v_ilev_rc_door2","v_ilev_rc_door3_l","v_ilev_rc_door3_r","v_ilev_rc_doorel_l","v_ilev_rc_doorel_r","v_ilev_roc_door1_l","v_ilev_roc_door1_r","v_ilev_roc_door2","v_ilev_roc_door3","v_ilev_roc_door4","v_ilev_roc_door5","v_ilev_serv_door01","v_ilev_shrf2door","v_ilev_shrfdoor","v_ilev_sol_off_door01","v_ilev_spraydoor","v_ilev_ss_door01","v_ilev_ss_door02","v_ilev_ss_door03","v_ilev_ss_door04","v_ilev_ss_door5_l","v_ilev_ss_door5_r","v_ilev_ss_doorext","v_ilev_stad_fdoor","v_ilev_staffdoor","v_ilev_store_door","v_ilev_ta_door","v_ilev_ta_door2","v_ilev_tort_door","v_ilev_trev_door","v_ilev_trev_doorbath","v_ilev_trev_doorfront","v_ilev_trev_patiodoor","v_ilev_vag_door","v_ilev_vagostoiletdoor" };
    int[] doorHashes;
    int nextDoorUnlock; int nextDoorLog; int nextDoorDiag;

    bool IsDoorModel(Prop pr)
    {
        int mh = pr.Model.Hash;
        bool r;
        if (doorModelIs.TryGetValue(mh, out r)) return r;
        r = false;
        try
        {
            Vector3 mn, mx;
            pr.Model.GetDimensions(out mn, out mx);
            float w = mx.X - mn.X, d = mx.Y - mn.Y, h = mx.Z - mn.Z;
            float thin = Math.Min(w, d), wide = Math.Max(w, d);
            r = thin < 0.25f && wide > 0.6f && wide < 1.7f && h > 1.8f && h < 3.0f;
        }
        catch { }
        doorModelIs[mh] = r;
        return r;
    }

    void UnlockHomeDoors(Ped me, int now)
    {
        if (now < nextDoorUnlock) return;
        nextDoorUnlock = now + 200;
        // dong cua khi Sep di xa
        for (int i = openDoors.Count - 1; i >= 0; i--)
        {
            int h = openDoors[i];
            bool ok = Function.Call<bool>(Hash.DOES_ENTITY_EXIST, h);
            Vector3 hp = ok ? Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, h, true) : Vector3.Zero;
            if (!ok) { openDoors.RemoveAt(i); continue; }
            float ty;
            if (me.Position.DistanceTo(hp) <= 4.5f && doorTargetYaw.TryGetValue(h, out ty)) Function.Call(Hash.SET_ENTITY_ROTATION, h, 0f, 0f, ty, 2, true);
            if (me.Position.DistanceTo(hp) > 4.5f)
            {
                float oh;
                if (doorOrigYaw.TryGetValue(h, out oh)) Function.Call(Hash.SET_ENTITY_ROTATION, h, 0f, 0f, oh, 2, true);
                Function.Call(Hash.SET_ENTITY_COLLISION, h, true, false);
                openDoors.RemoveAt(i);
            }
        }
        if (me.IsInVehicle()) return;
        if (doorHashes == null)
        {
            List<int> hl = new List<int>();
            foreach (string nm in DoorModelNames) hl.Add((int)new Model(nm).Hash);
            doorHashes = hl.ToArray();
            Log("danh sach model cua: " + doorHashes.Length);
        }
        Vector3 mp = me.Position;
        List<int> found = new List<int>();
        foreach (int mh in doorHashes)
        {
            int fh = 0;
            try { fh = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE, mp.X, mp.Y, mp.Z, 2.2f, mh, false, false, false); } catch { }
            if (fh != 0 && !found.Contains(fh)) found.Add(fh);
        }
        try
        {
            foreach (Prop pp in World.GetNearbyProps(mp, 2.2f))
                if (pp != null && pp.Exists() && !found.Contains(pp.Handle) && IsDoorModel(pp)) found.Add(pp.Handle);
        }
        catch (Exception ex) { if (now > nextDoorDiag) { nextDoorDiag = now + 5000; Log("quet vat the loi: " + ex.Message); } }
        if (now > nextDoorDiag && found.Count == 0) { nextDoorDiag = now + 6000; try { Log("quanh Sep khong thay cua nao; props gan=" + World.GetNearbyProps(mp, 2.2f).Length); } catch { } }
        foreach (int h in found)
        {
            if (openDoors.Contains(h)) continue;
            Vector3 hpos = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, h, true);
            float oh = Function.Call<float>(Hash.GET_ENTITY_HEADING, h);
            doorOrigYaw[h] = oh;
            double rad = oh * Math.PI / 180.0;
            Vector3 hp = hpos;
            Vector3 rel = me.Position - hp;
            float side = rel.X * (float)Math.Cos(rad) + rel.Y * (float)Math.Sin(rad);
            float yaw = oh + (side > 0f ? -95f : 95f);
            Function.Call(Hash.FREEZE_ENTITY_POSITION, h, true);
            Function.Call(Hash.SET_ENTITY_COLLISION, h, false, false);
            Function.Call(Hash.SET_ENTITY_ROTATION, h, 0f, 0f, yaw, 2, true);
            doorTargetYaw[h] = yaw;
            openDoors.Add(h);
            if (now > nextDoorLog) { nextDoorLog = now + 2000; Log("mo cua handle " + h + " tai " + hp.X.ToString("0.0") + "," + hp.Y.ToString("0.0") + "," + hp.Z.ToString("0.0")); }
        }
    }

    // ---- do vat trong nha: tim noi that co san (giuong, tu lanh, sofa, tv) gan Sep, bam E de dung
    static readonly string[] BedModels = { "v_res_mbbed", "v_res_mbbed_mess", "v_res_mdbed", "v_res_msonbed", "v_res_msonbed_s", "v_res_d_bed", "v_res_lestersbed", "v_res_tre_bed1", "v_res_tre_bed1_messy", "v_res_tre_bed2", "apa_mp_h_bed_double_08", "apa_mp_h_bed_double_09", "apa_mp_h_bed_wide_05", "apa_mp_h_bed_with_table_02" };
    static readonly string[] FridgeModels = { "v_res_fridgemoda", "v_res_fridgemodsml", "v_ilev_mm_fridge_l", "v_ilev_mm_fridge_r", "prop_fridge_01", "prop_fridge_03", "prop_trailr_fridge", "prop_cs_fridge" };
    static readonly string[] SofaModels = { "v_res_m_h_sofa", "v_res_m_h_sofa_sml", "v_res_mp_sofa", "v_res_d_sofa", "v_res_fh_sofa", "v_res_j_sofa", "v_res_r_sofa", "v_ilev_m_sofa", "prop_t_sofa", "prop_t_sofa_02", "apa_mp_h_stn_sofacorn_01", "apa_mp_h_stn_sofacorn_05", "apa_mp_h_stn_sofa2seat_02", "prop_couch_lg_02", "prop_couch_sm_02" };
    string nearFurn = ""; int nextFurnScan; int nextRest;

    bool NearModel(Vector3 p, string[] names, float r)
    {
        foreach (string n in names)
        {
            try
            {
                int h = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE, p.X, p.Y, p.Z, r, (int)new Model(n).Hash, false, false, false);
                if (h != 0) return true;
            }
            catch { }
        }
        return false;
    }

    void ScanFurniture(Ped me, int now)
    {
        if (!insideHome || enterUntil > 0 || me.IsInVehicle()) { nearFurn = ""; return; }
        if (now < nextFurnScan) return;
        nextFurnScan = now + 600;
        Vector3 p = me.Position;
        nearFurn = NearModel(p, BedModels, 2.3f) ? "bed" : NearModel(p, FridgeModels, 2.0f) ? "fridge" : NearModel(p, SofaModels, 1.8f) ? "sofa" : "";
    }

    void UseFurniture()
    {
        if (nearFurn == "bed") { CloseMenu(); Sleep(); }
        else if (nearFurn == "fridge" && LifeFridgeOn())
        {
            AppDomain.CurrentDomain.SetData("GVL.FridgeReq", true);   // file Doi song 2 lo: an mon trong tu lanh (co han dung)
        }
        else if (nearFurn == "fridge")
        {
            hunger = Clamp(hunger + 30f); thirst = Clamp(thirst + 30f);
            Notify("Lay do trong tu lanh: an uong mien phi (+30 no, +30 nuoc).");
        }
        else if (nearFurn == "sofa")
        {
            if (Game.GameTime < nextRest) { Notify("Vua nghi xong, doi mot luc."); return; }
            nextRest = Game.GameTime + 60000;
            energy = Clamp(energy + 8f);
            Notify("Nghi tren sofa mot lat (+8 nang luong).");
        }
    }

    // ---- tim nha thue
    void RentHome(int idx)
    {
        HomeInfo h = Homes[idx];
        int deposit = h.Rent * 3;
        if (!Spend(deposit)) return;
        homeIdx = idx;
        hasHome = true;
        home = h.Pos;
        rentPerDay = h.Rent;
        homeName = h.Name;
        homeEnergy = h.Energy;
        MakeHomeBlips();
        Notify("~g~Da thue " + h.Name + "~s~. Coc 3 ngay $" + deposit + ", thue $" + h.Rent + "/ngay. Den gan nha bam " + MK + " de ngu.");
        PostEvent("Có gã vừa thuê " + h.Name + ".");
        Save();
    }

    void LeaveHome()
    {
        hasHome = false;
        homeIdx = -1;
        homeName = "";
        rentPerDay = RentPerDay;
        MakeHomeBlips();
        Notify("Da tra nha.");
    }

    void ShowHomes()
    {
        Begin("TIM NHA THUE  (coc 3 ngay)", delegate { ShowMain(); });
        Vector3 me = Game.Player.Character.Position;
        for (int i = 0; i < Homes.Length; i++)
        {
            int idx = i;
            HomeInfo h = Homes[i];
            int dist = (int)me.DistanceTo2D(h.Pos);
            string tag = (homeIdx == i) ? "  [DANG O]" : "";
            Item(h.Name + "  $" + h.Rent + "/ngay  (" + h.Note + ")  cach " + dist + "m" + tag, delegate
            {
                if (homeIdx == idx) { Notify("Sep dang o day roi."); return; }
                if (Game.Player.Character.Position.DistanceTo2D(Homes[idx].Pos) < 80f)
                {
                    if (homeIdx >= 0 || (hasHome && homeIdx < 0)) LeaveHome();
                    RentHome(idx);
                    ShowHomes();
                }
                else
                {
                    Function.Call(Hash.SET_NEW_WAYPOINT, Homes[idx].Pos.X, Homes[idx].Pos.Y);
                    Notify("Da dat diem den: ~y~" + Homes[idx].Name + "~s~. Den gan (80 m) roi mo lai muc nay de ky thue.");
                    CloseMenu();
                }
            });
        }
        if (hasHome) Item("Tra nha hien tai (" + (homeName.Length > 0 ? homeName : "cho tu chon") + ")", delegate { LeaveHome(); ShowHomes(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    // ---- ngu / lam ca (mo man hinh den, tua gio, xong thi sang lai)
    void Sleep()
    {
        Ped me = Game.Player.Character;
        float maxDist = homeIdx >= 0 ? 70f : 30f;
        if (!hasHome || (!insideHome && me.Position.DistanceTo(home) > maxDist)) { Notify("Phai o gan nha (" + (int)maxDist + " m) moi ngu duoc."); return; }
        if (me.IsInVehicle()) { Notify("Xuong xe da."); return; }
        fadeAction = 1;
        GTA.UI.Screen.FadeOut(800);
        sleepAt = Game.GameTime + 1100;
    }

    void SleepRough()
    {
        Ped me = Game.Player.Character;
        if (me.IsInVehicle()) { Notify("Xuong xe da."); return; }
        if (Game.Player.WantedLevel > 0) { Notify("Dang bi truy na, khong ngu duoc."); return; }
        fadeAction = 2;
        GTA.UI.Screen.FadeOut(800);
        sleepAt = Game.GameTime + 1100;
    }

    void StartWork()
    {
        Ped me = Game.Player.Character;
        int hour = World.CurrentTimeOfDay.Hours;
        if (me.Position.DistanceTo2D(WorkSpot) > 40f)
        {
            Function.Call(Hash.SET_NEW_WAYPOINT, WorkSpot.X, WorkSpot.Y);
            Notify("Da dat diem den: ~y~Los Santos Customs~s~ (Burton). Den gan roi mo lai muc nay de lam ca.");
            return;
        }
        if (hour < 7 || hour >= 18) { Notify("Chi lam ca tu 7h den 18h."); return; }
        if (me.IsInVehicle()) { Notify("Xuong xe da."); return; }
        fadeAction = 3;
        GTA.UI.Screen.FadeOut(800);
        sleepAt = Game.GameTime + 1100;
    }

    void FinishSleep()
    {
        Ped me = Game.Player.Character;
        if (fadeAction == 1 || fadeAction == 2 || fadeAction == 4) sleptCount++;
        if (FinishLifeAction(me)) { fadeAction = 0; Save(); return; }
        if (fadeAction == 3)
        {
            AdvanceTime(4 * 60);
            EarnCash(WorkPay, "wage", "luong cong viec");
            hunger = Clamp(hunger - 15f);
            thirst = Clamp(thirst - 18f);
            energy = Clamp(energy - 20f);
            hygiene = Clamp(hygiene - 25f);
            rep += 1;
            GTA.UI.Screen.FadeIn(800);
            Notify("~g~Het ca tho phu 4 gio:~s~ +$" + WorkPay + ". Hoi met, doi va khat.");
        }
        else if (fadeAction == 2)
        {
            AdvanceTime(6 * 60);
            energy = Math.Max(energy, 70f);
            hunger = Clamp(hunger - 18f);
            thirst = Clamp(thirst - 18f);
            hygiene = Clamp(hygiene - 20f);
            me.Health = Math.Max(me.Health, me.MaxHealth * 3 / 4);
            string extra = "";
            if (rnd.Next(100) < 30 && Game.Player.Money > 0)
            {
                int stolen = Math.Min(Game.Player.Money, 50 + rnd.Next(250));
                if (!PayCash(stolen, "theft", "bi trom khi ngu")) stolen = 0;
                extra = " ~r~Bi mat $" + stolen + " luc dang ngu!~s~";
            }
            GTA.UI.Screen.FadeIn(800);
            Notify("Ngu ngoai duong 6 tieng." + extra);
        }
        else
        {
            AdvanceTime(8 * 60);
            energy = Math.Max(energy, homeEnergy);
            hunger = Clamp(hunger - 15f);
            thirst = Clamp(thirst - 15f);
            me.Health = me.MaxHealth;
            GTA.UI.Screen.FadeIn(800);
            Notify("Da ngu 8 tieng. Khoe re!");
        }
        fadeAction = 0;
        Save();
    }

    // ---- camera phat toc do
    void UpdateTraffic(Ped me, int now)
    {
        if (now < nextTraffic || !me.IsInVehicle() || Game.Player.WantedLevel > 0) return;
        Vehicle v = me.CurrentVehicle;
        if (v == null || !v.Exists()) return;
        if (v.Speed > 38f && rnd.Next(100) < 30)   // > ~137 km/h
        {
            nextTraffic = now + 60000;
            int fine = 200;
            if (PayCash(fine, "fine", "camera phat toc do")) { Notify("~r~Camera phat toc do:~s~ -$" + fine + "."); }
            else { debt += fine; Notify("~r~Camera phat toc do:~s~ thieu tien, ghi no $" + fine + "."); }
            rep -= 1;
        }
    }

    // ------------------------------------------------------------ di chuyen: taxi + tai xe rieng
    class Ride
    {
        public int Kind;        // 0 taxi, 1 tai xe mang xe cua Sep toi, 2 tai xe cho Sep di bang xe cua Sep
        public int State;       // 0 cho nap model, 1 dang toi, 2 cho Sep len xe, 3 dang cho di, 4 da xong (cho Sep xuong)
        public Vehicle Veh; public Ped Driver;
        public bool OwnVeh;     // xe cua Sep: ket thuc thi giu lai xe
        public Vector3 Start, Dest;
        public int T;
        public Model VehModel, PedModel;
        public Vector3 SpawnPos; public float SpawnHead;
    }
    Ride ride;
    Vehicle ownVeh;
    int ownVehHash;
    const int TaxiBase = 15;
    const float TaxiPerM = 0.02f;
    const int DriverFee = 50;
    const int RecoverFee = 150;   // goi xe ve khi xe da mat/qua xa

    bool HasWaypoint() { try { return Function.Call<bool>(Hash.IS_WAYPOINT_ACTIVE); } catch { return false; } }
    Vector3 WaypointGround()
    {
        Vector3 w = World.WaypointPosition;
        float z = World.GetGroundHeight(new Vector3(w.X, w.Y, 1000f));
        w.Z = z > 1f ? z : Game.Player.Character.Position.Z;
        return w;
    }

    Vector3 RoadNear(Vector3 around, float dist)
    {
        double a = rnd.NextDouble() * Math.PI * 2.0;
        Vector3 raw = around + new Vector3((float)Math.Cos(a) * dist, (float)Math.Sin(a) * dist, 0f);
        Vector3 r = World.GetNextPositionOnStreet(raw);
        return r == Vector3.Zero ? raw : r;
    }

    void CallTaxi()
    {
        Ped me = Game.Player.Character;
        if (ride != null) { Notify("Dang co mot chuyen xe roi. Huy truoc neu muon goi lai."); return; }
        ride = new Ride();
        ride.Kind = 0; ride.State = 0; ride.T = Game.GameTime;
        ride.VehModel = new Model("taxi"); ride.PedModel = new Model("a_m_m_business_01");
        ride.SpawnPos = RoadNear(me.Position, 120f);
        ride.SpawnHead = FaceHeading(ride.SpawnPos, me.Position);
        Notify("Da goi ~y~taxi~s~. Xe dang toi, phi $" + TaxiBase + " + $" + TaxiPerM.ToString("0.00", CultureInfo.InvariantCulture) + "/m.");
    }

    void CallDriver(bool giveRide)
    {
        Ped me = Game.Player.Character;
        if (ride != null) { Notify("Dang co mot chuyen xe roi. Huy truoc neu muon goi lai."); return; }
        if (ownVehHash == 0) { Notify("Sep chua co xe rieng. Ngoi tren xe roi chon 'Ghi nho xe nay la xe cua Sep'."); return; }
        bool have = ownVeh != null && ownVeh.Exists() && ownVeh.IsAlive && ownVeh.Position.DistanceTo(me.Position) < 700f
                    && Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, ownVeh, -1, false);
        int fee = DriverFee + (have ? 0 : RecoverFee);
        if (!Spend(fee)) return;
        ride = new Ride();
        ride.Kind = giveRide ? 2 : 1; ride.State = 0; ride.T = Game.GameTime; ride.OwnVeh = true;
        ride.PedModel = new Model("s_m_y_valet_01");
        if (have) ride.Veh = ownVeh;
        else
        {
            ride.VehModel = new Model(ownVehHash);
            ride.SpawnPos = RoadNear(me.Position, 110f);
            ride.SpawnHead = FaceHeading(ride.SpawnPos, me.Position);
        }
        Notify("Tai xe dang " + (have ? "lay xe cua Sep" : "dua xe cua Sep ve tu bai giu xe") + " toi (-$" + fee + ").");
    }

    void RememberVehicle()
    {
        Ped me = Game.Player.Character;
        Vehicle v = me.CurrentVehicle;
        if (v == null || !v.Exists()) { Notify("Hay ngoi tren xe muon ghi nho."); return; }
        if (ownVeh != null && ownVeh.Exists() && ownVeh != v) ownVeh.IsPersistent = false;
        ownVeh = v; ownVeh.IsPersistent = true; ownVehHash = v.Model.Hash;
        Notify("~g~Da ghi nho xe nay la xe cua Sep.~s~ Goi tai xe trong menu Di chuyen.");
        Save();
    }

    void BoardNow()
    {
        if (ride == null || ride.Veh == null || !ride.Veh.Exists()) return;
        Ped me = Game.Player.Character;
        if (me.Position.DistanceTo(ride.Veh.Position) > 25f) { Notify("Xe con xa, cho xe toi gan da."); return; }
        me.SetIntoVehicle(ride.Veh, VehicleSeat.RightRear);
    }

    void EndRide(bool driveAway)
    {
        if (ride == null) return;
        Ride r = ride; ride = null;
        try
        {
            if (r.Driver != null && r.Driver.Exists())
            {
                if (r.OwnVeh)
                {
                    if (r.Veh != null && r.Veh.Exists() && r.Driver.IsInVehicle(r.Veh)) Function.Call(Hash.TASK_LEAVE_VEHICLE, r.Driver, r.Veh, 0);
                    r.Driver.MarkAsNoLongerNeeded();
                }
                else if (driveAway && r.Veh != null && r.Veh.Exists())
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, r.Driver, r.Veh, 15f, 786603);
                    r.Driver.MarkAsNoLongerNeeded(); r.Veh.MarkAsNoLongerNeeded();
                }
                else { r.Driver.Delete(); if (r.Veh != null && r.Veh.Exists()) r.Veh.Delete(); }
            }
            else if (!r.OwnVeh && r.Veh != null && r.Veh.Exists()) r.Veh.Delete();
        }
        catch (Exception ex) { Log("ket thuc chuyen xe loi: " + ex.Message); }
    }

    void DriveTo(Vector3 d, float speed)
    {
        Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, ride.Driver, ride.Veh, d.X, d.Y, d.Z, speed, 786603, 12f);
    }

    void UpdateRide(Ped me, int now)
    {
        if (ride == null) return;
        Ride r = ride;
        if (r.State == 0)
        {
            if (now - r.T > 20000) { Notify("Khong goi duoc xe, thu lai sau."); if (r.OwnVeh) EarnCash(DriverFee, "transport", "hoan phi goi xe that bai"); ride = null; return; }
            if (!Loaded(r.PedModel)) return;
            if (r.Veh == null)
            {
                if (!Loaded(r.VehModel)) return;
                r.Veh = World.CreateVehicle(r.VehModel, r.SpawnPos, r.SpawnHead);
                if (r.Veh == null || !r.Veh.Exists()) { Notify("Khong tao duoc xe."); ride = null; return; }
                r.Veh.IsPersistent = true;
                if (r.OwnVeh) { ownVeh = r.Veh; }
            }
            r.Driver = World.CreatePed(r.PedModel, r.Veh.Position + new Vector3(0f, 0f, 2f), r.Veh.Heading);
            if (r.Driver == null || !r.Driver.Exists()) { Notify("Khong tao duoc tai xe."); EndRide(false); return; }
            r.Driver.IsPersistent = true;
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, r.Driver, true);
            Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, r.Driver, false);          // khong cho Sep keo tai xe ra
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, r.Driver, 0, false);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, r.Driver, 17, true);        // khong bo chay khi bi va cham/danh
            r.Driver.SetIntoVehicle(r.Veh, VehicleSeat.Driver);
            DriveTo(me.Position, 22f);
            r.State = 1; r.T = now;
            return;
        }
        if (r.Veh == null || !r.Veh.Exists() || !r.Veh.IsAlive || r.Driver == null || !r.Driver.Exists() || !r.Driver.IsAlive)
        { Notify("Chuyen xe bi huy (xe hoac tai xe gap su co)."); EndRide(false); return; }
        if (r.State == 1)
        {
            float d = r.Veh.Position.DistanceTo(me.Position);
            if ((now - r.T) % 8000 < 30) DriveTo(me.Position, 22f);   // cap nhat cho Sep dang dung
            if (d < 18f && r.Veh.Speed < 2f)
            {
                if (r.Kind == 1)
                {
                    Notify("~g~Xe cua Sep da toi.~s~ Tai xe giao chia khoa roi di.");
                    EndRide(false);
                    return;
                }
                r.State = 2; r.T = now;
                Notify((r.Kind == 0 ? "~y~Taxi~s~" : "~y~Tai xe~s~") + " da toi. Len xe (F, hoac menu Di chuyen > Len xe ngay) va dat diem den tren ban do.");
            }
            else if (now - r.T > 240000) { Notify("Xe ket duong qua lau, da huy."); EndRide(false); }
            return;
        }
        if (r.State == 1 || r.State == 2)
        {
            // Sep bam F lai gan xe: tu dua len ghe sau, tranh keo tai xe ra khoi ghe lai
            bool entering = false;
            try { entering = Function.Call<bool>(Hash.IS_PED_GETTING_INTO_A_VEHICLE, me); } catch { }
            if (entering && !me.IsInVehicle(r.Veh) && me.Position.DistanceTo(r.Veh.Position) < 12f && r.Kind != 1)
            {
                me.Task.ClearAll();
                me.SetIntoVehicle(r.Veh, VehicleSeat.RightRear);
            }
        }
        if (r.State == 1 && r.Kind != 1 && me.IsInVehicle(r.Veh)) { r.State = 2; r.T = now; }
        if (r.State == 2)
        {
            if (me.IsInVehicle(r.Veh))
            {
                if (!HasWaypoint()) { if ((now - r.T) % 6000 < 30) GTA.UI.Screen.ShowSubtitle("Dat ~y~diem den~s~ tren ban do de xe chay", 3000); return; }
                r.Dest = WaypointGround(); r.Start = r.Veh.Position;
                DriveTo(r.Dest, r.Kind == 0 ? 24f : 20f);
                r.State = 3; r.T = now;
                Notify("Dang di toi diem den (" + (int)r.Start.DistanceTo2D(r.Dest) + " m).");
            }
            else if (now - r.T > 120000) { Notify("Xe doi lau qua da di."); EndRide(r.Kind == 0); }
            return;
        }
        if (r.State == 3)
        {
            if (!me.IsInVehicle(r.Veh)) { FinishRide(r, me); EndRide(r.Kind == 0); return; }
            if (HasWaypoint() && WaypointGround().DistanceTo2D(r.Dest) > 30f) { r.Dest = WaypointGround(); DriveTo(r.Dest, r.Kind == 0 ? 24f : 20f); Notify("Doi diem den."); }
            if (r.Veh.Position.DistanceTo2D(r.Dest) < 30f && r.Veh.Speed < 2f)
            {
                FinishRide(r, me);
                r.State = 4; r.T = now;
                Notify("~g~Da toi noi.~s~ Sep xuong xe nhe.");
            }
            return;
        }
        if (r.State == 4)
        {
            if (!me.IsInVehicle(r.Veh) || now - r.T > 60000) EndRide(r.Kind == 0);
        }
    }

    void FinishRide(Ride r, Ped me)
    {
        if (r.Kind != 0 || r.Start == Vector3.Zero) return;
        float dist = r.Start.DistanceTo2D(r.Veh != null && r.Veh.Exists() ? r.Veh.Position : me.Position);
        int fare = TaxiBase + (int)(dist * TaxiPerM);
        if (PayCash(fare, "transport", "tien taxi")) { Notify("Tra tien taxi ~r~-$" + fare + "~s~ (" + (int)dist + " m)."); }
        else { debt += fare; Notify("~r~Khong du tien taxi, ghi no $" + fare + ".~s~"); }
        r.Start = Vector3.Zero;   // khong tinh tien hai lan
    }

    void ShowTransport()
    {
        Ped me = Game.Player.Character;
        Begin("DI CHUYEN", delegate { ShowMain(); });
        if (ride != null)
        {
            string st = ride.State <= 1 ? "xe dang toi" : ride.State == 2 ? "xe dang cho Sep" : ride.State == 3 ? "dang cho Sep di" : "da toi noi";
            Info("Chuyen hien tai: " + (ride.Kind == 0 ? "Taxi" : "Tai xe rieng") + " - " + st);
            if (ride.State == 2) Item("Len xe ngay  (ngoi ghe sau)", delegate { BoardNow(); CloseMenu(); });
            Item("Huy chuyen xe", delegate { EndRide(ride != null && ride.Kind == 0); Notify("Da huy chuyen xe."); ShowTransport(); });
        }
        else
        {
            Item("Goi taxi  (phi $" + TaxiBase + " + $" + TaxiPerM.ToString("0.00", CultureInfo.InvariantCulture) + "/m, tra khi toi noi)", delegate { CallTaxi(); CloseMenu(); });
            if (ownVehHash != 0)
            {
                Item("Nho tai xe mang xe cua Sep toi day  (-$" + DriverFee + ", xe mat/qua xa them $" + RecoverFee + ")", delegate { CallDriver(false); CloseMenu(); });
                Item("Nho tai xe cho Sep di bang xe rieng  (-$" + DriverFee + ", dat diem den tren ban do)", delegate { CallDriver(true); CloseMenu(); });
            }
            else Info("Chua co xe rieng: ngoi tren xe roi chon muc ghi nho ben duoi");
        }
        if (me.IsInVehicle()) Item("Ghi nho xe dang ngoi la xe cua Sep", delegate { RememberVehicle(); ShowTransport(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    // ==================================================================== DOI SONG
    // Cu dan co lich (luu lai qua cac lan choi), nhip pho theo gio, ve sinh ca nhan, dich vu doi thuong.

    // ---- cu dan: nguoi Sep chon lam hang xom; co nha, cho lam, cho an trua. Sep di xa thi ho "khuat", toi gan thi hien lai dung cho theo gio.
    const int MaxResidents = 8;
    const string ResFile = "GtaVietLifeResidents.txt";   // GtaVietLife.cs (noi chuyen) doc file nay de NPC nho ten va lich cua cu dan
    class Resident
    {
        public int Id;
        public Ped P;
        public int ModelHash;
        public bool Female;
        public int NameIdx;
        public Vector3 Home, Work, Lunch;
        public int Phase = -1;
        public int LastCmd;
        public bool EngineControlled;
        public string EngineActivity = "";
        public string EngineLocation = "";
        public bool EngineScenarioPending;
        public Blip B;
    }
    readonly List<Resident> residents = new List<Resident>();
    int nextResId = 1;
    string lastResFile = "";
    int lastResPh = -1;
    static readonly string[][] ResNamesM = { new[] { "Anh Tuan", "Anh Tuấn" }, new[] { "Chu Bay", "Chú Bảy" }, new[] { "Anh Khoa", "Anh Khoa" }, new[] { "Bac Sau", "Bác Sáu" }, new[] { "Anh Long", "Anh Long" }, new[] { "Anh Phat", "Anh Phát" }, new[] { "Cau Minh", "Cậu Minh" }, new[] { "Anh Hung", "Anh Hùng" } };
    static readonly string[][] ResNamesF = { new[] { "Chi Lan", "Chị Lan" }, new[] { "Co Hoa", "Cô Hoa" }, new[] { "Chi Mai", "Chị Mai" }, new[] { "Em Thu", "Em Thu" }, new[] { "Co Ut", "Cô Út" }, new[] { "Chi Hang", "Chị Hằng" }, new[] { "Ba Tu", "Bà Tư" }, new[] { "Em Vy", "Em Vy" } };
    string ResName(Resident r) { string[][] a = r.Female ? ResNamesF : ResNamesM; return a[((r.NameIdx % a.Length) + a.Length) % a.Length][0]; }
    string ResNameVi(Resident r) { string[][] a = r.Female ? ResNamesF : ResNamesM; return a[((r.NameIdx % a.Length) + a.Length) % a.Length][1]; }

    // lich: 0 ngu (23-6h), 1 sang som quanh nha (6-7h), 2 lam sang (7-11h), 3 an trua (11-13h), 4 lam chieu (13-17h), 5 tan lam di dao (17-19h), 6 toi o nha (19-23h)
    static int ResPhase(int h) { if (h >= 23 || h < 6) return 0; if (h < 7) return 1; if (h < 11) return 2; if (h < 13) return 3; if (h < 17) return 4; if (h < 19) return 5; return 6; }
    static readonly string[] ResPhaseName = { "dang ngu trong nha", "sang som quanh nha", "dang lam viec", "nghi an trua", "lam ca chieu", "tan lam, di dao", "buoi toi o nha" };
    static readonly string[] ResPhaseVi = { "khuya rồi, đang ngủ ở nhà (bị người chơi gọi dậy)", "sáng sớm, đang loanh quanh gần nhà", "đang trong giờ làm buổi sáng", "đang nghỉ trưa, đi ăn cơm gần chỗ làm", "đang trong giờ làm buổi chiều", "vừa tan làm, đang đi dạo thư giãn", "buổi tối, đang ở quanh nhà" };
    static Vector3 ResSpot(Resident r, int ph) { return ph == 2 || ph == 4 ? r.Work : ph == 3 ? r.Lunch : r.Home; }
    static float ResRadius(int ph) { return ph == 0 ? 3f : ph == 1 ? 8f : ph == 3 ? 10f : ph == 5 ? 25f : ph == 6 ? 8f : 15f; }

    Vector3 StreetAround(Vector3 c, float minD, float maxD)
    {
        for (int i = 0; i < 6; i++)
        {
            double ang = rnd.NextDouble() * Math.PI * 2;
            float dist = minD + (float)rnd.NextDouble() * (maxD - minD);
            Vector3 v = World.GetNextPositionOnStreet(c + new Vector3((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist, 0f));
            if (v != Vector3.Zero) return v;
        }
        return c;
    }

    // diem dung tren via he gan 'c' (tranh dat nguoi giua long duong)
    Vector3 SidewalkNear(Vector3 c)
    {
        try
        {
            OutputArgument o = new OutputArgument();
            if (Function.Call<bool>((Hash)0xB61C8E878A4199CA, c.X, c.Y, c.Z, true, o, 16)) { Vector3 v = o.GetResult<Vector3>(); if (v != Vector3.Zero) return v; }   // GET_SAFE_COORD_FOR_PED
        }
        catch { }
        return c;
    }

    void AttachResBlip(Resident r)
    {
        try
        {
            if (r.B != null && r.B.Exists()) r.B.Delete();
            r.B = r.P.AddBlip();
            r.B.Color = BlipColor.Blue;
            r.B.Scale = 0.6f;
            r.B.IsShortRange = true;
            r.B.Name = "Cu dan: " + ResName(r);
        }
        catch (Exception ex) { Log("blip cu dan loi: " + ex.Message); }
    }

    void DespawnRes(Resident r)
    {
        try { if (r.B != null && r.B.Exists()) r.B.Delete(); } catch { }
        r.B = null;
        try { if (r.P != null && r.P.Exists()) r.P.Delete(); } catch { }
        r.P = null;
        r.Phase = -1;
    }

    void MakeResident()
    {
        if (residents.Count >= MaxResidents) { Notify("Toi da " + MaxResidents + " cu dan. Bo bot mot nguoi trong danh sach truoc."); return; }
        Ped me = Game.Player.Character;
        Ped best = null;
        float bd = 6f;
        foreach (Ped p in World.GetNearbyPeds(me, 6f))
        {
            if (p == null || !p.Exists() || p == me || !p.IsHuman || !p.IsAlive || p.IsPlayer || p.IsInVehicle()) continue;
            bool taken = false;
            foreach (Resident r0 in residents) if (r0.P == p) taken = true;
            if (taken || staffPeds.Contains(p)) continue;
            float d = p.Position.DistanceTo(me.Position);
            if (d < bd) { bd = d; best = p; }
        }
        if (best == null) { Notify("Khong co ai o gan (6 m, di bo)."); return; }
        Resident r = new Resident();
        r.Id = nextResId++;
        r.P = best;
        r.ModelHash = best.Model.Hash;
        r.Female = best.Gender == Gender.Female;
        r.NameIdx = rnd.Next(8);
        r.Work = best.Position;
        r.Home = StreetAround(best.Position, 150f, 350f);
        r.Lunch = StreetAround(r.Work, 40f, 90f);
        best.IsPersistent = true;
        AttachResBlip(r);
        residents.Add(r);
        WriteResFile();
        Notify("~g~Cu dan moi: " + ResName(r) + "~s~. Ban ngay lam o day, trua di an gan day, toi ve nha, khuya ngu.");
        Save();
    }

    void ProcessEngineCommands(int now)
    {
        Queue<Dictionary<string, object>> queue = AppDomain.CurrentDomain.GetData("GVL.Engine.Commands") as Queue<Dictionary<string, object>>;
        if (queue == null) return;
        int available;
        lock (queue) available = queue.Count;
        int processed = 0;
        int examined = 0;
        while (processed < 8 && examined < available)
        {
            Dictionary<string, object> command = null;
            lock (queue) { if (queue.Count > 0) command = queue.Dequeue(); }
            if (command == null) return;
            examined++;
            try
            {
                string type = Convert.ToString(command.ContainsKey("command_type") ? command["command_type"] : "");
                string npcId = Convert.ToString(command.ContainsKey("npc_id") ? command["npc_id"] : "");
                string commandId = Convert.ToString(command.ContainsKey("command_id") ? command["command_id"] : "");
                Dictionary<string, object> payload = command.ContainsKey("payload") ? command["payload"] as Dictionary<string, object> : null;
                if (string.IsNullOrEmpty(commandId))
                {
                    Log("lenh Engine thieu command_id: " + type);
                    continue;
                }
                if (type != "actor.set_activity" || payload == null || string.IsNullOrEmpty(npcId))
                {
                    Log("lenh Engine khong ho tro: " + commandId + " / " + type);
                    Action<string, string, string> reportUnsupported = AppDomain.CurrentDomain.GetData("GVL.Engine.Report") as Action<string, string, string>;
                    if (reportUnsupported != null) { reportUnsupported(commandId, "unsupported", "unknown_command_or_invalid_payload"); processed++; }
                    else { lock (queue) queue.Enqueue(command); }
                    continue;
                }
                Resident resident = null;
                foreach (Resident item in residents) if ("res_" + item.Id == npcId) { resident = item; break; }
                if (resident == null || resident.P == null || !resident.P.Exists())
                {
                    lock (queue) queue.Enqueue(command);
                    continue; // NPC chua materialize: hoan lenh, khong chan cac NPC khac
                }
                bool followingPlayer = false;
                try { followingPlayer = Game.Player.Character.PedGroup.Contains(resident.P); } catch { }
                if (resident.P.IsPositionFrozen || followingPlayer)
                {
                    lock (queue) queue.Enqueue(command);
                    continue; // khong cat ngang hoi thoai/di theo nguoi choi
                }
                string activity = Convert.ToString(payload.ContainsKey("activity") ? payload["activity"] : "");
                string locationId = Convert.ToString(payload.ContainsKey("location_id") ? payload["location_id"] : "");
                if (!IsSupportedEngineResidentActivity(resident, activity, locationId))
                {
                    Action<string, string, string> reportUnsupported = AppDomain.CurrentDomain.GetData("GVL.Engine.Report") as Action<string, string, string>;
                    if (reportUnsupported != null) { reportUnsupported(commandId, "unsupported", "activity_or_location_not_mapped"); processed++; }
                    else { lock (queue) queue.Enqueue(command); }
                    continue;
                }
                if (!ApplyEngineResidentActivity(resident, activity, locationId, now))
                {
                    lock (queue) queue.Enqueue(command);
                    continue;
                }
                Action<string> ack = AppDomain.CurrentDomain.GetData("GVL.Engine.Ack") as Action<string>;
                if (ack != null) ack(commandId);
                processed++;
            }
            catch (Exception ex)
            {
                Log("thuc thi lenh NPC tu engine loi: " + ex.Message);
                lock (queue) queue.Enqueue(command);
            }
        }
    }

    static string WorldJsonEscape(string value)
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

    void SyncEngineWorldCatalog()
    {
        if (engineWorldCatalogSent || !(AppDomain.CurrentDomain.GetData("GVL.Engine.Connected") is bool) ||
            !(bool)AppDomain.CurrentDomain.GetData("GVL.Engine.Connected")) return;
        Action<string, string> post = AppDomain.CurrentDomain.GetData("GVL.Engine.WorldPost") as Action<string, string>;
        if (post == null) return;
        for (int i = 0; i < Homes.Length; i++)
        {
            string id = "building.home." + i;
            string exterior = "home.gta." + i + ".exterior";
            string interior = "home.gta." + i + ".interior";
            string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
                "\",\"building_id\":\"" + id + "\",\"category\":\"residential\",\"name\":\"" +
                WorldJsonEscape(Homes[i].Name) + "\",\"adapter_maps\":{\"gta5.shvdn\":{\"entrance\":\"" +
                exterior + "\",\"interior\":\"" + interior + "\"}},\"interactions\":[\"enter\",\"exit\",\"inspect\"]}";
            post("/engine/v1/world/buildings", body);
            foreach (string kind in new[] { "bed", "fridge", "sofa" })
            {
                string objectId = "object.home." + i + "." + kind;
                string objectBody = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
                    "\",\"object_id\":\"" + objectId + "\",\"building_id\":\"" + id +
                    "\",\"kind\":\"" + kind + "\",\"adapter_anchors\":{\"gta5.shvdn\":\"" + kind +
                    "\"},\"initial_state\":{}}";
                post("/engine/v1/world/objects", objectBody);
            }
        }
        engineWorldCatalogSent = true;
        Log("da dang ky danh muc nha va vat dung voi Life Engine");
    }

    bool RequestEngineWorldInteraction(string targetKind, string targetId, string action)
    {
        if (!(AppDomain.CurrentDomain.GetData("GVL.Engine.Connected") is bool) ||
            !(bool)AppDomain.CurrentDomain.GetData("GVL.Engine.Connected")) return false;
        Action<string, string> post = AppDomain.CurrentDomain.GetData("GVL.Engine.WorldPost") as Action<string, string>;
        if (post == null) return false;
        string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
            "\",\"actor_id\":\"player.main\",\"target_kind\":\"" + WorldJsonEscape(targetKind) +
            "\",\"target_id\":\"" + WorldJsonEscape(targetId) + "\",\"action\":\"" +
            WorldJsonEscape(action) + "\",\"adapter_id\":\"gta5.shvdn\"}";
        post("/engine/v1/world/interact", body);
        return true;
    }

    void ProcessEngineWorldCommands(int now)
    {
        Queue<Dictionary<string, object>> queue = AppDomain.CurrentDomain.GetData("GVL.Engine.WorldCommands") as Queue<Dictionary<string, object>>;
        if (queue == null) return;
        int processed = 0;
        while (processed < 4)
        {
            Dictionary<string, object> command = null;
            lock (queue) { if (queue.Count > 0) command = queue.Dequeue(); }
            if (command == null) break;
            object idValue, typeValue, actorValue;
            if (!command.TryGetValue("command_id", out idValue) || !command.TryGetValue("command_type", out typeValue))
            { processed++; continue; }
            string commandId = Convert.ToString(idValue, CultureInfo.InvariantCulture);
            string commandType = Convert.ToString(typeValue, CultureInfo.InvariantCulture);
            Dictionary<string, object> payload = command.ContainsKey("payload") ? command["payload"] as Dictionary<string, object> : null;
            if (payload == null) { ReportWorldCommand(commandId, "rejected", "missing_payload"); processed++; continue; }
            string targetId = Convert.ToString(payload.ContainsKey("target_id") ? payload["target_id"] : "");
            string action = Convert.ToString(payload.ContainsKey("action") ? payload["action"] : "");
            if (!string.IsNullOrEmpty(pendingWorldCommandId))
            {
                lock (queue) queue.Enqueue(command);
                break;
            }
            if (commandType == "space.transition" && targetId.StartsWith("building.home.", StringComparison.Ordinal))
            {
                int idx;
                if (!int.TryParse(targetId.Substring("building.home.".Length), out idx) || idx < 0 || idx >= Homes.Length)
                { ReportWorldCommand(commandId, "unsupported", "home_id_not_mapped"); processed++; continue; }
                if (!hasHome || homeIdx != idx)
                { ReportWorldCommand(commandId, "rejected", "player_does_not_lease_target_home"); processed++; continue; }
                Ped me = Game.Player.Character;
                if (me == null || !me.Exists() || me.IsInVehicle())
                { lock (queue) queue.Enqueue(command); break; }
                if (action == "enter")
                {
                    if (insideHome) { ReportWorldCommand(commandId, "completed", "already_inside"); processed++; continue; }
                    if (me.Position.DistanceTo(Homes[idx].Pos) > 25f)
                    { lock (queue) queue.Enqueue(command); break; }
                    EnterHome();
                    pendingWorldCommandId = commandId; pendingWorldCommandAction = "enter";
                    pendingWorldCommandDeadline = now + 15000;
                    processed++;
                    continue;
                }
                if (action == "exit")
                {
                    if (!insideHome) { ReportWorldCommand(commandId, "rejected", "player_not_inside_home"); processed++; continue; }
                    ExitHome();
                    pendingWorldCommandId = commandId; pendingWorldCommandAction = "exit";
                    pendingWorldCommandDeadline = now + 15000;
                    processed++;
                    continue;
                }
                ReportWorldCommand(commandId, "unsupported", "home_action_not_supported");
                processed++;
                continue;
            }
            if (commandType == "interaction.use_object" && targetId.StartsWith("object.home.", StringComparison.Ordinal))
            {
                string[] parts = targetId.Split('.');
                if (parts.Length < 4) { ReportWorldCommand(commandId, "unsupported", "object_id_invalid"); processed++; continue; }
                int idx;
                if (!int.TryParse(parts[2], out idx) || idx != homeIdx || parts[3] != nearFurn)
                { lock (queue) queue.Enqueue(command); break; }
                if (!insideHome) { ReportWorldCommand(commandId, "rejected", "object_requires_home_interior"); processed++; continue; }
                UseFurniture();
                ReportWorldCommand(commandId, "completed", "native_object_interaction_started");
                processed++;
                continue;
            }
            ReportWorldCommand(commandId, "unsupported", "world_command_not_mapped");
            processed++;
        }
    }

    void CompletePendingWorldTransition(int now)
    {
        if (string.IsNullOrEmpty(pendingWorldCommandId) || enterUntil > 0) return;
        bool completed = pendingWorldCommandAction == "enter" ? insideHome :
            pendingWorldCommandAction == "exit" ? !insideHome : false;
        string id = pendingWorldCommandId;
        if (completed) ReportWorldCommand(id, "completed", "player_space_transition_observed");
        else if (now >= pendingWorldCommandDeadline) ReportWorldCommand(id, "failed", "space_transition_not_observed");
        else return;
        pendingWorldCommandId = "";
        pendingWorldCommandAction = "";
        pendingWorldCommandDeadline = 0;
    }

    void ReportWorldCommand(string commandId, string status, string reason)
    {
        Action<string, string, string> report = AppDomain.CurrentDomain.GetData("GVL.Engine.WorldAck") as Action<string, string, string>;
        if (report != null) report(commandId, status, reason);
    }

    bool IsSupportedEngineResidentActivity(Resident resident, string activity, string locationId)
    {
        if (resident == null) return false;
        bool knownActivity = activity == "sleep" || activity == "eat" || activity == "work" ||
            activity == "commute" || activity == "socialize" || activity == "leisure" ||
            activity == "free_time" || activity == "idle";
        bool knownLocation = locationId == "res_" + resident.Id + ".home" ||
            locationId == "res_" + resident.Id + ".leisure" ||
            locationId == "res_" + resident.Id + ".work" ||
            locationId == "res_" + resident.Id + ".lunch";
        return knownActivity && knownLocation;
    }

    bool ApplyEngineResidentActivity(Resident resident, string activity, string locationId, int now)
    {
        if (resident == null || resident.P == null || !resident.P.Exists()) return false;
        Vector3 target;
        if (locationId == "res_" + resident.Id + ".home" || locationId == "res_" + resident.Id + ".leisure") target = resident.Home;
        else if (locationId == "res_" + resident.Id + ".work") target = resident.Work;
        else if (locationId == "res_" + resident.Id + ".lunch") target = resident.Lunch;
        else return false; // semantic location must have an explicit GTA mapping
        Ped ped = resident.P;
        resident.EngineControlled = true;
        resident.EngineActivity = activity;
        resident.EngineLocation = locationId;
        resident.Phase = -1;
        float distance = ped.Position.DistanceTo2D(target);
        if (distance > 10f)
        {
            resident.EngineScenarioPending = true;
            if (resident.LastCmd == 0 || now - resident.LastCmd > 5000)
            {
                resident.LastCmd = now;
                float speed = distance > 250f ? 2.0f : 1.0f;
                Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped, target.X, target.Y, target.Z, speed, -1, 2.0f, false, 0f);
            }
            return false; // command remains pending until the resident physically reaches the mapped area
        }
        return StartEngineActivityScenario(resident, target);
    }

    bool StartEngineActivityScenario(Resident resident, Vector3 target)
    {
        if (resident == null || resident.P == null || !resident.P.Exists()) return false;
        string scenario = "";
        switch (resident.EngineActivity)
        {
            case "sleep": scenario = "WORLD_HUMAN_BUM_SLEEP"; break;
            case "eat": scenario = "WORLD_HUMAN_EATING"; break;
            case "work": scenario = "WORLD_HUMAN_CLIPBOARD"; break;
            case "socialize": scenario = "WORLD_HUMAN_STAND_MOBILE"; break;
            case "leisure": scenario = "WORLD_HUMAN_PICNIC"; break;
            case "free_time": scenario = "WORLD_HUMAN_STAND_IMPATIENT"; break;
        }
        Ped ped = resident.P;
        try
        {
            Function.Call(Hash.CLEAR_PED_TASKS, ped);
            if (scenario.Length > 0)
                Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped, scenario, 0, true);
            else
            {
                float radius = resident.EngineActivity == "sleep" ? 2.5f : 7f;
                Function.Call(Hash.TASK_WANDER_IN_AREA, ped, target.X, target.Y, target.Z, radius, 2f, 6f);
            }
        }
        catch (Exception ex)
        {
            Log("scenario NPC khong kha dung (" + resident.EngineActivity + "): " + ex.Message);
            try { Function.Call(Hash.TASK_WANDER_IN_AREA, ped, target.X, target.Y, target.Z, 5f, 2f, 6f); } catch { }
        }
        resident.EngineScenarioPending = false;
        return true; // task was issued only after physical arrival was observed within the location radius
    }

    void UpdateResidents(int now)
    {
        if (now < nextResidentCheck || residents.Count == 0) return;
        nextResidentCheck = now + 3000;
        Ped me = Game.Player.Character;
        int ph = ResPhase(World.CurrentTimeOfDay.Hours);
        bool changed = false;
        for (int i = residents.Count - 1; i >= 0; i--)
        {
            Resident r = residents[i];
            Vector3 spot = ResSpot(r, ph);
            float rad = ResRadius(ph);
            if (r.P != null && r.P.Exists())
            {
                Ped p = r.P;
                if (!p.IsAlive)
                {
                    Notify("~r~Cu dan " + ResName(r) + " da mat.~s~");
                    PostEvent("Hàng xóm " + ResNameVi(r) + " vừa qua đời, cả khu xôn xao.");
                    DespawnRes(r); residents.RemoveAt(i); changed = true;
                    continue;
                }
                if (p.IsPositionFrozen) continue;   // dang noi chuyen voi Sep
                float dMe = p.Position.DistanceTo(me.Position);
                // Sep o xa, hoac khuya ma nguoi nay da ve toi nha va Sep khong o canh: cho "vao nha"/khuat, khoi ton bo nho
                if (dMe > 180f || (ph == 0 && dMe > 35f && p.Position.DistanceTo2D(r.Home) < 25f)) { DespawnRes(r); changed = true; continue; }
                bool inGroup = false;
                try { inGroup = me.PedGroup.Contains(p); } catch { }
                if (inGroup) continue;   // dang di theo Sep
                if (r.EngineControlled)
                {
                    if (r.EngineScenarioPending) ApplyEngineResidentActivity(r, r.EngineActivity, r.EngineLocation, now);
                    continue; // Engine is authoritative; wait for arrival before issuing the activity scenario
                }
                float d = p.Position.DistanceTo2D(spot);
                if (d > rad + 10f)
                {
                    if (now - r.LastCmd > 20000)
                    {
                        r.LastCmd = now; r.Phase = -1;
                        float spd = d > 250f ? 2.0f : 1.0f;   // xa qua thi di nhanh (chay bo nhe)
                        Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, p, spot.X, spot.Y, spot.Z, spd, -1, 2.0f, false, 0f);
                    }
                }
                else if (r.Phase != ph)
                {
                    r.Phase = ph;
                    Function.Call(Hash.TASK_WANDER_IN_AREA, p, spot.X, spot.Y, spot.Z, rad, 2f, 6f);
                }
            }
            else
            {
                if (r.P != null) { r.P = null; changed = true; }
                if (ph == 0) continue;   // dang ngu trong nha, khong hien ra
                float dSpot = me.Position.DistanceTo(spot);
                if (dSpot > 140f) continue;
                Vector3 sp = SidewalkNear(spot);
                bool seen = false;
                try { seen = Function.Call<bool>((Hash)0xE33D59DA70B58FDF, sp.X, sp.Y, sp.Z, 1.2f); } catch { }   // IS_SPHERE_VISIBLE
                if (seen && me.Position.DistanceTo(sp) < 60f) continue;   // khong cho nguoi "moc" ra truoc mat Sep
                Model m = new Model(r.ModelHash);
                if (!m.IsInCdImage) { Log("cu dan " + r.Id + ": model khong con, bo"); residents.RemoveAt(i); changed = true; continue; }
                if (!Loaded(m)) continue;
                Ped np = World.CreatePed(m, sp);
                if (np == null || !np.Exists()) continue;
                np.IsPersistent = true;
                r.P = np; r.LastCmd = 0; r.Phase = ph;
                AttachResBlip(r);
                Function.Call(Hash.TASK_WANDER_IN_AREA, np, spot.X, spot.Y, spot.Z, rad, 2f, 6f);
                if (r.EngineControlled && !string.IsNullOrEmpty(r.EngineActivity))
                    ApplyEngineResidentActivity(r, r.EngineActivity, r.EngineLocation, now);
                changed = true;
            }
        }
        if (changed || ph != lastResPh) { lastResPh = ph; WriteResFile(); }
    }

    // handle|khoa|ten co dau|dang lam gi  (de phan noi chuyen gan dung tri nho cho cu dan)
    void WriteResFile()
    {
        try
        {
            int ph = ResPhase(World.CurrentTimeOfDay.Hours);
            StringBuilder b = new StringBuilder();
            foreach (Resident r in residents)
                if (r.P != null && r.P.Exists()) b.Append(r.P.Handle + "|res_" + r.Id + "|" + ResNameVi(r) + "|" + ResPhaseVi[ph] + "\n");
            string s = b.ToString();
            if (s == lastResFile) return;
            lastResFile = s;
            File.WriteAllText(ResFile, s, new UTF8Encoding(false));
        }
        catch (Exception ex) { Log("ghi file cu dan loi: " + ex.Message); }
    }

    void ShowResidents()
    {
        Begin("CU DAN & HANG XOM  (" + residents.Count + "/" + MaxResidents + ")", delegate { ShowMain(); });
        int ph = ResPhase(World.CurrentTimeOfDay.Hours);
        Vector3 me = Game.Player.Character.Position;
        Item("Dat nguoi gan nhat lam cu dan  (co ten, co lich, nho Sep)", delegate { MakeResident(); ShowResidents(); });
        foreach (Resident r0 in residents)
        {
            Resident r = r0;
            Vector3 at = r.P != null && r.P.Exists() ? r.P.Position : ResSpot(r, ph);
            Item(ResName(r) + "  (" + ResPhaseName[ph] + ", cach " + (int)me.DistanceTo2D(at) + "m)", delegate { ShowResident(r); });
        }
        Item("Nhip pho theo gio: " + (rhythmOn ? "BAT" : "TAT") + "  (dem vang nguoi, gio cao diem dong)", delegate { rhythmOn = !rhythmOn; Save(); ShowResidents(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowResident(Resident r)
    {
        int ph = ResPhase(World.CurrentTimeOfDay.Hours);
        Begin("CU DAN: " + ResName(r).ToUpper(), delegate { ShowResidents(); });
        Info("Bay gio: " + ResPhaseName[ph] + (r.P != null && r.P.Exists() ? "" : " (dang khuat, toi gan se gap)"));
        Info("Lich: 7-11h lam, 11-13h an trua, 13-17h lam, 17-19h di dao, toi o nha, 23-6h ngu");
        Item("Dan duong toi cho nguoi nay dang o", delegate
        {
            Vector3 at = r.P != null && r.P.Exists() ? r.P.Position : ResSpot(r, ph);
            Function.Call(Hash.SET_NEW_WAYPOINT, at.X, at.Y);
            Notify("Da dat diem den: ~b~" + ResName(r) + "~s~.");
            CloseMenu();
        });
        Item("Dan duong toi nha nguoi nay", delegate { Function.Call(Hash.SET_NEW_WAYPOINT, r.Home.X, r.Home.Y); Notify("Da dat diem den: nha " + ResName(r) + "."); CloseMenu(); });
        Item("Thoi khong lam cu dan nua", delegate
        {
            residents.Remove(r);
            try { if (r.B != null && r.B.Exists()) r.B.Delete(); } catch { }
            if (r.P != null && r.P.Exists()) r.P.MarkAsNoLongerNeeded();
            r.P = null;
            WriteResFile(); Save();
            Notify(ResName(r) + " khong con la cu dan.");
            ShowResidents();
        });
        Item("Quay lai", delegate { ShowResidents(); });
    }

    // ---- nhip pho: so nguoi/xe tren duong theo gio (khuya vang, gio cao diem dong). Phai goi moi khung hinh.
    bool rhythmOn = true;
    void UpdateCityRhythm()
    {
        if (!rhythmOn) return;
        int h = World.CurrentTimeOfDay.Hours;
        float ped, veh;
        if (h < 4) { ped = 0.2f; veh = 0.3f; }
        else if (h < 6) { ped = 0.35f; veh = 0.45f; }
        else if (h < 7) { ped = 0.7f; veh = 0.8f; }
        else if (h < 9) { ped = 1f; veh = 1f; }        // cao diem sang
        else if (h < 11) { ped = 0.8f; veh = 0.8f; }
        else if (h < 13) { ped = 0.95f; veh = 0.85f; } // trua, ra duong an com
        else if (h < 17) { ped = 0.8f; veh = 0.8f; }
        else if (h < 19) { ped = 1f; veh = 1f; }       // cao diem chieu
        else if (h < 22) { ped = 0.75f; veh = 0.65f; }
        else { ped = 0.45f; veh = 0.45f; }
        try
        {
            Function.Call((Hash)0x95E3D6257B166CF2, ped);        // SET_PED_DENSITY_MULTIPLIER_THIS_FRAME
            Function.Call((Hash)0x7A556143A1C03898, ped, ped);   // SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME
            Function.Call((Hash)0x245A6883D966D537, veh);        // SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME
            Function.Call((Hash)0xB3B3359379FE77D3, veh);        // SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME
        }
        catch { }
    }

    // ---- cau noi voi file Doi song 2 (GtaVietLifeDoiSong.cs) qua AppDomain data, xem docs/CORE.md
    int sleptCount;
    readonly float[] lifeNeedsPub = new float[3];
    void SyncLifeBus()
    {
        try
        {
            AppDomain d = AppDomain.CurrentDomain;
            float[] add = d.GetData("GVL.NeedsAdd") as float[];   // file Ca nhan cung doc khoa nay; ai doc truoc thi cong roi xoa
            if (add != null && add.Length >= 3)
            {
                d.SetData("GVL.NeedsAdd", null);
                hunger = Clamp(hunger + add[0]); thirst = Clamp(thirst + add[1]); energy = Clamp(energy + add[2]);
            }
            object ha = d.GetData("GVL.HygAdd");
            if (ha is float) { d.SetData("GVL.HygAdd", null); hygiene = Clamp(hygiene + (float)ha); }
            object ra = d.GetData("GVL.RepAdd");
            if (ra is int) { d.SetData("GVL.RepAdd", null); rep += (int)ra; }
            lifeNeedsPub[0] = hunger; lifeNeedsPub[1] = thirst; lifeNeedsPub[2] = energy;
            d.SetData("GVL.Needs", lifeNeedsPub);
            d.SetData("GVL.Hygiene", hygiene);
            d.SetData("GVL.Home", new int[] { hasHome ? 1 : 0, insideHome ? 1 : 0 });
            d.SetData("GVL.Slept", sleptCount);
        }
        catch { }
    }

    // tra tien qua cong chung GVL.Pay (Core/Kinh te ghi so); khong co thi tru tien mat nhu cu
    bool PayVia(int amount, string cat, string note)
    {
        Func<int, string, string, bool> f = null;
        try { f = AppDomain.CurrentDomain.GetData("GVL.Pay") as Func<int, string, string, bool>; } catch { }
        if (f == null) return Spend(amount);
        bool ok = false;
        try { ok = f(amount, cat, note); } catch { }
        if (!ok) Notify("Khong du tien ($" + amount + ").");
        return ok;
    }

    static bool LifeFridgeOn() { try { object o = AppDomain.CurrentDomain.GetData("GVL.LifeOn"); return o is bool && (bool)o; } catch { return false; } }

    // ---- ve sinh ca nhan: giam dan theo gio game (chi khi dang choi), tam o nha/nha nghi. NPC thay Sep ban thi noi.
    float hygiene = 90f;
    int nextHygWarn;

    void Shower()
    {
        Ped me = Game.Player.Character;
        if (me.IsInVehicle()) { Notify("Xuong xe da."); return; }
        bool atHome = hasHome && (insideHome || me.Position.DistanceTo(home) < 30f);
        bool atMotel = NearMotel(me) >= 0;
        if (!atHome && !atMotel) { Notify("Chi tam duoc o nha minh hoac nha nghi."); return; }
        if (!atHome && !PayVia(ShowerMotelPrice, "leisure", "tam nha nghi")) return;
        fadeAction = 5;
        GTA.UI.Screen.FadeOut(600);
        sleepAt = Game.GameTime + 900;
    }

    // ---- dich vu doi thuong: rua xe, cuu ho sua xe, nha nghi
    const int CarWashPrice = 15, CarWashMobile = 40, MotelPrice = 60, ShowerMotelPrice = 10;
    // toa do uoc luong, chua kiem chung trong game: tiem rua xe o Strawberry; nha nghi Pink Cage (Alta)
    static readonly Vector3[] CarWashSpots = { new Vector3(25.0f, -1392.0f, 29.3f) };
    static readonly Vector3[] MotelSpots = { new Vector3(324.5f, -212.0f, 54.1f) };

    int NearMotel(Ped me) { for (int i = 0; i < MotelSpots.Length; i++) if (me.Position.DistanceTo(MotelSpots[i]) < 40f) return i; return -1; }

    Vehicle ServiceVehicle(Ped me)
    {
        if (me.IsInVehicle()) return me.CurrentVehicle;
        if (ownVeh != null && ownVeh.Exists() && ownVeh.Position.DistanceTo(me.Position) < 10f) return ownVeh;
        try
        {
            Vehicle lv = Function.Call<Vehicle>((Hash)0xB6997A7EB3F5C8C0);   // GET_PLAYERS_LAST_VEHICLE
            if (lv != null && lv.Exists() && lv.Position.DistanceTo(me.Position) < 10f) return lv;
        }
        catch { }
        return null;
    }

    Vehicle svcVeh;
    void WashCar()
    {
        Ped me = Game.Player.Character;
        Vehicle v = ServiceVehicle(me);
        if (v == null) { Notify("Ngoi tren xe (hoac dung canh xe 10 m) roi chon rua xe."); return; }
        bool atShop = false;
        foreach (Vector3 c in CarWashSpots) if (v.Position.DistanceTo(c) < 25f) atShop = true;
        int price = atShop ? CarWashPrice : CarWashMobile;
        if (!PayVia(price, "car", "rua xe")) return;
        svcVeh = v;
        fadeAction = 6;
        GTA.UI.Screen.FadeOut(600);
        sleepAt = Game.GameTime + 900;
        Notify((atShop ? "Rua xe tai tiem" : "Goi tho rua xe tan noi") + " (-$" + price + ").");
    }

    int RepairCost(Vehicle v)
    {
        float eng = 1000f, body = 1000f;
        try { eng = Function.Call<float>((Hash)0xC45D23BAF168AAB8, v); body = Function.Call<float>((Hash)0xF271147EB7B40F12, v); } catch { }   // GET_VEHICLE_ENGINE_HEALTH / BODY_HEALTH
        float dmg = Math.Max(0f, 1000f - eng) + Math.Max(0f, 1000f - body) * 0.5f;
        return 60 + (int)(dmg / 4f);
    }

    void RepairCar()
    {
        Ped me = Game.Player.Character;
        Vehicle v = ServiceVehicle(me);
        if (v == null) { Notify("Ngoi tren xe (hoac dung canh xe 10 m) roi goi cuu ho."); return; }
        int cost = RepairCost(v);
        if (!PayVia(cost, "car", "sua xe tai cho")) return;
        svcVeh = v;
        fadeAction = 7;
        GTA.UI.Screen.FadeOut(600);
        sleepAt = Game.GameTime + 900;
        Notify("Tho cuu ho toi sua xe tai cho (-$" + cost + ").");
    }

    void StayMotel()
    {
        Ped me = Game.Player.Character;
        if (NearMotel(me) < 0)
        {
            Function.Call(Hash.SET_NEW_WAYPOINT, MotelSpots[0].X, MotelSpots[0].Y);
            Notify("Da dat diem den: ~y~nha nghi~s~. Toi noi roi mo lai muc nay.");
            CloseMenu();
            return;
        }
        if (me.IsInVehicle()) { Notify("Xuong xe da."); return; }
        if (Game.Player.WantedLevel > 0) { Notify("Dang bi truy na, nha nghi khong cho thue."); return; }
        if (!PayVia(MotelPrice, "leisure", "nha nghi")) return;
        CloseMenu();
        fadeAction = 4;
        GTA.UI.Screen.FadeOut(800);
        sleepAt = Game.GameTime + 1100;
    }

    // cac viec tua gio cua phan doi song (fadeAction 4..7); tra ve true neu da xu ly
    bool FinishLifeAction(Ped me)
    {
        if (fadeAction == 4)
        {
            AdvanceTime(8 * 60);
            energy = Math.Max(energy, 90f);
            hunger = Clamp(hunger - 15f); thirst = Clamp(thirst - 15f);
            hygiene = 100f;
            me.Health = me.MaxHealth;
            GTA.UI.Screen.FadeIn(800);
            Notify("Ngu nha nghi 8 tieng, tam rua sach se (-$" + MotelPrice + ").");
            return true;
        }
        if (fadeAction == 5)
        {
            AdvanceTime(20);
            hygiene = 100f;
            energy = Clamp(energy + 3f);
            try { Function.Call((Hash)0x8FE22675A5A45817, me); Function.Call((Hash)0x9C720776DAA43E7E, me); } catch { }   // CLEAR_PED_BLOOD_DAMAGE, CLEAR_PED_WETNESS
            GTA.UI.Screen.FadeIn(600);
            Notify("Tam xong, thom tho sach se.");
            return true;
        }
        if (fadeAction == 6 || fadeAction == 7)
        {
            AdvanceTime(fadeAction == 6 ? 15 : 40);
            try
            {
                if (svcVeh != null && svcVeh.Exists())
                {
                    Function.Call((Hash)0x79D3B596FE44EE8B, svcVeh, 0f);   // SET_VEHICLE_DIRT_LEVEL
                    if (fadeAction == 7)
                    {
                        Function.Call((Hash)0x115722B1B9C14C1C, svcVeh);   // SET_VEHICLE_FIXED
                        Function.Call((Hash)0x953DA1E1B12C0491, svcVeh);   // SET_VEHICLE_DEFORMATION_FIXED
                    }
                }
            }
            catch (Exception ex) { Log("dich vu xe loi: " + ex.Message); }
            svcVeh = null;
            GTA.UI.Screen.FadeIn(600);
            Notify(fadeAction == 6 ? "Xe sach bong roi." : "Xe da sua xong, chay ngon.");
            return true;
        }
        return false;
    }

    void ShowServices()
    {
        Ped me = Game.Player.Character;
        Begin("DICH VU DOI THUONG", delegate { ShowMain(); });
        bool atHome = hasHome && (insideHome || me.Position.DistanceTo(home) < 30f);
        Item("Tam rua  (" + (atHome ? "o nha, mien phi" : "nha nghi $" + ShowerMotelPrice) + ")  - ve sinh " + (int)hygiene + "%", delegate { CloseMenu(); Shower(); });
        Vehicle v = ServiceVehicle(me);
        if (v != null)
        {
            bool atShop = false;
            foreach (Vector3 c in CarWashSpots) if (v.Position.DistanceTo(c) < 25f) atShop = true;
            Item("Rua xe  (" + (atShop ? "tai tiem $" + CarWashPrice : "tho toi tan noi $" + CarWashMobile) + ")", delegate { CloseMenu(); WashCar(); });
            Item("Goi cuu ho sua xe tai cho  ($" + RepairCost(v) + ")", delegate { CloseMenu(); RepairCar(); });
        }
        else
        {
            Info("Rua xe / sua xe: ngoi tren xe hoac dung canh xe");
            Item("Dan duong toi tiem rua xe (re hon: $" + CarWashPrice + ")", delegate { Function.Call(Hash.SET_NEW_WAYPOINT, CarWashSpots[0].X, CarWashSpots[0].Y); Notify("Da dat diem den: tiem rua xe."); CloseMenu(); });
        }
        Item("Ngu nha nghi  ($" + MotelPrice + "/dem, tam + ngu 8 tieng, an toan)", delegate { StayMotel(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    // ------------------------------------------------------------ menu
    void OpenMenu()
    {
        menuOpen = true;
        ShowMain();
    }

    void CloseMenu() { menuOpen = false; }

    void Item(string label, Action act)
    {
        menuLabels.Add(label);
        menuActions.Add(act);
    }

    // dong chi de doc (khong chon duoc)
    void Info(string label)
    {
        menuLabels.Add(label);
        menuActions.Add(null);
    }

    int menuSel;
    int NextActionable(int from, int dir)
    {
        int n = menuActions.Count;
        if (n == 0) return 0;
        for (int k = 1; k <= n; k++)
        {
            int j = ((from + dir * k) % n + n) % n;
            if (menuActions[j] != null) return j;
        }
        return from;
    }

    void Begin(string title, Action back)
    {
        menuTitle = title;
        menuLabels = new List<string>();
        menuActions = new List<Action>();
        menuBack = back;
        menuSel = -1;   // se chon dong dau tien chon duoc khi ve
    }

    void ShowMain()
    {
        Begin("DOI SONG  (" + MK + "/Backspace: dong)", null);
        Item("An com  (-$" + FoodPrice + ")", delegate { Eat(); });
        Item("Uong nuoc  (-$" + DrinkPrice + ")", delegate { Drink(); });
        if (hasHome) Item("Ngu o nha  (tua 8 gio)", delegate { CloseMenu(); Sleep(); });
        if (hasHome && homeIdx >= 0) Item(insideHome ? "Ra ngoai" : "Vao trong nha (dung gan nha)", delegate { CloseMenu(); if (insideHome) ExitHome(); else EnterHome(); });
        Item("Tim nha thue...", delegate { ShowHomes(); });
        Item("Ngu ngoai duong  (mien phi, 30% bi cuop)", delegate { CloseMenu(); SleepRough(); });
        Item("Cong viec...", delegate { ShowJob(); });
        Item("Kinh doanh...", delegate { ShowBiz(); });
        Item("Di chuyen: goi taxi, tai xe rieng...", delegate { ShowTransport(); });
        Item("Dich vu: tam rua, rua xe, sua xe, nha nghi...", delegate { ShowServices(); });
        Item("Cu dan & hang xom...", delegate { ShowResidents(); });
        Item("Thong tin", delegate { ShowInfo(); });
        if (debt > 0) Item("Tra no  ($" + debt + ")", delegate { PayDebt(); ShowMain(); });
    }

    void ShowJob()
    {
        Begin("CONG VIEC", delegate { ShowMain(); });
        if (!jobGiaoHang) Item("Dang ky lam Giao hang (ca 6h-22h)", delegate { jobGiaoHang = true; Notify("Da nhan viec ~g~Giao hang~s~."); ShowJob(); });
        else
        {
            if (!delivering) Item("Nhan don giao hang", delegate { CloseMenu(); StartDelivery(); });
            else Item("Huy don dang giao", delegate { EndDelivery(false, false); ShowJob(); });
            Item("Nghi viec", delegate { if (delivering) EndDelivery(false, false); jobGiaoHang = false; ShowJob(); });
        }
        if (jobGiaoHang) Info("Dang ky giao hang thi luc tat may van chay vai don moi ngay (2-5 don, tru tien xang).");
        Item("Lam ca tho phu o LS Customs  (4 gio, +$" + WorkPay + ", 7h-18h)", delegate { CloseMenu(); StartWork(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowBiz()
    {
        Begin("KINH DOANH", delegate { ShowMain(); });
        Item("Bat dong san - mua, cho thue toa nha that...  (" + owned.Count + " noi)", delegate { ShowEstates(); });
        if (shops.Count > 0) Item("Bang kho cac quan - dat hang tu xa...  (" + shops.Count + " quan)", delegate { ShowStockBoard(); });
        if (shops.Count > 0) Item("Ban si B2B - hop dong cung ung...  (cong no $" + ReceivableTotal() + ")", delegate { ShowB2B(); });
        if (shops.Count > 0) Item("Danh sach quan, dat diem den tren ban do...", delegate { ShowShopList(); });
        if (!hasBiz || !NearBiz())
        {
            if (hasBiz) Info("(Hay den gan quan de quan ly - cham xanh tren ban do)");
            Item("Mo quan moi o day...  (khu " + ZoneName(Game.Player.Character.Position) + ")", delegate { ShowOpenMenu(); });
        }
        else
        {
            Shop cur = shops[activeShop];
            SyncOut();
            Info("Quan #" + (activeShop + 1) + ": " + ModeName[cur.Mode] + " - " + ZoneName(cur.Pos) + "  (khach x" + (ModeTraffic[cur.Mode] * ZoneMult(cur.Pos)).ToString("0.0", CultureInfo.InvariantCulture) + (cur.Rent > 0 ? ", " + FeeWord(cur.Mode) + " $" + cur.Rent + "/ngay" : ", khong tien thue") + (cur.Mode == 2 ? ", con " + cur.DaysLeft + " ngay" : "") + ")");
            Item("Nhap hang +" + StockPack + " phan  (-$" + PackCost(bizType, 1, false) + ")  kho: " + bizStock, delegate
            {
                if (Spend(PackCost(bizType, 1, false))) { bizStock += StockPack; }
                ShowBiz();
            });
            Item("Nhap 5 lo +" + (StockPack * 5) + " phan, chiet khau NPP 7%  (-$" + PackCost(bizType, 5, false) + ")", delegate
            {
                if (Spend(PackCost(bizType, 5, false))) { bizStock += StockPack * 5; }
                ShowBiz();
            });
            Item("Nhap 10 lo +" + (StockPack * 10) + " phan, chiet khau NPP 12%  (-$" + PackCost(bizType, 10, false) + ")", delegate
            {
                if (Spend(PackCost(bizType, 10, false))) { bizStock += StockPack * 10; }
                ShowBiz();
            });
            if ((bizType == 6 || bizType == 7) && Game.Player.Character.IsInVehicle()) Item(bizType == 7 ? "Sua + rua xe cua Sep (mien phi, tru 1 luot vat tu)" : "Rua xe cua Sep (mien phi, tru 1 luot vat tu)", delegate { ServeOwnCar(); ShowBiz(); });
            Item("Doi quan sang cho phang gan day (mien phi)", delegate { RelocateBiz(); ShowBiz(); });
            int cap = CapOf(cur);
            Info("Loai hinh: " + Types[TIdx(bizType)].Name + " (" + (Types[TIdx(bizType)].Channel == 0 ? "GT" : "MT") + ")   " + Types[TIdx(bizType)].Note);
            Info("Hien tai: cap " + bizLevel + " - " + TierName[bizLevel - 1] + "  (gia ban TB $" + Price() + "/" + UW(bizType) + ", gia von TB $" + UnitCost(bizType) + ")");
            Item(bizLevel >= cap ? "Cap toi da cua hinh thuc nay (" + cap + ")" + (cap < 4 ? " - muon len nua hay chuyen hinh thuc thue tot hon" : "") : "Nang cap len cap " + (bizLevel + 1) + " " + TierName[bizLevel] + " (" + TierPerk[bizLevel] + ")  -$" + (2500 * bizLevel), delegate
            {
                if (bizLevel >= CapOf(shops[activeShop])) Notify("Quan nay chi toi da cap " + CapOf(shops[activeShop]) + ".");
                else if (Spend(2500 * bizLevel)) { bizLevel++; BuildBizProps(); Notify("~g~Nang cap thanh cong: " + TierName[bizLevel - 1] + "~s~. Gia ban $" + Price() + "/phan, them khach."); }
                ShowBiz();
            });
            Item("Thue nhan vien  (luong $" + WagePerDay + "/ngay)  hien: " + bizStaff + "/" + MaxStaff, delegate
            {
                if (bizStaff < MaxStaff) { bizStaff++; if (staffPeds.Count < MaxVisibleStaff) SpawnStaff(); } else Notify("Da du nhan vien.");
                ShowBiz();
            });
            Item("Sa thai 1 nhan vien", delegate
            {
                if (bizStaff > 0) { bizStaff--; if (staffPeds.Count > Math.Min(bizStaff, MaxVisibleStaff)) FireStaff(); }
                ShowBiz();
            });
            Item("Rut tien quan  ($" + bizCash + ")", delegate
            {
                EarnCash(bizCash, "business", "rut tien quy quan");
                Notify("Da rut $" + bizCash + ".");
                bizCash = 0;
                ShowBiz();
            });
            Item(cur.Mode == 3 ? "Ban lai quan (nhan 70% gia mua)..." : "Tra mat bang / dong quan...", delegate { ConfirmCloseShop(); });
            Item("Mo them quan moi o cho khac...  (cach quan nay it nhat " + (int)ShopGap + " m)", delegate { ShowOpenMenu(); });
        }
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowInfo()
    {
        Begin("THONG TIN", delegate { ShowMain(); });
        Info("Tien: $" + Game.Player.Money + "   No: $" + debt);
        Info("Uy tin: " + rep + "   Giao hang hom nay: " + deliveriesToday + " don ($" + earnedToday + ")");
        Info("Nha: " + (hasHome ? (homeName.Length > 0 ? homeName : "cho tu chon") + " ($" + rentPerDay + "/ngay)" : "chua thue"));
        Info("Quan: " + (shops.Count > 0 ? shops.Count + " quan (dang chay: #" + (activeShop + 1) + " cap " + bizLevel + " " + TierName[bizLevel - 1] + ", kho " + bizStock + ", nv " + bizStaff + ", quy $" + bizCash + ")" : "chua co"));
        { int ev = 0, en = 0; foreach (Owned o in owned) { ev += o.Value; en += o.LastNet; } Info("Bat dong san: " + owned.Count + " noi, gia tri $" + ev + ", loi rong ngay qua $" + en); }
        Info("Cu dan co lich: " + residents.Count);
        Item("Quay lai", delegate { ShowMain(); });
    }

    void PayDebt()
    {
        int pay = Math.Min(debt, Game.Player.Money);
        if (pay <= 0) { Notify("Khong co tien de tra."); return; }
        if (!PayCash(pay, "debt", "tra no")) return;
        debt -= pay;
        Notify("Da tra no $" + pay + (debt > 0 ? ", con $" + debt : ", het no!"));
    }

    // ---- Sep tu ngoi vao ghe cua quan
    int NearestFreeChair(Ped me)
    {
        int best = -1; float bd = 1.7f;
        for (int i = 0; i < chairProps.Count; i++)
        {
            Prop ch = chairProps[i];
            if (ch == null || !ch.Exists() || chairBusy[i] || ChairTaken(i) || SpotOccupied(i)) continue;
            float d = me.Position.DistanceTo2D(ch.Position);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    void UpdatePlayerSeat(Ped me)
    {
        if (menuOpen) return;
        if (sitIdx >= 0)
        {
            bool ok = sitIdx == 9999 ? (sitBench != null && sitBench.Exists()) : (sitIdx < chairProps.Count && chairProps[sitIdx] != null && chairProps[sitIdx].Exists());
            float mx = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 30);
            float my = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 31);
            if (!ok || me.IsInVehicle() || Math.Abs(mx) > 0.6f || Math.Abs(my) > 0.6f) StandUp(me);
            else
            {
                if (sitIdx != 9999) KeepOnSeat(me, sitIdx);
                GTA.UI.Screen.ShowSubtitle("~y~" + sitKey + "~s~: dung day", 100);
            }
            return;
        }
        if (hasBiz && !me.IsInVehicle() && (NearestFreeChair(me) >= 0 || NearestBench(me) != null))
            GTA.UI.Screen.ShowSubtitle("~y~" + sitKey + "~s~: ngoi vao ghe", 100);
    }

    Prop NearestBench(Ped me)
    {
        Prop best = null; float bd = 2.2f;
        foreach (Prop b in benchProps)
        {
            if (b == null || !b.Exists()) continue;
            float d = me.Position.DistanceTo2D(b.Position);
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    void SitDown(Ped me)
    {
        int i = NearestFreeChair(me);
        if (i < 0)
        {
            Prop b = NearestBench(me);
            if (b == null) return;
            sitBench = b;
            sitIdx = 9999;
            me.Task.ClearAllImmediately();
            Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, me, "PROP_HUMAN_SEAT_BENCH", b.Position.X, b.Position.Y, b.Position.Z + 0.5f, b.Heading + 180f, -1, true, true);
            return;
        }
        Prop ch = chairProps[i];
        sitIdx = i;
        chairBusy[i] = true;
        me.Task.ClearAllImmediately();
        Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, me, "PROP_HUMAN_SEAT_CHAIR", ch.Position.X, ch.Position.Y, ch.Position.Z + seatZ, i < chairFace.Count ? chairFace[i] : ch.Heading, -1, true, true);
    }

    // ngoi vao ghe ma bi game hat len cao thi keo ve dung cho ngoi (va ghi log de tim nguyen nhan)
    int seatFixLogs;
    void KeepOnSeat(Ped p, int chairIdx)
    {
        if (p == null || !p.Exists() || chairIdx < 0 || chairIdx >= chairProps.Count) return;
        Prop ch = chairProps[chairIdx];
        if (ch == null || !ch.Exists()) return;
        float expect = ch.Position.Z + seatZ;
        float dz = p.Position.Z - expect;
        if (dz > 3.0f || dz < -3.0f)
        {
            if (seatFixLogs++ < 12) Log("ngoi bi lech: ghe z=" + ch.Position.Z.ToString("0.00") + " nguoi z=" + p.Position.Z.ToString("0.00") + " dz=" + dz.ToString("0.00") + " cach ghe " + p.Position.DistanceTo2D(ch.Position).ToString("0.0") + " m");
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, p, ch.Position.X, ch.Position.Y, expect, false, false, false);
            p.Heading = (chairIdx < chairFace.Count) ? chairFace[chairIdx] : ch.Heading;
        }
    }

    // neu bi day len cao bat thuong quanh quan thi dua ve mat dat (chong roi chet)
    void SnapNearGround(Ped p)
    {
        if (p == null || !p.Exists() || !hasBiz) return;
        Vector3 pos = p.Position;
        if (pos.DistanceTo2D(bizPos) < 30f && Math.Abs(pos.Z - (GroundForBiz() + 1f)) > 2.5f)
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, p, pos.X, pos.Y, GroundForBiz() + 1f, false, false, false);
    }

    void StandUp(Ped me)
    {
        if (sitIdx >= 0 && sitIdx < chairBusy.Count) chairBusy[sitIdx] = false;
        sitIdx = -1;
        sitBench = null;
        me.Task.ClearAll();
        SnapNearGround(me);
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, me, true);   // khong chet vi roi khi dung day
        safeUntil = Game.GameTime + 3000;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != menuKey && PersonalMenuOpen()) return;   // dang mo menu Ca nhan: phim so/Enter thuoc ve menu do
        if (e.KeyCode == Keys.F8 && hasBiz)
        {
            // thoat ket / bi hat len cao: dua Sep ve mat dat canh quan
            Ped pl8 = Game.Player.Character;
            Function.Call(Hash.SET_ENTITY_INVINCIBLE, pl8, true);
            pl8.Task.ClearAllImmediately();
            Vector3 t = bizPos + new Vector3(-(float)Math.Sin(bizHeading * Math.PI / 180.0), (float)Math.Cos(bizHeading * Math.PI / 180.0), 0f) * 3f;
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, pl8, t.X, t.Y, GroundForBiz() + 1f, false, false, false);
            sitIdx = -1;
            safeUntil = Game.GameTime + 4000;
            Notify("Da dua Sep ve mat dat canh quan.");
        }
        if (e.KeyCode == Keys.F9 && hasBiz)
        {
            staffAngle = (staffAngle + 1) % 4;
            ReslotStaff();
            Notify("Cho dung cua nhan vien: huong " + (staffAngle + 1) + "/4 (F9 de xoay tiep)");
            Save();
        }
        if (e.KeyCode == sitKey && !menuOpen && nearFurn.Length > 0 && Game.GameTime > doorCool) { doorCool = Game.GameTime + 700; if (!RequestEngineWorldInteraction("object", "object.home." + Math.Max(0, homeIdx) + "." + nearFurn, "use")) UseFurniture(); return; }
        if (e.KeyCode == sitKey && !menuOpen && enterUntil <= 0 && Game.GameTime > doorCool && hasHome && homeIdx >= 0 && nearFurn.Length == 0 && !Game.Player.Character.IsInVehicle())
        {
            Vector3 pp = Game.Player.Character.Position;
            if (!insideHome && pp.DistanceTo(Homes[homeIdx].Pos) < 12f) { if (!RequestEngineWorldInteraction("building", "building.home." + homeIdx, "enter")) EnterHome(); return; }
            if (insideHome) { if (!RequestEngineWorldInteraction("building", "building.home." + homeIdx, "exit")) ExitHome(); return; }
        }
        if (e.KeyCode == stockKey && !menuOpen) { showStock = !showStock; Notify(showStock ? "Bang kho: ~g~bat~s~" : "Bang kho: ~y~tat~s~"); }
        if (e.KeyCode == sitKey && !menuOpen)
        {
            Ped pl = Game.Player.Character;
            if (sitIdx >= 0) StandUp(pl);
            else if (hasBiz && !pl.IsInVehicle()) SitDown(pl);
        }
        if (e.KeyCode == menuKey)
        {
            if (!menuOpen) OpenMenu();
            else if (menuBack != null) menuBack();
            else CloseMenu();
            return;
        }
        if (!menuOpen) return;
        if (e.KeyCode == Keys.Back)
        {
            if (menuBack != null) menuBack(); else CloseMenu();
            return;
        }
        if (menuSel < 0 || menuSel >= menuActions.Count || menuActions[menuSel] == null) menuSel = NextActionable(-1, 1);
        if (e.KeyCode == navUp) { menuSel = NextActionable(menuSel, -1); return; }
        if (e.KeyCode == navDown) { menuSel = NextActionable(menuSel, 1); return; }
        if (e.KeyCode == navOk)
        {
            if (menuSel >= 0 && menuSel < menuActions.Count && menuActions[menuSel] != null) pendingActs.Enqueue(menuActions[menuSel]);
            return;
        }
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

    // ------------------------------------------------------------ vẽ màn hình
    static Color NeedColor(float v)
    {
        return v < 15f ? Color.FromArgb(255, 255, 80, 80) : v < 35f ? Color.FromArgb(255, 255, 210, 80) : Color.White;
    }

    void DrawText(string s, float x, float y, float scale, Color c)
    {
        GTA.UI.TextElement t = new GTA.UI.TextElement(s, new PointF(x, y), scale, c);
        t.Outline = true;
        t.Draw();
    }

    void DrawBar(string label, float value, float x, float y, Color fill)
    {
        float w = 170f, h = 16f;
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, h), Color.FromArgb(150, 0, 0, 0)).Draw();
        Color c = value < 15f ? Color.FromArgb(230, 230, 60, 60) : fill;
        float fw = (w - 4f) * Math.Max(0f, Math.Min(100f, value)) / 100f;
        if (fw > 0f) new GTA.UI.ContainerElement(new PointF(x + 2f, y + 2f), new SizeF(fw, h - 4f), c).Draw();
        DrawText(label + " " + (int)value + "%", x + 6f, y - 3f, 0.34f, Color.White);
    }

    // bieu tuong ve bang cac o vuong nho (12x12), khong can file anh. 0 = trong suot, con lai la mau RGB.
    static readonly Dictionary<string, int[]> IconData = new Dictionary<string, int[]> {
        { "food", new int[] { 6, 3, 3, 1, 0xF0B050, 9, 3, 1, 1, 0xF0B030, 10, 3, 4, 1, 0xF0B050, 4, 4, 2, 1, 0xF0B050, 6, 4, 1, 1, 0xF0B030, 7, 4, 1, 4, 0xF0B050, 8, 4, 1, 2, 0xF09030, 9, 4, 1, 1, 0xF0B070, 10, 4, 1, 2, 0xF0B050, 11, 4, 1, 2, 0xF09030, 12, 4, 1, 1, 0xF0B030, 13, 4, 3, 1, 0xF0B050, 3, 5, 1, 1, 0xF0B030, 4, 5, 1, 1, 0xF0B050, 5, 5, 1, 2, 0xF09030, 6, 5, 1, 1, 0xF0B070, 9, 5, 1, 1, 0xF0B050, 12, 5, 2, 2, 0xF0B050, 14, 5, 1, 2, 0xF09030, 15, 5, 1, 1, 0xF0B050, 16, 5, 1, 1, 0xF0B030, 2, 6, 2, 2, 0xF0B050, 4, 6, 1, 1, 0xF09030, 6, 6, 1, 1, 0xF0B050, 8, 6, 1, 1, 0xF0B050, 9, 6, 2, 1, 0xF09030, 11, 6, 1, 1, 0xF0B050, 15, 6, 1, 1, 0xF09050, 16, 6, 1, 2, 0xF0B050, 17, 6, 1, 1, 0xF0B030, 4, 7, 1, 2, 0xF09050, 5, 7, 1, 1, 0xF0B050, 6, 7, 1, 1, 0xF09030, 8, 7, 1, 1, 0xF0D070, 9, 7, 1, 1, 0xF0B050, 10, 7, 1, 1, 0xF09030, 11, 7, 1, 1, 0xF0B070, 12, 7, 1, 1, 0xF0B050, 13, 7, 1, 1, 0xF09030, 14, 7, 2, 1, 0xF0B050, 17, 7, 1, 2, 0xF0B050, 2, 8, 1, 1, 0xF0B050, 3, 8, 1, 1, 0xF09030, 5, 8, 1, 1, 0xF09050, 6, 8, 1, 1, 0xF0B050, 7, 8, 1, 1, 0xF09050, 8, 8, 1, 1, 0xF09030, 9, 8, 2, 1, 0xF09050, 11, 8, 1, 1, 0xF09030, 12, 8, 5, 1, 0xF09050, 1, 9, 1, 1, 0xB0B050, 2, 9, 1, 1, 0x90D050, 3, 9, 1, 1, 0x90B050, 4, 9, 12, 1, 0x90D050, 16, 9, 1, 1, 0x90B050, 17, 9, 1, 1, 0x90D050, 18, 9, 1, 1, 0xB0B050, 2, 10, 15, 1, 0x70D050, 17, 10, 1, 1, 0x70F050, 2, 11, 1, 1, 0x707030, 3, 11, 14, 1, 0x705030, 17, 11, 1, 1, 0x707030, 2, 12, 16, 1, 0x703030, 2, 13, 1, 1, 0x703030, 3, 13, 14, 1, 0x705030, 17, 13, 1, 1, 0x703030, 2, 14, 2, 1, 0xF0B050, 4, 14, 1, 1, 0xF09030, 5, 14, 10, 1, 0xF09050, 15, 14, 1, 1, 0xF09030, 16, 14, 2, 1, 0xF0B050, 1, 15, 1, 1, 0xF09050, 2, 15, 1, 2, 0xF0B050, 3, 15, 1, 1, 0xF09050, 4, 15, 12, 2, 0xF0B050, 16, 15, 1, 1, 0xF09050, 17, 15, 2, 1, 0xF0B050, 3, 16, 1, 1, 0xF0B050, 16, 16, 2, 1, 0xF0B050 } },
        { "drink", new int[] { 9, 2, 2, 16, 0x50B0F0, 8, 4, 1, 7, 0x50B0F0, 11, 4, 1, 14, 0x50B0F0, 7, 6, 1, 4, 0x50B0F0, 12, 6, 1, 12, 0x50B0F0, 6, 8, 1, 4, 0x50B0F0, 13, 8, 1, 9, 0x50B0F0, 5, 10, 1, 6, 0x50B0F0, 7, 10, 1, 1, 0x30B0F0, 14, 10, 1, 6, 0x50B0F0, 7, 11, 1, 1, 0x90D0F0, 8, 11, 1, 1, 0x70B0F0, 15, 11, 1, 4, 0x50B0F0, 4, 12, 1, 3, 0x50B0F0, 6, 12, 1, 2, 0x70B0F0, 7, 12, 1, 2, 0xD0F0F0, 8, 12, 1, 2, 0xB0F0F0, 6, 14, 1, 3, 0x50B0F0, 7, 14, 1, 1, 0xB0D0F0, 8, 14, 1, 1, 0x90D0F0, 7, 15, 2, 3, 0x50B0F0 } },
        { "energy", new int[] { 11, 2, 1, 2, 0xF0D030, 10, 3, 1, 1, 0xF0D030, 9, 4, 1, 1, 0xF0D030, 10, 4, 1, 1, 0xF0F050, 9, 5, 1, 2, 0xF0F030, 10, 5, 1, 8, 0xF0D030, 8, 6, 1, 1, 0xF0D030, 7, 7, 1, 1, 0xF0D030, 8, 7, 1, 1, 0xF0F050, 9, 7, 1, 6, 0xF0D030, 7, 8, 1, 1, 0xF0F050, 8, 8, 1, 2, 0xF0D030, 11, 8, 2, 2, 0xF0D030, 13, 8, 1, 1, 0xF0F030, 14, 8, 1, 1, 0xF0D030, 6, 9, 2, 1, 0xF0D030, 13, 9, 1, 2, 0xF0D030, 5, 10, 1, 1, 0xF0D030, 6, 10, 1, 1, 0xF0F050, 7, 10, 1, 1, 0xF0F030, 8, 10, 1, 1, 0xF0F050, 11, 10, 1, 1, 0xF0D030, 12, 10, 1, 1, 0xF0F050, 11, 11, 1, 1, 0xF0F030, 12, 11, 1, 1, 0xF0D030, 11, 12, 1, 1, 0xF0D030, 9, 13, 2, 1, 0xF0F030, 8, 14, 1, 4, 0xF0D030, 9, 14, 1, 1, 0xF0F050, 10, 14, 1, 1, 0xF0D030, 9, 15, 1, 1, 0xF0D030 } },
        { "stock", new int[] { 2, 5, 1, 5, 0x907030, 3, 5, 1, 1, 0xB07030, 4, 5, 12, 2, 0x907030, 16, 5, 1, 1, 0xB07030, 17, 5, 1, 11, 0x907030, 3, 6, 1, 1, 0x907030, 16, 6, 1, 1, 0x907030, 3, 7, 2, 1, 0xB07050, 5, 7, 1, 10, 0x907030, 6, 7, 1, 1, 0xB07030, 7, 7, 2, 1, 0xB07050, 9, 7, 2, 1, 0xB07030, 11, 7, 2, 1, 0xB07050, 13, 7, 1, 1, 0xB07030, 14, 7, 1, 10, 0x907030, 15, 7, 2, 1, 0xB07050, 3, 8, 2, 2, 0xB09050, 6, 8, 1, 2, 0xB07050, 7, 8, 2, 2, 0xB09050, 9, 8, 1, 2, 0xB07050, 10, 8, 1, 9, 0x907030, 11, 8, 2, 2, 0xB09050, 13, 8, 1, 2, 0xB07050, 15, 8, 2, 2, 0xB09050, 2, 10, 1, 2, 0x905030, 3, 10, 1, 1, 0xB07030, 4, 10, 1, 1, 0x907030, 6, 10, 4, 1, 0x907030, 11, 10, 3, 1, 0x907030, 15, 10, 1, 1, 0x907030, 16, 10, 1, 1, 0xB07030, 3, 11, 2, 1, 0xB07050, 6, 11, 1, 1, 0x907030, 7, 11, 2, 1, 0xB07050, 9, 11, 1, 1, 0x907030, 11, 11, 2, 1, 0xB07050, 13, 11, 1, 1, 0x907030, 15, 11, 2, 1, 0xB07050, 2, 12, 1, 4, 0x907030, 3, 12, 2, 4, 0xB09050, 6, 12, 1, 4, 0xB07050, 7, 12, 2, 4, 0xB09050, 9, 12, 1, 1, 0xB07050, 11, 12, 2, 4, 0xB09050, 13, 12, 1, 4, 0xB07050, 15, 12, 2, 4, 0xB09050, 9, 13, 1, 2, 0x907030, 9, 15, 1, 1, 0xB07050, 2, 16, 1, 1, 0x905030, 3, 16, 1, 1, 0xB07050, 4, 16, 1, 1, 0xB07030, 6, 16, 1, 1, 0x907030, 7, 16, 1, 1, 0xB07030, 8, 16, 1, 1, 0xB07050, 9, 16, 1, 1, 0x907030, 11, 16, 1, 1, 0xB07050, 12, 16, 1, 1, 0xB07030, 13, 16, 1, 1, 0x907030, 15, 16, 1, 1, 0xB07030, 16, 16, 1, 1, 0xB07050, 17, 16, 1, 1, 0x905030 } },
        { "money", new int[] { 7, 1, 2, 1, 0xB09010, 9, 1, 2, 1, 0xD09010, 11, 1, 2, 1, 0xB09010, 5, 2, 1, 1, 0xB09010, 6, 2, 1, 1, 0xD0B010, 7, 2, 6, 2, 0xF0D030, 13, 2, 1, 1, 0xD0B010, 14, 2, 1, 1, 0xB09010, 4, 3, 1, 1, 0xD09010, 5, 3, 2, 1, 0xF0D030, 13, 3, 2, 1, 0xF0D030, 15, 3, 1, 1, 0xD09010, 3, 4, 1, 1, 0xB09010, 4, 4, 1, 1, 0xF0D030, 5, 4, 1, 1, 0xF0D050, 6, 4, 1, 2, 0xF0D030, 7, 4, 1, 1, 0xF0B030, 8, 4, 4, 1, 0xF0D030, 12, 4, 1, 1, 0xF0B030, 13, 4, 1, 7, 0xF0D030, 14, 4, 1, 1, 0xF0D050, 15, 4, 1, 1, 0xF0D030, 16, 4, 1, 1, 0xD09010, 2, 5, 1, 1, 0xB09010, 3, 5, 1, 10, 0xF0D030, 4, 5, 1, 1, 0xF0D050, 5, 5, 1, 1, 0xF0B030, 7, 5, 1, 2, 0xF0D030, 8, 5, 1, 1, 0xF0F050, 9, 5, 2, 1, 0xD0B030, 11, 5, 1, 1, 0xF0F050, 12, 5, 1, 1, 0xF0D050, 14, 5, 1, 1, 0xF0B030, 15, 5, 1, 1, 0xF0D050, 16, 5, 1, 10, 0xF0D030, 17, 5, 1, 1, 0xB09010, 2, 6, 1, 1, 0xD0B010, 4, 6, 2, 1, 0xF0D030, 6, 6, 1, 1, 0xF0D050, 8, 6, 1, 1, 0xD0B030, 9, 6, 2, 1, 0xB07010, 11, 6, 1, 1, 0xD09030, 12, 6, 1, 1, 0xD0B030, 14, 6, 2, 1, 0xF0D030, 17, 6, 1, 1, 0xD0B010, 1, 7, 1, 2, 0xB09010, 2, 7, 1, 6, 0xF0D030, 4, 7, 1, 1, 0xF0B030, 5, 7, 2, 1, 0xF0D030, 7, 7, 6, 1, 0x907010, 14, 7, 1, 4, 0xF0D030, 15, 7, 1, 1, 0xF0B030, 17, 7, 1, 6, 0xF0D030, 18, 7, 1, 2, 0xB09010, 4, 8, 1, 4, 0xF0D030, 5, 8, 1, 1, 0xF0F050, 6, 8, 1, 2, 0xD0B030, 7, 8, 1, 1, 0x905010, 8, 8, 2, 1, 0xD0B030, 10, 8, 1, 1, 0xD09030, 11, 8, 1, 1, 0xF0F050, 12, 8, 1, 1, 0xF0B030, 15, 8, 1, 4, 0xF0D030, 1, 9, 1, 1, 0xD09010, 5, 9, 1, 1, 0xF0D050, 7, 9, 1, 1, 0x907010, 8, 9, 2, 1, 0xB09010, 10, 9, 1, 1, 0xB09030, 11, 9, 2, 1, 0xF0D030, 18, 9, 1, 2, 0xD09010, 1, 10, 1, 3, 0xB09010, 5, 10, 2, 3, 0xF0D030, 7, 10, 1, 1, 0xB09030, 8, 10, 4, 1, 0x907010, 12, 10, 1, 1, 0xB09010, 7, 11, 2, 1, 0xF0D030, 9, 11, 1, 1, 0xB09030, 10, 11, 2, 1, 0xB09010, 12, 11, 1, 2, 0x905010, 13, 11, 1, 1, 0xD0B030, 14, 11, 1, 2, 0xF0F050, 18, 11, 1, 2, 0xB09010, 4, 12, 1, 2, 0xF0B030, 7, 12, 1, 1, 0xF0D030, 8, 12, 1, 1, 0xF0F050, 9, 12, 1, 1, 0xD09030, 10, 12, 2, 1, 0xD0B030, 13, 12, 1, 1, 0xB09030, 15, 12, 1, 2, 0xF0B030, 2, 13, 1, 1, 0xD0B010, 5, 13, 1, 1, 0xF0D030, 6, 13, 1, 1, 0xD0B030, 7, 13, 1, 1, 0x907010, 8, 13, 1, 1, 0xB09010, 9, 13, 1, 2, 0xB07010, 10, 13, 3, 1, 0x907010, 13, 13, 2, 1, 0xF0D030, 17, 13, 1, 1, 0xD0B010, 2, 14, 1, 1, 0xB09010, 4, 14, 1, 1, 0xF0D050, 5, 14, 2, 1, 0xF0B030, 7, 14, 1, 1, 0xD0B030, 8, 14, 1, 1, 0xB09030, 10, 14, 1, 1, 0xB07010, 11, 14, 1, 1, 0xB09030, 12, 14, 2, 1, 0xF0D030, 14, 14, 1, 1, 0xF0B030, 15, 14, 1, 1, 0xF0D050, 17, 14, 1, 1, 0xB09010, 3, 15, 1, 1, 0xD09010, 4, 15, 1, 1, 0xF0D030, 5, 15, 1, 1, 0xF0D050, 6, 15, 3, 2, 0xF0D030, 9, 15, 2, 1, 0xB09030, 11, 15, 2, 3, 0xF0D030, 13, 15, 1, 1, 0xF0B030, 14, 15, 1, 1, 0xF0D050, 15, 15, 1, 1, 0xF0D030, 16, 15, 1, 1, 0xD09010, 4, 16, 1, 1, 0xD09010, 5, 16, 1, 1, 0xF0D030, 9, 16, 2, 1, 0xD0B030, 13, 16, 1, 1, 0xF0D050, 14, 16, 1, 1, 0xF0D030, 15, 16, 1, 1, 0xD09010, 5, 17, 1, 1, 0xB09010, 6, 17, 1, 1, 0xD0B010, 7, 17, 4, 1, 0xF0D030, 13, 17, 1, 1, 0xD0B010, 14, 17, 1, 1, 0xB09010, 7, 18, 1, 1, 0xB09010, 8, 18, 4, 1, 0xD09010, 12, 18, 1, 1, 0xB09010 } },
    };

    // bieu tuong: uu tien anh PNG sieu net (GtaVietLifeIcons canh script) qua CustomSprite (nap bang reflection de khong loi bien dich
    // neu ban SHVDN khong co); thieu thi ve khoi mau 20x20 (moi nhom 5 so = x, y, rong, cao, mau RGB)
    readonly Dictionary<string, object> spriteIcons = new Dictionary<string, object>();
    System.Reflection.ConstructorInfo spriteCtor; System.Reflection.PropertyInfo spPos, spSize; System.Reflection.MethodInfo spDraw;
    bool spriteProbed, spriteOk;

    void ProbeSprite()
    {
        spriteProbed = true;
        try
        {
            Type t = null;
            foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("GTA.UI.CustomSprite", false);
                if (t != null) break;
            }
            if (t == null) { Log("khong co CustomSprite, dung icon khoi mau"); return; }
            foreach (System.Reflection.ConstructorInfo ci in t.GetConstructors())
            {
                System.Reflection.ParameterInfo[] ps = ci.GetParameters();
                if (ps.Length >= 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(SizeF) && ps[2].ParameterType == typeof(PointF))
                { if (spriteCtor == null || ps.Length < spriteCtor.GetParameters().Length) spriteCtor = ci; }
            }
            spPos = t.GetProperty("Position"); spSize = t.GetProperty("Size"); spDraw = t.GetMethod("Draw", Type.EmptyTypes);
            spriteOk = spriteCtor != null && spPos != null && spSize != null && spDraw != null;
            Log("CustomSprite: " + (spriteOk ? "dung duoc" : "thieu thanh phan"));
        }
        catch (Exception ex) { Log("probe sprite loi: " + ex.Message); }
    }

    object GetSprite(string name, float x, float y, float size)
    {
        object sp;
        if (spriteIcons.TryGetValue(name, out sp)) return sp;
        sp = null;
        try
        {
            string f = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Filename), "GtaVietLifeIcons"), name + ".png");
            if (System.IO.File.Exists(f))
            {
                System.Reflection.ParameterInfo[] ps = spriteCtor.GetParameters();
                object[] args = new object[ps.Length];
                args[0] = f; args[1] = new SizeF(size, size); args[2] = new PointF(x, y);
                for (int i = 3; i < ps.Length; i++)
                    args[i] = ps[i].ParameterType == typeof(Color) ? (object)Color.White : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                sp = spriteCtor.Invoke(args);
            }
        }
        catch (Exception ex) { Log("nap icon " + name + " loi: " + ex.Message); sp = null; }
        spriteIcons[name] = sp;
        return sp;
    }

    void DrawIcon(string name, float x, float y, float size)
    {
        if (!spriteProbed) ProbeSprite();
        if (spriteOk)
        {
            try
            {
                object sp = GetSprite(name, x, y, size);
                if (sp != null)
                {
                    spSize.SetValue(sp, new SizeF(size, size), null);
                    spPos.SetValue(sp, new PointF(x, y), null);
                    spDraw.Invoke(sp, null);
                    return;
                }
            }
            catch (Exception ex) { Log("ve icon loi: " + ex.Message); spriteOk = false; }
        }
        int[] d;
        if (!IconData.TryGetValue(name, out d)) return;
        float c = size / 20f;
        for (int k = 0; k + 4 < d.Length; k += 5)
        {
            int v = d[k + 4];
            new GTA.UI.ContainerElement(new PointF(x + d[k] * c, y + d[k + 1] * c), new SizeF(d[k + 2] * c + 0.4f, d[k + 3] * c + 0.4f), Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255)).Draw();
        }
    }

    void DrawStat(string icon, string fallback, float value, float x, float y, Color fill)
    {
        DrawIcon(icon, x, y - 3f, 22f);
        float w = 150f, h = 16f, bx = x + 28f;
        new GTA.UI.ContainerElement(new PointF(bx, y), new SizeF(w, h), Color.FromArgb(150, 0, 0, 0)).Draw();
        Color c = value < 15f ? Color.FromArgb(230, 230, 60, 60) : fill;
        float fw = (w - 4f) * Math.Max(0f, Math.Min(100f, value)) / 100f;
        if (fw > 0f) new GTA.UI.ContainerElement(new PointF(bx + 2f, y + 2f), new SizeF(fw, h - 4f), c).Draw();
        DrawText((int)value + "%", bx + w + 6f, y - 3f, 0.34f, value < 15f ? Color.FromArgb(255, 255, 110, 110) : Color.White);
    }

    // bang kho tat ca quan: bieu tuong + thanh ton kho (do <=10, vang <=30, xanh con lai) + so luong
    void DrawStockPanel(float x, float y)
    {
        if (shops.Count == 0) return;
        SyncOut();
        float rowH = 20f;
        float w = 250f, h = 26f + shops.Count * rowH;
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, h), Color.FromArgb(140, 0, 0, 0)).Draw();
        DrawIcon("stock", x + 4f, y + 2f, 20f);
        DrawText("KHO CAC QUAN  (" + stockKey + ": an)", x + 30f, y + 1f, 0.32f, Color.FromArgb(255, 255, 210, 80));
        for (int i = 0; i < shops.Count; i++)
        {
            Shop sh = shops[i];
            float ry = y + 26f + i * rowH;
            bool act = i == activeShop;
            DrawText("#" + (i + 1), x + 6f, ry - 2f, 0.32f, act ? Color.FromArgb(255, 255, 210, 80) : Color.White);
            float bx = x + 34f, bw = 130f;
            new GTA.UI.ContainerElement(new PointF(bx, ry), new SizeF(bw, 12f), Color.FromArgb(150, 0, 0, 0)).Draw();
            Color c = sh.Stock <= 10 ? Color.FromArgb(235, 235, 70, 70) : sh.Stock <= 30 ? Color.FromArgb(235, 235, 200, 60) : Color.FromArgb(235, 90, 200, 90);
            float fw = (bw - 4f) * Math.Max(0f, Math.Min(1f, sh.Stock / 100f));
            if (fw > 0f) new GTA.UI.ContainerElement(new PointF(bx + 2f, ry + 2f), new SizeF(fw, 8f), c).Draw();
            DrawText(sh.Stock <= 0 ? "HET" : sh.Stock.ToString(), bx + bw + 6f, ry - 3f, 0.32f, sh.Stock <= 10 ? Color.FromArgb(255, 255, 110, 110) : Color.White);
            DrawText(sh.Staff <= 0 ? "!nv" : "", bx + bw + 46f, ry - 3f, 0.30f, Color.FromArgb(255, 255, 170, 80));
        }
    }

    void DrawHud()
    {
        float x = 18f, y = 18f;
        DrawIcon("money", x, y - 2f, 24f);
        DrawText("$" + Game.Player.Money, x + 28f, y - 4f, 0.5f, Color.FromArgb(255, 120, 230, 120));
        y += 28f;
        DrawStat("food", "An", hunger, x, y, Color.FromArgb(220, 235, 150, 40));
        DrawStat("drink", "Uong", thirst, x, y + 22f, Color.FromArgb(220, 60, 160, 235));
        DrawStat("energy", "Nang luong", energy, x, y + 44f, Color.FromArgb(220, 235, 210, 60));
        DrawStat("hygiene", "Ve sinh", hygiene, x, y + 66f, Color.FromArgb(220, 120, 220, 200));
        float below = y + 88f;
        if (showStock && shops.Count > 0) { DrawStockPanel(x, below); below += 30f + shops.Count * 20f; }
        y = below - 66f;   // phan chu phia duoi tinh theo vi tri moi (below da tinh ca thanh ve sinh)
        string extra = "";
        if (jobGiaoHang) extra += "Giao hang";
        if (delivering)
        {
            int left = (deadlineMs - Game.GameTime) / 1000;
            extra += " | con " + (int)Game.Player.Character.Position.DistanceTo2D(dest) + "m, " + (left > 0 ? left + "s" : "TRE");
        }
        if (debt > 0) extra += (extra.Length > 0 ? " | " : "") + "No $" + debt;
        if (extra.Length > 0) DrawText(extra, x, y + 66f, 0.32f, Color.White);
    }

    // tach nhan thanh dong chinh + dong phu: "Ten  (chi tiet)" -> Ten / chi tiet
    static void SplitLabel(string label, out string main, out string detail)
    {
        int i = label.IndexOf("  (");
        if (i > 0 && label.EndsWith(")"))
        {
            main = label.Substring(0, i);
            detail = label.Substring(i + 3, label.Length - i - 4);
        }
        else { main = label; detail = ""; }
    }

    void DrawMenu()
    {
        if (menuSel < 0 || menuSel >= menuActions.Count || menuActions[menuSel] == null) menuSel = NextActionable(-1, 1);
        float x = 40f, y = 150f, w = 600f, pad = 14f;
        int total = menuLabels.Count;
        const int maxRows = 11;
        int first = 0;
        if (total > maxRows) first = Math.Max(0, Math.Min(total - maxRows, menuSel - maxRows / 2));
        int last = Math.Min(total, first + maxRows);
        float h = 52f;
        for (int i = first; i < last; i++) { string m0, d0; SplitLabel(menuLabels[i], out m0, out d0); h += d0.Length > 0 ? 46f : 30f; }
        h += 30f;
        Color gold = Color.FromArgb(255, 255, 205, 90);
        // nen + vien vang + thanh tieu de
        new GTA.UI.ContainerElement(new PointF(x - 2f, y - 2f), new SizeF(w + 4f, h + 4f), Color.FromArgb(230, 200, 150, 40)).Draw();
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, h), Color.FromArgb(235, 16, 18, 24)).Draw();
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, 40f), Color.FromArgb(240, 36, 40, 52)).Draw();
        DrawText(menuTitle, x + pad, y + 5f, 0.5f, gold);
        float cy = y + 48f;
        int shownNum = 0;
        for (int i = 0; i < first; i++) if (menuActions[i] != null) shownNum++;
        for (int i = first; i < last; i++)
        {
            string m, d; SplitLabel(menuLabels[i], out m, out d);
            float rh = d.Length > 0 ? 46f : 30f;
            bool act = menuActions[i] != null;
            bool sel = i == menuSel;
            if (sel) new GTA.UI.ContainerElement(new PointF(x + 6f, cy - 2f), new SizeF(w - 12f, rh - 2f), Color.FromArgb(235, 215, 165, 50)).Draw();
            Color mc = sel ? Color.FromArgb(255, 20, 20, 24) : (act ? Color.White : Color.FromArgb(255, 150, 156, 170));
            Color dc = sel ? Color.FromArgb(255, 60, 48, 20) : Color.FromArgb(255, 150, 156, 170);
            string prefix = act ? (shownNum + 1 <= 9 ? (shownNum + 1) + "  " : "    ") : "    ";
            if (act) shownNum++;
            DrawText(prefix + m, x + pad, cy, 0.4f, mc);
            if (d.Length > 0) DrawText(d, x + pad + 26f, cy + 22f, 0.31f, dc);
            cy += rh;
        }
        if (total > maxRows) DrawText((first > 0 ? "^ " : "  ") + "(" + (menuSel + 1) + "/" + total + ")" + (last < total ? " v" : ""), x + w - 110f, y + 8f, 0.32f, Color.FromArgb(255, 180, 184, 196));
        DrawText("dau , va dau . : chon  -  Enter hoac phim so: chay  -  Backspace: quay lai  -  " + MK + ": dong", x + pad, y + h - 26f, 0.29f, Color.FromArgb(255, 150, 156, 170));
    }

    // ------------------------------------------------------------ cầu nối AI
    void PostState()
    {
        NameValueCollection c = new NameValueCollection();
        c["hour"] = World.CurrentTimeOfDay.Hours.ToString();
        c["hunger"] = ((int)hunger).ToString();
        c["thirst"] = ((int)thirst).ToString();
        c["energy"] = ((int)energy).ToString();
        c["hygiene"] = ((int)hygiene).ToString();
        c["rep"] = rep.ToString();
        c["debt"] = debt.ToString();
        c["job"] = jobGiaoHang ? "shipper giao hàng" : "";
        c["business"] = hasBiz ? "quán nhỏ có " + bizStaff + " nhân viên" : "";
        Send("/state", c);
    }

    void PostEvent(string text)
    {
        NameValueCollection c = new NameValueCollection();
        c["text"] = text;
        Send("/event", c);
    }

    void Send(string path, NameValueCollection data)
    {
        string url = server + path;
        Task.Run(delegate
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Encoding = Encoding.UTF8;
                    wc.UploadValues(url, "POST", data);
                }
            }
            catch { }
        });
    }

    // ------------------------------------------------------------ lưu / nạp
    void Save()
    {
        try
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<string> l = new List<string>();
            l.Add("hunger=" + hunger.ToString(ci));
            l.Add("thirst=" + thirst.ToString(ci));
            l.Add("energy=" + energy.ToString(ci));
            l.Add("hygiene=" + hygiene.ToString(ci));
            l.Add("rhythm=" + (rhythmOn ? 1 : 0));
            l.Add("resnext=" + nextResId);
            foreach (Resident r in residents)
                l.Add("res=" + r.Id + "," + r.ModelHash + "," + (r.Female ? 1 : 0) + "," + r.NameIdx + "," + r.Home.X.ToString(ci) + "," + r.Home.Y.ToString(ci) + "," + r.Home.Z.ToString(ci) + "," + r.Work.X.ToString(ci) + "," + r.Work.Y.ToString(ci) + "," + r.Work.Z.ToString(ci) + "," + r.Lunch.X.ToString(ci) + "," + r.Lunch.Y.ToString(ci) + "," + r.Lunch.Z.ToString(ci));
            l.Add("rep=" + rep);
            l.Add("debt=" + debt);
            l.Add("seen=" + NowEpoch());
            foreach (int[] h in dayHist) l.Add("hist=" + string.Join(",", Array.ConvertAll(h, x => x.ToString())));
            l.Add("b2bday=" + b2bDay);
            foreach (int[] ct in contracts) l.Add("b2b=" + ct[0] + "," + ct[1]);
            foreach (int[] rv in receivables) l.Add("recv=" + rv[0] + "," + rv[1] + "," + rv[2]);
            foreach (Owned o in owned) l.Add("estate=" + o.E + "," + o.Occ + "," + o.Mode + "," + o.Level + "," + o.RentLv + "," + (o.Manager ? 1 : 0) + "," + o.Value + "," + o.Paid + "," + o.LastNet + "," + o.LastGross + "," + o.TotalNet);
            l.Add("job=" + (jobGiaoHang ? 1 : 0));
            l.Add("deliveries=" + deliveriesTotal);
            l.Add("hasHome=" + (hasHome ? 1 : 0));
            l.Add("homeIdx=" + homeIdx);
            l.Add("rent=" + rentPerDay);
            l.Add("homeEnergy=" + homeEnergy.ToString(ci));
            l.Add("staffAngle=" + staffAngle);
            l.Add("home=" + home.X.ToString(ci) + "," + home.Y.ToString(ci) + "," + home.Z.ToString(ci));
            l.Add("hasBiz=" + (hasBiz ? 1 : 0));
            l.Add("biz=" + bizPos.X.ToString(ci) + "," + bizPos.Y.ToString(ci) + "," + bizPos.Z.ToString(ci));
            l.Add("stock=" + bizStock);
            l.Add("staff=" + bizStaff);
            l.Add("cash=" + bizCash);
            l.Add("level=" + bizLevel);
            l.Add("heading=" + bizHeading.ToString(ci));
            l.Add("ground=" + bizGround.ToString(ci));
            SyncOut();
            l.Add("active=" + activeShop);
            l.Add("ownveh=" + ownVehHash);
            foreach (Shop sh in shops)
                l.Add("shop=" + sh.Pos.X.ToString(ci) + "," + sh.Pos.Y.ToString(ci) + "," + sh.Pos.Z.ToString(ci) + "," + sh.Heading.ToString(ci) + "," + sh.Ground.ToString(ci) + "," + sh.Level + "," + sh.Stock + "," + sh.Staff + "," + sh.Cash + "," + sh.SoldToday + "," + sh.Mode + "," + sh.Rent + "," + sh.DaysLeft + "," + sh.Overdue + "," + sh.Paid + "," + (sh.Indoor ? 1 : 0) + "," + sh.Type);
            File.WriteAllLines(SaveFile, l.ToArray());
            WriteDash();
        }
        catch (Exception ex) { Log("save loi: " + ex.Message); }
    }

    // Anh chup so lieu kinh doanh cho trang dashboard tren dien thoai (dashboard.py doc file nay)
    void WriteDash()
    {
        try
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder b = new StringBuilder();
            b.Append("{\"ts\":" + NowEpoch() + ",\"money\":" + Game.Player.Money + ",\"debt\":" + debt + ",\"rep\":" + rep);
            b.Append(",\"offlineFactor\":" + OfflineFactor.ToString(ci) + ",\"gameDaySec\":" + GameDaySeconds + ",\"maxOfflineDays\":" + MaxOfflineDays);
            b.Append(",\"day\":" + dayCounter + ",\"shops\":[");
            for (int i = 0; i < shops.Count; i++)
            {
                Shop sh = shops[i];
                if (i > 0) b.Append(",");
                b.Append("{\"name\":\"Quan " + (i + 1) + "\",\"type\":\"" + Types[TIdx(sh.Type)].Name + "\",\"channel\":\"" + (Types[TIdx(sh.Type)].Channel == 0 ? "GT" : "MT") + "\"");
                b.Append(",\"level\":" + sh.Level + ",\"staff\":" + sh.Staff + ",\"stock\":" + sh.Stock + ",\"cash\":" + sh.Cash + ",\"soldToday\":" + sh.SoldToday);
                b.Append(",\"mode\":\"" + ModeName[sh.Mode] + "\",\"rent\":" + sh.Rent + ",\"overdue\":" + sh.Overdue + ",\"lastProfit\":" + sh.LastProfit + ",\"lastRev\":" + sh.LastRev + ",\"pack1\":" + PackCost(sh.Type, 1, true) + ",\"pack5\":" + PackCost(sh.Type, 5, true) + ",\"pack10\":" + PackCost(sh.Type, 10, true) + ",\"upCost\":" + (sh.Level >= CapOf(sh) ? -1 : 2500 * sh.Level) + ",\"maxStaff\":" + MaxStaff + ",\"wage\":" + WagePerDay + "}");
            }
            b.Append("],\"estates\":[");
            for (int i = 0; i < owned.Count; i++)
            {
                Owned o = owned[i]; Estate es = Estates[o.E];
                if (i > 0) b.Append(",");
                b.Append("{\"n\":\"" + es.Name + "\",\"k\":\"" + EstateKind[es.Kind] + "\",\"u\":" + es.Units + ",\"o\":" + o.Occ + ",\"m\":" + o.Mode + ",\"v\":" + o.Value + ",\"net\":" + o.LastNet + ",\"lv\":" + o.Level + "}");
            }
            b.Append("],\"ship\":{\"job\":" + (jobGiaoHang ? 1 : 0) + ",\"today\":" + deliveriesToday + ",\"earned\":" + earnedToday + ",\"total\":" + deliveriesTotal + "}");
            b.Append(",\"contracts\":" + contracts.Count + ",\"receivables\":" + receivables.Count + ",\"hist\":[");
            for (int i = 0; i < dayHist.Count; i++)
            {
                if (i > 0) b.Append(",");
                b.Append("[" + string.Join(",", Array.ConvertAll(dayHist[i], x => x.ToString())) + "]");
            }
            b.Append("],\"cmds\":[");
            for (int i = 0; i < cmdResults.Count; i++)
            {
                if (i > 0) b.Append(",");
                b.Append("[\"" + cmdResults[i][0] + "\",\"" + cmdResults[i][1] + "\",\"" + cmdResults[i][2].Replace("\"", "") + "\"]");
            }
            b.Append("]}");
            File.WriteAllText("GtaVietLifeDash.json", b.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex) { Log("dash loi: " + ex.Message); }
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
                switch (k)
                {
                    case "hunger": hunger = float.Parse(v, ci); break;
                    case "thirst": thirst = float.Parse(v, ci); break;
                    case "energy": energy = float.Parse(v, ci); break;
                    case "hygiene": hygiene = float.Parse(v, ci); break;
                    case "rhythm": rhythmOn = v != "0"; break;
                    case "resnext": nextResId = int.Parse(v); break;
                    case "res":
                        {
                            string[] q = v.Split(',');
                            if (q.Length < 13) break;
                            Resident r = new Resident();
                            r.Id = int.Parse(q[0]); r.ModelHash = int.Parse(q[1]); r.Female = q[2] == "1"; r.NameIdx = int.Parse(q[3]);
                            r.Home = new Vector3(float.Parse(q[4], ci), float.Parse(q[5], ci), float.Parse(q[6], ci));
                            r.Work = new Vector3(float.Parse(q[7], ci), float.Parse(q[8], ci), float.Parse(q[9], ci));
                            r.Lunch = new Vector3(float.Parse(q[10], ci), float.Parse(q[11], ci), float.Parse(q[12], ci));
                            residents.Add(r);
                            if (r.Id >= nextResId) nextResId = r.Id + 1;
                            break;
                        }
                    case "rep": rep = int.Parse(v); break;
                    case "debt": debt = int.Parse(v); break;
                    case "seen": lastSeen = long.Parse(v); break;
                    case "hist": { string[] q = v.Split(','); if (q.Length == 7) { int[] h = new int[7]; for (int z = 0; z < 7; z++) h[z] = int.Parse(q[z]); dayHist.Add(h); } break; }
                    case "b2bday": b2bDay = int.Parse(v); break;
                    case "b2b": { string[] q = v.Split(','); int dd = int.Parse(q[0]); if (dd >= 0 && dd < Deals.Length) contracts.Add(new int[] { dd, int.Parse(q[1]) }); break; }
                    case "recv": { string[] q = v.Split(','); receivables.Add(new int[] { int.Parse(q[0]), int.Parse(q[1]), int.Parse(q[2]) }); break; }
                    case "job": jobGiaoHang = v == "1"; break;
                    case "estate":
                        {
                            string[] q = v.Split(',');
                            if (q.Length < 11) break;
                            int ee = int.Parse(q[0]);
                            if (ee < 0 || ee >= Estates.Length || OwnedIdx(ee) >= 0) break;
                            Owned o = new Owned { E = ee, Occ = Math.Max(0, Math.Min(Estates[ee].Units, int.Parse(q[1]))), Mode = int.Parse(q[2]) == 1 ? 1 : 0, Level = Math.Max(1, Math.Min(3, int.Parse(q[3]))), RentLv = Math.Max(0, Math.Min(2, int.Parse(q[4]))), Manager = q[5] == "1", Value = int.Parse(q[6]), Paid = int.Parse(q[7]), LastNet = int.Parse(q[8]), LastGross = int.Parse(q[9]), TotalNet = int.Parse(q[10]) };
                            owned.Add(o);
                            break;
                        }
                    case "deliveries": deliveriesTotal = int.Parse(v); break;
                    case "hasHome": hasHome = v == "1"; break;
                    case "homeIdx": homeIdx = int.Parse(v); if (homeIdx >= 0 && homeIdx < Homes.Length) homeName = Homes[homeIdx].Name; break;
                    case "rent": rentPerDay = int.Parse(v); break;
                    case "homeEnergy": homeEnergy = float.Parse(v, ci); break;
                    case "staffAngle": staffAngle = int.Parse(v) % 4; break;
                    case "home": home = ParseVec(v); break;
                    case "hasBiz": hasBiz = v == "1"; break;
                    case "biz": bizPos = ParseVec(v); break;
                    case "stock": bizStock = int.Parse(v); break;
                    case "staff": bizStaff = int.Parse(v); break;
                    case "cash": bizCash = int.Parse(v); break;
                    case "level": bizLevel = int.Parse(v); break;
                    case "heading": bizHeading = float.Parse(v, ci); break;
                    case "ground": bizGround = float.Parse(v, ci); break;
                    case "ownveh": ownVehHash = int.Parse(v); break;
                    case "shop":
                        {
                            string[] q = v.Split(',');
                            Shop sh = new Shop();
                            sh.Pos = new Vector3(float.Parse(q[0], ci), float.Parse(q[1], ci), float.Parse(q[2], ci));
                            sh.Heading = float.Parse(q[3], ci); sh.Ground = float.Parse(q[4], ci);
                            sh.Level = int.Parse(q[5]); sh.Stock = int.Parse(q[6]); sh.Staff = int.Parse(q[7]); sh.Cash = int.Parse(q[8]); sh.SoldToday = int.Parse(q[9]);
                            sh.Mode = Math.Max(0, Math.Min(3, int.Parse(q[10]))); sh.Rent = int.Parse(q[11]); sh.DaysLeft = int.Parse(q[12]); sh.Overdue = int.Parse(q[13]); sh.Paid = int.Parse(q[14]); sh.Indoor = q.Length > 15 && q[15] == "1"; sh.Type = q.Length > 16 ? Math.Max(0, Math.Min(Types.Length - 1, int.Parse(q[16]))) : 0;
                            shops.Add(sh);
                            break;
                        }
                }
            }
        }
        catch (Exception ex) { Log("load loi: " + ex.Message); }
    }

    static Vector3 ParseVec(string v)
    {
        string[] p = v.Split(',');
        CultureInfo ci = CultureInfo.InvariantCulture;
        return new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci));
    }

    // ------------------------------------------------------------ tiện ích
    static float Clamp(float v) { return v < 0f ? 0f : v > 100f ? 100f : v; }

    string SafeStreet(Vector3 pos)
    {
        try { return World.GetStreetName(pos); } catch { return "đâu đó trong thành phố"; }
    }

    void Notify(string msg) { GTA.UI.Notification.Show(msg); }

    static void Log(string msg)
    {
        try { File.AppendAllText("GtaVietLifeWorld.log", DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine); }
        catch { }
    }

    static string Short(Exception ex)
    {
        string m = ex.Message ?? ex.GetType().Name;
        return m.Length > 90 ? m.Substring(0, 90) : m;
    }
}
