// GTA Viet Life - UIS: cua nha, noi that dung chung, vung stream quanh nguoi choi (GTA V Legacy, SHVDN v3).
// Noi that goc nam o toa do co dinh: nhieu nha dung chung mot mau, tai mot thoi diem chi mot nha "so huu" mau do.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeInteriors : Script
{
    const string SaveFile = "GtaVietLifeInteriors.sav";
    readonly GvlSaveFile store = new GvlSaveFile(SaveFile, delegate(List<string> lines) { return lines.Count > 1 && lines[0] == "version=1" && lines[lines.Count - 1] == "end=1"; }, Log);
    const string BuildingPath = "building.uis.";
    const float PromptRange = 1.8f;
    const float ScanRange = 45f;
    const int VisitorCapacity = 3;

    sealed class Template
    {
        public readonly string Name, Ipl; public readonly Vector3 Spawn; public readonly int Tier; public readonly bool Shell;
        public Template(string name, float x, float y, float z, string ipl, int tier, bool shell)
        { Name = name; Spawn = new Vector3(x, y, z); Ipl = ipl; Tier = tier; Shell = shell; }
    }

    // Toa do va ten IPL lay tu du an bob74_ipl (giay phep MIT, (c) 2024 Bob74). Chua kiem trong game:
    // mau nao khong nap duoc hai lan thi tu bi loai. Tier: 1 < 35 m2, 2 35-90, 3 90-180, 4 > 180.
    // Shell = vo rong (kho), dung de ghep do (Modular) cho quan; nha o dung noi that co san.
    static readonly Template[] Templates = {
        new Template("Phong tro binh dan", 261.4586f, -998.8196f, -99.0086f, "", 1, false),
        new Template("Nha xe moc Sandy", 1973.0f, 3815.0f, 33.43f, "", 1, false),
        new Template("Can ho trung binh", 347.2686f, -999.2955f, -99.1962f, "", 2, false),
        new Template("Can ho cao cap 1", -35.3128f, -580.4199f, 88.7122f, "", 3, false),
        new Template("Can ho cao cap 2", -1477.14f, -538.7499f, 55.5264f, "", 3, false),
        new Template("Chung cu A", -1462.281f, -539.6276f, 72.4443f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo_", 3, false),
        new Template("Chung cu B", -914.9026f, -374.8731f, 112.6748f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__1", 3, false),
        new Template("Chung cu C", -609.5669f, 51.2821f, 96.6002f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__2", 3, false),
        new Template("Chung cu D", -778.5061f, 331.316f, 210.3972f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__3", 3, false),
        new Template("Chung cu E", -22.6135f, -590.1432f, 78.4309f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__4", 3, false),
        new Template("Biet thu 1", -169.286f, 486.4938f, 137.4436f, "", 4, false),
        new Template("Biet thu 2", 340.9412f, 437.1798f, 149.3925f, "", 4, false),
        new Template("Biet thu 3", 373.023f, 416.105f, 145.7006f, "", 4, false),
        new Template("Biet thu 4", -676.127f, 588.612f, 145.1698f, "", 4, false),
        new Template("Biet thu 5", -763.107f, 615.906f, 144.1401f, "", 4, false),
        new Template("Biet thu 6", -857.798f, 682.563f, 152.6529f, "", 4, false),
        new Template("Biet thu 7", 120.5f, 549.952f, 184.097f, "", 4, false),
        new Template("Biet thu 8", -1288f, 440.748f, 97.6946f, "", 4, false),
        new Template("Penthouse hien dai", -787.7805f, 334.9232f, 215.8384f, "apa_v_mp_h_01_a", 4, false),
        new Template("Penthouse sang trong", -773.2258f, 322.8252f, 194.8862f, "apa_v_mp_h_01_b", 4, false),
        new Template("Penthouse co dien", -787.7805f, 334.9232f, 186.1134f, "apa_v_mp_h_01_c", 4, false),
        new Template("Nha Michael", -802.3f, 175.0f, 72.8f, "", 4, false),
        new Template("Nha Franklin", 7.1197f, 536.6153f, 176.028f, "", 4, false),
        new Template("Ki-ot (kho nho)", 1094.988f, -3101.776f, -39.0f, "ex_exec_warehouse_placement_interior_1_int_warehouse_s_dlc_milo", 1, true),
        new Template("Cua hang vua (kho nho)", 1094.988f, -3101.776f, -39.0f, "ex_exec_warehouse_placement_interior_1_int_warehouse_s_dlc_milo", 2, true),
        new Template("Quan lon (kho vua)", 1056.486f, -3105.724f, -39.0f, "ex_exec_warehouse_placement_interior_0_int_warehouse_m_dlc_milo", 3, true),
        new Template("Sieu thi (kho lon)", 1006.967f, -3102.079f, -39.0f, "ex_exec_warehouse_placement_interior_2_int_warehouse_l_dlc_milo", 4, true) };

    static int[] TemplateTiers() { int[] t = new int[Templates.Length]; for (int i = 0; i < t.Length; i++) t[i] = Templates[i].Tier; return t; }
    static bool[] TemplateShells() { bool[] s = new bool[Templates.Length]; for (int i = 0; i < s.Length; i++) s[i] = Templates[i].Shell; return s; }

    // Cua da co san trong du an (nha thue cua module Doi song).
    static readonly float[][] SeedDoors = {
        new float[] { 1972.7f, 3817.0f, 33.4f }, new float[] { -14.8689f, -1441.1822f, 31.1932f },
        new float[] { 7.5139f, 539.5268f, 176.2028f }, new float[] { -816.4f, 178.3f, 72.8274f } };

    // Diem nghiem thu (phim F7 di lan luot). Z = 0: do mat dat khi toi noi.
    sealed class TestPoint
    {
        public readonly string Name; public readonly Vector3 Pos;
        public TestPoint(string name, float x, float y, float z) { Name = name; Pos = new Vector3(x, y, z); }
    }

    static readonly TestPoint[] TestPoints = {
        new TestPoint("Cua hang 24/7 Strawberry (di bo vao)", 24.5f, -1346.9f, 0f),
        new TestPoint("Ammu-Nation Pillbox Hill (di bo vao)", 19.1f, -1110.0f, 29.8f),
        new TestPoint("Nha Strawberry (cua portal)", -14.8689f, -1441.1822f, 31.1932f),
        new TestPoint("Nha Vinewood Hills (cua portal)", 7.5139f, 539.5268f, 176.2028f),
        new TestPoint("Trailer Sandy Shores (cua portal)", 1972.7f, 3817.0f, 33.4f) };

    sealed class Anchor { public GvlObjectKind Kind; public Vector3 Pos; public float Heading; public Prop Spawned; public bool Bought; }

    static Dictionary<int, GvlObjectKind> kindByHash;
    static Dictionary<int, GvlObjectKind> KindByHash()
    {
        if (kindByHash != null) return kindByHash;
        kindByHash = new Dictionary<int, GvlObjectKind>();
        foreach (GvlObjectKind kind in GvlObjects.Kinds)
            foreach (string model in kind.Models) kindByHash[Game.GenerateHash(model)] = kind;
        return kindByHash;
    }

    sealed class Member { public Ped P; public Anchor Spot; public int Stage, Until; }

    sealed class Visitor
    {
        public Ped P; public string Building; public int Stage, Until; public Vector3 Door; public Anchor Spot;
    }

    readonly GvlRegistry registry = new GvlRegistry();
    readonly GvlBubble bubble = new GvlBubble();
    readonly GvlPortal portal = new GvlPortal();
    readonly GvlTemplateHealth health = new GvlTemplateHealth();
    readonly GvlOccupancy occupancy = new GvlOccupancy(VisitorCapacity);
    readonly GvlStuck stuck = new GvlStuck();
    readonly List<Anchor> anchors = new List<Anchor>();
    readonly List<Visitor> visitors = new List<Visitor>();
    readonly Dictionary<int, int> visitCooldown = new Dictionary<int, int>();
    readonly HashSet<string> registeredWithEngine = new HashSet<string>();
    readonly Dictionary<int, bool> doorModel = new Dictionary<int, bool>();

    bool enabled = true, autoDiscover = true, showMarkers = true, dirty, furnishPending, mpMapsAsked;
    readonly Dictionary<string, List<GvlPlacement>> owned = new Dictionary<string, List<GvlPlacement>>();
    readonly List<Member> household = new List<Member>();
    Keys cycleKey = Keys.F5, buyKey = Keys.F6;
    int buyIndex, nextMeasure, nextHousehold;
    readonly GvlPrepare prepare = new GvlPrepare();
    int movedAt;                 // luc man hinh toi; 0 khi khong dang chuyen cho
    Keys useKey = Keys.H, teleportKey = Keys.F7;
    int testIndex = -1;
    string building = "";
    readonly HashSet<int> tried = new HashSet<int>();
    int template = -1;
    Vector3 returnPos, travelTarget;
    float returnHeading;
    int nextBubble, nextScan, nextSave, nextVisit, keyCooldown;
    Anchor activity; int activityStage, activityUntil;

    public GtaVietLifeInteriors()
    {
        enabled = Settings.GetValue("Interiors", "Enabled", true);
        autoDiscover = Settings.GetValue("Interiors", "AutoDiscoverDoors", true);
        showMarkers = Settings.GetValue("Interiors", "ShowMarkers", true);
        cycleKey = Settings.GetValue("Keys", "CycleFurniture", cycleKey);
        buyKey = Settings.GetValue("Keys", "BuyFurniture", buyKey);
        useKey = Settings.GetValue("Keys", "Interior", useKey);
        teleportKey = Settings.GetValue("Keys", "TestTeleport", teleportKey);
        Load();
        foreach (float[] door in SeedDoors) registry.Add(door[0], door[1], door[2], "seed");
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
        Log("san sang: " + registry.Count + " cua, " + Templates.Length + " mau noi that");
    }

    void OnTick(object sender, EventArgs e)
    {
        if (!enabled) return;
        try
        {
            Ped me = Game.Player.Character;
            if (me == null || !me.Exists()) return;
            int now = Game.GameTime;
            if (!mpMapsAsked)
            {
                // Biet thu, penthouse, can ho cao cap la noi that cua ban do Online: phai nap ban do do vao Story thi moi vao duoc.
                mpMapsAsked = true;
                if (Settings.GetValue("Interiors", "LoadOnlineInteriors", true)) { Function.Call(Hash.ON_ENTER_MP); Log("da nap ban do noi that Online cho che do Story"); }
            }
            if (me.IsDead) { if (portal.Phase != GvlPortal.Outside || building.Length > 0) LeaveEverything(false); return; }

            StepPrepare(me, now);
            StepPortal(me, now);
            // Luoi an toan: man hinh toi qua lau thi dua nguoi choi ve cho cu, bat ke chuyen gi hong.
            if (movedAt > 0 && now - movedAt > GvlPortal.BlackScreenLimitMs) Rescue(me);
            if (portal.Phase == GvlPortal.Inside)
            {
                // Bi dich chuyen ra ngoai (nhiem vu, trainer, chet): don dep, khong keo nguoi choi ve.
                if (template >= 0 && me.Position.DistanceTo(Templates[template].Spawn) > 80f) LeaveEverything(false);
                else if (furnishPending) Furnish(me);
            }
            if (now > nextBubble) { nextBubble = now + 500; UpdateBubble(me); }
            if (autoDiscover && portal.Phase == GvlPortal.Outside && now > nextScan) { nextScan = now + 1500; DiscoverDoors(me); DiscoverFacades(me); }
            if (portal.Phase == GvlPortal.Outside && now > nextMeasure) { nextMeasure = now + 1000; MeasureNearby(me); }
            if (portal.Phase == GvlPortal.Inside && now > nextHousehold) { nextHousehold = now + 1500; StepHousehold(me, now); }
            StepActivity(me, now);
            if (now > nextVisit) { nextVisit = now + 10000; StartVisit(now); }
            StepVisitors(me, now);
            DrawPrompts(me);
            if (dirty && now > nextSave) { nextSave = now + 15000; Save(); }
        }
        catch (Exception ex) { Log("tick loi: " + ex.Message); }
    }

    // ------------------------------------------------------------ vung stream 120 m / 150 m
    void UpdateBubble(Ped me)
    {
        // Khi dang o trong, vung quet tinh tu cua ngoai de NPC va catalog quanh nha khong bi go.
        Vector3 center = portal.Phase == GvlPortal.Outside ? me.Position : returnPos;
        bubble.Update(registry, center.X, center.Y, center.Z);
        RegisterWithEngine();
    }

    void RegisterWithEngine()
    {
        object connected = AppDomain.CurrentDomain.GetData("GVL.Engine.Connected");
        Action<string, string> post = AppDomain.CurrentDomain.GetData("GVL.Engine.WorldPost") as Action<string, string>;
        if (!(connected is bool) || !(bool)connected || post == null) return;
        int sent = 0;
        foreach (string id in bubble.Active)
        {
            if (sent >= 5) break;
            GvlEntrance e = registry.Find(id);
            if (e == null || e.Tier == 0 || !registeredWithEngine.Add(id)) continue;   // chi gui toa nha da do
            string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
                "\",\"building_id\":\"" + BuildingPath + id + "\",\"category\":\"residential\",\"name\":\"Nha " + id +
                "\",\"adapter_maps\":{\"gta5.shvdn\":{\"entrance\":\"" + id + ".door\",\"interior\":\"" + id +
                ".interior\"}},\"interactions\":[\"enter\",\"exit\",\"inspect\"],\"attributes\":{\"tier\":" + e.Tier +
                ",\"purpose\":\"" + (e.Purpose == GvlTier.Shop ? "shop" : "home") + "\",\"area_m2\":" +
                ((int)e.Area).ToString(CultureInfo.InvariantCulture) + ",\"zone\":\"" + (e.Zone ?? "") + "\"}}";
            post("/engine/v1/world/buildings", body);
            sent++;
        }
    }

    // ------------------------------------------------------------ do cua
    bool IsDoorModel(Prop prop)
    {
        int hash = prop.Model.Hash;
        bool result;
        if (doorModel.TryGetValue(hash, out result)) return result;
        OutputArgument low = new OutputArgument(), high = new OutputArgument();
        Function.Call(Hash.GET_MODEL_DIMENSIONS, hash, low, high);
        Vector3 min = low.GetResult<Vector3>(), max = high.GetResult<Vector3>();
        result = GvlRegistry.LooksLikeDoor(max.X - min.X, max.Y - min.Y, max.Z - min.Z);
        doorModel[hash] = result;
        return result;
    }

    void DiscoverDoors(Ped me)
    {
        if (me.IsInVehicle() || InteriorOf(me) != 0) return;
        int added = 0;
        foreach (Prop prop in World.GetNearbyProps(me.Position, ScanRange))
        {
            if (added >= 8 || registry.Count >= GvlRegistry.Capacity) break;
            if (prop == null || !prop.Exists() || !IsDoorModel(prop)) continue;
            Vector3 p = prop.Position;
            if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, p.X, p.Y, p.Z) != 0) continue;
            if (!FacesStreet(p)) continue;   // chi cua mat pho hai ben duong
            if (registry.Add(p.X, p.Y, p.Z, "scan")) { added++; dirty = true; }
        }
    }

    // Quet hai ben va phia truoc: tuong toa nha co mai, gan duong, chua co loi vao trong 18 m thi dat mot loi vao.
    void DiscoverFacades(Ped me)
    {
        if (me.IsInVehicle() || InteriorOf(me) != 0 || registry.Count >= GvlRegistry.Capacity) return;
        Vector3 from = me.Position + new Vector3(0f, 0f, 0.3f);
        Vector3[] dirs = { me.RightVector, -me.RightVector, me.ForwardVector };
        foreach (Vector3 d in dirs)
        {
            Vector3 dir = new Vector3(d.X, d.Y, 0f);
            if (dir.Length() < 0.1f) continue;
            dir.Normalize();
            RaycastResult hit = World.Raycast(from, from + dir * GvlFacade.Reach, IntersectFlags.Map, me);
            if (!hit.DidHit) continue;
            Vector3 n = hit.SurfaceNormal;
            Vector3 flat = new Vector3(n.X, n.Y, 0f);
            if (flat.Length() < 0.1f) continue;
            flat.Normalize();
            Vector3 door = hit.HitPosition + flat * 0.8f;
            float ground = World.GetGroundHeight(new Vector3(door.X, door.Y, from.Z + 1f));
            door.Z = ground > 0f && Math.Abs(ground - from.Z) < 4f ? ground : from.Z - 1f;
            float roof;
            bool roofBehind = RoofAbove(hit.HitPosition - flat * 2f, door.Z, me, out roof);
            int nearby = registry.Within(door.X, door.Y, door.Z, GvlFacade.Gap).Count;
            if (!GvlFacade.Accept(n.Z, hit.HitPosition.DistanceTo(from), nearby, roofBehind, roofBehind && nearby == 0 && FacesStreet(door))) continue;
            if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, door.X, door.Y, door.Z) != 0) continue;
            if (registry.Add(door.X, door.Y, door.Z, "facade")) { dirty = true; Log("loi vao mat tien moi " + GvlRegistry.MakeId(door.X, door.Y, door.Z)); return; }
        }
    }

    static bool FacesStreet(Vector3 p)
    {
        OutputArgument node = new OutputArgument();
        if (!Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE, p.X, p.Y, p.Z, node, 1, 3f, 0f)) return false;
        return node.GetResult<Vector3>().DistanceTo2D(p) < 30f;
    }

    void MeasureNearby(Ped me)
    {
        foreach (GvlEntrance e in registry.Within(me.Position.X, me.Position.Y, me.Position.Z, 40f))
            if (e.Tier == 0) { Measure(e, me); return; }   // moi giay mot toa nha
    }

    // Do mat bang bang tia tu tren xuong: o nao co mai cao hon cua 2,4 m la thuoc toa nha.
    void Measure(GvlEntrance e, Ped me)
    {
        Vector3 door = new Vector3(e.X, e.Y, e.Z);
        Vector3 start = Vector3.Zero;
        bool found = false;
        float height;
        for (int i = 0; i < 8 && !found; i++)
        {
            double angle = i * Math.PI / 4.0;
            Vector3 c = door + new Vector3((float)Math.Cos(angle) * 2.5f, (float)Math.Sin(angle) * 2.5f, 0f);
            if (RoofAbove(c, door.Z, me, out height)) { start = c; found = true; }
        }
        float area = 20f, top = 0f;
        if (!found)
        {
            // Khong co mai quanh "cua": cot den, bien bao... khong phai toa nha, an di.
            e.Tier = -1; dirty = true;
            Log("bo loi vao " + e.Id + ": khong co mai, khong phai toa nha");
            return;
        }
        if (found)
        {
            int cells = GvlFootprint.Cells(delegate(int ix, int iy)
            {
                float h;
                bool covered = RoofAbove(start + new Vector3(ix * GvlFootprint.Cell, iy * GvlFootprint.Cell, 0f), door.Z, me, out h);
                if (covered && h > top) top = h;
                return covered;
            });
            area = GvlFootprint.Area(cells);
        }
        string zone = Function.Call<string>(Hash.GET_NAME_OF_ZONE, e.X, e.Y, e.Z) ?? "";
        e.Area = area; e.Zone = zone;
        e.Tier = GvlTier.Classify(area, top, zone);
        e.Purpose = e.Source == "seed" ? GvlTier.Home : GvlTier.Purpose(e.Id, zone, e.Tier);
        dirty = true;
        registeredWithEngine.Remove(e.Id);
        Log("do toa nha " + e.Id + ": " + area.ToString("0", CultureInfo.InvariantCulture) + " m2, cao " +
            top.ToString("0", CultureInfo.InvariantCulture) + " m, vung " + zone + ", tier " + e.Tier + ", " +
            (e.Purpose == GvlTier.Shop ? "quan" : "nha"));
    }

    static bool RoofAbove(Vector3 c, float doorZ, Ped me, out float height)
    {
        RaycastResult hit = World.Raycast(new Vector3(c.X, c.Y, doorZ + 60f), new Vector3(c.X, c.Y, doorZ - 1f), IntersectFlags.Map, me);
        height = hit.DidHit ? hit.HitPosition.Z - doorZ : 0f;
        return hit.DidHit && height > 2.4f;
    }

    // ------------------------------------------------------------ cua vao / cua ra
    static int InteriorOf(Ped ped) { return Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, ped); }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!enabled || (e.KeyCode != useKey && e.KeyCode != teleportKey && e.KeyCode != cycleKey && e.KeyCode != buyKey) ||
            Game.GameTime < keyCooldown) return;
        keyCooldown = Game.GameTime + 600;
        try
        {
            Ped me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead || me.IsInVehicle() || OtherMenuOpen() || prepare.Active) return;
            if (e.KeyCode == teleportKey) { MarkTestPoint(); return; }
            if (portal.Phase == GvlPortal.Outside && e.KeyCode != useKey) return;
            if (portal.Phase == GvlPortal.Outside)
            {
                if (e.Shift) { MarkDoor(me); return; }
                GvlEntrance door = registry.Nearest(me.Position.X, me.Position.Y, me.Position.Z, PromptRange);
                if (door != null && door.Tier >= 0) BeginEnter(me, door);
            }
            else if (portal.Phase == GvlPortal.Inside && template >= 0)
            {
                if (e.KeyCode == cycleKey) { buyIndex = (buyIndex + 1) % GvlObjects.Buyable.Length; ShowBuyChoice(); return; }
                if (e.KeyCode == buyKey) { BuyOrSell(me); return; }
                if (e.KeyCode != useKey) return;
                if (activity != null) { StopActivity(me); return; }
                if (me.Position.DistanceTo(Templates[template].Spawn) < PromptRange) { BeginExit(me); return; }
                Anchor near = NearestAnchor(me.Position, 2.2f);
                if (near != null) BeginActivity(me, near);
            }
        }
        catch (Exception ex) { Log("phim loi: " + ex.Message); }
    }

    static bool OtherMenuOpen()
    {
        foreach (string key in new[] { "GVL.WorldMenu", "GVL.PersonalMenu", "GVL.EconMenu", "GVL.LifeMenu" })
        {
            object value = AppDomain.CurrentDomain.GetData(key);
            if (value is bool && (bool)value) return true;
        }
        return false;
    }

    void MarkDoor(Ped me)
    {
        if (InteriorOf(me) != 0) { Notify("Dung ben ngoai, quay mat vao cua roi bam lai."); return; }
        Vector3 p = me.Position + me.ForwardVector * 0.8f;
        if (registry.Add(p.X, p.Y, p.Z, "player")) { dirty = true; Save(); Notify("Da ghi cua moi. Bam ~y~" + useKey + "~s~ de vao."); }
        else Notify("Cho nay da co cua.");
    }

    // Khong dich chuyen nguoi choi toi diem thu nua (de roi xuyen ban do): chi danh dau tren ban do.
    void MarkTestPoint()
    {
        testIndex = (testIndex + 1) % TestPoints.Length;
        TestPoint point = TestPoints[testIndex];
        Function.Call(Hash.SET_NEW_WAYPOINT, point.Pos.X, point.Pos.Y);
        Notify("~g~Diem " + (testIndex + 1) + "/" + TestPoints.Length + "~s~: " + point.Name + ". Da danh dau tren ban do.");
    }

    void BeginEnter(Ped me, GvlEntrance door)
    {
        if (Game.Player.WantedLevel > 0) { Notify("Dang bi truy na, chua vao nha duoc."); return; }
        if (door.Tier == 0) Measure(door, me);
        tried.Clear();
        int pick = ChooseTemplate(door, tried);
        if (pick < 0) { Notify("Khong co mau noi that nao hop voi toa nha nay."); return; }
        building = door.Id; template = pick;
        returnPos = me.Position; returnHeading = me.Heading;
        StartPrepare(Game.GameTime);
        Notify("Dang mo cua...");
    }

    // Nap noi that TRUOC khi dua nguoi choi toi: man hinh khong toi va khong roi neu noi that hong.
    void StartPrepare(int now)
    {
        if (Templates[template].Ipl.Length > 0) Function.Call(Hash.REQUEST_IPL, Templates[template].Ipl);
        prepare.Begin(now);
    }

    void StepPrepare(Ped me, int now)
    {
        if (!prepare.Active) return;
        // Nguoi choi di xa cua hoac len xe thi thoi.
        if (me.IsInVehicle() || me.Position.DistanceTo(returnPos) > 6f) { prepare.Cancel(); building = ""; template = -1; return; }
        Vector3 spawn = Templates[template].Spawn;
        int interior = Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, spawn.X, spawn.Y, spawn.Z);
        bool ready = false;
        if (interior != 0)
        {
            Function.Call(Hash.PIN_INTERIOR_IN_MEMORY, interior);
            ready = Function.Call<bool>(Hash.IS_INTERIOR_READY, interior);
        }
        int outcome = prepare.Step(now, ready);
        if (outcome == GvlPortal.Wait) return;
        if (outcome == GvlPortal.Done)
        {
            returnPos = me.Position; returnHeading = me.Heading;
            Travel(me, spawn);
            portal.BeginEnter(now);
            Save();   // ghi diem quay ve truoc khi roi mat dat
            return;
        }
        health.Failed(template);
        tried.Add(template);
        dirty = true;
        Log("mau '" + Templates[template].Name + "' chua san sang, bo qua cho " + building);
        int next = NextTemplate();
        if (next >= 0) { template = next; StartPrepare(now); return; }
        Log("khong nap duoc noi that nao cho " + building);
        Notify("~r~Cua nay chua vao duoc:~s~ khong noi that nao nap duoc.");
        building = ""; template = -1;
    }

    bool GroundBelow(Ped me)
    {
        Vector3 p = me.Position;
        return World.Raycast(p + new Vector3(0f, 0f, 0.5f), p - new Vector3(0f, 0f, 3f),
            IntersectFlags.Map | IntersectFlags.Objects, me).DidHit;
    }

    void Rescue(Ped me)
    {
        Log("chuyen cho qua lau, dua nguoi choi ve cua");
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, returnPos.X, returnPos.Y, returnPos.Z, false, false, false);
        prepare.Cancel();
        portal.Reset();
        Release(me);
        building = ""; template = -1; dirty = true; Save();
        Notify("~y~Khong chuyen duoc cho,~s~ da dua ve cua.");
    }

    // Cung Tier hoac nho hon, khong bao gio lon hon: nha nho khong mo ra biet thu.
    int ChooseTemplate(GvlEntrance door, HashSet<int> skip)
    {
        int[] tiers = TemplateTiers(); bool[] shells = TemplateShells();
        int top = door.Tier > 0 ? door.Tier : 1;
        foreach (int purpose in door.Purpose == GvlTier.Shop ? new[] { GvlTier.Shop, GvlTier.Home } : new[] { GvlTier.Home })
            for (int tier = top; tier >= 1; tier--)
            {
                int pick = GvlInteriorChoice.Choose(door.Id, tier, purpose, tiers, shells, health, skip);
                if (pick >= 0) return pick;
            }
        return -1;
    }

    int NextTemplate()
    {
        GvlEntrance door = registry.Find(building);
        return door == null ? -1 : ChooseTemplate(door, tried);
    }

    void BeginExit(Ped me)
    {
        EndHousehold();
        MoveVisitorsOutside();
        ClearAnchors();
        Travel(me, returnPos);
        portal.BeginExit(Game.GameTime);
    }

    void Travel(Ped me, Vector3 target)
    {
        travelTarget = target;
        movedAt = Game.GameTime;
        GTA.UI.Screen.FadeOut(150);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, me, true);
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, target.X, target.Y, target.Z, false, false, false);
        Function.Call(Hash.SET_FOCUS_POS_AND_VEL, target.X, target.Y, target.Z, 0f, 0f, 0f);
    }

    // Giu nguoi choi dung yen cho toi khi san va tuong quanh nhan vat da nap xong.
    void StepPortal(Ped me, int now)
    {
        if (portal.Phase != GvlPortal.Entering && portal.Phase != GvlPortal.Leaving) return;
        bool entering = portal.Phase == GvlPortal.Entering;
        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, travelTarget.X, travelTarget.Y, travelTarget.Z);
        bool loaded = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, me);
        bool ground = loaded && GroundBelow(me);
        int outcome = portal.Step(now, loaded, entering ? InteriorOf(me) != 0 : true, ground);
        if (outcome == GvlPortal.Wait) return;
        if (outcome == GvlPortal.Abort)
        {
            // Noi that bao san sang ma van khong dung duoc: tra ve cua, lan sau doi mau khac.
            health.Failed(template);
            Log("mau '" + Templates[template].Name + "' khong dung duoc cho " + building);
            Rescue(me);
            return;
        }
        Release(me);
        if (entering)
        {
            health.Worked(template);
            furnishPending = true;
            Emit("building.entered", "id=" + building + ";template=" + template);
            GvlEntrance entered = registry.Find(building);
            string kind = entered != null && entered.Purpose == GvlTier.Shop ? "quan" : "nha";
            Notify("Da vao ~g~" + Templates[template].Name + "~s~ (" + kind + " cap " + Templates[template].Tier + "). Ve cho cua va bam ~y~" +
                useKey + "~s~ de ra. ~y~" + cycleKey + "/" + buyKey + "~s~: mua do bay.");
        }
        else
        {
            me.Heading = returnHeading;
            Emit("building.exited", "id=" + building);
            building = ""; template = -1;
        }
        Save();
    }

    void Release(Ped me)
    {
        movedAt = 0;
        Function.Call(Hash.CLEAR_FOCUS);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, me, false);
        GTA.UI.Screen.FadeIn(400);
    }

    // Dua moi thu ve trang thai an toan (chet, bi dich chuyen, script bi nap lai).
    void LeaveEverything(bool returnPlayer)
    {
        Ped me = Game.Player.Character;
        ReleaseAllVisitors();
        EndHousehold();
        ClearAnchors();
        prepare.Cancel();
        if (me != null && me.Exists())
        {
            if (returnPlayer && building.Length > 0)
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, returnPos.X, returnPos.Y, returnPos.Z, false, false, false);
            if (activity != null) me.Task.ClearAll();
            Release(me);
        }
        activity = null; building = ""; template = -1; furnishPending = false;
        portal.Reset();
        Save();
    }

    // ------------------------------------------------------------ do dac (ActivityAnchor)
    int BuildingTier() { GvlEntrance e = registry.Find(building); return e != null && e.Tier > 0 ? e.Tier : Templates[template].Tier; }
    int BuildingPurpose() { GvlEntrance e = registry.Find(building); return e != null ? e.Purpose : GvlTier.Home; }

    List<GvlPlacement> Owned(string id)
    {
        List<GvlPlacement> list;
        if (!owned.TryGetValue(id, out list)) { list = new List<GvlPlacement>(); owned[id] = list; }
        return list;
    }

    void Furnish(Ped me)
    {
        furnishPending = false;
        ClearAnchors();
        Template t = Templates[template];
        Vector3 spawn = t.Spawn;
        int interior = InteriorOf(me);
        if (interior == 0) return;
        // 1. Do co san trong noi that goc hoac MLO: nhan theo mo hinh.
        int existing = 0;
        foreach (Prop prop in World.GetNearbyProps(spawn, 25f))
        {
            if (anchors.Count >= 60) break;
            GvlObjectKind kind;
            if (prop == null || !prop.Exists() || !KindByHash().TryGetValue(prop.Model.Hash, out kind)) continue;
            Vector3 p = prop.Position;
            if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, p.X, p.Y, p.Z) != interior || NearestAnchor(p, 0.6f) != null) continue;
            Anchor a = new Anchor(); a.Kind = kind; a.Pos = p; a.Heading = prop.Heading;
            anchors.Add(a); existing++;
        }
        // 2. Vo rong: ghep do theo hat giong cua toa nha. 3. Do nguoi choi da mua.
        List<GvlPlacement> plan = new List<GvlPlacement>();
        if (t.Shell) plan.AddRange(GvlLayout.Generate(building, BuildingTier(), BuildingPurpose()));
        plan.AddRange(Owned(building));
        int placed = 0;
        foreach (GvlPlacement p in plan) if (SpawnPlacement(me, p, spawn, interior)) placed++;
        Log("noi that " + building + ": " + anchors.Count + "/" + (existing + plan.Count) + " diem sinh hoat (" + existing +
            " co san, ghep " + placed + "/" + plan.Count + ")");
        StartHousehold(me, interior);
    }

    bool SpawnPlacement(Ped me, GvlPlacement p, Vector3 spawn, int interior)
    {
        GvlObjectKind kind = GvlObjects.Find(p.Kind);
        if (kind == null) return false;
        Model model = null;
        foreach (string name in kind.Models)
        {
            Model candidate = new Model(name);
            if (candidate.IsInCdImage && candidate.Request(500)) { model = candidate; break; }
        }
        if (model == null) { Log("khong co mo hinh cho " + p.Kind); return false; }
        foreach (float scale in new[] { 1f, 0.8f, 0.6f, 0.4f })
        {
            Vector3 at = new Vector3(spawn.X + p.X * scale, spawn.Y + p.Y * scale, spawn.Z);
            if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, at.X, at.Y, at.Z) != interior) continue;
            if (NearestAnchor(at, 1.0f) != null) continue;
            Vector3 eye = spawn + new Vector3(0f, 0f, 0.5f);
            if (World.Raycast(eye, at + new Vector3(0f, 0f, 0.5f), IntersectFlags.Map, me).DidHit) continue;
            RaycastResult floor = World.Raycast(at + new Vector3(0f, 0f, 1f), at - new Vector3(0f, 0f, 3f), IntersectFlags.Map, me);
            float z = floor.DidHit ? floor.HitPosition.Z : spawn.Z - 0.95f;
            Prop prop = World.CreateProp(model, new Vector3(at.X, at.Y, z), false, false);
            if (prop == null || !prop.Exists()) continue;
            prop.Heading = p.Heading;
            prop.IsPositionFrozen = true;
            model.MarkAsNoLongerNeeded();
            Anchor a = new Anchor(); a.Kind = kind; a.Pos = prop.Position; a.Heading = p.Heading; a.Spawned = prop; a.Bought = p.Bought;
            anchors.Add(a);
            return true;
        }
        model.MarkAsNoLongerNeeded();
        return false;
    }

    // ------------------------------------------------------------ mua, ban do bay (kieu Big Ambitions)
    void ShowBuyChoice()
    {
        string kind = GvlObjects.Buyable[buyIndex];
        GvlObjectKind info = GvlObjects.Find(kind);
        Notify("Do de mua: ~y~" + kind + "~s~ (" + info.Label + ") $" + GvlObjects.Price(kind) + ". Bam ~y~" + buyKey + "~s~ de mua va dat truoc mat.");
    }

    void BuyOrSell(Ped me)
    {
        Vector3 spawn = Templates[template].Spawn;
        Anchor near = NearestAnchor(me.Position, 1.3f);
        List<GvlPlacement> mine = Owned(building);
        if (near != null && near.Bought && near.Spawned != null)
        {
            int refund = GvlObjects.Price(near.Kind.Id) / 2;
            mine.RemoveAll(delegate(GvlPlacement p) { return Math.Abs(spawn.X + p.X - near.Pos.X) < 0.6f && Math.Abs(spawn.Y + p.Y - near.Pos.Y) < 0.6f; });
            if (near.Spawned.Exists()) near.Spawned.Delete();
            anchors.Remove(near);
            Earn(refund, "ban lai " + near.Kind.Id);
            Notify("Da ban lai " + near.Kind.Id + " +$" + refund + ".");
            dirty = true; Save();
            return;
        }
        string kind = GvlObjects.Buyable[buyIndex];
        Vector3 at = me.Position + me.ForwardVector * 1.3f;
        GvlPlacement placement = new GvlPlacement();
        placement.Kind = kind; placement.X = at.X - spawn.X; placement.Y = at.Y - spawn.Y; placement.Heading = me.Heading + 180f; placement.Bought = true;
        List<GvlPlacement> all = new List<GvlPlacement>(mine);
        foreach (Anchor a in anchors) { GvlPlacement taken = new GvlPlacement(); taken.X = a.Pos.X - spawn.X; taken.Y = a.Pos.Y - spawn.Y; all.Add(taken); }
        if (!GvlLayout.CanPlace(all, Math.Max(BuildingTier(), Templates[template].Tier), placement.X, placement.Y))
        { Notify("Cho nay chat hoac ngoai phong. Dung cho khac."); return; }
        int price = GvlObjects.Price(kind);
        if (!Pay(price, "mua " + kind)) { Notify("~r~Khong du tien~s~ ($" + price + ")."); return; }
        if (!SpawnPlacement(me, placement, spawn, InteriorOf(me))) { Earn(price, "hoan tien " + kind); Notify("Khong dat duoc o day, da hoan tien."); return; }
        mine.Add(placement);
        Emit("building.furniture_bought", "id=" + building + ";kind=" + kind + ";price=" + price);
        Notify("~g~Da mua " + kind + "~s~ -$" + price + ".");
        dirty = true; Save();
    }

    static bool Pay(int amount, string note)
    {
        Func<int, string, string, bool> pay = AppDomain.CurrentDomain.GetData("GVL.PayCash") as Func<int, string, string, bool>;
        if (pay != null) return pay(amount, "furniture", note);
        if (Game.Player.Money < amount) return false;
        Game.Player.Money = Game.Player.Money - amount;
        return true;
    }

    static void Earn(int amount, string note)
    {
        Action<int, string, string> earn = AppDomain.CurrentDomain.GetData("GVL.EarnCash") as Action<int, string, string>;
        if (earn != null) earn(amount, "furniture", note);
        else Game.Player.Money = Game.Player.Money + amount;
    }

    // ------------------------------------------------------------ ho gia dinh song trong nha
    static readonly string[][] HouseholdModels = {
        new[] { "a_m_m_eastsa_01", "a_f_y_eastsa_01", "a_m_m_salton_01", "a_f_m_eastsa_02" },
        new[] { "a_m_y_business_01", "a_f_y_business_01", "a_m_m_skater_01", "a_f_y_hipster_01" },
        new[] { "a_m_m_bevhills_02", "a_f_y_bevhills_01", "a_m_y_bevhills_01", "a_f_m_bevhills_01" } };

    void StartHousehold(Ped me, int interior)
    {
        EndHousehold();
        GvlEntrance e = registry.Find(building);
        if (e == null || e.Purpose != GvlTier.Home || e.Source == "seed") return;   // nha cua nguoi choi khong co nguoi la
        string[] models = HouseholdModels[GvlTier.ZoneWealth(e.Zone)];
        int size = GvlTier.HouseholdSize(e.Id, BuildingTier());
        Vector3 spawn = Templates[template].Spawn;
        for (int i = 0; i < size; i++)
        {
            Model model = new Model(models[(int)((GvlTemplates.StableHash(e.Id + "/m" + i)) % (uint)models.Length)]);
            if (!model.IsInCdImage || !model.Request(1000)) continue;
            Vector3 at = spawn + new Vector3(1.2f * (i + 1), 0.8f, 0f);
            if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, at.X, at.Y, at.Z) != interior) at = spawn;
            Ped ped = World.CreatePed(model, at);
            model.MarkAsNoLongerNeeded();
            if (ped == null || !ped.Exists()) continue;
            ped.IsPersistent = true;
            Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true);
            Member m = new Member(); m.P = ped; household.Add(m);
        }
        if (household.Count > 0) Log("ho gia dinh " + building + ": " + household.Count + " nguoi");
    }

    void StepHousehold(Ped me, int now)
    {
        int hour = World.CurrentTimeOfDay.Hours;
        foreach (Member m in household)
        {
            if (m.P == null || !m.P.Exists() || m.P.IsDead) continue;
            if (m.Stage == 0)
            {
                Anchor spot = FreeAnchor(GvlObjects.Wanted(hour), m);
                if (spot == null) continue;
                m.Spot = spot; m.Stage = 1; m.Until = now + 9000;
                WalkTo(m.P, StandPoint(spot, m.P.Position));
            }
            else if (m.Stage == 1)
            {
                if (m.P.Position.DistanceTo2D(m.Spot.Pos) < 1.4f || now > m.Until)
                {
                    StartScenario(m.P, m.Spot);
                    m.Stage = 2; m.Until = now + 30000 + (int)(GvlTemplates.StableHash(m.P.Handle + "/" + now) % 30000u);
                }
            }
            else if (now > m.Until) { m.P.Task.ClearAll(); m.Stage = 0; m.Spot = null; }
        }
    }

    Anchor FreeAnchor(string[] wanted, Member self)
    {
        foreach (string kind in wanted)
            foreach (Anchor a in anchors)
            {
                if (a.Kind.Id != kind || a == activity) continue;
                bool taken = false;
                foreach (Member m in household) if (m != self && m.Spot == a) taken = true;
                if (!taken) return a;
            }
        return null;
    }

    void EndHousehold()
    {
        foreach (Member m in household) if (m.P != null && m.P.Exists()) m.P.Delete();
        household.Clear();
    }

    static void StartScenario(Ped ped, Anchor a)
    {
        if (a.Kind.Sit)
            Function.Call(Hash.TASK_START_SCENARIO_AT_POSITION, ped, a.Kind.Scenario, a.Pos.X, a.Pos.Y, a.Pos.Z + 0.5f, a.Heading + 180f, 0, true, false);
        else
        {
            Vector3 toward = a.Pos - ped.Position;
            ped.Heading = (float)(Math.Atan2(-toward.X, toward.Y) * 180.0 / Math.PI);
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped, a.Kind.Scenario, 0, true);
        }
    }

    void ClearAnchors()
    {
        foreach (Anchor a in anchors) if (a.Spawned != null && a.Spawned.Exists()) a.Spawned.Delete();
        anchors.Clear();
    }

    Anchor NearestAnchor(Vector3 p, float range)
    {
        Anchor best = null; float bestDistance = range;
        foreach (Anchor a in anchors)
        {
            float d = p.DistanceTo2D(a.Pos);
            if (d <= bestDistance) { best = a; bestDistance = d; }
        }
        return best;
    }

    static Vector3 StandPoint(Anchor a, Vector3 from)
    {
        Vector3 away = from - a.Pos; away.Z = 0f;
        Vector3 p = away.Length() < 0.1f ? a.Pos : a.Pos + away.Normalized * 0.9f;
        p.Z = from.Z;
        return p;
    }

    static void WalkTo(Ped ped, Vector3 p)
    {
        Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, ped, p.X, p.Y, p.Z, 1.0f, 6000, 0f, 0.3f);
    }

    // ------------------------------------------------------------ sinh hoat cua nguoi choi: di toi do vat roi moi lam
    void BeginActivity(Ped me, Anchor a)
    {
        foreach (Member m in household)
            if (m.Spot == a && m.P != null && m.P.Exists()) { m.P.Task.ClearAll(); m.Stage = 0; m.Spot = null; }   // nhuong cho
        activity = a; activityStage = 0; activityUntil = Game.GameTime + 6000;
        WalkTo(me, StandPoint(a, me.Position));
    }

    void StepActivity(Ped me, int now)
    {
        if (activity == null) return;
        if (activityStage == 0)
        {
            bool arrived = me.Position.DistanceTo2D(activity.Pos) < 1.2f;
            if (!arrived && now < activityUntil) return;
            if (!arrived && me.Position.DistanceTo2D(activity.Pos) > 2.5f) { StopActivity(me); Notify("Khong toi duoc cho do."); return; }
            StartScenario(me, activity);   // ngoi dung ghe, quay mat vao do vat
            activityStage = 1; activityUntil = now + activity.Kind.Seconds * 1000;
            return;
        }
        if (now < activityUntil) return;
        GvlObjectKind kind = activity.Kind;
        AddNeeds(kind.Hunger, kind.Thirst, kind.Energy);
        if (kind.Hygiene > 0f) AppDomain.CurrentDomain.SetData("GVL.HygAdd", kind.Hygiene);
        Emit("building.activity", "id=" + building + ";kind=" + kind.Id);
        Notify("~g~Xong: " + kind.Label + ".~s~" + Gains(kind));
        StopActivity(me);
    }

    static string Gains(GvlObjectKind kind)
    {
        string text = "";
        if (kind.Hunger > 0f) text += " No +" + (int)kind.Hunger;
        if (kind.Thirst > 0f) text += " Nuoc +" + (int)kind.Thirst;
        if (kind.Energy > 0f) text += " Nang luong +" + (int)kind.Energy;
        if (kind.Hygiene > 0f) text += " Ve sinh +" + (int)kind.Hygiene;
        return text;
    }

    void StopActivity(Ped me) { activity = null; me.Task.ClearAll(); }

    static void AddNeeds(float hunger, float thirst, float energy)
    {
        if (hunger == 0f && thirst == 0f && energy == 0f) return;
        float[] pending = AppDomain.CurrentDomain.GetData("GVL.NeedsAdd") as float[];
        if (pending != null && pending.Length >= 3) { hunger += pending[0]; thirst += pending[1]; energy += pending[2]; }
        AppDomain.CurrentDomain.SetData("GVL.NeedsAdd", new float[] { hunger, thirst, energy });
    }

    // ------------------------------------------------------------ cu dan: di bo toi cua, vao, sinh hoat, ra ve
    void StartVisit(int now)
    {
        if (visitors.Count >= VisitorCapacity || !File.Exists("GtaVietLifeResidents.txt")) return;
        Ped me = Game.Player.Character;
        foreach (string line in File.ReadAllLines("GtaVietLifeResidents.txt"))
        {
            string[] parts = line.Split('|');
            int handle, until;
            if (parts.Length < 3 || !int.TryParse(parts[0], out handle) || handle == 0) continue;
            if (visitCooldown.TryGetValue(handle, out until) && now < until) continue;
            if (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, handle) || !Function.Call<bool>(Hash.IS_ENTITY_A_PED, handle)) continue;
            Ped ped = (Ped)Entity.FromHandle(handle);
            if (ped == null || !ped.Exists() || ped.IsDead || ped.IsInVehicle() || ped.IsPositionFrozen) continue;
            if (me.PedGroup != null && me.PedGroup.Contains(ped)) continue;
            Vector3 at = ped.Position;
            GvlEntrance door = registry.Nearest(at.X, at.Y, at.Z, 30f);
            if (door == null || !bubble.Active.Contains(door.Id) || !occupancy.TryEnter(door.Id, handle)) continue;
            Visitor v = new Visitor(); v.P = ped; v.Building = door.Id; v.Door = new Vector3(door.X, door.Y, door.Z);
            v.Stage = 0; v.Until = now + 60000;
            visitors.Add(v);
            visitCooldown[handle] = now + 240000;
            PublishHeld();
            Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped, door.X, door.Y, door.Z, 1.0f, -1, 1.2f, false, 0f);
            return;
        }
    }

    bool PlayerIsIn(string id) { return portal.Phase == GvlPortal.Inside && building == id && template >= 0; }

    // Stage: 0 di toi cua; 1 o trong khi nguoi choi khong co mat (an, dung yen); 2 di toi do vat; 3 dang sinh hoat; 4 di ra cua trong.
    void StepVisitors(Ped me, int now)
    {
        for (int i = visitors.Count - 1; i >= 0; i--)
        {
            Visitor v = visitors[i];
            if (v.P == null || !v.P.Exists() || v.P.IsDead || !bubble.Active.Contains(v.Building)) { EndVisit(i); continue; }
            bool present = PlayerIsIn(v.Building);
            if (v.Stage == 0)
            {
                float d = v.P.Position.DistanceTo2D(v.Door);
                bool blocked = stuck.Update(v.P.Handle, now, d);
                if (d < 1.8f || (blocked && d < 6f))
                {
                    stuck.Clear(v.P.Handle);
                    if (present) BringInside(v, now);
                    else { Hide(v.P, v.Door); v.Stage = 1; v.Until = now + 25000; }
                }
                else if (blocked || now > v.Until) EndVisit(i);   // khong toi duoc cua: tra lai lich cu
            }
            else if (v.Stage == 1)
            {
                if (present) BringInside(v, now);
                else if (now > v.Until) EndVisit(i);
            }
            else if (!present) { Hide(v.P, v.Door); v.Stage = 1; v.Until = now + 5000; v.Spot = null; }
            else if (v.Stage == 2)
            {
                float d = v.P.Position.DistanceTo2D(v.Spot.Pos);
                if (d < 1.3f || stuck.Update(v.P.Handle, now, d) || now > v.Until)
                {
                    stuck.Clear(v.P.Handle);
                    if (d >= 1.3f) Place(v.P, StandPoint(v.Spot, Templates[template].Spawn));
                    Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, v.P, v.Spot.Kind.Scenario, 0, true);
                    v.Stage = 3; v.Until = now + 20000;
                }
            }
            else if (v.Stage == 3)
            {
                if (now > v.Until) { v.P.Task.ClearAll(); WalkTo(v.P, Templates[template].Spawn); v.Stage = 4; v.Until = now + 15000; }
            }
            else
            {
                float d = v.P.Position.DistanceTo2D(Templates[template].Spawn);
                if (d < 1.3f || stuck.Update(v.P.Handle, now, d) || now > v.Until) EndVisit(i);
            }
        }
    }

    void BringInside(Visitor v, int now)
    {
        Anchor free = null;
        foreach (Anchor a in anchors)
            if (occupancy.TryReserve(v.Building, a.Kind.Id, v.P.Handle)) { free = a; break; }
        if (free == null) { if (v.Stage == 0) { Hide(v.P, v.Door); v.Stage = 1; v.Until = now + 15000; } return; }
        Show(v.P);
        Vector3 spawn = Templates[template].Spawn;
        Vector3 beside = new Vector3(spawn.X + 0.6f * (visitors.IndexOf(v) + 1), spawn.Y, spawn.Z);
        Place(v.P, beside);
        v.Spot = free; v.Stage = 2; v.Until = now + 12000;
        WalkTo(v.P, StandPoint(free, spawn));
    }

    static void Place(Ped ped, Vector3 p)
    {
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, ped, p.X, p.Y, p.Z, false, false, false);
    }

    static void Hide(Ped ped, Vector3 door)
    {
        ped.Task.ClearAll();
        Place(ped, door);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, ped, true);
        Function.Call(Hash.SET_ENTITY_COLLISION, ped, false, false);
        Function.Call(Hash.SET_ENTITY_VISIBLE, ped, false, false);
    }

    static void Show(Ped ped)
    {
        Function.Call(Hash.SET_ENTITY_VISIBLE, ped, true, false);
        Function.Call(Hash.SET_ENTITY_COLLISION, ped, true, true);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, ped, false);
    }

    void EndVisit(int index)
    {
        Visitor v = visitors[index];
        visitors.RemoveAt(index);
        if (v.P != null)
        {
            int handle = v.P.Handle;
            occupancy.Leave(handle); stuck.Clear(handle);
            if (v.P.Exists())
            {
                bool wasInside = v.Stage >= 1;
                Show(v.P);
                v.P.Task.ClearAll();
                if (wasInside) Place(v.P, v.Door);
                v.P.Task.WanderAround(v.Door, 12f);
            }
        }
        PublishHeld();
    }

    void MoveVisitorsOutside()
    {
        foreach (Visitor v in visitors)
            if (v.Stage >= 2 && v.P != null && v.P.Exists()) { Hide(v.P, v.Door); v.Stage = 1; v.Until = Game.GameTime + 5000; v.Spot = null; }
    }

    void ReleaseAllVisitors() { for (int i = visitors.Count - 1; i >= 0; i--) EndVisit(i); }

    void PublishHeld()
    {
        List<int> held = new List<int>();
        foreach (Visitor v in visitors) if (v.P != null) held.Add(v.P.Handle);
        AppDomain.CurrentDomain.SetData("GVL.Interiors.Held", held.ToArray());
    }

    // ------------------------------------------------------------ hien thi
    void DrawPrompts(Ped me)
    {
        if (me.IsInVehicle() || OtherMenuOpen()) return;
        if (portal.Phase == GvlPortal.Outside)
        {
            Vector3 p = me.Position;
            if (showMarkers)
                foreach (GvlEntrance marked in registry.Within(p.X, p.Y, p.Z, 40f))
                    if (marked.Tier >= 0) DrawDoorMarker(new Vector3(marked.X, marked.Y, marked.Z), marked.Purpose == GvlTier.Shop);
            GvlEntrance door = registry.Nearest(p.X, p.Y, p.Z, PromptRange);
            if (door != null && door.Tier >= 0)
                GTA.UI.Screen.ShowSubtitle("~y~" + useKey + "~s~: vao " + (door.Purpose == GvlTier.Shop ? "quan" : "nha") +
                    (door.Tier > 0 ? " (cap " + door.Tier + ")" : ""), 100);
        }
        else if (portal.Phase == GvlPortal.Inside && template >= 0)
        {
            DrawMarker(Templates[template].Spawn);
            Anchor near = NearestAnchor(me.Position, 2.2f);
            if (activity != null)
            {
                string left = activityStage == 1 ? " con " + Math.Max(0, (activityUntil - Game.GameTime + 999) / 1000) + " giay" : "";
                GTA.UI.Screen.ShowSubtitle("Dang " + activity.Kind.Label + left + "... ~y~" + useKey + "~s~: dung", 100);
            }
            else if (me.Position.DistanceTo(Templates[template].Spawn) < PromptRange) GTA.UI.Screen.ShowSubtitle("~y~" + useKey + "~s~: ra ngoai", 100);
            else if (near != null) GTA.UI.Screen.ShowSubtitle("~y~" + useKey + "~s~: " + near.Kind.Label +
                (near.Bought ? "   ~y~" + buyKey + "~s~: ban lai" : ""), 100);
        }
    }

    // Vong tron xanh la tren mat dat (quan: vang) va mui ten nho phia tren, nhin thay tu xa.
    static void DrawDoorMarker(Vector3 p, bool shop)
    {
        int r = shop ? 255 : 60, g = shop ? 200 : 230, b = shop ? 40 : 90;
        Function.Call(Hash.DRAW_MARKER, 25, p.X, p.Y, p.Z + 0.05f, 0f, 0f, 0f, 0f, 0f, 0f, 1.2f, 1.2f, 1.2f,
            r, g, b, 170, false, false, 2, false, 0, 0, false);
        Function.Call(Hash.DRAW_MARKER, 2, p.X, p.Y, p.Z + 1.3f, 0f, 0f, 0f, 180f, 0f, 0f, 0.4f, 0.4f, 0.4f,
            r, g, b, 200, true, true, 2, false, 0, 0, false);
    }

    static void DrawMarker(Vector3 p)
    {
        Function.Call(Hash.DRAW_MARKER, 2, p.X, p.Y, p.Z + 0.4f, 0f, 0f, 0f, 180f, 0f, 0f, 0.25f, 0.25f, 0.25f,
            80, 200, 255, 150, false, true, 2, false, 0, 0, false);
    }

    // ------------------------------------------------------------ luu / tai
    void Load()
    {
        if (!store.Load(ParseSaveLine)) Notify("~r~GTA Viet Life: khong doc duoc danh ba cua. File cu duoc giu nguyen, tien trinh moi KHONG duoc luu.");
    }

    void ParseSaveLine(string line)
    {
        int i = line.IndexOf('=');
        if (i < 0) return;
        string key = line.Substring(0, i), value = line.Substring(i + 1);
        if (key == "door") registry.Load(value);
        else if (key == "bad2") health.Load(value);   // chi so mau cu khac thu tu moi, bo "bad"
        else if (key == "own")
        {
            int bar = value.IndexOf('|');
            if (bar > 0) owned[value.Substring(0, bar)] = GvlLayout.Parse(value.Substring(bar + 1));
        }
        else if (key == "inside") Restore(value);
    }

    // Chi tiep tuc trang thai "dang o trong" khi nhan vat that su con o cho mau noi that do.
    void Restore(string value)
    {
        string[] f = value.Split('|');
        CultureInfo ci = CultureInfo.InvariantCulture;
        if (f.Length != 6) return;
        int pick = int.Parse(f[1], ci);
        Ped me = Game.Player.Character;
        if (pick < 0 || pick >= Templates.Length || me == null || !me.Exists()) return;
        building = f[0]; template = pick;
        returnPos = new Vector3(float.Parse(f[2], ci), float.Parse(f[3], ci), float.Parse(f[4], ci));
        returnHeading = float.Parse(f[5], ci);
        if (me.Position.DistanceTo(Templates[pick].Spawn) > 60f) { building = ""; template = -1; return; }
        if (Templates[pick].Ipl.Length > 0) Function.Call(Hash.REQUEST_IPL, Templates[pick].Ipl);
        travelTarget = Templates[pick].Spawn;
        Function.Call(Hash.FREEZE_ENTITY_POSITION, me, true);
        movedAt = Game.GameTime;
        portal.BeginEnter(Game.GameTime);
    }

    void Save()
    {
        try
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            List<string> lines = new List<string>();
            lines.Add("version=1");
            foreach (string door in registry.Serialize()) lines.Add("door=" + door);
            foreach (string bad in health.Serialize()) lines.Add("bad2=" + bad);
            foreach (KeyValuePair<string, List<GvlPlacement>> o in owned)
                if (o.Value.Count > 0) lines.Add("own=" + o.Key + "|" + GvlLayout.Serialize(o.Value));
            if (building.Length > 0 && template >= 0)
                lines.Add("inside=" + building + "|" + template + "|" + returnPos.X.ToString(ci) + "|" +
                    returnPos.Y.ToString(ci) + "|" + returnPos.Z.ToString(ci) + "|" + returnHeading.ToString(ci));
            lines.Add("end=1");
            if (store.Write(lines)) dirty = false;
        }
        catch (Exception ex) { Log("save loi: " + ex.Message); }
    }

    void OnAborted(object sender, EventArgs e)
    {
        try { LeaveEverything(true); } catch (Exception ex) { Log("thoat loi: " + ex.Message); }
    }

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
            list.Add(seq + "\t" + type + "\t" + data);
        }
    }

    static void Notify(string message) { GTA.UI.Notification.Show(message, false); }

    static void Log(string message)
    {
        try { File.AppendAllText("GtaVietLifeInteriors.log", DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine); }
        catch { }
    }
}

