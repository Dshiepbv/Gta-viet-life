// GTA Việt Life: LÕI DÙNG CHUNG cho mọi module (đồng hồ, bus sự kiện, bật/tắt module, cổng trả tiền).
// Chép vào thư mục "scripts". Các script khác KHÔNG phụ thuộc cứng vào file này: thiếu nó thì mỗi module tự chạy như cũ.
// Giao diện chi tiết: docs/CORE.md trong repo.
//
// Mọi script SHVDN chạy chung một AppDomain nên trao đổi qua AppDomain.CurrentDomain.GetData/SetData("GVL.*"):
//   GVL.Clock   long[] {giây thật (epoch), ngày game, phút trong ngày, thứ (0=T2..6=CN), cuối tuần 0/1, giờ}
//   GVL.Events  List<string> "seq\tloai\tk=v;k=v"   (Emit/Poll: chép hàm mẫu trong docs/CORE.md)
//   GVL.Mod.<Ten> bool       (bật/tắt module, đọc từ GtaVietLifeCore.ini)
//   GVL.Pay     Func<int,string,string,bool>  trả tiền (so tien, nhom, ghi chu). Kinh tế đăng ký bản có ngân hàng;
//               không ai đăng ký thì lõi dùng bản mặc định: trừ tiền mặt + phát sự kiện ledger.expense.
//   GVL.Earn    Action<int,string,string>     nhận tiền, tương tự.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using GTA;

public class GtaVietLifeCore : Script
{
    // tien: nhan vat tuy bien khong co tien trong game => dung vi cua Core (GVL.CashGet/CashSet), xem docs/CORE.md
    static int PlayerCash
    {
        get { Func<int> g = AppDomain.CurrentDomain.GetData("GVL.CashGet") as Func<int>; return g != null ? g() : Game.Player.Money; }
        set { Action<int> s = AppDomain.CurrentDomain.GetData("GVL.CashSet") as Action<int>; if (s != null) s(value); else Game.Player.Money = value; }
    }

    const string SaveFile = "GtaVietLifeCore.sav";
    static readonly string[] Modules = { "Personal", "Legal", "Body", "Social", "Life", "Economy" };
    string server = "http://127.0.0.1:5577";
    bool forward = true, logEvents = true;

    long gameDay;
    int wallet;
    bool walletOn;
    int lastMinute = -1;
    long lastSeenSeq;
    int nextPublish, nextForward, nextSave;
    readonly List<string> outbox = new List<string>();
    readonly long[] clock = new long[6];

    public GtaVietLifeCore()
    {
        int port = Settings.GetValue("Bridge", "Port", 5577);
        server = "http://127.0.0.1:" + port;
        forward = Settings.GetValue("Core", "ForwardEventsToBridge", true);
        logEvents = Settings.GetValue("Core", "LogEvents", true);
        foreach (string m in Modules) Set("GVL.Mod." + m, Settings.GetValue("Modules", m, true));
        Load();
        if (Get("GVL.Pay") == null) { Func<int, string, string, bool> pay = DefaultPay; Set("GVL.Pay", pay); Set("GVL.PayOwner", "core"); }
        if (Get("GVL.Earn") == null) { Action<int, string, string> earn = DefaultEarn; Set("GVL.Earn", earn); }
        // vi tien: nhan vat tuy bien (freemode) khong co tien trong game, nen Core giu vi rieng (xem docs/CORE.md)
        Func<int> cashGet = delegate { return walletOn ? wallet : Game.Player.Money; };   // RAW_MONEY
        Action<int> cashSet = delegate (int v) { v = Math.Max(0, v); if (walletOn) wallet = v; else Game.Player.Money = v; };   // RAW_MONEY
        Action<int> walletEnter = delegate (int start) { wallet = Math.Max(0, start); walletOn = true; Set("GVL.CustomMoney", true); Save(); };
        Func<int> walletLeave = delegate { int w = wallet; walletOn = false; Set("GVL.CustomMoney", false); Save(); return w; };
        Set("GVL.CashGet", cashGet); Set("GVL.CashSet", cashSet);
        Set("GVL.WalletEnter", walletEnter); Set("GVL.WalletLeave", walletLeave);
        Set("GVL.CustomMoney", walletOn);
        Set("GVL.CoreVersion", 2);
        Tick += OnTick;
        Aborted += delegate { Save(); };
    }

