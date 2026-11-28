// GTA Viet Life: phuc vu truc tiep trong cua hang. Vao quan, den quay tinh tien, bam E goi mon.
// Khong co menu: chon mon bang , va . ngay tai quay. Nguoi ban va nguoi don dep la ped that trong noi that.
// Quay duoc nhan ra bang vat the may tinh tien co san trong game, nen dung duoc o moi noi that co may nay.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class GtaVietLifeShopFloor : Script
{
    // Hang hoa doc tu Data Pack (scripts\GtaVietLifeData\vi-VN); thieu file thi dung vai mon co san.
    readonly List<GvlItem> catalog = new List<GvlItem>();
    readonly List<GvlItem> counterMenu = new List<GvlItem>();
    readonly GvlBasket basket = new GvlBasket();
    int shelfAt = -1, shelfPick;
    GvlItem serving;
    float[] servingNeeds = new float[3];

    void LoadCatalog()
    {
        string[] folders = {
            Path.Combine("scripts", "GtaVietLifeData", "vi-VN"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "GtaVietLifeData", "vi-VN") };
        foreach (string folder in folders)
        {
            string menuFile = Path.Combine(folder, "food_drink_menu.json"), goodsFile = Path.Combine(folder, "goods_prices.json");
            if (!File.Exists(menuFile) || !File.Exists(goodsFile)) continue;
            try
            {
                JavaScriptSerializer json = new JavaScriptSerializer();
                foreach (object o in (object[])json.DeserializeObject(File.ReadAllText(menuFile, Encoding.UTF8)))
                {
                    Dictionary<string, object> d = (Dictionary<string, object>)o;
                    catalog.Add(GvlGoods.Make(Text(d, "id"), Text(d, "name"), Text(d, "category"), "", Number(d, "price_min"), Number(d, "price_max")));
                }
                foreach (object o in (object[])json.DeserializeObject(File.ReadAllText(goodsFile, Encoding.UTF8)))
                {
                    Dictionary<string, object> d = (Dictionary<string, object>)o;
                    catalog.Add(GvlGoods.Make(Text(d, "id"), Text(d, "name"), "grocery", Text(d, "unit"), Number(d, "price_min"), Number(d, "price_max")));
                }
                Log("nap " + catalog.Count + " mat hang tu " + folder);
                break;
            }
            catch (Exception ex) { catalog.Clear(); Log("doc Data Pack loi (" + folder + "): " + ex.Message); }
        }
        if (catalog.Count == 0)
        {
            catalog.Add(GvlGoods.Make("b1", "Bánh mì", "food", "", 20000, 30000));
            catalog.Add(GvlGoods.Make("b2", "Phở bò", "food", "", 35000, 65000));
            catalog.Add(GvlGoods.Make("b3", "Nước suối", "drink", "", 5000, 10000));
            catalog.Add(GvlGoods.Make("b4", "Cà phê sữa đá", "drink", "", 15000, 30000));
            catalog.Add(GvlGoods.Make("b5", "Gạo", "grocery", "kg", 15000, 30000));
            Log("khong thay Data Pack, dung " + catalog.Count + " mon co san");
        }
        foreach (GvlItem item in catalog) if (item.ReadyToEat && item.Category != "snack") counterMenu.Add(item);
        if (counterMenu.Count == 0) counterMenu.AddRange(catalog);
    }

    static string Text(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v != null ? v.ToString() : ""; }
    static int Number(Dictionary<string, object> d, string key)
    {
        object v;
        try { return d.TryGetValue(key, out v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0; }
        catch (Exception) { return 0; }
    }

    static string PropFor(GvlItem item)
    {
        if (item.Category == "drink") return item.Name.ToLowerInvariant().Contains("ca phe") ? "p_amb_coffeecup_01" : "prop_ld_flow_bottle";
        return item.Category == "food" ? "prop_food_bs_tray_01" : "prop_sandwich_01";
    }

    static string ScenarioFor(GvlItem item)
    {
        if (item.Category == "drink") return item.Name.ToLowerInvariant().Contains("ca phe") ? "WORLD_HUMAN_AA_COFFEE" : "WORLD_HUMAN_DRINKING";
        return "WORLD_HUMAN_EATING";
    }

    static readonly string[] Registers = { "prop_till_01", "prop_till_02", "prop_till_03", "v_ret_gc_cashreg" };
    const float CounterRange = 2.2f, SearchRange = 14f, LeaveRange = 30f, ShelfRange = 1.8f;

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
        LoadCatalog();
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
            int near = atCounter || order.Busy ? -1 : NearestShelf(me.Position);
            if (near != shelfAt) { shelfAt = near; shelfPick = 0; }
            BusSet("GVL.ShopCounter", atCounter || order.Busy || shelfAt >= 0);
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
                int shelf = shelves.IndexOf(c.Shelf);
                List<GvlItem> onShelf = ShelfItems(shelf < 0 ? 0 : shelf);
                GvlItem bought = onShelf[(int)((uint)(c.P.Handle + now) % (uint)onShelf.Count)];
                Log("khach mua " + bought.Name + " $" + bought.Price + " (" + sales + " luot)");
                Emit("shop.npc_sale", "x=" + ((int)register.X) + ";y=" + ((int)register.Y) + ";item=" + bought.Id + ";price=" + bought.Price);
            }
            else if (step == GvlShopper.Leave) { c.P.Task.ClearAll(); GoTo(c.P, entry); }
            else if (step == GvlShopper.Gone) { c.P.Delete(); customers.RemoveAt(i); }
        }
    }

    int NearestShelf(Vector3 p)
    {
        int best = -1; float bestDistance = ShelfRange;
        for (int i = 0; i < shelves.Count; i++)
        {
            float d = p.DistanceTo2D(shelves[i]);
            if (d < bestDistance && Math.Abs(p.Z - shelves[i].Z) < 2.5f) { bestDistance = d; best = i; }
        }
        return best;
    }

    List<GvlItem> ShelfItems(int shelf) { return GvlGoods.OnShelf(catalog, GvlGoods.ShelfKind(shelf)); }

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
                if (e.KeyCode == previousKey) order.Move(-1, counterMenu.Count);
                else if (e.KeyCode == nextKey) order.Move(1, counterMenu.Count);
                else if (e.KeyCode == cancelKey) order.Cancel();
                else if (e.KeyCode == useKey) Buy(me);
                return;
            }
            if (order.Busy || me.IsInVehicle()) return;
            // Dung truoc ke: xem tung mon tren ke, E bo vao gio.
            if (shelfAt >= 0)
            {
                List<GvlItem> onShelf = ShelfItems(shelfAt);
                if (e.KeyCode == previousKey) shelfPick = (shelfPick - 1 + onShelf.Count) % onShelf.Count;
                else if (e.KeyCode == nextKey) shelfPick = (shelfPick + 1) % onShelf.Count;
                else if (e.KeyCode == useKey) TakeFromShelf(me, onShelf[shelfPick % onShelf.Count]);
                return;
            }
            bool atCounter = me.Position.DistanceTo(register) < CounterRange;
            if (e.KeyCode == useKey && atCounter && basket.Count > 0) { Checkout(me); return; }
            if (e.KeyCode == cancelKey && basket.Count > 0) { basket.Clear(); Notify("Da tra hang ve ke."); return; }
            if (e.KeyCode == useKey && atCounter)
            {
                order.Open();
                me.Heading = HeadingTo(me.Position, register);
                if (seller != null && seller.Exists()) Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, seller, me, 2000);
            }
        }
        catch (Exception ex) { Log("phim loi: " + ex.Message); }
    }

    static bool PayCash(int amount, string category, string note)
    {
        Func<int, string, string, bool> pay = Bus("GVL.PayCash") as Func<int, string, string, bool>;
        if (pay != null) return pay(amount, category, note);
        if (Game.Player.Money < amount) return false;
        Game.Player.Money = Game.Player.Money - amount;
        return true;
    }

    void TakeFromShelf(Ped me, GvlItem item)
    {
        if (!basket.Add(item)) { Notify("Gio hang day (" + GvlBasket.Capacity + " mon). Ra quay tra tien."); return; }
        me.Heading = HeadingTo(me.Position, shelves[shelfAt]);
        Function.Call(Hash.REQUEST_ANIM_DICT, "mp_common");
        Function.Call(Hash.TASK_PLAY_ANIM, me, "mp_common", "givetake1_a", 8f, -8f, 1200, 48, 0f, false, false, false);
        Notify("Lay ~g~" + item.Name + "~s~ $" + item.Price + ". Gio: " + basket.Count + " mon, $" + basket.Total() + ".");
        Log("lay tu ke " + item.Name + " $" + item.Price);
    }

    // Tra tien ca gio: do an/uong dung ngay tai quan, tap hoa mang ve tu lanh.
    void Checkout(Ped me)
    {
        int total = basket.Total();
        List<GvlItem> items = basket.Items;
        if (!PayCash(total, "shopping", "mua " + items.Count + " mon")) { Notify("~r~Khong du tien~s~ (gio hang $" + total + ")."); return; }
        basket.Clear();
        me.Heading = HeadingTo(me.Position, register);
        if (seller != null && seller.Exists()) Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, seller, me, 2000);
        List<string> fridgeLines = Bus("GVL.FridgeAdd") as List<string>;
        if (fridgeLines == null) { fridgeLines = new List<string>(); BusSet("GVL.FridgeAdd", fridgeLines); }
        GvlItem first = null; float h = 0f, t = 0f, en = 0f; int groceries = 0;
        foreach (GvlItem item in items)
        {
            if (!item.ReadyToEat)
            {
                lock (fridgeLines)
                    fridgeLines.Add(item.Name + "|" + item.Hunger.ToString(CultureInfo.InvariantCulture) + "|" + item.Thirst.ToString(CultureInfo.InvariantCulture) +
                        "|" + item.Energy.ToString(CultureInfo.InvariantCulture) + "|" + item.Days);
                groceries++;
                continue;
            }
            if (first == null) first = item;
            h += item.Hunger; t += item.Thirst; en += item.Energy;
        }
        Log("tra tien gio hang $" + total + ": " + items.Count + " mon, " + groceries + " mon vao tu lanh");
        Emit("shop.purchase", "total=" + total + ";items=" + items.Count + ";x=" + ((int)register.X) + ";y=" + ((int)register.Y));
        Notify("Da tra ~g~$" + total + "~s~." + (groceries > 0 ? " " + groceries + " mon tap hoa se cat vao tu lanh." : ""));
        if (first == null) return;
        serving = first; servingNeeds = new float[] { h, t, en };
        order.Open(); order.Paid(Game.GameTime);
    }

    void Buy(Ped me)
    {
        GvlItem dish = counterMenu[order.Choice % counterMenu.Count];
        if (!PayCash(dish.Price, "food", dish.Name)) { Notify("~r~Khong du tien~s~ (can $" + dish.Price + ")."); order.Cancel(); return; }
        serving = dish; servingNeeds = new float[] { dish.Hunger, dish.Thirst, dish.Energy };
        order.Paid(Game.GameTime);
        Log("goi mon " + dish.Name);
        if (sellerOwned && seller != null && seller.Exists())
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, seller, "PROP_HUMAN_BUM_BIN", 0, true);   // cui xuong lay mon
    }

    void ServeDish(Ped me)
    {
        GvlItem dish = serving ?? counterMenu[0];
        if (sellerOwned && seller != null && seller.Exists())
        {
            seller.Task.ClearAll();
            Function.Call(Hash.TASK_TURN_PED_TO_FACE_ENTITY, seller, me, 2000);
        }
        RemoveServed();
        Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, me, ScenarioFor(dish), 0, true);
        Model model = new Model(PropFor(dish));
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
        GvlItem dish = serving ?? counterMenu[0];
        Log("hoan tat " + dish.Name);
        me.Task.ClearAll();
        RemoveServed();
        float[] pending = Bus("GVL.NeedsAdd") as float[];
        float hunger = servingNeeds[0], thirst = servingNeeds[1], energy = servingNeeds[2];
        if (pending != null && pending.Length >= 3) { hunger += pending[0]; thirst += pending[1]; energy += pending[2]; }
        BusSet("GVL.NeedsAdd", new float[] { hunger, thirst, energy });
        Notify("~g~Ngon!~s~" + (servingNeeds[0] > 0f ? " No +" + (int)servingNeeds[0] : "") + (servingNeeds[1] > 0f ? " Nuoc +" + (int)servingNeeds[1] : "")
            + (servingNeeds[2] > 0f ? " Nang luong +" + (int)servingNeeds[2] : ""));
        serving = null;
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
        for (int i = 0; i < shelves.Count; i++)   // cham nho tren moi ke de biet cho lay hang
            Function.Call(Hash.DRAW_MARKER, 2, shelves[i].X, shelves[i].Y, shelves[i].Z + 1.9f, 0f, 0f, 0f, 180f, 0f, 0f, 0.14f, 0.14f, 0.14f,
                60, 230, 90, 140, false, true, 2, false, 0, 0, false);
        string cart = basket.Count > 0 ? "   ~b~Gio: " + basket.Count + " mon $" + basket.Total() + "~s~" : "";
        if (order.State == GvlOrder.Choosing)
        {
            GvlItem d = counterMenu[order.Choice % counterMenu.Count];
            GTA.UI.Screen.ShowSubtitle("~y~< ,~s~  " + d.Name + "  $" + d.Price + "  ~y~. >~s~    ~g~" + useKey + "~s~: mua    Backspace: thoi", 100);
        }
        else if (order.State == GvlOrder.Preparing) GTA.UI.Screen.ShowSubtitle("Dang chuan bi...", 100);
        else if (order.State == GvlOrder.Eating) GTA.UI.Screen.ShowSubtitle("Dang dung " + (serving != null ? serving.Name : "") + "...", 100);
        else if (shelfAt >= 0)
        {
            List<GvlItem> onShelf = ShelfItems(shelfAt);
            GvlItem it = onShelf[shelfPick % onShelf.Count];
            GTA.UI.Screen.ShowSubtitle("Ke ~y~" + GvlGoods.KindName(GvlGoods.ShelfKind(shelfAt)) + "~s~: " + it.Name + (it.Unit.Length > 0 ? " (" + it.Unit + ")" : "") +
                " $" + it.Price + "  ~y~, .~s~ xem (" + (shelfPick % onShelf.Count + 1) + "/" + onShelf.Count + ")  ~g~" + useKey + "~s~: lay" + cart, 100);
        }
        else if (atCounter && basket.Count > 0) GTA.UI.Screen.ShowSubtitle("~g~" + useKey + "~s~: tra tien " + basket.Count + " mon ($" + basket.Total() + ")    Backspace: tra lai ke", 100);
        else if (atCounter) GTA.UI.Screen.ShowSubtitle("~y~" + useKey + "~s~: goi mon tai quay    (lay hang o ke roi mang ra day)", 100);
        else if (basket.Count > 0) GTA.UI.Screen.ShowSubtitle("Mang gio hang ra quay tra tien." + cart, 100);
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
        if (basket.Count > 0) Log("roi quan, tra " + basket.Count + " mon chua tinh tien ve ke");
        basket.Clear(); shelfAt = -1; serving = null;
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