// <gvl-testable:uis>
// Logic UIS thuan C#, khong goi API GTA; tests/test_csharp_logic.py bien dich doan nay va chay that.
public sealed class GvlEntrance
{
    public string Id, Source, Zone = "";
    public float X, Y, Z, Area;
    public int Tier, Purpose;   // Tier 0: chua do
}

// Danh ba cua: luoi o vuong de tra cuu nhanh, khong trung trong pham vi MergeDistance.
public sealed class GvlRegistry
{
    public const float CellSize = 60f;
    public const float MergeDistance = 2.5f;
    public const int Capacity = 5000;
    readonly Dictionary<long, List<GvlEntrance>> cells = new Dictionary<long, List<GvlEntrance>>();
    readonly Dictionary<string, GvlEntrance> byId = new Dictionary<string, GvlEntrance>();
    readonly List<GvlEntrance> all = new List<GvlEntrance>();

    public int Count { get { return all.Count; } }

    public static bool LooksLikeDoor(float sizeX, float sizeY, float height)
    {
        float thin = Math.Min(sizeX, sizeY), wide = Math.Max(sizeX, sizeY);
        return thin < 0.25f && wide > 0.7f && wide < 1.7f && height > 1.9f && height < 3.0f;
    }

    public static string MakeId(float x, float y, float z)
    {
        return ((int)Math.Round(x)).ToString(CultureInfo.InvariantCulture) + "_" +
            ((int)Math.Round(y)).ToString(CultureInfo.InvariantCulture) + "_" +
            ((int)Math.Round(z)).ToString(CultureInfo.InvariantCulture);
    }