    static object Get(string k) { try { return AppDomain.CurrentDomain.GetData(k); } catch { return null; } }
    static void Set(string k, object v) { try { AppDomain.CurrentDomain.SetData(k, v); } catch { } }

    // ---- bus su kien (ban goc; cac script khac chep ham nay)
    public static void Emit(string type, string data)
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

    static bool DefaultPay(int amount, string cat, string note)
    {
        if (amount <= 0) return true;
        if (PlayerCash < amount) return false;
        PlayerCash = PlayerCash - amount;
        Emit("ledger.expense", "amount=" + amount + ";cat=" + cat + ";note=" + Clean(note));
        return true;
    }

    static void DefaultEarn(int amount, string cat, string note)
    {
        if (amount <= 0) return;
        PlayerCash = PlayerCash + amount;
        Emit("ledger.income", "amount=" + amount + ";cat=" + cat + ";note=" + Clean(note));
    }

    static string Clean(string s) { return (s ?? "").Replace(";", ",").Replace("\t", " ").Replace("=", "-"); }

    void OnTick(object sender, EventArgs e)
    {
        int now = Game.GameTime;
        TimeSpan t = World.CurrentTimeOfDay;
        int m = t.Hours * 60 + t.Minutes;
        if (lastMinute >= 0 && m < lastMinute)
        {
            gameDay++;
            Emit("core.day", "day=" + gameDay + ";weekday=" + (gameDay % 7));
            Save();
        }
        lastMinute = m;
        if (now > nextPublish)
        {
            nextPublish = now + 250;
            clock[0] = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            clock[1] = gameDay; clock[2] = m; clock[3] = gameDay % 7; clock[4] = gameDay % 7 >= 5 ? 1 : 0; clock[5] = t.Hours;
            Set("GVL.Clock", clock);
            DrainEvents();
        }
        if (now > nextForward && outbox.Count > 0) { nextForward = now + 3000; Forward(); }
        if (now > nextSave) { nextSave = now + 15000; Save(); }
    }

    // doc su kien moi: ghi log, gui cau noi, cat bot danh sach (giu 300 dong cuoi)
    void DrainEvents()
    {
        List<string> list = Get("GVL.Events") as List<string>;
        if (list == null) return;
        List<string> fresh = new List<string>();
        lock (list)
        {
            foreach (string line in list)
            {
                long seq;
                int tab = line.IndexOf('\t');
                if (tab > 0 && long.TryParse(line.Substring(0, tab), out seq) && seq > lastSeenSeq) { fresh.Add(line); lastSeenSeq = seq; }
            }
            if (list.Count > 300) list.RemoveRange(0, list.Count - 300);
        }
        if (fresh.Count == 0) return;
        if (logEvents)
        {
            try
            {
                StringBuilder b = new StringBuilder();
                foreach (string f in fresh) b.Append(DateTime.Now.ToString("HH:mm:ss") + " d" + gameDay + " " + f.Replace('\t', ' ') + Environment.NewLine);
                File.AppendAllText("GtaVietLifeEvents.log", b.ToString());
            }
            catch { }
        }
        if (forward) outbox.AddRange(fresh);
        if (outbox.Count > 200) outbox.RemoveRange(0, outbox.Count - 200);
    }

    void Forward()
    {
        NameValueCollection c = new NameValueCollection();
        c["lines"] = string.Join("\n", outbox.ToArray());
        c["day"] = gameDay.ToString();
        outbox.Clear();
        string url = server + "/gvl_event";
        Task.Run(delegate
        {
            try { using (WebClient wc = new WebClient()) { wc.Proxy = null; wc.Encoding = Encoding.UTF8; wc.UploadValues(url, "POST", c); } }
            catch { }
        });
    }

    void Save()
    {
        try { File.WriteAllLines(SaveFile, new string[] { "day=" + gameDay, "wallet=" + wallet, "walletOn=" + (walletOn ? 1 : 0) }); } catch { }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(SaveFile)) return;
            foreach (string line in File.ReadAllLines(SaveFile))
            {
                if (line.StartsWith("day=")) long.TryParse(line.Substring(4), out gameDay);
                else if (line.StartsWith("wallet=")) int.TryParse(line.Substring(7), out wallet);
                else if (line.StartsWith("walletOn=")) walletOn = line.Substring(9) == "1";
            }
        }
        catch { }
    }
}
