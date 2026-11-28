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
        public readonly string Name; public readonly Vector3 Spawn; public readonly string Ipl;
        public Template(string name, float x, float y, float z, string ipl) { Name = name; Spawn = new Vector3(x, y, z); Ipl = ipl; }
    }

    // Toa do va ten IPL lay tu du an bob74_ipl (giay phep MIT, (c) 2024 Bob74). Chua kiem trong game:
    // mau nao khong nap duoc hai lan thi tu bi loai.
    static readonly Template[] Templates = {
        new Template("Nha binh dan", 261.4586f, -998.8196f, -99.0086f, ""),
        new Template("Nha trung luu", 347.2686f, -999.2955f, -99.1962f, ""),
        new Template("Can ho cao cap 1", -35.3128f, -580.4199f, 88.7122f, ""),
        new Template("Can ho cao cap 2", -1477.14f, -538.7499f, 55.5264f, ""),
        new Template("Biet thu 1", -169.286f, 486.4938f, 137.4436f, ""),
        new Template("Biet thu 2", 340.9412f, 437.1798f, 149.3925f, ""),
        new Template("Biet thu 3", 373.023f, 416.105f, 145.7006f, ""),
        new Template("Biet thu 4", -676.127f, 588.612f, 145.1698f, ""),
        new Template("Biet thu 5", -763.107f, 615.906f, 144.1401f, ""),
        new Template("Biet thu 6", -857.798f, 682.563f, 152.6529f, ""),
        new Template("Biet thu 7", 120.5f, 549.952f, 184.097f, ""),
        new Template("Biet thu 8", -1288f, 440.748f, 97.6946f, ""),
        new Template("Chung cu A", -1462.281f, -539.6276f, 72.4443f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo_"),
        new Template("Chung cu B", -914.9026f, -374.8731f, 112.6748f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__1"),
        new Template("Chung cu C", -609.5669f, 51.2821f, 96.6002f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__2"),
        new Template("Chung cu D", -778.5061f, 331.316f, 210.3972f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__3"),
        new Template("Chung cu E", -22.6135f, -590.1432f, 78.4309f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__4"),
        new Template("Chung cu F", -609.5669f, 51.2821f, -183.9808f, "mpbusiness_int_placement_interior_v_mp_apt_h_01_milo__5"),
        new Template("Penthouse hien dai", -787.7805f, 334.9232f, 215.8384f, "apa_v_mp_h_01_a"),
        new Template("Penthouse sang trong", -773.2258f, 322.8252f, 194.8862f, "apa_v_mp_h_01_b"),
        new Template("Penthouse co dien", -787.7805f, 334.9232f, 186.1134f, "apa_v_mp_h_01_c"),
        new Template("Nha sinh hoat chung 1", 1107.04f, -3157.399f, -37.5186f, "bkr_biker_interior_placement_interior_0_biker_dlc_int_01_milo"),
        new Template("Nha sinh hoat chung 2", 998.4809f, -3164.711f, -38.9073f, "bkr_biker_interior_placement_interior_1_biker_dlc_int_02_milo"),
        // Ba noi that cot truyen (toa do dang dung trong module Doi song): luon co san trong che do Story.
        new Template("Nha Trevor", 1973.0f, 3815.0f, 33.43f, ""),
        new Template("Nha Michael", -802.3f, 175.0f, 72.8f, ""),
        new Template("Nha Franklin", 7.1197f, 536.6153f, 176.028f, "") };

    const int StoryTemplates = 3;      // nam cuoi danh sach
    const int RandomTries = 3;         // sau chung nay lan hong thi dung noi that cot truyen

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

    sealed class AnchorKind
    {
        public string Id, Label, Scenario; public string[] Models; public int Seconds; public float Hunger, Thirst, Energy, Hygiene;
        public AnchorKind(string id, string label, string scenario, int seconds, float hunger, float thirst, float energy, float hygiene, params string[] models)
        { Id = id; Label = label; Scenario = scenario; Seconds = seconds; Hunger = hunger; Thirst = thirst; Energy = energy; Hygiene = hygiene; Models = models; }
    }

    static readonly AnchorKind[] Kinds = {
        new AnchorKind("bed", "ngu", "WORLD_HUMAN_BUM_SLEEP", 12, 0f, 0f, 35f, 0f, "v_res_msonbed", "v_res_mbbed", "v_res_mdbed", "v_res_d_bed", "v_res_tre_bed1"),
        new AnchorKind("table", "an com", "WORLD_HUMAN_EATING", 8, 20f, 5f, 0f, 0f, "prop_table_03", "prop_table_01", "prop_table_02"),
        new AnchorKind("fridge", "lay do uong", "WORLD_HUMAN_DRINKING", 6, 8f, 25f, 0f, 0f, "prop_fridge_01", "prop_fridge_03", "v_res_fridgemoda", "v_res_fridgemodsml"),
        new AnchorKind("toilet", "ve sinh", "WORLD_HUMAN_STAND_IMPATIENT", 6, 0f, 0f, 0f, 8f, "prop_toilet_01", "prop_toilet_02"),
        new AnchorKind("desk", "lam viec", "WORLD_HUMAN_CLIPBOARD", 10, 0f, 0f, 0f, 0f, "prop_off_desk_01", "v_corp_offdesk", "prop_table_04") };

    sealed class Anchor { public AnchorKind Kind; public Vector3 Pos; public Prop Spawned; }

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

    bool enabled = true, autoDiscover = true, showMarkers = true, dirty, furnishPending;
    Keys useKey = Keys.H, teleportKey = Keys.F7;
    int testIndex = -1, teleportUntil;
    TestPoint teleporting;
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
            if (me.IsDead) { if (portal.Phase != GvlPortal.Outside || building.Length > 0) LeaveEverything(false); return; }

            StepTeleport(me, now);
            StepPortal(me, now);
            if (portal.Phase == GvlPortal.Inside)
            {
                // Bi dich chuyen ra ngoai (nhiem vu, trainer, chet): don dep, khong keo nguoi choi ve.
                if (template >= 0 && me.Position.DistanceTo(Templates[template].Spawn) > 80f) LeaveEverything(false);
                else if (furnishPending) Furnish(me);
            }
            if (now > nextBubble) { nextBubble = now + 500; UpdateBubble(me); }
            if (autoDiscover && portal.Phase == GvlPortal.Outside && now > nextScan) { nextScan = now + 1500; DiscoverDoors(me); }
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
            if (!registeredWithEngine.Add(id)) continue;
            string body = "{\"request_id\":\"" + Guid.NewGuid().ToString("N") +
                "\",\"building_id\":\"" + BuildingPath + id + "\",\"category\":\"residential\",\"name\":\"Nha " + id +
                "\",\"adapter_maps\":{\"gta5.shvdn\":{\"entrance\":\"" + id + ".door\",\"interior\":\"" + id +
                ".interior\"}},\"interactions\":[\"enter\",\"exit\",\"inspect\"]}";
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
            if (registry.Add(p.X, p.Y, p.Z, "scan")) { added++; dirty = true; }
        }
    }

    // ------------------------------------------------------------ cua vao / cua ra
    static int InteriorOf(Ped ped) { return Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, ped); }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!enabled || (e.KeyCode != useKey && e.KeyCode != teleportKey) || Game.GameTime < keyCooldown) return;
        keyCooldown = Game.GameTime + 600;
        try
        {
            Ped me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead || me.IsInVehicle() || OtherMenuOpen() || teleporting != null) return;
            if (e.KeyCode == teleportKey) { BeginTeleport(me); return; }
            if (portal.Phase == GvlPortal.Outside)
            {
                if (e.Shift) { MarkDoor(me); return; }
                GvlEntrance door = registry.Nearest(me.Position.X, me.Position.Y, me.Position.Z, PromptRange);
                if (door != null) BeginEnter(me, door);
            }
            else if (portal.Phase == GvlPortal.Inside && template >= 0)
            {
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

    void BeginTeleport(Ped me)
    {
        if (portal.Phase != GvlPortal.Outside) { Notify("Ra khoi nha truoc khi dich chuyen."); return; }
        testIndex = (testIndex + 1) % TestPoints.Length;
        teleporting = TestPoints[testIndex];
        teleportUntil = Game.GameTime + 6000;
        Vector3 p = teleporting.Pos;
        float z = p.Z > 0f ? p.Z + 0.3f : 80f;
        Travel(me, new Vector3(p.X, p.Y, z), "");
    }

    void StepTeleport(Ped me, int now)
    {
        if (teleporting == null) return;
        Vector3 p = teleporting.Pos;
        Function.Call(Hash.REQUEST_COLLISION_AT_COORD, travelTarget.X, travelTarget.Y, travelTarget.Z);
        bool loaded = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, me);
        float ground = p.Z;
        if (ground <= 0f)
        {
            OutputArgument height = new OutputArgument();
            if (Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD, p.X, p.Y, 80f, height, false)) ground = height.GetResult<float>();
        }
        if (now < teleportUntil && (!loaded || ground <= 0f)) return;
        if (ground <= 0f) { Vector3 street = World.GetNextPositionOnStreet(new Vector3(p.X, p.Y, 30f)); Place(me, street); }
        else Place(me, new Vector3(p.X, p.Y, ground + 0.3f));
        Release(me);
        Notify("~g~Diem " + (testIndex + 1) + "/" + TestPoints.Length + "~s~: " + teleporting.Name);
        teleporting = null;
    }

    void BeginEnter(Ped me, GvlEntrance door)
    {
        if (Game.Player.WantedLevel > 0) { Notify("Dang bi truy na, chua vao nha duoc."); return; }
        int pick = GvlTemplates.Assign(door.Id, Templates.Length, health);
        if (pick < 0) { Notify("Khong co mau noi that nao nap duoc tren may nay."); return; }
        building = door.Id; template = pick;
        tried.Clear();
        returnPos = me.Position; returnHeading = me.Heading;
        Travel(me, Templates[pick].Spawn, Templates[pick].Ipl);
        portal.BeginEnter(Game.GameTime);
        Save();   // ghi diem quay ve truoc khi roi mat dat
    }

    int NextTemplate()
    {
        if (tried.Count < RandomTries) return GvlTemplates.Assign(building, Templates.Length, health, tried);
        for (int i = Templates.Length - StoryTemplates; i < Templates.Length; i++)
            if (!tried.Contains(i)) return i;
        return -1;
    }

    void BeginExit(Ped me)
    {
        MoveVisitorsOutside();
        ClearAnchors();
        Travel(me, returnPos, "");
        portal.BeginExit(Game.GameTime);
    }

    void Travel(Ped me, Vector3 target, string ipl)
    {
        travelTarget = target;
        if (ipl.Length > 0) Function.Call(Hash.REQUEST_IPL, ipl);
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
        int outcome = portal.Step(now, loaded, entering ? InteriorOf(me) != 0 : true);
        if (outcome == GvlPortal.Wait) return;
        if (outcome == GvlPortal.Abort)
        {
            health.Failed(template);
            tried.Add(template);
            Log("mau '" + Templates[template].Name + "' khong nap duoc cho " + building);
            int next = NextTemplate();
            if (next >= 0)
            {
                template = next;
                Travel(me, Templates[next].Spawn, Templates[next].Ipl);
                portal.BeginEnter(now);
                Save();
                return;
            }
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, me, returnPos.X, returnPos.Y, returnPos.Z, false, false, false);
            Release(me);
            building = ""; template = -1; dirty = true; Save();
            Notify("~r~Khong nap duoc noi that nao.~s~ Xem GtaVietLifeInteriors.log.");
            return;
        }
        Release(me);
        if (entering)
        {
            health.Worked(template);
            furnishPending = true;
            Emit("building.entered", "id=" + building + ";template=" + template);
            Notify("Da vao ~g~" + Templates[template].Name + "~s~. Ve cho cua va bam ~y~" + useKey + "~s~ de ra.");
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
        Function.Call(Hash.CLEAR_FOCUS);
        Function.Call(Hash.FREEZE_ENTITY_POSITION, me, false);
        GTA.UI.Screen.FadeIn(400);
    }

    // Dua moi thu ve trang thai an toan (chet, bi dich chuyen, script bi nap lai).
    void LeaveEverything(bool returnPlayer)
    {
        Ped me = Game.Player.Character;
        ReleaseAllVisitors();
        ClearAnchors();
        teleporting = null;
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
    void Furnish(Ped me)
    {
        furnishPending = false;
        ClearAnchors();
        Vector3 spawn = Templates[template].Spawn;
        int interior = InteriorOf(me);
        if (interior == 0) return;
        foreach (AnchorKind kind in Kinds)
        {
            Anchor anchor = FindExisting(kind, spawn, interior) ?? SpawnFurniture(me, kind, spawn, interior);
            if (anchor != null) anchors.Add(anchor);
        }
        Log("noi that " + building + ": " + anchors.Count + "/" + Kinds.Length + " diem sinh hoat");
    }

    static Anchor FindExisting(AnchorKind kind, Vector3 spawn, int interior)
    {
        foreach (string name in kind.Models)
        {
            int handle = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE, spawn.X, spawn.Y, spawn.Z, 15f,
                Game.GenerateHash(name), false, false, false);
            if (handle == 0) continue;
            Vector3 p = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, handle, true);
            if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, p.X, p.Y, p.Z) != interior) continue;
            Anchor a = new Anchor(); a.Kind = kind; a.Pos = p; return a;
        }
        return null;
    }

    // Vi tri sinh theo hat giong co dinh cua toa nha; chi nhan cho cung noi that va nhin thay tu cua.
    Anchor SpawnFurniture(Ped me, AnchorKind kind, Vector3 spawn, int interior)
    {
        foreach (string name in kind.Models)
        {
            Model model = new Model(name);
            if (!model.IsInCdImage) continue;
            if (!model.Request(500)) continue;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float dx, dy;
                GvlTemplates.Offset(building, kind.Id, attempt, out dx, out dy);
                Vector3 p = new Vector3(spawn.X + dx, spawn.Y + dy, spawn.Z);
                if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, p.X, p.Y, p.Z) != interior) continue;
                if (NearestAnchor(p, 1.4f) != null) continue;
                Vector3 beyond = p + (p - spawn).Normalized * 0.7f;
                if (World.Raycast(spawn, beyond, IntersectFlags.Map | IntersectFlags.Objects, me).DidHit) continue;
                Prop prop = World.CreateProp(model, new Vector3(p.X, p.Y, spawn.Z - 0.95f), false, false);
                if (prop == null || !prop.Exists()) continue;
                prop.IsPositionFrozen = true;
                model.MarkAsNoLongerNeeded();
                Anchor a = new Anchor(); a.Kind = kind; a.Pos = prop.Position; a.Spawned = prop; return a;
            }
            model.MarkAsNoLongerNeeded();
            return null;
        }
        return null;
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
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, me, activity.Kind.Scenario, 0, true);
            activityStage = 1; activityUntil = now + activity.Kind.Seconds * 1000;
            return;
        }
        if (now < activityUntil) return;
        AnchorKind kind = activity.Kind;
        AddNeeds(kind.Hunger, kind.Thirst, kind.Energy);
        if (kind.Hygiene > 0f) AppDomain.CurrentDomain.SetData("GVL.HygAdd", kind.Hygiene);
        Emit("building.activity", "id=" + building + ";kind=" + kind.Id);
        Notify("Xong: " + kind.Label + ".");
        StopActivity(me);
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
                foreach (GvlEntrance door in registry.Within(p.X, p.Y, p.Z, 15f))
                    DrawMarker(new Vector3(door.X, door.Y, door.Z));
            if (registry.Nearest(p.X, p.Y, p.Z, PromptRange) != null)
                GTA.UI.Screen.ShowSubtitle("~y~" + useKey + "~s~: mo cua vao nha", 100);
        }
        else if (portal.Phase == GvlPortal.Inside && template >= 0)
        {
            DrawMarker(Templates[template].Spawn);
            Anchor near = NearestAnchor(me.Position, 2.2f);
            if (activity != null) GTA.UI.Screen.ShowSubtitle("Dang " + activity.Kind.Label + "... ~y~" + useKey + "~s~: dung", 100);
            else if (me.Position.DistanceTo(Templates[template].Spawn) < PromptRange) GTA.UI.Screen.ShowSubtitle("~y~" + useKey + "~s~: ra ngoai", 100);
            else if (near != null) GTA.UI.Screen.ShowSubtitle("~y~" + useKey + "~s~: " + near.Kind.Label, 100);
        }
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
        else if (key == "bad") health.Load(value);
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
            foreach (string bad in health.Serialize()) lines.Add("bad=" + bad);
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
    public string Id, Source;
    public float X, Y, Z;
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
            lines.Add(e.X.ToString("R", ci) + "|" + e.Y.ToString("R", ci) + "|" + e.Z.ToString("R", ci) + "|" + e.Source);
        return lines;
    }

    // Dong hong bi bo qua rieng le.
    public bool Load(string line)
    {
        string[] f = (line ?? "").Split('|');
        float x, y, z;
        CultureInfo ci = CultureInfo.InvariantCulture;
        if (f.Length != 4 || !float.TryParse(f[0], NumberStyles.Float, ci, out x) ||
            !float.TryParse(f[1], NumberStyles.Float, ci, out y) || !float.TryParse(f[2], NumberStyles.Float, ci, out z)) return false;
        return Add(x, y, z, f[3]);
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
    int since;
    public int Phase { get; private set; }

    public void BeginEnter(int now) { Phase = Entering; since = now; }
    public void BeginExit(int now) { Phase = Leaving; since = now; }
    public void Reset() { Phase = Outside; }

    public int Step(int now, bool collisionLoaded, bool insideInterior)
    {
        if (Phase != Entering && Phase != Leaving) return Wait;
        int elapsed = now - since;
        if (elapsed < MinFreezeMs) return Wait;
        if (Phase == Entering)
        {
            if (collisionLoaded && insideInterior) { Phase = Inside; return Done; }
            if (elapsed >= TimeoutMs) { Phase = Outside; return Abort; }
            return Wait;
        }
        // Diem quay ve la cho nguoi choi vua dung tren mat dat nen qua han van tha ra.
        if (collisionLoaded || elapsed >= TimeoutMs) { Phase = Outside; return Done; }
        return Wait;
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
