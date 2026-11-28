// GTA Viet Life: phuc vu truc tiep trong cua hang. Vao quan, den quay tinh tien, bam E goi mon.
// Khong co menu: chon mon bang , va . ngay tai quay. Nguoi ban va nguoi don dep la ped that trong noi that.
// Quay duoc nhan ra bang vat the may tinh tien co san trong game, nen dung duoc o moi noi that co may nay.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeShopFloor : Script
{
    sealed class Dish
    {
        public readonly string Name, Prop, Scenario; public readonly int Price; public readonly float Hunger, Thirst, Energy;
        public Dish(string name, int price, float hunger, float thirst, float energy, string prop, string scenario)
        { Name = name; Price = price; Hunger = hunger; Thirst = thirst; Energy = energy; Prop = prop; Scenario = scenario; }
    }

    static readonly Dish[] Dishes = {
        new Dish("Banh mi", 5, 25f, 0f, 0f, "prop_sandwich_01", "WORLD_HUMAN_EATING"),
        new Dish("Com hop", 8, 40f, 5f, 0f, "prop_food_bs_tray_01", "WORLD_HUMAN_EATING"),
        new Dish("Pho", 10, 45f, 15f, 5f, "prop_food_bs_tray_01", "WORLD_HUMAN_EATING"),
        new Dish("Nuoc suoi", 1, 0f, 30f, 0f, "prop_ld_flow_bottle", "WORLD_HUMAN_DRINKING"),
        new Dish("Ca phe sua da", 3, 0f, 15f, 20f, "p_amb_coffeecup_01", "WORLD_HUMAN_AA_COFFEE") };

    static readonly string[] Registers = { "prop_till_01", "prop_till_02", "prop_till_03", "v_ret_gc_cashreg" };
    const float CounterRange = 2.2f, SearchRange = 14f, LeaveRange = 30f;

    readonly GvlOrder order = new GvlOrder();
    Keys useKey = Keys.E, previousKey = Keys.Oemcomma, nextKey = Keys.OemPeriod, cancelKey = Keys.Back;
    bool enabled = true;
    Vector3 register, behind;
    bool haveShop;
    Ped seller, cleaner;
    bool sellerOwned;
    Prop served;
    readonly List<Vector3> cleaningSpots = new List<Vector3>();
    int cleaningSpot, cleaningUntil, nextSearch;
    bool cleanerWorking;
    // Khach NPC: vao cua, xem ke, ra quay tra tien, ve.
    static readonly string[] Shelves = { "v_ret_247shelves01", "v_ret_247shelves02", "prop_ff_shelves_01", "v_ret_ml_shelfrk", "prop_shelves_01" };
    static readonly string[] CustomerModels = { "a_m_y_hipster_01", "a_f_y_tourist_01", "a_m_m_eastsa_01", "a_f_y_business_01", "a_m_y_skater_01" };
    sealed class Customer { public Ped P; public GvlShopper Plan; public Vector3 Shelf; }
    readonly List<Customer> customers = new List<Customer>();
    readonly List<Vector3> shelves = new List<Vector3>();
    Vector3 entry;
    int nextCustomer, sales;

    public GtaVietLifeShopFloor()
    {
        enabled = Settings.GetValue("ShopFloor", "Enabled", true);
        useKey = Settings.GetValue("Keys", "Order", useKey);
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += delegate { Close(); };
        Log("san sang");
    }

    static object Bus(string key) { return AppDomain.CurrentDomain.GetData(key); }
    static void BusSet(string key, object value) { AppDomain.CurrentDomain.SetData(key, value); }

    static bool MenuOpen()
    {
        foreach (string key in new[] { "GVL.WorldMenu", "GVL.PersonalMenu", "GVL.EconMenu", "GVL.LifeMenu" })
        {
            object value = Bus(key);
            if (value is bool && (bool)value) return true;
        }
        return false;
    }

    void OnTick(object sender, EventArgs e)
    {
        if (!enabled) return;
        try
        {
            Ped me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) { Close(); return; }
            int now = Game.GameTime;
            if (haveShop && me.Position.DistanceTo(register) > LeaveRange) Close();
            if (!haveShop && now > nextSearch) { nextSearch = now + 1000; FindShop(me); }
            if (!haveShop) { BusSet("GVL.ShopCounter", false); return; }

            bool atCounter = !me.IsInVehicle() && me.Position.DistanceTo(register) < CounterRange;
            BusSet("GVL.ShopCounter", atCounter || order.Busy);
            KeepStaff(me, now);
            StepCustomers(now);
            if (order.State == GvlOrder.Choosing && !atCounter) order.Cancel();
            int step = order.Tick(now);
            if (step == GvlOrder.Serve) ServeDish(me);
            else if (step == GvlOrder.Finish) FinishDish(me);
            Draw(atCounter);
        }
        catch (Exception ex) { Log("tick loi: " + ex.Message); }
    }

    // ------------------------------------------------------------ tim quay trong noi that
    void FindShop(Ped me)
    {
        if (me.IsInVehicle() || Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me) == 0) return;
        Vector3 p = me.Position;
        float best = SearchRange;
        Vector3 found = Vector3.Zero;
        foreach (string name in Registers)
        {
            int handle = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE, p.X, p.Y, p.Z, SearchRange,
                Game.GenerateHash(name), false, false, false);
            if (handle == 0) continue;
            Vector3 at = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, handle, true);
            float distance = p.DistanceTo(at);
            if (distance < best) { best = distance; found = at; }
        }
        if (found == Vector3.Zero) return;
        register = found;
        // Khach dung o phia truoc quay; nguoi ban dung phia ben kia may tinh tien.
        Vector3 away = register - p; away.Z = 0f;
        away = away.Length() < 0.1f ? new Vector3(0f, 1f, 0f) : away.Normalized;
        behind = new Vector3(register.X + away.X * 0.75f, register.Y + away.Y * 0.75f, p.Z);
        haveShop = true;
        entry = p;
        FindCleaningSpots(me, p);
        FindShelves(me);
        Log("quan tai " + register.X.ToString("0") + "," + register.Y.ToString("0") + "; cho don dep: " + cleaningSpots.Count);
    }

    void FindCleaningSpots(Ped me, Vector3 entry)
    {
        cleaningSpots.Clear();
        int interior = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me);
        Vector3 chest = entry + new Vector3(0f, 0f, 0.3f);
        for (int i = 0; i < 12 && cleaningSpots.Count < 3; i++)
        {
            double angle = i * Math.PI / 6.0;
            float radius = i % 2 == 0 ? 3.2f : 2.2f;
            Vector3 spot = new Vector3(entry.X + (float)Math.Cos(angle) * radius, entry.Y + (float)Math.Sin(angle) * radius, entry.Z);
            if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, spot.X, spot.Y, spot.Z) != interior) continue;
            if (spot.DistanceTo2D(register) < 1.8f) continue;
            if (World.Raycast(chest, spot + new Vector3(0f, 0f, 0.3f), IntersectFlags.Map | IntersectFlags.Objects, me).DidHit) continue;
            cleaningSpots.Add(spot);
        }
    }

    void FindShelves(Ped me)
    {
        shelves.Clear();
        int interior = Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, me);
        HashSet<int> wanted = new HashSet<int>();
        foreach (string name in Shelves) wanted.Add(Game.GenerateHash(name));
        foreach (Prop prop in World.GetNearbyProps(register, SearchRange))
        {
            if (prop == null || !prop.Exists() || !wanted.Contains(prop.Model.Hash)) continue;
            Vector3 at = prop.Position;
            if (Function.Call<int>(Hash.GET_INTERIOR_AT_COORDS, at.X, at.Y, at.Z) == interior) shelves.Add(at);
        }
        if (shelves.Count == 0) shelves.AddRange(cleaningSpots);   // cua hang goc khong co ke nhan ra duoc
    }

    // ------------------------------------------------------------ khach
    void StepCustomers(int now)
    {
        if (now > nextCustomer && customers.Count < 2)
        {
            nextCustomer = now + 20000;
            Ped ped = SpawnWorker(CustomerModels[(int)((uint)now % (uint)CustomerModels.Length)], entry, HeadingTo(entry, register));
            if (ped != null)
            {
                Customer c = new Customer(); c.P = ped; c.Plan = new GvlShopper(now);
                c.Shelf = shelves.Count > 0 ? shelves[(int)((uint)ped.Handle % (uint)shelves.Count)] : register;
                customers.Add(c);
                GoTo(ped, c.Shelf);
            }
        }
        for (int i = customers.Count - 1; i >= 0; i--)
        {
            Customer c = customers[i];
            if (c.P == null || !c.P.Exists() || c.P.IsDead) { customers.RemoveAt(i); continue; }
            Vector3 target = c.Plan.Stage == GvlShopper.ToShelf ? c.Shelf : c.Plan.Stage == GvlShopper.ToRegister ? FrontOfRegister() : entry;
            bool arrived = c.P.Position.DistanceTo2D(target) < 1.5f;
            int step = c.Plan.Tick(now, arrived);
            if (step == GvlShopper.Browse) Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, c.P, "WORLD_HUMAN_WINDOW_SHOP_BROWSE", 0, true);
            else if (step == GvlShopper.GoPay) { c.P.Task.ClearAll(); GoTo(c.P, FrontOfRegister()); }
            else if (step == GvlShopper.Pay)
            {
                Function.Call(Hash.TASK_TURN_PED_TO_FACE_COORD, c.P, register.X, register.Y, register.Z, 1500);
                sales++;
                Log("khach mua hang (" + sales + " luot)");
                Emit("shop.npc_sale", "x=" + ((int)register.X) + ";y=" + ((int)register.Y));
            }
            else if (step == GvlShopper.Leave) { c.P.Task.ClearAll(); GoTo(c.P, entry); }
            else if (step == GvlShopper.Gone) { c.P.Delete(); customers.RemoveAt(i); }
        }
    }

    Vector3 FrontOfRegister()
    {
        Vector3 toward = entry - register; toward.Z = 0f;
        toward = toward.Length() < 0.1f ? new Vector3(0f, -1f, 0f) : toward.Normalized;
        return new Vector3(register.X + toward.X * 1.1f, register.Y + toward.Y * 1.1f, entry.Z);
    }

    static void GoTo(Ped ped, Vector3 p)
    {
        Function.Call(Hash.TASK_FOLLOW_NAV_MESH_TO_COORD, ped, p.X, p.Y, p.Z, 1.0f, 12000, 0.8f, false, 0f);
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
            list.Add(seq + "\t" + type + "\t" + (data ?? ""));
        }
    }

    // ------------------------------------------------------------ nhan vien
    int nextStaff;

    void KeepStaff(Ped me, int now)
    {
        if (now < nextStaff) return;
        nextStaff = now + 500;
        if (seller == null || !seller.Exists() || seller.IsDead)
        {
            seller = ExistingClerk(me);
            sellerOwned = seller == null;
            if (sellerOwned) seller = SpawnWorker("mp_m_shopkeep_01", behind, HeadingTo(behind, register));
        }
        if (cleaningSpots.Count == 0) return;
        if (cleaner == null || !cleaner.Exists() || cleaner.IsDead)
        {
            cleaner = SpawnWorker("s_f_y_sweatshop_01", cleaningSpots[0], 0f);
            cleaningSpot = 0; cleanerWorking = false; cleaningUntil = 0;
        }
        if (cleaner == null || now < cleaningUntil) return;
        if (cleanerWorking)
        {
            // Quet xong cho nay thi di sang cho khac.
            cleanerWorking = false;
            cleaningSpot = (cleaningSpot + 1) % cleaningSpots.Count;
            Vector3 to = cleaningSpots[cleaningSpot];
            cleaner.Task.ClearAll();
            Function.Call(Hash.TASK_GO_STRAIGHT_TO_COORD, cleaner, to.X, to.Y, to.Z, 1.0f, 6000, 0f, 0.4f);
            cleaningUntil = now + 6000;
        }
        else
        {
            cleanerWorking = true;
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, cleaner, "WORLD_HUMAN_JANITOR", 0, true);
            cleaningUntil = now + 12000;
        }
    }

    // Cua hang cua game co the da co nguoi ban: dung nguoi do, khong sinh them.
    Ped ExistingClerk(Ped me)
    {
        foreach (Ped ped in World.GetNearbyPeds(register, 2.0f))
            if (ped != null && ped.Exists() && ped != me && ped != cleaner && ped.IsAlive && ped.IsHuman) return ped;
        return null;
    }

    Ped SpawnWorker(string modelName, Vector3 at, float heading)
    {
        Model model = new Model(modelName);
        if (!model.IsInCdImage || !model.Request(1000)) { Log("khong nap duoc nhan vat " + modelName); return null; }
        Ped ped = World.CreatePed(model, at, heading);
        model.MarkAsNoLongerNeeded();
        if (ped == null || !ped.Exists()) return null;
        ped.IsPersistent = true;
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true);
        return ped;
    }

    static float HeadingTo(Vector3 from, Vector3 to)
    {
        return (float)(Math.Atan2(-(to.X - from.X), to.Y - from.Y) * 180.0 / Math.PI);
    }

    // ------------------------------------------------------------ goi mon
    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!enabled || !haveShop || MenuOpen()) return;
        try
        {
            Ped me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) return;
            if (order.State == GvlOrder.Choosing)
            {
                if (e.KeyCode == previousKey) order.Move(-1, Dishes.Length);
                else if (e.KeyCode == nextKey) order.Move(1, Dishes.Length);
                else if (e.KeyCode == cancelKey) order.Cancel();
                else if (e.KeyCode == useKey) Buy(me);
                return;
            }
            if (e.KeyCode == useKey && !order.Busy && !me.IsInVehicle() && me.Position.DistanceTo(register) < CounterRange)
            {
                order.Open();
                me.Heading = HeadingTo(me.Position, register);
                if (seller != null && seller.Exists()) Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, seller, me, 2000);
            }
        }
        catch (Exception ex) { Log("phim loi: " + ex.Message); }
    }

    void Buy(Ped me)
    {
        Dish dish = Dishes[order.Choice];
        Func<int, string, string, bool> pay = Bus("GVL.PayCash") as Func<int, string, string, bool>;
        bool paid;
        if (pay != null) paid = pay(dish.Price, "food", dish.Name);
        else
        {
            paid = Game.Player.Money >= dish.Price;
            if (paid) Game.Player.Money = Game.Player.Money - dish.Price;
        }
        if (!paid) { Notify("~r~Khong du tien~s~ (can $" + dish.Price + ")."); order.Cancel(); return; }
        order.Paid(Game.GameTime);
        Log("goi mon " + dish.Name);
        if (sellerOwned && seller != null && seller.Exists())
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, seller, "PROP_HUMAN_BUM_BIN", 0, true);   // cui xuong lay mon
    }

    void ServeDish(Ped me)
    {
        Dish dish = Dishes[order.Choice];
        if (sellerOwned && seller != null && seller.Exists())
        {
            seller.Task.ClearAll();
            Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, seller, me, 2000);
        }
        RemoveServed();
        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, me, dish.Scenario, 0, true);
        Model model = new Model(dish.Prop);
        if (model.IsInCdImage && model.Request(500))
        {
            served = World.CreateProp(model, register + new Vector3(0f, 0f, 0.1f), false, false);
            if (served != null && served.Exists()) served.IsPositionFrozen = true;
            model.MarkAsNoLongerNeeded();
        }
        Notify("Moi dung ~g~" + dish.Name + "~s~.");
    }

    void FinishDish(Ped me)
    {
        Dish dish = Dishes[order.Choice];
        Log("hoan tat " + dish.Name);
        me.Task.ClearAll();
        RemoveServed();
        float[] pending = Bus("GVL.NeedsAdd") as float[];
        float hunger = dish.Hunger, thirst = dish.Thirst, energy = dish.Energy;
        if (pending != null && pending.Length >= 3) { hunger += pending[0]; thirst += pending[1]; energy += pending[2]; }
        BusSet("GVL.NeedsAdd", new float[] { hunger, thirst, energy });
        Notify("~g~Ngon!~s~" + (dish.Hunger > 0f ? " No +" + (int)dish.Hunger : "") + (dish.Thirst > 0f ? " Nuoc +" + (int)dish.Thirst : "")
            + (dish.Energy > 0f ? " Nang luong +" + (int)dish.Energy : ""));
    }

    void RemoveServed()
    {
        if (served != null && served.Exists()) served.Delete();
        served = null;
    }

    void Draw(bool atCounter)
    {
        Function.Call(Hash.DRAW_MARKER, 2, register.X, register.Y, register.Z + 0.55f, 0f, 0f, 0f, 180f, 0f, 0f, 0.16f, 0.16f, 0.16f,
            255, 205, 90, 160, false, true, 2, false, 0, 0, false);
        if (MenuOpen()) return;
        if (order.State == GvlOrder.Choosing)
        {
            Dish d = Dishes[order.Choice];
            GTA.UI.Screen.ShowSubtitle("~y~< ,~s~  " + d.Name + "  $" + d.Price + "  ~y~. >~s~    ~g~" + useKey + "~s~: mua    Backspace: thoi", 100);
        }
        else if (order.State == GvlOrder.Preparing) GTA.UI.Screen.ShowSubtitle("Dang lam mon...", 100);
        else if (order.State == GvlOrder.Eating) GTA.UI.Screen.ShowSubtitle("Dang dung " + Dishes[order.Choice].Name + "...", 100);
        else if (atCounter) GTA.UI.Screen.ShowSubtitle("~y~" + useKey + "~s~: goi mon", 100);
    }

    // ------------------------------------------------------------ don dep
    void Close()
    {
        try
        {
            bool eating = order.State == GvlOrder.Eating;
            order.Cancel();
            RemoveServed();
            Ped me = Game.Player.Character;
            if (eating && me != null && me.Exists()) me.Task.ClearAll();
            if (seller != null && seller.Exists()) { if (sellerOwned) seller.Delete(); else seller.Task.ClearAll(); }
            if (cleaner != null && cleaner.Exists()) cleaner.Delete();
            foreach (Customer c in customers) if (c.P != null && c.P.Exists()) c.P.Delete();
        }
        catch (Exception ex) { Log("don dep loi: " + ex.Message); }
        seller = null; cleaner = null; haveShop = false; cleaningSpots.Clear(); customers.Clear(); shelves.Clear();
        BusSet("GVL.ShopCounter", false);
    }

    void Notify(string message) { GTA.UI.Notification.Show(message); }

    static void Log(string message)
    {
        try { File.AppendAllText("GtaVietLifeShopFloor.log", DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine); }
        catch (IOException) { }
    }
}

