using System.Text;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Interop;
using Microsoft.Win32;
using System.IO;
using IOPath = System.IO.Path;
using System.Reflection;
using Forms = System.Windows.Forms;

using ScreenProtector.Properties;

namespace ScreenProtector
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Windows API for dark title bar
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        private const int WS_MINIMIZEBOX = 0x20000;
        private const int WS_MAXIMIZEBOX = 0x10000;
        
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private bool _suppressSliderUpdate = false;
        private DispatcherTimer? _idleTimer;
        private int _idleSecondsThreshold = 60;
        private int? _savedBrightness = null;
        private bool _isDimmed = false;
        private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ScreenProtector";
        private Forms.NotifyIcon? _notifyIcon;
        private Forms.ContextMenuStrip? _contextMenu;

        // Settings properties
        public static Properties.Settings Settings { get; } = Properties.Settings.Default;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;
            SourceInitialized += MainWindow_SourceInitialized;

            // React to language changes to update UI-bound resources
            Localization.Localizer.LanguageChanged += (s, e) =>
            {
                // Force UI update for bindings that use TranslationProvider
                Dispatcher.Invoke(() =>
                {
                    // Re-bind title
                    this.Title = Properties.Resources.Title_App;

                    // Update tray/context menu if initialized
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Text = Properties.Resources.Tray_NotifyText;
                        if (_contextMenu != null)
                        {
                            foreach (Forms.ToolStripItem item in _contextMenu.Items)
                            {
                                if (item is Forms.ToolStripMenuItem mi)
                                {
                                    if (mi.Text == Properties.Resources.Tray_Show || mi.Text == Properties.Resources.Tray_Exit)
                                    {
                                        // already localized
                                    }
                                    else
                                    {
                                        // Attempt to set by name
                                        if (mi.Text.Contains("显示") || mi.Text.Contains("Show")) mi.Text = Properties.Resources.Tray_Show;
                                        if (mi.Text.Contains("退出") || mi.Text.Contains("Exit")) mi.Text = Properties.Resources.Tray_Exit;
                                    }
                                }
                            }
                        }
                    }
                });
            };
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Apply dark title bar
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
        }

        private bool _isHiddenToTray = false; // Track if window is minimized to tray
        private bool _notifyIconInitialized = false;
        private bool _isStartupSilent = false; // Track if started with -startup argument

        public void SetStartupSilent(bool value) => _isStartupSilent = value;

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (!_notifyIconInitialized)
            {
                _notifyIconInitialized = true;
                InitializeNotifyIcon();
            }
            
            // Handle startup silent mode: hide to tray after initialization
            if (_isStartupSilent && WindowState == WindowState.Minimized)
            {
                Visibility = Visibility.Hidden;
                ShowInTaskbar = false;
            }
            
            // Initialize TextBox values after controls are rendered
            if (IdleInput != null)
                IdleInput.Text = _idleSecondsThreshold.ToString();
            
            if (BrightnessInput != null && BrightnessSlider != null)
            {
                var current = (int)BrightnessSlider.Value;
                BrightnessInput.Text = current.ToString();

                // Initialize icon and label texts based on current resources
                try
                {
                    IconTextBlock.Text = Properties.Resources.Icon_Gear;
                }
                catch { }

                try
                {
                    BrightnessLabel.Text = Properties.Resources.Brightness_Placeholder + Properties.Resources.PercentSign;
                }
                catch { }
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                _isHiddenToTray = true;
                InitializeNotifyIcon(); // Always initialize to ensure tray icon exists
                Visibility = Visibility.Hidden;
                ShowInTaskbar = false;
            }
            else if (WindowState == WindowState.Normal)
            {
                _isHiddenToTray = false;
                Visibility = Visibility.Visible;
                ShowInTaskbar = true;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();

        private int GetIdleSeconds()
        {
            var lastInput = new LASTINPUTINFO();
            lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);
            if (!GetLastInputInfo(ref lastInput))
                return 0;

            var idle = GetTickCount() - lastInput.dwTime;
            return (int)(idle / 1000);
        }

        private void MainWindow_Closed(object? sender, System.EventArgs e)
        {
            if (_idleTimer != null)
            {
                _idleTimer.Stop();
                _idleTimer.Tick -= IdleTimer_Tick;
                _idleTimer = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _contextMenu?.Dispose();
                _notifyIcon.Dispose();
            }

            // Save settings
            Settings.IdleSecondsThreshold = _idleSecondsThreshold;
            Settings.EnableIdleCheck = EnableIdleCheck.IsChecked ?? false;
            Settings.EnableStartup = StartupCheckBox.IsChecked ?? false;
            Settings.Save();
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            RefreshBrightnessSlider();
            // Initialize startup checkbox state from registry
            var isStartupEnabled = IsStartupEnabled();
            StartupCheckBox.IsChecked = isStartupEnabled;

            // Load settings
            _idleSecondsThreshold = Settings.IdleSecondsThreshold;
            IdleSlider.Value = _idleSecondsThreshold;
            EnableIdleCheck.IsChecked = Settings.EnableIdleCheck;

            // Initialize language combo
            try
            {
                Localization.LanguageMenuHelper.AddLanguageMenu(LanguageCombo);
            }
            catch { }
        }

        internal void InitializeNotifyIcon()
        {
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                
                // Use absolute path based on application location
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var iconPath = IOPath.Combine(appDirectory, "Assets", "app.ico");
                if (!File.Exists(iconPath))
                {
                    // Fallback to embedded resource or default icon
                    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenProtector.Assets.app.ico") ??
                                      System.Reflection.Assembly.GetEntryAssembly()?.GetManifestResourceStream("ScreenProtector.Assets.app.ico");
                    if (stream != null)
                    {
                        _notifyIcon = new Forms.NotifyIcon
                        {
                            Icon = new System.Drawing.Icon(stream),
                            Visible = true,
                            Text = "Screen Protector"
                        };
                    }
                    else
                    {
                        // Use default system icon as last resort
                        _notifyIcon = new Forms.NotifyIcon
                                            {
                                                Icon = System.Drawing.SystemIcons.Application,
                                                Visible = true,
                                                Text = Properties.Resources.Tray_NotifyText
                                            };
                    }
                }
                else
                {
                    _notifyIcon = new Forms.NotifyIcon
                                    {
                                        Icon = new System.Drawing.Icon(iconPath),
                                        Visible = true,
                                        Text = Properties.Resources.Tray_NotifyText
                                    };
                }
                
                // Force refresh tray icon
                _notifyIcon.Visible = false;
                _notifyIcon.Visible = true;

                // Create context menu
                _contextMenu = new Forms.ContextMenuStrip();

                var showMenuItem = new Forms.ToolStripMenuItem(Properties.Resources.Tray_Show, null, (s, e) =>
                {
                    ShowFromTray();
                });
                _contextMenu.Items.Add(showMenuItem);

                _contextMenu.Items.Add(new Forms.ToolStripSeparator());

                var exitMenuItem = new Forms.ToolStripMenuItem(Properties.Resources.Tray_Exit, null, (s, e) =>
                {
                    System.Windows.Application.Current.Shutdown();
                });
                _contextMenu.Items.Add(exitMenuItem);

                _notifyIcon.ContextMenuStrip = _contextMenu;

                // Double-click to show window
                _notifyIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left)
                        ShowFromTray();
                };
            }
            catch (Exception ex)
            {
                // Log exception to debug output
                System.Diagnostics.Debug.WriteLine($"Failed to initialize notify icon: {ex}");
                throw; // Re-throw to surface the error
            }
        }

        private void ShowFromTray()
        {
            Dispatcher.BeginInvoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Visibility = Visibility.Visible;
                ShowInTaskbar = true;
                Activate();
                Focus();
            });
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                try
                {
                    var culture = new System.Globalization.CultureInfo(tag);
                    Localization.Localizer.SetCulture(culture);
                }
                catch { }
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshBrightnessSlider();
        }

        private void StartupCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SetStartup(true);
            Settings.EnableStartup = true;
            Settings.Save();
        }

        private void StartupCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SetStartup(false);
            Settings.EnableStartup = false;
            Settings.Save();
        }

        private void EnableIdleCheck_Checked(object sender, RoutedEventArgs e)
        {
            StartIdleTimer();
            Settings.EnableIdleCheck = true;
        }

        private void EnableIdleCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            StopIdleTimer();
            // restore if dimmed
            TryRestoreFromIdle();
            Settings.EnableIdleCheck = false;
        }

        private void IdleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IdleLabel == null || IdleInput == null)
                return;

            _idleSecondsThreshold = (int)Math.Round(e.NewValue);
            IdleLabel.Text = _idleSecondsThreshold + Properties.Resources.SecondUnit;
            IdleInput.Text = _idleSecondsThreshold.ToString();
            Settings.IdleSecondsThreshold = _idleSecondsThreshold;
            Settings.Save();
        }

        private void IdleInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IdleInput == null || IdleSlider == null)
                return;

            if (int.TryParse(IdleInput.Text, out int value))
            {
                value = Math.Clamp(value, 0, 600);
                _idleSecondsThreshold = value;
                IdleSlider.Value = value;
                IdleLabel.Text = value + Properties.Resources.SecondUnit;
                Settings.IdleSecondsThreshold = value;
                Settings.Save();
            }
        }

        private void NumberValidation_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        private static bool IsTextAllowed(string text)
        {
            return text.All(c => char.IsDigit(c));
        }

        private void StartIdleTimer()
        {
            if (_idleTimer == null)
            {
                _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _idleTimer.Tick += IdleTimer_Tick;
            }
            _idleTimer.Start();
        }

        private void StopIdleTimer()
        {
            if (_idleTimer != null)
                _idleTimer.Stop();
        }

        private void IdleTimer_Tick(object? sender, System.EventArgs e)
        {
            try
            {
                if (_idleSecondsThreshold <= 0)
                    return;

                var idle = GetIdleSeconds();
                if (!_isDimmed && idle >= _idleSecondsThreshold)
                {
                    // go dim
                    var current = GetCurrentBrightness();
                    if (current.HasValue)
                    {
                        _savedBrightness = current.Value;
                        try
                        {
                            SetBrightness(0);
                            _suppressSliderUpdate = true;
                            BrightnessSlider.Value = 0;
                            BrightnessLabel.Text = Properties.Resources.Brightness_Placeholder + Properties.Resources.PercentSign; // keep same, will update icon separately
                            _suppressSliderUpdate = false;
                            _isDimmed = true;
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
                else if (_isDimmed && idle < _idleSecondsThreshold)
                {
                    // restore
                    TryRestoreFromIdle();
                }
            }
            catch
            {
                // swallow
            }
        }

        private void TryRestoreFromIdle()
        {
            if (_isDimmed)
            {
                if (_savedBrightness.HasValue)
                {
                    try
                    {
                        SetBrightness(_savedBrightness.Value);
                        _suppressSliderUpdate = true;
                        BrightnessSlider.Value = _savedBrightness.Value;
                        BrightnessLabel.Text = _savedBrightness.Value + Properties.Resources.PercentSign;
                        _suppressSliderUpdate = false;
                    }
                    catch
                    {
                        // ignore
                    }
                }
                _savedBrightness = null;
                _isDimmed = false;
            }
        }

        private void RefreshBrightnessSlider()
        {
            try
            {
                var current = GetCurrentBrightness();
                if (current.HasValue)
                {
                    _suppressSliderUpdate = true;
                    BrightnessSlider.Value = current.Value;
                    BrightnessLabel.Text = current.Value + Properties.Resources.PercentSign;
                    BrightnessInput.Text = current.Value.ToString();
                    _suppressSliderUpdate = false;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"{Properties.Resources.Error_CannotReadBrightness}: {ex.Message}", Properties.Resources.Error_Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderUpdate || BrightnessLabel == null || BrightnessInput == null)
                return;

            var value = (int)Math.Round(e.NewValue);
            BrightnessLabel.Text = value + Properties.Resources.PercentSign;
            BrightnessInput.Text = value.ToString();
            try
            {
                SetBrightness(value);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"{Properties.Resources.Error_CannotSetBrightness}: {ex.Message}", Properties.Resources.Error_Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrightnessInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (BrightnessInput == null || BrightnessSlider == null || _suppressSliderUpdate)
                return;

            if (int.TryParse(BrightnessInput.Text, out int value))
            {
                value = Math.Clamp(value, 0, 100);
                _suppressSliderUpdate = true;
                BrightnessSlider.Value = value;
                BrightnessLabel.Text = value + Properties.Resources.PercentSign;
                try
                {
                    SetBrightness(value);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"{Properties.Resources.Error_CannotSetBrightness}: {ex.Message}", Properties.Resources.Error_Title, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _suppressSliderUpdate = false;
                }
            }
        }

        private int? GetCurrentBrightness()
        {
            try
            {
                var scope = new ManagementScope("\\\\.\\root\\WMI");
                scope.Connect();
                using (var searcher = new ManagementObjectSearcher(scope, new SelectQuery("WmiMonitorBrightness")))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        var val = obj.GetPropertyValue("CurrentBrightness");
                        if (val != null)
                            return Convert.ToInt32(val);
                    }
                }
            }
            catch
            {
                // swallow to allow fallback
            }
            return null;
        }

        private void SetBrightness(int brightness)
        {
            // brightness: 0-100
            var scope = new ManagementScope("\\\\.\\root\\WMI");
            scope.Connect();
            using (var searcher = new ManagementObjectSearcher(scope, new SelectQuery("WmiMonitorBrightnessMethods")))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject obj in results)
                {
                    // Parameters: Timeout, Brightness
                    var inParams = new object[] { (uint)1, (byte)brightness };
                    obj.InvokeMethod("WmiSetBrightness", inParams);
                    return;
                }
            }
            throw new InvalidOperationException("没有找到支持的亮度方法 (WmiMonitorBrightnessMethods)。");
        }

        private bool IsStartupEnabled()
        {
            try
            {
                // Check shortcut in user's Startup folder
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = IOPath.Combine(startupFolder, AppName + ".lnk");

                if (File.Exists(shortcutPath))
                {
                    try
                    {
                        // Use dynamic COM interop to read shortcut
                        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        string target = shortcut.TargetPath;

                        if (!string.IsNullOrEmpty(target))
                        {
                            var currentExe = Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.Location ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

                            bool matches = string.Equals(IOPath.GetFullPath(target), IOPath.GetFullPath(currentExe ?? ""), StringComparison.OrdinalIgnoreCase);
                            return matches;
                        }
                    }
                    catch (Exception ex)
                    {
                        // If shortcut exists but we can't read it, assume it's enabled
                        return true;
                    }
                }

                // Fallback: check registry Run key
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, false);
                if (key != null)
                {
                    var value = key.GetValue(AppName);
                    bool isInRegistry = value != null && !string.IsNullOrEmpty(value.ToString());
                    return isInRegistry;
                }
            }
            catch (Exception ex)
            {
            }
            return false;
        }

        private void SetStartup(bool enable)
        {
            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = IOPath.Combine(startupFolder, AppName + ".lnk");

                if (enable)
                {
                    // Get the executable path
                    var exePath = Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.Location ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

                    if (exePath != null)
                    {
                        try
                        {
                            // Create shortcut using COM with dynamic binding
                            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                            dynamic shortcut = shell.CreateShortcut(shortcutPath);

                            shortcut.TargetPath = exePath;
                            shortcut.Arguments = "-startup";
                            shortcut.WorkingDirectory = IOPath.GetDirectoryName(exePath) ?? "";
                            shortcut.Description = "Screen Protector";
                            shortcut.IconLocation = exePath;
                            shortcut.Save();

                            // Clean up COM objects
                            Marshal.ReleaseComObject(shortcut);
                            Marshal.ReleaseComObject(shell);

                            return;
                        }
                        catch (Exception comEx)
                        {
                        }
                    }

                    // Fallback: write to registry
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, true) ?? Registry.CurrentUser.CreateSubKey(StartupRegKey);
                        var fallbackPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (fallbackPath != null)
                        {
                            key.SetValue(AppName, $"\"{fallbackPath}\" -startup");
                        }
                    }
                    catch (Exception regEx)
                    {
                    }
                }
                else
                {
                    // Delete shortcut if exists
                    if (File.Exists(shortcutPath))
                    {
                        try 
                        { 
                            File.Delete(shortcutPath);
                        } 
                        catch (Exception delEx)
                        {
                        }
                    }

                    // Also remove registry Run key
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, true);
                        if (key != null)
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                    catch (Exception regEx)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                StartupCheckBox.IsChecked = IsStartupEnabled();
            }
        }
    }
}