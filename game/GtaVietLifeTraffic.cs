// GTA Việt Life - MODULE 1: GIAO THÔNG KIỂU TP.HCM (độc lập, không phụ thuộc script nào khác).
// Chép vào thư mục "scripts" cùng GtaVietLifeTraffic.ini. Đổi thông số trong .ini rồi bấm ] để nạp lại.
//
// Làm gì:
//  1. Xe máy chiếm đa số: đếm xe NPC quanh người chơi, thiếu xe máy thì sinh thêm ở xa tầm nhìn,
//     thừa ô tô (ngoài tầm nhìn) thì xoá bớt. Xe buýt, xe tải, xe công vụ giữ nguyên.
//  2. Phân tầng đường: trục lớn đông và đều, hẻm/đường nhánh xe máy chạy chậm, người đi bộ nhiều hơn.
//  3. Giờ cao điểm (7-9h, 17-19h): xe đông hơn, chạy chậm lại, ùn nhẹ.
//  4. Lái hiền hơn: hạ tốc độ tối đa của xe NPC, giảm hung hăng, tăng khả năng lái.
// Mọi con số nằm trong .ini. Log ngắn mỗi 20 giây ở GtaVietLifeTraffic.log (để chỉnh từ xa).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeTraffic : Script
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    const string LogFile = "GtaVietLifeTraffic.log";

    // ---- cấu hình (đọc ở dạng chuỗi để không dính dấu phẩy thập phân của máy Việt)
    bool enabled = true;
    float bikeShare = 0.75f;            // tỉ lệ xe máy trong tổng xe NPC gần người chơi
    float densMajor = 1.0f, densSide = 0.9f, densRush = 1.0f, densOff = 0.7f;
    float pedSide = 1.5f, pedMajor = 1.0f;
    float spdMajor = 13f, spdSide = 8f, spdRush = 6f;      // m/s: 13 ≈ 47 km/h, 8 ≈ 29 km/h, 6 ≈ 22 km/h
    float aggressiveness = 0.0f;
    float scanRadius = 150f, spawnMin = 80f, spawnMax = 140f;
    int targetMajor = 22, targetSide = 16;                  // số xe máy mục tiêu quanh người chơi
    float rushBoost = 1.4f;
    int rushAm1 = 7, rushAm2 = 9, rushPm1 = 17, rushPm2 = 19;

    readonly Random rnd = new Random();
    readonly HashSet<int> tuned = new HashSet<int>();
    readonly List<Vehicle> mine = new List<Vehicle>();
    readonly string[] bikeModels = { "faggio", "faggio", "faggio", "faggio2", "faggio2", "faggio3", "vader", "nemesis", "ruffian" };
    readonly List<Model> bikeCache = new List<Model>();

    int nextScan, nextRetune, nextLog, nextNode;
    int roadDensity = 8; bool major = false, rush = false;
    int nBike, nCar, nSpawned, nRemoved;
    string lastErr = "";

    public GtaVietLifeTraffic()
    {
        LoadCfg();
        Tick += OnTick;
        Interval = 0;
        Log("khoi dong; enabled=" + enabled + " bikeShare=" + bikeShare.ToString(CI));
    }

    float F(string key, float def)
    {
        string s = Settings.GetValue<string>("Traffic", key, def.ToString(CI));
        float v; return float.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CI, out v) ? v : def;
    }
    void LoadCfg()
    {
        enabled = Settings.GetValue("Traffic", "Enabled", true);
        bikeShare = Math.Max(0f, Math.Min(0.95f, F("BikeShare", bikeShare)));
        densMajor = F("DensityMajor", densMajor); densSide = F("DensitySide", densSide);
        densRush = F("DensityRush", densRush); densOff = F("DensityOffPeak", densOff);
        pedSide = F("PedDensitySide", pedSide); pedMajor = F("PedDensityMajor", pedMajor);
        spdMajor = F("SpeedMajorMps", spdMajor); spdSide = F("SpeedSideMps", spdSide); spdRush = F("SpeedRushMps", spdRush);
        aggressiveness = F("Aggressiveness", aggressiveness);
        scanRadius = F("ScanRadius", scanRadius); spawnMin = F("SpawnMin", spawnMin); spawnMax = F("SpawnMax", spawnMax);
        targetMajor = (int)F("BikesMajor", targetMajor); targetSide = (int)F("BikesSide", targetSide);
        rushBoost = F("RushBoost", rushBoost);
        rushAm1 = (int)F("RushMorningFrom", rushAm1); rushAm2 = (int)F("RushMorningTo", rushAm2);
        rushPm1 = (int)F("RushEveningFrom", rushPm1); rushPm2 = (int)F("RushEveningTo", rushPm2);
    }

    void Log(string s) { try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\r\n"); } catch { } }

    void OnTick(object sender, EventArgs e)
    {
        if (!enabled) return;
        try
        {
            Ped me = Game.Player.Character;
            if (me == null || !me.Exists()) return;
            int now = Game.GameTime;
            int hour = World.CurrentTimeOfDay.Hours;
            rush = (hour >= rushAm1 && hour < rushAm2) || (hour >= rushPm1 && hour < rushPm2);

            // phân tầng đường: mật độ nút đường (0..15) tại chỗ người chơi; nút dày = trục lớn
            if (now > nextNode)
            {
                nextNode = now + 1500;
                var dens = new OutputArgument(); var flags = new OutputArgument();
                Vector3 p = me.Position;
                Function.Call((Hash)0x0568566ACBB5DEDC, p.X, p.Y, p.Z, dens, flags);   // GET_VEHICLE_NODE_PROPERTIES
                roadDensity = dens.GetResult<int>();
                major = roadDensity >= 9;
            }

            // mật độ mỗi khung hình (phải gọi lại mỗi tick)
            float vd = major ? densMajor : densSide;
            vd *= rush ? densRush : densOff;
            Function.Call((Hash)0x245A6883D966D537, vd);                            // SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME
            Function.Call((Hash)0xB3B3359379FE77D3, vd);                            // SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME
            Function.Call((Hash)0xEAE6DCC7EEE3DB1D, major ? 0.6f : 1.0f);           // SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME
            Function.Call((Hash)0x95E3D6257B166CF2, major ? pedMajor : pedSide);    // SET_PED_DENSITY_MULTIPLIER_THIS_FRAME
            Function.Call((Hash)0x7A556143A1C03898, major ? pedMajor : pedSide, major ? pedMajor : pedSide); // SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME

            if (now > nextScan) { nextScan = now + 600; Scan(me, now); }
            if (now > nextLog)
            {
                nextLog = now + 20000;
                Log("duong=" + (major ? "lon" : "nho") + "(" + roadDensity + ") " + (rush ? "cao diem" : "binh thuong") + " | xe may=" + nBike + " oto=" + nCar +
                    " | da sinh=" + nSpawned + " da xoa=" + nRemoved + (lastErr.Length > 0 ? " | loi cuoi: " + lastErr : ""));
            }
        }
        catch (Exception ex) { lastErr = ex.Message; }
    }

    float CruiseSpeed()
    {
        if (rush) return major ? spdRush : spdRush * 0.8f;
        return major ? spdMajor : spdSide;
    }

    static bool IsAmbient(Vehicle v, Ped me)
    {
        if (v == null || !v.Exists() || v.IsPersistent) return false;
        Ped d = v.Driver;
        if (d == null || !d.Exists() || d.IsPlayer || d.IsDead) return false;
        if (me.IsInVehicle() && v == me.CurrentVehicle) return false;
        if (v == Game.Player.LastVehicle) return false;
        return true;
    }

    static bool IsProtectedClass(Vehicle v)
    {
        VehicleClass c = v.ClassType;
        return c == VehicleClass.Emergency || c == VehicleClass.Service || c == VehicleClass.Commercial ||
               c == VehicleClass.Industrial || c == VehicleClass.Helicopters || c == VehicleClass.Planes ||
               c == VehicleClass.Boats || c == VehicleClass.Trains || c == VehicleClass.Military || c == VehicleClass.Utility;
    }

    void Scan(Ped me, int now)
    {
        Vehicle[] near = World.GetNearbyVehicles(me, scanRadius);
        int bikes = 0, cars = 0;
        float spd = CruiseSpeed();
        bool retune = now > nextRetune; if (retune) nextRetune = now + 6000;
        var removable = new List<Vehicle>();

        foreach (Vehicle v in near)
        {
            if (!IsAmbient(v, me)) continue;
            bool isBike = v.Model.IsBike || v.Model.IsQuadBike;
            bool isCar = v.Model.IsCar;
            if (!isBike && !isCar) continue;
            if (isCar && IsProtectedClass(v)) continue;
            if (isBike) bikes++; else { cars++; removable.Add(v); }

            // hạ tốc độ + lái hiền (mỗi xe 1 lần, rồi nhắc lại mỗi 6 giây vì game hay đặt lại nhiệm vụ)
            if (tuned.Add(v.Handle) || retune)
            {
                Ped d = v.Driver;
                float s = (float)(spd * (0.85 + rnd.NextDouble() * 0.3));   // mỗi xe lệch nhau một chút cho tự nhiên
                if (isBike) s *= 0.95f; else s *= 0.9f;
                Function.Call((Hash)0x0E46A3FCBDE2A1B1, v, s + 1.5f);               // SET_ENTITY_MAX_SPEED: trần cứng
                Function.Call((Hash)0x5C9B84BD7D31D908, d, s);                      // SET_DRIVE_TASK_CRUISE_SPEED
                Function.Call((Hash)0xA731F608CA104E3C, d, aggressiveness);         // SET_DRIVER_AGGRESSIVENESS
                Function.Call((Hash)0xB195FFA8042FC5C3, d, 1.0f);                   // SET_DRIVER_ABILITY
            }
        }
        nBike = bikes; nCar = cars;
        if (tuned.Count > 4000) tuned.Clear();

        int total = bikes + cars;
        float cap = 1f - bikeShare;   // tỉ lệ ô tô tối đa
        Vector3 pp = me.Position;

        // 1) thừa ô tô: xoá tối đa 2 chiếc/lần, chỉ xe xa người chơi và ngoài tầm nhìn
        if (total >= 6 && cars > cap * total + 1)
        {
            int del = 0;
            foreach (Vehicle v in removable)
            {
                if (del >= 2) break;
                if (v.Position.DistanceTo(pp) < 60f || v.IsOnScreen) continue;
                Ped d = v.Driver;
                foreach (Ped o in v.Occupants) if (o != null && o.Exists()) o.Delete();
                v.Delete(); del++; nRemoved++;
            }
        }

        // 2) thiếu xe máy: sinh tối đa 2 chiếc/lần, ngoài tầm nhìn, trên đường phía trước/hai bên
        int target = (int)((major ? targetMajor : targetSide) * (rush ? rushBoost : 1f));
        if (bikes < target && me.Position.DistanceTo(Vector3.Zero) > 1f && Game.Player.WantedLevel == 0)
        {
            int made = 0;
            for (int tries = 0; tries < 6 && made < 2 && bikes + made < target; tries++)
            {
                if (SpawnBike(me)) made++;
            }
        }

        // 3) dọn xe do mình sinh khi đã xa hơn 220 m
        for (int i = mine.Count - 1; i >= 0; i--)
        {
            Vehicle v = mine[i];
            if (v == null || !v.Exists()) { mine.RemoveAt(i); continue; }
            if (v.Position.DistanceTo(pp) > 220f && !v.IsOnScreen)
            {
                foreach (Ped o in v.Occupants) if (o != null && o.Exists()) o.Delete();
                v.Delete(); mine.RemoveAt(i);
            }
        }
    }

    bool SpawnBike(Ped me)
    {
        Vector3 pp = me.Position;
        double ang = rnd.NextDouble() * Math.PI * 2;
        float dist = spawnMin + (float)rnd.NextDouble() * (spawnMax - spawnMin);
        Vector3 raw = pp + new Vector3((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist, 0f);
        Vector3 st = World.GetNextPositionOnStreet(raw);
        if (st == Vector3.Zero) return false;
        float d = st.DistanceTo(pp);
        if (d < spawnMin * 0.8f || d > spawnMax * 1.4f) return false;
        if (Function.Call<bool>((Hash)0xDD79DF9839364A12, st.X, st.Y, st.Z, 4f)) return false;   // IS_SPHERE_VISIBLE: không sinh trước mắt
        // tránh chồng lên xe khác
        if (World.GetNearbyVehicles(st, 6f).Length > 0) return false;

        Model m = bikeCache.Count > 0 ? bikeCache[rnd.Next(bikeCache.Count)] : (Model)null;
        if (m == null || !m.IsValid)
        {
            string name = bikeModels[rnd.Next(bikeModels.Length)];
            Model nm = new Model(name);
            if (!nm.IsInCdImage || !nm.Request(500)) return false;
            if (!bikeCache.Contains(nm)) bikeCache.Add(nm);
            m = nm;
        }
        if (!m.IsLoaded && !m.Request(300)) return false;

        float heading = rnd.Next(2) == 0 ? (float)(rnd.NextDouble() * 360.0) : (me.Heading + 180f) % 360f;
        Vehicle v = World.CreateVehicle(m, st, heading);
        if (v == null || !v.Exists()) return false;
        Ped rider = v.CreateRandomPedOnSeat(VehicleSeat.Driver);
        if (rider == null || !rider.Exists()) { v.Delete(); return false; }
        v.IsPersistent = false;
        rider.IsPersistent = false;
        float s = CruiseSpeed();
        Function.Call((Hash)0x0E46A3FCBDE2A1B1, v, s + 1.5f);
        Function.Call((Hash)0xA731F608CA104E3C, rider, aggressiveness);
        Function.Call((Hash)0xB195FFA8042FC5C3, rider, 1.0f);
        rider.Task.CruiseWithVehicle(v, s, DrivingStyle.Normal);
        tuned.Add(v.Handle);
        mine.Add(v);
        nSpawned++;
        return true;
    }
}
