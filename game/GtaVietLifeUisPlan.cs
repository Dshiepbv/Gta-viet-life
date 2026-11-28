// GTA Viet Life: quy tac thuan cua UIS (khong goi API GTA). Bien dich vao GtaVietLifeInteriors.dll.
// tests/test_csharp_logic.py bien dich khoi duoi day cung harness va chay that.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// <gvl-testable:uisplan>
// Quy mo toa nha: Tier 1 < 35 m2, Tier 2 35-90, Tier 3 90-180, Tier 4 > 180 (chi khu giau).
public static class GvlTier
{
    public const int Poor = 0, Middle = 1, Rich = 2;
    public const int Home = 0, Shop = 1;

    static readonly string[] RichZones = { "RICHM", "ROCKF", "BHAMCA", "CHIL", "GOLF", "PBLUFF", "RGLEN", "TONGVAH", "BANHAMC", "MORN", "WVINE" };
    static readonly string[] PoorZones = { "STRAW", "DAVIS", "RANCHO", "CHAMH", "SANDY", "GRAPES", "STAD", "CYPRE", "ELYSIAN", "MURRI", "ZANCUDO", "HARMO", "PALETO", "EAST_V", "LMESA" };
    static readonly string[] CommercialZones = { "DOWNT", "PBOX", "LEGSQU", "TEXTI", "VINE", "DTVINE", "ALTA", "BURTON", "HAWICK", "KOREAT", "LMESA", "DELPE", "STRAW", "SKID", "VCANA", "DELBE", "MIRR" };

