// GTA Viet Life: doc/ghi file luu dung chung. File nay duoc bien dich vao tung DLL can luu.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// <gvl-testable:daywatch>
// Core la noi dem ngay. Khong co Core thi tu nhan biet luc dong ho vong qua nua dem.
internal sealed class GvlDayWatch
{
    long lastCoreDay = -1;
    int lastMinute = -1;

    public static long CoreDay()
    {
        long[] clock = AppDomain.CurrentDomain.GetData("GVL.Clock") as long[];
        return clock != null && clock.Length > 1 ? clock[1] : -1L;
    }

    // true dung mot lan moi khi sang ngay moi. coreDay < 0 nghia la khong co Core.
    public bool NewDay(long coreDay, int minute)
    {
        bool wrapped = lastMinute >= 0 && minute < lastMinute && lastMinute - minute > 600;
        lastMinute = minute;
        if (coreDay < 0) { lastCoreDay = -1; return wrapped; }
        bool advanced = lastCoreDay >= 0 && coreDay > lastCoreDay;
        lastCoreDay = coreDay;
        return advanced;
    }
}
// </gvl-testable:daywatch>

// <gvl-testable:banksync>
// Tat Engine van choi: moi thay doi thanh event luu ben va gui bu. Khi so cai da nhan het thi so cai la chuan.
internal static class GvlBankSync
{
    public const int None = 0, TakeOver = 1, Adopt = 2;

    // ledger = { da nhan quyen (0/1), so du ngan hang, seq moi nhat so cai da ap dung }; null khi chua dong bo xong.
    public static int Decide(long[] ledger, long lastBankSequence, long gameBank)
    {
        if (ledger == null || ledger.Length < 3) return None;
        if (ledger[0] == 0) return TakeOver;
        if (ledger[2] < lastBankSequence) return None;           // con thay doi cua game chua toi so cai
        if (ledger[1] < 0 || ledger[1] > int.MaxValue) return None;
        return ledger[1] == gameBank ? None : Adopt;
    }
}
// </gvl-testable:banksync>

// <gvl-testable:autosave>
// Khi nao goi luu tu dong cua GTA: tien thay doi (hoac qua ngay), tien da dung yen vai giay, cach lan truoc du lau, va dang an toan.
internal sealed class GvlAutoSave
{
    public const int MinGapMs = 90000;
    public const int QuietMs = 6000;
    public const int MinChange = 1;
    bool started, dayDue;
    long savedMoney, lastMoney, lastSaveAt, lastChangeAt;

    public void DayPassed() { dayDue = true; }

    public bool Due(int now, long money, bool safe)
    {
        if (!started) { started = true; savedMoney = lastMoney = money; lastSaveAt = now; lastChangeAt = now; return false; }
        if (now < lastSaveAt) { lastSaveAt = now; lastChangeAt = now; }   // GameTime bat dau lai sau khi nap
        if (money != lastMoney) { lastMoney = money; lastChangeAt = now; }
        if (!safe) return false;
        if (Math.Abs(money - savedMoney) < MinChange && !dayDue) return false;
        if (now - lastChangeAt < QuietMs) return false;
        return now - lastSaveAt >= MinGapMs;
    }

    public void Saved(int now, long money) { savedMoney = money; lastSaveAt = now; dayDue = false; }

    // Game khong chiu luu (dang lam nhiem vu, tat tu luu...): doi them mot quang roi thu lai.
    public void Failed(int now) { lastSaveAt = now; }
}
// </gvl-testable:autosave>

// <gvl-testable:savefile>
// Ghi qua file tam roi File.Replace, giu ban .bak. Tai loi ca hai thi khong bao gio ghi de.
internal sealed class GvlSaveFile
{
    public const string Header = "#gvl-save=1";
    public const string Footer = "#gvl-end";

    readonly string path;
    readonly Func<List<string>, bool> acceptUnmarked;
    readonly Action<string> log;

    // acceptUnmarked kiem tra file do ban cu ghi (khong co dong dau va dong cuoi).
    public GvlSaveFile(string path, Func<List<string>, bool> acceptUnmarked, Action<string> log)
    {
        this.path = path; this.acceptUnmarked = acceptUnmarked; this.log = log;
    }

    public bool Blocked { get; private set; }
    public bool LoadedBackup { get; private set; }
    public string BackupPath { get { return path + ".bak"; } }
    public string BrokenPath { get { return path + ".broken"; } }

    public static bool LooksKeyValue(List<string> lines)
    {
        foreach (string line in lines) if (line.IndexOf('=') > 0) return true;
        return false;
    }

    // false: co file luu nhung khong doc duoc; Write se bi chan cho toi lan tai thanh cong.
    public bool Load(Action<string> parseLine)
    {
        Blocked = false;
        bool primary = File.Exists(path), backup = File.Exists(BackupPath);
        if (!primary && !backup) return true;
        string source = path;
        List<string> lines = primary ? Read(path) : null;
        if (lines == null && backup) { source = BackupPath; lines = Read(BackupPath); }
        if (lines == null)
        {
            Blocked = true;
            Preserve();
            Report("khong doc duoc " + path + " lan ban sao; giu nguyen file, khong ghi de");
            return false;
        }
        LoadedBackup = source != path;
        int bad = 0;
        foreach (string line in lines)
        {
            try { parseLine(line); }
            catch (Exception ex) { bad++; Report("dong luu hong trong " + source + ": " + ex.Message); }
        }
        if (bad > 0 || source != path)
        {
            Preserve();
            Report("da tai " + source + " (" + bad + " dong hong); ban goc giu o " + BrokenPath);
        }
        return true;
    }

    public bool Write(IEnumerable<string> lines)
    {
        if (Blocked) return false;
        string temp = path + ".tmp";
        using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.WriteLine(Header);
            foreach (string line in lines) writer.WriteLine(line);
            writer.WriteLine(Footer);
            writer.Flush();
            stream.Flush(true);
        }
        if (!File.Exists(path)) File.Move(temp, path);
        else if (Read(path) != null) File.Replace(temp, path, BackupPath);
        else File.Replace(temp, path, BrokenPath);   // file chinh hong: khong de no de len ban .bak tot
        return true;
    }

    // null: file thieu, cut hoac khong dung dinh dang.
    List<string> Read(string file)
    {
        try
        {
            List<string> lines = new List<string>(File.ReadAllLines(file, Encoding.UTF8));
            foreach (string line in lines) if (line.IndexOf('\0') >= 0) return null;
            if (lines.Count > 0 && lines[0].StartsWith("#", StringComparison.Ordinal))
            {
                if (lines.Count < 2 || lines[0] != Header || lines[lines.Count - 1] != Footer) return null;
                return lines.GetRange(1, lines.Count - 2);
            }
            return acceptUnmarked(lines) ? lines : null;
        }
        catch (Exception ex) { Report("doc " + file + " loi: " + ex.Message); return null; }
    }

    void Preserve()
    {
        try { if (File.Exists(path)) File.Copy(path, BrokenPath, true); }
        catch (Exception ex) { Report("khong giu duoc ban goc: " + ex.Message); }
    }

    void Report(string message) { if (log != null) log(message); }
}
// </gvl-testable:savefile>
