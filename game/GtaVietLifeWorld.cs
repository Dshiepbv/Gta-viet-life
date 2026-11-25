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
    Keys menuKey = Keys.M;

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
    Blip bizBlip;
    int bizStock, bizStaff, bizCash;
    readonly List<Ped> staffPeds = new List<Ped>();
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
    float staffZ = 0f;
    string standModel = "prop_hotdogstand_01";
    float cupX = 0.06f, cupY = 0f, cupZ = 0f, cupTable = 0.9f;
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
        seatZ = Settings.GetValue("Business", "SeatZ", seatZ);
        staffZ = Settings.GetValue("Business", "StaffZ", staffZ);
        standYaw = Settings.GetValue("Business", "StandYaw", standYaw);
        standModel = Settings.GetValue("Business", "StandModel", standModel);
        cupX = Settings.GetValue("Business", "CupHandX", cupX);
        cupY = Settings.GetValue("Business", "CupHandY", cupY);
        cupZ = Settings.GetValue("Business", "CupHandZ", cupZ);
        cupTable = Settings.GetValue("Business", "CupTableZ", cupTable);

        Load();

        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
        Notify("~g~GTA Viet Life: Doi song~s~ da bat. Bam ~y~M~s~ de mo menu.");
        if (hasBiz) { CreateBizBlip(); BuildBizProps(); }
        for (int i = 0; i < Math.Min(bizStaff, MaxVisibleStaff); i++) SpawnStaff();
    }

    // ------------------------------------------------------------ vòng lặp
    void OnTick(object sender, EventArgs e)
    {
        Ped me = Game.Player.Character;
        if (me == null || !me.Exists()) return;
        int now = Game.GameTime;

        if (sleepAt > 0 && now > sleepAt) { sleepAt = 0; FinishSleep(); }
        if (sleepAt > 0) return;
        UpdateClock();
        UpdateNeeds(me, now);
        UpdateWanted(me);
        UpdateDelivery(me, now);
        UpdateResidents(now);
        UpdateStaff(now);
        UpdateCustomers(now, me);
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
        ClearBizProps();
        if (bizBlip != null && bizBlip.Exists()) bizBlip.Delete();
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
            Notify("~r~Sep dang " + what + "!~s~ Bam M de an/uong/ngu.");
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
        if (hasBiz)
        {
            int target = 15 + (bizLevel - 1) * 8 + bizStaff * 12 + rnd.Next(0, 11) + Math.Max(0, rep / 5);
            int remain = Math.Max(0, target - soldToday);   // khach thay tan mat da ban theo thoi gian thuc
            int sold = Math.Min(bizStock, remain);
            bizStock -= sold;
            int revenue = sold * Price();
            Game.Player.Money = Game.Player.Money + revenue;
            int wages = bizStaff * WagePerDay;
            if (Game.Player.Money >= wages) Game.Player.Money = Game.Player.Money - wages;
            else { debt += wages; report += " ~r~Thieu tien tra luong, ghi no.~s~"; }
            if (sold + soldToday >= 10) rep += 1;
            report += " Quan: ban " + (sold + soldToday) + " phan, luong -$" + wages + ".";
            soldToday = 0;
            if (bizStock == 0) report += " ~y~Het hang!~s~";
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
    void BuyBiz()
    {
        if (hasBiz) return;
        if (Game.Player.Money < BizPrice) { Notify("Khong du $" + BizPrice + " de mo quan."); return; }
        Game.Player.Money = Game.Player.Money - BizPrice;
        hasBiz = true;
        PlaceBizOnFlatGround();
        bizLevel = 1;
        bizStock = 30;
        bizStaff = 0;
        bizCash = 0;
        CreateBizBlip();
        BuildBizProps();
        Notify("~g~Da mo quan!~s~ Tang 30 phan hang. Dung gan quan bam M de quan ly.");
        PostEvent("Có gã vừa mở một quán nhỏ ở " + SafeStreet(bizPos) + ".");
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
        PlaceBizOnFlatGround();
        CreateBizBlip();
        BuildBizProps();
        for (int i = 0; i < Math.Min(staff, MaxVisibleStaff); i++) SpawnStaff();
        Notify("Da doi quan ve cho Sep dang dung. Cham xanh tren ban do da cap nhat.");
    }

    void CreateBizBlip()
    {
        if (bizBlip != null && bizBlip.Exists()) bizBlip.Delete();
        bizBlip = World.CreateBlip(bizPos);
        bizBlip.Color = BlipColor.Green;
        bizBlip.Name = "Quan cua Sep";
    }

    static float FaceHeading(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        return (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);
    }

    void ClearBizProps()
    {
        foreach (Prop p in bizProps) if (p != null && p.Exists()) p.Delete();
        bizProps.Clear();
        chairProps.Clear();
        chairBusy.Clear();
        chairFace.Clear();
        chairTable.Clear();
        foreach (Prop tc in tableCups) DeleteProp(tc);
        tableCups.Clear();
    }

    Prop AddProp(string model, Vector3 pos, float heading) { return AddProp(model, pos, heading, true); }

    Prop AddProp(string model, Vector3 pos, float heading, bool onGround)
    {
        try
        {
            Model m = new Model(model);
            if (!m.IsValid || !m.Request(1500)) { Log("khong nap duoc prop " + model); return null; }
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
        ClearBizProps();
        foreach (Cust k in customers) k.Chair = -1;
        float h = bizHeading * (float)Math.PI / 180f;
        Vector3 right = new Vector3((float)Math.Cos(h), (float)Math.Sin(h), 0f);
        Vector3 fwd = new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
        Prop stand = AddProp(standModel, bizPos, bizHeading + standYaw);
        if (stand == null && standModel != "prop_hotdogstand_01") { Log("model quay " + standModel + " khong dung duoc, dung quay hot dog"); stand = AddProp("prop_hotdogstand_01", bizPos, bizHeading + standYaw); }
        if (stand != null) Function.Call(Hash.SET_ENTITY_COLLISION, stand, false, false);   // quay chi de trang tri, nhan vien khong bi day len mat quay
        string[] parasols = { "prop_parasol_01", "prop_parasol_01", "prop_parasol_03", "prop_parasol_04" };
        int tables = Math.Min(4, bizLevel);   // cap 1: 1 ban, cap 2: 2 ban, cap 3: 3 ban, cap 4: 4 ban
        for (int l = 1; l <= tables; l++)
        {
            int row = (l - 1) / 2;
            float side = (l % 2 == 1) ? 1f : -1f;
            Vector3 c = bizPos + fwd * (3.5f + row * 3.0f) + right * (2.4f * side);
            AddProp("prop_table_03", c, bizHeading);
            AddProp("prop_chair_01a", c + right * 1.1f, bizHeading - 90f);
            if (bizProps.Count > 0) { chairProps.Add(bizProps[bizProps.Count - 1]); chairBusy.Add(false); chairFace.Add(FaceHeading(c + right * 1.1f, c)); chairTable.Add(c); tableCups.Add(null); }
            AddProp("prop_chair_01a", c - right * 1.1f, bizHeading + 90f);
            if (bizProps.Count > 0) { chairProps.Add(bizProps[bizProps.Count - 1]); chairBusy.Add(false); chairFace.Add(FaceHeading(c - right * 1.1f, c)); chairTable.Add(c); tableCups.Add(null); }
            AddProp(parasols[Math.Min(l, 4) - 1], c, bizHeading);
        }
        // cap 2: chau cay hai ben quay
        if (bizLevel >= 2)
        {
            AddProp("prop_pot_plant_05a", bizPos + right * 2.2f + fwd * 0.6f, bizHeading);
            AddProp("prop_pot_plant_05a", bizPos - right * 2.2f + fwd * 0.6f, bizHeading);
        }
        // cap 3: den suoi, thung rac, ghe da
        if (bizLevel >= 3)
        {
            AddProp("prop_patio_heater_01", bizPos + fwd * 3.0f + right * 0.0f, bizHeading);
            AddProp("prop_bin_01a", bizPos + right * 2.8f - fwd * 0.4f, bizHeading);
            AddProp("prop_bench_01a", bizPos - right * 3.2f + fwd * 1.5f, bizHeading + 90f);
        }
        // cap 4: ghe quay bar, hang cay xanh, them den suoi
        if (bizLevel >= 4)
        {
            AddProp("prop_bar_stool_01", bizPos - right * 1.4f + fwd * 1.0f, bizHeading);
            AddProp("prop_bar_stool_01", bizPos - right * 2.0f + fwd * 1.0f, bizHeading);
            AddProp("prop_pot_plant_05a", bizPos + right * 4.4f + fwd * 3.0f, bizHeading);
            AddProp("prop_pot_plant_05a", bizPos - right * 4.4f + fwd * 3.0f, bizHeading);
            AddProp("prop_patio_heater_01", bizPos + fwd * 6.5f, bizHeading);
        }
    }

    static readonly string[] TierName = { "Xe day", "Quay via he", "Quan nuoc co mai che", "Quan sang trong" };
    static readonly string[] TierPerk = {
        "1 ban",
        "2 ban, chau cay",
        "3 ban, o che mau, den suoi, ghe da, thung rac",
        "4 ban, ghe quay bar, cay xanh, nhieu den suoi" };

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
            if (!mdl.Request(1000)) return;
            float h = bizHeading * (float)Math.PI / 180f;
            Vector3 right = new Vector3((float)Math.Cos(h), (float)Math.Sin(h), 0f);
            Vector3 fwd = new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
            int k = staffPeds.Count;
            // nhan vien dung sau quay (k=0) hoac ben canh quay, quay mat ve phia khach
            Vector3 pos0 = bizPos - fwd * 1.25f + right * (k == 0 ? 0f : (k % 2 == 1 ? 1f : -1f) * 0.9f * ((k + 1) / 2));   // nguoi ban dung sau quay (phia tay day), tren mat dat, khong chen vao than quay
            // bizPos.Z = do cao tam nguoi choi luc dat quan (cho phang) => dung luon cho nhan vien, khong lay Z cua quay (tam quay cao hon dat)
            Vector3 pos = new Vector3(pos0.X, pos0.Y, GroundForBiz() + 1.0f + staffZ);
            Ped p = World.CreatePed(mdl, pos);
            mdl.MarkAsNoLongerNeeded();
            if (p == null || !p.Exists()) return;
            p.Heading = bizHeading;
            pos = p.Position;                  // vi tri dung that cua nhan vien
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
            if (!mdl.Request(1000)) return;
            Ped p = World.CreatePed(mdl, sp);
            mdl.MarkAsNoLongerNeeded();
            if (p == null || !p.Exists()) return;
            Cust c = new Cust();
            c.P = p;
            c.T = Game.GameTime;
            c.Qty = 1 + rnd.Next(2);
            // neu co ghe trong thi 50% ngoi lai
            for (int i = 0; i < chairProps.Count; i++)
                if (!chairBusy[i] && !ChairTaken(i) && rnd.Next(2) == 0) { c.Chair = i; chairBusy[i] = true; c.Qty += 1; break; }
            p.IsPersistent = true;
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, p, true);
            Vector3 dest2 = QueueSpot();
            Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, p, dest2.X, dest2.Y, dest2.Z, 1.0f, 25000, 1.5f, false, 0f);
            customers.Add(c);
            Log("sinh khach, cach quan " + (int)sp.DistanceTo(bizPos) + " m, ghe " + c.Chair);
        }
        catch (Exception ex) { Log("SpawnCustomer loi: " + ex.Message); }
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
            if (c.Stage == 0)
            {
                bool arrived = p.Position.DistanceTo(bizPos) < 3.2f;
                if (!arrived && now - c.T > 30000 && p.Position.DistanceTo(bizPos) < 10f) arrived = true;
                if (arrived || now - c.T > 30000)
                {
                    if (!arrived) { Log("khach khong toi duoc quay (cach " + (int)p.Position.DistanceTo(bizPos) + " m), bo"); Release(c); customers.RemoveAt(i); continue; }
                    bool served = bizStaff > 0 || me.Position.DistanceTo(bizPos) < 8f;
                    if (!served)
                    {
                        if (now - c.T > 45000) { Notify("Khach bo di vi khong ai phuc vu. Thue nhan vien hoac dung gan quan."); Release(c); customers.RemoveAt(i); }
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
                if (bizStock == 0) Notify("~r~Het hang!~s~ Den quan bam M > Kinh doanh > Nhap hang de tiep tuc ban.");
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
                    if (t.IsValid && t.Request(800)) { cupModel = name; Log("dung model ly: " + name); break; }
                }
                if (cupModel == null) { Log("khong co model ly nao dung duoc"); cupModel = ""; }
            }
            if (cupModel.Length == 0) return null;
            Model m = new Model(cupModel);
            if (!m.Request(800)) return null;
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
        Vector3 pos = cp + toward * 0.75f;
        pos.Z = cp.Z + cupTable;
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
                    if (Math.Abs(p.Position.Z - staffSlots[i].Z) > 0.3f && p.Position.DistanceTo2D(bizPos) < 8f)
                        p.Position = staffSlots[i];
                    continue;
                }
                // ve cho dung
                if (p.Position.DistanceTo(staffSlots[i]) > 1.5f) p.Position = staffSlots[i];
                p.Heading = bizHeading;
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
        Notify("~g~Da thue " + h.Name + "~s~. Coc 3 ngay $" + deposit + ", thue $" + h.Rent + "/ngay. Den gan nha bam M de ngu.");
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

    void Begin(string title, Action back)
    {
        menuTitle = title;
        menuLabels = new List<string>();
        menuActions = new List<Action>();
        menuBack = back;
    }

    void ShowMain()
    {
        Begin("DOI SONG  (M/Backspace: dong)", null);
        Item("An com  (-$" + FoodPrice + ")", delegate { Eat(); });
        Item("Uong nuoc  (-$" + DrinkPrice + ")", delegate { Drink(); });
        if (hasHome) Item("Ngu o nha  (tua 8 gio)", delegate { CloseMenu(); Sleep(); });
        Item("Tim nha thue...", delegate { ShowHomes(); });
        Item("Ngu ngoai duong  (mien phi, 30% bi cuop)", delegate { CloseMenu(); SleepRough(); });
        Item("Cong viec...", delegate { ShowJob(); });
        Item("Kinh doanh...", delegate { ShowBiz(); });
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
        if (!hasBiz)
        {
            Item("Mo quan o day  (-$" + BizPrice + ")", delegate { BuyBiz(); ShowBiz(); });
        }
        else if (!NearBiz())
        {
            Item("(Hay den gan quan - cham xanh tren ban do)", delegate { });
            Item("Doi quan ve cho Sep dang dung  (-$" + MoveFar + ", giu nguyen hang, nhan vien, cap)", delegate
            {
                if (Game.Player.Character.IsInVehicle()) { Notify("Xuong xe roi hay doi quan."); return; }
                if (Spend(MoveFar)) { RelocateBiz(); }
                ShowBiz();
            });
        }
        else
        {
            Item("Nhap hang +" + StockPack + " phan  (-$" + StockPrice + ")  kho: " + bizStock, delegate
            {
                if (Spend(StockPrice)) { bizStock += StockPack; }
                ShowBiz();
            });
            Item("Doi quan sang cho phang gan day (mien phi)", delegate { RelocateBiz(); ShowBiz(); });
            Item("Hien tai: cap " + bizLevel + " - " + TierName[bizLevel - 1] + "  (gia ban $" + Price() + "/phan)", delegate { });
            Item(bizLevel >= 4 ? "Quan da dat cap toi da" : "Nang cap len cap " + (bizLevel + 1) + " " + TierName[bizLevel] + " (" + TierPerk[bizLevel] + ")  -$" + (2500 * bizLevel), delegate
            {
                if (bizLevel >= 4) Notify("Quan da toi da.");
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
        }
        Item("Quay lai", delegate { ShowMain(); });
    }

    void ShowInfo()
    {
        Begin("THONG TIN", delegate { ShowMain(); });
        Item("Tien: $" + Game.Player.Money + "   No: $" + debt, delegate { });
        Item("Uy tin: " + rep + "   Giao hang hom nay: " + deliveriesToday + " don ($" + earnedToday + ")", delegate { });
        Item("Nha: " + (hasHome ? (homeName.Length > 0 ? homeName : "cho tu chon") + " ($" + rentPerDay + "/ngay)" : "chua thue"), delegate { });
        Item("Quan: " + (hasBiz ? "cap " + bizLevel + " " + TierName[bizLevel - 1] + ", kho " + bizStock + ", nv " + bizStaff + ", quy $" + bizCash : "chua co"), delegate { });
        Item("Cu dan co lich: " + residents.Count, delegate { });
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

    void OnKeyDown(object sender, KeyEventArgs e)
    {
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
        int idx = -1;
        if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9) idx = (int)e.KeyCode - (int)Keys.D1;
        else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9) idx = (int)e.KeyCode - (int)Keys.NumPad1;
        if (idx >= 0 && idx < menuActions.Count)
        {
            try { menuActions[idx](); }
            catch (Exception ex) { Log("menu loi: " + ex); Notify("Loi menu: " + Short(ex)); }
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

    void DrawHud()
    {
        float x = 18f, y = 18f;
        DrawText("$" + Game.Player.Money, x, y - 4f, 0.5f, Color.FromArgb(255, 120, 230, 120));
        y += 28f;
        DrawBar("An", hunger, x, y, Color.FromArgb(220, 235, 150, 40));
        DrawBar("Uong", thirst, x, y + 22f, Color.FromArgb(220, 60, 160, 235));
        DrawBar("Nang luong", energy, x, y + 44f, Color.FromArgb(220, 235, 210, 60));
        if (hasBiz) DrawText("Kho quan: " + bizStock + " phan" + (bizStock == 0 ? "  (HET HANG - nhap hang de co khach)" : ""), x, y + 66f, 0.32f, bizStock == 0 ? Color.FromArgb(255, 255, 90, 90) : Color.White);
        string extra = "";
        if (jobGiaoHang) extra += "Giao hang";
        if (delivering)
        {
            int left = (deadlineMs - Game.GameTime) / 1000;
            extra += " | con " + (int)Game.Player.Character.Position.DistanceTo2D(dest) + "m, " + (left > 0 ? left + "s" : "TRE");
        }
        if (debt > 0) extra += (extra.Length > 0 ? " | " : "") + "No $" + debt;
        if (extra.Length > 0) DrawText(extra, x, y + (hasBiz ? 86f : 66f), 0.32f, Color.White);
    }

    void DrawMenu()
    {
        float x = 40f, y = 170f, w = 520f;
        float h = 38f + menuLabels.Count * 26f;
        new GTA.UI.ContainerElement(new PointF(x, y), new SizeF(w, h), Color.FromArgb(200, 10, 10, 10)).Draw();
        DrawText(menuTitle, x + 12f, y + 6f, 0.45f, Color.FromArgb(255, 255, 210, 80));
        for (int i = 0; i < menuLabels.Count; i++)
            DrawText((i + 1) + ". " + menuLabels[i], x + 12f, y + 38f + i * 26f, 0.38f, Color.White);
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
            l.Add("home=" + home.X.ToString(ci) + "," + home.Y.ToString(ci) + "," + home.Z.ToString(ci));
            l.Add("hasBiz=" + (hasBiz ? 1 : 0));
            l.Add("biz=" + bizPos.X.ToString(ci) + "," + bizPos.Y.ToString(ci) + "," + bizPos.Z.ToString(ci));
            l.Add("stock=" + bizStock);
            l.Add("staff=" + bizStaff);
            l.Add("cash=" + bizCash);
            l.Add("level=" + bizLevel);
            l.Add("heading=" + bizHeading.ToString(ci));
            l.Add("ground=" + bizGround.ToString(ci));
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
                    case "home": home = ParseVec(v); break;
                    case "hasBiz": hasBiz = v == "1"; break;
                    case "biz": bizPos = ParseVec(v); break;
                    case "stock": bizStock = int.Parse(v); break;
                    case "staff": bizStaff = int.Parse(v); break;
                    case "cash": bizCash = int.Parse(v); break;
                    case "level": bizLevel = int.Parse(v); break;
                    case "heading": bizHeading = float.Parse(v, ci); break;
                    case "ground": bizGround = float.Parse(v, ci); break;
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