    static bool In(string[] list, string zone)
    {
        foreach (string z in list) if (string.Equals(z, zone, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static int ZoneWealth(string zone)
    {
        if (In(RichZones, zone ?? "")) return Rich;
        return In(PoorZones, zone ?? "") ? Poor : Middle;
    }

    public static bool Commercial(string zone) { return In(CommercialZones, zone ?? ""); }

    public static int BySize(float area)
    {
        if (area < 35f) return 1;
        if (area < 90f) return 2;
        if (area <= 180f) return 3;
        return 4;
    }

    // heightM: chieu cao mai so voi cua. Nha cao tang ngoai khu giau la can ho tung phong, khong phai dinh thu.
    public static int Classify(float area, float heightM, string zone)
    {
        int tier = BySize(area);
        int wealth = ZoneWealth(zone);
        if (heightM > 10f && wealth != Rich) tier = Math.Min(tier, 2);
        if (tier == 4 && wealth != Rich) tier = 3;
        if (wealth == Poor) tier = Math.Min(tier, 3);
        return Math.Max(1, tier);
    }

    // Co dinh theo toa nha: cung mot cua luon cung cong nang.
    public static int Purpose(string buildingId, string zone, int tier)
    {
        uint roll = GvlTemplates.StableHash("purpose/" + buildingId) % 100u;
        uint shopChance = Commercial(zone) ? (tier == 1 ? 70u : 55u) : 15u;
        if (tier == 4 && ZoneWealth(zone) == Rich) shopChance = Commercial(zone) ? 40u : 5u;
        return roll < shopChance ? Shop : Home;
    }

    public static int HouseholdSize(string buildingId, int tier)
    {
        int max = tier <= 1 ? 1 : tier == 2 ? 2 : 3;
        return 1 + (int)(GvlTemplates.StableHash("household/" + buildingId) % (uint)max);
    }
}

// Dien tich mat bang: o luoi co mai (tia tu tren xuong cham vat the cao hon cua) noi lien voi o sau cua.
public static class GvlFootprint
{
    public const float Cell = 2f;
    public const int MaxRadius = 12;   // toi da 25 x 25 o = 2.500 m2

    public static int Cells(Func<int, int, bool> covered)
    {
        if (!covered(0, 0)) return 0;
        HashSet<long> seen = new HashSet<long>();
        Queue<int[]> open = new Queue<int[]>();
        open.Enqueue(new[] { 0, 0 }); seen.Add(0L);
        int count = 0;
        while (open.Count > 0)
        {
            int[] c = open.Dequeue();
            count++;
            int[][] next = { new[] { c[0] + 1, c[1] }, new[] { c[0] - 1, c[1] }, new[] { c[0], c[1] + 1 }, new[] { c[0], c[1] - 1 } };
            foreach (int[] n in next)
            {
                if (Math.Abs(n[0]) > MaxRadius || Math.Abs(n[1]) > MaxRadius) continue;
                long key = ((long)n[0] << 32) ^ (uint)n[1];
                if (!seen.Add(key) || !covered(n[0], n[1])) continue;
                open.Enqueue(n);
            }
        }
        return count;
    }

    public static float Area(int cells) { return cells * Cell * Cell; }
}

// Loai do vat: hanh dong, thoi gian, hieu ung va cac mo hinh goc nhan ra hoac dung de dat.
public sealed class GvlObjectKind
{
    public readonly string Id, Label, Scenario; public readonly int Seconds;
    public readonly float Hunger, Thirst, Energy, Hygiene, Fun;
    public readonly bool Sit; public readonly string[] Models;
    public GvlObjectKind(string id, string label, string scenario, int seconds, float hunger, float thirst, float energy,
        float hygiene, float fun, bool sit, params string[] models)
    {
        Id = id; Label = label; Scenario = scenario; Seconds = seconds; Hunger = hunger; Thirst = thirst; Energy = energy;
        Hygiene = hygiene; Fun = fun; Sit = sit; Models = models;
    }
}

public static class GvlObjects
{
    public static readonly GvlObjectKind[] Kinds = {
        new GvlObjectKind("bed", "ngu", "WORLD_HUMAN_BUM_SLEEP", 12, 0f, 0f, 35f, 0f, 0f, false,
            "v_res_msonbed", "v_res_mbbed", "v_res_mdbed", "v_res_d_bed", "v_res_tre_bed1", "v_res_tre_bed2", "apa_mp_h_bed_double_08"),
        new GvlObjectKind("chair", "ngoi nghi", "PROP_HUMAN_SEAT_CHAIR_MP_PLAYER", 8, 0f, 0f, 6f, 0f, 2f, true,
            "prop_chair_01a", "prop_chair_01b", "prop_chair_04a", "v_res_fh_dineeamesa", "prop_table_03_chr", "v_ilev_chair02_ped"),
        new GvlObjectKind("sofa", "ngoi xem tivi", "PROP_HUMAN_SEAT_ARMCHAIR", 10, 0f, 0f, 8f, 0f, 10f, true,
            "prop_couch_01", "prop_couch_03", "v_res_tre_sofa", "v_res_m_l_sofa", "apa_mp_h_stn_sofacorn_01"),
        new GvlObjectKind("table", "an com", "WORLD_HUMAN_EATING", 8, 20f, 5f, 0f, 0f, 0f, false,
            "prop_table_03", "prop_table_01", "prop_table_02", "v_res_fh_dinetable"),
        new GvlObjectKind("stove", "nau an", "PROP_HUMAN_BBQ", 12, 35f, 0f, 0f, 0f, 2f, false,
            "prop_cooker_03", "v_res_cooker", "prop_bbq_1"),
        new GvlObjectKind("fridge", "lay do uong", "WORLD_HUMAN_DRINKING", 6, 8f, 25f, 0f, 0f, 0f, false,
            "prop_fridge_01", "prop_fridge_03", "v_res_fridgemoda", "v_res_fridgemodsml"),
        new GvlObjectKind("shelf", "xep hang", "PROP_HUMAN_PARKING_METER", 8, 0f, 0f, -3f, 0f, 0f, false,
            "v_ret_247shelves01", "v_ret_247shelves02", "prop_ff_shelves_01", "v_ret_ml_shelfrk", "prop_shelves_01"),
        new GvlObjectKind("counter", "lau quay", "WORLD_HUMAN_MAID_CLEAN", 8, 0f, 0f, -2f, 0f, 0f, false,
            "prop_ff_counter_01", "v_ret_247_counter", "prop_bar_cooler_01"),
        new GvlObjectKind("register", "dem tien", "PROP_HUMAN_ATM", 6, 0f, 0f, 0f, 0f, 0f, false,
            "prop_till_01", "prop_till_02", "prop_till_03", "v_ret_gc_cashreg"),
        new GvlObjectKind("toilet", "ve sinh", "WORLD_HUMAN_STAND_IMPATIENT", 6, 0f, 0f, 0f, 8f, 0f, false,
            "prop_toilet_01", "prop_toilet_02"),
        new GvlObjectKind("shower", "tam", "WORLD_HUMAN_STAND_IMPATIENT_UPRIGHT", 10, 0f, 0f, 5f, 60f, 3f, false,
            "prop_shower_rack_01", "v_res_mbtaps", "prop_sink_02"),
        new GvlObjectKind("tv", "xem tivi", "WORLD_HUMAN_STAND_IMPATIENT", 10, 0f, 0f, 3f, 0f, 12f, false,
            "prop_tv_flat_01", "prop_tv_flat_02", "prop_tv_03", "v_res_tre_tv"),
        new GvlObjectKind("computer", "lam viec", "PROP_HUMAN_SEAT_COMPUTER", 10, 0f, 0f, -4f, 0f, 2f, true,
            "prop_laptop_01a", "prop_monitor_01a", "prop_off_desk_01", "v_corp_offdesk", "prop_table_04") };

    public static GvlObjectKind Find(string id)
    {
        foreach (GvlObjectKind k in Kinds) if (k.Id == id) return k;
        return null;
    }

    // Do nguoi choi mua them de bay trong nha hoac quan (kieu Big Ambitions).
    public static readonly string[] Buyable = { "chair", "sofa", "table", "bed", "tv", "fridge", "stove", "shelf", "computer" };

    public static int Price(string kind)
    {
        switch (kind)
        {
            case "bed": return 350; case "sofa": return 280; case "tv": return 400; case "fridge": return 300;
            case "stove": return 320; case "shelf": return 180; case "computer": return 450; case "table": return 120;
            default: return 60;
        }
    }

    // Lich sinh hoat cua ho gia dinh theo gio game.
    public static string[] Wanted(int hour)
    {
        if (hour < 6 || hour >= 23) return new[] { "bed", "sofa" };
        if (hour < 9) return new[] { "stove", "table", "toilet", "shower", "fridge" };
        if (hour < 12) return new[] { "computer", "chair", "tv", "shelf", "counter" };
        if (hour < 14) return new[] { "table", "stove", "fridge" };
        if (hour < 18) return new[] { "computer", "chair", "shelf", "counter", "sofa" };
        if (hour < 21) return new[] { "table", "stove", "tv", "sofa" };
        return new[] { "tv", "sofa", "shower", "toilet" };
    }
}

public sealed class GvlPlacement
{
    public string Kind; public float X, Y, Heading; public bool Bought;

    public string Serialize()
    {
        CultureInfo ci = CultureInfo.InvariantCulture;
        return Kind + ":" + X.ToString("0.###", ci) + ":" + Y.ToString("0.###", ci) + ":" + Heading.ToString("0.#", ci) + ":" + (Bought ? 1 : 0);
    }

    public static GvlPlacement Parse(string text)
    {
        string[] f = (text ?? "").Split(':');
        float x, y, h;
        CultureInfo ci = CultureInfo.InvariantCulture;
        if (f.Length != 5 || GvlObjects.Find(f[0]) == null || !float.TryParse(f[1], NumberStyles.Float, ci, out x) ||
            !float.TryParse(f[2], NumberStyles.Float, ci, out y) || !float.TryParse(f[3], NumberStyles.Float, ci, out h)) return null;
        GvlPlacement p = new GvlPlacement(); p.Kind = f[0]; p.X = x; p.Y = y; p.Heading = h; p.Bought = f[4] == "1";
        return p;
    }
}

// Bo cuc noi that ghep tu do vat goc, sinh co dinh theo Building_ID.
public static class GvlLayout
{
    public const float MinGap = 1.4f;

    public static float Radius(int tier) { return tier <= 1 ? 3.2f : tier == 2 ? 5.5f : tier == 3 ? 8f : 11f; }

    public static string[] Required(int tier, int purpose)
    {
        if (purpose == GvlTier.Shop)
        {
            List<string> shop = new List<string> { "register", "counter", "fridge" };
            int shelves = tier <= 1 ? 1 : tier == 2 ? 3 : tier == 3 ? 5 : 7;
            for (int i = 0; i < shelves; i++) shop.Add("shelf");
            if (tier >= 2) { shop.Add("chair"); shop.Add("table"); }
            if (tier >= 3) { shop.Add("stove"); shop.Add("toilet"); }
            return shop.ToArray();
        }
        if (tier <= 1) return new[] { "bed", "table", "fridge", "toilet" };
        if (tier == 2) return new[] { "bed", "table", "chair", "stove", "fridge", "toilet", "shower", "tv", "sofa" };
        List<string> home = new List<string> { "bed", "bed", "table", "chair", "chair", "stove", "fridge", "toilet", "shower", "tv", "sofa", "computer" };
        if (tier == 4) { home.Add("bed"); home.Add("sofa"); home.Add("tv"); home.Add("shower"); }
        return home.ToArray();
    }

    // Diem dat nam trong vong tron ban kinh theo Tier, cach cua it nhat 1,6 m va cach nhau MinGap.
    public static List<GvlPlacement> Generate(string buildingId, int tier, int purpose)
    {
        List<GvlPlacement> result = new List<GvlPlacement>();
        float radius = Radius(tier);
        string[] kinds = Required(tier, purpose);
        for (int k = 0; k < kinds.Length; k++)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                uint h = GvlTemplates.StableHash(buildingId + "/" + k + "/" + kinds[k] + "/" + attempt);
                double angle = (h % 3600) / 3600.0 * Math.PI * 2.0;
                double distance = 1.6 + ((h >> 12) % 1000) / 1000.0 * (radius - 1.6);
                float x = (float)(Math.Cos(angle) * distance), y = (float)(Math.Sin(angle) * distance);
                if (Near(result, x, y, MinGap)) continue;
                GvlPlacement p = new GvlPlacement();
                p.Kind = kinds[k]; p.X = x; p.Y = y;
                p.Heading = (float)(Math.Atan2(-(0 - x), 0 - y) * 180.0 / Math.PI);   // quay mat vao giua phong
                result.Add(p);
                break;
            }
        }
        return result;
    }

    public static bool Near(List<GvlPlacement> placed, float x, float y, float gap)
    {
        foreach (GvlPlacement p in placed)
        {
            float dx = p.X - x, dy = p.Y - y;
            if (dx * dx + dy * dy < gap * gap) return true;
        }
        return false;
    }

    // Do nguoi choi mua phai nam trong phong va khong de len do khac.
    public static bool CanPlace(List<GvlPlacement> placed, int tier, float x, float y)
    {
        return Math.Sqrt(x * x + y * y) <= Radius(tier) + 0.5 && !Near(placed, x, y, MinGap);
    }

    public static string Serialize(List<GvlPlacement> placed)
    {
        StringBuilder b = new StringBuilder();
        foreach (GvlPlacement p in placed) { if (b.Length > 0) b.Append(';'); b.Append(p.Serialize()); }
        return b.ToString();
    }

    public static List<GvlPlacement> Parse(string text)
    {
        List<GvlPlacement> placed = new List<GvlPlacement>();
        foreach (string part in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            GvlPlacement p = GvlPlacement.Parse(part);
            if (p != null) placed.Add(p);
        }
        return placed;
    }
}

// Chon noi that: mau dung Tier va cong nang; khong bao gio mo biet thu cho nha nho.
public static class GvlInteriorChoice
{
    // tiers[i], shells[i]: Tier cua mau va mau co phai vo rong de ghep do (Modular) hay khong.
    public static int Choose(string buildingId, int tier, int purpose, int[] tiers, bool[] shells, GvlTemplateHealth health, HashSet<int> skip)
    {
        List<int> fit = new List<int>();
        for (int i = 0; i < tiers.Length; i++)
        {
            if (tiers[i] != tier || !health.Usable(i) || (skip != null && skip.Contains(i))) continue;
            if (purpose == GvlTier.Shop && !shells[i]) continue;   // quan chi ghep trong vo rong
            if (purpose == GvlTier.Home && shells[i]) continue;    // nha o dung noi that co san
            fit.Add(i);
        }
        if (fit.Count == 0) return -1;
        return fit[(int)(GvlTemplates.StableHash("interior/" + buildingId) % (uint)fit.Count)];
    }
}

// Xa hoi tren pho: nguoi dan ra khoi cua len xe di lam, di bo ve nha, vao/ra cua hang theo gio.
public static class GvlStreet
{
    public const int None = -1, OutByCar = 0, OutOnFoot = 1, HomeByCar = 2, HomeOnFoot = 3, IntoShop = 4, OutOfShop = 5;

