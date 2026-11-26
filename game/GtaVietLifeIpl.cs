// GTA Việt Life - MODULE 3: MỞ KHOÁ NHÀ / NỘI THẤT (nạp các IPL của GTA Online ở chế độ chơi đơn).
// Độc lập. Chép vào "scripts" cùng GtaVietLifeIpl.ini, bấm ] để nạp lại.
// Mỗi nhóm bật/tắt riêng trong .ini. Tên IPL không tồn tại trong bản game của Sếp thì game bỏ qua (không lỗi).
// CHỈ DÙNG Ở CHẾ ĐỘ CHƠI ĐƠN, KHÔNG VÀO GTA ONLINE KHI ĐANG CÀI MOD.
// Log: GtaVietLifeIpl.log (nhóm nào nạp được bao nhiêu IPL đang bật).

using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Native;

public class GtaVietLifeIpl : Script
{
    const string LogFile = "GtaVietLifeIpl.log";
    bool done;
    int startAt, recheck, recheckAt;

    static readonly Dictionary<string, string[]> Groups = new Dictionary<string, string[]>
    {
        { "Yacht", new[] { "hei_yacht_heist", "hei_yacht_heist_Bar", "hei_yacht_heist_Bedrm", "hei_yacht_heist_Bridge", "hei_yacht_heist_DistantLights", "hei_yacht_heist_enginrm", "hei_yacht_heist_LODLights", "hei_yacht_heist_Lounge" } },
        { "Carrier", new[] { "hei_carrier", "hei_carrier_DistantLights", "hei_Carrier_int1", "hei_Carrier_int2", "hei_Carrier_int3", "hei_Carrier_int4", "hei_Carrier_int5", "hei_Carrier_int6", "hei_carrier_LODLights" } },
        { "Apartments", new[] { "apa_v_mp_h_01_a", "apa_v_mp_h_01_b", "apa_v_mp_h_01_c", "apa_v_mp_h_02_a", "apa_v_mp_h_02_b", "apa_v_mp_h_02_c", "apa_v_mp_h_03_a", "apa_v_mp_h_03_b", "apa_v_mp_h_03_c", "apa_v_mp_h_04_a", "apa_v_mp_h_04_b", "apa_v_mp_h_04_c", "apa_v_mp_h_05_a", "apa_v_mp_h_05_b", "apa_v_mp_h_05_c", "apa_v_mp_h_06_a", "apa_v_mp_h_06_b", "apa_v_mp_h_06_c", "apa_v_mp_h_07_a", "apa_v_mp_h_07_b", "apa_v_mp_h_07_c", "apa_v_mp_h_08_a", "apa_v_mp_h_08_b", "apa_v_mp_h_08_c" } },
        { "Offices", new[] { "ex_dt1_02_office_02b", "ex_dt1_02_office_02c", "ex_dt1_02_office_01a", "ex_dt1_02_office_01b", "ex_dt1_02_office_01c", "ex_dt1_02_office_02a", "ex_dt1_02_office_03a", "ex_dt1_02_office_03b", "ex_dt1_02_office_03c", "ex_dt1_11_office_01a", "ex_dt1_11_office_01b", "ex_dt1_11_office_01c", "ex_dt1_11_office_02a", "ex_dt1_11_office_02b", "ex_dt1_11_office_02c", "ex_dt1_11_office_03a", "ex_dt1_11_office_03b", "ex_dt1_11_office_03c", "ex_sm_13_office_01a", "ex_sm_13_office_01b", "ex_sm_13_office_01c", "ex_sm_13_office_02a", "ex_sm_13_office_02b", "ex_sm_13_office_02c", "ex_sm_13_office_03a", "ex_sm_13_office_03b", "ex_sm_13_office_03c", "ex_sm_15_office_01a", "ex_sm_15_office_01b", "ex_sm_15_office_01c", "ex_sm_15_office_02a", "ex_sm_15_office_02b", "ex_sm_15_office_02c", "ex_sm_15_office_03a", "ex_sm_15_office_03b", "ex_sm_15_office_03c" } },
        { "Clubhouse", new[] { "bkr_biker_interior_placement_interior_0_biker_dlc_int_01_milo", "bkr_biker_interior_placement_interior_1_biker_dlc_int_02_milo", "bkr_biker_interior_placement_interior_2_biker_dlc_int_ware01_milo", "bkr_biker_interior_placement_interior_3_biker_dlc_int_ware02_milo", "bkr_biker_interior_placement_interior_4_biker_dlc_int_ware03_milo", "bkr_biker_interior_placement_interior_5_biker_dlc_int_ware04_milo", "bkr_biker_interior_placement_interior_6_biker_dlc_int_ware05_milo" } },
        { "Nightclub", new[] { "ba_int_placement_ba_interior_0_dlc_int_01_ba_milo_", "ba_int_placement_ba_interior_1_dlc_int_02_ba_milo_", "ba_int_placement_ba_interior_2_dlc_int_03_ba_milo_", "ba_int_placement_ba_interior_3_dlc_int_04_ba_milo_", "ba_int_placement_ba_interior_4_dlc_int_05_ba_milo_" } },
        { "Casino", new[] { "vw_casino_main", "vw_casino_garage", "vw_casino_carpark", "vw_casino_penthouse", "vw_dlc_casino_door", "vw_casino_door" } },
        { "Bunker", new[] { "gr_case0_bunkerclosed", "gr_case1_bunkerclosed", "gr_case2_bunkerclosed", "gr_case3_bunkerclosed", "gr_case4_bunkerclosed", "gr_case5_bunkerclosed", "gr_case6_bunkerclosed", "gr_case7_bunkerclosed", "gr_case9_bunkerclosed", "gr_case10_bunkerclosed", "gr_case11_bunkerclosed", "gr_grdlc_interior_placement_interior_0_grdlc_int_01_milo_", "gr_grdlc_interior_placement_interior_1_grdlc_int_02_milo_" } },
        { "Misc", new[] { "FIBlobby", "FBI_repair", "bnkheist_apt_norm", "shr_int", "chop_props", "farm", "farm_props", "farmint", "farmint_cap", "des_farmhouse", "gasparticle_grp2", "lr_cs6_08_grave_closed", "refit_unload", "post_hiest_unload", "id2_14_during_door", "ferris_finale_anim", "Coroner_Int_on", "plg_01", "lr_cs6_08_grave_closed" } },
    };