    static int Cell(float v) { return (int)Math.Floor(v / CellSize); }
    static long Key(int cx, int cy) { return ((long)cx << 32) ^ (uint)cy; }

    public static float Distance(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2, dy = y1 - y2;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public GvlEntrance Find(string id) { GvlEntrance e; return byId.TryGetValue(id, out e) ? e : null; }

    public List<GvlEntrance> Within(float x, float y, float z, float radius)
    {
        List<GvlEntrance> found = new List<GvlEntrance>();
        int span = (int)Math.Ceiling(radius / CellSize);
        for (int cx = Cell(x) - span; cx <= Cell(x) + span; cx++)
            for (int cy = Cell(y) - span; cy <= Cell(y) + span; cy++)
            {
                List<GvlEntrance> list;
                if (!cells.TryGetValue(Key(cx, cy), out list)) continue;
                foreach (GvlEntrance e in list)
                    if (Distance(x, y, e.X, e.Y) <= radius && Math.Abs(z - e.Z) <= Math.Max(3f, radius)) found.Add(e);
            }
        return found;
    }

    public GvlEntrance Nearest(float x, float y, float z, float radius)
    {
        GvlEntrance best = null; float bestDistance = float.MaxValue;
        foreach (GvlEntrance e in Within(x, y, z, radius))
        {
            if (Math.Abs(z - e.Z) > 2.5f) continue;
            float d = Distance(x, y, e.X, e.Y);
            if (d < bestDistance) { best = e; bestDistance = d; }
        }
        return best;
    }

    public bool Add(float x, float y, float z, string source)
    {
        if (all.Count >= Capacity || Nearest(x, y, z, MergeDistance) != null) return false;
        string id = MakeId(x, y, z);
        if (byId.ContainsKey(id)) return false;
        GvlEntrance e = new GvlEntrance();
        e.Id = id; e.X = x; e.Y = y; e.Z = z; e.Source = source;
        byId[id] = e; all.Add(e);
        List<GvlEntrance> list;
        long key = Key(Cell(x), Cell(y));
        if (!cells.TryGetValue(key, out list)) { list = new List<GvlEntrance>(); cells[key] = list; }
        list.Add(e);
        return true;
    }

    public List<string> Serialize()
    {
        List<string> lines = new List<string>();
        CultureInfo ci = CultureInfo.InvariantCulture;
        foreach (GvlEntrance e in all)
            lines.Add(e.X.ToString("R", ci) + "|" + e.Y.ToString("R", ci) + "|" + e.Z.ToString("R", ci) + "|" + e.Source + "|" +
                e.Tier + "|" + e.Purpose + "|" + e.Area.ToString("0.#", ci) + "|" + (e.Zone ?? ""));
        return lines;
    }

    // Dong hong bi bo qua rieng le.
    public bool Load(string line)
    {
        string[] f = (line ?? "").Split('|');
        float x, y, z;
        CultureInfo ci = CultureInfo.InvariantCulture;
        if ((f.Length != 4 && f.Length != 8) || !float.TryParse(f[0], NumberStyles.Float, ci, out x) ||
            !float.TryParse(f[1], NumberStyles.Float, ci, out y) || !float.TryParse(f[2], NumberStyles.Float, ci, out z)) return false;
        if (!Add(x, y, z, f[3])) return false;
        int tier, purpose; float area;
        if (f.Length == 8 && int.TryParse(f[4], out tier) && tier >= 0 && tier <= 4 && int.TryParse(f[5], out purpose) &&
            float.TryParse(f[6], NumberStyles.Float, ci, out area))
        {
            GvlEntrance e = Find(MakeId(x, y, z));
            e.Tier = tier; e.Purpose = purpose == 1 ? 1 : 0; e.Area = area; e.Zone = f[7];
        }
        return true;
    }
}

// Nha GTA phan lon khong co cua roi (cua dinh lien vao toa nha): dat loi vao o mat tuong quay ra pho.
public static class GvlFacade
{
    public const float Gap = 18f;        // hai loi vao mat tien cach nhau it nhat
    public const float Reach = 14f;      // tia quet tu nguoi choi toi tuong
    public static bool Accept(float normalZ, float distance, int entrancesNearby, bool roofBehind, bool nearRoad)
    {
        if (Math.Abs(normalZ) > 0.3f) return false;           // mat dat, mai, cau thang: khong phai tuong
        if (distance < 1.2f || distance > Reach) return false;
        return roofBehind && nearRoad && entrancesNearby == 0;
    }
}

// Bat trong 120 m, chi go khi xa hon 150 m de khong nhap nhay o ria.
public sealed class GvlBubble
{
    public const float EnterRadius = 120f;
    public const float ExitRadius = 150f;
    public readonly HashSet<string> Active = new HashSet<string>();

