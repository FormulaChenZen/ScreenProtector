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
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {

        private bool _suppressSliderUpdate = false;
        private DispatcherTimer? _idleTimer;
        private int _idleSecondsThreshold = 60;
        private int? _savedBrightness = null;
        private bool _isDimmed = false;
        private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ScreenProtector";
        private Forms.NotifyIcon? _notifyIcon;
        private Forms.ContextMenuStrip? _contextMenu;
        private Forms.ToolStripMenuItem? _screenOffMenuItem;
        private readonly ScreenBlackout _screenBlackout = new();
        private readonly IdleScreenOffTrigger _idleScreenOffTrigger = new();
        private readonly LocalSessionGuard _localSessionGuard = new();
        private readonly RemoteBrightnessSession _remoteBrightness;
        private readonly DispatcherTimer _sessionTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        private bool _automaticBlackout;
        private bool _loadingIdleSettings = true;
        private bool _loadingModeSettings = true;
        private bool _updatingModeControls;
        private bool _useRemoteMode;
        private bool _isClosed;
        private bool _isClosing;
        private Exception? _remoteBrightnessError;
        private long _brightnessRetryAfter;

        // Settings properties
        public static Properties.Settings Settings { get; } = Properties.Settings.Default;

        public MainWindow()
        {
            _useRemoteMode = Settings.UseRemoteMode;
            _remoteBrightness = new RemoteBrightnessSession(
                () => _isDimmed ? _savedBrightness : GetCurrentBrightness(), SetBrightness);
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;
            _screenBlackout.Closed += ScreenBlackout_Closed;
            _sessionTimer.Tick += SessionTimer_Tick;

            // React to language changes to update UI-bound resources
            Localization.Localizer.LanguageChanged += (s, e) =>
            {
                // Force UI update for bindings that use TranslationProvider
                Dispatcher.Invoke(() =>
                {
                    // Re-bind title
                    this.Title = Properties.Resources.Title_App;
                    UpdateScreenOffControls();

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
                                    mi.Text = mi.Name switch
                                    {
                                        "Show" => Properties.Resources.Tray_Show,
                                        "TurnOffDisplay" => Properties.Resources.Tray_TurnOffDisplay,
                                        "Exit" => Properties.Resources.Tray_Exit,
                                        _ => mi.Text
                                    };
                                }
                            }
                        }
                    }
                });
            };
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
                UpdateBrightnessControls((int)BrightnessSlider.Value);
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

        private int GetIdleSeconds(out uint lastInputTick)
        {
            var lastInput = new LASTINPUTINFO();
            lastInput.cbSize = (uint)Marshal.SizeOf(lastInput);
            lastInputTick = 0;
            if (!GetLastInputInfo(ref lastInput))
                return 0;

            lastInputTick = lastInput.dwTime;
            var idle = GetTickCount() - lastInput.dwTime;
            return (int)(idle / 1000);
        }

        private void MainWindow_Closed(object? sender, System.EventArgs e)
        {
            _isClosed = true;
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            _sessionTimer.Stop();
            _sessionTimer.Tick -= SessionTimer_Tick;
            _screenBlackout.Closed -= ScreenBlackout_Closed;
            _screenBlackout.Dispose();
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
            Settings.EnableIdleScreenOff = EnableIdleScreenOff.IsChecked ?? false;
            Settings.AutoSwitchMode = AutoSwitchMode.IsChecked == true;
            Settings.UseRemoteMode = ProtectionModeCombo.SelectedIndex == 1;
            Settings.EnableStartup = StartupCheckBox.IsChecked ?? false;
            Settings.Save();
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            AutoSwitchMode.IsChecked = Settings.AutoSwitchMode;
            ProtectionModeCombo.SelectedIndex = Settings.UseRemoteMode ? 1 : 0;
            _loadingModeSettings = false;
            try
            {
                SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Session notifications unavailable: {ex.Message}");
            }
            RefreshSessionState();
            _sessionTimer.Start();
            RefreshBrightnessSlider();
            // Initialize startup checkbox state from registry
            var isStartupEnabled = IsStartupEnabled();
            StartupCheckBox.IsChecked = isStartupEnabled;

            // Load settings
            _idleSecondsThreshold = Settings.IdleSecondsThreshold;
            IdleSlider.Value = _idleSecondsThreshold;
            EnableIdleScreenOff.IsChecked = Settings.EnableIdleScreenOff;
            EnableIdleCheck.IsChecked = Settings.EnableIdleCheck;
            _loadingIdleSettings = false;
            if (EnableIdleCheck.IsChecked == true)
                StartIdleTimer();

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
                }) { Name = "Show" };
                _contextMenu.Items.Add(showMenuItem);

                _screenOffMenuItem = new Forms.ToolStripMenuItem(Properties.Resources.Tray_TurnOffDisplay, null, (s, e) =>
                {
                    ShowBlackout();
                }) { Name = "TurnOffDisplay", Enabled = CanUseLocalProtection && !_screenBlackout.IsActive };
                _contextMenu.Items.Add(_screenOffMenuItem);

                _contextMenu.Items.Add(new Forms.ToolStripSeparator());

                var exitMenuItem = new Forms.ToolStripMenuItem(Properties.Resources.Tray_Exit, null, (s, e) =>
                {
                    Close();
                }) { Name = "Exit" };
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

        private void ScreenOffButton_Click(object sender, RoutedEventArgs e) => ShowBlackout();

        private bool CanUseLocalProtection => _localSessionGuard.IsLocal && !_useRemoteMode && !_remoteBrightness.HasSavedBrightness;

        private void ProtectionModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => SaveProtectionMode();

        private void AutoSwitchMode_Changed(object sender, RoutedEventArgs e) => SaveProtectionMode();

        private void SaveProtectionMode()
        {
            if (_loadingModeSettings || _updatingModeControls)
                return;

            Settings.AutoSwitchMode = AutoSwitchMode.IsChecked == true;
            Settings.UseRemoteMode = ProtectionModeCombo.SelectedIndex == 1;
            Settings.Save();
            _brightnessRetryAfter = 0;
            RefreshSessionState();
        }

        private void ShowBlackout(bool automatic = false)
        {
            RefreshSessionState();
            if (!CanUseLocalProtection || _screenBlackout.IsActive)
                return;

            ScreenOffErrorText.Visibility = Visibility.Collapsed;
            Exception? error = null;
            try
            {
                _automaticBlackout = automatic;
                _screenBlackout.Show();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                UpdateScreenOffControls();
            }

            if (error != null)
            {
                string message = $"{Properties.Resources.Error_CannotTurnOffDisplay}: {error.Message}";
                if (!automatic)
                    System.Windows.MessageBox.Show(message, Properties.Resources.Error_Title,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                else
                {
                    ScreenOffErrorText.Text = message;
                    ScreenOffErrorText.Visibility = Visibility.Visible;
                }
            }
        }

        private void ScreenBlackout_Closed(object? sender, EventArgs e)
        {
            _automaticBlackout = false;
            UpdateScreenOffControls();
        }

        private void UpdateScreenOffControls()
        {
            bool pending = _screenBlackout.IsActive;
            bool local = _localSessionGuard.IsLocal;
            _updatingModeControls = true;
            try
            {
                ProtectionModeCombo.SelectedIndex = _useRemoteMode ? 1 : 0;
                ProtectionModeCombo.IsEnabled = AutoSwitchMode.IsChecked != true;
            }
            finally { _updatingModeControls = false; }
            ScreenOffButton.IsEnabled = CanUseLocalProtection && !pending;
            IdleProtectionOptions.IsEnabled = CanUseLocalProtection;
            BrightnessOptions.IsEnabled = !_useRemoteMode && !_remoteBrightness.HasSavedBrightness;
            RemoteSessionText.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
            RemoteBrightnessText.Text = _remoteBrightnessError != null
                ? $"{(_useRemoteMode ? Properties.Resources.Error_RdpBrightness : Properties.Resources.Error_RestoreBrightness)}: {_remoteBrightnessError.Message}"
                : _remoteBrightness.IsDimmed ? Properties.Resources.DisplayOff_RdpDimmed : string.Empty;
            RemoteBrightnessText.Visibility = string.IsNullOrEmpty(RemoteBrightnessText.Text)
                ? Visibility.Collapsed : Visibility.Visible;
            ScreenOffPendingText.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            if (!pending)
                _automaticBlackout = false;
            if (_screenOffMenuItem != null)
                _screenOffMenuItem.Enabled = CanUseLocalProtection && !pending;
        }

        private void SessionTimer_Tick(object? sender, EventArgs e) => RefreshSessionState();

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (_isClosed || Dispatcher.HasShutdownStarted)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                if (!_isClosed)
                    RefreshSessionState();
            });
        }

        private void RefreshSessionState()
        {
            if (_isClosing || _isClosed)
                return;

            bool changed = _localSessionGuard.Refresh();
            bool remoteMode = ProtectionModeSelector.UseRemote(Settings.AutoSwitchMode, Settings.UseRemoteMode,
                _localSessionGuard.Kind, _useRemoteMode);
            if (remoteMode != _useRemoteMode)
            {
                _useRemoteMode = remoteMode;
                changed = true;
                if (!remoteMode)
                    _localSessionGuard.RestartIdleCountdown();
            }
            if (changed)
            {
                _idleScreenOffTrigger.Reset();
                _brightnessRetryAfter = 0;
            }

            if (!_localSessionGuard.IsLocal || _useRemoteMode)
            {
                try
                {
                    _screenBlackout.Close();
                }
                catch (Exception ex)
                {
                    ScreenOffErrorText.Text = $"{Properties.Resources.Error_CannotExitBlackout}: {ex.Message}";
                    ScreenOffErrorText.Visibility = Visibility.Visible;
                }
            }
            if (Environment.TickCount64 >= _brightnessRetryAfter)
            {
                try
                {
                    if (_useRemoteMode && (!Settings.AutoSwitchMode || _localSessionGuard.IsRemote))
                    {
                        // Transfer the original idle brightness without briefly lighting the laptop screen.
                        try { _remoteBrightness.Enter(); }
                        finally
                        {
                            if (_remoteBrightness.HasSavedBrightness)
                            {
                                _savedBrightness = null;
                                _isDimmed = false;
                            }
                        }
                        UpdateBrightnessControls(0);
                    }
                    else if (!_useRemoteMode && _remoteBrightness.HasSavedBrightness &&
                        (!Settings.AutoSwitchMode || _localSessionGuard.IsLocal))
                    {
                        int? original = _remoteBrightness.SavedBrightness;
                        _remoteBrightness.Restore();
                        if (original.HasValue)
                            UpdateBrightnessControls(original.Value);
                    }
                    else if (!_localSessionGuard.IsLocal && !_remoteBrightness.HasSavedBrightness)
                    {
                        RestoreFromIdle();
                    }
                    _remoteBrightnessError = null;
                }
                catch (Exception ex)
                {
                    _remoteBrightnessError = ex;
                    _brightnessRetryAfter = Environment.TickCount64 + 5000;
                }
            }
            UpdateScreenOffControls();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            _sessionTimer.Stop();
            Exception? error = null;
            try
            {
                _screenBlackout.Close();
            }
            catch (Exception ex)
            {
                error = new InvalidOperationException(Properties.Resources.Error_CannotExitBlackout, ex);
            }
            try
            {
                RestoreFromIdle();
                _remoteBrightness.Restore();
            }
            catch (Exception ex)
            {
                error ??= new InvalidOperationException(Properties.Resources.Error_RestoreBrightness, ex);
            }
            if (error != null)
            {
                e.Cancel = true;
                System.Windows.MessageBox.Show($"{error.Message}: {error.InnerException?.Message}",
                    Properties.Resources.Error_Title, MessageBoxButton.OK, MessageBoxImage.Error);
                _isClosing = false;
                _sessionTimer.Start();
            }
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
            if (_loadingIdleSettings)
                return;

            StartIdleTimer();
            Settings.EnableIdleCheck = true;
            Settings.Save();
        }

        private void EnableIdleCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_loadingIdleSettings)
                return;

            StopIdleTimer();
            CancelAutomaticBlackout();
            // restore if dimmed
            TryRestoreFromIdle();
            Settings.EnableIdleCheck = false;
            Settings.Save();
        }

        private void EnableIdleScreenOff_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingIdleSettings)
                return;

            Settings.EnableIdleScreenOff = EnableIdleScreenOff.IsChecked == true;
            Settings.Save();
            if (Settings.EnableIdleScreenOff)
                TryRestoreFromIdle();
            else
                CancelAutomaticBlackout();
        }

        private void CancelAutomaticBlackout()
        {
            if (!_automaticBlackout)
                return;

            _screenBlackout.Close();
        }

        private void IdleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IdleLabel == null || IdleInput == null)
                return;

            _idleSecondsThreshold = (int)Math.Round(e.NewValue);
            IdleLabel.Text = _idleSecondsThreshold + Properties.Resources.SecondUnit;
            IdleInput.Text = _idleSecondsThreshold.ToString();
            if (!_loadingIdleSettings)
            {
                Settings.IdleSecondsThreshold = _idleSecondsThreshold;
                Settings.Save();
            }
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
                if (!_loadingIdleSettings)
                {
                    Settings.IdleSecondsThreshold = value;
                    Settings.Save();
                }
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
                if (_isClosing || !CanUseLocalProtection || EnableIdleCheck.IsChecked != true)
                    return;

                var idle = _localSessionGuard.GetIdleSeconds(GetIdleSeconds(out uint lastInputTick));

                bool screenOffEnabled = EnableIdleScreenOff.IsChecked == true;
                bool turnOff = _idleScreenOffTrigger.ShouldTrigger(lastInputTick, idle,
                    _idleSecondsThreshold, screenOffEnabled);

                if (_idleSecondsThreshold <= 0)
                {
                    CancelAutomaticBlackout();
                    TryRestoreFromIdle();
                    return;
                }

                if (screenOffEnabled)
                {
                    if (turnOff && !_screenBlackout.IsActive)
                        ShowBlackout(automatic: true);
                    return;
                }

                if (!_isDimmed && idle >= _idleSecondsThreshold)
                {
                    // go dim
                    var current = GetCurrentBrightness();
                    if (current.HasValue)
                    {
                        _savedBrightness = current.Value;
                        _isDimmed = true;
                        try
                        {
                            SetBrightness(0);
                            UpdateBrightnessControls(0);
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
            try { RestoreFromIdle(); }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        }

        private void RestoreFromIdle()
        {
            if (_isDimmed && _savedBrightness.HasValue)
            {
                SetBrightness(_savedBrightness.Value);
                UpdateBrightnessControls(_savedBrightness.Value);
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
                    UpdateBrightnessControls(current.Value);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"{Properties.Resources.Error_CannotReadBrightness}: {ex.Message}", Properties.Resources.Error_Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateBrightnessControls(int brightness)
        {
            bool previous = _suppressSliderUpdate;
            _suppressSliderUpdate = true;
            try
            {
                BrightnessSlider.Value = brightness;
                BrightnessLabel.Text = brightness + Properties.Resources.PercentSign;
                BrightnessInput.Text = brightness.ToString();
            }
            finally { _suppressSliderUpdate = previous; }
        }

        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderUpdate || _useRemoteMode || _remoteBrightness.HasSavedBrightness || BrightnessLabel == null || BrightnessInput == null)
                return;

            var value = (int)Math.Round(e.NewValue);
            UpdateBrightnessControls(value);
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
            if (BrightnessInput == null || BrightnessSlider == null || _suppressSliderUpdate || _useRemoteMode || _remoteBrightness.HasSavedBrightness)
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
                using (var searcher = new ManagementObjectSearcher(scope, new SelectQuery("SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE")))
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
            if (brightness < 0 || brightness > 100)
                throw new ArgumentOutOfRangeException(nameof(brightness));
            var scope = new ManagementScope("\\\\.\\root\\WMI");
            scope.Connect();
            using (var searcher = new ManagementObjectSearcher(scope, new SelectQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE")))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject obj in results)
                {
                    // Parameters: Timeout, Brightness
                    var inParams = new object[] { (uint)1, (byte)brightness };
                    var result = obj.InvokeMethod("WmiSetBrightness", inParams);
                    if (result == null || Convert.ToUInt32(result) != 0)
                        throw new InvalidOperationException($"WmiSetBrightness failed: {result ?? "no result"}.");
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

        private void WebsiteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 打开网站的正确方式
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://rumystic.com",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                // 如果上面的方式失败，尝试其他方法
                try
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                    psi.FileName = "cmd.exe";
                    psi.Arguments = "/c start https://rumystic.com";
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception innerEx)
                {
                    // 静默处理异常
                }
            }
        }
    }
}
