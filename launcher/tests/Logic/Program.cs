using GtaVietLife.Launcher.Services;

var failures = 0;
void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL " + what); } }
if (args.Contains("control")) Check(false, "control case");

var root = Path.Combine(Path.GetTempPath(), "gvl-launcher-" + Guid.NewGuid().ToString("N"));
string Write(string relative, string text)
{
    var path = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
    return path;
}
string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
bool Exists(string relative) => File.Exists(Path.Combine(root, relative));

try
{
    // ---- manifest
    var a = Write("pkg/a.bin", "alpha");
    var b = Write("pkg/sub/b.bin", "beta");
    var pkg = Path.Combine(root, "pkg");
    var manifest = new PackageManifest { Version = "1", Files = { ["a.bin"] = PackageManifest.HashFile(a), ["sub/b.bin"] = PackageManifest.HashFile(b) } };
    File.WriteAllText(Path.Combine(pkg, "manifest.json"),
        "{\"version\":\"9\",\"files\":{\"a.bin\":\"" + manifest.Files["a.bin"] + "\"}}");
    Check(PackageManifest.Load(pkg)!.Version == "9", "manifest keys are case insensitive");
    Check(manifest.Verify(pkg).Count == 0, "intact package");
    File.WriteAllText(a, "changed");
    Check(manifest.Verify(pkg).Single().Contains("checksum"), "altered file");
    File.Delete(b);
    Check(manifest.Verify(pkg).Count == 2, "missing file");
    Check(new PackageManifest { Files = { ["../outside"] = "00" } }.Verify(pkg).Single().Contains("không hợp lệ"), "path traversal refused");
    Check(new PackageManifest().Verify(pkg).Count == 1, "empty manifest is not intact");

    // ---- environment
    var exe = Write("game/GTA5.exe", "");
    Check(LegacyEnvironment.FindProblem(Write("e/GTA5_Enhanced.exe", ""), "1.0.1.0")!.Contains("Enhanced"), "enhanced refused");
    Check(LegacyEnvironment.FindProblem(exe, "1.0.3411.0")!.Contains("ScriptHookV.dll"), "missing hooks listed");
    Write("game/ScriptHookV.dll", ""); Write("game/dinput8.dll", ""); Write("game/ScriptHookVDotNet.asi", ""); Write("game/ScriptHookVDotNet3.dll", "");
    Check(LegacyEnvironment.FindProblem(exe, "1.0.3411.0")!.Contains("nightly"), "stable SHVDN refused on new builds");
    Check(LegacyEnvironment.FindProblem(exe, "1.0.3179.0") == null, "stable SHVDN fine on old builds");
    Write("game/ScriptHookVDotNet-Loader.asi", ""); Write("game/ScriptHookVDotNet.dll", "");
    Check(LegacyEnvironment.FindProblem(exe, "1.0.3411.0") == null, "nightly accepted");
    Check(LegacyEnvironment.GameBuild("1, 0, 3411, 0") == 3411 && LegacyEnvironment.GameBuild("unknown") == 0, "build parsing");

    // ---- game detection
    Check(GameLocator.FirstGame(new[] { null, "", Path.Combine(root, "nowhere"), "\"" + Path.Combine(root, "game") + "\"" }) is { } hit && Path.GetFullPath(hit) == Path.GetFullPath(exe), "quoted folder found");
    Check(GameLocator.FirstGame(new[] { Path.Combine(root, "e") }) == null, "enhanced-only folder ignored");
    var steam = GameLocator.SteamGameFolders("\"0\" { \"path\"  \"D:\\\\Games\\\\Steam\" }").Single();
    Check(steam == Path.Combine("D:\\Games\\Steam", "steamapps", "common", "Grand Theft Auto V"), "steam library parsed: " + steam);

    // ---- diagnostics
    Write("diag/launcher/launcher.log", "token=abc123 ok");
    Write("diag/game/GtaVietLifeCore.deadletter.log", "{\"gemini_api_key\":\"SECRET\"} url?a=pull&key=K1");
    Write("diag/game/scripts/GtaVietLifeCore.log", "scripts log");
    Write("diag/game/GtaVietLifeWorld.sav", "player save");
    var logs = DiagnosticsBundle.LogFiles(Path.Combine(root, "diag/launcher"), Path.Combine(root, "none"), Path.Combine(root, "none"), Path.Combine(root, "diag/game"));
    Check(logs.Count == 3 && logs.All(f => f.EndsWith(".log")), "logs only, saves excluded");
    var bundle = Path.Combine(root, "diag/out.zip");
    Check(DiagnosticsBundle.Write(logs, bundle) == 3 && !File.Exists(bundle + ".tmp"), "bundle written");
    using (var zip = System.IO.Compression.ZipFile.OpenRead(bundle))
    {
        var all = string.Join("\n", zip.Entries.Select(entry => { using var reader = new StreamReader(entry.Open()); return reader.ReadToEnd(); }));
        Check(zip.Entries.Count == 3 && all.Contains("scripts log"), "all entries readable");
        Check(!all.Contains("abc123") && !all.Contains("SECRET") && !all.Contains("K1"), "secrets removed");
    }

    // ---- releases: dashboard APK releases must not hide the Launcher release
    string Release(string tag, string flags, params string[] names) =>
        "{\"tag_name\":\"" + tag + "\"," + flags + "\"assets\":[" + string.Join(",", names.Select(n =>
            "{\"name\":\"" + n + "\",\"browser_download_url\":\"https://example.test/" + tag + "/" + n + "\"}")) + "]}";
    var both = new[] { ReleaseFeed.PackageName, ReleaseFeed.ChecksumsName };
    var feed = "[" + string.Join(",",
        Release("dash-13", "", "GtaDashboard-13.apk"), Release("v0.3.9", "", both), Release("v0.10.0", "", both),
        Release("v0.11.0", "\"prerelease\":true,", both), Release("v0.12.0", "\"draft\":true,", both),
        Release("v0.13.0", "", ReleaseFeed.PackageName)) + "]";
    var found = ReleaseFeed.Newest(feed);
    Check(found?.Tag == "v0.10.0" && found.PackageUrl.EndsWith("v0.10.0/" + ReleaseFeed.PackageName), "newest installable release: " + found?.Tag);
    Check(ReleaseFeed.Newest("[" + Release("dash-1", "", "a.apk") + "]") == null && ReleaseFeed.Newest("{}") == null, "no launcher release");
    Check(!ReleaseFeed.IsNewer(new Version("0.3.5"), new Version("0.3.5.0")), "same version is not newer");
    Check(ReleaseFeed.IsNewer(new Version("0.3.6"), new Version("0.3.5.0")), "newer version");

    // ---- seven states
    foreach (var title in new[] { "SẴN SÀNG KHỞI CHẠY", "ĐANG KIỂM TRA", "ĐANG TẢI BẢN v1", "ĐANG CÀI MODULE", "CẦN CHỌN GTA V", "KHÔNG TƯƠNG THÍCH", "CẬP NHẬT THẤT BẠI" })
        Check(LauncherStates.All.Contains(LauncherStates.Of(title)), "state for " + title);
    Check(LauncherStates.Of("KHÔNG TƯƠNG THÍCH") == LauncherStates.Incompatible && LauncherStates.Of("CHƯA SẴN SÀNG") == LauncherStates.Error &&
          LauncherStates.Of("ĐANG KHỞI ĐỘNG") == LauncherStates.Installing && LauncherStates.Of("BỘ CÀI BỊ HỎNG") == LauncherStates.NeedsRepair, "state mapping");
    Check(LauncherStates.All.Distinct().Count() == 7, "seven states");

    // ---- community MLO packs: original link + SHA-256, never repackaged
    var rpfBytes = new byte[] { 0x37, 0x46, 0x50, 0x52, 1, 2, 3 };
    var zipPath = Path.Combine(root, "mlo/source.zip");
    Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
    using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
    using (var entry = zip.CreateEntry("pack/dlc.rpf").Open()) entry.Write(rpfBytes);
    var zipHash = PackageManifest.HashFile(zipPath);
    var good = new MloPack { Name = "Quan", Url = "https://example.test/quan.zip", Sha256 = zipHash, DlcName = "gvl_quan" };
    var wrong = new MloPack { Name = "Sai", Url = "https://example.test/sai.zip", Sha256 = new string('0', 64), DlcName = "gvl_sai" };
    var http = new MloPack { Name = "Http", Url = "http://example.test/x.zip", Sha256 = zipHash, DlcName = "gvl_http" };
    Check(MloPacks.Problem(good) == null && MloPacks.Problem(http) != null && MloPacks.Problem(new MloPack { Name = "x", Url = good.Url, Sha256 = zipHash, DlcName = "../x" }) != null, "pack validation");
    var downloads = 0;
    Func<string, string, CancellationToken, Task> fake = (url, path, ct) => { downloads++; File.Copy(zipPath, path, true); return Task.CompletedTask; };
    var gameDir = Path.Combine(root, "mlo/game");
    var staging = Path.Combine(root, "mlo/staging");
    var results = MloPacks.InstallAll(new[] { good, wrong, http }, gameDir, staging, Path.Combine(root, "mlo/backup"), fake, CancellationToken.None).GetAwaiter().GetResult();
    Check(results[0].Installed && File.ReadAllBytes(MloPacks.Target(gameDir, good)).SequenceEqual(rpfBytes), "pack installed from zip");
    Check(!results[1].Installed && !File.Exists(MloPacks.Target(gameDir, wrong)), "wrong hash not installed");
    Check(!results[2].Installed && downloads == 2, "http link never downloaded");
    results = MloPacks.InstallAll(new[] { good }, gameDir, staging, Path.Combine(root, "mlo/backup"), fake, CancellationToken.None).GetAwaiter().GetResult();
    Check(results[0].Message == "Đã có" && downloads == 2, "installed pack not downloaded again");
    var manifestPath = Path.Combine(root, "mlo/manifest.json");
    File.WriteAllText(manifestPath, "{\"version\":\"1\",\"files\":{},\"mlo_packs\":[{\"name\":\"A\",\"url\":\"https://a\",\"sha256\":\"" + zipHash + "\",\"dlcName\":\"a\"}]}");
    File.WriteAllText(Path.Combine(root, "mlo/user.json"), "[{\"name\":\"B\",\"url\":\"https://b\",\"sha256\":\"" + zipHash + "\",\"dlcName\":\"a\"}]");
    var loaded = MloPacks.Load(manifestPath, Path.Combine(root, "mlo/user.json"));
    Check(loaded.Count == 1 && loaded[0].Name == "B", "user list overrides the manifest for the same pack");
    File.WriteAllText(manifestPath, "{\"mlo_catalog\":[{\"id\":\"mlo_001\",\"name\":\"Motel\",\"url\":\"https://x\",\"status\":\"candidate_not_runtime_tested\"}," +
        "{\"id\":\"mlo_002\",\"name\":\"Ok\",\"url\":\"https://y/p.zip\",\"status\":\"verified\",\"sha256\":\"" + zipHash + "\",\"dlcName\":\"ok_pack\"}]}");
    var catalog = MloPacks.Catalog(manifestPath);
    Check(catalog.Count == 2 && catalog[0].Status == "candidate_not_runtime_tested", "candidate status kept as published");
    Check(MloPacks.Activate(catalog[0], true) == null, "candidates are never installed");
    Check(MloPacks.Activate(catalog[1], false) == null && MloPacks.Activate(catalog[1], true)?.DlcName == "ok_pack", "verified pack needs a compatible game");

    // ---- installer
    Write("src/GtaVietLifeCore.dll", "core-v2"); Write("src/GtaVietLifeWorld.dll", "world-v2");
    Write("src/GtaVietLifeCore.ini", "default"); Write("src/GtaVietLifeNew.ini", "new-default");
    Write("src/GtaVietLifeIcons/food.png", "icon-v2");
    Write("scripts/GtaVietLifeCore.dll", "core-v1"); Write("scripts/GtaVietLifeCore.ini", "player-settings");
    Write("scripts/GtaVietLifeWorld.cs", "old source"); Write("scripts/GtaVietLifeWorld.sav", "player save");
    Write("scripts/OtherMod.dll", "someone else");
    Write("GtaVietLifeWorld.sav", "root save");
    var source = Path.Combine(root, "src"); var scripts = Path.Combine(root, "scripts"); var backups = Path.Combine(root, "backups");

    // A failure half way through leaves the previous installation exactly as it was.
    var writes = 0;
    var threw = false;
    try { ScriptInstaller.Install(source, scripts, backups, _ => { if (++writes == 4) throw new IOException("disk full"); }); }
    catch (IOException) { threw = true; }
    Check(threw && Read("scripts/GtaVietLifeCore.dll") == "core-v1", "dll restored after failure");
    Check(Read("scripts/GtaVietLifeWorld.cs") == "old source", "retired source restored");
    Check(!Exists("scripts/GtaVietLifeWorld.dll") && !Exists("scripts/GtaVietLifeNew.ini"), "partial files removed");
    Check(Directory.GetFiles(scripts, "*.gvltmp", SearchOption.AllDirectories).Length == 0, "no temp files left");

    var done = ScriptInstaller.Install(source, scripts, backups);
    Check(Read("scripts/GtaVietLifeCore.dll") == "core-v2" && Read("scripts/GtaVietLifeWorld.dll") == "world-v2", "installed");
    Check(Read("scripts/GtaVietLifeCore.ini") == "player-settings", "existing ini kept");
    Check(Read("scripts/GtaVietLifeNew.ini") == "new-default", "missing ini added");
    Check(!Exists("scripts/GtaVietLifeWorld.cs") && File.ReadAllText(Path.Combine(done.BackupDir!, "GtaVietLifeWorld.cs")) == "old source", "source moved to backup");
    Check(File.ReadAllText(Path.Combine(done.BackupDir!, "GtaVietLifeCore.dll")) == "core-v1", "replaced dll backed up");
    Check(Read("scripts/GtaVietLifeWorld.sav") == "player save" && Read("GtaVietLifeWorld.sav") == "root save", "saves untouched");
    Check(Read("scripts/OtherMod.dll") == "someone else", "other mods untouched");
    var again = ScriptInstaller.Install(source, scripts, backups);
    Check(again.Copied == 0 && again.Retired == 0 && again.BackupDir == null, "second run changes nothing");
    var empty = false;
    try { ScriptInstaller.Install(Path.Combine(root, "nothing"), scripts, backups); } catch (FileNotFoundException) { empty = true; }
    Check(empty, "package without modules refused");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
Console.WriteLine("failures=" + failures);
return failures == 0 ? 0 : 1;
