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

    // cư dân (NPC có lịch)
    class Resident
    {
        public Ped P;
        public Vector3 Home;
        public Vector3 Work;
        public int Phase = -1;
        public int LastCmd;
    }
    readonly List<Resident> residents = new List<Resident>();

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

        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
        Notify("~g~GTA Viet Life: Doi song~s~ da bat. Bam ~y~" + MK + "~s~ de mo menu.");
        foreach (Shop sh in shops) MakeShopBlip(sh);
        if (hasBiz) { BuildBizProps(); }
        for (int i = 0; i < Math.Min(bizStaff, MaxVisibleStaff); i++) SpawnStaff();
    }

    // ------------------------------------------------------------ vòng lặp
    void OnTick(object sender, EventArgs e)
    {
        Ped me = Game.Player.Character;
        if (me == null || !me.Exists()) return;
        int now = Game.GameTime;

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
                float rate = ModeTraffic[rs.Mode] * ZoneMult(rs.Pos) * (rs.Indoor ? 1.15f : 1f) * (1f + (rs.Level - 1) * 0.2f) * (1f + rs.Staff * 0.25f);
                rs.NextSale = now + (int)(rnd.Next(42000, 78000) / Math.Max(0.2f, rate));
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
                int rq = Math.Min(rs.Stock, rnd.Next(1, 4));
                rs.Stock -= rq;
                rs.SoldToday += rq;
                int rpay = rq * (SalePrice + (rs.Level - 1) * 6);
                Game.Player.Money = Game.Player.Money + rpay;
                Notify("~g~Quan #" + (ri + 1) + ": +$" + rpay + "~s~ ban " + rq + " phan (kho con " + rs.Stock + ", " + ZoneName(rs.Pos) + ").");
                if (rs.Stock <= 5 && rs.Stock > 0) Notify("~y~Quan #" + (ri + 1) + " sap het hang~s~ (con " + rs.Stock + " phan).");
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

        if (sleepAt > 0 && now > sleepAt) { sleepAt = 0; FinishSleep(); }
        if (sleepAt > 0) return;
        UpdateClock();
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
        if (destBlip != null && destBlip.Exists()) destBlip.Delete();
        foreach (Resident r in residents) if (r.P != null && r.P.Exists()) r.P.IsPersistent = false;
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
        hunger = Clamp(hunger - 0.06f * delta);
        thirst = Clamp(thirst - 0.09f * delta);
        energy = Clamp(energy - 0.05f * delta);
        if (dayCounter > lastDayTick) { lastDayTick = dayCounter; DailyTick(); }
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
    }

    void DailyTick()
    {
        deliveriesToday = 0;
        earnedToday = 0;
        string report = "~b~Qua ngay moi.~s~";

        if (hasHome)
        {
            if (Game.Player.Money >= rentPerDay) { Game.Player.Money = Game.Player.Money - rentPerDay; report += " Tien nha -$" + rentPerDay + "."; }
            else { debt += rentPerDay; report += " ~r~Thieu tien nha, ghi no $" + rentPerDay + ".~s~"; }
        }
        if (debt > 0)
        {
            debt += debt / 20; // lãi 5% mỗi ngày
            int pay = Math.Min(Game.Player.Money, Math.Max(0, Game.Player.Money * 3 / 10));
            pay = Math.Min(pay, debt);
            if (pay > 0) { Game.Player.Money = Game.Player.Money - pay; debt -= pay; report += " Chu no lay $" + pay + "."; }
            if (debt > 3000) { rep -= 2; report += " ~r~No qua nhieu, mat uy tin!~s~"; PostEvent("Có gã chơi nổi nhưng nợ nần chồng chất, người ta bắt đầu ngán."); }
        }
        if (shops.Count > 0)
        {
            SyncOut();
            for (int i = shops.Count - 1; i >= 0; i--)
            {
                Shop sh = shops[i];
                string nm = "Quan " + (i + 1);
                int target = sh.Staff <= 0 ? 0 : (int)((15 + (sh.Level - 1) * 8 + sh.Staff * 12 + rnd.Next(0, 11) + Math.Max(0, rep / 5)) * ModeTraffic[sh.Mode] * ZoneMult(sh.Pos) * (sh.Indoor ? 1.15f : 1f));
                int remain = Math.Max(0, target - sh.SoldToday);   // khach thay tan mat da ban theo thoi gian thuc
                int sold = Math.Min(sh.Stock, remain);
                sh.Stock -= sold;
                int revenue = sold * (SalePrice + (sh.Level - 1) * 6);
                Game.Player.Money = Game.Player.Money + revenue;
                int wages = sh.Staff * WagePerDay;
                if (Game.Player.Money >= wages) Game.Player.Money = Game.Player.Money - wages;
                else { debt += wages; report += " ~r~Thieu tien tra luong " + nm + ", ghi no.~s~"; }
                if (sold + sh.SoldToday >= 10) rep += 1;
                report += " " + nm + ": ban " + (sold + sh.SoldToday) + " phan, luong -$" + wages;
                sh.SoldToday = 0;
                if (sh.Rent > 0)
                {
                    if (Game.Player.Money >= sh.Rent) { Game.Player.Money = Game.Player.Money - sh.Rent; sh.Overdue = 0; report += ", " + FeeWord(sh.Mode) + " -$" + sh.Rent; }
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
                report += ".";
                if (sh.Stock == 0) report += " ~y~" + nm + " het hang!~s~";
            }
            if (activeShop >= 0) SyncIn(activeShop);
        }
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
            if (Game.Player.Money >= fine) { Game.Player.Money = Game.Player.Money - fine; Notify("~r~Bi phat $" + fine + "~s~ vi vi pham (" + prevWanted + " sao)."); }
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
        Game.Player.Money = Game.Player.Money + pay;
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
    // ====== nhieu quan + cac hinh thuc thue mat bang ======
    class Shop
    {
        public Vector3 Pos; public float Heading, Ground;
        public int Level = 1, Stock, Staff, Cash, SoldToday;
        public int Mode, Rent, DaysLeft, Overdue, Paid;
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
    }

    void SyncIn(int i)
    {
        Shop sh = shops[i];
        activeShop = i; hasBiz = true;
        bizPos = sh.Pos; bizHeading = sh.Heading; bizGround = sh.Ground; bizIndoor = sh.Indoor;
        bizLevel = sh.Level; bizStock = sh.Stock; bizStaff = sh.Staff; bizCash = sh.Cash; soldToday = sh.SoldToday;
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
        int setup = (int)(ModeSetup[mode] * (mode == 3 ? zm * (indoor ? 1.5f : 1f) : 1f));
        if (Game.Player.Money < setup) { Refuse("Khong du tien: can $" + setup + ", Sep co $" + Game.Player.Money + "."); return; }
        if (!Spend(setup)) return;
        SyncOut();
        if (hasBiz) { ClearStaff(); ClearBizProps(); }
        Shop sh = new Shop();
        sh.Mode = mode; sh.Paid = setup; sh.Rent = (int)(ModeRent[mode] * zm * (indoor ? 1.3f : 1f)); sh.DaysLeft = mode == 2 ? 30 : 0;
        shops.Add(sh);
        activeShop = shops.Count - 1;
        hasBiz = true;
        bizIndoor = indoor;
        if (indoor) PlaceBizIndoor(); else PlaceBizOnFlatGround();
        bizLevel = 1; bizStock = 30; bizStaff = 0; bizCash = 0; soldToday = 0;
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
        new object[] { "Yellow Jack Inn (nha co noi that)", 1991f, 3051f, true } };
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
            Item(nm + (taken ? " [da co quan cua Sep]" : "") + (Listings[i].Length > 3 ? " [vao ben trong roi mo quan]" : "") + "  (" + ZoneName(lp) + ", khach x" + zm.ToString("0.0", CultureInfo.InvariantCulture) + ", thue $" + (int)(ModeRent[1] * zm) + "/ngay, mua dut $" + (int)(ModeSetup[3] * zm) + ", " + (int)pp.DistanceTo2D(lp) + " m)", delegate
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
        for (int m = 0; m < 4; m++)
        {
            int mode = m;
            int setup = (int)(ModeSetup[m] * (m == 3 ? zm : 1f));
            int rent = (int)(ModeRent[m] * zm);
            string cond = m == 0 ? "phi via he $" + rent + "/ngay, toi da cap 2"
                : m == 1 ? "thue $" + rent + "/ngay, huy luc nao cung duoc, toi da cap 3"
                : m == 2 ? "dat coc, thue $" + rent + "/ngay, huy giua chung mat coc, toi da cap 4"
                : "tra mot lan, khong tien thue, ban lai 70%, toi da cap 4";
            Item(ModeName[m] + "  -$" + setup + "  (" + cond + ")", delegate { OpenShop(mode); if (menuNote.Length > 0) ShowOpenMenu(); else ShowBiz(); });
        }
        Item("Quay lai", delegate { ShowBiz(); });
    }

    const float RemoteFee = 1.10f;   // phi giao hang khi dat tu xa

    string StockTag(Shop sh)
    {
        return sh.Stock <= 0 ? "HET HANG" : sh.Stock <= 10 ? "SAP HET (" + sh.Stock + ")" : sh.Stock + " phan";
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
            if (sh.Stock <= 10) { low++; lowCost += (int)(StockPrice * RemoteFee); }
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
        int one = (int)(StockPrice * RemoteFee);
        Item("Dat hang +" + StockPack + " phan  (-$" + one + ")", delegate { RemoteBuy(idx, 1, false); ShowStockShop(idx); });
        Item("Dat hang +" + (StockPack * 5) + " phan  (-$" + one * 5 + ")", delegate { RemoteBuy(idx, 5, false); ShowStockShop(idx); });
        Item("Dat hang +" + (StockPack * 10) + " phan  (-$" + one * 10 + ")", delegate { RemoteBuy(idx, 10, false); ShowStockShop(idx); });
        Item("Dat diem den toi quan nay", delegate { Function.Call(Hash.SET_NEW_WAYPOINT, shops[idx].Pos.X, shops[idx].Pos.Y); Notify("Da dat diem den toi quan #" + (idx + 1) + "."); ShowStockShop(idx); });
        Item("Quay lai bang kho", delegate { ShowStockBoard(); });
    }

    // dat hang tu xa: tra tien + phi giao hang, hang cong thang vao kho quan do
    bool RemoteBuy(int idx, int packs, bool quiet)
    {
        if (idx < 0 || idx >= shops.Count) return false;
        int cost = (int)(StockPrice * RemoteFee) * packs;
        if (Game.Player.Money < cost) { if (!quiet) Notify("Khong du tien ($" + cost + ")."); return false; }
        Game.Player.Money = Game.Player.Money - cost;
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
            Game.Player.Money = Game.Player.Money + refund + bizCash;
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

    int Price() { return SalePrice + (bizLevel - 1) * 6; }

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
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, p, "WORLD_HUMAN_STAND_IMPATIENT", 0, true);
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
            nextCustomer = now + Math.Max(7000, gap);
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
                else if (bizStock <= 5) Notify("~y~Kho sap het~s~ (con " + bizStock + " phan).");
                soldToday += qty;
                int pay = qty * Price();
                Game.Player.Money = Game.Player.Money + pay;
                Notify("~g~+$" + pay + "~s~ ban " + qty + " phan (kho con " + bizStock + ").");
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

    // ------------------------------------------------------------ cư dân có lịch sinh hoạt
    void MakeResident()
    {
        Ped me = Game.Player.Character;
        Ped best = null;
        float bd = 6f;
        foreach (Ped p in World.GetNearbyPeds(me, 6f))
        {
            if (p == null || !p.Exists() || p == me || !p.IsHuman || !p.IsAlive || p.IsPlayer) continue;
            float d = p.Position.DistanceTo(me.Position);
            if (d < bd) { bd = d; best = p; }
        }
        if (best == null) { Notify("Khong co ai o gan."); return; }
        foreach (Resident r0 in residents) if (r0.P == best) { Notify("Nguoi nay da la cu dan roi."); return; }
        Resident r = new Resident();
        r.P = best;
        r.Work = best.Position;
        Vector3 h = Vector3.Zero;
        for (int i = 0; i < 6 && h == Vector3.Zero; i++)
        {
            double ang = rnd.NextDouble() * Math.PI * 2;
            float dist = 150f + (float)rnd.NextDouble() * 200f;
            h = World.GetNextPositionOnStreet(best.Position + new Vector3((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist, 0f));
        }
        r.Home = h == Vector3.Zero ? best.Position : h;
        best.IsPersistent = true;
        Blip b = best.AddBlip();
        b.Color = BlipColor.Blue;
        b.Scale = 0.6f;
        residents.Add(r);
        Notify("~g~Cu dan moi~s~: ban ngay o day, toi ve nha.");
    }

    // 0 = ban ngay (7-19h) o cho lam, 1 = buoi toi (19-23h) gan nha, 2 = dem (23-7h) o nha
    int PhaseNow()
    {
        int h = World.CurrentTimeOfDay.Hours;
        if (h >= 7 && h < 19) return 0;
        if (h >= 19 && h < 23) return 1;
        return 2;
    }

    void UpdateResidents(int now)
    {
        if (now < nextResidentCheck || residents.Count == 0) return;
        nextResidentCheck = now + 10000;
        int phase = PhaseNow();
        for (int i = residents.Count - 1; i >= 0; i--)
        {
            Resident r = residents[i];
            Ped p = r.P;
            if (p == null || !p.Exists() || !p.IsAlive) { residents.RemoveAt(i); continue; }
            if (p.IsPositionFrozen) continue; // dang noi chuyen voi Sep
            Vector3 c = phase == 0 ? r.Work : r.Home;
            float radius = phase == 0 ? 20f : phase == 1 ? 10f : 3f;
            float d = p.Position.DistanceTo2D(c);
            if (d > radius + 10f)
            {
                if (now - r.LastCmd > 20000)
                {
                    r.LastCmd = now;
                    Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, p, c.X, c.Y, c.Z, 1.0f, -1, 2.0f, false, 0f);
                }
            }
            else if (r.Phase != phase)
            {
                r.Phase = phase;
                Function.Call(Hash.TASK_WANDER_IN_AREA, p, c.X, c.Y, c.Z, radius, 2f, 6f);
            }
        }
    }

    // ------------------------------------------------------------ nhà, ăn, uống, ngủ
    bool Spend(int amount)
    {
        if (Game.Player.Money < amount) { Notify("Khong du tien ($" + amount + ")."); return false; }
        Game.Player.Money = Game.Player.Money - amount;
        return true;
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
        if (!hasHome || me.Position.DistanceTo(home) > maxDist) { Notify("Phai o gan nha (" + (int)maxDist + " m) moi ngu duoc."); return; }
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
        if (fadeAction == 3)
        {
            AdvanceTime(4 * 60);
            Game.Player.Money = Game.Player.Money + WorkPay;
            hunger = Clamp(hunger - 15f);
            thirst = Clamp(thirst - 18f);
            energy = Clamp(energy - 20f);
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
            me.Health = Math.Max(me.Health, me.MaxHealth * 3 / 4);
            string extra = "";
            if (rnd.Next(100) < 30 && Game.Player.Money > 0)
            {
                int stolen = Math.Min(Game.Player.Money, 50 + rnd.Next(250));
                Game.Player.Money = Game.Player.Money - stolen;
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
            if (Game.Player.Money >= fine) { Game.Player.Money = Game.Player.Money - fine; Notify("~r~Camera phat toc do:~s~ -$" + fine + "."); }
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
            if (now - r.T > 20000) { Notify("Khong goi duoc xe, thu lai sau."); if (r.OwnVeh) Game.Player.Money = Game.Player.Money + DriverFee; ride = null; return; }
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
        if (Game.Player.Money >= fare) { Game.Player.Money = Game.Player.Money - fare; Notify("Tra tien taxi ~r~-$" + fare + "~s~ (" + (int)dist + " m)."); }
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
        Item("Tim nha thue...", delegate { ShowHomes(); });
        Item("Ngu ngoai duong  (mien phi, 30% bi cuop)", delegate { CloseMenu(); SleepRough(); });
        Item("Cong viec...", delegate { ShowJob(); });
        Item("Kinh doanh...", delegate { ShowBiz(); });
        Item("Di chuyen: goi taxi, tai xe rieng...", delegate { ShowTransport(); });
        Item("Dat nguoi gan nhat lam cu dan (co lich)", delegate { MakeResident(); });
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
        Item("Lam ca tho phu o LS Customs  (4 gio, +$" + WorkPay + ", 7h-18h)", delegate { CloseMenu(); StartWork(); });
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowBiz()
    {
        Begin("KINH DOANH", delegate { ShowMain(); });
        if (shops.Count > 0) Item("Bang kho cac quan - dat hang tu xa...  (" + shops.Count + " quan)", delegate { ShowStockBoard(); });
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
            Item("Nhap hang +" + StockPack + " phan  (-$" + StockPrice + ")  kho: " + bizStock, delegate
            {
                if (Spend(StockPrice)) { bizStock += StockPack; }
                ShowBiz();
            });
            Item("Doi quan sang cho phang gan day (mien phi)", delegate { RelocateBiz(); ShowBiz(); });
            int cap = CapOf(cur);
            Info("Hien tai: cap " + bizLevel + " - " + TierName[bizLevel - 1] + "  (gia ban $" + Price() + "/phan)");
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
                Game.Player.Money = Game.Player.Money + bizCash;
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
        Info("Cu dan co lich: " + residents.Count);
        Item("Quay lai", delegate { ShowMain(); });
    }

    void PayDebt()
    {
        int pay = Math.Min(debt, Game.Player.Money);
        if (pay <= 0) { Notify("Khong co tien de tra."); return; }
        Game.Player.Money = Game.Player.Money - pay;
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

    // bieu tuong PNG trong thu muc GtaVietLifeIcons canh script (neu thieu thi khong ve icon)
    readonly Dictionary<string, GTA.UI.CustomSprite> icons = new Dictionary<string, GTA.UI.CustomSprite>();
    void DrawIcon(string name, float x, float y, float size)
    {
        try
        {
            GTA.UI.CustomSprite sp;
            if (!icons.TryGetValue(name, out sp))
            {
                string f = Path.Combine(Path.Combine(Path.GetDirectoryName(Filename), "GtaVietLifeIcons"), name + ".png");
                sp = File.Exists(f) ? new GTA.UI.CustomSprite(f, new SizeF(size, size), new PointF(x, y)) : null;
                icons[name] = sp;
            }
            if (sp == null) return;
            sp.Size = new SizeF(size, size);
            sp.Position = new PointF(x, y);
            sp.Draw();
        }
        catch { }
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
        float below = y + 66f;
        if (showStock && shops.Count > 0) { DrawStockPanel(x, below); below += 30f + shops.Count * 20f; }
        y = below - 66f;   // phan chu phia duoi tinh theo vi tri moi
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
            l.Add("rep=" + rep);
            l.Add("debt=" + debt);
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
                l.Add("shop=" + sh.Pos.X.ToString(ci) + "," + sh.Pos.Y.ToString(ci) + "," + sh.Pos.Z.ToString(ci) + "," + sh.Heading.ToString(ci) + "," + sh.Ground.ToString(ci) + "," + sh.Level + "," + sh.Stock + "," + sh.Staff + "," + sh.Cash + "," + sh.SoldToday + "," + sh.Mode + "," + sh.Rent + "," + sh.DaysLeft + "," + sh.Overdue + "," + sh.Paid + "," + (sh.Indoor ? 1 : 0));
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
                switch (k)
                {
                    case "hunger": hunger = float.Parse(v, ci); break;
                    case "thirst": thirst = float.Parse(v, ci); break;
                    case "energy": energy = float.Parse(v, ci); break;
                    case "rep": rep = int.Parse(v); break;
                    case "debt": debt = int.Parse(v); break;
                    case "job": jobGiaoHang = v == "1"; break;
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
                            sh.Mode = Math.Max(0, Math.Min(3, int.Parse(q[10]))); sh.Rent = int.Parse(q[11]); sh.DaysLeft = int.Parse(q[12]); sh.Overdue = int.Parse(q[13]); sh.Paid = int.Parse(q[14]); sh.Indoor = q.Length > 15 && q[15] == "1";
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
