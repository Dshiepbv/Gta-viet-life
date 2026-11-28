using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Reflection;
using GtaVietLife.Launcher.Models;
namespace GtaVietLife.Launcher.Services;

public sealed class UpdateService {
 const string Api="https://api.github.com/repos/Dshiepbv/Gta-viet-life/releases?per_page=100";

 public static Version Current=>Assembly.GetExecutingAssembly().GetName().Version??new Version(0,0);

 static HttpClient Client(){
  var http=new HttpClient();http.DefaultRequestHeaders.UserAgent.ParseAdd("GTA-Viet-Life-Launcher/0.1");
  return http;
 }

 /// <summary>The newer release, or null when this version is current. Throws when GitHub cannot be read.</summary>
 public async Task<LauncherRelease?> FindNewerAsync(CancellationToken ct){
  using var http=Client();
  using var response=await http.GetAsync(Api,ct);
  if(!response.IsSuccessStatusCode)throw new HttpRequestException("GitHub trả về mã "+(int)response.StatusCode);
  var newest=ReleaseFeed.Newest(await response.Content.ReadAsStringAsync(ct));
  if(newest==null){RuntimeManager.Log("Update check: chưa có bản phát hành Launcher nào.");return null;}
  return ReleaseFeed.IsNewer(newest.Version,Current)?newest:null;
 }

 /// <summary>Downloads and verifies the package, then starts the updater. The caller must exit afterwards.</summary>
 public async Task InstallAsync(LauncherRelease release,CancellationToken ct){
  using var http=Client();http.Timeout=TimeSpan.FromMinutes(30);
  var dir=Path.Combine(LauncherSettings.Dir,"Updates");Directory.CreateDirectory(dir);
  var zip=Path.Combine(dir,ReleaseFeed.PackageName);var sums=Path.Combine(dir,ReleaseFeed.ChecksumsName);
  await Download(http,release.ChecksumsUrl,sums,ct);await Download(http,release.PackageUrl,zip,ct);
  var expected=File.ReadLines(sums).Select(x=>x.Trim().Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries)).FirstOrDefault(x=>x.Length>=2&&x[^1].TrimStart('*')==ReleaseFeed.PackageName)?[0];
  if(string.IsNullOrWhiteSpace(expected))throw new InvalidDataException("Checksum file has no package hash.");
  using(var fs=File.OpenRead(zip)){
   var actual=await SHA256.HashDataAsync(fs,ct);
   if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),actual))throw new InvalidDataException("Downloaded update SHA-256 mismatch.");
  }
  var updaterSource=Path.Combine(AppContext.BaseDirectory,"GtaVietLife.Updater.exe");if(!File.Exists(updaterSource))throw new FileNotFoundException("Updater helper is missing.",updaterSource);
  var updater=Path.Combine(Path.GetTempPath(),"GtaVietLife.Updater-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(updaterSource,updater);
  var target=AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
  // Thu muc lam viec phai nam ngoai thu muc cai, neu khong Windows khong cho trinh cap nhat doi ten no.
  var psi=new ProcessStartInfo(updater){UseShellExecute=true,WorkingDirectory=Path.GetTempPath()};psi.ArgumentList.Add(zip);psi.ArgumentList.Add(target);psi.ArgumentList.Add(Environment.ProcessId.ToString());
  Process.Start(psi);RuntimeManager.Log("Verified update "+release.Tag+" staged; handing off to updater.");
 }

 static async Task Download(HttpClient http,string url,string path,CancellationToken ct){using var r=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);r.EnsureSuccessStatusCode();await using var input=await r.Content.ReadAsStreamAsync(ct);await using var output=File.Create(path);await input.CopyToAsync(output,ct);}
}