    public void Update(GvlRegistry registry, float x, float y, float z)
    {
        List<string> gone = new List<string>();
        foreach (string id in Active)
        {
            GvlEntrance e = registry.Find(id);
            if (e == null || GvlRegistry.Distance(x, y, e.X, e.Y) > ExitRadius) gone.Add(id);
        }
        foreach (string id in gone) Active.Remove(id);
        foreach (GvlEntrance e in registry.Within(x, y, z, EnterRadius)) Active.Add(e.Id);
    }
}

// Mau hong qua MaxFailures lan lien tiep thi khong dung nua.
public sealed class GvlTemplateHealth
{
    public const int MaxFailures = 2;
    readonly Dictionary<int, int> failures = new Dictionary<int, int>();

    public bool Usable(int index) { int n; return !failures.TryGetValue(index, out n) || n < MaxFailures; }
    public void Failed(int index) { int n; failures.TryGetValue(index, out n); failures[index] = n + 1; }
    public void Worked(int index) { failures.Remove(index); }

    public List<string> Serialize()
    {
        List<string> lines = new List<string>();
        foreach (KeyValuePair<int, int> p in failures) lines.Add(p.Key + ":" + p.Value);
        return lines;
    }

    public void Load(string line)
    {
        string[] f = (line ?? "").Split(':');
        int index, n;
        if (f.Length == 2 && int.TryParse(f[0], out index) && int.TryParse(f[1], out n)) failures[index] = n;
    }
}

public static class GvlTemplates
{
    public static uint StableHash(string text)
    {
        uint h = 2166136261u;
        foreach (char c in text) { h ^= c; unchecked { h *= 16777619u; } }
        return h;
    }