    public GtaVietLifeIpl()
    {
        startAt = Game.GameTime + 4000;
        Tick += OnTick; Interval = 500;
    }

    void Log(string s) { try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + " " + s + "\r\n"); } catch { } }

    void Report(string tag)
    {
        foreach (KeyValuePair<string, string[]> g in Groups)
        {
            int on = 0; foreach (string n in g.Value) if (Function.Call<bool>(Hash.IS_IPL_ACTIVE, n)) on++;
            Log("[" + tag + "] " + g.Key + ": " + on + "/" + g.Value.Length);
        }
    }

    void OnTick(object sender, EventArgs e)
    {
        if (recheck > 0 && Game.GameTime > recheckAt) { recheckAt = Game.GameTime + 15000; recheck--; Report("kiem tra lai"); return; }
        if (done || Game.GameTime < startAt) return;
        done = true; recheck = 3; recheckAt = Game.GameTime + 10000;
        try
        {
            if (!Settings.GetValue("Ipl", "Enabled", true)) { Log("tat trong ini"); return; }
            foreach (KeyValuePair<string, string[]> g in Groups)
            {
                if (!Settings.GetValue("Ipl", g.Key, true)) { Log(g.Key + ": tat"); continue; }
                int on = 0;
                foreach (string n in g.Value)
                {
                    Function.Call(Hash.REQUEST_IPL, n);
                    if (Function.Call<bool>(Hash.IS_IPL_ACTIVE, n)) on++;
                }
                Log(g.Key + ": " + on + "/" + g.Value.Length + " IPL dang bat");
            }
        }
        catch (Exception ex) { Log("loi: " + ex.Message); }
    }
}