    // roll: 0..99. Sang di lam, trua mua sam, chieu toi ve nha, khuya yen tinh.
    public static int PickTrip(int hour, int roll)
    {
        roll = Math.Max(0, Math.Min(99, roll));
        if (hour >= 6 && hour < 10) return roll < 55 ? OutByCar : roll < 85 ? OutOnFoot : IntoShop;
        if (hour >= 10 && hour < 16) return roll < 40 ? IntoShop : roll < 75 ? OutOfShop : roll < 90 ? OutOnFoot : HomeOnFoot;
        if (hour >= 16 && hour < 21) return roll < 45 ? HomeByCar : roll < 75 ? HomeOnFoot : roll < 90 ? IntoShop : OutOfShop;
        if (hour >= 21 || hour < 1) return roll < 50 ? HomeOnFoot : roll < 80 ? HomeByCar : OutOfShop;
        return roll < 15 ? HomeByCar : None;   // 1h-6h: thinh thoang co nguoi ve muon
    }

    public static int MaxActive(int hour)
    {
        if ((hour >= 7 && hour < 9) || (hour >= 17 && hour < 19)) return 6;
        if (hour >= 1 && hour < 6) return 1;
        return 4;
    }

    public static bool UsesShop(int trip) { return trip == IntoShop || trip == OutOfShop; }
    public static bool StartsAtDoor(int trip) { return trip == OutByCar || trip == OutOnFoot || trip == OutOfShop; }
    public static bool UsesCar(int trip) { return trip == OutByCar || trip == HomeByCar; }
}

// Mot chuyen di: tung chang co han gio, toi noi hoac qua gio thi sang chang sau. Khong bao gio ket mai.
public sealed class GvlTrip
{
    public const int Emerge = 0, WalkToCar = 1, Drive = 2, Park = 3, WalkToDoor = 4, Vanish = 5, Walk = 6, Done = 7;
    public const int EmergeMs = 1200, WalkMs = 25000, DriveMs = 60000, ParkMs = 6000, VanishMs = 900, AwayMs = 40000;
    readonly int[] plan;
    int index, until;
    public int Kind { get; private set; }
    public int Stage { get { return index < plan.Length ? plan[index] : Done; } }