    // Cung mot toa nha luon ra cung mot mau; mau hong thi lay mau ke tiep.
    public static int Assign(string buildingId, int count, GvlTemplateHealth health, HashSet<int> skip = null)
    {
        if (count <= 0) return -1;
        int start = (int)(StableHash(buildingId) % (uint)count);
        for (int i = 0; i < count; i++)
        {
            int index = (start + i) % count;
            if (health.Usable(index) && (skip == null || !skip.Contains(index))) return index;
        }
        return -1;
    }

    // Do lech vi tri do dac so voi cua trong, co dinh theo toa nha, loai do va lan thu.
    public static void Offset(string buildingId, string kind, int attempt, out float dx, out float dy)
    {
        uint h = StableHash(buildingId + "/" + kind + "/" + attempt);
        double angle = (h % 3600) / 3600.0 * Math.PI * 2.0;
        double radius = 1.8 + ((h >> 12) % 400) / 100.0;
        dx = (float)(Math.Cos(angle) * radius);
        dy = (float)(Math.Sin(angle) * radius);
    }
}

// Qua cua: dong bang it nhat 200 ms va cho toi khi va cham da nap; qua han thi huy.
public sealed class GvlPortal
{
    public const int Outside = 0, Entering = 1, Inside = 2, Leaving = 3;
    public const int Wait = 0, Done = 1, Abort = 2;
    public const int MinFreezeMs = 200;
    public const int TimeoutMs = 8000;
    public const int ExitTimeoutMs = 20000;
    public const int BlackScreenLimitMs = 25000;
    int since;
    public int Phase { get; private set; }

