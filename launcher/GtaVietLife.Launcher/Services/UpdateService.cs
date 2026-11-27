using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using GtaVietLife.Launcher.Models;
namespace GtaVietLife.Launcher.Services;
public sealed class UpdateService {
 const string Api="https://api.github.com/repos/Dshiepbv/Gta-viet-life/releases/latest";
 const string PackageName="GtaVietLife-Windows-x64.zip";const string ChecksumsName="SHA256SUMS.txt";
 public async Task<bool> CheckAndLaunchUpdaterAsync(CancellationToken ct){
  using var http=new HttpClient();http.DefaultRequestHeaders.UserAgent.ParseAdd("GTA-Viet-Life-Launcher/0.1");
  JsonDocument release;
  try{using var r=await http.GetAsync(Api,ct);if(!r.IsSuccessStatusCode){RuntimeManager.Log("Update check skipped: GitHub release endpoint returned "+(int)r.StatusCode);return false;}release=JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));}
  catch(Exception ex){RuntimeManager.Log("Update check unavailable: "+ex.Message);return false;}
  using(release){
   var root=release.RootElement;var tag=root.GetProperty("tag_name").GetString()??"";
   if(!Version.TryParse(tag.TrimStart('v','V'),out var latest))return false;
   var current=Assembly.GetExecutingAssembly().GetName().Version??new Version(0,0);
   if(latest<=current){RuntimeManager.Log("Launcher is current ("+current+").");return false;}
   var assets=root.GetProperty("assets");
   string? packageUrl=null,checksumsUrl=null;
   foreach(var a in assets.EnumerateArray()){var name=a.GetProperty("name").GetString();var url=a.GetProperty("browser_download_url").GetString();if(name==PackageName)packageUrl=url;if(name==ChecksumsName)checksumsUrl=url;}
   if(packageUrl==null||checksumsUrl==null){RuntimeManager.Log("Release "+tag+" missing package/checksum assets.");return false;}
   var dir=Path.Combine(LauncherSettings.Dir,"Updates");Directory.CreateDirectory(dir);
   var zip=Path.Combine(dir,PackageName);var sums=Path.Combine(dir,ChecksumsName);
   await Download(http,checksumsUrl,sums,ct);await Download(http,packageUrl,zip,ct);
   var expected=File.ReadLines(sums).Select(x=>x.Trim().Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries)).FirstOrDefault(x=>x.Length>=2&&x[^1].TrimStart('*')==PackageName)?[0];
   if(string.IsNullOrWhiteSpace(expected))throw new InvalidDataException("Checksum file has no package hash.");
   using var fs=File.OpenRead(zip);var actual=Convert.ToHexString(await SHA256.HashDataAsync(fs,ct)).ToLowerInvariant();
   if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),Convert.FromHexString(actual)))throw new InvalidDataException("Downloaded update SHA-256 mismatch.");
   var updaterSource=Path.Combine(AppContext.BaseDirectory,"GtaVietLife.Updater.exe");if(!File.Exists(updaterSource))throw new FileNotFoundException("Updater helper is missing.",updaterSource);
   var updater=Path.Combine(Path.GetTempPath(),"GtaVietLife.Updater-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(updaterSource,updater);
   var target=AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
   var psi=new ProcessStartInfo(updater){UseShellExecute=true};psi.ArgumentList.Add(zip);psi.ArgumentList.Add(target);psi.ArgumentList.Add(Environment.ProcessId.ToString());
   Process.Start(psi);RuntimeManager.Log("Verified update "+tag+" staged; handing off to updater.");
   return true;
  }
 }
 static async Task Download(HttpClient http,string url,string path,CancellationToken ct){using var r=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);r.EnsureSuccessStatusCode();await using var input=await r.Content.ReadAsStreamAsync(ct);await using var output=File.Create(path);await input.CopyToAsync(output,ct);}
}