    public GvlTrip(int kind, int now)
    {
        Kind = kind;
        switch (kind)
        {
            case GvlStreet.OutByCar: plan = new[] { Emerge, WalkToCar, Drive }; break;
            case GvlStreet.HomeByCar: plan = new[] { Drive, Park, WalkToDoor, Vanish }; break;
            case GvlStreet.HomeOnFoot: case GvlStreet.IntoShop: plan = new[] { WalkToDoor, Vanish }; break;
            default: plan = new[] { Emerge, Walk }; break;
        }
        until = now + Limit(Stage);
    }

    static int Limit(int stage)
    {
        switch (stage)
        {
            case Emerge: return EmergeMs;
            case Drive: return DriveMs;
            case Park: return ParkMs;
            case Vanish: return VanishMs;
            case Walk: return AwayMs;
            default: return WalkMs;
        }
    }

    // reached: da toi dich cua chang nay. Tra ve true khi vua sang chang moi (ke ca Done).
    public bool Step(int now, bool reached)
    {
        if (Stage == Done) return false;
        bool timed = Stage == Emerge || Stage == Vanish ? now >= until : reached || now >= until;
        if (!timed) return false;
        index++;
        until = now + Limit(Stage);
        return true;
    }
}
// </gvl-testable:uisplan>