// Mot mat hang tren ke. Gia VND trong Data Pack doi ra do-la cua game (1 USD = 5.000 VND).
public sealed class GvlItem
{
    public string Id = "", Name = "", Category = "", Unit = "";
    public int Price, Days;
    public float Hunger, Thirst, Energy;
    public bool ReadyToEat { get { return Category != "grocery"; } }
}

public static class GvlGoods
{
    public const int VndPerDollar = 5000;
    public static readonly string[] ShelfKinds = { "drink", "snack", "grocery", "food", "dessert" };
    static readonly string[] KindNames = { "Do uong", "Do an vat", "Tap hoa", "Do an san", "Trang mieng" };

    public static int Dollars(int vndMin, int vndMax)
    {
        double mid = ((double)Math.Max(0, vndMin) + Math.Max(0, vndMax)) / 2.0;
        return (int)Math.Max(1, Math.Round(mid / VndPerDollar));
    }

    // Chu GTA khong hien du dau tieng Viet: bo dau, giu chu.
    public static string Ascii(string text)
    {
        string d = (text ?? "").Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        StringBuilder b = new StringBuilder(d.Length);
        foreach (char c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(c);
        return b.ToString().Normalize(NormalizationForm.FormC);
    }

    public static GvlItem Make(string id, string name, string category, string unit, int vndMin, int vndMax)
    {
        GvlItem item = new GvlItem();
        item.Id = id ?? ""; item.Name = Ascii(name); item.Unit = Ascii(unit);
        item.Category = string.IsNullOrEmpty(category) ? "grocery" : category;
        item.Price = Dollars(vndMin, vndMax);
        string lower = item.Name.ToLowerInvariant();
        switch (item.Category)
        {
            case "food": item.Hunger = 40f; item.Thirst = 5f; break;
            case "drink": item.Thirst = 30f; item.Energy = lower.Contains("ca phe") || lower.Contains("tra") ? 15f : 0f; break;
            case "snack": item.Hunger = 12f; break;
            case "dessert": item.Hunger = 10f; item.Thirst = 8f; break;
            default: item.Hunger = 20f; item.Days = lower.Contains("thit") || lower.Contains("ca ") || lower.Contains("rau") ? 3 : 10; break;
        }
        return item;
    }

    public static string ShelfKind(int shelfIndex) { return ShelfKinds[((shelfIndex % ShelfKinds.Length) + ShelfKinds.Length) % ShelfKinds.Length]; }

    public static string KindName(string kind)
    {
        int i = Array.IndexOf(ShelfKinds, kind);
        return i >= 0 ? KindNames[i] : "Hang hoa";
    }

    public static List<GvlItem> OnShelf(List<GvlItem> all, string kind)
    {
        List<GvlItem> found = all.FindAll(delegate(GvlItem x) { return x.Category == kind; });
        return found.Count > 0 ? found : new List<GvlItem>(all);
    }
}

// Gio hang: lay tu ke, tra tien mot lan o quay.
public sealed class GvlBasket
{
    public const int Capacity = 10;
    readonly List<GvlItem> items = new List<GvlItem>();
    public int Count { get { return items.Count; } }
    public List<GvlItem> Items { get { return new List<GvlItem>(items); } }

    public bool Add(GvlItem item)
    {
        if (item == null || items.Count >= Capacity) return false;
        items.Add(item); return true;
    }

    public int Total()
    {
        long sum = 0;
        foreach (GvlItem i in items) sum += i.Price;
        return (int)Math.Min(int.MaxValue, sum);
    }

    public void Clear() { items.Clear(); }
}
// </gvl-testable:order>
