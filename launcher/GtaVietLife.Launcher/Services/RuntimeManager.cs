using System.Diagnostics;
using Microsoft.Win32;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
  if(GameRunning()){
   if(File.Exists(Path.Combine(sd,"GtaVietLifeCore.dll")))return(true,"GTA V đang chạy; giữ nguyên các script đã nạp. Bản cập nhật module sẽ áp dụng ở lần mở game kế tiếp.",sd);
   return(false,"GTA V đang chạy và chưa có module trong scripts. Hãy thoát game để Launcher cài đặt an toàn.",sd);
  }
  string gameVersion=FileVersionInfo.GetVersionInfo(exe).FileVersion??"unknown";
  var problem=LegacyEnvironment.FindProblem(exe,gameVersion);
  if(problem!=null)return(false,problem+" Launcher không tự cài Script Hook V hay ScriptHookVDotNet.",sd);
  if(!HasNet48())return(false,"Máy chưa có .NET Framework 4.8 trở lên.",sd);
  if(!HasVcRuntime())return(false,"Máy chưa có Microsoft Visual C++ Redistributable x64 (v14).",sd);
  var shLog=Path.Combine(gd,"ScriptHookV.log");
  var logLines=(File.Exists(shLog)?ReadShared(shLog):"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
  int unknown=Array.FindLastIndex(logLines,line=>line.Contains("Unknown game version",StringComparison.OrdinalIgnoreCase));
  int success=Array.FindLastIndex(logLines,line=>line.Contains("Success",StringComparison.OrdinalIgnoreCase));
  if(unknown>=0&&unknown>success)
   return(false,"Script Hook V báo Unknown game version cho GTA build "+gameVersion+". Cần bản Script Hook V hỗ trợ đúng bản game.",sd);
  var installed=ScriptInstaller.Install(Path.Combine(root,"game"),sd,Path.Combine(data,"Backups"));
  if(File.Exists(Path.Combine(sd,"GtaVietLifeCore.ini")))EnsureEnginePort(Path.Combine(sd,"GtaVietLifeCore.ini"),Path.Combine(data,"Backups","ini"));
  var note=installed.Copied+installed.Retired==0?"Module đã là bản mới nhất.":
   "Đã cài "+installed.Copied+" file, cất "+installed.Retired+" script .cs cũ vào "+installed.BackupDir+".";
  return(true,"GTA build "+gameVersion+". "+note+" Save game và save của mod được giữ nguyên. Tương thích thực tế chỉ xác nhận được khi Adapter kết nối trong game.",sd);
 }
 static bool GameRunning()=>Process.GetProcessesByName("GTA5").Length>0||Process.GetProcessesByName("GTA5_Enhanced").Length>0;
 static string ReadShared(string path){
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
  using var reader=new StreamReader(stream);return reader.ReadToEnd();
 }
 void EnsureEnginePort(string path,string backupRoot){
  var lines=File.ReadAllLines(path).ToList();
  int section=lines.FindIndex(x=>x.Trim().Equals("[Engine]",StringComparison.OrdinalIgnoreCase));
  bool changed=false;
  if(section<0){lines.Add("");lines.Add("[Engine]");lines.Add("Port=5580");changed=true;}
  else{
   int end=lines.FindIndex(section+1,lines.Count-section-1,x=>x.TrimStart().StartsWith("[",StringComparison.Ordinal));
   if(end<0)end=lines.Count;
   int port=lines.FindIndex(section+1,end-section-1,x=>x.TrimStart().StartsWith("Port=",StringComparison.OrdinalIgnoreCase));
   if(port>=0){if(!lines[port].Trim().Equals("Port=5580",StringComparison.OrdinalIgnoreCase)){lines[port]="Port=5580";changed=true;}}
   else{lines.Insert(end,"Port=5580");changed=true;}
  }
  if(!changed)return;
  Directory.CreateDirectory(backupRoot);File.Copy(path,Path.Combine(backupRoot,Path.GetFileName(path)),true);
  var temp=path+".tmp";File.WriteAllLines(temp,lines,new UTF8Encoding(false));File.Move(temp,path,true);
 }
 static bool HasNet48(){
  try{using var key=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
   return Convert.ToInt32(key?.GetValue("Release")??0)>=528040;
  }catch{return false;}
 }
 static bool HasVcRuntime(){
  try{using var key=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
   return Convert.ToInt32(key?.GetValue("Installed")??0)==1;
  }catch{return false;}
 }
 /// <summary>The Engine Host is required; the voice bridge is optional and reported through BridgeProblem.</summary>
 public async Task StartAsync(CancellationToken ct){
  await StartEngineAsync(ct);
  BridgeProblem=null;
  try{
   if(!await HealthyAsync("http://127.0.0.1:5577/health",ct)){Kill(bridge);bridge=null;KillNamed("GtaVietLifeBridge");StartBridge();}
   await WaitAsync("http://127.0.0.1:5577/health",90,ct);
  }catch(Exception ex)when(ex is not OperationCanceledException){BridgeProblem=ex.Message;Log("Bridge unavailable: "+ex);Kill(bridge);bridge=null;}
 }
 public string? BridgeProblem {get;private set;}
 public async Task StartEngineAsync(CancellationToken ct){
  if(!await HealthyAsync("http://127.0.0.1:5580/health",ct)){Kill(host);host=null;KillNamed("GtaVietLifeEngine");StartHost();}
  await WaitAsync("http://127.0.0.1:5580/health",30,ct);
  using(var hostStatus=await StatusAsync(ct)){
   if(!hostStatus.RootElement.TryGetProperty("service",out var service)||service.GetString()!="gta-viet-life-engine")
    throw new InvalidOperationException("Cổng 5580 không thuộc GTA Viet Life Engine Host đã xác thực.");
  }
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
   if(url.Contains(":5577")&&bridge?.HasExited==true)throw new InvalidOperationException("Cầu nối AI thoát với mã "+bridge.ExitCode);
   try{using var r=await http.GetAsync(url,ct);if(r.IsSuccessStatusCode)return;}catch{}
   await Task.Delay(700,ct);
  }throw new TimeoutException("Dịch vụ không phản hồi: "+url);
 }
 public async Task<JsonDocument> StatusAsync(CancellationToken ct){
  using var q=new HttpRequestMessage(HttpMethod.Get,"http://127.0.0.1:5580/engine/v1/status");q.Headers.Add("X-GVL-Token",Token);
  using var r=await http.SendAsync(q,ct);r.EnsureSuccessStatusCode();return JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
 }
 public async Task<bool> BridgeHealthyAsync(){
  try{using var r=await http.GetAsync("http://127.0.0.1:5577/health");if(!r.IsSuccessStatusCode)return false;
   using var d=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return d.RootElement.GetProperty("service").GetString()=="gta-viet-life-ai-bridge";
  }catch{return false;}
 }
 public async Task RestartBridgeAsync(CancellationToken ct){
  Kill(bridge);bridge=null;KillNamed("GtaVietLifeBridge");StartBridge();
  await WaitAsync("http://127.0.0.1:5577/health",180,ct);
 }
 static void KillNamed(string name){foreach(var p in Process.GetProcessesByName(name)){try{p.Kill(true);p.WaitForExit(3000);}catch{}}}
 public void Launch(string exe){
  // PlayGTAV.exe is the store-aware entry point; GTA5.exe alone fails on some store installs.
  var starter=Path.Combine(Path.GetDirectoryName(exe)!,"PlayGTAV.exe");
  var target=File.Exists(starter)?starter:exe;
  Process.Start(new ProcessStartInfo(target){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(target)!});
 }
 public async Task StopAsync(){
  try{using var q=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:5580/engine/v1/shutdown");q.Headers.Add("X-GVL-Token",Token);q.Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new{request_id=Guid.NewGuid().ToString("N")}),System.Text.Encoding.UTF8,"application/json");using var r=await http.SendAsync(q);}catch{}
  await Task.Delay(250);Kill(host);Kill(bridge);KillNamed("GtaVietLifeBridge");
 }
 static void Kill(Process? p){try{if(p is{HasExited:false})p.Kill(true);}catch{}}
 public static void Log(string s){try{Directory.CreateDirectory(LauncherSettings.Dir);File.AppendAllText(Path.Combine(LauncherSettings.Dir,"launcher.log"),$"[{DateTime.Now:O}] {s}{Environment.NewLine}");}catch{}}
 public void Dispose()=>http.Dispose();
}