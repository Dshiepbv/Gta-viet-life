using System.Text.Json;

namespace GtaVietLife.Launcher.Services;

public sealed record LauncherRelease(Version Version, string Tag, string PackageUrl, string ChecksumsUrl);

/// <summary>
/// Chooses the newest installable release from the GitHub release list. The "latest" endpoint cannot be used:
/// the same repository publishes dashboard APK releases, which are often newer.
/// </summary>
public static class ReleaseFeed
{
    public const string PackageName = "GtaVietLife-Windows-x64.zip";
    public const string ChecksumsName = "SHA256SUMS.txt";

    public static LauncherRelease? Newest(string releasesJson)
    {
        using var document = JsonDocument.Parse(releasesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
        LauncherRelease? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft") || Flag(release, "prerelease")) continue;
            var tag = Text(release, "tag_name");
            if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version)) continue;
            string package = "", checksums = "";
            if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Text(asset, "name");
                    if (name == PackageName) package = Text(asset, "browser_download_url");
                    if (name == ChecksumsName) checksums = Text(asset, "browser_download_url");
                }
            if (!package.StartsWith("https://", StringComparison.Ordinal) ||
                !checksums.StartsWith("https://", StringComparison.Ordinal)) continue;
            if (best == null || version > best.Version) best = new LauncherRelease(version, tag, package, checksums);
        }
        return best;
    }

    /// <summary>Compares major.minor.build only: the tag has three parts, the assembly version four.</summary>
    public static bool IsNewer(Version release, Version current) =>
        new Version(release.Major, release.Minor, Math.Max(0, release.Build)) >
        new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