// <gvl-testable:order>
// Thu tu mot lan goi mon: dong -> dang chon -> dang lam -> dang an -> dong.
public sealed class GvlOrder
{
    public const int Closed = 0, Choosing = 1, Preparing = 2, Eating = 3;
    public const int Nothing = 0, Serve = 1, Finish = 2;
    public const int PrepareMs = 3500, EatMs = 7000;
    int until;

    public int State { get; private set; }
    public int Choice { get; private set; }
    public bool Busy { get { return State != Closed; } }

    public void Open() { if (State == Closed) State = Choosing; }

    public void Move(int step, int count)
    {
        if (State == Choosing && count > 0) Choice = ((Choice + step) % count + count) % count;
    }

    // Chi huy duoc khi chua tra tien, tru khi roi quan (goi tu noi don dep).
    public void Cancel() { State = Closed; }

    public void Paid(int now)
    {
        if (State != Choosing) return;
        State = Preparing; until = now + PrepareMs;
    }

    public int Tick(int now)
    {
        if ((State != Preparing && State != Eating) || now < until) return Nothing;
        if (State == Preparing) { State = Eating; until = now + EatMs; return Serve; }
        State = Closed;
        return Finish;
    }
}

// Mot khach: toi ke -> xem hang -> ra quay -> tra tien -> ve cua -> bien mat. Ket o buoc nao qua lau thi sang buoc sau.
public sealed class GvlShopper
{
    public const int ToShelf = 0, Browsing = 1, ToRegister = 2, Paying = 3, Leaving = 4, Done = 5;
    public const int Nothing = 0, Browse = 1, GoPay = 2, Pay = 3, Leave = 4, Gone = 5;
    public const int WalkLimitMs = 15000, BrowseMs = 7000, PayMs = 4000;
    int until;
    public int Stage { get; private set; }

    public GvlShopper(int now) { until = now + WalkLimitMs; }

    public int Tick(int now, bool arrived)
    {
        switch (Stage)
        {
            case ToShelf:
                if (!arrived && now < until) return Nothing;
                Stage = Browsing; until = now + BrowseMs; return Browse;
            case Browsing:
                if (now < until) return Nothing;
                Stage = ToRegister; until = now + WalkLimitMs; return GoPay;
            case ToRegister:
                if (!arrived && now < until) return Nothing;
                Stage = Paying; until = now + PayMs; return Pay;
            case Paying:
                if (now < until) return Nothing;
                Stage = Leaving; until = now + WalkLimitMs; return Leave;
            case Leaving:
                if (!arrived && now < until) return Nothing;
                Stage = Done; return Gone;
            default:
                return Nothing;
        }
    }
}
// </gvl-testable:order>
