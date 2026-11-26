// GTA Việt Life - MODULE 4: XE ÔM / SHIPPER LÀM TỪNG BƯỚC NGOÀI ĐƯỜNG (độc lập, không có menu).
// Chép vào "scripts" cùng GtaVietLifeXeOm.ini, bấm ] để nạp lại.
//
// Phím F7 (đổi trong .ini): vòng chế độ  Tắt -> Xe ôm -> Shipper -> Tắt.  Phải ngồi trên xe máy.
//  XE ÔM : có chấm xanh trên bản đồ là khách đang đứng chờ ở vỉa hè. Chạy tới, dừng cạnh khách, khách tự leo lên yên sau
//          và nói điểm đến (có đường chỉ trên bản đồ). Chở tới nơi, dừng lại: khách xuống xe và trả tiền. Chạy êm, đúng giờ có tiền boa.
//  SHIPPER: có chấm cam là chỗ lấy hàng. Dừng ở đó, hàng được buộc sau xe. Chạy tới nhà người nhận (chấm xanh) và dừng cạnh người nhận.
//          Giao trễ bị trừ tiền công.
// Tiền công theo khoảng cách, nhận bằng tiền mặt qua cổng tiền chung nếu có. Ghi log ngắn ở GtaVietLifeXeOm.log.
// Giọng nói của khách: dùng cầu nối AI /say nếu đang chạy (không chạy thì chỉ hiện phụ đề).

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeXeOm : Script
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    const string LogFile = "GtaVietLifeXeOm.log";

    Keys modeKey = Keys.F7;
    string bridge = "http://127.0.0.1:5577";
    float farePerM = 0.008f;      // $ mỗi mét chở khách (xe ôm)
    int fareBase = 3;
    float shipPerM = 0.0125f;     // $ mỗi mét giao hàng (8$ + dist/80 theo bảng giá chung)
    int shipBase = 8;

    int mode = 0;                 // 0 tắt, 1 xe ôm, 2 shipper
    int step = 0;                 // xe ôm: 1 chờ đón, 2 đang lên xe, 3 đang chở. shipper: 1 chờ lấy hàng, 2 đang giao
    Ped cust; Blip bPick, bDest; Vehicle bike; Vector3 pickPos, destPos; Prop parcel;
    int stepT, deadline, stopSince, startT; float tripLen; float damageAtStart;
    readonly Random rnd = new Random();
    readonly string[] custModels = { "a_m_y_ktown_01", "a_f_y_ktown_01", "a_m_m_ktown_01", "a_f_m_ktown_01", "a_m_y_ktown_02", "a_f_y_ktown_02" };
    string lastErr = "";

    public GtaVietLifeXeOm()
    {
        try { modeKey = (Keys)Enum.Parse(typeof(Keys), Settings.GetValue<string>("XeOm", "Key", "F7")); } catch { }
        int port = Settings.GetValue("Bridge", "Port", 5577); bridge = "http://127.0.0.1:" + port;
        Tick += OnTick; KeyDown += OnKeyDown; Interval = 0;
        Log("khoi dong");
    }

    void Log(string s) { try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\r\n"); } catch { } }
    void Say(string who, string msg) { GTA.UI.Screen.ShowSubtitle("~y~" + who + ":~s~ " + msg, 5000); }

    void Speak(string text, bool female)
    {
        Task.Run(() =>
        {
            try
            {
                var c = new NameValueCollection(); c["key"] = "xeom_" + (cust != null ? cust.Handle : 0); c["gender"] = female ? "female" : "male"; c["pedtype"] = "4"; c["text"] = text;
                using (var wc = new WebClient()) wc.UploadValues(bridge + "/say", c);
            }
            catch { }
        });
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != modeKey) return;
        Ped me = Game.Player.Character;
        if (mode != 0 && step >= 2) { Notify("Dang co khach/hang tren xe, hoan thanh chuyen nay da."); return; }
        mode = (mode + 1) % 3;
        Cleanup(false);
        if (mode == 0) { Notify("Da nghi chay xe om/shipper."); return; }
        if (!me.IsInVehicle() || !me.CurrentVehicle.Model.IsBike) { Notify("~r~Phai ngoi tren xe may~s~ moi nhan chuyen."); mode = 0; return; }
        Notify(mode == 1 ? "~g~Xe om:~s~ cho chap xanh tren ban do (khach dang cho)." : "~o~Shipper:~s~ den cham cam lay hang.");
        NewJob(me);
    }

    void Notify(string m) { Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING"); Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, m); Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false); }

    static bool InView(Vector3 p)
    {
        Vector3 to = p - GameplayCamera.Position; float len = to.Length(); if (len < 1f) return true;
        Vector3 d = GameplayCamera.Direction; return (to.X * d.X + to.Y * d.Y + to.Z * d.Z) / len > 0.35f;
    }

    bool SafePavement(Vector3 raw, out Vector3 pos)
    {
        var o = new OutputArgument();
        bool ok = Function.Call<bool>(Hash.GET_SAFE_COORD_FOR_PED, raw.X, raw.Y, raw.Z, true, o, 16);
        pos = ok ? o.GetResult<Vector3>() : Vector3.Zero; return ok;
    }

    bool PickSpot(Vector3 around, float min, float max, bool needHidden, out Vector3 pos)
    {
        pos = Vector3.Zero;
        for (int i = 0; i < 16; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2; float d = min + (float)rnd.NextDouble() * (max - min);
            Vector3 raw = around + new Vector3((float)Math.Cos(a) * d, (float)Math.Sin(a) * d, 0f);
            Vector3 p;
            if (!SafePavement(raw, out p) || p == Vector3.Zero) continue;
            if (needHidden && InView(p)) continue;
            pos = p; return true;
        }
        return false;
    }

    Blip MakeBlip(Vector3 p, BlipSprite sp, BlipColor col, string name, bool route)
    {
        Blip b = World.CreateBlip(p); b.Sprite = sp; b.Color = col; b.Name = name; b.ShowRoute = route; return b;
    }

    void NewJob(Ped me)
    {
        Cleanup(false);
        bike = me.CurrentVehicle; startT = Game.GameTime; stopSince = 0;
        Vector3 pp = me.Position;
        if (!PickSpot(pp, 120f, 260f, true, out pickPos)) { Notify("Chua co viec, thu lai sau."); step = 0; mode = 0; return; }
        Model m = new Model(custModels[rnd.Next(custModels.Length)]);
        if (!m.IsInCdImage || !m.Request(800)) { Notify("Loi nap nguoi."); mode = 0; return; }
        if (mode == 1)
        {
            cust = World.CreatePed(m, pickPos);
            if (cust == null || !cust.Exists()) { mode = 0; return; }
            cust.IsPersistent = true; cust.BlockPermanentEvents = true;
            cust.Task.StartScenarioAtPosition("WORLD_HUMAN_STAND_IMPATIENT", pickPos, 0f, 0, false, true);
            bPick = MakeBlip(pickPos, BlipSprite.Standard, BlipColor.Green, "Khach xe om", true);
        }
        else
        {
            bPick = MakeBlip(pickPos, BlipSprite.Store, BlipColor.Orange, "Cho lay hang", true);
        }
        step = 1; stepT = Game.GameTime;
        Log("viec moi mode=" + mode + " tai " + (int)pickPos.X + "," + (int)pickPos.Y);
    }

    void Cleanup(bool keepCust)
    {
        if (bPick != null && bPick.Exists()) bPick.Delete(); bPick = null;
        if (bDest != null && bDest.Exists()) bDest.Delete(); bDest = null;
        if (parcel != null && parcel.Exists()) parcel.Delete(); parcel = null;
        if (cust != null && cust.Exists() && !keepCust) { cust.IsPersistent = false; cust.MarkAsNoLongerNeeded(); }
        if (!keepCust) cust = null;
        Function.Call(Hash.SET_WAYPOINT_OFF);
        step = 0;
    }

    void Pay(int amount, string note)
    {
        if (amount <= 0) return;
        var f = AppDomain.CurrentDomain.GetData("GVL.EarnCash") as Action<int, string, string>;
        if (f != null) { try { f(amount, "delivery", note); return; } catch { } }
        Game.Player.Money = Game.Player.Money + amount;
    }

    void OnTick(object sender, EventArgs e)
    {
        if (mode == 0 || step == 0) return;
        try
        {
            Ped me = Game.Player.Character; int now = Game.GameTime;
            if (me == null || !me.Exists()) return;
            bool onBike = me.IsInVehicle() && me.CurrentVehicle.Model.IsBike;
            if (step >= 2 && !onBike && now - stepT > 45000) { Notify("~r~Bo chuyen~s~ (roi xe qua lau)."); Cleanup(false); mode = 0; return; }
            if (onBike) bike = me.CurrentVehicle;
            if (bike == null || !bike.Exists()) return;
            float speed = bike.Speed;
            bool stopped = speed < 1.5f;
            if (stopped) { if (stopSince == 0) stopSince = now; } else stopSince = 0;
            bool heldStop = stopSince != 0 && now - stopSince > 1200;

            if (mode == 1) TickOm(me, now, heldStop, speed); else TickShip(me, now, heldStop);
        }
        catch (Exception ex) { lastErr = ex.Message; Log("loi: " + ex.Message); }
    }

    // ---------------------------------------------------------------- XE ÔM
    void TickOm(Ped me, int now, bool heldStop, float speed)
    {
        if (cust == null || !cust.Exists() || cust.IsDead) { Notify("Khach bien mat."); Cleanup(false); NewJob(me); return; }
        if (step == 1)
        {
            if (bike.Position.DistanceTo(cust.Position) < 9f && heldStop)
            {
                step = 2; stepT = now;
                Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, cust, true);
                cust.Task.ClearAll();
                cust.Task.EnterVehicle(bike, VehicleSeat.Passenger, 8000, 2f);
                Say("Khach", "Anh ơi, chở em với!"); Speak("Anh ơi, chở em với!", cust.Gender == Gender.Female);
            }
            else if (now - stepT > 240000) { Notify("Khach di mat roi, tim khach khac."); Cleanup(false); NewJob(me); }
        }
        else if (step == 2)
        {
            if (cust.IsInVehicle(bike))
            {
                if (!PickSpot(bike.Position, 700f, 1800f, false, out destPos)) { Notify("Khong co diem den."); Cleanup(false); return; }
                tripLen = bike.Position.DistanceTo(destPos);
                string street = World.GetStreetName(destPos);
                bDest = MakeBlip(destPos, BlipSprite.Waypoint, BlipColor.Green, "Diem den cua khach", true);
                Function.Call(Hash.SET_NEW_WAYPOINT, destPos.X, destPos.Y);
                if (bPick != null && bPick.Exists()) { bPick.Delete(); bPick = null; }
                deadline = now + (int)(tripLen / 7f * 1000f) + 60000; damageAtStart = bike.Health; step = 3; stepT = now;
                string msg = "Chở em tới " + (string.IsNullOrEmpty(street) ? "chỗ đã đánh dấu" : "đường " + street) + " nha anh.";
                Say("Khach", msg); Speak(msg, cust.Gender == Gender.Female);
                Log("don khach, cho " + (int)tripLen + " m");
            }
            else if (now - stepT > 12000) { Notify("Khach khong len duoc xe, bo."); Cleanup(false); NewJob(me); }
        }
        else if (step == 3)
        {
            if (!cust.IsInVehicle(bike)) { Notify("~r~Khach roi xe giua duong.~s~"); Cleanup(false); NewJob(me); return; }
            if (bike.Position.DistanceTo(destPos) < 20f && heldStop)
            {
                int fare = fareBase + (int)(tripLen * farePerM);
                bool onTime = now < deadline; bool smooth = bike.Health >= damageAtStart - 40f;
                int tip = (onTime ? 2 : 0) + (smooth ? 2 : 0);
                cust.Task.LeaveVehicle(bike, false);
                Pay(fare + tip, "xe om");
                Notify("Xong chuyen: ~g~+$" + (fare + tip) + "~s~" + (tip > 0 ? " (boa $" + tip + ")" : ""));
                string bye = tip > 0 ? "Cảm ơn anh, chạy êm quá, em boa thêm nha!" : "Cảm ơn anh nha.";
                Say("Khach", bye); Speak(bye, cust.Gender == Gender.Female);
                Log("xong chuyen xe om " + (int)tripLen + " m, tien " + (fare + tip));
                Ped c = cust; Cleanup(true); cust = null; c.IsPersistent = false; c.Task.WanderAround(); c.MarkAsNoLongerNeeded();
                NewJob(me);
            }
        }
    }

    // ---------------------------------------------------------------- SHIPPER
    void TickShip(Ped me, int now, bool heldStop)
    {
        if (step == 1)
        {
            if (bike.Position.DistanceTo(pickPos) < 8f && heldStop)
            {
                Model pm = new Model("prop_cs_package_01");
                if (pm.IsInCdImage && pm.Request(500))
                {
                    parcel = World.CreateProp(pm, bike.Position, false, false);
                    if (parcel != null && parcel.Exists()) Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, parcel, bike, Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, bike, "chassis"), 0f, -0.55f, 0.35f, 0f, 0f, 0f, false, false, false, false, 2, true);
                }
                if (!PickSpot(bike.Position, 600f, 1600f, false, out destPos)) { Notify("Khong co diem giao."); Cleanup(false); return; }
                tripLen = bike.Position.DistanceTo(destPos);
                if (bPick != null && bPick.Exists()) { bPick.Delete(); bPick = null; }
                bDest = MakeBlip(destPos, BlipSprite.Waypoint, BlipColor.Green, "Giao hang", true);
                Function.Call(Hash.SET_NEW_WAYPOINT, destPos.X, destPos.Y);
                deadline = now + (int)(tripLen / 6.5f * 1000f) + 60000; step = 2; stepT = now;
                Notify("Da lay hang. Giao toi diem xanh, " + (deadline - now) / 1000 + " giay.");
                Log("lay hang, giao " + (int)tripLen + " m");
            }
        }
        else if (step == 2)
        {
            if (cust == null && bike.Position.DistanceTo(destPos) < 140f)
            {
                Model m = new Model(custModels[rnd.Next(custModels.Length)]);
                if (m.IsInCdImage && m.Request(500))
                {
                    cust = World.CreatePed(m, destPos);
                    if (cust != null && cust.Exists()) { cust.IsPersistent = true; cust.BlockPermanentEvents = true; cust.Task.StartScenarioAtPosition("WORLD_HUMAN_STAND_IMPATIENT", destPos, 0f, 0, false, true); }
                }
            }
            if (bike.Position.DistanceTo(destPos) < 12f && heldStop)
            {
                int pay = shipBase + (int)(tripLen * shipPerM);
                bool late = now > deadline;
                if (late) pay = pay / 2;
                Pay(pay, "giao hang");
                Notify((late ? "Giao tre, bi tru: " : "Giao xong: ") + "~g~+$" + pay + "~s~");
                if (cust != null && cust.Exists())
                {
                    string t = late ? "Sao giờ mới tới vậy anh? Thôi đưa em đây." : "Cảm ơn anh, đúng đồ rồi!";
                    Say("Nguoi nhan", t); Speak(t, cust.Gender == Gender.Female);
                    cust.Task.LookAt(me, 3000);
                }
                Log("giao xong " + (int)tripLen + " m, tien " + pay + (late ? " (tre)" : ""));
                Ped c = cust; Cleanup(true); cust = null;
                if (c != null && c.Exists()) { c.IsPersistent = false; c.MarkAsNoLongerNeeded(); }
                NewJob(me);
            }
        }
    }
}
