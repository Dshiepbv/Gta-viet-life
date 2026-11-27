using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using GtaVietLife.Launcher.Models;
namespace GtaVietLife.Launcher.Services;
public sealed class RuntimeManager:IDisposable {
 readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(3)};
 Process? host,bridge; readonly string root=AppContext.BaseDirectory;
 readonly string data=Path.Combine(LauncherSettings.Dir,"Data");
 readonly string tokenPath=Path.Combine(LauncherSettings.Dir,"host.token");
 public string HostData=>Path.Combine(data,"Engine"); public string BridgeData=>Path.Combine(data,"Bridge");
 public string Token=>File.Exists(tokenPath)?File.ReadAllText(tokenPath).Trim():"";
 public RuntimeManager(){Directory.CreateDirectory(data);Directory.CreateDirectory(HostData);Directory.CreateDirectory(BridgeData);if(!File.Exists(tokenPath)||File.ReadAllText(tokenPath).Trim().Length<48)File.WriteAllText(tokenPath,Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));}
 public (bool,string,string) InstallAdapter(string exe){
  var gd=Path.GetDirectoryName(exe)!;var sd=Path.Combine(gd,"scripts");
  bool gameRunning=Process.GetProcessesByName("GTA5").Length>0||Process.GetProcessesByName("GTA5_Enhanced").Length>0;
  if(gameRunning){
   if(File.Exists(Path.Combine(sd,"GtaVietLifeCore.cs")))return(true,"GTA V đang chạy; giữ nguyên các script đã nạp. Bản cập nhật module sẽ áp dụng ở lần mở game kế tiếp.",sd);
   return(false,"GTA V đang chạy và chưa có Adapter trong scripts. Hãy thoát game để Launcher cài đặt an toàn.",sd);
  }
  bool asi=File.Exists(Path.Combine(gd,"ScriptHookVDotNet.asi"));
  bool dll=File.Exists(Path.Combine(gd,"ScriptHookVDotNet3.dll"))||File.Exists(Path.Combine(gd,"ScriptHookVDotNet2.dll"))||File.Exists(Path.Combine(sd,"ScriptHookVDotNet3.dll"));
  if(!asi||!dll)return(false,"Chưa phát hiện đầy đủ ScriptHookVDotNet. Cần cài runtime tương thích trước; Launcher không tự thay thế mod loader.",sd);
  var src=Path.Combine(root,"game");if(!Directory.Exists(src))return(false,"Thiếu gói module game trong bộ cài.",sd);
  Directory.CreateDirectory(sd);var backup=Path.Combine(data,"Backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
  foreach(var f in Directory.GetFiles(src,"GtaVietLife*.*")){
   var n=Path.GetFileName(f);if(!n.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)&&!n.EndsWith(".ini",StringComparison.OrdinalIgnoreCase))continue;
   var t=Path.Combine(sd,n);
   if(File.Exists(t)){
    var oldHash=SHA256.HashData(File.ReadAllBytes(t));var newHash=SHA256.HashData(File.ReadAllBytes(f));
    if(System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(oldHash,newHash))continue;
    Directory.CreateDirectory(backup);File.Copy(t,Path.Combine(backup,n),true);
   }
   File.Copy(f,t,true);
  }
  var iconSource=Path.Combine(src,"GtaVietLifeIcons");
  if(Directory.Exists(iconSource)){
   var iconTarget=Path.Combine(sd,"GtaVietLifeIcons");
   foreach(var icon in Directory.GetFiles(iconSource,"*",SearchOption.AllDirectories)){
    var rel=Path.GetRelativePath(iconSource,icon);var dest=Path.Combine(iconTarget,rel);
    if(File.Exists(dest)){
     var a=SHA256.HashData(File.ReadAllBytes(dest));var b=SHA256.HashData(File.ReadAllBytes(icon));
     if(System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a,b))continue;
     var backupFile=Path.Combine(backup,"GtaVietLifeIcons",rel);Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);File.Copy(dest,backupFile,true);
    }
    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(icon,dest,true);
   }
  }
  return(true,"Đã phát hiện tệp ScriptHookVDotNet và cài module GTA Viet Life. Phiên bản runtime/game sẽ được xác nhận qua phiên Adapter; save game và mod khác được giữ nguyên.",sd);
 }
 public async Task StartAsync(CancellationToken ct){
  if(!await HealthyAsync("http://127.0.0.1:5580/health",ct)){Kill(host);host=null;KillNamed("GtaVietLifeEngine");StartHost();}
  await WaitAsync("http://127.0.0.1:5580/health",30,ct);
  if(!await HealthyAsync("http://127.0.0.1:5577/ping",ct)){Kill(bridge);bridge=null;KillNamed("GtaVietLifeBridge");StartBridge();}
  await WaitAsync("http://127.0.0.1:5577/ping",180,ct);
 }
 async Task<bool> HealthyAsync(string url,CancellationToken ct){try{using var r=await http.GetAsync(url,ct);return r.IsSuccessStatusCode;}catch{return false;}}
 void StartHost(){
  var exe=Path.Combine(root,"runtime","engine","GtaVietLifeEngine.exe");if(!File.Exists(exe))throw new FileNotFoundException("Thiếu Engine Host đã đóng gói.",exe);
  if(host is{HasExited:false})return;
  KillNamed("GtaVietLifeEngine");
  var p=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe)!};
  p.ArgumentList.Add("--host");p.ArgumentList.Add("127.0.0.1");p.ArgumentList.Add("--port");p.ArgumentList.Add("5580");p.ArgumentList.Add("--token-file");p.ArgumentList.Add(tokenPath);p.ArgumentList.Add("--data-dir");p.ArgumentList.Add(HostData);
  host=Process.Start(p)??throw new InvalidOperationException("Không khởi động được Engine Host.");Track(host,"engine");
 }
 void StartBridge(){
  var exe=Path.Combine(root,"runtime","bridge","GtaVietLifeBridge.exe");if(!File.Exists(exe))throw new FileNotFoundException("Thiếu cầu nối AI đã đóng gói.",exe);
  var sample=Path.Combine(root,"runtime","bridge","config.example.json");var userConfig=Path.Combine(BridgeData,"config.json");
  if(!File.Exists(userConfig)&&File.Exists(sample))File.Copy(sample,userConfig);
  if(bridge is{HasExited:false})return;
  KillNamed("GtaVietLifeBridge");
  var p=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe)!};p.Environment["GVL_DATA_DIR"]=BridgeData;
  bridge=Process.Start(p)??throw new InvalidOperationException("Không khởi động được cầu nối AI.");Track(bridge,"bridge");
 }
 void Track(Process p,string n){p.EnableRaisingEvents=true;p.Exited+=(_,_)=>Log(n+" exited "+p.ExitCode);}
 async Task WaitAsync(string url,int seconds,CancellationToken ct){
  var end=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<end){ct.ThrowIfCancellationRequested();
   if(host?.HasExited==true)throw new InvalidOperationException("Engine Host thoát với mã "+host.ExitCode);
   if(bridge?.HasExited==true)throw new InvalidOperationException("Cầu nối AI thoát với mã "+bridge.ExitCode);
   try{using var r=await http.GetAsync(url,ct);if(r.IsSuccessStatusCode)return;}catch{}
   await Task.Delay(700,ct);
  }throw new TimeoutException("Dịch vụ không phản hồi: "+url);
 }
 public async Task<JsonDocument> StatusAsync(CancellationToken ct){
  using var q=new HttpRequestMessage(HttpMethod.Get,"http://127.0.0.1:5580/engine/v1/status");q.Headers.Add("X-GVL-Token",Token);
  using var r=await http.SendAsync(q,ct);r.EnsureSuccessStatusCode();return JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
 }
 public async Task<bool> BridgeHealthyAsync(){try{using var r=await http.GetAsync("http://127.0.0.1:5577/ping");return r.IsSuccessStatusCode;}catch{return false;}}
 public async Task RestartBridgeAsync(CancellationToken ct){
  Kill(bridge);bridge=null;KillNamed("GtaVietLifeBridge");StartBridge();
  await WaitAsync("http://127.0.0.1:5577/ping",180,ct);
 }
 static void KillNamed(string name){foreach(var p in Process.GetProcessesByName(name)){try{p.Kill(true);p.WaitForExit(3000);}catch{}}}
 public void Launch(string exe){Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(exe)!});}
 public async Task StopAsync(){
  try{using var q=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:5580/engine/v1/shutdown");q.Headers.Add("X-GVL-Token",Token);q.Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new{request_id=Guid.NewGuid().ToString("N")}),System.Text.Encoding.UTF8,"application/json");using var r=await http.SendAsync(q);}catch{}
  await Task.Delay(250);Kill(host);Kill(bridge);KillNamed("GtaVietLifeBridge");
 }
 static void Kill(Process? p){try{if(p is{HasExited:false})p.Kill(true);}catch{}}
 public static void Log(string s){try{Directory.CreateDirectory(LauncherSettings.Dir);File.AppendAllText(Path.Combine(LauncherSettings.Dir,"launcher.log"),$"[{DateTime.Now:O}] {s}{Environment.NewLine}");}catch{}}
 public void Dispose()=>http.Dispose();
}