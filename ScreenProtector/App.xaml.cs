using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ScreenProtector
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        
        private const int SW_RESTORE = 9;
        
        private static Mutex? mutex;
        
        protected override void OnExit(ExitEventArgs e)
        {
            if (mutex != null)
            {
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
            base.OnExit(e);
        }
        
        private void App_Startup(object sender, StartupEventArgs e)
        {
            bool createdNew;
            mutex = new Mutex(true, "ScreenProtectorMutex", out createdNew);
            
            if (!createdNew)
            {
                // Another instance is already running, bring it to foreground
                IntPtr hWnd = FindWindow(null, ScreenProtector.Properties.Resources.Title_App ?? string.Empty);
                if (hWnd != IntPtr.Zero)
                {
                    if (IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                    }
                    SetForegroundWindow(hWnd);
                }
                
                // Exit this instance
                Current.Shutdown();
                return;
            }
            
            // First instance continues normally
            var mainWindow = new MainWindow();
            
            // Check if started with -startup argument (silent startup to tray)
            bool isStartupLaunch = e.Args.Contains("-startup");
            
            if (isStartupLaunch)
            {
                // Silent startup: minimize to tray without showing on taskbar
                mainWindow.SetStartupSilent(true);
                mainWindow.WindowState = WindowState.Minimized;
            }
            
            mainWindow.Show();
        }
    }
}