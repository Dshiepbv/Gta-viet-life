using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using GtaVietLife.Launcher.Models;
using GtaVietLife.Launcher.Services;
namespace GtaVietLife.Launcher;
public partial class MainWindow:Window {
 readonly LauncherSettings settings=LauncherSettings.Load();readonly RuntimeManager runtime=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(3)};bool busy,ready;CancellationTokenSource? cts;
 public MainWindow(){
  InitializeComponent();GamePathBox.Text=settings.GameExecutable;timer.Tick+=Poll;
  Loaded+=async(_,_)=>{
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
   await runtime.StartAsync(CancellationToken.None);ready=true;HostState.Text="Hoạt động";BridgeState.Text="Hoạt động";AdapterState.Text="Đã cài · chờ game";
   Set("SẴN SÀNG KHỞI CHẠY","Các dịch vụ đã hoạt động và Adapter đã được cài. Sếp có thể mở GTA V.");PlayButton.IsEnabled=true;
   cts=new();timer.Start();
  }catch(Exception ex){Log("ERROR "+ex);Set("CHƯA SẴN SÀNG",ex.Message);HostState.Text="Lỗi";BridgeState.Text="Lỗi";AdapterState.Text="Chưa xác nhận";}
  finally{busy=false;StartButton.IsEnabled=true;Progress.IsIndeterminate=false;}
 }
 void Play_Click(object s,RoutedEventArgs e){try{runtime.Launch(GamePathBox.Text.Trim());Set("ĐANG CHỜ ADAPTER","GTA V đã mở. Chờ ScriptHookVDotNet nạp và kết nối Engine Host.");}catch(Exception ex){Set("KHÔNG MỞ ĐƯỢC GAME",ex.Message);}}
 async void Poll(object? s,EventArgs e){
  try{using var d=await runtime.StatusAsync(CancellationToken.None);bool connected=d.RootElement.GetProperty("adapter_connected").GetBoolean();
   HostState.Text="Hoạt động";BridgeState.Text=await runtime.BridgeHealthyAsync()?"Hoạt động":"Mất kết nối";AdapterState.Text=connected?"Đã bắt tay":"Chưa vào game / chưa kết nối";
   if(connected)Set("ĐANG TRONG GAME · ĐÃ KẾT NỐI","Adapter đã bắt tay và gửi heartbeat tới Engine Host.");
   else if(ready)Set("SẴN SÀNG KHỞI CHẠY","Dịch vụ hoạt động. Mở GTA V để Adapter kết nối.");
  }catch(Exception ex){HostState.Text="Mất kết nối";Log("Poll "+ex.Message);}
 }
 void Export_Click(object s,RoutedEventArgs e){
  var dest=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"GtaVietLife-Diagnostics.txt");
  using var w=new StreamWriter(dest,false);foreach(var p in new[]{Path.Combine(LauncherSettings.Dir,"launcher.log"),Path.Combine(runtime.HostData,"engine-host.log"),Path.Combine(runtime.BridgeData,"bridge.log")}){w.WriteLine("===== "+Path.GetFileName(p)+" =====");if(File.Exists(p))w.WriteLine(File.ReadAllText(p));}
  MessageBox.Show("Đã xuất nhật ký: "+dest,"GTA Viet Life");
 }
 void Exit_Click(object s,RoutedEventArgs e)=>Close();
 void Set(string title,string detail){StatusTitle.Text=title;StatusDetail.Text=detail;StatusTitle.Foreground=ready?System.Windows.Media.Brushes.LightGreen:System.Windows.Media.Brushes.White;}
 void Log(string s){RuntimeManager.Log(s);Dispatcher.Invoke(()=>LogText.Text+=$"[{DateTime.Now:HH:mm:ss}] {s}\n");}
}