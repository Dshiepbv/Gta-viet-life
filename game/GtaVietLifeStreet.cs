// GTA Việt Life - MODULE 2: HÀNG QUÁN VỈA HÈ ĐÔNG ĐÚC (độc lập, không phụ thuộc script nào khác).
// Chép vào "scripts" cùng GtaVietLifeStreet.ini; bấm ] để nạp lại.
//
// Làm gì: dọc vỉa hè quanh người chơi tự sinh các "cụm sinh hoạt": xe đẩy / quầy, bàn ghế, chủ quán đứng đúng chỗ,
// khách ngồi ăn uống (nhiều hay ít tuỳ giờ), xe máy dựng sát lề. Cách xa là tự dọn, đi tới đâu có tới đó.
// Loại cụm theo giờ: sáng = bánh mì/phở, trưa = cơm tấm, chiều tối = cà phê cóc / trà đá / nhậu vỉa hè.
// Đây là phần "nhìn thấy bằng mắt" (chưa có mua bán): mua bán bằng giọng nói là module sau.
// Log ngắn mỗi 20 giây ở GtaVietLifeStreet.log.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeStreet : Script
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    const string LogFile = "GtaVietLifeStreet.log";

    bool enabled = true;
    int maxHubs = 7;                 // số cụm tối đa cùng lúc quanh người chơi
    float spawnMin = 45f, spawnMax = 110f, despawnDist = 170f;
    int customerMax = 5;
    float rushHourBoost = 1.0f;

    class Hub
    {
        public Vector3 Pos; public float Heading; public int Kind;
        public List<Prop> Props = new List<Prop>();
        public List<Ped> Peds = new List<Ped>();
        public List<Vehicle> Bikes = new List<Vehicle>();
    }
    readonly List<Hub> hubs = new List<Hub>();
    readonly Random rnd = new Random();

    // loại cụm: tên, mô hình quầy, mô hình bàn, mô hình ghế, có khách ngồi, giờ hợp (từ, đến)
    class Kind { public string Name, Stall, Table, Seat; public int H1, H2, Seats; }
    static readonly Kind[] Kinds = {
        new Kind { Name = "banh mi / pho", Stall = "prop_hotdogstand_01", Table = "prop_table_03", Seat = "prop_bar_stool_01", H1 = 5,  H2 = 10, Seats = 3 },
        new Kind { Name = "com tam",       Stall = "prop_hotdogstand_01", Table = "prop_table_03", Seat = "prop_bar_stool_01", H1 = 10, H2 = 15, Seats = 4 },
        new Kind { Name = "ca phe coc",    Stall = "prop_hotdogstand_01", Table = "prop_table_03", Seat = "prop_bar_stool_01", H1 = 14, H2 = 20, Seats = 4 },
        new Kind { Name = "tra da / nhau", Stall = "prop_hotdogstand_01", Table = "prop_table_03", Seat = "prop_bar_stool_01", H1 = 17, H2 = 24, Seats = 5 },
    };
    readonly string[] vendorModels = { "a_f_m_ktown_01", "a_m_m_ktown_01", "a_f_y_ktown_01" };
    readonly string[] guestModels = { "a_m_y_ktown_01", "a_f_y_ktown_01", "a_m_m_ktown_01", "a_f_m_ktown_01", "a_m_y_ktown_02", "a_f_y_ktown_02" };
    readonly string[] bikeModels = { "faggio", "faggio2", "faggio3" };

    int nextTick, nextLog;
    int nMade, nGone; string lastErr = "";

    public GtaVietLifeStreet()
    {
        LoadCfg();
        Tick += OnTick; Interval = 0;
        Log("khoi dong; enabled=" + enabled + " maxHubs=" + maxHubs);
    }

    float F(string key, float def)
    {
        string s = Settings.GetValue<string>("Street", key, def.ToString(CI));
        float v; return float.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CI, out v) ? v : def;
    }
    void LoadCfg()
    {
        enabled = Settings.GetValue("Street", "Enabled", true);
        maxHubs = (int)F("MaxHubs", maxHubs); customerMax = (int)F("CustomersPerHub", customerMax);
        spawnMin = F("SpawnMin", spawnMin); spawnMax = F("SpawnMax", spawnMax); despawnDist = F("DespawnDistance", despawnDist);
    }
    void Log(string s) { try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\r\n"); } catch { } }

    static bool InView(Vector3 p)
    {
        Vector3 to = p - GameplayCamera.Position; float len = to.Length(); if (len < 1f) return true;
        Vector3 d = GameplayCamera.Direction;
        return (to.X * d.X + to.Y * d.Y + to.Z * d.Z) / len > 0.35f;
    }

    static Model Load(string name)
    {
        Model m = new Model(name);
        if (!m.IsInCdImage) return null;
        if (!m.IsLoaded && !m.Request(400)) return null;
        return m;
    }

    void OnTick(object sender, EventArgs e)
    {
        if (!enabled) return;
        int now = Game.GameTime;
        if (now < nextTick) return;
        nextTick = now + 2500;
        try
        {
            Ped me = Game.Player.Character;
            if (me == null || !me.Exists() || Game.Player.WantedLevel > 0) return;
            Vector3 pp = me.Position;
            int hour = World.CurrentTimeOfDay.Hours;

            // dọn cụm đã xa
            for (int i = hubs.Count - 1; i >= 0; i--)
            {
                Hub h = hubs[i];
                if (h.Pos.DistanceTo(pp) > despawnDist && !InView(h.Pos)) { Clear(h); hubs.RemoveAt(i); nGone++; }
            }
            // sinh cụm mới (1 cụm/lần, khi thiếu)
            if (hubs.Count < maxHubs) TryMakeHub(me, hour);

            if (now > nextLog)
            {
                nextLog = now + 20000;
                Log("cum=" + hubs.Count + " (da lap " + nMade + ", da don " + nGone + ") gio=" + hour + (lastErr.Length > 0 ? " | loi cuoi: " + lastErr : ""));
            }
        }
        catch (Exception ex) { lastErr = ex.Message; }
    }

    void Clear(Hub h)
    {
        foreach (Ped p in h.Peds) if (p != null && p.Exists()) p.Delete();
        foreach (Prop p in h.Props) if (p != null && p.Exists()) p.Delete();
        foreach (Vehicle v in h.Bikes) if (v != null && v.Exists()) v.Delete();
    }

    Kind PickKind(int hour)
    {
        var fit = new List<Kind>();
        foreach (Kind k in Kinds) if (hour >= k.H1 && hour < k.H2) fit.Add(k);
        if (fit.Count == 0) return null;    // khuya: ít hàng, chỉ để trống
        return fit[rnd.Next(fit.Count)];
    }

    void TryMakeHub(Ped me, int hour)
    {
        Kind kind = PickKind(hour);
        if (kind == null && rnd.Next(100) < 70) return;
        if (kind == null) kind = Kinds[3];

        Vector3 pp = me.Position;
        double ang = rnd.NextDouble() * Math.PI * 2;
        float dist = spawnMin + (float)rnd.NextDouble() * (spawnMax - spawnMin);
        Vector3 raw = pp + new Vector3((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist, 0f);
        var outPos = new OutputArgument();
        if (!Function.Call<bool>(Hash.GET_SAFE_COORD_FOR_PED, raw.X, raw.Y, raw.Z, true, outPos, 16)) return;      // vỉa hè (onlyOnPavement)
        Vector3 pos = outPos.GetResult<Vector3>();
        if (pos == Vector3.Zero) return;
        float d = pos.DistanceTo(pp);
        if (d < spawnMin * 0.8f || d > spawnMax * 1.4f) return;
        if (InView(pos)) return;
        foreach (Hub h in hubs) if (h.Pos.DistanceTo(pos) < 35f) return;    // không dồn cục
        if (World.GetNearbyPeds(pos, 3f).Length > 3) return;

        // hướng: quay mặt ra lòng đường (hướng tới điểm đường gần nhất)
        Vector3 st = World.GetNextPositionOnStreet(pos);
        float heading = (st == Vector3.Zero) ? (float)(rnd.NextDouble() * 360.0) : (st - pos).ToHeading();

        Model stallM = Load(kind.Stall), tableM = Load(kind.Table), seatM = Load(kind.Seat);
        Model vm = Load(vendorModels[rnd.Next(vendorModels.Length)]);
        if (stallM == null || vm == null) return;

        Hub hub = new Hub { Pos = pos, Heading = heading, Kind = Array.IndexOf(Kinds, kind) };
        double hr = heading * Math.PI / 180.0;
        Vector3 fwd = new Vector3((float)(-Math.Sin(hr)), (float)Math.Cos(hr), 0f);   // hướng ra lòng đường
        Vector3 right = new Vector3(fwd.Y, -fwd.X, 0f);

        // quầy/xe đẩy
        Prop stall = World.CreateProp(stallM, pos, new Vector3(0f, 0f, heading + 180f), false, false);
        if (stall != null && stall.Exists()) { stall.IsPositionFrozen = true; hub.Props.Add(stall); }

        // chủ quán đứng sau quầy
        Vector3 vpos = pos - fwd * 1.0f;
        Ped vendor = World.CreatePed(vm, vpos, heading);
        if (vendor != null && vendor.Exists())
        {
            vendor.IsPersistent = false; vendor.BlockPermanentEvents = true;
            vendor.Task.StartScenarioAtPosition("WORLD_HUMAN_STAND_IMPATIENT", vendor.Position, heading, 0, false, true);
            hub.Peds.Add(vendor);
        }

        // bàn ghế + khách, số khách tuỳ giờ (giờ ăn đông hơn)
        int seats = Math.Min(kind.Seats, customerMax);
        int crowd = seats;
        if (hour < 7 || hour >= 21) crowd = Math.Max(1, seats / 2);
        for (int i = 0; i < kind.Seats; i++)
        {
            Vector3 sp = pos + fwd * 2.2f + right * ((i - (kind.Seats - 1) / 2f) * 1.0f);
            if (seatM != null)
            {
                Prop seat = World.CreateProp(seatM, sp, new Vector3(0f, 0f, heading), false, false);
                if (seat != null && seat.Exists()) { seat.IsPositionFrozen = true; hub.Props.Add(seat); }
            }
            if (i < crowd)
            {
                Model gm = Load(guestModels[rnd.Next(guestModels.Length)]);
                if (gm == null) continue;
                Ped g = World.CreatePed(gm, sp, heading + 180f);
                if (g != null && g.Exists())
                {
                    g.IsPersistent = false; g.BlockPermanentEvents = true;
                    g.Task.StartScenarioAtPosition("PROP_HUMAN_SEAT_CHAIR", sp, heading + 180f, 0, true, true);
                    hub.Peds.Add(g);
                }
            }
        }
        if (tableM != null)
        {
            Prop tb = World.CreateProp(tableM, pos + fwd * 3.0f, new Vector3(0f, 0f, heading), false, false);
            if (tb != null && tb.Exists()) { tb.IsPositionFrozen = true; hub.Props.Add(tb); }
        }

        // xe máy dựng sát lề (2-3 chiếc)
        int nb = 2 + rnd.Next(2);
        for (int i = 0; i < nb; i++)
        {
            Model bm = Load(bikeModels[rnd.Next(bikeModels.Length)]);
            if (bm == null) break;
            Vector3 bp = pos + fwd * 4.2f + right * ((i - 1) * 1.1f);
            Vehicle b = World.CreateVehicle(bm, bp, heading + 90f);
            if (b != null && b.Exists()) { b.IsPersistent = false; b.IsEngineRunning = false; hub.Bikes.Add(b); }
        }

        hubs.Add(hub); nMade++;
    }
}
