using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using GtaVietLife.Launcher.Models;
using GtaVietLife.Launcher.Services;
namespace GtaVietLife.Launcher;
public partial class MainWindow:Window {
 readonly LauncherSettings settings=LauncherSettings.Load();readonly RuntimeManager runtime=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(3)};bool busy,ready,recovering;CancellationTokenSource? cts;
 public MainWindow(){
  InitializeComponent();GamePathBox.Text=settings.GameExecutable;timer.Tick+=Poll;
  Loaded+=async(_,_)=>{
   try{
    var manifest=PackageManifest.Load(AppContext.BaseDirectory);
    var damaged=manifest?.Verify(AppContext.BaseDirectory)??new List<string>{"thiếu manifest.json"};
    if(damaged.Count>0){Log("Kiểm tra bộ cài: "+string.Join("; ",damaged.Take(10)));Set("BỘ CÀI BỊ HỎNG","Hãy chạy lại file cài đặt. "+damaged[0]);StartButton.IsEnabled=false;return;}
    Log("Bộ cài "+manifest!.Version+" đầy đủ, checksum khớp.");
    if(UpdateHealth.Pending){await runtime.StartEngineAsync(CancellationToken.None);UpdateHealth.Confirm();Log("Bản cập nhật khởi động tốt.");}
   }catch(Exception ex){
    Log("Startup self-check failed: "+ex);
    if(UpdateHealth.Pending){Application.Current.Shutdown(1);return;}
   }
   try{if(await new UpdateService().CheckAndLaunchUpdaterAsync(CancellationToken.None)){Application.Current.Shutdown();return;}}
   catch(Exception ex){Log("Update check failed: "+ex.Message);}
   if(File.Exists(settings.GameExecutable))await Prepare();else Set("CẦN CHỌN GTA V","Chọn GTA5.exe một lần để lưu đường dẫn.");};
  Closing+=async(_,e)=>{
   bool gameRunning=System.Diagnostics.Process.GetProcessesByName("GTA5").Length>0 ||
                    System.Diagnostics.Process.GetProcessesByName("GTA5_Enhanced").Length>0;
   if(gameRunning){
    var choice=MessageBox.Show("GTA V đang chạy. Đóng Launcher sẽ dừng Engine Host và cầu nối AI. Chọn Yes để giữ Launcher mở và tiếp tục chơi.",
      "GTA Viet Life",MessageBoxButton.YesNo,MessageBoxImage.Warning);
    if(choice==MessageBoxResult.Yes){e.Cancel=true;return;}
   }
   timer.Stop();cts?.Cancel();if(ready)await runtime.StopAsync();runtime.Dispose();
  };
 }
 void Browse_Click(object s,RoutedEventArgs e){var d=new OpenFileDialog{Title="Chọn GTA5.exe",Filter="GTA V|GTA5.exe;GTA5_Enhanced.exe|Executable|*.exe"};if(d.ShowDialog()==true){GamePathBox.Text=d.FileName;settings.GameExecutable=d.FileName;settings.Save();_=Prepare();}}
 async void Start_Click(object s,RoutedEventArgs e)=>await Prepare();
 async Task Prepare(){
  if(busy)return;busy=true;StartButton.IsEnabled=false;PlayButton.IsEnabled=false;Progress.IsIndeterminate=true;
  try{
   var exe=GamePathBox.Text.Trim();if(!File.Exists(exe))throw new FileNotFoundException("Chọn đúng tệp GTA5.exe.");
   settings.GameExecutable=exe;settings.Save();Set("ĐANG KIỂM TRA","Kiểm tra mod runtime và tự cài module vào thư mục scripts.");
   var result=runtime.InstallAdapter(exe);RuntimeInfo.Text=result.Item2;Log(result.Item2);if(!result.Item1)throw new InvalidOperationException(result.Item2);
   Set("ĐANG KHỞI ĐỘNG","Khởi động Engine Host và cầu nối AI cục bộ.");
   await runtime.StartAsync(CancellationToken.None);ready=true;HostState.Text="Hoạt động";BridgeState.Text=runtime.BridgeProblem==null?"Hoạt động":"Không chạy";AdapterState.Text="Đã cài · chờ game";
   Set("SẴN SÀNG KHỞI CHẠY",runtime.BridgeProblem==null?"Các dịch vụ đã hoạt động và module đã được cài. Bạn có thể mở GTA V.":"Engine và module đã sẵn sàng. Cầu nối AI (hội thoại) chưa chạy: "+runtime.BridgeProblem);PlayButton.IsEnabled=true;
   cts=new();timer.Start();
  }catch(Exception ex){Log("ERROR "+ex);Set("CHƯA SẴN SÀNG",ex.Message);HostState.Text="Lỗi";BridgeState.Text="Lỗi";AdapterState.Text="Chưa xác nhận";}
  finally{busy=false;StartButton.IsEnabled=true;Progress.IsIndeterminate=false;}
 }
 void Play_Click(object s,RoutedEventArgs e){try{runtime.Launch(GamePathBox.Text.Trim());Set("ĐANG CHỜ ADAPTER","GTA V đã mở. Chờ ScriptHookVDotNet nạp và kết nối Engine Host.");}catch(Exception ex){Set("KHÔNG MỞ ĐƯỢC GAME",ex.Message);}}
 async void Poll(object? s,EventArgs e){
  try{using var d=await runtime.StatusAsync(CancellationToken.None);var root=d.RootElement;bool connected=root.GetProperty("adapter_connected").GetBoolean();
   HostState.Text="Hoạt động";
   bool bridgeHealthy=await runtime.BridgeHealthyAsync();
   BridgeState.Text=bridgeHealthy?"Hoạt động":"Mất kết nối";
   AdapterState.Text=connected?"Đã bắt tay":"Chưa vào game / chưa kết nối";
   if(!bridgeHealthy&&ready&&!recovering&&runtime.BridgeProblem==null){
    recovering=true;Set("ĐANG KHÔI PHỤC CẦU NỐI AI","Cầu nối AI bị gián đoạn; Launcher đang khởi động lại thành phần này.");
    try{await runtime.RestartBridgeAsync(CancellationToken.None);BridgeState.Text="Hoạt động";Set("SẴN SÀNG KHỞI CHẠY","Cầu nối AI đã được khôi phục.");}
    catch(Exception recovery){Log("Bridge recovery failed: "+recovery);Set("CHẾ ĐỘ HẠN CHẾ",recovery.Message);}
    finally{recovering=false;}
    return;
   }
   if(connected){
    var session=root.GetProperty("adapter_session");
    var edition=session.GetProperty("base_game").GetString()??"gta5";
    var build=session.GetProperty("base_game_build").GetString()??"unknown";
    Set("ĐANG TRONG GAME · ĐÃ KẾT NỐI","Adapter đã bắt tay. Phiên bản game: "+edition+" / "+build+".");
   }
   else if(ready)Set("SẴN SÀNG KHỞI CHẠY","Dịch vụ hoạt động. Mở GTA V để Adapter kết nối.");
  }catch(Exception ex){
   HostState.Text="Mất kết nối";Log("Poll "+ex.Message);
   if(ready&&!busy&&!recovering){
    recovering=true;Set("ĐANG KHÔI PHỤC DỊCH VỤ","Phát hiện dịch vụ cục bộ bị gián đoạn; Launcher đang khởi động lại.");
    try{await runtime.StopAsync();await runtime.StartAsync(CancellationToken.None);HostState.Text="Hoạt động";BridgeState.Text="Hoạt động";Set("SẴN SÀNG KHỞI CHẠY","Dịch vụ đã được khôi phục. Mở GTA V hoặc tiếp tục phiên chơi.");}
    catch(Exception recovery){Log("Recovery failed: "+recovery);Set("CẦN KHẮC PHỤC",recovery.Message);}
    finally{recovering=false;}
   }
  }
 }
 void Export_Click(object s,RoutedEventArgs e){
  var files=new List<string>{
   Path.Combine(LauncherSettings.Dir,"launcher.log"),
   Path.Combine(runtime.HostData,"engine-host.log"),
   Path.Combine(runtime.BridgeData,"bridge.log")
  };
  var gameDir=Path.GetDirectoryName(settings.GameExecutable??"");
  if(!string.IsNullOrWhiteSpace(gameDir)){
   foreach(var name in new[]{"ScriptHookV.log","ScriptHookVDotNet.log"})files.Add(Path.Combine(gameDir,name));
   files.AddRange(Directory.GetFiles(gameDir,"GtaVietLife*.log"));
   var scripts=Path.Combine(gameDir,"scripts");
   if(Directory.Exists(scripts))files.AddRange(Directory.GetFiles(scripts,"GtaVietLife*.log"));
  }
  var dest=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"GtaVietLife-Diagnostics.txt");
  using(var w=new StreamWriter(dest,false, new System.Text.UTF8Encoding(false))){
   foreach(var p in files.Distinct(StringComparer.OrdinalIgnoreCase)){
    if(!File.Exists(p))continue;
    w.WriteLine("===== "+Path.GetFileName(p)+" =====");
    w.WriteLine(Redact(File.ReadAllText(p)));
   }
  }
  MessageBox.Show("Đã xuất nhật ký: "+dest+"\nGói chẩn đoán không chứa file save hoặc cấu hình. Nhật ký có thể có nội dung tương tác; hãy kiểm tra trước khi chia sẻ.","GTA Viet Life");
 }
 static string Redact(string value){
  value=Regex.Replace(value,@"(?i)(""?(?:api[_-]?key|token|password|secret|authorization|x-gvl-token)""?\s*[:=]\s*)(""[^""]*""|'[^']*'|[^,\s;]+)","$1[REDACTED]");
  return Regex.Replace(value,@"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*","Bearer [REDACTED]");
 }
 void Exit_Click(object s,RoutedEventArgs e)=>Close();
 void Set(string title,string detail){StatusTitle.Text=title;StatusDetail.Text=detail;bool positive=title.StartsWith("SẴN SÀNG",StringComparison.Ordinal)||title.StartsWith("ĐANG TRONG GAME",StringComparison.Ordinal);StatusTitle.Foreground=positive?System.Windows.Media.Brushes.LightGreen:System.Windows.Media.Brushes.White;}
 void Log(string s){RuntimeManager.Log(s);Dispatcher.Invoke(()=>LogText.Text+=$"[{DateTime.Now:HH:mm:ss}] {s}\n");}
}