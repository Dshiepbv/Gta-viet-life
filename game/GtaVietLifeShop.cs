// GTA Việt Life - MODULE 5: CỬA HÀNG SÚNG / XE / MÁY BAY BÁN BẰNG GIỌNG NÓI (độc lập; mua bán xử lý ở GtaVietLife.cs).
// Chép vào "scripts" cùng GtaVietLifeShop.ini, bấm ] để nạp lại.
//
// Đặt các điểm bán cố định có chấm trên bản đồ, người bán đứng sẵn tại đó. Sếp lại gần, người bán chào, Sếp nói tên món
// ("cho tôi khẩu Pistol", "lấy chiếc Sultan") là mua: trừ tiền, súng vào tay, xe/máy bay xuất hiện ngay trước mặt.
// Giá = giá gốc Online x hệ số trong .ini. Giá gốc dưới đây là ƯỚC LƯỢNG của em, hệ số để cân với kinh tế game.
// Mẫu nào không có trong bản game của Sếp thì bị bỏ (xem log GtaVietLifeShop.log).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeShop : Script
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    const string LogFile = "GtaVietLifeShop.log";

    float wScale = 0.15f, vScale = 0.35f, aScale = 0.35f;     // hệ số nhân giá gốc Online
    bool enabled = true;

    // kind: W vũ khí, V xe, A máy bay/trực thăng. code = tên WEAPON_ hoặc tên mẫu xe. price = giá gốc Online ước lượng.
    class Item { public string Name, Kind, Code; public int Price; public Item(string n, string k, string c, int p) { Name = n; Kind = k; Code = c; Price = p; } }
    class Point { public string Name, Model; public Vector3 Pos; public float Head; public BlipSprite Sprite; public BlipColor Color; public Item[] Items; public Ped P; public Blip B; }

    static readonly Item[] Guns = {
        new Item("Pistol", "W", "WEAPON_PISTOL", 2500), new Item("Combat Pistol", "W", "WEAPON_COMBATPISTOL", 3500), new Item("AP Pistol", "W", "WEAPON_APPISTOL", 4500),
        new Item("Pistol .50", "W", "WEAPON_PISTOL50", 5500), new Item("Micro SMG", "W", "WEAPON_MICROSMG", 7000), new Item("SMG", "W", "WEAPON_SMG", 9000),
        new Item("Pump Shotgun", "W", "WEAPON_PUMPSHOTGUN", 6500), new Item("Assault Rifle", "W", "WEAPON_ASSAULTRIFLE", 17500), new Item("Carbine Rifle", "W", "WEAPON_CARBINERIFLE", 19500),
        new Item("Sniper Rifle", "W", "WEAPON_SNIPERRIFLE", 38000), new Item("Knife", "W", "WEAPON_KNIFE", 400), new Item("Baseball Bat", "W", "WEAPON_BAT", 300) };
    static readonly Item[] Cars = {
        new Item("Faggio", "V", "faggio", 4000), new Item("Vader", "V", "vader", 9000), new Item("Sanchez", "V", "sanchez", 8000), new Item("Bati 801", "V", "bati", 15000),
        new Item("Blista", "V", "blista", 15000), new Item("Futo", "V", "futo", 9000), new Item("Buffalo", "V", "buffalo", 35000), new Item("Sultan", "V", "sultan", 12000),
        new Item("Kuruma", "V", "kuruma", 95000), new Item("Banshee", "V", "banshee", 105000), new Item("Infernus", "V", "infernus", 440000), new Item("Zentorno", "V", "zentorno", 725000),
        new Item("Adder", "V", "adder", 1000000), new Item("Cognoscenti", "V", "cognoscenti", 254000) };
    static readonly Item[] Planes = {
        new Item("Dodo", "A", "dodo", 500000), new Item("Luxor", "A", "luxor", 1625000), new Item("Shamal", "A", "shamal", 1150000) };
    static readonly Item[] Helis = {
        new Item("Maverick", "A", "maverick", 780000), new Item("Frogger", "A", "frogger", 1300000), new Item("Buzzard", "A", "buzzard2", 1750000) };

    Point[] points;
    int nextTick, nextLog;
    readonly Dictionary<int, string> pub = new Dictionary<int, string>();

    public GtaVietLifeShop()
    {
        LoadCfg();
        points = new[] {
            new Point { Name = "Ammu-Nation Pillbox", Model = "s_m_y_ammucity_01", Pos = new Vector3(17.2f, -1107.9f, 29.8f), Head = 160f, Sprite = BlipSprite.AmmuNation, Color = BlipColor.Red, Items = Guns },
            new Point { Name = "Ammu-Nation Hawick", Model = "s_m_y_ammucity_01", Pos = new Vector3(251.6f, -49.4f, 69.9f), Head = 70f, Sprite = BlipSprite.AmmuNation, Color = BlipColor.Red, Items = Guns },
            new Point { Name = "Ammu-Nation Little Seoul", Model = "s_m_y_ammucity_01", Pos = new Vector3(-661.6f, -933.5f, 21.8f), Head = 180f, Sprite = BlipSprite.AmmuNation, Color = BlipColor.Red, Items = Guns },
            new Point { Name = "Salon xe Premium Deluxe", Model = "a_m_m_business_01", Pos = new Vector3(-47.0f, -1097.0f, 26.4f), Head = 20f, Sprite = BlipSprite.PersonalVehicleCar, Color = BlipColor.Blue, Items = Cars },
            new Point { Name = "San bay LSIA - may bay", Model = "s_m_m_pilot_01", Pos = new Vector3(-1269.0f, -3374.0f, 13.9f), Head = 330f, Sprite = BlipSprite.Plane, Color = BlipColor.Blue, Items = Planes },
            new Point { Name = "Bai truc thang Vespucci", Model = "s_m_m_pilot_02", Pos = new Vector3(-724.5f, -1444.0f, 5.0f), Head = 140f, Sprite = BlipSprite.Helicopter, Color = BlipColor.Blue, Items = Helis },
        };
        Tick += OnTick; Interval = 0;
        Log("khoi dong; " + points.Length + " diem ban");
    }

    float F(string key, float def)
    {
        string s = Settings.GetValue<string>("Shop", key, def.ToString(CI));
        float v; return float.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CI, out v) ? v : def;
    }
    void LoadCfg() { enabled = Settings.GetValue("Shop", "Enabled", true); wScale = F("WeaponScale", wScale); vScale = F("VehicleScale", vScale); aScale = F("AircraftScale", aScale); }
    void Log(string s) { try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\r\n"); } catch { } }

    string Menu(Point p)
    {
        var sb = new StringBuilder();
        foreach (Item it in p.Items)
        {
            float sc = it.Kind == "W" ? wScale : it.Kind == "V" ? vScale : aScale;
            int price = Math.Max(1, (int)Math.Round(it.Price * sc / 10.0) * 10);
            if (price < 1) price = 1;
            if (sb.Length > 0) sb.Append(';');
            sb.Append(it.Name).Append(':').Append(price).Append(":@").Append(it.Kind).Append(':').Append(it.Code);
        }
        return sb.ToString();
    }

    void OnTick(object sender, EventArgs e)
    {
        if (!enabled) return;
        int now = Game.GameTime; if (now < nextTick) return; nextTick = now + 2000;
        try
        {
            Ped me = Game.Player.Character; if (me == null || !me.Exists()) return;
            bool changed = false;
            foreach (Point p in points)
            {
                if (p.B == null || !p.B.Exists()) { p.B = World.CreateBlip(p.Pos); p.B.Sprite = p.Sprite; p.B.Color = p.Color; p.B.Name = p.Name; p.B.IsShortRange = true; }
                float d = p.Pos.DistanceTo(me.Position);
                if (d < 90f && (p.P == null || !p.P.Exists()))
                {
                    Model m = new Model(p.Model);
                    if (!m.IsInCdImage || !m.Request(500)) { Log("khong nap duoc " + p.Model); continue; }
                    p.P = World.CreatePed(m, p.Pos, p.Head);
                    if (p.P != null && p.P.Exists())
                    {
                        p.P.IsPersistent = true; p.P.BlockPermanentEvents = true;
                        p.P.Task.StartScenarioAtPosition("WORLD_HUMAN_STAND_IMPATIENT", p.Pos, p.Head, 0, false, true);
                        changed = true; Log("dat nguoi ban " + p.Name);
                    }
                }
                else if (d > 200f && p.P != null && p.P.Exists()) { p.P.Delete(); p.P = null; changed = true; }
            }
            if (changed) Publish();
            if (now > nextLog) { nextLog = now + 60000; }
        }
        catch (Exception ex) { Log("loi: " + ex.Message); }
    }

    void Publish()
    {
        pub.Clear();
        foreach (Point p in points) if (p.P != null && p.P.Exists()) pub[p.P.Handle] = p.Name + "|" + Menu(p);
        AppDomain.CurrentDomain.SetData("GVL.Shops", new Dictionary<int, string>(pub));
    }
}
