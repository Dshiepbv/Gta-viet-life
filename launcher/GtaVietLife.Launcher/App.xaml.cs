using System.Threading;
using System.Windows;
namespace GtaVietLife.Launcher;
public partial class App : Application
{
    private Mutex? singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstance = new Mutex(true, "Local\\GtaVietLifeLauncher", out bool created);
        if (!created)
        {
            MessageBox.Show("GTA Viet Life Launcher đang chạy.", "GTA Viet Life", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        try { singleInstance?.ReleaseMutex(); } catch { }
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}