// GTA Viet Life: doc/ghi file luu dung chung. File nay duoc bien dich vao tung DLL can luu.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