    public void BeginEnter(int now) { Phase = Entering; since = now; }
    public void BeginExit(int now) { Phase = Leaving; since = now; }
    public void Reset() { Phase = Outside; }

    public int Step(int now, bool collisionLoaded, bool insideInterior, bool groundBelow)
    {
        if (Phase != Entering && Phase != Leaving) return Wait;
        int elapsed = now - since;
        if (elapsed < MinFreezeMs) return Wait;
        if (Phase == Entering)
        {
            if (collisionLoaded && insideInterior && groundBelow) { Phase = Inside; return Done; }
            if (elapsed >= TimeoutMs) { Phase = Outside; return Abort; }
            return Wait;
        }
        // Diem quay ve la cho nguoi choi vua dung tren mat dat nen qua han van tha ra.
        if ((collisionLoaded && groundBelow) || elapsed >= ExitTimeoutMs) { Phase = Outside; return Done; }
        return Wait;
    }
}

// Cho noi that nap xong truoc khi di chuyen nguoi choi.
public sealed class GvlPrepare
{
    public const int LimitMs = 3000;
    int since;
    public bool Active { get; private set; }

    public void Begin(int now) { Active = true; since = now; }
    public void Cancel() { Active = false; }

    public int Step(int now, bool ready)
    {
        if (!Active) return GvlPortal.Wait;
        if (ready) { Active = false; return GvlPortal.Done; }
        if (now - since >= LimitMs) { Active = false; return GvlPortal.Abort; }
        return GvlPortal.Wait;
    }
}

// Suc chua moi toa nha va giu cho tren tung do vat de hai nguoi khong de len nhau.
public sealed class GvlOccupancy
{
    readonly int capacity;
    readonly Dictionary<int, string> actors = new Dictionary<int, string>();
    readonly Dictionary<string, int> spots = new Dictionary<string, int>();

