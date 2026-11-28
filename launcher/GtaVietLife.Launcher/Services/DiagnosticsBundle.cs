using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GtaVietLife.Launcher.Services;

/// <summary>Collects logs only (never saves or configuration) into one zip with secrets removed.</summary>
public static class DiagnosticsBundle
{
    public static List<string> LogFiles(string launcherDir, string engineDir, string bridgeDir, string? gameDir)
    {
        var files = new List<string>
        {
            Path.Combine(launcherDir, "launcher.log"),
            Path.Combine(engineDir, "engine-host.log"),
            Path.Combine(bridgeDir, "bridge.log")
        };
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            foreach (var folder in new[] { gameDir, Path.Combine(gameDir, "scripts") })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var pattern in new[] { "ScriptHookV*.log", "GtaVietLife*.log" })
                    files.AddRange(Directory.GetFiles(folder, pattern)
                        .Where(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)));
            }
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToList();
    }

    public static string Redact(string value)
    {
        value = Regex.Replace(value,
            @"(?i)(""?(?:[a-z_]*api[_-]?key|[a-z_-]*token|password|secret|authorization|cloud_key|dash_pin)""?\s*[:=]\s*)(""[^""]*""|'[^']*'|[^,\s;&]+)",
            "$1[REDACTED]");
        value = Regex.Replace(value, @"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*", "Bearer [REDACTED]");
        return Regex.Replace(value, @"([?&]key=)[^&\s""]+", "$1[REDACTED]");
    }

    /// <summary>Writes the zip next to the destination first, so a failure never leaves half a bundle.</summary>
    public static int Write(IEnumerable<string> files, string destination)
    {
        var temp = destination + ".tmp";
        var count = 0;
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                for (var n = 2; !used.Add(name); n++) name = n + "-" + Path.GetFileName(path);
                string text;
                try
                {
                    // The game and the services keep their logs open while running.
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(input);
                    text = reader.ReadToEnd();
                }
                catch (IOException ex) { text = "Không đọc được: " + ex.Message; }
                catch (UnauthorizedAccessException ex) { text = "Không đọc được: " + ex.Message; }
                using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write(Redact(text));
                count++;
            }
        }
        File.Move(temp, destination, true);
        return count;
    }
}
