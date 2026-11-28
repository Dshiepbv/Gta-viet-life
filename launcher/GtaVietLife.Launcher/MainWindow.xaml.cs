using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using GtaVietLife.Launcher.Models;
using GtaVietLife.Launcher.Services;
namespace GtaVietLife.Launcher;
public partial class MainWindow:Window {
 readonly LauncherSettings settings=LauncherSettings.Load();readonly RuntimeManager runtime=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(3)};bool busy,ready,recovering,closing,stopped;CancellationTokenSource? cts;
 public MainWindow(){
  InitializeComponent();
  if(!File.Exists(settings.GameExecutable)){var found=InstalledGame.Find();if(found!=null){settings.GameExecutable=found;settings.Save();}}
  GamePathBox.Text=settings.GameExecutable;AutoLaunchBox.IsChecked=settings.AutoLaunchGame;timer.Tick+=Poll;
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
   try{
    var newer=await new UpdateService().FindNewerAsync(CancellationToken.None);
    if(newer!=null){UpdateButton.Content="Cập nhật lên "+newer.Tag;Log("Có bản mới "+newer.Tag+". Bấm nút cập nhật để cài.");}
   }catch(Exception ex){Log("Update check failed: "+ex.Message);}
   if(File.Exists(settings.GameExecutable)){await Prepare();if(ready&&settings.AutoLaunchGame&&!RuntimeManager.GameRunning())Play_Click(this,new RoutedEventArgs());}else Set("CẦN CHỌN GTA V","Chọn GTA5.exe một lần để lưu đường dẫn.");};
  Closing+=async(_,e)=>{
   if(stopped)return;
   e.Cancel=true;   // đóng thật sau khi các dịch vụ đã dừng
   if(closing)return;
   if(RuntimeManager.GameRunning()){
    var choice=MessageBox.Show("GTA V đang chạy. Đóng Launcher sẽ dừng Engine Host và cầu nối AI. Chọn Yes để giữ Launcher mở và tiếp tục chơi.",
      "GTA Viet Life",MessageBoxButton.YesNo,MessageBoxImage.Warning);
    if(choice==MessageBoxResult.Yes)return;
   }
   closing=true;timer.Stop();cts?.Cancel();
   Set("ĐANG DỪNG DỊCH VỤ","Chờ Engine Host lưu dữ liệu và thoát.");
   try{await runtime.StopAsync();}catch(Exception ex){Log("Stop failed: "+ex.Message);}
   runtime.Dispose();stopped=true;
   await Dispatcher.BeginInvoke(new Action(Close));
  };
 }
 void Browse_Click(object s,RoutedEventArgs e){var d=new OpenFileDialog{Title="Chọn GTA5.exe",Filter="GTA V|GTA5.exe;GTA5_Enhanced.exe|Executable|*.exe"};if(d.ShowDialog()==true){GamePathBox.Text=d.FileName;settings.GameExecutable=d.FileName;settings.Save();_=Prepare();}}
 async void Start_Click(object s,RoutedEventArgs e)=>await Prepare();
 async Task Prepare(){
  if(busy)return;busy=true;StartButton.IsEnabled=false;PlayButton.IsEnabled=false;Progress.IsIndeterminate=true;
  try{
   var exe=GamePathBox.Text.Trim();if(!File.Exists(exe))throw new FileNotFoundException("Chọn đúng tệp GTA5.exe.");
   settings.GameExecutable=exe;settings.Save();Set("ĐANG KIỂM TRA","Kiểm tra mod runtime và tự cài module vào thư mục scripts.");
   Set("ĐANG CÀI MODULE","Kiểm tra tương thích và cài module vào thư mục scripts.");
   var result=runtime.InstallAdapter(exe);RuntimeInfo.Text=result.Item2;Log(result.Item2);
   if(!result.Item1){Set("KHÔNG TƯƠNG THÍCH",result.Item2);HostState.Text="Chưa khởi động";AdapterState.Text="Chưa cài";return;}
   await InstallMloPacks(Path.GetDirectoryName(exe)!);
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
 async Task InstallMloPacks(string gameDir){
  var manifest=Path.Combine(AppContext.BaseDirectory,"manifest.json");
  var packs=MloPacks.Load(manifest,Path.Combine(LauncherSettings.Dir,"mlo-packs.json"));
  var catalog=MloPacks.Catalog(manifest);
  foreach(var candidate in catalog){
   var active=MloPacks.Activate(candidate,true);   // chi goi toi day khi kiem tra tuong thich da qua
   if(active!=null)packs.Add(active);else Log("MLO "+candidate.Name+": "+candidate.Status+" (chỉ tham khảo, chưa kích hoạt)");
  }
  if(packs.Count==0)return;
  Set("ĐANG TẢI GÓI NỘI THẤT",packs.Count+" gói MLO khai báo trong manifest.json / mlo-packs.json.");
  using var http=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromMinutes(20)};http.DefaultRequestHeaders.UserAgent.ParseAdd("GTA-Viet-Life-Launcher/0.3");
  var results=await MloPacks.InstallAll(packs,gameDir,Path.Combine(LauncherSettings.Dir,"Data","MloStaging"),Path.Combine(LauncherSettings.Dir,"Data","Backups","mlo"),
   async (url,path,ct)=>{using var r=await http.GetAsync(url,System.Net.Http.HttpCompletionOption.ResponseHeadersRead,ct);r.EnsureSuccessStatusCode();
    await using var input=await r.Content.ReadAsStreamAsync(ct);await using var output=File.Create(path);await input.CopyToAsync(output,ct);},CancellationToken.None);
  foreach(var r in results)Log("MLO "+r.Name+": "+r.Message);
 }
 async void Update_Click(object s,RoutedEventArgs e){
  if(busy||closing)return;
  if(RuntimeManager.GameRunning()){MessageBox.Show("Hãy thoát GTA V trước khi cập nhật.","GTA Viet Life");return;}
  busy=true;UpdateButton.IsEnabled=false;StartButton.IsEnabled=false;PlayButton.IsEnabled=false;Progress.IsIndeterminate=true;
  var handedOver=false;
  try{
   Set("ĐANG KIỂM TRA BẢN MỚI","Đọc danh sách phát hành trên GitHub.");
   var updates=new UpdateService();
   var newer=await updates.FindNewerAsync(CancellationToken.None);
   if(newer==null){
    Set("KHÔNG CÓ BẢN MỚI","Đang dùng bản "+UpdateService.Current.ToString(3)+".");
    MessageBox.Show("Chưa có bản phát hành nào mới hơn "+UpdateService.Current.ToString(3)+".","GTA Viet Life");return;
   }
   if(MessageBox.Show("Cài bản "+newer.Tag+" (đang dùng "+UpdateService.Current.ToString(3)+")?\nTải khoảng vài trăm MB, Launcher sẽ tự mở lại. Save game được giữ nguyên.",
      "GTA Viet Life",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){Set("ĐÃ HỦY CẬP NHẬT","Bạn có thể bấm lại sau.");return;}
   // Dịch vụ đang chạy giữ file trong thư mục cài: dừng trước khi trình cập nhật thay thư mục.
   timer.Stop();cts?.Cancel();ready=false;await runtime.StopAsync();
   Set("ĐANG TẢI BẢN "+newer.Tag,"Đang tải và kiểm tra checksum. Đừng tắt Launcher.");
   await updates.InstallAsync(newer,CancellationToken.None);
   handedOver=true;stopped=true;runtime.Dispose();Application.Current.Shutdown();
  }catch(Exception ex){Log("Update failed: "+ex);Set("CẬP NHẬT THẤT BẠI",ex.Message+" Bấm 'Kiểm tra & khởi động' để chạy lại dịch vụ.");}
  finally{if(!handedOver){busy=false;UpdateButton.IsEnabled=true;StartButton.IsEnabled=true;Progress.IsIndeterminate=false;}}
 }
 void AutoLaunch_Click(object s,RoutedEventArgs e){settings.AutoLaunchGame=AutoLaunchBox.IsChecked==true;settings.Save();}
 void Export_Click(object s,RoutedEventArgs e){
  try{
   var files=DiagnosticsBundle.LogFiles(LauncherSettings.Dir,runtime.HostData,runtime.BridgeData,Path.GetDirectoryName(settings.GameExecutable??""));
   var dest=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"GtaVietLife-Diagnostics.zip");
   var count=DiagnosticsBundle.Write(files,dest);
   MessageBox.Show("Đã xuất "+count+" nhật ký: "+dest+"\nGói chẩn đoán không chứa file save hoặc cấu hình. Nhật ký có thể có nội dung tương tác; hãy kiểm tra trước khi chia sẻ.","GTA Viet Life");
  }catch(Exception ex){Log("Export failed: "+ex);MessageBox.Show("Không xuất được nhật ký: "+ex.Message,"GTA Viet Life");}
 }
 void Exit_Click(object s,RoutedEventArgs e)=>Close();
 void Set(string title,string detail){StateBadge.Text=LauncherStates.Of(title).ToUpperInvariant();StatusTitle.Text=title;StatusDetail.Text=detail;bool positive=title.StartsWith("SẴN SÀNG",StringComparison.Ordinal)||title.StartsWith("ĐANG TRONG GAME",StringComparison.Ordinal);StatusTitle.Foreground=positive?System.Windows.Media.Brushes.LightGreen:System.Windows.Media.Brushes.White;}
 void Log(string s){RuntimeManager.Log(s);Dispatcher.Invoke(()=>LogText.Text+=$"[{DateTime.Now:HH:mm:ss}] {s}\n");}
}