    public GvlOccupancy(int capacity) { this.capacity = capacity; }

    public int Count(string building)
    {
        int n = 0;
        foreach (string b in actors.Values) if (b == building) n++;
        return n;
    }

    public bool TryEnter(string building, int actor)
    {
        if (actors.ContainsKey(actor) || Count(building) >= capacity) return false;
        actors[actor] = building;
        return true;
    }

    public bool TryReserve(string building, string spot, int actor)
    {
        string current;
        if (!actors.TryGetValue(actor, out current) || current != building) return false;
        string key = building + "/" + spot;
        int owner;
        if (spots.TryGetValue(key, out owner)) return owner == actor;
        Release(actor);
        spots[key] = actor;
        return true;
    }

    void Release(int actor)
    {
        List<string> mine = new List<string>();
        foreach (KeyValuePair<string, int> p in spots) if (p.Value == actor) mine.Add(p.Key);
        foreach (string key in mine) spots.Remove(key);
    }

    public void Leave(int actor) { Release(actor); actors.Remove(actor); }
}

// Khong tien them duoc it nhat MinProgress trong LimitMs thi coi la ket.
public sealed class GvlStuck
{
    public const int LimitMs = 8000;
    public const float MinProgress = 0.5f;
    readonly Dictionary<int, float> best = new Dictionary<int, float>();
    readonly Dictionary<int, int> since = new Dictionary<int, int>();

    public bool Update(int actor, int now, float distanceToGoal)
    {
        float previous;
        if (!best.TryGetValue(actor, out previous) || distanceToGoal <= previous - MinProgress)
        {
            best[actor] = distanceToGoal; since[actor] = now;
            return false;
        }
        return now - since[actor] >= LimitMs;
    }

    public void Clear(int actor) { best.Remove(actor); since.Remove(actor); }
}
// </gvl-testable:uis>
