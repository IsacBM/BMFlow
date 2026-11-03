using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DockBar
{
    public sealed class App : Application
    {
        [STAThread]
        public static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), "UnhandledException: " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "null")); } catch { }
            };
            try
            {
                bool firstInstance;
                using (var singleInstance = new Mutex(true, @"Local\BMFlow.SingleInstance", out firstInstance))
                {
                    if (!firstInstance) return;
                    var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), "DispatcherUnhandledException: " + (e.Exception != null ? e.Exception.ToString() : "null")); } catch { }
                    };
                    var win = new DockWindow();
                    app.Run(win);
                    GC.KeepAlive(singleInstance);
                }
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), "MainCatch: " + ex.ToString()); } catch { }
            }
        }
    }

    public sealed class DockWindow : Window
    {
        private static readonly int currentProcessId = ReadCurrentProcessId();
        private static readonly FontFamily windowsSystemIconFont = new FontFamily("Segoe Fluent Icons");
        private readonly Dictionary<string, Border> runningDots = new Dictionary<string, Border>();
        private static readonly Dictionary<string, ImageSource> iconCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly object iconCacheLock = new object();
        private readonly List<FrameworkElement> magnifyIcons = new List<FrameworkElement>();
        private readonly Dictionary<FrameworkElement, double> magnifyGrowth = new Dictionary<FrameworkElement, double>();
        private readonly List<FrameworkElement> wavePassives = new List<FrameworkElement>();
        private readonly List<WaveSample> waveSamples = new List<WaveSample>();
        private readonly List<Border> themedTiles = new List<Border>();
        private readonly List<TextBlock> themedText = new List<TextBlock>();
        private readonly List<Border> themedSeparators = new List<Border>();
        private readonly DispatcherTimer timer;
        private readonly System.Threading.Timer adaptiveContrastTimer;
        private readonly System.Threading.Timer volumeMonitorTimer;
        private readonly System.Threading.Timer batteryMonitorTimer;
        private readonly System.Threading.Timer networkMonitorTimer;
        private readonly System.Threading.Timer pinnedStateMonitorTimer;
        private int adaptiveContrastSampling;
        private int volumeMonitorRunning;
        private int batteryMonitorRunning;
        private int networkMonitorRunning;
        private int pinnedStateMonitorRunning;
        private string pinnedStateSignature = String.Empty;
        private readonly DispatcherTimer previewTimer;
        private readonly DispatcherTimer previewHideTimer;
        private readonly DispatcherTimer previewSettleTimer;
        private readonly DispatcherTimer autoHideTimer;
        private readonly DispatcherTimer edgeRevealTimer;
        private readonly DispatcherTimer fullscreenTimer;
        private readonly DispatcherTimer shellRecoveryTimer;
        private readonly DispatcherTimer pinnedRefreshDebounceTimer;
        private TextBlock dayText;
        private TextBlock dateText;
        private TextBlock batteryText;
        private TextBlock volumeText;
        private TextBlock networkText;
        private TextBlock trayArrowText;
        private string lastVolumeGlyph = String.Empty;
        private int lastVolumePercent = -1;
        private bool lastVolumeMuted;
        private string lastBatteryGlyph = String.Empty;
        private string lastBatteryLabel = String.Empty;
        private string lastNetworkGlyph = String.Empty;
        private string lastNetworkLabel = String.Empty;
        private TextBlock clockText;
        private TextBlock clockDateText;
        private Image recycleBinImage;
        private StackPanel dockRow;
        private StackPanel assistantGroup;
        private Border assistantButton;
        private readonly List<TaskApp> installedAssistants = new List<TaskApp>();
        private string assistantName = String.Empty;
        private string assistantPosition = "Após aplicativos";
        private bool showAssistant = true;
        private Border assistantSepLeft;
        private Border assistantSepRight;
        private StackPanel assistantTile;
        private DesktopWidgetWindow desktopWidget;
        private bool showDesktopWidget = true;
        private double widgetX = double.NaN;
        private double widgetY = double.NaN;
        private bool widgetLocked = false;
        private bool widgetSeconds = true;
        private bool widget24Hour = false;
        private string widgetLanguage = "Inglês";
        private string widgetTheme = "Ciano da Referência";
        private double widgetScale = 1.0;
        private bool showMascot = true;
        private string mascotType = "CatBlack";
        private string mascotMode = "PatrolRight";
        private double mascotScale = 1.0;
        private DockMascot dockMascot;
        private MenuItem mascotMenuItem;
        private bool? recycleBinFull;
        private StackPanel taskItems;
        private Border dockShell;
        private string taskSignature = String.Empty;
        private static readonly List<string> cachedPinnedNames = new List<string>();
        private static DateTime pinnedNamesCacheExpiresUtc = DateTime.MinValue;
        private static readonly List<string> cachedTaskbarButtonNames = new List<string>();
        private static DateTime taskbarButtonNamesCacheExpiresUtc = DateTime.MinValue;
        private static readonly List<TaskApp> cachedPackagedApps = new List<TaskApp>();
        private static readonly object packagedAppsCacheLock = new object();
        private static DateTime packagedAppsCacheExpiresUtc = DateTime.MinValue;
        private bool taskbarHidden;
        private bool darkMode = true;
        private bool? adaptiveLightSurface;
        private static readonly Effect darkTextHalo = CreateTextHalo(Colors.Black);
        private static readonly Effect lightTextHalo = CreateTextHalo(Colors.White);
        private byte dockOpacity = 168;
        private string dockTheme = "Meia-noite";
        private string iconTileStyle = "Branco";
        private double dockIconSize = 48;
        private double dockBottomGap = 4;
        private double runningIndicatorWidth = 9;
        private TaskApp pendingPreview;
        private FrameworkElement pendingPreviewAnchor;
        private FrameworkElement activePreviewAnchor;
        private PreviewWindow previewWindow;
        private string activePreviewKey;
        private DispatcherTimer taskRefreshSoonTimer;
        // Último momento em que cada janela ficou em primeiro plano. Permite
        // restaurar a janela mais recente de um grupo, como a barra do Windows.
        private readonly Dictionary<IntPtr, int> windowActivationTicks = new Dictionary<IntPtr, int>();
        private int previewLastLeft = Int32.MinValue;
        private int previewLastTop = Int32.MinValue;
        private int previewSettleFrames;
        private FrameworkElement magnifyHost;
        private bool magnificationRendering;
        private bool waveLayoutDirty = true;
        private TimeSpan lastWaveFrame;
        private TimeSpan lastTooltipFrame;
        private FrameworkElement waveTopContainer;
        private bool tooltipPositionNudge;
        private int taskRefreshRunning;
        private int systemRefreshRunning;
        private volatile bool isClosing;
        private DateTime nextTaskRefreshUtc = DateTime.MinValue;
        private int lastForegroundTaskRefreshTick;
        private bool positionDockPending;
        private bool autoHideEnabled = true;
        private bool keepWindowsOffDock = true;
        private bool dockAutoHidden;
        private bool macCursorEnabled;
        private string cursorTheme = "macOS";
        private string macCursorSize = "Regular";
        private bool edgeRevealPending;
        private bool fullscreenSuppressed;
        private bool startMenuActive;
        private bool startMenuReservationActive;
        private ContextMenu dockMenu;
        private ContextMenu activeContextMenu;

        private bool IsAnyContextMenuOpen()
        {
            return (dockMenu != null && dockMenu.IsOpen) || (activeContextMenu != null && activeContextMenu.IsOpen);
        }
        private MenuItem keepWindowsOffItem;
        private IntPtr dockHandle;
        private HwndSource dockSource;
        private uint shellHookMessage;
        private DispatcherTimer fastTaskRefreshTimer;
        private bool appBarRegistered;
        private uint appBarCallbackMessage;
        private Rect appBarArea;
        private int shellRecoveryAttempts;
        private WinEventDelegate trayOverflowEventDelegate;
        private IntPtr trayOverflowEventHook;
        private int trayOverflowHookGeneration;
        private int trayPopupAnchorX;
        private int trayPopupBarTop;
        private int trayPopupWorkLeft;
        private int trayPopupWorkRight;
        private int trayPopupWorkTop;
        private WinEventDelegate quickSettingsEventDelegate;
        private IntPtr quickSettingsEventHook;
        private int quickSettingsHookGeneration;
        private int quickSettingsAnchorX;
        private int quickSettingsBarTop;
        private int quickSettingsWorkLeft;
        private int quickSettingsWorkRight;
        private int quickSettingsWorkTop;
        private bool quickSettingsActive;
        private DispatcherTimer quickSettingsMonitorTimer;
        private WinEventDelegate startMenuEventDelegate;
        private IntPtr startMenuEventHook;
        private int startMenuHookGeneration;
        private int startMenuBarTop;
        private int startMenuWorkTop;
        private WinEventDelegate taskbarVisibilityEventDelegate;
        private IntPtr taskbarVisibilityEventHook;
        private readonly Dictionary<IntPtr, int> taskbarOriginalExtendedStyles = new Dictionary<IntPtr, int>();
        private readonly List<string> appOrder = new List<string>();
        private WorkspaceManager workspaceManager;
        internal WorkspaceManager CurrentWorkspaceManager { get { return workspaceManager; } }
        private DockSettingsWindow settingsWindow;
        private bool startWithWindows;
        private Point taskDragStart;
        private bool taskDragInProgress;
        private int taskClickCount;
        private IntPtr foregroundBeforeDockClick;
        private IntPtr lastExternalForegroundWindow;
        private WinEventDelegate foregroundEventDelegate;
        private IntPtr foregroundEventHook;

        private struct WaveSample
        {
            public FrameworkElement Icon;
            public FrameworkElement Container;
            public double Center;
            public double Scale;
            public double Growth;
            public double ExtraWidth;
            public double Influence;
            public double Offset;
            public int ZIndex;
        }

        private sealed class SystemSnapshot
        {
            public HashSet<string> ProcessNames;
            public string BatteryGlyph;
            public bool CriticalBattery;
            public bool LowBattery;
            public bool Charging;
            public string VolumeGlyph;
            public string NetworkGlyph;
            public string BatteryLabel;
            public string NetworkLabel;
            public string VolumeLabel;
            public bool? RecycleFull;
            public ImageSource RecycleSource;
        }

        private sealed class BatteryVisualState
        {
            public string Glyph;
            public string Label;
            public bool Critical;
            public bool Low;
            public bool Charging;
        }

        private sealed class NetworkVisualState
        {
            public string Glyph;
            public string Label;
        }

        private static int ReadCurrentProcessId()
        {
            using (Process process = Process.GetCurrentProcess()) return process.Id;
        }

        public DockWindow()
        {
            LoadSettings();
            workspaceManager = new WorkspaceManager();
            workspaceManager.Load(this);
            MacCursorService.IsEnabled = macCursorEnabled;
            MacCursorService.CurrentTheme = cursorTheme;
            MacCursorService.CurrentSize = macCursorSize;
            if (macCursorEnabled)
            {
                try { MacCursorService.Apply(cursorTheme, macCursorSize); } catch { }
            }
            startWithWindows = IsStartupEnabled();
            Title = "B.M Flow";
            Height = 118;
            MinWidth = 0;
            MaxWidth = Math.Max(500, SystemParameters.PrimaryScreenWidth - 20);
            SizeToContent = SizeToContent.Width;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            SnapsToDevicePixels = true;

            Content = BuildDock();
            SourceInitialized += delegate { InitializeNativeDock(); };
            Loaded += delegate
            {
                if (taskbarHidden)
                {
                    UpdateTaskbarVisibilityGuard();
                    ApplyCompleteTaskbarHide();
                }
                else SetTaskbarVisible(true);
                ApplyDockSizing();
                SchedulePositionDock();
                RefreshState();
                DiscoverAssistantsAsync();
                UpdateDesktopWidgetVisibility();
                if (dockMascot != null) dockMascot.Start();
            };
            SizeChanged += delegate
            {
                SchedulePositionDock();
                if (dockMascot != null) dockMascot.NotifyLayoutUpdated();
            };
            Closing += delegate
            {
                isClosing = true;
                if (dockMascot != null)
                {
                    try { dockMascot.Stop(); } catch { }
                    dockMascot = null;
                }
                if (desktopWidget != null)
                {
                    try { desktopWidget.Close(); } catch { }
                    desktopWidget = null;
                }
                UnregisterWorkspaceHotKeys();
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                edgeRevealTimer.Stop();
                pinnedRefreshDebounceTimer.Stop();
                adaptiveContrastTimer.Dispose();
                volumeMonitorTimer.Dispose();
                batteryMonitorTimer.Dispose();
                networkMonitorTimer.Dispose();
                pinnedStateMonitorTimer.Dispose();
                previewSettleTimer.Stop();
                ReleaseStartMenuHook();
                ReleaseTaskbarVisibilityGuard();
                ReleaseTrayOverflowHook();
                ReleaseQuickSettingsHook();
                if (quickSettingsMonitorTimer != null) quickSettingsMonitorTimer.Stop();
                ReleaseForegroundTracking();
                if (dockHandle != IntPtr.Zero) DeregisterShellHookWindow(dockHandle);
                RemoveAppBar();
                SaveSettings();
                RestoreOriginalTaskbarState();
            };
            SystemParameters.StaticPropertyChanged += delegate { PositionDock(); };

            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { RefreshState(); };
            timer.Start();

            // O restante do estado pode ser atualizado lentamente, mas o
            // contraste precisa acompanhar trocas de página e wallpaper.
            adaptiveContrastTimer = new System.Threading.Timer(SampleAdaptiveContrastBackground, null, 120, 160);
            volumeMonitorTimer = new System.Threading.Timer(MonitorVolumeBackground, null, 180, 220);
            batteryMonitorTimer = new System.Threading.Timer(MonitorBatteryBackground, null, 240, 750);
            networkMonitorTimer = new System.Threading.Timer(MonitorNetworkBackground, null, 320, 900);
            pinnedStateMonitorTimer = new System.Threading.Timer(MonitorPinnedStateBackground, null, 500, 520);

            previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
            previewTimer.Tick += delegate
            {
                previewTimer.Stop();
                ShowPreview();
            };
            previewHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            previewHideTimer.Tick += delegate
            {
                previewHideTimer.Stop();
                // Durante a navegação por teclado a prévia continua aberta
                // mesmo com o cursor fora; Esc, Enter ou trocar de app a fecham.
                if (previewWindow != null && (previewWindow.KeyboardActive || previewWindow.IsMouseOver)) return;
                HidePreview();
            };
            previewSettleTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            previewSettleTimer.Tick += delegate
            {
                RepositionOpenPreview();
                if (--previewSettleFrames <= 0) previewSettleTimer.Stop();
            };

            autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            autoHideTimer.Tick += delegate
            {
                autoHideTimer.Stop();
                if (autoHideEnabled && !fullscreenSuppressed && !startMenuActive && !IsMouseOver &&
                    !IsAnyContextMenuOpen() && previewWindow == null && settingsWindow == null &&
                    !taskDragInProgress && !quickSettingsActive) HideDockAnimated();
            };

            fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            fullscreenTimer.Tick += delegate { RefreshFullscreenState(); };
            fullscreenTimer.Start();

            // Não depende do hit-test da faixa WPF transparente: a posição
            // global do cursor revela a Dock na borda física da tela.
            edgeRevealTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(70)
            };
            edgeRevealTimer.Tick += delegate { PollDockRevealEdge(); };

            shellRecoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            shellRecoveryTimer.Tick += delegate
            {
                shellRecoveryAttempts++;
                RestoreShellStateAfterResume(false);
                if (shellRecoveryAttempts >= 7) shellRecoveryTimer.Stop();
            };
            pinnedRefreshDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(280)
            };
            pinnedRefreshDebounceTimer.Tick += delegate
            {
                pinnedRefreshDebounceTimer.Stop();
                if (taskRefreshRunning != 0)
                {
                    pinnedRefreshDebounceTimer.Start();
                    return;
                }
                pinnedNamesCacheExpiresUtc = DateTime.MinValue;
                taskbarButtonNamesCacheExpiresUtc = DateTime.MinValue;
                nextTaskRefreshUtc = DateTime.MinValue;
                RefreshTaskItems();
            };
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private UIElement BuildDock()
        {
            var root = new Grid { ClipToBounds = false, Background = Brushes.Transparent };
            root.MouseEnter += delegate
            {
                edgeRevealPending = false;
                edgeRevealTimer.Stop();
                autoHideTimer.Stop();
                if (!fullscreenSuppressed) ShowDockAnimated();
            };
            root.MouseLeave += delegate { ScheduleAutoHide(); };
            dockShell = new Border
            {
                Height = 72,
                Margin = new Thickness(5, 0, 5, 5),
                VerticalAlignment = VerticalAlignment.Bottom,
                ClipToBounds = false,
                CornerRadius = new CornerRadius(21),
                Background = new SolidColorBrush(Color.FromArgb(168, 31, 31, 35)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(70, 61, 47),
                    BlurRadius = 22,
                    ShadowDepth = 3,
                    Opacity = .25
                },
                // O fundo e sua sombra não mudam durante a onda dos ícones.
                // Mantê-los rasterizados evita recompor o blur sem necessidade.
                CacheMode = new BitmapCache { RenderAtScale = 1 }
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                // Reserva lateral para o primeiro e o último ícone ampliarem sem
                // encostar no limite da janela transparente.
                Margin = new Thickness(30, 0, 30, 5),
                ClipToBounds = false,
                Background = Brushes.Transparent
            };
            dockRow = row;
            magnifyHost = row;
            row.SizeChanged += delegate { waveLayoutDirty = true; };
            row.MouseEnter += delegate { StartDockWave(); };
            row.MouseLeave += delegate { StopDockWave(); };

            var menu = new ContextMenu();
            ConfigureDockContextMenu(menu, dockShell);
            dockMenu = menu;
            menu.Opened += delegate { SyncDockMenu(); };
            var settingsItem = new MenuItem { Header = "Configurações…" };
            settingsItem.Click += delegate { OpenSettingsWindow(); };
            menu.Items.Add(settingsItem);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var wsMenu = new MenuItem { Header = "Espaços de Trabalho" };
            menu.Items.Add(wsMenu);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var themeMenu = new MenuItem { Header = "Tema" };
            themeMenu.Items.Add(ThemeItem("Nativo", dockTheme == "Nativo"));
            themeMenu.Items.Add(ThemeItem("Liquid Glass", dockTheme == "Liquid Glass"));
            themeMenu.Items.Add(ThemeItem("Meia-noite", dockTheme == "Meia-noite"));
            themeMenu.Items.Add(ThemeItem("Rocket", dockTheme == "Rocket"));
            themeMenu.Items.Add(ThemeItem("Prateleira Aqua", dockTheme == "Prateleira Aqua"));
            themeMenu.Items.Add(ThemeItem("Neon", dockTheme == "Neon"));
            themeMenu.Items.Add(ThemeItem("Canarinho", dockTheme == "Canarinho"));
            themeMenu.Items.Add(ThemeItem("Papel", dockTheme == "Papel"));
            themeMenu.Items.Add(ThemeItem("Flutuante", dockTheme == "Flutuante"));
            menu.Items.Add(themeMenu);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var sizeMenu = new MenuItem { Header = "Tamanho e posição" };
            sizeMenu.Items.Add(SliderMenuItem("Tamanho dos ícones", "px", 40, 64, dockIconSize, delegate(double value)
            {
                dockIconSize = value;
                ApplyDockSizing();
                SaveSettings();
            }));
            sizeMenu.Items.Add(SliderMenuItem("Largura do indicador", "px", 4, 18, runningIndicatorWidth, delegate(double value)
            {
                SetRunningIndicatorWidth(value);
            }));
            sizeMenu.Items.Add(SliderMenuItem("Espaço inferior", "px", 0, 20, dockBottomGap, delegate(double value)
            {
                dockBottomGap = value;
                UpdateAppBarRegistration();
                SchedulePositionDock();
                SaveSettings();
            }));
            menu.Items.Add(sizeMenu);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var transparencyMenu = new MenuItem { Header = "Transparência" };
            transparencyMenu.Items.Add(OpacityItem("Mais transparente (45%)", 115, dockOpacity == 115));
            transparencyMenu.Items.Add(OpacityItem("Translúcido (65%)", 168, dockOpacity == 168));
            transparencyMenu.Items.Add(OpacityItem("Mais sólido (85%)", 217, dockOpacity == 217));
            menu.Items.Add(transparencyMenu);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var autoHideItem = new MenuItem { Header = "Ocultar automaticamente", IsCheckable = true, IsChecked = autoHideEnabled };
            autoHideItem.Click += delegate
            {
                autoHideEnabled = autoHideItem.IsChecked;
                if (keepWindowsOffItem != null) keepWindowsOffItem.IsEnabled = !autoHideEnabled;
                UpdateAppBarRegistration();
                SchedulePositionDock();
                if (autoHideEnabled) ScheduleAutoHide(); else ShowDockAnimated();
                SaveSettings();
            };
            menu.Items.Add(autoHideItem);
            keepWindowsOffItem = new MenuItem { Header = "Manter janelas acima da Dock", IsCheckable = true, IsChecked = keepWindowsOffDock, IsEnabled = !autoHideEnabled };
            keepWindowsOffItem.Click += delegate
            {
                keepWindowsOffDock = keepWindowsOffItem.IsChecked;
                UpdateAppBarRegistration();
                SchedulePositionDock();
                SaveSettings();
            };
            menu.Items.Add(keepWindowsOffItem);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var taskbarItem = new MenuItem { Header = "Ocultar barra de tarefas original", IsCheckable = true, IsChecked = taskbarHidden };
            taskbarItem.Click += delegate
            {
                taskbarHidden = taskbarItem.IsChecked;
                UpdateTaskbarVisibilityGuard();
                if (taskbarHidden) ApplyCompleteTaskbarHide();
                else RestoreOriginalTaskbarState();
                UpdateAppBarRegistration();
                PositionDock();
                SaveSettings();
            };
            menu.Items.Add(taskbarItem);
            var assistantMenuItem = new MenuItem { Header = "Exibir assistente", IsCheckable = true, IsChecked = showAssistant };
            assistantMenuItem.Click += delegate
            {
                SetShowAssistant(assistantMenuItem.IsChecked);
            };
            menu.Items.Add(assistantMenuItem);
            var widgetMenuItem = new MenuItem { Header = "Exibir widget na área de trabalho", IsCheckable = true, IsChecked = showDesktopWidget };
            widgetMenuItem.Click += delegate
            {
                SetShowDesktopWidget(widgetMenuItem.IsChecked);
            };
            menu.Items.Add(widgetMenuItem);
            mascotMenuItem = new MenuItem { Header = "Exibir mascote na barra", IsCheckable = true, IsChecked = showMascot };
            mascotMenuItem.Click += delegate
            {
                SetShowMascot(mascotMenuItem.IsChecked);
            };
            menu.Items.Add(mascotMenuItem);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var closeItem = new MenuItem { Header = "Fechar B.M Flow" };
            closeItem.Click += delegate { Close(); };
            menu.Items.Add(closeItem);
            root.ContextMenu = menu;

            row.Children.Add(ImageTile(LoadEmbeddedImage("windows11.png"), "Iniciar", OpenStart));
            taskItems = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = false };
            row.Children.Add(taskItems);

            assistantGroup = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = false };
            assistantSepLeft = Separator();
            assistantTile = ImageTile(null, "Assistente", OpenSelectedAssistant) as StackPanel;
            if (assistantTile != null && assistantTile.Children.Count > 0) assistantButton = assistantTile.Children[0] as Border;
            assistantSepRight = Separator();
            assistantGroup.Children.Add(assistantSepLeft);
            assistantGroup.Children.Add(assistantTile);
            assistantGroup.Children.Add(assistantSepRight);
            row.Children.Add(assistantGroup);
            string downloadsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            row.Children.Add(ImageTile(IconForShellPath(downloadsPath), "Downloads", delegate { Launch("explorer.exe", downloadsPath); }));
            row.Children.Add(RecycleBinTile());
            row.Children.Add(Separator());
            row.Children.Add(SystemCard());
            PositionAssistantGroup();

            root.Children.Add(dockShell);
            root.Children.Add(row);
            var mascotCanvas = new Canvas
            {
                ClipToBounds = false,
                IsHitTestVisible = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            root.Children.Add(mascotCanvas);
            dockMascot = new DockMascot(this, mascotCanvas, dockShell, showMascot, mascotType, mascotMode, mascotScale);
            ApplyTheme();
            return root;
        }

        private void DiscoverAssistantsAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                var found = new List<TaskApp>();
                try
                {
                    foreach (TaskApp app in ReadPackagedApps())
                    {
                        if (!IsAssistantName(app.Name)) continue;
                        app.Icon = IconForShellItem(app.Identity);
                        if (!found.Any(item => String.Equals(item.Name, app.Name, StringComparison.OrdinalIgnoreCase))) found.Add(app);
                    }
                    foreach (string root in new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                    })
                    {
                        if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                        string[] links;
                        try { links = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories); }
                        catch { continue; }
                        foreach (string link in links)
                        {
                            string name = System.IO.Path.GetFileNameWithoutExtension(link);
                            if (!IsAssistantName(name)) continue;
                            TaskApp app = ResolveShortcut(link);
                            if (app != null && !found.Any(item => String.Equals(item.Name, app.Name, StringComparison.OrdinalIgnoreCase))) found.Add(app);
                        }
                    }
                }
                catch { }
                found = found.OrderBy(AssistantPriority).ThenBy(item => item.Name).ToList();
                if (isClosing) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { ApplyDiscoveredAssistants(found); }));
            });
        }

        private static bool IsAssistantName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            string value = name.ToLowerInvariant();
            if (value.Contains("setup") || value.Contains("installer") || value.Contains("uninstall")) return false;
            return value.Contains("chatgpt") || value.Contains("claude") ||
                   value.Contains("copilot") || value.Contains("gemini");
        }

        private static int AssistantPriority(TaskApp app)
        {
            string value = (app == null ? String.Empty : app.Name ?? String.Empty).ToLowerInvariant();
            if (value.Contains("chatgpt")) return 0;
            if (value.Contains("claude")) return 1;
            if (value.Contains("copilot")) return 2;
            if (value.Contains("gemini")) return 3;
            return 9;
        }

        private void ApplyDiscoveredAssistants(List<TaskApp> found)
        {
            installedAssistants.Clear();
            installedAssistants.AddRange(found);
            TaskApp selected = installedAssistants.FirstOrDefault(item => String.Equals(item.Name, assistantName, StringComparison.OrdinalIgnoreCase));
            if (selected == null) selected = installedAssistants.FirstOrDefault();
            assistantName = selected == null ? String.Empty : selected.Name;
            UpdateAssistantButton();
            SaveSettings();
        }

        private void UpdateAssistantButton()
        {
            if (assistantButton == null) return;
            TaskApp selected = installedAssistants.FirstOrDefault(item => String.Equals(item.Name, assistantName, StringComparison.OrdinalIgnoreCase));
            assistantButton.Tag = selected != null && IsMostlyLightIcon(selected.Icon) ? "LightIcon" : null;
            assistantButton.Child = selected != null && selected.Icon != null
                ? (UIElement)new Image { Source = selected.Icon, Width = 35, Height = 35, Stretch = Stretch.Uniform }
                : new TextBlock { Text = "\uE768", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 25, Foreground = new SolidColorBrush(Color.FromRgb(36, 91, 171)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ApplyTileTheme(assistantButton);
            SetToolTipText(assistantButton, selected == null ? "Assistente" : selected.Name);
            var menu = new ContextMenu();
            ConfigureDockContextMenu(menu, assistantButton);
            foreach (TaskApp app in installedAssistants)
            {
                TaskApp choice = app;
                var item = new MenuItem { Header = choice.Name, IsCheckable = true, IsChecked = choice.Name == assistantName };
                item.Click += delegate { SetAssistantName(choice.Name); };
                menu.Items.Add(item);
            }
            if (menu.Items.Count > 0) menu.Items.Add(new System.Windows.Controls.Separator());
            var hideItem = new MenuItem { Header = "Ocultar assistente da Dock" };
            hideItem.Click += delegate { SetShowAssistant(false); };
            menu.Items.Add(hideItem);
            assistantButton.ContextMenu = menu;
        }

        private void OpenSelectedAssistant()
        {
            TaskApp selected = installedAssistants.FirstOrDefault(item => String.Equals(item.Name, assistantName, StringComparison.OrdinalIgnoreCase));
            if (selected != null) Launch(selected.Target, selected.Arguments);
        }

        private void PositionAssistantGroup()
        {
            if (dockRow == null || assistantGroup == null || taskItems == null) return;
            if (dockRow.Children.Contains(assistantGroup)) dockRow.Children.Remove(assistantGroup);

            if (!showAssistant)
            {
                if (assistantTile != null) assistantTile.Visibility = Visibility.Collapsed;
                if (assistantSepRight != null) assistantSepRight.Visibility = Visibility.Collapsed;
                if (assistantSepLeft != null) assistantSepLeft.Visibility = Visibility.Visible;

                int index = Math.Min(dockRow.Children.Count, dockRow.Children.IndexOf(taskItems) + 1);
                dockRow.Children.Insert(index, assistantGroup);
                waveLayoutDirty = true;
                SchedulePositionDock();
                return;
            }

            if (assistantTile != null) assistantTile.Visibility = Visibility.Visible;
            if (assistantSepRight != null) assistantSepRight.Visibility = Visibility.Visible;
            if (assistantSepLeft != null) assistantSepLeft.Visibility = Visibility.Visible;

            int targetIndex;
            if (assistantPosition == "Após Iniciar") targetIndex = Math.Min(1, dockRow.Children.Count);
            else if (assistantPosition == "Antes da bandeja") targetIndex = Math.Max(0, dockRow.Children.Count - 2);
            else targetIndex = Math.Min(dockRow.Children.Count, dockRow.Children.IndexOf(taskItems) + 1);

            dockRow.Children.Insert(targetIndex, assistantGroup);
            waveLayoutDirty = true;
            SchedulePositionDock();
        }

        private FrameworkElement ImageTile(ImageSource source, string tip, Action action)
        {
            var stack = new StackPanel { Width = 64, Height = 61, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
            var button = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(65, 120, 112, 101)),
                BorderThickness = new Thickness(.5),
                RenderTransformOrigin = new Point(.5, 1),
                RenderTransform = new ScaleTransform(1, 1),
                ToolTip = null,
                Cursor = Cursors.Hand,
                ClipToBounds = false,
                Tag = IsMostlyLightIcon(source) ? "LightIcon" : null,
                Child = source == null
                    ? (UIElement)new TextBlock { Text = "\uE80F", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 25, Foreground = new SolidColorBrush(Color.FromRgb(36, 91, 171)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : new Image { Source = source, Width = 35, Height = 35, Stretch = Stretch.Uniform }
            };
            button.MouseLeftButtonUp += delegate { DismissToolTip(button); action(); };
            button.ToolTip = CreateNameToolTip(tip, button);
            themedTiles.Add(button);
            ApplyTileTheme(button);
            AddMagnify(button);
            stack.Children.Add(button);
            return stack;
        }

        private FrameworkElement RecycleBinTile()
        {
            var tile = ImageTile(LoadRecycleBinIcon(false), "Lixeira", OpenRecycleBin) as StackPanel;
            if (tile != null && tile.Children.Count > 0)
            {
                var border = tile.Children[0] as Border;
                recycleBinImage = border == null ? null : border.Child as Image;
            }
            recycleBinFull = null;
            return tile;
        }

        private static ImageSource LoadRecycleBinIcon(bool full)
        {
            var info = new StockIconInfo { Size = Marshal.SizeOf(typeof(StockIconInfo)), Path = String.Empty };
            try
            {
                // SIID_RECYCLER (31) e SIID_RECYCLERFULL (32) são os ícones
                // oficiais fornecidos pelo Shell da versão atual do Windows.
                if (SHGetStockIconInfo(full ? 32u : 31u, 0x00000100, ref info) != 0 || info.Icon == IntPtr.Zero) return null;
                var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
                source.Freeze();
                return source;
            }
            catch { return null; }
            finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
        }

        private void RefreshRecycleBinIcon()
        {
            if (recycleBinImage == null) return;
            var info = new RecycleBinInfo { Size = Marshal.SizeOf(typeof(RecycleBinInfo)) };
            if (SHQueryRecycleBin(null, ref info) != 0) return;
            bool full = info.ItemCount > 0;
            if (recycleBinFull.HasValue && recycleBinFull.Value == full) return;
            ImageSource source = LoadRecycleBinIcon(full);
            if (source != null) recycleBinImage.Source = source;
            recycleBinFull = full;
        }

        private static ImageSource LoadEmbeddedImage(string name)
        {
            Stream stream = null;
            try
            {
                stream = typeof(DockWindow).Assembly.GetManifestResourceStream(name);
                if (stream == null) return null;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
            finally { if (stream != null) stream.Dispose(); }
        }

        private MenuItem OpacityItem(string label, byte alpha, bool selected = false)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = selected, Tag = alpha };
            item.Click += delegate
            {
                foreach (var sibling in ((MenuItem)item.Parent).Items.OfType<MenuItem>()) sibling.IsChecked = sibling == item;
                dockOpacity = alpha;
                ApplyTheme();
                SaveSettings();
            };
            return item;
        }

        private MenuItem ThemeItem(string name, bool selected = false)
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = selected, Tag = name };
            item.Click += delegate
            {
                var parent = item.Parent as MenuItem;
                if (parent != null)
                    foreach (var sibling in parent.Items.OfType<MenuItem>()) sibling.IsChecked = sibling == item;
                dockTheme = name;
                darkMode = name != "Nativo" && name != "Papel";
                ApplyTheme();
                SaveSettings();
            };
            return item;
        }

        private static string SettingsPath
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BMFlow", "settings.ini");
            }
        }

        private static string LegacySettingsPath
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DockBar", "settings.ini");
            }
        }

        private void LoadSettings()
        {
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path) && File.Exists(LegacySettingsPath))
                {
                    string migrationDirectory = System.IO.Path.GetDirectoryName(path);
                    if (!Directory.Exists(migrationDirectory)) Directory.CreateDirectory(migrationDirectory);
                    File.Copy(LegacySettingsPath, path, false);
                }
                if (!File.Exists(path)) return;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    if (String.IsNullOrWhiteSpace(rawLine) || rawLine.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                    int separator = rawLine.IndexOf('=');
                    if (separator <= 0) continue;
                    values[rawLine.Substring(0, separator).Trim()] = rawLine.Substring(separator + 1).Trim();
                }

                string text;
                bool flag;
                double number;
                int integer;
                if (values.TryGetValue("Theme", out text) && !String.IsNullOrWhiteSpace(text)) dockTheme = text;
                if (values.TryGetValue("IconTileStyle", out text) && !String.IsNullOrWhiteSpace(text)) iconTileStyle = text;
                darkMode = dockTheme != "Nativo" && dockTheme != "Papel";
                if (values.TryGetValue("Opacity", out text) && Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                    dockOpacity = (byte)Math.Max(80, Math.Min(255, integer));
                if (values.TryGetValue("IconSize", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    dockIconSize = Math.Max(40, Math.Min(64, number));
                if (values.TryGetValue("BottomGap", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    dockBottomGap = Math.Max(0, Math.Min(20, number));
                if (values.TryGetValue("IndicatorWidth", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    runningIndicatorWidth = Math.Max(4, Math.Min(18, number));
                if (values.TryGetValue("AutoHide", out text) && Boolean.TryParse(text, out flag)) autoHideEnabled = flag;
                if (values.TryGetValue("KeepWindowsOffDock", out text) && Boolean.TryParse(text, out flag)) keepWindowsOffDock = flag;
                if (values.TryGetValue("HideTaskbar", out text) && Boolean.TryParse(text, out flag)) taskbarHidden = flag;
                if (values.TryGetValue("MacCursorEnabled", out text) && Boolean.TryParse(text, out flag)) macCursorEnabled = flag;
                if (values.TryGetValue("CursorTheme", out text) && !String.IsNullOrWhiteSpace(text)) cursorTheme = text;
                if (values.TryGetValue("MacCursorSize", out text) && !String.IsNullOrWhiteSpace(text)) macCursorSize = text;
                if (values.TryGetValue("Assistant", out text)) assistantName = text;
                if (values.TryGetValue("AssistantPosition", out text) &&
                    new[] { "Após Iniciar", "Após aplicativos", "Antes da bandeja" }.Contains(text)) assistantPosition = text;
                if (values.TryGetValue("ShowAssistant", out text) && Boolean.TryParse(text, out flag)) showAssistant = flag;
                if (values.TryGetValue("WidgetEnabled", out text) && Boolean.TryParse(text, out flag)) showDesktopWidget = flag;
                if (values.TryGetValue("WidgetX", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) widgetX = number;
                if (values.TryGetValue("WidgetY", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) widgetY = number;
                if (values.TryGetValue("WidgetLocked", out text) && Boolean.TryParse(text, out flag)) widgetLocked = flag;
                if (values.TryGetValue("WidgetSeconds", out text) && Boolean.TryParse(text, out flag)) widgetSeconds = flag;
                if (values.TryGetValue("Widget24Hour", out text) && Boolean.TryParse(text, out flag)) widget24Hour = flag;
                if (values.TryGetValue("WidgetLanguage", out text) && !String.IsNullOrWhiteSpace(text)) widgetLanguage = text;
                if (values.TryGetValue("WidgetTheme", out text) && !String.IsNullOrWhiteSpace(text)) widgetTheme = text;
                if (values.TryGetValue("WidgetScale", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) widgetScale = Math.Max(0.5, Math.Min(2.5, number));
                if (values.TryGetValue("ShowMascot", out text) && Boolean.TryParse(text, out flag)) showMascot = flag;
                if (values.TryGetValue("MascotType", out text) && !String.IsNullOrWhiteSpace(text)) mascotType = text;
                if (values.TryGetValue("MascotMode", out text) && !String.IsNullOrWhiteSpace(text)) mascotMode = text;
                if (values.TryGetValue("MascotScale", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) mascotScale = Math.Max(0.6, Math.Min(3.0, number));
                if (values.TryGetValue("AppOrder", out text) && !String.IsNullOrWhiteSpace(text))
                {
                    appOrder.Clear();
                    foreach (string encoded in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        try { appOrder.Add(Uri.UnescapeDataString(encoded)); } catch { }
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                string path = SettingsPath;
                string directory = System.IO.Path.GetDirectoryName(path);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string temporary = path + ".tmp";
                File.WriteAllLines(temporary, new[]
                {
                    "# B.M Flow settings",
                    "Version=1",
                    "Theme=" + dockTheme,
                    "IconTileStyle=" + iconTileStyle,
                    "Opacity=" + dockOpacity.ToString(CultureInfo.InvariantCulture),
                    "IconSize=" + dockIconSize.ToString("0", CultureInfo.InvariantCulture),
                    "BottomGap=" + dockBottomGap.ToString("0", CultureInfo.InvariantCulture),
                    "IndicatorWidth=" + runningIndicatorWidth.ToString("0", CultureInfo.InvariantCulture),
                    "AutoHide=" + autoHideEnabled.ToString(),
                    "KeepWindowsOffDock=" + keepWindowsOffDock.ToString(),
                    "HideTaskbar=" + taskbarHidden.ToString(),
                    "MacCursorEnabled=" + macCursorEnabled.ToString(),
                    "CursorTheme=" + cursorTheme,
                    "MacCursorSize=" + macCursorSize,
                    "Assistant=" + assistantName,
                    "AssistantPosition=" + assistantPosition,
                    "ShowAssistant=" + showAssistant.ToString(),
                    "WidgetEnabled=" + showDesktopWidget.ToString(),
                    "WidgetX=" + (desktopWidget != null ? desktopWidget.Left.ToString("0", CultureInfo.InvariantCulture) : widgetX.ToString("0", CultureInfo.InvariantCulture)),
                    "WidgetY=" + (desktopWidget != null ? desktopWidget.Top.ToString("0", CultureInfo.InvariantCulture) : widgetY.ToString("0", CultureInfo.InvariantCulture)),
                    "WidgetLocked=" + (desktopWidget != null ? desktopWidget.IsLocked.ToString() : widgetLocked.ToString()),
                    "WidgetSeconds=" + (desktopWidget != null ? desktopWidget.ShowSeconds.ToString() : widgetSeconds.ToString()),
                    "Widget24Hour=" + (desktopWidget != null ? desktopWidget.Use24Hour.ToString() : widget24Hour.ToString()),
                    "WidgetLanguage=" + (desktopWidget != null ? desktopWidget.CurrentLanguage : widgetLanguage),
                    "WidgetTheme=" + (desktopWidget != null ? desktopWidget.CurrentTheme : widgetTheme),
                    "WidgetScale=" + (desktopWidget != null ? desktopWidget.CurrentScale.ToString("0.00", CultureInfo.InvariantCulture) : widgetScale.ToString("0.00", CultureInfo.InvariantCulture)),
                    "ShowMascot=" + showMascot.ToString(),
                    "MascotType=" + mascotType,
                    "MascotMode=" + mascotMode,
                    "MascotScale=" + mascotScale.ToString("0.00", CultureInfo.InvariantCulture),
                    "AppOrder=" + EncodeAppOrder()
                });
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch { }
        }

        private string EncodeAppOrder()
        {
            lock (appOrder) return String.Join(";", appOrder.Select(Uri.EscapeDataString));
        }

        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupRegistryName = "B.M Flow";
        private const string LegacyStartupRegistryName = "DockBar";

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
                {
                    string value = key.GetValue(StartupRegistryName) as string;
                    string executable = Process.GetCurrentProcess().MainModule.FileName;
                    if (!String.IsNullOrWhiteSpace(value) &&
                        String.Equals(value.Trim().Trim('"'), executable, StringComparison.OrdinalIgnoreCase)) return true;
                    string legacyValue = key.GetValue(LegacyStartupRegistryName) as string;
                    if (!String.IsNullOrWhiteSpace(legacyValue))
                    {
                        key.SetValue(StartupRegistryName, "\"" + executable + "\"", RegistryValueKind.String);
                        key.DeleteValue(LegacyStartupRegistryName, false);
                        return true;
                    }
                    return false;
                }
            }
            catch { return false; }
        }

        internal bool SetStartWithWindows(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
                {
                    if (enabled)
                    {
                        string executable = Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(StartupRegistryName, "\"" + executable + "\"", RegistryValueKind.String);
                    }
                    else key.DeleteValue(StartupRegistryName, false);
                    key.DeleteValue(LegacyStartupRegistryName, false);
                }
                startWithWindows = enabled;
                return true;
            }
            catch { return false; }
        }

        internal string CurrentTheme { get { return dockTheme; } }
        internal string CurrentIconTileStyle { get { return iconTileStyle; } }
        internal byte CurrentOpacity { get { return dockOpacity; } }
        internal double CurrentIconSize { get { return dockIconSize; } }
        internal double CurrentBottomGap { get { return dockBottomGap; } }
        internal double CurrentIndicatorWidth { get { return runningIndicatorWidth; } }
        internal bool CurrentAutoHide { get { return autoHideEnabled; } }
        internal bool CurrentKeepWindowsOff { get { return keepWindowsOffDock; } }
        internal bool CurrentHideTaskbar { get { return taskbarHidden; } }
        internal bool CurrentStartWithWindows { get { return startWithWindows; } }
        internal bool CurrentMacCursorEnabled { get { return macCursorEnabled; } }
        internal string CurrentCursorTheme { get { return cursorTheme; } }
        internal string CurrentMacCursorSize { get { return macCursorSize; } }
        internal string CurrentAssistantName { get { return assistantName; } }
        internal string CurrentAssistantPosition { get { return assistantPosition; } }
        internal bool CurrentShowAssistant { get { return showAssistant; } }
        internal IEnumerable<string> AssistantChoices
        {
            get
            {
                var names = installedAssistants.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (names.Count == 0) names.Add("Nenhum assistente detectado");
                return names;
            }
        }

        internal void SetAssistantName(string name)
        {
            if (!installedAssistants.Any(item => String.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            assistantName = name;
            UpdateAssistantButton();
            SaveSettings();
        }

        internal void SetAssistantPosition(string position)
        {
            if (!new[] { "Após Iniciar", "Após aplicativos", "Antes da bandeja" }.Contains(position)) return;
            assistantPosition = position;
            PositionAssistantGroup();
            SaveSettings();
        }

        internal void SetShowAssistant(bool show)
        {
            if (showAssistant == show) return;
            showAssistant = show;
            PositionAssistantGroup();
            SaveSettings();
        }

        internal bool CurrentShowDesktopWidget { get { return showDesktopWidget; } }
        internal bool DesktopWidgetLocked { get { return desktopWidget != null ? desktopWidget.IsLocked : widgetLocked; } }
        internal bool DesktopWidgetSeconds { get { return desktopWidget != null ? desktopWidget.ShowSeconds : widgetSeconds; } }
        internal bool DesktopWidget24Hour { get { return desktopWidget != null ? desktopWidget.Use24Hour : widget24Hour; } }
        internal string DesktopWidgetLanguage { get { return desktopWidget != null ? desktopWidget.CurrentLanguage : widgetLanguage; } }
        internal string DesktopWidgetTheme { get { return desktopWidget != null ? desktopWidget.CurrentTheme : widgetTheme; } }
        internal double DesktopWidgetScale { get { return desktopWidget != null ? desktopWidget.CurrentScale : widgetScale; } }
        internal string DesktopWidgetScaleLabel
        {
            get
            {
                double s = DesktopWidgetScale;
                if (Math.Abs(s - 0.8) < 0.05) return "80% (Pequeno)";
                if (Math.Abs(s - 1.25) < 0.05) return "125% (Grande)";
                if (Math.Abs(s - 1.5) < 0.05) return "150% (Extra Grande)";
                return "100% (Normal)";
            }
        }

        internal void SetShowDesktopWidget(bool show)
        {
            showDesktopWidget = show;
            UpdateDesktopWidgetVisibility();
            SaveSettings();
        }

        internal void SetDesktopWidgetLocked(bool locked)
        {
            widgetLocked = locked;
            if (desktopWidget != null) desktopWidget.SetLocked(locked);
            else SaveSettings();
        }

        internal void SetDesktopWidgetSeconds(bool seconds)
        {
            widgetSeconds = seconds;
            if (desktopWidget != null) desktopWidget.SetShowSeconds(seconds);
            else SaveSettings();
        }

        internal void SetDesktopWidget24Hour(bool h24)
        {
            widget24Hour = h24;
            if (desktopWidget != null) desktopWidget.Set24Hour(h24);
            else SaveSettings();
        }

        internal void SetDesktopWidgetLanguage(string lang)
        {
            widgetLanguage = lang;
            if (desktopWidget != null) desktopWidget.SetLanguage(lang);
            else SaveSettings();
        }

        internal void SetDesktopWidgetTheme(string theme)
        {
            widgetTheme = theme;
            if (desktopWidget != null) desktopWidget.SetTheme(theme);
            else SaveSettings();
        }

        internal void SetDesktopWidgetScaleFromLabel(string label)
        {
            double scale = 1.0;
            if (label.StartsWith("80%")) scale = 0.8;
            else if (label.StartsWith("125%")) scale = 1.25;
            else if (label.StartsWith("150%")) scale = 1.5;
            widgetScale = scale;
            if (desktopWidget != null) desktopWidget.SetScale(scale);
            else SaveSettings();
        }

        internal void CenterDesktopWidget()
        {
            if (desktopWidget != null) desktopWidget.CenterOnScreen();
        }

        internal void SetWidgetPosition(double x, double y)
        {
            widgetX = x;
            widgetY = y;
            SaveSettings();
        }

        internal void NotifyWidgetChanged()
        {
            if (desktopWidget != null)
            {
                widgetLocked = desktopWidget.IsLocked;
                widgetSeconds = desktopWidget.ShowSeconds;
                widget24Hour = desktopWidget.Use24Hour;
                widgetLanguage = desktopWidget.CurrentLanguage;
                widgetTheme = desktopWidget.CurrentTheme;
                widgetScale = desktopWidget.CurrentScale;
            }
            SaveSettings();
        }

        internal double DockShellActualWidth { get { return dockShell != null ? dockShell.ActualWidth : 0; } }
        internal double DockShellActualHeight { get { return dockShell != null ? dockShell.ActualHeight : 0; } }

        internal bool ShowMascot { get { return showMascot; } }
        internal string MascotType { get { return mascotType; } }
        internal string MascotMode { get { return mascotMode; } }
        internal double MascotScale { get { return mascotScale; } }

        internal string MascotTypeDisplay
        {
            get
            {
                switch (mascotType)
                {
                    case "Fox": return "Raposinha (Pixel Fox)";
                    case "CatOrange": return "Gatinho Tigrão (Laranja)";
                    case "CatWhite": return "Gatinho Neve (Branco)";
                    case "CatTuxedo": return "Gatinho Frajola (Tuxedo)";
                    case "Dog": return "Cachorrinho Caramelo";
                    case "Duck": return "Gansinho / Pato";
                    default: return "Gatinho Meia-Noite (Preto)";
                }
            }
        }

        internal string MascotModeDisplay
        {
            get
            {
                switch (mascotMode)
                {
                    case "Wander": return "Passear pela Barra toda";
                    case "RunCat": return "Ritmo pela CPU (RunCat)";
                    case "Sleep": return "Dormir / Descansar";
                    default: return "Canto Direito (Acima da Lixeira)";
                }
            }
        }

        internal string MascotScaleLabel
        {
            get
            {
                if (mascotScale <= 0.85) return "80% (Pequeno)";
                if (mascotScale >= 2.05) return "220% (Gigante)";
                if (mascotScale >= 1.7) return "180% (Extra Grande)";
                if (mascotScale >= 1.4) return "150% (Grande)";
                if (mascotScale >= 1.15) return "125% (Médio)";
                return "100% (Padrão)";
            }
        }

        internal void SetShowMascot(bool show)
        {
            showMascot = show;
            if (dockMascot != null) dockMascot.SetEnabled(show);
            if (mascotMenuItem != null) mascotMenuItem.IsChecked = show;
            SaveSettings();
        }

        internal void SetMascotType(string type)
        {
            mascotType = type;
            if (dockMascot != null) dockMascot.SetType(type);
            SaveSettings();
        }

        internal void SetMascotTypeFromDisplay(string display)
        {
            if (display.Contains("Raposinha") || display.Contains("Fox")) SetMascotType("Fox");
            else if (display.Contains("Laranja") || display.Contains("Tigrão")) SetMascotType("CatOrange");
            else if (display.Contains("Branco") || display.Contains("Neve")) SetMascotType("CatWhite");
            else if (display.Contains("Frajola") || display.Contains("Tuxedo")) SetMascotType("CatTuxedo");
            else if (display.Contains("Cachorro") || display.Contains("Caramelo")) SetMascotType("Dog");
            else if (display.Contains("Ganso") || display.Contains("Pato")) SetMascotType("Duck");
            else SetMascotType("CatBlack");
        }

        internal void SetMascotMode(string mode)
        {
            mascotMode = mode;
            if (dockMascot != null) dockMascot.SetMode(mode);
            SaveSettings();
        }

        internal void SetMascotModeFromDisplay(string display)
        {
            if (display.Contains("Passear")) SetMascotMode("Wander");
            else if (display.Contains("CPU") || display.Contains("RunCat")) SetMascotMode("RunCat");
            else if (display.Contains("Dormir") || display.Contains("Descansar")) SetMascotMode("Sleep");
            else SetMascotMode("PatrolRight");
        }

        internal void SetMascotScale(double scale)
        {
            mascotScale = Math.Max(0.6, Math.Min(3.0, scale));
            if (dockMascot != null) dockMascot.SetScale(mascotScale);
            SaveSettings();
        }

        internal void SetMascotScaleFromLabel(string label)
        {
            if (label.StartsWith("80%")) SetMascotScale(0.8);
            else if (label.StartsWith("125%")) SetMascotScale(1.25);
            else if (label.StartsWith("150%")) SetMascotScale(1.5);
            else if (label.StartsWith("180%")) SetMascotScale(1.8);
            else if (label.StartsWith("220%")) SetMascotScale(2.2);
            else SetMascotScale(1.0);
        }

        private void UpdateDesktopWidgetVisibility()
        {
            if (showDesktopWidget)
            {
                if (desktopWidget == null)
                {
                    try
                    {
                        desktopWidget = new DesktopWidgetWindow(this, widgetX, widgetY, widgetLocked, widgetSeconds, widget24Hour, widgetLanguage, widgetTheme, widgetScale);
                        desktopWidget.Show();
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    desktopWidget.Show();
                }
            }
            else
            {
                if (desktopWidget != null)
                {
                    desktopWidget.Hide();
                }
            }
        }

        internal void SelectTheme(string name)
        {
            dockTheme = name;
            darkMode = name != "Nativo" && name != "Papel";
            ApplyTheme();
            SaveSettings();
        }

        internal void SetIconTileStyle(string style)
        {
            iconTileStyle = style;
            foreach (Border tile in themedTiles) ApplyTileTheme(tile);
            SaveSettings();
        }

        internal void SetDockOpacity(double value)
        {
            dockOpacity = (byte)Math.Max(80, Math.Min(255, (int)Math.Round(value)));
            ApplyTheme();
            SaveSettings();
        }

        internal void SetDockIconSize(double value)
        {
            dockIconSize = Math.Max(40, Math.Min(64, Math.Round(value)));
            ApplyDockSizing();
            SaveSettings();
        }

        internal void SetDockBottomGap(double value)
        {
            dockBottomGap = Math.Max(0, Math.Min(20, Math.Round(value)));
            UpdateAppBarRegistration();
            SchedulePositionDock();
            SaveSettings();
        }

        internal void SetRunningIndicatorWidth(double value)
        {
            runningIndicatorWidth = Math.Max(4, Math.Min(18, Math.Round(value)));
            foreach (Border indicator in runningDots.Values) indicator.Width = runningIndicatorWidth;
            if (taskItems != null)
                foreach (StackPanel tile in taskItems.Children.OfType<StackPanel>())
                    foreach (Border indicator in tile.Children.OfType<Border>())
                        if (String.Equals(indicator.Tag as string, "RunningIndicator", StringComparison.Ordinal))
                            indicator.Width = runningIndicatorWidth;
            SaveSettings();
        }

        internal void SetAutoHideEnabled(bool enabled)
        {
            autoHideEnabled = enabled;
            if (keepWindowsOffItem != null) keepWindowsOffItem.IsEnabled = !autoHideEnabled;
            UpdateAppBarRegistration();
            SchedulePositionDock();
            if (autoHideEnabled) ScheduleAutoHide(); else ShowDockAnimated();
            SaveSettings();
        }

        internal void SetKeepWindowsOffDock(bool enabled)
        {
            keepWindowsOffDock = enabled;
            UpdateAppBarRegistration();
            SchedulePositionDock();
            SaveSettings();
        }

        internal void SetTaskbarHidden(bool hidden)
        {
            taskbarHidden = hidden;
            UpdateTaskbarVisibilityGuard();
            if (taskbarHidden) ApplyCompleteTaskbarHide(); else RestoreOriginalTaskbarState();
            UpdateAppBarRegistration();
            PositionDock();
            SaveSettings();
        }

        internal void SetMacCursorEnabled(bool enabled)
        {
            macCursorEnabled = enabled;
            MacCursorService.IsEnabled = enabled;
            if (enabled)
            {
                MacCursorService.Apply(cursorTheme, macCursorSize);
            }
            else
            {
                MacCursorService.Restore();
            }
            SaveSettings();
        }

        internal void SetCursorTheme(string theme)
        {
            cursorTheme = theme;
            MacCursorService.CurrentTheme = theme;
            if (macCursorEnabled)
            {
                MacCursorService.Apply(cursorTheme, macCursorSize);
            }
            SaveSettings();
        }

        internal void SetMacCursorSize(string size)
        {
            macCursorSize = size;
            MacCursorService.CurrentSize = size;
            if (macCursorEnabled)
            {
                MacCursorService.Apply(cursorTheme, macCursorSize);
            }
            SaveSettings();
        }

        public void OpenSettingsWindow(string initialPage = null)
        {
            autoHideTimer.Stop();
            ShowDockAnimated();
            if (settingsWindow != null)
            {
                if (!String.IsNullOrWhiteSpace(initialPage)) settingsWindow.ShowSettingsPageExternal(initialPage);
                settingsWindow.Activate();
                return;
            }
            settingsWindow = new DockSettingsWindow(this, initialPage ?? "general");
            settingsWindow.Owner = this;
            PositionSettingsWindowAboveDock(settingsWindow);
            settingsWindow.Closed += delegate
            {
                settingsWindow = null;
                ScheduleAutoHide();
            };
            settingsWindow.Show();
        }

        private void PositionSettingsWindowAboveDock(Window window)
        {
            if (window == null) return;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            Rect area = SystemParameters.WorkArea;
            double dockTop = RevealedDockTop();
            double availableHeight = Math.Max(0, dockTop - area.Top - 18);
            if (availableHeight < window.Height && availableHeight >= window.MinHeight)
                window.Height = availableHeight;
            window.Left = area.Left + Math.Max(8, (area.Width - window.Width) / 2);
            window.Top = Math.Max(area.Top + 8, dockTop - window.Height - 10);
        }

        private void ApplyWindows11MenuStyle(ContextMenu menu)
        {
            if (menu == null) return;
            bool isDark = darkMode;
            Color menuBgColor = isDark ? Color.FromArgb(120, 26, 26, 30) : Color.FromArgb(145, 245, 245, 248);
            Color menuBorderColor = isDark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);
            Brush textBrush = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(24, 24, 26));
            Brush secondaryTextBrush = isDark ? new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
            Brush hoverBrush = isDark ? new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
            Brush separatorBrush = isDark ? new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(24, 0, 0, 0));

            menu.Background = new SolidColorBrush(menuBgColor);
            menu.Foreground = textBrush;
            menu.BorderBrush = new SolidColorBrush(menuBorderColor);
            menu.BorderThickness = new Thickness(1);
            menu.Padding = new Thickness(4);
            menu.FontFamily = new FontFamily("Segoe UI");
            menu.FontSize = 13;
            menu.SnapsToDevicePixels = true;
            menu.HasDropShadow = true;

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.45,
                Color = Color.FromRgb(0, 0, 0)
            });
            border.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            menu.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };

            var itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 28.0));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new EventSetter(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnFluentSubmenuOpened)));

            var itemRoot = new FrameworkElementFactory(typeof(Border));
            itemRoot.Name = "ItemBorder";
            itemRoot.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            itemRoot.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
            itemRoot.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
            itemRoot.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var itemPanel = new FrameworkElementFactory(typeof(DockPanel));
            itemPanel.SetValue(DockPanel.LastChildFillProperty, true);

            var iconPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            iconPresenter.Name = "IconPresenter";
            iconPresenter.SetValue(ContentPresenter.ContentSourceProperty, "Icon");
            iconPresenter.SetValue(FrameworkElement.WidthProperty, 18.0);
            iconPresenter.SetValue(FrameworkElement.HeightProperty, 18.0);
            iconPresenter.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 8, 0));
            iconPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            iconPresenter.SetValue(DockPanel.DockProperty, Dock.Left);
            itemPanel.AppendChild(iconPresenter);

            var check = new FrameworkElementFactory(typeof(TextBlock));
            check.Name = "CheckMark";
            check.SetValue(TextBlock.TextProperty, "✓");
            check.SetValue(TextBlock.FontSizeProperty, 12.0);
            check.SetValue(FrameworkElement.WidthProperty, 18.0);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            check.SetValue(DockPanel.DockProperty, Dock.Left);
            itemPanel.AppendChild(check);

            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.Name = "SubmenuArrow";
            arrow.SetValue(TextBlock.TextProperty, "›");
            arrow.SetValue(TextBlock.FontSizeProperty, 18.0);
            arrow.SetValue(FrameworkElement.WidthProperty, 16.0);
            arrow.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            arrow.SetValue(DockPanel.DockProperty, Dock.Right);
            itemPanel.AppendChild(arrow);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            itemPanel.AppendChild(content);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
            popup.SetValue(Popup.HorizontalOffsetProperty, -2.0);
            popup.SetValue(Popup.VerticalOffsetProperty, -6.0);
            popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsSubmenuOpen") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent, Mode = System.Windows.Data.BindingMode.TwoWay });
            popup.SetBinding(Popup.PlacementTargetProperty, new System.Windows.Data.Binding { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var submenuBorder = new FrameworkElementFactory(typeof(Border));
            submenuBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(menuBgColor));
            submenuBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(menuBorderColor));
            submenuBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            submenuBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            submenuBorder.SetValue(Border.PaddingProperty, new Thickness(4));
            submenuBorder.SetValue(FrameworkElement.MinWidthProperty, 195.0);
            submenuBorder.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.45,
                Color = Color.FromRgb(0, 0, 0)
            });
            var submenuItems = new FrameworkElementFactory(typeof(StackPanel));
            submenuItems.SetValue(Panel.IsItemsHostProperty, true);
            submenuItems.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
            submenuBorder.AppendChild(submenuItems);
            popup.AppendChild(submenuBorder);
            itemPanel.AppendChild(popup);

            itemRoot.AppendChild(itemPanel);

            var itemTemplate = new ControlTemplate(typeof(MenuItem)) { VisualTree = itemRoot };
            var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "ItemBorder"));
            itemTemplate.Triggers.Add(highlighted);

            var checkedTrigger = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            itemTemplate.Triggers.Add(checkedTrigger);

            var submenuTrigger = new Trigger { Property = MenuItem.HasItemsProperty, Value = true };
            submenuTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SubmenuArrow"));
            itemTemplate.Triggers.Add(submenuTrigger);

            var noIconTrigger = new Trigger { Property = MenuItem.IconProperty, Value = null };
            noIconTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "IconPresenter"));
            itemTemplate.Triggers.Add(noIconTrigger);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .55));
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, secondaryTextBrush));
            itemStyle.Triggers.Add(disabled);

            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
            menu.Resources[typeof(MenuItem)] = itemStyle;

            var separatorLine = new FrameworkElementFactory(typeof(Border));
            separatorLine.SetValue(FrameworkElement.HeightProperty, 1.0);
            separatorLine.SetValue(Border.BackgroundProperty, separatorBrush);
            var separatorTemplate = new ControlTemplate(typeof(System.Windows.Controls.Separator)) { VisualTree = separatorLine };
            var separatorStyle = new Style(typeof(System.Windows.Controls.Separator));
            separatorStyle.Setters.Add(new Setter(Control.TemplateProperty, separatorTemplate));
            separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 4, 6, 4)));
            menu.Resources[typeof(System.Windows.Controls.Separator)] = separatorStyle;
            menu.Resources[SystemColors.HighlightBrushKey] = hoverBrush;
            menu.Resources[SystemColors.HighlightTextBrushKey] = textBrush;
            menu.Resources[SystemColors.MenuBrushKey] = new SolidColorBrush(menuBgColor);
            menu.Resources[SystemColors.MenuTextBrushKey] = textBrush;
        }

        private static bool ApplyBackdropToWindowHandle(IntPtr handle, HwndSource source, bool isDark)
        {
            if (handle == IntPtr.Zero) return false;
            try
            {
                if (source != null && source.CompositionTarget != null)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                }

                int cornerPreference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, 33, ref cornerPreference, Marshal.SizeOf(typeof(int)));

                int darkVal = isDark ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref darkVal, Marshal.SizeOf(typeof(int)));

                int backdropType = 3; // DWMSBT_TRANSIENTWINDOW (Acrylic)
                DwmSetWindowAttribute(handle, 38, ref backdropType, Marshal.SizeOf(typeof(int)));

                int tint = isDark ? unchecked((int)0x551E1C1C) : unchecked((int)0x65F0F0F0);
                ApplyAcrylicToHandle(handle, tint);

                return true;
            }
            catch { return false; }
        }

        private void OnFluentSubmenuOpened(object sender, RoutedEventArgs args)
        {
            var item = sender as MenuItem;
            if (item == null) return;
            int attempts = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            timer.Tick += delegate
            {
                attempts++;
                bool done = false;
                try
                {
                    var popup = item.Template.FindName("PART_Popup", item) as Popup;
                    Visual visual = popup == null ? null : popup.Child as Visual;
                    HwndSource source = visual == null ? null : PresentationSource.FromVisual(visual) as HwndSource;
                    if (source != null && source.Handle != IntPtr.Zero)
                    {
                        done = ApplyBackdropToWindowHandle(source.Handle, source, darkMode);
                    }
                }
                catch { }
                if (done || attempts >= 8 || !item.IsSubmenuOpen) timer.Stop();
            };
            try
            {
                var popup = item.Template.FindName("PART_Popup", item) as Popup;
                Visual visual = popup == null ? null : popup.Child as Visual;
                HwndSource immediate = visual == null ? null : PresentationSource.FromVisual(visual) as HwndSource;
                if (immediate != null && immediate.Handle != IntPtr.Zero)
                {
                    ApplyBackdropToWindowHandle(immediate.Handle, immediate, darkMode);
                    return;
                }
            }
            catch { }
            timer.Start();
        }

        private void ApplyContextMenuBackdrop(ContextMenu menu)
        {
            if (menu == null) return;
            int attempts = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            timer.Tick += delegate
            {
                attempts++;
                bool done = false;
                try
                {
                    HwndSource source = PresentationSource.FromVisual(menu) as HwndSource;
                    if (source != null && source.Handle != IntPtr.Zero)
                    {
                        done = ApplyBackdropToWindowHandle(source.Handle, source, darkMode);
                    }
                }
                catch { }
                if (done || attempts >= 8 || !menu.IsOpen) timer.Stop();
            };
            try
            {
                HwndSource immediate = PresentationSource.FromVisual(menu) as HwndSource;
                if (immediate != null && immediate.Handle != IntPtr.Zero)
                {
                    ApplyBackdropToWindowHandle(immediate.Handle, immediate, darkMode);
                    return;
                }
            }
            catch { }
            timer.Start();
        }

        private void ConfigureDockContextMenu(ContextMenu menu, FrameworkElement target = null)
        {
            ApplyWindows11MenuStyle(menu);
            menu.PlacementTarget = target ?? dockShell;
            menu.Placement = PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = delegate(Size popupSize, Size targetSize, Point offset)
            {
                double left;
                if (target != null && target != dockShell)
                {
                    left = (targetSize.Width - popupSize.Width) / 2.0;
                }
                else
                {
                    double pointerX;
                    try { pointerX = Mouse.GetPosition(dockShell).X; }
                    catch { pointerX = targetSize.Width / 2.0; }
                    left = pointerX - popupSize.Width / 2.0;
                }
                if (dockShell != null)
                {
                    left = Math.Max(4, Math.Min(left, dockShell.ActualWidth - popupSize.Width - 4));
                }
                return new[]
                {
                    new CustomPopupPlacement(new Point(left, -popupSize.Height - 10), PopupPrimaryAxis.Horizontal)
                };
            };
            menu.Opened += delegate
            {
                activeContextMenu = menu;
                autoHideTimer.Stop();
                ShowDockAnimated();
                HidePreview();
                ApplyContextMenuBackdrop(menu);
                ScheduleDockContextMenuPosition(menu);
            };
            menu.Closed += delegate
            {
                if (ReferenceEquals(activeContextMenu, menu)) activeContextMenu = null;
                ScheduleAutoHide();
            };
        }

        private void ScheduleDockContextMenuPosition(ContextMenu menu)
        {
            int attempts = 0;
            var positionTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(14)
            };
            positionTimer.Tick += delegate
            {
                attempts++;
                TryPositionDockContextMenu(menu);
                if (attempts >= 6 || menu == null || !menu.IsOpen) positionTimer.Stop();
            };
            // Uma primeira tentativa imediata reduz a piscada; as seguintes
            // vencem o reposicionamento tardio feito internamente pelo WPF.
            TryPositionDockContextMenu(menu);
            positionTimer.Start();
        }

        private bool TryPositionDockContextMenu(ContextMenu menu)
        {
            if (menu == null || !menu.IsOpen || dockShell == null) return false;
            try
            {
                HwndSource source = PresentationSource.FromVisual(menu) as HwndSource;
                if (source == null || source.Handle == IntPtr.Zero) return false;
                NativeRect bounds;
                if (!GetWindowRect(source.Handle, out bounds)) return false;
                int width = bounds.Right - bounds.Left;
                int height = bounds.Bottom - bounds.Top;
                if (width <= 0 || height <= 0) return false;

                Point dockTop = dockShell.PointToScreen(new Point(0, 0));
                CursorPoint cursor;
                if (!GetCursorPos(out cursor)) cursor = new CursorPoint { X = (int)dockTop.X + width / 2, Y = (int)dockTop.Y };

                int centerX = cursor.X;
                var target = menu.PlacementTarget as FrameworkElement;
                if (target != null && target != dockShell && target.IsLoaded && target.ActualWidth > 0)
                {
                    try
                    {
                        Point targetScreen = target.PointToScreen(new Point(target.ActualWidth / 2.0, 0));
                        centerX = (int)Math.Round(targetScreen.X);
                    }
                    catch { }
                }

                Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point(cursor.X, cursor.Y));
                int left = centerX - width / 2;
                left = Math.Max(screen.Bounds.Left + 6, Math.Min(left, screen.Bounds.Right - width - 6));
                int top = (int)Math.Round(dockTop.Y) - height - 10;
                top = Math.Max(screen.Bounds.Top + 6, top);
                return SetWindowPos(source.Handle, new IntPtr(-1), left, top, 0, 0, 0x0001 | 0x0010);
            }
            catch { return false; }
        }

        private static void ApplyAcrylicToHandle(IntPtr handle, int tint)
        {
            if (handle == IntPtr.Zero) return;
            var accent = new AccentPolicy { AccentState = 4, AccentFlags = 2, GradientColor = tint };
            int size = Marshal.SizeOf(accent);
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pointer, false);
                var data = new WindowCompositionAttributeData { Attribute = 19, SizeOfData = size, Data = pointer };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        private MenuItem SliderMenuItem(string label, string suffix, double minimum, double maximum, double value, Action<double> changed)
        {
            var panel = new StackPanel { Width = 250, Margin = new Thickness(5, 4, 5, 5) };
            var caption = new Grid();
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            caption.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var valueText = new TextBlock { Text = Math.Round(value) + " " + suffix, Opacity = .72, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(valueText, 1);
            caption.Children.Add(valueText);
            panel.Children.Add(caption);
            var slider = new Slider
            {
                Minimum = minimum, Maximum = maximum, Value = value,
                TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 9, 0, 0),
                IsMoveToPointEnabled = true
            };
            slider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> args)
            {
                valueText.Text = Math.Round(args.NewValue) + " " + suffix;
                changed(args.NewValue);
            };
            panel.Children.Add(slider);
            return new MenuItem { Header = panel, StaysOpenOnClick = true, Tag = label };
        }

        private void SyncDockMenu()
        {
            if (dockMenu == null) return;
            foreach (MenuItem item in dockMenu.Items.OfType<MenuItem>())
            {
                string header = item.Header as string;
                if (header == "Espaços de Trabalho" && workspaceManager != null)
                {
                    item.Items.Clear();
                    for (int i = 0; i < workspaceManager.Workspaces.Count; i++)
                    {
                        int idx = i;
                        var ws = workspaceManager.Workspaces[i];
                        bool isActive = i == workspaceManager.ActiveIndex;
                        var wsItem = new MenuItem
                        {
                            Header = (isActive ? "✓  " : "    ") + (ws.Icon ?? "💼") + " " + ws.Name + "  (Ctrl+Alt+" + (i + 1) + ")",
                            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
                        };
                        wsItem.Click += delegate { SwitchToWorkspace(idx); };
                        item.Items.Add(wsItem);
                    }
                    item.Items.Add(new System.Windows.Controls.Separator());
                    var activeWs = workspaceManager.ActiveWorkspace;
                    string actName = activeWs != null ? activeWs.Name : "Espaço";
                    var launchAll = new MenuItem
                    {
                        Header = "🚀 Abrir / Restaurar todos os apps (" + actName + ")"
                    };
                    launchAll.Click += delegate { LaunchOrRestoreWorkspaceApps(activeWs); };
                    item.Items.Add(launchAll);

                    var manageItem = new MenuItem
                    {
                        Header = "⚙️ Gerenciar espaços…"
                    };
                    manageItem.Click += delegate { OpenSettingsWindow("workspaces"); };
                    item.Items.Add(manageItem);
                }
                else if (header == "Tema")
                {
                    foreach (MenuItem child in item.Items.OfType<MenuItem>()) child.IsChecked = String.Equals(child.Tag as string, dockTheme, StringComparison.Ordinal);
                }
                else if (header == "Transparência")
                {
                    foreach (MenuItem child in item.Items.OfType<MenuItem>()) child.IsChecked = child.Tag is byte && (byte)child.Tag == dockOpacity;
                }
                else if (header == "Tamanho e posição")
                {
                    foreach (MenuItem child in item.Items.OfType<MenuItem>())
                    {
                        var panel = child.Header as StackPanel;
                        var slider = panel == null ? null : panel.Children.OfType<Slider>().FirstOrDefault();
                        if (slider == null) continue;
                        string key = child.Tag as string;
                        slider.Value = key == "Tamanho dos ícones" ? dockIconSize
                            : key == "Largura do indicador" ? runningIndicatorWidth
                            : dockBottomGap;
                    }
                }
                else if (header == "Ocultar automaticamente") item.IsChecked = autoHideEnabled;
                else if (header == "Manter janelas acima da Dock") { item.IsChecked = keepWindowsOffDock; item.IsEnabled = !autoHideEnabled; }
                else if (header == "Ocultar barra de tarefas original") item.IsChecked = taskbarHidden;
                else if (header == "Exibir assistente") item.IsChecked = showAssistant;
                else if (header == "Exibir widget na área de trabalho") item.IsChecked = showDesktopWidget;
                else if (header == "Exibir mascote na barra") item.IsChecked = showMascot;
            }
        }

        private void ApplyDockSizing()
        {
            if (magnifyHost == null || dockShell == null) return;
            StopDockWave();
            double scale = dockIconSize / 48.0;
            magnifyHost.LayoutTransform = new ScaleTransform(scale, scale);
            dockShell.Height = Math.Max(62, 72 + (dockIconSize - 48) * 1.15);
            Height = Math.Max(100, 118 + (dockIconSize - 48) * 1.45);
            waveLayoutDirty = true;
            UpdateAppBarRegistration();
            SchedulePositionDock();
        }

        private void ApplyTheme()
        {
            if (dockShell != null)
            {
                Color shellColor = Color.FromRgb(31, 31, 35);
                Color borderColor = Colors.White;
                byte borderAlpha = 75;
                byte alpha = dockOpacity;
                double borderWidth = 1;
                double radius = 21;
                Color glowColor = Color.FromRgb(70, 61, 47);
                double glow = .25;
                double blur = 22;

                if (dockTheme == "Nativo")
                {
                    shellColor = Color.FromRgb(210, 214, 220); borderColor = Color.FromRgb(245, 248, 255);
                    borderAlpha = 180; glowColor = Color.FromRgb(30, 40, 55); glow = .22;
                }
                else if (dockTheme == "Liquid Glass")
                {
                    shellColor = Color.FromRgb(42, 65, 78); borderColor = Color.FromRgb(210, 250, 255);
                    alpha = (byte)Math.Min((int)alpha, 132); borderAlpha = 220; borderWidth = 2; radius = 25;
                    glowColor = Color.FromRgb(100, 235, 255); glow = .55; blur = 28;
                }
                else if (dockTheme == "Rocket")
                {
                    shellColor = Color.FromRgb(23, 29, 45); borderColor = Color.FromRgb(49, 125, 255);
                    borderAlpha = 220; borderWidth = 2; glowColor = Color.FromRgb(24, 108, 255); glow = .72; blur = 28;
                }
                else if (dockTheme == "Prateleira Aqua")
                {
                    shellColor = Color.FromRgb(36, 73, 78); borderColor = Color.FromRgb(157, 245, 238);
                    borderAlpha = 180; borderWidth = 1.5; radius = 12; glowColor = Color.FromRgb(52, 208, 211); glow = .48;
                }
                else if (dockTheme == "Neon")
                {
                    shellColor = Color.FromRgb(10, 15, 27); borderColor = Color.FromRgb(0, 238, 255);
                    borderAlpha = 245; borderWidth = 2; radius = 27; glowColor = Color.FromRgb(255, 0, 190); glow = .85; blur = 31;
                }
                else if (dockTheme == "Canarinho")
                {
                    shellColor = Color.FromRgb(103, 139, 19); borderColor = Color.FromRgb(245, 226, 43);
                    borderAlpha = 230; borderWidth = 2; radius = 25; glowColor = Color.FromRgb(205, 231, 48); glow = .62;
                }
                else if (dockTheme == "Papel")
                {
                    shellColor = Color.FromRgb(250, 249, 244); borderColor = Color.FromRgb(218, 218, 212);
                    alpha = (byte)Math.Max((int)alpha, 225); borderAlpha = 245; glowColor = Color.FromRgb(50, 50, 55); glow = .18; blur = 18;
                }
                else if (dockTheme == "Flutuante")
                {
                    shellColor = Color.FromRgb(20, 23, 29); borderColor = Colors.Transparent;
                    alpha = 18; borderAlpha = 0; borderWidth = 0; glow = 0; blur = 0;
                }

                dockShell.Background = new SolidColorBrush(Color.FromArgb(alpha, shellColor.R, shellColor.G, shellColor.B));
                dockShell.BorderBrush = new SolidColorBrush(Color.FromArgb(borderAlpha, borderColor.R, borderColor.G, borderColor.B));
                dockShell.BorderThickness = new Thickness(borderWidth);
                dockShell.CornerRadius = new CornerRadius(radius);
                dockShell.Effect = glow <= 0 ? null : new DropShadowEffect
                {
                    Color = glowColor, BlurRadius = blur, ShadowDepth = dockTheme == "Prateleira Aqua" ? 5 : 2, Opacity = glow
                };
            }

            foreach (var tile in themedTiles) ApplyTileTheme(tile);
            adaptiveLightSurface = null;
            ApplyReadableSystemStyle();
        }

        private static Effect CreateTextHalo(Color color)
        {
            var effect = new DropShadowEffect
            {
                Color = color,
                Direction = 0,
                ShadowDepth = 0,
                BlurRadius = 3.4,
                Opacity = .95
            };
            effect.Freeze();
            return effect;
        }

        private void ApplyTileTheme(Border tile)
        {
            if (tile == null) return;
            Color background = darkMode ? Color.FromArgb(205, 45, 45, 50) : Color.FromArgb(205, 255, 255, 255);
            Color border = darkMode ? Color.FromArgb(65, 255, 255, 255) : Color.FromArgb(75, 120, 112, 101);
            double radius = 12;
            if (dockTheme == "Liquid Glass")
            {
                background = Color.FromArgb(115, 225, 248, 255); border = Color.FromArgb(210, 235, 255, 255); radius = 15;
            }
            else if (dockTheme == "Rocket")
            {
                background = Color.FromArgb(225, 27, 34, 53); border = Color.FromArgb(150, 66, 130, 255);
            }
            else if (dockTheme == "Prateleira Aqua")
            {
                background = Color.FromArgb(205, 225, 245, 241); border = Color.FromArgb(120, 51, 179, 184); radius = 8;
            }
            else if (dockTheme == "Neon")
            {
                background = Color.FromArgb(225, 23, 25, 38); border = Color.FromArgb(220, 0, 235, 255); radius = 15;
            }
            else if (dockTheme == "Canarinho")
            {
                background = Color.FromArgb(225, 188, 205, 48); border = Color.FromArgb(225, 250, 229, 55); radius = 16;
            }
            else if (dockTheme == "Papel")
            {
                background = Color.FromArgb(245, 255, 255, 252); border = Color.FromArgb(85, 110, 110, 105); radius = 14;
            }
            else if (dockTheme == "Flutuante")
            {
                background = Color.FromArgb(210, 36, 39, 48); border = Color.FromArgb(55, 255, 255, 255); radius = 14;
            }
            if (iconTileStyle == "Branco")
            {
                background = Color.FromArgb(238, 255, 255, 255);
                border = Color.FromArgb(75, 110, 110, 115);
            }
            else if (iconTileStyle == "Escuro")
            {
                background = Color.FromArgb(225, 38, 40, 48);
                border = Color.FromArgb(75, 255, 255, 255);
            }
            else if (iconTileStyle == "Transparente")
            {
                background = Color.FromArgb(30, 255, 255, 255);
                border = Color.FromArgb(48, 255, 255, 255);
            }
            if (String.Equals(tile.Tag as string, "LightIcon", StringComparison.Ordinal))
            {
                // Ícones oficiais brancos (ChatGPT e alguns assistentes) somem
                // sobre o estilo Branco. Apenas esses blocos recebem contraste.
                background = Color.FromArgb(235, 38, 40, 48);
                border = Color.FromArgb(90, 255, 255, 255);
            }
            tile.Background = new SolidColorBrush(background);
            tile.BorderBrush = new SolidColorBrush(border);
            tile.CornerRadius = new CornerRadius(radius);
        }

        private FrameworkElement TaskTile(TaskApp app)
        {
            var stack = new StackPanel { Width = 64, Height = 61, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false, Tag = app };
            stack.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                taskDragStart = args.GetPosition(taskItems);
                taskDragInProgress = false;
                taskClickCount = args.ClickCount;
            };
            stack.PreviewMouseMove += delegate(object sender, MouseEventArgs args)
            {
                if (args.LeftButton != MouseButtonState.Pressed) return;
                Point current = args.GetPosition(taskItems);
                if (!taskDragInProgress)
                {
                    if (Math.Abs(current.X - taskDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                        Math.Abs(current.Y - taskDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    taskDragInProgress = true;
                    HidePreview();
                    StopDockWave();
                    PrepareTaskTileDrag(stack);
                    stack.Cursor = Cursors.SizeWE;
                    Mouse.Capture(stack, CaptureMode.SubTree);
                }
                MoveTaskTileToPointer(stack, current.X);
                PositionDraggedTaskTile(stack, current.X);
                args.Handled = true;
            };
            stack.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                if (!taskDragInProgress) return;
                args.Handled = true;
                stack.Cursor = Cursors.Arrow;
                if (Mouse.Captured == stack) Mouse.Capture(null);
                FinishTaskTileDrag(stack);
                SaveCurrentTaskOrder();
                taskDragInProgress = false;
                if (magnifyHost != null && magnifyHost.IsMouseOver) StartDockWave();
            };
            var button = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(65, 120, 112, 101)),
                BorderThickness = new Thickness(.5),
                RenderTransformOrigin = new Point(.5, 1),
                RenderTransform = new ScaleTransform(1, 1),
                ToolTip = null,
                Cursor = Cursors.Hand
                ,ClipToBounds = false,
                Tag = IsMostlyLightIcon(app.Icon) ? "LightIcon" : null
            };

            if (app.Icon != null)
                button.Child = new Image { Source = app.Icon, Width = 33, Height = 33, Stretch = Stretch.Uniform };
            else
                button.Child = new TextBlock { Text = "\uE7C3", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 25, Foreground = new SolidColorBrush(Color.FromRgb(45, 99, 178)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            button.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                if (taskDragInProgress)
                {
                    taskDragInProgress = false;
                    return;
                }
                DismissToolTip(button);
                // O primeiro clique já restaurou, minimizou ou alternou. O
                // segundo clique de um duplo clique é ignorado para que a ação
                // não seja desfeita imediatamente.
                if (taskClickCount >= 2)
                {
                    taskClickCount = 0;
                    foregroundBeforeDockClick = IntPtr.Zero;
                    return;
                }
                if (app.WindowHandles.Count == 0) Bounce(stack);
                OpenTask(app);
            };
            // Clique do meio abre uma nova janela/instância, como na barra do Windows.
            button.MouseUp += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.ChangedButton != MouseButton.Middle || taskDragInProgress) return;
                args.Handled = true;
                foregroundBeforeDockClick = IntPtr.Zero;
                HidePreview();
                Bounce(stack);
                Launch(app.Target, app.Arguments);
            };
            button.ContextMenu = BuildAppContextMenu(app, button);
            if (app.WindowHandle != IntPtr.Zero)
            {
                button.ToolTip = null;
                button.MouseEnter += delegate { SchedulePreview(app, button); };
                button.MouseLeave += delegate { SchedulePreviewHide(); };
            }
            else button.ToolTip = CreateNameToolTip(app.Name, button);
            themedTiles.Add(button);
            ApplyTileTheme(button);
            AddMagnify(button, 1.40);
            stack.Children.Add(button);
            Color tileAccent = app.GetAccentColor() ?? Color.FromRgb(25, 120, 225);
            stack.Children.Add(new Border
            {
                Width = app.WindowHandles.Count > 0 ? runningIndicatorWidth : 0,
                Height = app.WindowHandles.Count > 0 ? 4 : 0,
                Margin = new Thickness(0, 3, 0, 0),
                Background = new SolidColorBrush(tileAccent),
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = "RunningIndicator"
            });
            return stack;
        }

        private static string TaskAppKey(TaskApp app)
        {
            if (app == null) return String.Empty;
            if (!String.IsNullOrWhiteSpace(app.AppUserModelId)) return "aumid:" + app.AppUserModelId.ToLowerInvariant();
            if (!String.IsNullOrWhiteSpace(app.Identity)) return "id:" + app.Identity.ToLowerInvariant();
            string target = app.Target ?? String.Empty;
            try { target = System.IO.Path.GetFullPath(target); } catch { }
            return "path:" + target.ToLowerInvariant();
        }

        private void MoveTaskTileToPointer(FrameworkElement dragged, double pointerX)
        {
            if (dragged == null || taskItems == null || !taskItems.Children.Contains(dragged)) return;
            int insertIndex = 0;
            foreach (FrameworkElement element in taskItems.Children.OfType<FrameworkElement>())
            {
                if (element == dragged) continue;
                Vector offset = VisualTreeHelper.GetOffset(element);
                double center = offset.X + element.ActualWidth / 2;
                if (pointerX >= center) insertIndex++;
            }

            int oldIndex = taskItems.Children.IndexOf(dragged);
            if (oldIndex == insertIndex) return;
            var oldPositions = new Dictionary<FrameworkElement, double>();
            foreach (FrameworkElement element in taskItems.Children.OfType<FrameworkElement>())
            {
                if (element != dragged)
                    oldPositions[element] = element.TranslatePoint(new Point(0, 0), taskItems).X;
            }
            taskItems.Children.Remove(dragged);
            insertIndex = Math.Max(0, Math.Min(insertIndex, taskItems.Children.Count));
            taskItems.Children.Insert(insertIndex, dragged);
            taskItems.UpdateLayout();
            foreach (var item in oldPositions)
            {
                double newX = item.Key.TranslatePoint(new Point(0, 0), taskItems).X;
                double delta = item.Value - newX;
                if (Math.Abs(delta) < .5) continue;
                var translate = item.Key.RenderTransform as TranslateTransform;
                if (translate == null)
                {
                    translate = new TranslateTransform();
                    item.Key.RenderTransform = translate;
                }
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.X = 0;
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(145))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                });
            }
            Mouse.Capture(dragged, CaptureMode.SubTree);
            waveLayoutDirty = true;
        }

        private void PrepareTaskTileDrag(FrameworkElement dragged)
        {
            foreach (FrameworkElement element in taskItems.Children.OfType<FrameworkElement>())
            {
                var translate = element.RenderTransform as TranslateTransform;
                if (translate == null)
                {
                    translate = new TranslateTransform();
                    element.RenderTransform = translate;
                }
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.X = 0;
            }
            dragged.Opacity = .96;
            Panel.SetZIndex(dragged, 1000);
        }

        private void PositionDraggedTaskTile(FrameworkElement dragged, double pointerX)
        {
            if (dragged == null || taskItems == null) return;
            Vector offset = VisualTreeHelper.GetOffset(dragged);
            double center = offset.X + dragged.ActualWidth / 2;
            var translate = dragged.RenderTransform as TranslateTransform;
            if (translate == null)
            {
                translate = new TranslateTransform();
                dragged.RenderTransform = translate;
            }
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = pointerX - center;
        }

        private void FinishTaskTileDrag(FrameworkElement dragged)
        {
            if (dragged == null) return;
            dragged.Opacity = 1;
            Panel.SetZIndex(dragged, 0);
            var translate = dragged.RenderTransform as TranslateTransform;
            if (translate == null) return;
            double start = translate.X;
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(start, 0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }

        private void SaveCurrentTaskOrder()
        {
            lock (appOrder)
            {
                appOrder.Clear();
                foreach (FrameworkElement element in taskItems.Children.OfType<FrameworkElement>())
                {
                    var app = element.Tag as TaskApp;
                    if (app != null) appOrder.Add(TaskAppKey(app));
                }
            }
            if (workspaceManager != null && workspaceManager.WorkspacesEnabled && workspaceManager.ActiveWorkspace != null && !workspaceManager.ActiveWorkspace.SyncWithWindows)
            {
                var ws = workspaceManager.ActiveWorkspace;
                var reordered = new List<WorkspaceAppItem>();
                foreach (FrameworkElement element in taskItems.Children.OfType<FrameworkElement>())
                {
                    var app = element.Tag as TaskApp;
                    if (app == null) continue;
                    var match = ws.Apps.FirstOrDefault(a => SameTarget(a.Target, app.Target) ||
                        (!String.IsNullOrWhiteSpace(a.AppUserModelId) && String.Equals(a.AppUserModelId, app.AppUserModelId, StringComparison.OrdinalIgnoreCase)) ||
                        String.Equals(a.Name, app.Name, StringComparison.OrdinalIgnoreCase));
                    if (match != null && !reordered.Contains(match)) reordered.Add(match);
                }
                foreach (var a in ws.Apps) if (!reordered.Contains(a)) reordered.Add(a);
                ws.Apps.Clear();
                ws.Apps.AddRange(reordered);
                workspaceManager.Save();
            }
            taskSignature = String.Empty;
            waveLayoutDirty = true;
            SaveSettings();
        }

        private static MenuItem CreateJumpListItem(string header, string glyph, Action action)
        {
            var item = new MenuItem { Header = header };
            if (!String.IsNullOrWhiteSpace(glyph))
            {
                item.Icon = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
            }
            if (action != null)
            {
                item.Click += delegate { action(); };
            }
            return item;
        }

        private static MenuItem CreateJumpListHeader(string title)
        {
            var item = new MenuItem
            {
                Header = title,
                IsEnabled = false,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Margin = new Thickness(6, 4, 6, 2)
            };
            return item;
        }

        private static void LaunchAsAdmin(string target, string arguments)
        {
            if (String.IsNullOrWhiteSpace(target)) return;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = target,
                    Arguments = arguments ?? String.Empty,
                    Verb = "runas",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch { }
        }

        private static void CreatePinnedShortcut(string target, string arguments, string name)
        {
            object shell = null;
            object shortcutObject = null;
            try
            {
                string pinned = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                if (!Directory.Exists(pinned)) Directory.CreateDirectory(pinned);
                string cleanName = String.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(target) : name;
                string lnkPath = System.IO.Path.Combine(pinned, cleanName + ".lnk");
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return;
                shell = Activator.CreateInstance(type);
                dynamic dynamicShell = shell;
                shortcutObject = dynamicShell.CreateShortcut(lnkPath);
                dynamic shortcut = shortcutObject;
                shortcut.TargetPath = target;
                if (!String.IsNullOrWhiteSpace(arguments)) shortcut.Arguments = arguments;
                shortcut.Save();
            }
            catch { }
            finally
            {
                if (shortcutObject != null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void RemovePinnedShortcut(string target, string name)
        {
            try
            {
                string pinned = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                if (!Directory.Exists(pinned)) return;
                foreach (string link in Directory.GetFiles(pinned, "*.lnk"))
                {
                    var item = ResolveShortcut(link);
                    if (item != null && (SameTarget(item.Target, target) || String.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        try { File.Delete(link); } catch { }
                    }
                }
            }
            catch { }
        }

        private void TogglePinApp(TaskApp app)
        {
            if (app == null) return;
            if (workspaceManager != null && workspaceManager.WorkspacesEnabled && workspaceManager.ActiveWorkspace != null && !workspaceManager.ActiveWorkspace.SyncWithWindows)
            {
                var ws = workspaceManager.ActiveWorkspace;
                var existing = ws.Apps.FirstOrDefault(a => SameTarget(a.Target, app.Target) ||
                    (!String.IsNullOrWhiteSpace(a.AppUserModelId) && String.Equals(a.AppUserModelId, app.AppUserModelId, StringComparison.OrdinalIgnoreCase)) ||
                    String.Equals(a.Name, app.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    ws.Apps.Remove(existing);
                    app.IsPinned = false;
                }
                else
                {
                    ws.Apps.Add(new WorkspaceAppItem
                    {
                        Name = app.Name,
                        Target = app.Target,
                        Arguments = app.Arguments,
                        AppUserModelId = app.AppUserModelId,
                        Identity = app.Identity
                    });
                    app.IsPinned = true;
                }
                workspaceManager.Save();
                taskSignature = String.Empty;
                pinnedRefreshDebounceTimer.Stop();
                pinnedRefreshDebounceTimer.Start();
                return;
            }

            if (app.IsPinned)
            {
                RemovePinnedShortcut(app.Target, app.Name);
                app.IsPinned = false;
            }
            else
            {
                CreatePinnedShortcut(app.Target, app.Arguments, app.Name);
                app.IsPinned = true;
            }
            pinnedNamesCacheExpiresUtc = DateTime.MinValue;
            cachedPinnedNames.Clear();
            ScheduleTaskRefreshSoon();
        }

        private ContextMenu BuildAppContextMenu(TaskApp app, Border button)
        {
            var menu = new ContextMenu();
            ConfigureDockContextMenu(menu, button);

            string targetExe = String.IsNullOrWhiteSpace(app.Target) ? String.Empty : System.IO.Path.GetFileName(app.Target).ToLowerInvariant();
            bool isWallpaperEngine = targetExe.Contains("wallpaper") || targetExe.Contains("ui32") || app.Name.IndexOf("Wallpaper Engine", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isExplorer = targetExe == "explorer.exe";
            bool isBrowser = targetExe == "chrome.exe" || targetExe == "msedge.exe" || targetExe == "firefox.exe" || targetExe == "brave.exe" || targetExe == "opera.exe" || targetExe == "vivaldi.exe";
            bool isTerminal = targetExe == "cmd.exe" || targetExe == "powershell.exe" || targetExe == "pwsh.exe" || targetExe == "wt.exe";

            // 1. Status de segundo plano
            if (app.IsRunningInBackground && app.WindowHandles.Count == 0)
            {
                menu.Items.Add(CreateJumpListHeader("● EM EXECUÇÃO EM SEGUNDO PLANO"));
            }

            // 2. Janelas abertas
            MenuItem minimizeRestore = null;
            if (app.WindowHandles.Count > 0)
            {
                if (app.WindowHandles.Count > 1) menu.Items.Add(CreateJumpListHeader("JANELAS"));
                for (int index = 0; index < app.WindowHandles.Count; index++)
                {
                    IntPtr handle = app.WindowHandles[index];
                    string title = index < app.WindowTitles.Count && !String.IsNullOrWhiteSpace(app.WindowTitles[index]) ? app.WindowTitles[index] : app.Name;
                    var windowItem = CreateJumpListItem(title, "\uE7C3", delegate { ActivateWindow(handle); });
                    menu.Items.Add(windowItem);
                }
                menu.Items.Add(new System.Windows.Controls.Separator());

                bool single = app.WindowHandles.Count == 1;
                bool anyVisible = app.WindowHandles.Any(handle => WindowCommands.IsAlive(handle) && !WindowCommands.IsMinimized(handle));
                minimizeRestore = CreateJumpListItem(
                    anyVisible ? (single ? "Minimizar janela" : "Minimizar todas as janelas") : (single ? "Restaurar janela" : "Restaurar todas as janelas"),
                    anyVisible ? "\uE921" : "\uE923",
                    delegate { ToggleTaskGroupMinimized(app); });
                menu.Items.Add(minimizeRestore);

                var closeAll = CreateJumpListItem(
                    single ? "Fechar janela" : "Fechar todas as janelas",
                    "\uE8BB",
                    delegate
                    {
                        foreach (IntPtr handle in app.WindowHandles) WindowCommands.Close(handle);
                        ScheduleTaskRefreshSoon();
                    });
                menu.Items.Add(closeAll);
                menu.Items.Add(new System.Windows.Controls.Separator());
            }

            // 3. Tarefas específicas (Jump List Tasks)
            var taskList = new List<MenuItem>();
            if (isWallpaperEngine)
            {
                string weDir = System.IO.Path.GetDirectoryName(app.Target);
                string uiPath = System.IO.Path.Combine(weDir ?? String.Empty, "ui32.exe");
                if (!File.Exists(uiPath)) uiPath = app.Target;
                string wp32Path = System.IO.Path.Combine(weDir ?? String.Empty, "wallpaper32.exe");
                if (!File.Exists(wp32Path)) wp32Path = System.IO.Path.Combine(weDir ?? String.Empty, "wallpaper64.exe");
                if (!File.Exists(wp32Path)) wp32Path = app.Target;

                taskList.Add(CreateJumpListItem("Abrir painel do Wallpaper Engine", "\uE790", delegate { Launch(uiPath, "-control open"); }));
                taskList.Add(CreateJumpListItem("Pausar planos de fundo", "\uE769", delegate { Launch(wp32Path, "-control pause"); }));
                taskList.Add(CreateJumpListItem("Retomar planos de fundo", "\uE768", delegate { Launch(wp32Path, "-control play"); }));
                taskList.Add(CreateJumpListItem("Parar planos de fundo", "\uE71A", delegate { Launch(wp32Path, "-control stop"); }));
                taskList.Add(CreateJumpListItem("Encerrar Wallpaper Engine", "\uE711", delegate
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        foreach (string p in new[] { "ui32", "wallpaper32", "wallpaper64", "webwallpaper32" })
                            foreach (var proc in Process.GetProcessesByName(p)) try { proc.Kill(); } catch { }
                        ScheduleTaskRefreshSoon();
                    });
                }));
            }
            else if (isBrowser)
            {
                taskList.Add(CreateJumpListItem("Nova janela", "\uE78B", delegate { Launch(app.Target, String.Empty); }));
                string incognitoArg = targetExe == "msedge.exe" ? "-inprivate" : (targetExe == "firefox.exe" ? "-private-window" : "--incognito");
                string incognitoLabel = targetExe == "msedge.exe" ? "Nova janela InPrivate" : "Nova janela anônima";
                taskList.Add(CreateJumpListItem(incognitoLabel, "\uE727", delegate { Launch(app.Target, incognitoArg); }));
            }
            else if (isTerminal)
            {
                taskList.Add(CreateJumpListItem("Nova janela", "\uE756", delegate { Launch(app.Target, String.Empty); }));
                taskList.Add(CreateJumpListItem("Executar como Administrador", "\uE7EF", delegate { LaunchAsAdmin(app.Target, app.Arguments); }));
            }
            else
            {
                taskList.Add(CreateJumpListItem(app.WindowHandles.Count > 0 ? "Abrir nova janela" : "Abrir", "\uE78B", delegate { Launch(app.Target, app.Arguments); }));
                if (File.Exists(app.Target) && app.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    taskList.Add(CreateJumpListItem("Executar como Administrador", "\uE7EF", delegate { LaunchAsAdmin(app.Target, app.Arguments); }));
                }
            }

            if (taskList.Count > 0)
            {
                menu.Items.Add(CreateJumpListHeader("TAREFAS"));
                foreach (var t in taskList) menu.Items.Add(t);
                menu.Items.Add(new System.Windows.Controls.Separator());
            }

            // 4. Arquivos Recentes / Frequentes
            MenuItem recentHeader = CreateJumpListHeader(isExplorer ? "FREQUENTES" : "RECENTES");
            recentHeader.Visibility = Visibility.Collapsed;
            menu.Items.Add(recentHeader);

            // 5. Barra / Dock
            if (File.Exists(app.Target))
            {
                var location = CreateJumpListItem("Mostrar no Explorador de Arquivos", "\uE838", delegate { Launch("explorer.exe", "/select,\"" + app.Target + "\""); });
                menu.Items.Add(location);
            }

            var pinItem = CreateJumpListItem(
                app.IsPinned ? "Desafixar da barra de tarefas" : "Fixar na barra de tarefas",
                app.IsPinned ? "\uE77A" : "\uE718",
                delegate { TogglePinApp(app); });
            menu.Items.Add(pinItem);

            bool recentLoaded = false;
            menu.Opened += delegate
            {
                if (minimizeRestore != null)
                {
                    bool single = app.WindowHandles.Count == 1;
                    bool anyVisible = app.WindowHandles.Any(handle => WindowCommands.IsAlive(handle) && !WindowCommands.IsMinimized(handle));
                    minimizeRestore.Header = anyVisible
                        ? (single ? "Minimizar janela" : "Minimizar todas as janelas")
                        : (single ? "Restaurar janela" : "Restaurar todas as janelas");
                }

                if (recentLoaded) return;
                recentLoaded = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    List<RecentDestination> destinations = ReadRecentDestinations(app.AppUserModelId, app.Target, 10);
                    if (destinations.Count == 0) return;
                    try
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                        {
                            recentHeader.Visibility = Visibility.Visible;
                            int insertBase = menu.Items.IndexOf(recentHeader) + 1;
                            int directCount = Math.Min(5, destinations.Count);
                            for (int i = 0; i < directCount; i++)
                            {
                                RecentDestination dest = destinations[i];
                                string glyph = dest.IsFolder ? "\uE8B7" : "\uE8A5";
                                var rItem = CreateJumpListItem(dest.Name, glyph, delegate { Launch(dest.Path, null); });
                                rItem.ToolTip = dest.Path;
                                menu.Items.Insert(insertBase + i, rItem);
                            }
                            if (destinations.Count > 5)
                            {
                                var moreMenu = new MenuItem { Header = "Mais recentes" };
                                moreMenu.Icon = new TextBlock
                                {
                                    Text = "\uE712",
                                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                                    FontSize = 13,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    HorizontalAlignment = HorizontalAlignment.Center
                                };
                                for (int i = 5; i < destinations.Count; i++)
                                {
                                    RecentDestination dest = destinations[i];
                                    string glyph = dest.IsFolder ? "\uE8B7" : "\uE8A5";
                                    var sub = CreateJumpListItem(dest.Name, glyph, delegate { Launch(dest.Path, null); });
                                    sub.ToolTip = dest.Path;
                                    moreMenu.Items.Add(sub);
                                }
                                menu.Items.Insert(insertBase + directCount, moreMenu);
                            }
                            menu.Items.Insert(insertBase + (destinations.Count > 5 ? directCount + 1 : directCount), new System.Windows.Controls.Separator());
                            ScheduleDockContextMenuPosition(menu);
                        }));
                    }
                    catch { }
                });
            };
            return menu;
        }

        private static List<RecentDestination> ReadRecentDestinations(string appUserModelId, string target, int maximum)
        {
            var result = new List<RecentDestination>();
            if (maximum <= 0) return result;

            if (!String.IsNullOrWhiteSpace(appUserModelId))
            {
                ReadAppDocList(appUserModelId, 0, maximum, result);
                if (result.Count < maximum)
                {
                    ReadAppDocList(appUserModelId, 1, maximum, result);
                }
            }

            string targetName = String.IsNullOrWhiteSpace(target) ? String.Empty : System.IO.Path.GetFileName(target).ToLowerInvariant();
            if (targetName == "explorer.exe" || (appUserModelId != null && appUserModelId.IndexOf("explorer", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                AddSpecialFolderIfMissing(result, "Downloads", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), maximum);
                AddSpecialFolderIfMissing(result, "Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), maximum);
                AddSpecialFolderIfMissing(result, "Imagens", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), maximum);
                AddSpecialFolderIfMissing(result, "Área de Trabalho", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), maximum);
                AddSpecialFolderIfMissing(result, "Este Computador", "shell:MyComputerFolder", maximum);
            }

            if (result.Count == 0 && !String.IsNullOrWhiteSpace(target))
            {
                ReadRecentShortcutsForTarget(target, maximum, result);
            }

            return result;
        }

        private static void ReadAppDocList(string appUserModelId, int listType, int maximum, List<RecentDestination> result)
        {
            object listObject = null;
            object arrayObject = null;
            try
            {
                Type listTypeCls = Type.GetTypeFromCLSID(new Guid("86BEC222-30F2-47E0-9F25-60D11CD75C28"));
                if (listTypeCls == null) return;
                listObject = Activator.CreateInstance(listTypeCls);
                var lists = listObject as IApplicationDocumentLists;
                if (lists == null || lists.SetAppID(appUserModelId) != 0) return;
                Guid arrayId = typeof(IObjectArray).GUID;
                if (lists.GetList(listType, (uint)maximum, ref arrayId, out arrayObject) != 0 || arrayObject == null) return;
                var array = arrayObject as IObjectArray;
                if (array == null) return;
                uint count;
                if (array.GetCount(out count) != 0) return;
                Guid shellItemId = typeof(IShellItem).GUID;
                for (uint index = 0; index < count && result.Count < maximum; index++)
                {
                    object itemObject = null;
                    try
                    {
                        if (array.GetAt(index, ref shellItemId, out itemObject) != 0 || itemObject == null) continue;
                        var item = itemObject as IShellItem;
                        if (item == null) continue;
                        string path = ShellItemDisplayName(item, 0x80058000); // SIGDN_FILESYSPATH
                        if (String.IsNullOrWhiteSpace(path)) path = ShellItemDisplayName(item, 0x80028000); // parsing name
                        if (String.IsNullOrWhiteSpace(path) || result.Any(existing => String.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                        string name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
                        if (String.IsNullOrWhiteSpace(name)) name = path;
                        bool isDir = Directory.Exists(path);
                        result.Add(new RecentDestination { Name = name, Path = path, IsFolder = isDir });
                    }
                    catch { }
                    finally { if (itemObject != null && Marshal.IsComObject(itemObject)) Marshal.FinalReleaseComObject(itemObject); }
                }
            }
            catch { }
            finally
            {
                if (arrayObject != null && Marshal.IsComObject(arrayObject)) Marshal.FinalReleaseComObject(arrayObject);
                if (listObject != null && Marshal.IsComObject(listObject)) Marshal.FinalReleaseComObject(listObject);
            }
        }

        private static void AddSpecialFolderIfMissing(List<RecentDestination> result, string name, string path, int maximum)
        {
            if (result.Count >= maximum || String.IsNullOrWhiteSpace(path)) return;
            if (result.Any(r => String.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            result.Add(new RecentDestination { Name = name, Path = path, IsFolder = true });
        }

        private static void ReadRecentShortcutsForTarget(string target, int maximum, List<RecentDestination> result)
        {
            try
            {
                string recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (!Directory.Exists(recentFolder)) return;
                foreach (string link in Directory.GetFiles(recentFolder, "*.lnk").OrderByDescending(p => File.GetLastWriteTimeUtc(p)).Take(30))
                {
                    if (result.Count >= maximum) break;
                    try
                    {
                        var resolved = ResolveShortcut(link);
                        if (resolved == null || String.IsNullOrWhiteSpace(resolved.Target)) continue;
                        if (!File.Exists(resolved.Target) && !Directory.Exists(resolved.Target)) continue;
                        if (result.Any(r => String.Equals(r.Path, resolved.Target, StringComparison.OrdinalIgnoreCase))) continue;
                        string name = System.IO.Path.GetFileName(resolved.Target);
                        result.Add(new RecentDestination { Name = name, Path = resolved.Target, IsFolder = Directory.Exists(resolved.Target) });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string ShellItemDisplayName(IShellItem item, uint kind)
        {
            IntPtr value = IntPtr.Zero;
            try
            {
                if (item.GetDisplayName(kind, out value) != 0 || value == IntPtr.Zero) return String.Empty;
                return Marshal.PtrToStringUni(value) ?? String.Empty;
            }
            catch { return String.Empty; }
            finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
        }

        private static void Bounce(FrameworkElement container)
        {
            var translate = container.RenderTransform as TranslateTransform;
            if (translate == null)
            {
                translate = new TranslateTransform();
                container.RenderTransform = translate;
            }
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(520) };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-15, KeyTime.FromPercent(.24), new CubicEase { EasingMode = EasingMode.EaseOut }));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(.48), new CubicEase { EasingMode = EasingMode.EaseIn }));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromPercent(.68), new CubicEase { EasingMode = EasingMode.EaseOut }));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseIn }));
            translate.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        private void SchedulePreview(TaskApp app, FrameworkElement anchor)
        {
            bool switchingPreview = previewWindow != null;
            pendingPreview = app;
            pendingPreviewAnchor = anchor;
            previewHideTimer.Stop();
            previewTimer.Stop();
            // A primeira abertura ainda exige intenção do usuário. Depois que
            // o painel já está visível, a troca entre aplicativos é quase
            // imediata, com apenas um pequeno debounce contra passagens rápidas.
            previewTimer.Interval = TimeSpan.FromMilliseconds(switchingPreview ? 16 : 320);
            previewTimer.Start();
        }

        private void ScheduleTaskRefreshSoon()
        {
            if (taskRefreshSoonTimer == null)
            {
                taskRefreshSoonTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                taskRefreshSoonTimer.Tick += delegate
                {
                    taskRefreshSoonTimer.Stop();
                    nextTaskRefreshUtc = DateTime.MinValue;
                    RefreshTaskItems();
                };
            }
            taskRefreshSoonTimer.Stop();
            taskRefreshSoonTimer.Start();
        }

        private Size GetScreenWorkArea(FrameworkElement anchor)
        {
            try
            {
                Point screenPoint = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
                Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)));
                var source = PresentationSource.FromVisual(this);
                double dpiX = 1;
                double dpiY = 1;
                if (source != null && source.CompositionTarget != null)
                {
                    dpiX = source.CompositionTarget.TransformToDevice.M11;
                    dpiY = source.CompositionTarget.TransformToDevice.M22;
                }
                return new Size(screen.WorkingArea.Width / Math.Max(0.1, dpiX), screen.WorkingArea.Height / Math.Max(0.1, dpiY));
            }
            catch
            {
                return new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
            }
        }

        private void ShowPreview()
        {
            if (pendingPreview == null || pendingPreviewAnchor == null || pendingPreview.WindowHandles.Count == 0 || !pendingPreviewAnchor.IsMouseOver) return;
            string key = TaskAppKey(pendingPreview);
            if (previewWindow != null && String.Equals(activePreviewKey, key, StringComparison.OrdinalIgnoreCase))
            {
                previewWindow.UpdateSources(pendingPreview.WindowHandles, pendingPreview.WindowTitles);
                activePreviewAnchor = pendingPreviewAnchor;
                RepositionOpenPreview();
                return;
            }

            HidePreviewWindowOnly();
            Size screenArea = GetScreenWorkArea(pendingPreviewAnchor);
            Color? accent = pendingPreview.GetAccentColor();
            previewWindow = new PreviewWindow(pendingPreview.WindowHandles, pendingPreview.WindowTitles, pendingPreview.Name, screenArea, accent);
            PreviewWindow openedPreview = previewWindow;
            activePreviewKey = key;
            openedPreview.Closed += delegate
            {
                if (!ReferenceEquals(previewWindow, openedPreview)) return;
                previewWindow = null;
                activePreviewKey = null;
                activePreviewAnchor = null;
                previewLastLeft = Int32.MinValue;
                previewLastTop = Int32.MinValue;
                ScheduleAutoHide();
            };
            openedPreview.LayoutChanged += delegate { RepositionOpenPreview(); };
            openedPreview.WindowsChanged += delegate { ScheduleTaskRefreshSoon(); };
            activePreviewAnchor = pendingPreviewAnchor;
            previewWindow.Owner = this;
            previewWindow.MouseEnter += delegate { previewHideTimer.Stop(); };
            previewWindow.MouseLeave += delegate { SchedulePreviewHide(); };

            Point screenPoint = activePreviewAnchor.PointToScreen(new Point(activePreviewAnchor.ActualWidth / 2, 0));
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
                screenPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);

            double left = screenPoint.X - previewWindow.Width / 2;
            double top = screenPoint.Y - previewWindow.Height - 7;
            previewWindow.Left = Math.Max(SystemParameters.VirtualScreenLeft + 6, Math.Min(left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - previewWindow.Width - 6));
            previewWindow.Top = Math.Max(SystemParameters.VirtualScreenTop + 6, top);
            previewWindow.Show();
            previewLastLeft = Int32.MinValue;
            previewLastTop = Int32.MinValue;
            RepositionOpenPreview();
        }

        private void RepositionOpenPreview()
        {
            if (previewWindow == null || activePreviewAnchor == null ||
                !previewWindow.IsLoaded || !activePreviewAnchor.IsLoaded) return;
            try
            {
                // PointToScreen já inclui ScaleTransform e TranslateTransform da
                // onda. SetWindowPos move só a janela nativa, sem recalcular o
                // layout WPF da prévia em cada frame.
                Point anchor = activePreviewAnchor.PointToScreen(
                    new Point(activePreviewAnchor.ActualWidth / 2, 0));
                var source = PresentationSource.FromVisual(this);
                double dpiX = 1;
                double dpiY = 1;
                if (source != null && source.CompositionTarget != null)
                {
                    dpiX = source.CompositionTarget.TransformToDevice.M11;
                    dpiY = source.CompositionTarget.TransformToDevice.M22;
                }

                int width = Math.Max(1, (int)Math.Round(previewWindow.Width * dpiX));
                int height = Math.Max(1, (int)Math.Round(previewWindow.Height * dpiY));
                Forms.Screen screen = Forms.Screen.FromPoint(
                    new Drawing.Point((int)Math.Round(anchor.X), (int)Math.Round(anchor.Y)));
                int previewLeft = (int)Math.Round(anchor.X - width / 2.0);
                int previewTop = (int)Math.Round(anchor.Y - height - 7 * dpiY);
                previewLeft = Math.Max(screen.WorkingArea.Left + 6,
                    Math.Min(previewLeft, screen.WorkingArea.Right - width - 6));
                previewTop = Math.Max(screen.WorkingArea.Top + 6, previewTop);
                if (previewLeft == previewLastLeft && previewTop == previewLastTop) return;

                IntPtr handle = new WindowInteropHelper(previewWindow).Handle;
                if (handle == IntPtr.Zero) return;
                const uint MoveOnlyNoActivate = 0x0001 | 0x0004 | 0x0010 | 0x0200;
                SetWindowPos(handle, IntPtr.Zero, previewLeft, previewTop, 0, 0, MoveOnlyNoActivate);
                previewLastLeft = previewLeft;
                previewLastTop = previewTop;
            }
            catch { }
        }

        private void HidePreview()
        {
            previewTimer.Stop();
            previewHideTimer.Stop();
            pendingPreview = null;
            pendingPreviewAnchor = null;
            HidePreviewWindowOnly();
        }

        private void SchedulePreviewHide()
        {
            previewHideTimer.Stop();
            previewHideTimer.Start();
        }

        private void HidePreviewWindowOnly()
        {
            if (previewWindow == null) return;
            previewSettleTimer.Stop();
            activePreviewKey = null;
            previewWindow.Close();
            previewWindow = null;
            activePreviewAnchor = null;
            previewLastLeft = Int32.MinValue;
            previewLastTop = Int32.MinValue;
        }

        private FrameworkElement AppTile(string glyph, string tip, string processName, Action action)
        {
            var stack = new StackPanel { Width = 64, Height = 61, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false };
            var button = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 120, 112, 101)),
                BorderThickness = new Thickness(.5),
                RenderTransformOrigin = new Point(.5, 1),
                RenderTransform = new ScaleTransform(1, 1),
                ToolTip = null,
                Cursor = Cursors.Hand
                ,ClipToBounds = false
            };

            button.Child = new TextBlock
            {
                Text = glyph,
                FontFamily = glyph == ">_" ? new FontFamily("Consolas") : new FontFamily("Segoe MDL2 Assets"),
                FontSize = glyph == ">_" ? 19 : 25,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(36, 91, 171)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            button.MouseLeftButtonUp += delegate { DismissToolTip(button); action(); };
            button.ToolTip = CreateNameToolTip(tip, button);
            themedTiles.Add(button);
            ApplyTileTheme(button);
            AddMagnify(button);
            stack.Children.Add(button);

            var dot = new Border
            {
                Width = runningIndicatorWidth,
                Height = 4,
                Margin = new Thickness(0, 3, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(25, 120, 225)),
                CornerRadius = new CornerRadius(1.5),
                Visibility = Visibility.Hidden,
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = "RunningIndicator"
            };
            stack.Children.Add(dot);
            if (!String.IsNullOrWhiteSpace(processName)) runningDots[processName] = dot;
            return stack;
        }

        private FrameworkElement MusicCard()
        {
            var card = Card(178);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

            var art = new Border
            {
                Width = 43,
                Height = 43,
                CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush(Color.FromRgb(95, 26, 101), Color.FromRgb(255, 126, 58), 45),
                Child = new TextBlock { Text = "\uE8D6", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 21, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Grid.SetColumn(art, 0);
            grid.Children.Add(art);

            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = "Fim de tarde", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
            labels.Children.Add(new TextBlock { Text = "Minha sessão", FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(205, 197, 187)) });
            labels.Children.Add(new Border { Height = 2, Margin = new Thickness(0, 7, 4, 0), Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)) });
            Grid.SetColumn(labels, 1);
            grid.Children.Add(labels);

            var controls = new TextBlock { Text = "\uE892  \uE768", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(controls, 2);
            grid.Children.Add(controls);
            card.Child = grid;
            return card;
        }

        private FrameworkElement DateCard()
        {
            var border = new Border
            {
                Width = 58,
                Height = 52,
                Margin = new Thickness(3, 0, 3, 8),
                CornerRadius = new CornerRadius(11),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)),
                BorderThickness = new Thickness(.5)
            };
            var stack = new StackPanel();
            dayText = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(238, 61, 50)), TextAlignment = TextAlignment.Center, Padding = new Thickness(0, 2, 0, 2) };
            dateText = new TextBlock { FontSize = 24, Foreground = new SolidColorBrush(Color.FromRgb(28, 28, 28)), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
            stack.Children.Add(dayText);
            stack.Children.Add(dateText);
            border.Child = stack;
            return border;
        }

        private FrameworkElement WeatherCard()
        {
            var card = Card(118);
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = "\uE706", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 25, Foreground = new SolidColorBrush(Color.FromRgb(255, 207, 40)), Margin = new Thickness(7, 0, 9, 0) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = "29°", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = "São Paulo", Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 210)), FontSize = 8 });
            row.Children.Add(text);
            card.Child = row;
            return card;
        }

        private Border Card(double width)
        {
            var card = new Border
            {
                Width = width,
                Height = 50,
                Margin = new Thickness(3, 0, 3, 8),
                Padding = new Thickness(5),
                CornerRadius = new CornerRadius(11),
                Background = new LinearGradientBrush(Color.FromRgb(55, 48, 46), Color.FromRgb(20, 23, 29), 15),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                RenderTransformOrigin = new Point(.5, 1),
                RenderTransform = new ScaleTransform(1, 1)
            };
            AddMagnify(card, 1.035, false);
            return card;
        }

        private FrameworkElement SystemCard()
        {
            var holder = new Grid { Width = 154, Height = 63, ClipToBounds = false };
            var grid = new Grid
            {
                Width = 150,
                Height = 56,
                Margin = new Thickness(4, 0, 0, 7),
                ClipToBounds = false,
                RenderTransformOrigin = new Point(.5, 1),
                RenderTransform = new ScaleTransform(1, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

            var clockStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand };
            clockText = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, LineHeight = 17, ToolTip = null };
            clockDateText = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 10.5, FontWeight = FontWeights.Normal, Foreground = Brushes.White, Opacity = .82, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 0), ToolTip = null };
            clockStack.ToolTip = CreateNameToolTip("Data e hora", clockStack);
            themedText.Add(clockText);
            themedText.Add(clockDateText);
            clockStack.MouseLeftButtonUp += delegate { DismissToolTip(clockStack); OpenCalendarPanel(); };
            clockStack.Children.Add(clockText);
            clockStack.Children.Add(clockDateText);
            Grid.SetColumn(clockStack, 0);
            grid.Children.Add(clockStack);

            var tray = new Grid { Width = 72, Height = 54, VerticalAlignment = VerticalAlignment.Center };
            tray.RowDefinitions.Add(new RowDefinition());
            tray.RowDefinitions.Add(new RowDefinition());
            tray.ColumnDefinitions.Add(new ColumnDefinition());
            tray.ColumnDefinitions.Add(new ColumnDefinition());

            batteryText = SystemGlyph("\uEBA0", "Bateria");
            // A bandeja do Windows usa a variante compacta MobBattery.
            batteryText.FontSize = 16.5;
            batteryText.Cursor = Cursors.Hand;
            batteryText.MouseLeftButtonUp += delegate { DismissToolTip(batteryText); OpenQuickSettings(batteryText); };
            volumeText = SystemGlyph("\uE995", "Clique para ativar/desativar o som");
            volumeText.MouseLeftButtonUp += delegate { DismissToolTip(volumeText); OpenQuickSettings(volumeText); };
            networkText = SystemGlyph("\uE701", "Wi-Fi");
            networkText.MouseLeftButtonUp += delegate { DismissToolTip(networkText); OpenQuickSettings(networkText); };
            trayArrowText = SystemGlyph("\uE70E", "Ícones ocultos");
            trayArrowText.MouseLeftButtonUp += delegate { OpenTrayOverflow(); };
            batteryText.ToolTip = CreateNameToolTip("Bateria", batteryText, tray);
            networkText.ToolTip = CreateNameToolTip("Wi-Fi", networkText, tray);
            volumeText.ToolTip = CreateNameToolTip("Volume", volumeText, tray);

            Grid.SetRow(batteryText, 0); Grid.SetColumn(batteryText, 0);
            Grid.SetRow(networkText, 0); Grid.SetColumn(networkText, 1);
            Grid.SetRow(volumeText, 1); Grid.SetColumn(volumeText, 0);
            Grid.SetRow(trayArrowText, 1); Grid.SetColumn(trayArrowText, 1);
            tray.Children.Add(batteryText);
            tray.Children.Add(networkText);
            tray.Children.Add(volumeText);
            tray.Children.Add(trayArrowText);
            Grid.SetColumn(tray, 1);
            grid.Children.Add(tray);
            holder.Children.Add(grid);
            AddMagnify(grid, 1.08);
            return holder;
        }

        private TextBlock SystemGlyph(string glyph, string tip)
        {
            var text = new TextBlock
            {
                Text = glyph,
                // A mesma família de glifos usada pela interface do Windows 11.
                // Mantemos o controle na Dock para não desanexar a bandeja do Explorer.
                FontFamily = windowsSystemIconFont,
                FontSize = 17.5,
                FontWeight = FontWeights.Normal,
                Foreground = darkMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                ToolTip = null,
                Cursor = Cursors.Hand
            };
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(text, TextRenderingMode.Grayscale);
            text.ToolTip = CreateNameToolTip(tip, text);
            themedText.Add(text);
            return text;
        }

        private Border Separator()
        {
            var separator = new Border { Width = 1, Height = 42, Margin = new Thickness(7, 0, 7, 8) };
            themedSeparators.Add(separator);
            wavePassives.Add(separator);
            waveLayoutDirty = true;
            separator.Background = darkMode ? new SolidColorBrush(Color.FromArgb(85, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(60, 70, 60, 50));
            return separator;
        }

        private void AddMagnify(FrameworkElement element, double scale = 1.38, bool spreadNeighbors = true)
        {
            if (spreadNeighbors)
            {
                if (!magnifyIcons.Contains(element)) magnifyIcons.Add(element);
                magnifyGrowth[element] = Math.Max(0, scale - 1);
                waveLayoutDirty = true;
            }
            if (spreadNeighbors)
            {
                element.MouseEnter += delegate
                {
                    var parent = element.Parent as UIElement;
                    if (parent != null) Panel.SetZIndex(parent, 100);
                };
                return;
            }
            element.MouseEnter += delegate
            {
                var parent = element.Parent as UIElement;
                if (parent != null) Panel.SetZIndex(parent, 100);
                if (spreadNeighbors) AnimateNeighborSpread(element, true);
                AnimateScale(element, scale, 110);
            };
            element.MouseLeave += delegate
            {
                AnimateScale(element, 1, 150);
                if (spreadNeighbors) AnimateNeighborSpread(element, false);
                var parent = element.Parent as UIElement;
                if (parent != null) Panel.SetZIndex(parent, 0);
            };
        }

        private void StartDockWave()
        {
            if (magnificationRendering) return;
            magnificationRendering = true;
            lastWaveFrame = TimeSpan.Zero;
            lastTooltipFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += RenderDockWave;
        }

        private void StopDockWave()
        {
            if (magnificationRendering)
            {
                CompositionTarget.Rendering -= RenderDockWave;
                magnificationRendering = false;
            }
            ResetDockWave();
            if (previewWindow != null)
            {
                // Os ícones retornam por uma animação de 165 ms. Mantemos a
                // prévia presa ao ícone durante esse retorno para ela não ficar
                // visualmente suspensa longe da Dock.
                previewSettleFrames = 13;
                previewSettleTimer.Stop();
                previewSettleTimer.Start();
            }
            waveLayoutDirty = true;
        }

        private void RenderDockWave(object sender, EventArgs args)
        {
            if (magnifyHost == null || !magnifyHost.IsLoaded || !magnifyHost.IsMouseOver)
            {
                StopDockWave();
                return;
            }
            var rendering = args as RenderingEventArgs;
            TimeSpan current = rendering == null ? TimeSpan.Zero : rendering.RenderingTime;
            // CompositionTarget.Rendering já acompanha a taxa real do monitor.
            // Ignoramos somente chamadas duplicadas do mesmo frame; limitar a 90 Hz
            // criava uma cadência irregular principalmente em telas de 120/144 Hz.
            if (lastWaveFrame != TimeSpan.Zero && current == lastWaveFrame) return;
            double elapsed = lastWaveFrame == TimeSpan.Zero || current <= lastWaveFrame
                ? 1.0 / 60.0
                : Math.Min(.05, (current - lastWaveFrame).TotalSeconds);
            lastWaveFrame = current;
            UpdateDockWave(magnifyHost, Mouse.GetPosition(magnifyHost).X, elapsed);
            RepositionOpenPreview();
            // Reancorar um Popup pode disparar layout nativo. Trinta atualizações
            // por segundo são suficientes para o rótulo acompanhar sem roubar
            // frames da onda dos ícones.
            if (lastTooltipFrame == TimeSpan.Zero || current <= lastTooltipFrame ||
                (current - lastTooltipFrame).TotalMilliseconds >= 32)
            {
                lastTooltipFrame = current;
                RepositionOpenDockToolTips();
            }
        }

        private void RepositionOpenDockToolTips()
        {
            // ToolTip usa um Popup nativo, que não acompanha automaticamente
            // RenderTransform/TranslateTransform. Uma alteração subpixel força
            // o Popup a recalcular a âncora sem produzir movimento visível.
            tooltipPositionNudge = !tooltipPositionNudge;
            double nudge = tooltipPositionNudge ? .001 : 0;
            foreach (FrameworkElement icon in magnifyIcons.Where(item => item.IsLoaded))
            {
                var tooltip = icon.ToolTip as ToolTip;
                if (tooltip != null && tooltip.IsOpen) tooltip.HorizontalOffset = nudge;
            }
        }

        private void RebuildWaveLayout(FrameworkElement dockRow)
        {
            magnifyIcons.RemoveAll(delegate(FrameworkElement item)
            {
                if (item.IsLoaded) return false;
                magnifyGrowth.Remove(item);
                return true;
            });
            foreach (var sep in themedSeparators)
            {
                if (sep.IsLoaded && !wavePassives.Contains(sep))
                    wavePassives.Add(sep);
            }
            wavePassives.RemoveAll(item => !item.IsLoaded);
            waveSamples.Clear();

            foreach (var icon in magnifyIcons)
            {
                var container = icon.Parent as FrameworkElement;
                if (container == null || !container.IsVisible || !icon.IsVisible) continue;
                var translate = container.RenderTransform as TranslateTransform;
                var scaleTransform = icon.RenderTransform as ScaleTransform;
                double currentScale = scaleTransform == null ? 1 : scaleTransform.ScaleX;
                double currentOffset = translate == null ? 0 : translate.X;
                if (scaleTransform != null)
                {
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scaleTransform.ScaleX = currentScale;
                    scaleTransform.ScaleY = currentScale;
                }
                if (translate == null)
                {
                    translate = new TranslateTransform();
                    container.RenderTransform = translate;
                }
                else
                {
                    translate.BeginAnimation(TranslateTransform.XProperty, null);
                    translate.X = currentOffset;
                }
                double center;
                try
                {
                    center = icon.TranslatePoint(new Point(icon.ActualWidth / 2, 0), dockRow).X;
                    center -= currentOffset;
                }
                catch { continue; }
                double growth;
                if (!magnifyGrowth.TryGetValue(icon, out growth)) growth = .38;
                waveSamples.Add(new WaveSample
                {
                    Icon = icon,
                    Container = container,
                    Center = center,
                    Scale = currentScale,
                    Growth = growth,
                    ExtraWidth = icon.ActualWidth * (currentScale - 1),
                    Influence = 0,
                    Offset = currentOffset,
                    ZIndex = Panel.GetZIndex(container)
                });
            }

            foreach (var passive in wavePassives)
            {
                if (!passive.IsVisible) continue;
                var translate = passive.RenderTransform as TranslateTransform;
                double currentOffset = translate == null ? 0 : translate.X;
                if (translate == null)
                {
                    translate = new TranslateTransform();
                    passive.RenderTransform = translate;
                }
                else
                {
                    translate.BeginAnimation(TranslateTransform.XProperty, null);
                    translate.X = currentOffset;
                }
                double center;
                try
                {
                    center = passive.TranslatePoint(new Point(passive.ActualWidth / 2, 0), dockRow).X;
                    center -= currentOffset;
                }
                catch { continue; }
                waveSamples.Add(new WaveSample
                {
                    Icon = null,
                    Container = passive,
                    Center = center,
                    Scale = 1,
                    Growth = 0,
                    ExtraWidth = 0,
                    Influence = 0,
                    Offset = currentOffset,
                    ZIndex = Panel.GetZIndex(passive)
                });
            }

            waveSamples.Sort(delegate(WaveSample left, WaveSample right) { return left.Center.CompareTo(right.Center); });
            if (waveTopContainer != null) Panel.SetZIndex(waveTopContainer, 0);
            waveTopContainer = null;
            waveLayoutDirty = false;
        }

        private void UpdateDockWave(FrameworkElement dockRow, double pointerX, double elapsed)
        {
            if (waveLayoutDirty) RebuildWaveLayout(dockRow);
            const double radius = 142;
            double scaleBlend = 1 - Math.Exp(-26 * elapsed);
            double offsetBlend = 1 - Math.Exp(-31 * elapsed);
            double totalExtra = 0;
            FrameworkElement closestContainer = null;
            double closestDistance = Double.MaxValue;

            for (int index = 0; index < waveSamples.Count; index++)
            {
                WaveSample sample = waveSamples[index];
                if (sample.Icon != null)
                {
                    double distance = Math.Abs(sample.Center - pointerX);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestContainer = sample.Container;
                    }
                    sample.Influence = distance >= radius ? 0 : (Math.Cos(Math.PI * distance / radius) + 1) / 2;
                    double targetScale = 1 + sample.Growth * sample.Influence;
                    sample.Scale += (targetScale - sample.Scale) * scaleBlend;
                    sample.ExtraWidth = sample.Icon.ActualWidth * (sample.Scale - 1);
                }
                totalExtra += sample.ExtraWidth;
                waveSamples[index] = sample;
            }

            // Panel.SetZIndex invalida a organização visual do painel. Antes ele
            // mudava para cada ícone em cada frame. Agora só troca quando o cursor
            // realmente passa para outro ícone.
            FrameworkElement nextTop = closestContainer == null || closestDistance >= radius
                ? null
                : closestContainer;
            if (!ReferenceEquals(nextTop, waveTopContainer))
            {
                if (waveTopContainer != null) Panel.SetZIndex(waveTopContainer, 0);
                if (nextTop != null) Panel.SetZIndex(nextTop, 100);
                waveTopContainer = nextTop;
            }

            double consumedExtra = 0;
            for (int index = 0; index < waveSamples.Count; index++)
            {
                WaveSample sample = waveSamples[index];
                var scaleTransform = sample.Icon == null ? null : sample.Icon.RenderTransform as ScaleTransform;
                if (sample.Icon != null && scaleTransform != null)
                {
                    if (Math.Abs(scaleTransform.ScaleX - sample.Scale) > .0001)
                    {
                        scaleTransform.ScaleX = sample.Scale;
                        scaleTransform.ScaleY = sample.Scale;
                    }
                }

                var translate = sample.Container.RenderTransform as TranslateTransform;
                if (translate == null)
                {
                    translate = new TranslateTransform();
                    sample.Container.RenderTransform = translate;
                }
                double targetOffset = consumedExtra + sample.ExtraWidth / 2 - totalExtra / 2;
                sample.Offset += (targetOffset - sample.Offset) * offsetBlend;
                if (Math.Abs(translate.X - sample.Offset) > .0001) translate.X = sample.Offset;
                consumedExtra += sample.ExtraWidth;
                waveSamples[index] = sample;
            }
        }

        private void ResetDockWave()
        {
            if (waveTopContainer != null)
            {
                Panel.SetZIndex(waveTopContainer, 0);
                waveTopContainer = null;
            }
            foreach (var icon in magnifyIcons.Where(item => item.IsLoaded))
            {
                AnimateScale(icon, 1, 165);
                var container = icon.Parent as FrameworkElement;
                if (container == null) continue;
                var translate = container.RenderTransform as TranslateTransform;
                if (translate != null)
                {
                    var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(165)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    translate.BeginAnimation(TranslateTransform.XProperty, animation);
                }
                Panel.SetZIndex(container, 0);
            }
            foreach (var passive in wavePassives.Where(item => item.IsLoaded))
            {
                var translate = passive.RenderTransform as TranslateTransform;
                if (translate == null) continue;
                var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(165)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                translate.BeginAnimation(TranslateTransform.XProperty, animation);
            }
        }

        private static ToolTip CreateNameToolTip(string name, FrameworkElement target)
        {
            return CreateNameToolTip(name, target, target);
        }

        private static ToolTip CreateNameToolTip(string name, FrameworkElement target, FrameworkElement anchor)
        {
            var tooltip = new ToolTip
            {
                Placement = PlacementMode.Custom,
                PlacementTarget = anchor,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                HasDropShadow = false
            };
            tooltip.CustomPopupPlacementCallback = delegate(Size popupSize, Size targetSize, Point offset)
            {
                return new[]
                {
                    new CustomPopupPlacement(
                        new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 8),
                        PopupPrimaryAxis.Horizontal)
                };
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var label = new TextBlock { Text = name, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = FontWeights.SemiBold };
            stack.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(245, 38, 38, 42)),
                Padding = new Thickness(13, 7, 13, 7),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = .35 },
                Child = label
            });
            stack.Children.Add(new Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(12, 0), new Point(6, 6) },
                Fill = new SolidColorBrush(Color.FromArgb(245, 38, 38, 42)),
                Width = 12,
                Height = 6,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            tooltip.Content = stack;
            tooltip.Tag = label;
            ToolTipService.SetInitialShowDelay(target, 220);
            ToolTipService.SetBetweenShowDelay(target, 40);
            ToolTipService.SetShowDuration(target, 5000);
            return tooltip;
        }

        private static void SetToolTipText(FrameworkElement target, string text)
        {
            if (target == null) return;
            var tooltip = target.ToolTip as ToolTip;
            var label = tooltip == null ? null : tooltip.Tag as TextBlock;
            if (label != null && !String.Equals(label.Text, text, StringComparison.Ordinal)) label.Text = text;
        }

        private static void DismissToolTip(FrameworkElement target)
        {
            if (target == null) return;
            var tooltip = target.ToolTip as ToolTip;
            if (tooltip != null) tooltip.IsOpen = false;
            ToolTipService.SetIsEnabled(target, false);
            var reenable = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            reenable.Tick += delegate
            {
                reenable.Stop();
                ToolTipService.SetIsEnabled(target, true);
            };
            reenable.Start();
        }

        private void AnimateNeighborSpread(FrameworkElement hovered, bool spread)
        {
            magnifyIcons.RemoveAll(item => !item.IsLoaded);
            double hoveredCenter;
            try { hoveredCenter = hovered.PointToScreen(new Point(hovered.ActualWidth / 2, 0)).X; }
            catch { return; }

            foreach (var icon in magnifyIcons)
            {
                if (icon == hovered) continue;
                double center;
                try { center = icon.PointToScreen(new Point(icon.ActualWidth / 2, 0)).X; }
                catch { continue; }

                double distance = center - hoveredCenter;
                double absolute = Math.Abs(distance);
                double offset = 0;
                if (spread && absolute < 190)
                {
                    double influence = 1 - (absolute / 190);
                    offset = Math.Sign(distance) * 25 * influence * influence;
                }

                var container = icon.Parent as FrameworkElement;
                if (container == null) continue;
                var translate = container.RenderTransform as TranslateTransform;
                if (translate == null)
                {
                    translate = new TranslateTransform();
                    container.RenderTransform = translate;
                }
                var animation = new DoubleAnimation(offset, TimeSpan.FromMilliseconds(spread ? 115 : 165))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                translate.BeginAnimation(TranslateTransform.XProperty, animation);
            }
        }

        private void AnimateScale(FrameworkElement element, double value, int milliseconds)
        {
            var transform = element.RenderTransform as ScaleTransform;
            if (transform == null) return;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private void RefreshState()
        {
            var now = DateTime.Now;
            if (dayText != null) dayText.Text = now.ToString("ddd", new CultureInfo("pt-BR")).ToUpperInvariant().Replace(".", "");
            if (dateText != null) dateText.Text = now.Day.ToString(CultureInfo.InvariantCulture);
            RefreshClock(now);
            RefreshTaskItems();
            RefreshSystemInfoAsync();
        }

        private void RefreshClock(DateTime now)
        {
            var culture = new CultureInfo("pt-BR");
            string time = now.ToString("HH:mm", culture);
            string date = now.ToString("dd/MM", culture);
            if (clockText != null && clockText.Text != time) clockText.Text = time;
            if (clockDateText != null && clockDateText.Text != date) clockDateText.Text = date;
            string fullDate = now.ToString("dddd, d 'de' MMMM 'de' yyyy", culture);
            if (fullDate.Length > 0) fullDate = Char.ToUpper(fullDate[0], culture) + fullDate.Substring(1);
            var clockStack = clockText == null ? null : clockText.Parent as FrameworkElement;
            SetToolTipText(clockStack, fullDate);
        }

        private void SampleAdaptiveContrastBackground(object ignored)
        {
            if (isClosing || dockHandle == IntPtr.Zero || dockAutoHidden || fullscreenSuppressed) return;
            if (dockTheme != "Flutuante" && dockOpacity > 80) return;
            if (Interlocked.CompareExchange(ref adaptiveContrastSampling, 1, 0) != 0) return;

            IntPtr desktopDc = IntPtr.Zero;
            try
            {
                NativeRect bounds;
                if (!GetWindowRect(dockHandle, out bounds) || bounds.Right - bounds.Left < 20) return;
                int sampleY = bounds.Top + 3;
                long luminance = 0;
                int samples = 0;
                desktopDc = GetDC(IntPtr.Zero);
                if (desktopDc == IntPtr.Zero) return;
                for (int index = 0; index < 9; index++)
                {
                    int x = bounds.Left + 10 + (int)Math.Round((bounds.Right - bounds.Left - 20) * index / 8.0);
                    uint pixel = GetPixel(desktopDc, x, sampleY);
                    if (pixel == 0xFFFFFFFF) continue;
                    byte red = (byte)(pixel & 0xFF);
                    byte green = (byte)((pixel >> 8) & 0xFF);
                    byte blue = (byte)((pixel >> 16) & 0xFF);
                    luminance += (red * 299L + green * 587L + blue * 114L) / 1000L;
                    samples++;
                }
                if (samples == 0) return;
                long threshold = adaptiveLightSurface == true ? 150 : 174;
                bool lightSurface = luminance / samples >= threshold;
                if (adaptiveLightSurface.HasValue && adaptiveLightSurface.Value == lightSurface) return;

                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing || (adaptiveLightSurface.HasValue && adaptiveLightSurface.Value == lightSurface)) return;
                    adaptiveLightSurface = lightSurface;
                    ApplyReadableSystemStyle();
                }));
            }
            catch { }
            finally
            {
                if (desktopDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, desktopDc);
                Interlocked.Exchange(ref adaptiveContrastSampling, 0);
            }
        }

        private void ApplyReadableSystemStyle()
        {
            bool darkForeground = !darkMode || adaptiveLightSurface == true;
            Brush foreground = darkForeground
                ? new SolidColorBrush(Color.FromRgb(30, 32, 36))
                : Brushes.White;
            Effect halo = darkForeground ? lightTextHalo : darkTextHalo;

            foreach (TextBlock text in themedText)
            {
                // A bateria conserva verde/laranja/vermelho para comunicar o
                // estado; o contorno escuro já a mantém visível em fundo claro.
                if (text != batteryText) text.Foreground = foreground;
                text.Effect = text == batteryText ? darkTextHalo : halo;
            }

            foreach (Border separator in themedSeparators)
                separator.Background = darkForeground
                    ? new SolidColorBrush(Color.FromArgb(105, 30, 32, 36))
                    : new SolidColorBrush(Color.FromArgb(92, 255, 255, 255));
        }

        private static string VolumeGlyphFor(float volume, bool muted)
        {
            if (muted || volume <= .001f) return "\uE74F";
            int percent = (int)Math.Round(volume * 100);
            if (percent <= 32) return "\uE993"; // uma onda
            if (percent <= 65) return "\uE994"; // duas ondas
            return "\uE995";                    // três ondas
        }

        private void MonitorVolumeBackground(object ignored)
        {
            if (isClosing || Interlocked.CompareExchange(ref volumeMonitorRunning, 1, 0) != 0) return;
            try
            {
                bool muted;
                float volume;
                if (!AudioController.TryGetState(out volume, out muted)) return;
                int percent = muted ? 0 : (int)Math.Round(volume * 100);
                string glyph = VolumeGlyphFor(volume, muted);
                if (glyph == lastVolumeGlyph && percent == lastVolumePercent && muted == lastVolumeMuted) return;
                lastVolumeGlyph = glyph;
                lastVolumePercent = percent;
                lastVolumeMuted = muted;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing || volumeText == null) return;
                    bool glyphChanged = volumeText.Text != glyph;
                    if (glyphChanged)
                    {
                        volumeText.Text = glyph;
                        volumeText.BeginAnimation(OpacityProperty, new DoubleAnimation(.58, 1, TimeSpan.FromMilliseconds(115))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                    }
                    SetToolTipText(volumeText, "Volume " + percent.ToString(CultureInfo.InvariantCulture) + "%");
                }));
            }
            catch { }
            finally { Interlocked.Exchange(ref volumeMonitorRunning, 0); }
        }

        private static BatteryVisualState ReadBatteryVisualState()
        {
            var power = Forms.SystemInformation.PowerStatus;
            int percent = power.BatteryLifePercent < 0 ? -1 : (int)Math.Round(power.BatteryLifePercent * 100);
            Forms.BatteryChargeStatus chargeStatus = power.BatteryChargeStatus;
            bool noBattery = percent < 0 || (chargeStatus & Forms.BatteryChargeStatus.NoSystemBattery) != 0;
            bool pluggedIn = power.PowerLineStatus == Forms.PowerLineStatus.Online;
            bool charging = !noBattery && ((chargeStatus & Forms.BatteryChargeStatus.Charging) != 0 || pluggedIn);
            int batteryLevel = percent < 0 ? 0 : Math.Max(0, Math.Min(10, (int)Math.Round(percent / 10.0)));
            string glyph;
            if (noBattery) glyph = charging ? "\uEB17" : "\uE996";
            else if (charging) glyph = Char.ConvertFromUtf32(0xEBAB + batteryLevel); // MobBatteryCharging0–10
            else glyph = Char.ConvertFromUtf32(0xEBA0 + batteryLevel);               // MobBattery0–10

            bool critical = !noBattery && ((chargeStatus & Forms.BatteryChargeStatus.Critical) != 0 || percent <= 10);
            bool low = !noBattery && !critical && ((chargeStatus & Forms.BatteryChargeStatus.Low) != 0 || percent <= 20);
            string label = noBattery ? "Bateria não detectada" : "Bateria " + percent + "%";
            if (charging && percent < 100) label += " - Carregando";
            else if (charging) label += " - Carregada e conectada à energia";
            else if (critical) label += " - Nível crítico";
            else if (low) label += " - Bateria fraca";
            return new BatteryVisualState { Glyph = glyph, Label = label, Critical = critical, Low = low, Charging = charging };
        }

        private void MonitorBatteryBackground(object ignored)
        {
            if (isClosing || Interlocked.CompareExchange(ref batteryMonitorRunning, 1, 0) != 0) return;
            try
            {
                BatteryVisualState state = ReadBatteryVisualState();
                if (state.Glyph == lastBatteryGlyph && state.Label == lastBatteryLabel) return;
                lastBatteryGlyph = state.Glyph;
                lastBatteryLabel = state.Label;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing || batteryText == null) return;
                    bool glyphChanged = batteryText.Text != state.Glyph;
                    if (glyphChanged)
                    {
                        batteryText.Text = state.Glyph;
                        batteryText.BeginAnimation(OpacityProperty, new DoubleAnimation(.58, 1, TimeSpan.FromMilliseconds(130))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                    }
                    batteryText.Foreground = state.Critical ? new SolidColorBrush(Color.FromRgb(255, 80, 80))
                        : state.Low ? new SolidColorBrush(Color.FromRgb(255, 184, 71))
                        : state.Charging ? new SolidColorBrush(Color.FromRgb(105, 220, 130))
                        : darkMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(45, 45, 45));
                    SetToolTipText(batteryText, state.Label);
                }));
            }
            catch { }
            finally { Interlocked.Exchange(ref batteryMonitorRunning, 0); }
        }

        private static NetworkVisualState ReadNetworkVisualState()
        {
            bool hasWifi = false;
            bool hasEthernet = false;
            bool wifiHasGateway = false;
            bool ethernetHasGateway = false;
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    bool hasGateway = false;
                    try
                    {
                        hasGateway = adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
                            gateway.Address != null &&
                            !gateway.Address.Equals(System.Net.IPAddress.Any) &&
                            !gateway.Address.Equals(System.Net.IPAddress.IPv6Any));
                    }
                    catch { }
                    switch (adapter.NetworkInterfaceType)
                    {
                        case System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211:
                            hasWifi = true;
                            wifiHasGateway = wifiHasGateway || hasGateway;
                            break;
                        case System.Net.NetworkInformation.NetworkInterfaceType.Ethernet:
                        case System.Net.NetworkInformation.NetworkInterfaceType.Ethernet3Megabit:
                        case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx:
                        case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT:
                        case System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet:
                            string identity = ((adapter.Name ?? String.Empty) + " " + (adapter.Description ?? String.Empty)).ToLowerInvariant();
                            bool virtualAdapter = identity.Contains("virtual") || identity.Contains("hyper-v") ||
                                identity.Contains("vethernet") || identity.Contains("wsl") || identity.Contains("vmware") ||
                                identity.Contains("tap-") || identity.Contains("vpn") || identity.Contains("tunnel");
                            if (!virtualAdapter)
                            {
                                hasEthernet = true;
                                ethernetHasGateway = ethernetHasGateway || hasGateway;
                            }
                            break;
                    }
                }
            }
            catch { }

            bool connected = hasWifi || hasEthernet;
            bool internet = false;
            object managerObject = null;
            try
            {
                Type managerType = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
                if (managerType != null)
                {
                    managerObject = Activator.CreateInstance(managerType);
                    dynamic manager = managerObject;
                    connected = connected || (bool)manager.IsConnected;
                    internet = (bool)manager.IsConnectedToInternet;
                }
            }
            catch { internet = connected && System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); }
            finally { if (managerObject != null && Marshal.IsComObject(managerObject)) Marshal.FinalReleaseComObject(managerObject); }

            bool useEthernet = ethernetHasGateway || (!wifiHasGateway && hasEthernet && !hasWifi);
            if (useEthernet)
                return new NetworkVisualState
                {
                    Glyph = internet ? "\uE839" : "\uEB56",
                    Label = internet ? "Ethernet - Conectado" : "Ethernet - Sem Internet"
                };
            if (hasWifi)
                return new NetworkVisualState
                {
                    Glyph = internet ? "\uE701" : "\uEB63",
                    Label = internet ? "Wi-Fi - Conectado" : "Wi-Fi - Sem Internet"
                };
            return new NetworkVisualState
            {
                Glyph = connected ? "\uE998" : "\uE774",
                Label = connected ? "Rede - Sem Internet" : "Sem conexão de rede"
            };
        }

        private void MonitorNetworkBackground(object ignored)
        {
            if (isClosing || Interlocked.CompareExchange(ref networkMonitorRunning, 1, 0) != 0) return;
            try
            {
                NetworkVisualState state = ReadNetworkVisualState();
                if (state.Glyph == lastNetworkGlyph && state.Label == lastNetworkLabel) return;
                lastNetworkGlyph = state.Glyph;
                lastNetworkLabel = state.Label;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing || networkText == null) return;
                    if (networkText.Text != state.Glyph)
                    {
                        networkText.Text = state.Glyph;
                        networkText.BeginAnimation(OpacityProperty, new DoubleAnimation(.58, 1, TimeSpan.FromMilliseconds(130))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                    }
                    SetToolTipText(networkText, state.Label);
                }));
            }
            catch { }
            finally { Interlocked.Exchange(ref networkMonitorRunning, 0); }
        }

        private void RefreshSystemInfoAsync()
        {
            if (Interlocked.CompareExchange(ref systemRefreshRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                SystemSnapshot snapshot = null;
                try { snapshot = ReadSystemSnapshot(); }
                catch { }
                if (isClosing)
                {
                    Interlocked.Exchange(ref systemRefreshRunning, 0);
                    return;
                }
                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        try { if (!isClosing && snapshot != null) ApplySystemSnapshot(snapshot); }
                        finally { Interlocked.Exchange(ref systemRefreshRunning, 0); }
                    }));
                }
                catch { Interlocked.Exchange(ref systemRefreshRunning, 0); }
            });
        }

        private SystemSnapshot ReadSystemSnapshot()
        {
            var snapshot = new SystemSnapshot
            {
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                VolumeGlyph = "\uE995"
            };

            Process[] processes = null;
            try
            {
                processes = Process.GetProcesses();
                foreach (Process process in processes)
                {
                    try { snapshot.ProcessNames.Add(process.ProcessName); }
                    catch { }
                }
            }
            finally
            {
                if (processes != null)
                    foreach (Process process in processes) process.Dispose();
            }

            BatteryVisualState battery = ReadBatteryVisualState();
            snapshot.BatteryGlyph = battery.Glyph;
            snapshot.CriticalBattery = battery.Critical;
            snapshot.LowBattery = battery.Low;
            snapshot.Charging = battery.Charging;

            bool muted;
            float volume;
            string volumeLabel = "Volume";
            if (AudioController.TryGetState(out volume, out muted))
            {
                snapshot.VolumeGlyph = VolumeGlyphFor(volume, muted);
                volumeLabel = "Volume " + (muted ? 0 : (int)Math.Round(volume * 100)) + "%";
            }
            snapshot.BatteryLabel = battery.Label;
            snapshot.VolumeLabel = volumeLabel;
            NetworkVisualState network = ReadNetworkVisualState();
            snapshot.NetworkGlyph = network.Glyph;
            snapshot.NetworkLabel = network.Label;

            if (recycleBinImage != null)
            {
                var recycleInfo = new RecycleBinInfo { Size = Marshal.SizeOf(typeof(RecycleBinInfo)) };
                if (SHQueryRecycleBin(null, ref recycleInfo) == 0)
                {
                    snapshot.RecycleFull = recycleInfo.ItemCount > 0;
                    if (!recycleBinFull.HasValue || recycleBinFull.Value != snapshot.RecycleFull.Value)
                        snapshot.RecycleSource = LoadRecycleBinIcon(snapshot.RecycleFull.Value);
                }
            }
            return snapshot;
        }

        private void ApplySystemSnapshot(SystemSnapshot snapshot)
        {
            foreach (var item in runningDots)
                item.Value.Visibility = snapshot.ProcessNames.Contains(item.Key) ? Visibility.Visible : Visibility.Hidden;

            if (batteryText.Text != snapshot.BatteryGlyph) batteryText.Text = snapshot.BatteryGlyph;
            if (volumeText.Text != snapshot.VolumeGlyph) volumeText.Text = snapshot.VolumeGlyph;
            if (!String.IsNullOrWhiteSpace(snapshot.NetworkGlyph) && networkText.Text != snapshot.NetworkGlyph)
                networkText.Text = snapshot.NetworkGlyph;
            lastVolumeGlyph = snapshot.VolumeGlyph;
            lastNetworkGlyph = snapshot.NetworkGlyph;
            lastNetworkLabel = snapshot.NetworkLabel;
            Brush batteryBrush = snapshot.CriticalBattery ? new SolidColorBrush(Color.FromRgb(255, 80, 80))
                : snapshot.LowBattery ? new SolidColorBrush(Color.FromRgb(255, 184, 71))
                : snapshot.Charging ? new SolidColorBrush(Color.FromRgb(105, 220, 130))
                : darkMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(45, 45, 45));
            var currentBrush = batteryText.Foreground as SolidColorBrush;
            var desiredBrush = batteryBrush as SolidColorBrush;
            if (currentBrush == null || desiredBrush == null || currentBrush.Color != desiredBrush.Color)
                batteryText.Foreground = batteryBrush;

            SetToolTipText(batteryText, snapshot.BatteryLabel);
            SetToolTipText(networkText, snapshot.NetworkLabel);
            SetToolTipText(volumeText, snapshot.VolumeLabel);
            SetToolTipText(trayArrowText, "Ícones ocultos");

            if (snapshot.RecycleFull.HasValue)
            {
                if (snapshot.RecycleSource != null) recycleBinImage.Source = snapshot.RecycleSource;
                recycleBinFull = snapshot.RecycleFull.Value;
            }
            ApplyReadableSystemStyle();
        }

        public void RefreshTaskItemsImmediate()
        {
            if (isClosing) return;
            nextTaskRefreshUtc = DateTime.MinValue;
            taskSignature = String.Empty;
            try
            {
                var apps = ReadTaskApps();
                if (apps != null && !isClosing) ApplyTaskItems(apps);
            }
            catch { }
        }

        private void RefreshTaskItems()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextTaskRefreshUtc) return;
            nextTaskRefreshUtc = now.AddMilliseconds(250);
            if (Interlocked.CompareExchange(ref taskRefreshRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<TaskApp> apps = null;
                try { apps = ReadTaskApps(); }
                catch { }

                if (isClosing)
                {
                    Interlocked.Exchange(ref taskRefreshRunning, 0);
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                    {
                        try
                        {
                            if (!isClosing && apps != null) ApplyTaskItems(apps);
                        }
                        finally { Interlocked.Exchange(ref taskRefreshRunning, 0); }
                    }));
                }
                catch { Interlocked.Exchange(ref taskRefreshRunning, 0); }
            });
        }

        private void ApplyTaskItems(List<TaskApp> apps)
        {
            // Durante um arraste não podemos trocar a árvore visual. Durante a
            // ampliação, porém, a atualização precisa continuar: do contrário um
            // app recém-aberto fica sem prévia até o cursor sair da Dock. O método
            // já é chamado em prioridade Background para não disputar com frames.
            if (taskDragInProgress) return;
            string signature = String.Join("|", apps.Select(a => (a.Identity ?? a.Target) + ":" +
                String.Join(",", a.WindowHandles.Select(h => h.ToInt64()).OrderBy(h => h).Select(h => h.ToString(CultureInfo.InvariantCulture)))));
            if (signature == taskSignature) return;
            taskSignature = signature;
            taskItems.Children.Clear();
            foreach (var app in apps) taskItems.Children.Add(TaskTile(app));
            waveLayoutDirty = true;
        }

        private List<TaskApp> ReadTaskApps()
        {
            var apps = new List<TaskApp>();
            var taskbarButtons = ReadTaskbarApplicationButtonNames();

            DockWorkspace activeWs = (workspaceManager != null && workspaceManager.WorkspacesEnabled)
                ? workspaceManager.ActiveWorkspace
                : null;

            bool useWindowsTaskbarPinned = (activeWs == null || activeWs.SyncWithWindows || (activeWs.Apps.Count == 0 && activeWs.Id == "default"));

            if (!useWindowsTaskbarPinned && activeWs != null)
            {
                var packaged = ReadPackagedApps();
                foreach (var wApp in activeWs.Apps)
                {
                    wApp.NormalizeExecutable();
                    TaskApp item = null;
                    if (!String.IsNullOrWhiteSpace(wApp.Target) && !String.Equals(wApp.Target, "explorer.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        item = new TaskApp
                        {
                            Name = wApp.Name,
                            Target = wApp.Target,
                            Arguments = wApp.Arguments,
                            AppUserModelId = wApp.AppUserModelId,
                            Identity = wApp.Identity,
                            IsPinned = true,
                            Icon = IconFor(wApp.Target) ?? ResolveAppIcon(wApp.Target, wApp.AppUserModelId)
                        };
                    }
                    else if (!String.IsNullOrWhiteSpace(wApp.AppUserModelId))
                    {
                        item = packaged.FirstOrDefault(p => String.Equals(p.AppUserModelId, wApp.AppUserModelId, StringComparison.OrdinalIgnoreCase));
                        if (item != null)
                        {
                            item = new TaskApp
                            {
                                Name = wApp.Name,
                                Target = item.Target,
                                Arguments = item.Arguments,
                                Identity = item.Identity,
                                AppUserModelId = item.AppUserModelId,
                                IsPinned = true,
                                Icon = IconForShellItem(item.Identity)
                            };
                        }
                    }
                    if (item == null)
                    {
                        item = new TaskApp
                        {
                            Name = wApp.Name,
                            Target = wApp.Target,
                            Arguments = wApp.Arguments,
                            AppUserModelId = wApp.AppUserModelId,
                            Identity = wApp.Identity,
                            IsPinned = true,
                            Icon = ResolveAppIcon(wApp.Target, wApp.AppUserModelId) ?? IconFor(wApp.Target)
                        };
                    }
                    if (!apps.Any(a => SameTarget(a.Target, item.Target) && String.Equals(a.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        apps.Add(item);
                    }
                }
            }
            else
            {
                string pinned = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                if (Directory.Exists(pinned))
                {
                    foreach (string link in Directory.GetFiles(pinned, "*.lnk").OrderBy(p => File.GetCreationTimeUtc(p)))
                    {
                        var item = ResolveShortcut(link);
                        if (item != null && !apps.Any(a => SameTarget(a.Target, item.Target)))
                        {
                            item.IsPinned = true;
                            apps.Add(item);
                        }
                    }
                }

                var pinnedNames = ReadPinnedButtonNames();
                if (pinnedNames.Count > 0)
                {
                    var ordered = new List<TaskApp>();
                    var packaged = ReadPackagedApps();
                    foreach (string pinnedName in pinnedNames)
                    {
                        var item = apps.FirstOrDefault(a => PinnedNameMatches(pinnedName, a));
                        if (item == null && (pinnedName.StartsWith("Explorador", StringComparison.OrdinalIgnoreCase) || pinnedName.StartsWith("File Explorer", StringComparison.OrdinalIgnoreCase)))
                        {
                            string explorerPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                            item = new TaskApp { Name = "Explorador de Arquivos", Target = explorerPath, Icon = IconFor(explorerPath) };
                        }
                        if (item == null)
                        {
                            item = packaged.FirstOrDefault(a => PinnedNameMatches(pinnedName, a));
                            if (item != null) item.Icon = IconForShellItem(item.Identity);
                        }
                        if (item != null)
                        {
                            item.IsPinned = true;
                            if (!ordered.Contains(item)) ordered.Add(item);
                        }
                    }
                    foreach (var item in apps) if (!ordered.Contains(item)) ordered.Add(item);
                    apps = ordered;
                }
            }

            foreach (IntPtr window in EnumerateTaskbarWindows())
            {
                Process process = null;
                try
                {
                    uint processId;
                    GetWindowThreadProcessId(window, out processId);
                    if (processId == 0 || processId == currentProcessId) continue;
                    process = Process.GetProcessById((int)processId);
                    string windowTitle = GetWindowTitle(window);
                    string path = process.MainModule == null ? String.Empty : process.MainModule.FileName;
                    string processName = process.ProcessName;
                    string windowAppId = GetWindowAppUserModelId(window);
                    bool isExplorerWindow = String.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                                            SameTarget(path, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));

                    var match = apps.FirstOrDefault(a =>
                    {
                        if (isExplorerWindow)
                        {
                            return String.Equals(a.Name, "Explorador de Arquivos", StringComparison.OrdinalIgnoreCase) ||
                                   String.Equals(a.Name, "File Explorer", StringComparison.OrdinalIgnoreCase) ||
                                   (SameTarget(a.Target, path) && String.IsNullOrWhiteSpace(a.Arguments) && String.IsNullOrWhiteSpace(a.AppUserModelId));
                        }

                        if (String.Equals(processName, "steamwebhelper", StringComparison.OrdinalIgnoreCase) ||
                            String.Equals(processName, "steam", StringComparison.OrdinalIgnoreCase))
                        {
                            if (String.Equals(a.Name, "Steam", StringComparison.OrdinalIgnoreCase) ||
                                String.Equals(System.IO.Path.GetFileNameWithoutExtension(a.Target), "steam", StringComparison.OrdinalIgnoreCase) ||
                                (!String.IsNullOrWhiteSpace(a.Target) && a.Target.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                return true;
                            }
                        }

                        if (!String.IsNullOrWhiteSpace(windowAppId))
                        {
                            if (String.Equals(a.AppUserModelId, windowAppId, StringComparison.OrdinalIgnoreCase) ||
                                String.Equals(a.Identity, windowAppId, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }

                        bool usesExplorerLauncher = !String.IsNullOrWhiteSpace(a.Arguments) &&
                                                    a.Arguments.IndexOf("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!usesExplorerLauncher && SameTarget(a.Target, path)) return true;

                        if (!usesExplorerLauncher && String.Equals(System.IO.Path.GetFileNameWithoutExtension(a.Target), processName, StringComparison.OrdinalIgnoreCase)) return true;

                        if (!String.IsNullOrWhiteSpace(a.Identity) &&
                            (processName.IndexOf(a.Identity, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             a.Identity.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0))
                            return true;

                        if (!String.IsNullOrWhiteSpace(a.Name) && String.Equals(a.Name, processName, StringComparison.OrdinalIgnoreCase)) return true;

                        return false;
                    });
                    if (match != null)
                    {
                        if (!match.WindowHandles.Contains(window))
                        {
                            match.WindowHandles.Add(window);
                            match.WindowTitles.Add(windowTitle);
                        }
                        if (match.WindowHandle == IntPtr.Zero) match.WindowHandle = window;
                        if (String.IsNullOrWhiteSpace(match.AppUserModelId)) match.AppUserModelId = windowAppId;
                    }
                    else if (!String.IsNullOrWhiteSpace(path))
                    {
                        bool alreadyInApps;
                        if (isExplorerWindow)
                        {
                            alreadyInApps = apps.Any(a => String.Equals(a.Name, "Explorador de Arquivos", StringComparison.OrdinalIgnoreCase) ||
                                                          String.Equals(a.Name, "File Explorer", StringComparison.OrdinalIgnoreCase) ||
                                                          (SameTarget(a.Target, path) && String.IsNullOrWhiteSpace(a.Arguments)));
                        }
                        else if (String.Equals(processName, "steamwebhelper", StringComparison.OrdinalIgnoreCase) ||
                                 String.Equals(processName, "steam", StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyInApps = apps.Any(a => String.Equals(a.Name, "Steam", StringComparison.OrdinalIgnoreCase) ||
                                                          String.Equals(System.IO.Path.GetFileNameWithoutExtension(a.Target), "steam", StringComparison.OrdinalIgnoreCase) ||
                                                          (!String.IsNullOrWhiteSpace(a.Target) && a.Target.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0));
                        }
                        else
                        {
                            alreadyInApps = apps.Any(a => SameTarget(a.Target, path) && (String.IsNullOrWhiteSpace(a.Arguments) || a.Arguments.IndexOf("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) < 0));
                        }

                        if (!alreadyInApps)
                        {
                            if (activeWs != null && activeWs.IsolateWorkspace) continue;
                            string description = String.Empty;
                            try { description = process.MainModule.FileVersionInfo.FileDescription; } catch { }
                            bool namedTaskbarButton = WindowHasTaskbarButton(windowTitle, processName, description, taskbarButtons);
                            if (taskbarButtons.Count > 0 && !namedTaskbarButton && String.IsNullOrWhiteSpace(windowAppId) && !IsExplicitTaskbarWindow(window)) continue;
                            string displayName = String.IsNullOrWhiteSpace(description) ? processName : description;
                            if (String.Equals(processName, "steamwebhelper", StringComparison.OrdinalIgnoreCase) || String.Equals(processName, "steam", StringComparison.OrdinalIgnoreCase))
                            {
                                displayName = "Steam";
                                string steamExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steam.exe");
                                if (!File.Exists(steamExe)) steamExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Steam\steam.exe");
                                if (File.Exists(steamExe)) path = steamExe;
                            }
                            else if (!String.IsNullOrWhiteSpace(windowTitle)) displayName = windowTitle;
                            var runningApp = new TaskApp { Name = displayName, Target = path, AppUserModelId = windowAppId, WindowHandle = window, Icon = IconFor(path) };
                            runningApp.WindowHandles.Add(window);
                            runningApp.WindowTitles.Add(windowTitle);
                            apps.Add(runningApp);
                        }
                    }
                }
                catch { }
                finally { if (process != null) process.Dispose(); }
            }
            // Identifica se algum aplicativo fixado está em execução em segundo plano
            try
            {
                foreach (var app in apps)
                {
                    if (app.WindowHandles.Count > 0) continue;
                    string exeName = System.IO.Path.GetFileNameWithoutExtension(app.Target);
                    if (String.IsNullOrWhiteSpace(exeName)) continue;

                    // O processo explorer.exe é o shell permanente do Windows; nunca deve acender apps como rodando em segundo plano
                    if (String.Equals(exeName, "explorer", StringComparison.OrdinalIgnoreCase)) continue;

                    bool isWallpaperEngine = String.Equals(exeName, "wallpaper32", StringComparison.OrdinalIgnoreCase) ||
                                             String.Equals(exeName, "wallpaper64", StringComparison.OrdinalIgnoreCase) ||
                                             String.Equals(exeName, "ui32", StringComparison.OrdinalIgnoreCase) ||
                                             app.Name.IndexOf("Wallpaper Engine", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isWallpaperEngine)
                    {
                        if (Process.GetProcessesByName("wallpaper32").Length > 0 ||
                            Process.GetProcessesByName("wallpaper64").Length > 0 ||
                            Process.GetProcessesByName("ui32").Length > 0)
                        {
                            app.IsRunningInBackground = true;
                        }
                    }
                    else if (String.Equals(exeName, "steam", StringComparison.OrdinalIgnoreCase) ||
                             String.Equals(app.Name, "Steam", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Process.GetProcessesByName("steam").Length > 0 || Process.GetProcessesByName("steamwebhelper").Length > 0)
                        {
                            app.IsRunningInBackground = true;
                        }
                    }
                    else
                    {
                        if (Process.GetProcessesByName(exeName).Length > 0)
                        {
                            app.IsRunningInBackground = true;
                        }
                    }
                }
            }
            catch { }

            if (activeWs == null || activeWs.Apps.Count == 0)
            {
                ApplySavedAppOrder(apps);
            }
            return apps;
        }

        private void ApplySavedAppOrder(List<TaskApp> apps)
        {
            List<string> saved;
            lock (appOrder) saved = new List<string>(appOrder);
            if (saved.Count == 0 || apps.Count < 2) return;
            var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < saved.Count; index++)
                if (!positions.ContainsKey(saved[index])) positions[saved[index]] = index;

            var ordered = apps.Select((app, index) => new { App = app, Original = index, Key = TaskAppKey(app) })
                .OrderBy(item => positions.ContainsKey(item.Key) ? positions[item.Key] : Int32.MaxValue)
                .ThenBy(item => item.Original)
                .Select(item => item.App)
                .ToList();
            apps.Clear();
            apps.AddRange(ordered);
        }

        private static bool WindowHasTaskbarButton(string windowTitle, string processName, string description, List<string> buttons)
        {
            foreach (string button in buttons)
            {
                if (!String.IsNullOrWhiteSpace(processName) && button.StartsWith(processName, StringComparison.OrdinalIgnoreCase)) return true;
                if (!String.IsNullOrWhiteSpace(description) && button.StartsWith(description, StringComparison.OrdinalIgnoreCase)) return true;
                int separator = button.IndexOf(" -", StringComparison.Ordinal);
                string appName = separator > 0 ? button.Substring(0, separator) : button;
                if (!String.IsNullOrWhiteSpace(windowTitle) &&
                    (windowTitle.StartsWith(appName, StringComparison.OrdinalIgnoreCase) || windowTitle.EndsWith(appName, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        private static List<string> ReadTaskbarApplicationButtonNames()
        {
            if (cachedTaskbarButtonNames.Count > 0 && DateTime.UtcNow < taskbarButtonNamesCacheExpiresUtc)
                return new List<string>(cachedTaskbarButtonNames);
            var names = new List<string>();
            try
            {
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar == IntPtr.Zero) return names;
                IntPtr provider = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                if (provider == IntPtr.Zero) provider = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null);
                AutomationElement root = AutomationElement.FromHandle(provider == IntPtr.Zero ? taskbar : provider);
                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants, condition);
                foreach (AutomationElement button in buttons)
                {
                    string name = button.Current.Name;
                    if (String.IsNullOrWhiteSpace(name)) continue;
                    if (name.IndexOf("fixado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("pinned", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("janela em execução", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("executando", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("running", StringComparison.OrdinalIgnoreCase) >= 0) names.Add(name);
                }
            }
            catch { }
            if (names.Count > 0)
            {
                cachedTaskbarButtonNames.Clear();
                cachedTaskbarButtonNames.AddRange(names);
                taskbarButtonNamesCacheExpiresUtc = DateTime.UtcNow.AddSeconds(2);
            }
            return names;
        }

        private static List<string> ReadPinnedButtonNames()
        {
            try
            {
                if (cachedPinnedNames.Count > 0 && DateTime.UtcNow < pinnedNamesCacheExpiresUtc)
                    return new List<string>(cachedPinnedNames);
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar == IntPtr.Zero) return new List<string>(cachedPinnedNames);
                var names = CollectPinnedButtonNames(taskbar);
                if (names.Count > 0)
                {
                    cachedPinnedNames.Clear();
                    cachedPinnedNames.AddRange(names);
                    pinnedNamesCacheExpiresUtc = DateTime.UtcNow.AddMinutes(5);
                    return names;
                }
                if (cachedPinnedNames.Count > 0)
                {
                    pinnedNamesCacheExpiresUtc = DateTime.UtcNow.AddSeconds(15);
                    return new List<string>(cachedPinnedNames);
                }
                bool wasVisible = IsWindowVisible(taskbar);
                ShowWindow(taskbar, 5);
                System.Threading.Thread.Sleep(30);
                names = CollectPinnedButtonNames(taskbar);
                if (!wasVisible) ShowWindow(taskbar, 0);
                if (names.Count > 0)
                {
                    cachedPinnedNames.Clear();
                    cachedPinnedNames.AddRange(names);
                    pinnedNamesCacheExpiresUtc = DateTime.UtcNow.AddMinutes(5);
                }
                return cachedPinnedNames.Count > 0 ? new List<string>(cachedPinnedNames) : names;
            }
            catch { return new List<string>(cachedPinnedNames); }
        }

        private void MonitorPinnedStateBackground(object ignored)
        {
            if (isClosing || Interlocked.CompareExchange(ref pinnedStateMonitorRunning, 1, 0) != 0) return;
            try
            {
                string signature = ReadPinnedStateSignature();
                if (String.IsNullOrWhiteSpace(signature)) return;
                if (String.IsNullOrWhiteSpace(pinnedStateSignature))
                {
                    pinnedStateSignature = signature;
                    return;
                }
                if (String.Equals(pinnedStateSignature, signature, StringComparison.Ordinal)) return;
                pinnedStateSignature = signature;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing) return;
                    pinnedRefreshDebounceTimer.Stop();
                    pinnedRefreshDebounceTimer.Interval = TimeSpan.FromMilliseconds(180);
                    pinnedRefreshDebounceTimer.Start();
                }));
            }
            catch { }
            finally { Interlocked.Exchange(ref pinnedStateMonitorRunning, 0); }
        }

        private static string ReadPinnedStateSignature()
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Action<byte> append = value => { hash ^= value; hash *= 1099511628211UL; };
                string pinned = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                try
                {
                    if (Directory.Exists(pinned))
                        foreach (string file in Directory.GetFiles(pinned, "*.lnk").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                        {
                            var info = new FileInfo(file);
                            foreach (byte value in Encoding.Unicode.GetBytes(info.Name)) append(value);
                            foreach (byte value in BitConverter.GetBytes(info.Length)) append(value);
                            foreach (byte value in BitConverter.GetBytes(info.LastWriteTimeUtc.Ticks)) append(value);
                        }
                }
                catch { }

                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband", false))
                    {
                        if (key != null)
                            foreach (string valueName in new[] { "Favorites", "FavoritesResolve", "FavoritesVersion" })
                            {
                                object value = key.GetValue(valueName);
                                byte[] bytes = value as byte[];
                                if (bytes != null) foreach (byte item in bytes) append(item);
                                else if (value != null) foreach (byte item in Encoding.Unicode.GetBytes(value.ToString())) append(item);
                            }
                    }
                }
                catch { }
                return hash.ToString("X16", CultureInfo.InvariantCulture);
            }
        }

        private static List<string> CollectPinnedButtonNames(IntPtr taskbar)
        {
            var names = new List<string>();
            try
            {
                IntPtr provider = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                if (provider == IntPtr.Zero) provider = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null);
                AutomationElement root = AutomationElement.FromHandle(provider == IntPtr.Zero ? taskbar : provider);
                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants, condition);
                foreach (AutomationElement button in buttons)
                {
                    string name = button.Current.Name;
                    if (!String.IsNullOrWhiteSpace(name) && (name.IndexOf("fixado", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("pinned", StringComparison.OrdinalIgnoreCase) >= 0)) names.Add(name);
                }
            }
            catch { }
            return names;
        }

        private static bool PinnedNameMatches(string taskbarName, TaskApp app)
        {
            if (String.IsNullOrWhiteSpace(taskbarName) || app == null || String.IsNullOrWhiteSpace(app.Name)) return false;
            if (taskbarName.StartsWith(app.Name, StringComparison.OrdinalIgnoreCase)) return true;
            string file = System.IO.Path.GetFileNameWithoutExtension(app.Target ?? String.Empty);
            return String.IsNullOrWhiteSpace(app.Identity) && String.Equals(file, "explorer", StringComparison.OrdinalIgnoreCase) &&
                (taskbarName.StartsWith("Explorador", StringComparison.OrdinalIgnoreCase) || taskbarName.StartsWith("File Explorer", StringComparison.OrdinalIgnoreCase));
        }

        private static List<TaskApp> ReadPackagedApps()
        {
            lock (packagedAppsCacheLock)
            {
                if (cachedPackagedApps.Count > 0 && DateTime.UtcNow < packagedAppsCacheExpiresUtc)
                    return ClonePackagedApps(cachedPackagedApps);
            }
            var apps = new List<TaskApp>();
            object shellObject = null;
            object folderObject = null;
            object itemsObject = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return apps;
                shellObject = Activator.CreateInstance(shellType);
                dynamic shell = shellObject;
                folderObject = shell.NameSpace("shell:AppsFolder");
                if (folderObject == null) return apps;
                dynamic folder = folderObject;
                itemsObject = folder.Items();
                dynamic items = itemsObject;
                int count = items.Count;
                for (int index = 0; index < count; index++)
                {
                    object itemObject = null;
                    try
                    {
                        itemObject = items.Item(index);
                        dynamic item = itemObject;
                        string name = item.Name as string;
                        string identity = item.Path as string;
                        if (!String.IsNullOrWhiteSpace(name) && !String.IsNullOrWhiteSpace(identity))
                            apps.Add(new TaskApp { Name = name, Target = "explorer.exe", Arguments = "shell:AppsFolder\\" + identity, Identity = identity, AppUserModelId = identity });
                    }
                    catch { }
                    finally { if (itemObject != null && Marshal.IsComObject(itemObject)) Marshal.FinalReleaseComObject(itemObject); }
                }
            }
            catch { }
            finally
            {
                if (itemsObject != null && Marshal.IsComObject(itemsObject)) Marshal.FinalReleaseComObject(itemsObject);
                if (folderObject != null && Marshal.IsComObject(folderObject)) Marshal.FinalReleaseComObject(folderObject);
                if (shellObject != null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
            }
            lock (packagedAppsCacheLock)
            {
                cachedPackagedApps.Clear();
                cachedPackagedApps.AddRange(ClonePackagedApps(apps));
                packagedAppsCacheExpiresUtc = DateTime.UtcNow.AddMinutes(5);
            }
            return apps;
        }

        private static List<TaskApp> ClonePackagedApps(IEnumerable<TaskApp> source)
        {
            return source.Select(app => new TaskApp
            {
                Name = app.Name,
                Target = app.Target,
                Arguments = app.Arguments,
                Identity = app.Identity,
                AppUserModelId = app.AppUserModelId,
                Icon = app.Icon
            }).ToList();
        }

        private static List<IntPtr> EnumerateTaskbarWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (IsTaskbarWindow(window) && (!String.IsNullOrWhiteSpace(GetWindowTitle(window)) || IsExplicitTaskbarWindow(window))) windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static string GetWindowTitle(IntPtr window)
        {
            int length = GetWindowTextLength(window);
            if (length <= 0) return String.Empty;
            var text = new StringBuilder(length + 1);
            GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        private static bool IsTaskbarWindow(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindowVisible(window)) return false;

            int cloaked = 0;
            int size = Marshal.SizeOf(typeof(int));
            if (DwmGetWindowAttribute(window, 14, out cloaked, size) == 0 && cloaked != 0) return false;

            long extendedStyle = GetWindowLong(window, -20);
            const long ToolWindow = 0x00000080L;
            const long AppWindow = 0x00040000L;
            bool explicitlyOnTaskbar = (extendedStyle & AppWindow) != 0;
            if ((extendedStyle & ToolWindow) != 0 && !explicitlyOnTaskbar) return false;
            if (GetWindow(window, 4) != IntPtr.Zero && !explicitlyOnTaskbar) return false;

            // Janelas filhas de outras janelas não aparecem na barra
            if (GetAncestor(window, 2) != window && !explicitlyOnTaskbar) return false;

            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            string cls = className.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" || cls == "DesktopBackgroundClass" ||
                cls == "DummyDwmWallpaperWindow" || cls == "FolderViewHost")
                return false;

            // Ignora renderizadores em segundo plano do Wallpaper Engine
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid != 0)
            {
                try
                {
                    using (var proc = Process.GetProcessById((int)pid))
                    {
                        string pName = proc.ProcessName;
                        if (String.Equals(pName, "wallpaper32", StringComparison.OrdinalIgnoreCase) ||
                            String.Equals(pName, "wallpaper64", StringComparison.OrdinalIgnoreCase) ||
                            String.Equals(pName, "webwallpaper32", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
                catch { }
            }

            return true;
        }

        private static bool IsExplicitTaskbarWindow(IntPtr window)
        {
            const long AppWindow = 0x00040000L;
            return window != IntPtr.Zero && (GetWindowLong(window, -20) & AppWindow) != 0;
        }

        private static string GetWindowAppUserModelId(IntPtr window)
        {
            IPropertyStore store = null;
            var value = new PropVariant();
            try
            {
                Guid storeId = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(window, ref storeId, out store) != 0 || store == null) return String.Empty;
                var key = new PropertyKey
                {
                    FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                    PropertyId = 5
                };
                if (store.GetValue(ref key, out value) != 0) return String.Empty;
                if (value.Type == 31 && value.PointerValue != IntPtr.Zero)
                    return Marshal.PtrToStringUni(value.PointerValue) ?? String.Empty;
                if (value.Type == 8 && value.PointerValue != IntPtr.Zero)
                    return Marshal.PtrToStringBSTR(value.PointerValue) ?? String.Empty;
            }
            catch { }
            finally
            {
                PropVariantClear(ref value);
                if (store != null && Marshal.IsComObject(store)) Marshal.FinalReleaseComObject(store);
            }
            return String.Empty;
        }

        private static TaskApp ResolveShortcut(string link)
        {
            object shell = null;
            object shortcutObject = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                shell = Activator.CreateInstance(type);
                dynamic dynamicShell = shell;
                shortcutObject = dynamicShell.CreateShortcut(link);
                dynamic shortcut = shortcutObject;
                string target = Environment.ExpandEnvironmentVariables((string)shortcut.TargetPath);
                string arguments = (string)shortcut.Arguments;
                string fileName = System.IO.Path.GetFileNameWithoutExtension(link);
                if (String.IsNullOrWhiteSpace(target))
                {
                    if (String.Equals(fileName, "File Explorer", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(fileName, "Explorador de Arquivos", StringComparison.OrdinalIgnoreCase))
                    {
                        target = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    }
                    else
                    {
                        return null;
                    }
                }
                return new TaskApp { Name = fileName, Target = target, Arguments = arguments, Icon = IconFor(target) };
            }
            catch { return null; }
            finally
            {
                if (shortcutObject != null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static ImageSource IconFor(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string cacheKey = "file:" + path;
            lock (iconCacheLock)
            {
                ImageSource cached;
                if (iconCache.TryGetValue(cacheKey, out cached)) return cached;
            }
            try
            {
                using (Drawing.Icon icon = Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(40, 40));
                    source.Freeze();
                    lock (iconCacheLock) iconCache[cacheKey] = source;
                    return source;
                }
            }
            catch { return null; }
        }

        private static bool IsMostlyLightIcon(ImageSource source)
        {
            BitmapSource bitmap = source as BitmapSource;
            if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return false;
            try
            {
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);
                long luminance = 0;
                int visible = 0;
                int stepX = Math.Max(1, width / 16);
                int stepY = Math.Max(1, height / 16);
                for (int y = 0; y < height; y += stepY)
                {
                    for (int x = 0; x < width; x += stepX)
                    {
                        int offset = y * stride + x * 4;
                        byte alpha = pixels[offset + 3];
                        if (alpha < 40) continue;
                        byte blue = pixels[offset];
                        byte green = pixels[offset + 1];
                        byte red = pixels[offset + 2];
                        luminance += (red * 299L + green * 587L + blue * 114L) / 1000L;
                        visible++;
                    }
                }
                return visible >= 8 && luminance / visible >= 205;
            }
            catch { return false; }
        }

        internal static Color? ExtractAccentColor(ImageSource source)
        {
            BitmapSource bitmap = source as BitmapSource;
            if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return null;
            try
            {
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                int stepX = Math.Max(1, width / 32);
                int stepY = Math.Max(1, height / 32);

                double[] binWeights = new double[4096];
                long[] binR = new long[4096];
                long[] binG = new long[4096];
                long[] binB = new long[4096];

                double totalColoredWeight = 0;
                double totalWeight = 0;

                for (int y = 0; y < height; y += stepY)
                {
                    for (int x = 0; x < width; x += stepX)
                    {
                        int offset = y * stride + x * 4;
                        byte alpha = pixels[offset + 3];
                        if (alpha < 50) continue;

                        byte b = pixels[offset];
                        byte g = pixels[offset + 1];
                        byte r = pixels[offset + 2];

                        int max = Math.Max(r, Math.Max(g, b));
                        int min = Math.Min(r, Math.Min(g, b));
                        int delta = max - min;

                        bool isGray = delta < 20;
                        bool isTooDark = max < 35;
                        bool isTooBright = min > 235;

                        double weight;
                        if (isGray || isTooDark || isTooBright)
                        {
                            weight = isGray ? 0.2 : 0.05;
                        }
                        else
                        {
                            double sat = (double)delta / max;
                            weight = 1.0 + (sat * 4.0);
                            if (max >= 70 && min <= 200) weight *= 1.4;
                            totalColoredWeight += weight;
                        }

                        totalWeight += weight;
                        int bin = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                        binWeights[bin] += weight;
                        binR[bin] += (long)(r * weight);
                        binG[bin] += (long)(g * weight);
                        binB[bin] += (long)(b * weight);
                    }
                }

                if (totalWeight < 1.0) return null;

                bool preferColored = totalColoredWeight >= 4.0;
                int bestBin = -1;
                double bestWeight = -1;

                for (int bin = 0; bin < 4096; bin++)
                {
                    double w = binWeights[bin];
                    if (w <= 0.001) continue;

                    int rBin = (bin >> 8) & 0xF;
                    int gBin = (bin >> 4) & 0xF;
                    int bBin = bin & 0xF;

                    int maxBin = Math.Max(rBin, Math.Max(gBin, bBin));
                    int minBin = Math.Min(rBin, Math.Min(gBin, bBin));
                    bool isColoredBin = (maxBin - minBin) >= 2;

                    if (preferColored && !isColoredBin) continue;

                    if (w > bestWeight)
                    {
                        bestWeight = w;
                        bestBin = bin;
                    }
                }

                if (bestBin == -1)
                {
                    for (int bin = 0; bin < 4096; bin++)
                    {
                        if (binWeights[bin] > bestWeight)
                        {
                            bestWeight = binWeights[bin];
                            bestBin = bin;
                        }
                    }
                }

                if (bestBin == -1 || bestWeight <= 0.001) return null;

                byte finalR = (byte)Math.Min(255, Math.Max(0, binR[bestBin] / bestWeight));
                byte finalG = (byte)Math.Min(255, Math.Max(0, binG[bestBin] / bestWeight));
                byte finalB = (byte)Math.Min(255, Math.Max(0, binB[bestBin] / bestWeight));

                return Color.FromRgb(finalR, finalG, finalB);
            }
            catch { return null; }
        }

        private static ImageSource IconForShellItem(string identity)
        {
            return IconForShellPath("shell:AppsFolder\\" + identity);
        }

        private static ImageSource IconForShellPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string cacheKey = "shell:" + path;
            lock (iconCacheLock)
            {
                ImageSource cached;
                if (iconCache.TryGetValue(cacheKey, out cached)) return cached;
            }
            IShellItemImageFactory factory = null;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) != 0 || factory == null) return null;
                var size = new ShellSize { Width = 48, Height = 48 };
                if (factory.GetImage(size, 0x105, out bitmap) != 0 || bitmap == IntPtr.Zero) return null;
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(40, 40));
                source.Freeze();
                lock (iconCacheLock) iconCache[cacheKey] = source;
                return source;
            }
            catch { return null; }
            finally
            {
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (factory != null && Marshal.IsComObject(factory)) Marshal.FinalReleaseComObject(factory);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShellSize { public int Width, Height; }

        [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(ShellSize size, int flags, out IntPtr bitmap);
        }

        internal static bool SameTarget(string left, string right)
        {
            if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right)) return false;
            try { return String.Equals(System.IO.Path.GetFullPath(left), System.IO.Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); } catch { return String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        }

        internal static TaskApp ResolveShortcutPublic(string link) { return ResolveShortcut(link); }
        internal static List<TaskApp> ReadPackagedAppsPublic() { return ReadPackagedApps(); }

        public static ImageSource ResolveAppIcon(string target, string appUserModelId)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(appUserModelId))
                {
                    var img = IconForShellItem(appUserModelId);
                    if (img != null) return img;
                }
                if (!String.IsNullOrWhiteSpace(target))
                {
                    var img = IconFor(target);
                    if (img != null) return img;
                }
            }
            catch { }
            return null;
        }

        internal List<WorkspaceAppItem> GetInitialPinnedForWorkspace()
        {
            var list = new List<WorkspaceAppItem>();
            try
            {
                string pinned = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                var shortcutApps = new List<TaskApp>();
                if (Directory.Exists(pinned))
                {
                    foreach (string link in Directory.GetFiles(pinned, "*.lnk").OrderBy(p => File.GetCreationTimeUtc(p)))
                    {
                        var item = ResolveShortcut(link);
                        if (item != null && !shortcutApps.Any(a => SameTarget(a.Target, item.Target)))
                        {
                            shortcutApps.Add(item);
                        }
                    }
                }

                var pinnedNames = ReadPinnedButtonNames();
                var packaged = ReadPackagedApps();
                var orderedApps = new List<TaskApp>();

                if (pinnedNames.Count > 0)
                {
                    foreach (string pinnedName in pinnedNames)
                    {
                        var item = shortcutApps.FirstOrDefault(a => PinnedNameMatches(pinnedName, a));
                        if (item == null && (pinnedName.StartsWith("Explorador", StringComparison.OrdinalIgnoreCase) || pinnedName.StartsWith("File Explorer", StringComparison.OrdinalIgnoreCase)))
                        {
                            string explorerPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                            item = new TaskApp { Name = "Explorador de Arquivos", Target = explorerPath };
                        }
                        if (item == null)
                        {
                            item = packaged.FirstOrDefault(a => PinnedNameMatches(pinnedName, a));
                        }
                        if (item != null && !orderedApps.Any(a => SameTarget(a.Target, item.Target) && String.Equals(a.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            orderedApps.Add(item);
                        }
                    }
                }
                foreach (var item in shortcutApps)
                {
                    if (!orderedApps.Any(a => SameTarget(a.Target, item.Target))) orderedApps.Add(item);
                }

                foreach (var app in orderedApps)
                {
                    list.Add(new WorkspaceAppItem
                    {
                        Name = app.Name,
                        Target = app.Target,
                        Arguments = app.Arguments,
                        Identity = app.Identity,
                        AppUserModelId = app.AppUserModelId
                    });
                }
            }
            catch { }
            return list;
        }

        private void OpenTask(TaskApp app)
        {
            if (app.WindowHandles.Count > 0)
            {
                IntPtr foreground = foregroundBeforeDockClick != IntPtr.Zero ? foregroundBeforeDockClick : lastExternalForegroundWindow;
                foregroundBeforeDockClick = IntPtr.Zero;
                IntPtr activeTarget = MatchActiveAppWindow(app, foreground);

                if (app.WindowHandles.Count == 1)
                {
                    IntPtr target = app.WindowHandles[0];
                    if (activeTarget == target && !WindowCommands.IsMinimized(target))
                    {
                        WindowCommands.Minimize(target);
                    }
                    else
                    {
                        ActivateWindow(target);
                    }
                    return;
                }

                // Múltiplas janelas:
                // Se a janela em foco pertence ao app, alterna para a próxima janela do grupo
                if (activeTarget != IntPtr.Zero && !WindowCommands.IsMinimized(activeTarget))
                {
                    int currentIndex = app.WindowHandles.IndexOf(activeTarget);
                    if (currentIndex >= 0 && app.WindowHandles.Count > 1)
                    {
                        int nextIndex = (currentIndex + 1) % app.WindowHandles.Count;
                        ActivateWindow(app.WindowHandles[nextIndex]);
                        return;
                    }
                }

                // Nenhuma janela do app está em foco ativo: ativa a janela mais recente
                IntPtr mostRecent = app.WindowHandles
                    .OrderByDescending(h => windowActivationTicks.ContainsKey(h) ? windowActivationTicks[h] : 0)
                    .FirstOrDefault();
                if (mostRecent == IntPtr.Zero) mostRecent = app.WindowHandles[0];
                ActivateWindow(mostRecent);
                return;
            }
            if (app.IsRunningInBackground)
            {
                string exeName = System.IO.Path.GetFileNameWithoutExtension(app.Target);
                bool isWallpaperEngine = String.Equals(exeName, "wallpaper32", StringComparison.OrdinalIgnoreCase) ||
                                         String.Equals(exeName, "wallpaper64", StringComparison.OrdinalIgnoreCase) ||
                                         String.Equals(exeName, "ui32", StringComparison.OrdinalIgnoreCase) ||
                                         app.Name.IndexOf("Wallpaper Engine", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isWallpaperEngine)
                {
                    string dir = System.IO.Path.GetDirectoryName(app.Target);
                    string uiPath = System.IO.Path.Combine(dir ?? String.Empty, "ui32.exe");
                    if (File.Exists(uiPath))
                    {
                        Launch(uiPath, "-control open");
                        return;
                    }
                }
            }
            Launch(app.Target, app.Arguments);
        }

        private void MinimizeTask(TaskApp app)
        {
            if (app == null || app.WindowHandles.Count == 0) return;
            IntPtr foreground = foregroundBeforeDockClick != IntPtr.Zero ? foregroundBeforeDockClick : lastExternalForegroundWindow;
            foregroundBeforeDockClick = IntPtr.Zero;
            IntPtr target = MatchActiveAppWindow(app, foreground);
            if (target == IntPtr.Zero) target = app.WindowHandles.FirstOrDefault(handle => !WindowCommands.IsMinimized(handle));
            if (target == IntPtr.Zero) target = app.WindowHandles[0];
            WindowCommands.Minimize(target);
        }

        private void ToggleTaskGroupMinimized(TaskApp app)
        {
            if (app == null || app.WindowHandles.Count == 0) return;
            bool anyVisible = app.WindowHandles.Any(handle => WindowCommands.IsAlive(handle) && !WindowCommands.IsMinimized(handle));
            if (anyVisible)
            {
                foreach (IntPtr handle in app.WindowHandles)
                    WindowCommands.Minimize(handle);
            }
            else
            {
                var ordered = app.WindowHandles
                    .OrderBy(h => windowActivationTicks.ContainsKey(h) ? windowActivationTicks[h] : 0);
                foreach (IntPtr handle in ordered)
                    ActivateWindow(handle);
            }
            ScheduleTaskRefreshSoon();
        }

        private IntPtr MatchActiveAppWindow(TaskApp app, IntPtr candidate)
        {
            if (app == null || candidate == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = GetAncestor(candidate, 3); // GA_ROOTOWNER
            foreach (IntPtr handle in app.WindowHandles)
                if (handle == candidate || handle == root || GetAncestor(handle, 3) == root) return handle;

            uint candidateProcess;
            GetWindowThreadProcessId(candidate, out candidateProcess);
            if (candidateProcess == 0 || candidateProcess == currentProcessId) return IntPtr.Zero;
            foreach (IntPtr handle in app.WindowHandles)
            {
                uint processId;
                GetWindowThreadProcessId(handle, out processId);
                if (processId == candidateProcess) return handle;
            }
            return IntPtr.Zero;
        }

        private void StartForegroundTracking()
        {
            ReleaseForegroundTracking();
            foregroundEventDelegate = OnForegroundWindowChanged;
            foregroundEventHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, foregroundEventDelegate, 0, 0, 0x0002);
            OnForegroundWindowChanged(IntPtr.Zero, 0x0003, GetForegroundWindow(), 0, 0, 0, 0);
        }

        private void OnForegroundWindowChanged(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (window == IntPtr.Zero) return;
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == 0 || processId == currentProcessId) return;
            if (IsStartMenuWindow(window))
            {
                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
                    {
                        ActivateDockForExternalStart(window);
                    }));
                }
                catch { }
                return;
            }
            lastExternalForegroundWindow = window;
            windowActivationTicks[window] = Environment.TickCount;
            int now = Environment.TickCount;
            if (unchecked(now - lastForegroundTaskRefreshTick) < 100) return;
            lastForegroundTaskRefreshTick = now;
            nextTaskRefreshUtc = DateTime.MinValue;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    RefreshTaskItems();
                    RefreshFullscreenState();
                }));
            }
            catch { }
        }

        private void ReleaseForegroundTracking()
        {
            if (foregroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(foregroundEventHook);
                foregroundEventHook = IntPtr.Zero;
            }
            foregroundEventDelegate = null;
        }

        private void ActivateWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            windowActivationTicks[window] = Environment.TickCount;
            WindowCommands.Activate(window);
        }

        private void ScheduleAutoHide()
        {
            autoHideTimer.Stop();
            if (autoHideEnabled && !fullscreenSuppressed && !startMenuActive && !quickSettingsActive && !IsAnyContextMenuOpen()) autoHideTimer.Start();
        }

        private void PollDockRevealEdge()
        {
            if (!autoHideEnabled || fullscreenSuppressed || startMenuActive || isClosing || IsAnyContextMenuOpen()) return;
            CursorPoint cursor;
            if (!GetCursorPos(out cursor)) return;
            Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point(cursor.X, cursor.Y));
            if (!screen.Primary)
            {
                if (edgeRevealPending)
                {
                    edgeRevealPending = false;
                    edgeRevealTimer.Stop();
                    ScheduleAutoHide();
                }
                return;
            }
            int revealLine = taskbarHidden ? screen.Bounds.Bottom : screen.WorkingArea.Bottom;
            if (dockAutoHidden)
            {
                if (cursor.Y < revealLine - 3 || cursor.Y > revealLine + 2) return;
                ShowDockAnimated();
                edgeRevealPending = true;
                // ShowDockAnimated interrompe o polling normal; durante uma
                // revelação sem MouseEnter continuamos apenas até o cursor sair
                // da borda ou entrar efetivamente na Dock.
                edgeRevealTimer.Start();
                return;
            }
            if (!edgeRevealPending) return;
            if (IsMouseOver)
            {
                edgeRevealPending = false;
                edgeRevealTimer.Stop();
                return;
            }
            if (cursor.Y < revealLine - 9 || cursor.Y > revealLine + 3)
            {
                edgeRevealPending = false;
                edgeRevealTimer.Stop();
                ScheduleAutoHide();
            }
        }

        private double DockBaseBottom()
        {
            if (appBarRegistered && !appBarArea.IsEmpty) return appBarArea.Bottom;
            var area = taskbarHidden ? new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight) : SystemParameters.WorkArea;
            return area.Bottom;
        }

        private double DockBottom()
        {
            return DockBaseBottom() - dockBottomGap;
        }

        private double HiddenDockTop()
        {
            // Mantém uma pequena faixa da borda real do dockShell visível. Deixar
            // apenas o topo da Window exposto não funciona porque essa área é
            // totalmente transparente e o Windows a trata como click-through.
            double shellHeight = dockShell == null || dockShell.ActualHeight <= 0 ? 72 : dockShell.ActualHeight;
            double shellBottomMargin = dockShell == null ? 5 : dockShell.Margin.Bottom;
            double shellTopInsideWindow = Math.Max(0, ActualHeight - shellBottomMargin - shellHeight);
            // Quando oculta, a faixa de ativação continua na borda física da
            // tela; o espaçamento configurado vale somente para a Dock aberta.
            return DockBaseBottom() - shellTopInsideWindow - 7;
        }

        private double RevealedDockTop()
        {
            double shellBottomMargin = dockShell == null ? 5 : dockShell.Margin.Bottom;
            return DockBottom() - ActualHeight + shellBottomMargin;
        }

        private void ShowDockAnimated()
        {
            if (fullscreenSuppressed || !IsLoaded) return;
            edgeRevealTimer.Stop();
            autoHideTimer.Stop();
            double target = RevealedDockTop();
            if (!dockAutoHidden && Math.Abs(Top - target) < .5) return;
            // Uma Window quase toda fora da tela pode ser temporariamente
            // desacelerada pelo DWM. Trazemos a janela de volta imediatamente e
            // animamos somente o conteúdo, evitando atraso no primeiro quadro.
            BeginAnimation(TopProperty, null);
            Top = target;
            AnimateDockRevealElement(dockShell);
            AnimateDockRevealElement(magnifyHost);
            dockAutoHidden = false;
        }

        private static void AnimateDockRevealElement(FrameworkElement element)
        {
            if (element == null) return;
            var translate = element.RenderTransform as TranslateTransform;
            if (translate == null)
            {
                translate = new TranslateTransform();
                element.RenderTransform = translate;
            }
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = 0;
            var animation = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(115))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            translate.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        private void HideDockAnimated()
        {
            if (fullscreenSuppressed || dockAutoHidden || !IsLoaded || IsAnyContextMenuOpen()) return;
            double target = HiddenDockTop();
            double start = Top;
            BeginAnimation(TopProperty, null);
            Top = target;
            var animation = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(220))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            BeginAnimation(TopProperty, animation);
            dockAutoHidden = true;
            edgeRevealPending = false;
            if (autoHideEnabled) edgeRevealTimer.Start();
            HidePreview();
        }

        private void RefreshFullscreenState()
        {
            bool fullscreen = IsForegroundWindowFullscreen();
            if (fullscreen == fullscreenSuppressed) return;
            fullscreenSuppressed = fullscreen;
            autoHideTimer.Stop();
            if (fullscreen)
            {
                edgeRevealTimer.Stop();
                HidePreview();
                RemoveAppBar();
                Visibility = Visibility.Hidden;
            }
            else
            {
                Visibility = Visibility.Visible;
                UpdateAppBarRegistration();
                dockAutoHidden = false;
                SchedulePositionDock();
                if (autoHideEnabled) ScheduleAutoHide();
            }
        }

        private bool IsForegroundWindowFullscreen()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == dockHandle || IsIconic(foreground)) return false;
            var className = new StringBuilder(128);
            GetClassName(foreground, className, className.Capacity);
            string windowClass = className.ToString();
            if (windowClass == "Shell_TrayWnd" || windowClass == "Progman" || windowClass == "WorkerW") return false;
            // Aplicativos WinUI usam moldura personalizada e podem preencher o
            // monitor sem serem fullscreen exclusivo (ChatGPT, Settings etc.).
            if (windowClass == "WinUIDesktopWin32WindowClass" || windowClass == "ApplicationFrameWindow") return false;
            // IsZoomed é o sinal nativo de uma janela apenas maximizada. Não
            // exigimos WS_CAPTION porque aplicativos modernos removem a legenda.
            if (IsZoomed(foreground)) return false;
            try
            {
                uint foregroundProcessId;
                GetWindowThreadProcessId(foreground, out foregroundProcessId);
                using (Process foregroundOwner = Process.GetProcessById((int)foregroundProcessId))
                {
                    string foregroundProcess = foregroundOwner.ProcessName;
                    if (foregroundProcess.IndexOf("ChatGPT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        foregroundProcess.IndexOf("OpenAI", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                }
            }
            catch { }

            NativeRect windowBounds;
            if (!GetWindowRect(foreground, out windowBounds)) return false;
            IntPtr monitor = MonitorFromWindow(foreground, 2);
            var monitorInfo = new NativeMonitorInfo { Size = Marshal.SizeOf(typeof(NativeMonitorInfo)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo)) return false;
            const int tolerance = 2;
            bool coversMonitor = windowBounds.Left <= monitorInfo.Monitor.Left + tolerance &&
                                 windowBounds.Top <= monitorInfo.Monitor.Top + tolerance &&
                                 windowBounds.Right >= monitorInfo.Monitor.Right - tolerance &&
                                 windowBounds.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
            if (!coversMonitor) return false;

            return true;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
        {
            if (args.Mode == PowerModes.Resume) ScheduleShellRecovery();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs args)
        {
            ScheduleShellRecovery();
        }

        private void ScheduleShellRecovery()
        {
            if (isClosing) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (isClosing) return;
                shellRecoveryTimer.Stop();
                shellRecoveryAttempts = 0;
                RestoreShellStateAfterResume(true);
                shellRecoveryTimer.Start();
            }));
        }

        private void RestoreShellStateAfterResume(bool rebuildAppBar)
        {
            if (isClosing) return;
            UpdateTaskbarVisibilityGuard();
            if (taskbarHidden) ApplyCompleteTaskbarHide();
            else SetTaskbarVisible(true);
            if (rebuildAppBar && appBarRegistered) RemoveAppBar();
            UpdateAppBarRegistration();
            if (dockHandle != IntPtr.Zero)
                SetWindowPos(dockHandle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            SchedulePositionDock();
        }

        private void InitializeNativeDock()
        {
            dockHandle = new WindowInteropHelper(this).Handle;
            dockSource = HwndSource.FromHwnd(dockHandle);
            if (dockSource != null) dockSource.AddHook(DockWindowProcedure);
            StartForegroundTracking();
            shellHookMessage = RegisterWindowMessage("SHELLHOOK");
            RegisterShellHookWindow(dockHandle);
            appBarCallbackMessage = RegisterWindowMessage("BMFlow.AppBar." + currentProcessId);
            UpdateAppBarRegistration();
            RegisterWorkspaceHotKeys();
        }

        private void TriggerFastTaskRefresh()
        {
            if (isClosing) return;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                {
                    if (isClosing) return;
                    nextTaskRefreshUtc = DateTime.MinValue;
                    if (fastTaskRefreshTimer == null)
                    {
                        fastTaskRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                        fastTaskRefreshTimer.Tick += delegate
                        {
                            fastTaskRefreshTimer.Stop();
                            nextTaskRefreshUtc = DateTime.MinValue;
                            RefreshTaskItems();
                        };
                    }
                    fastTaskRefreshTimer.Stop();
                    fastTaskRefreshTimer.Start();
                }));
            }
            catch { }
        }

        private IntPtr DockWindowProcedure(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int PowerBroadcast = 0x0218;
            const int ResumeSuspend = 0x0007;
            const int ResumeAutomatic = 0x0012;
            const int MouseActivate = 0x0021;
            const int MouseActivateNoActivate = 3;
            const int WM_HOTKEY = 0x0312;

            if (shellHookMessage != 0 && (uint)message == shellHookMessage)
            {
                int code = wParam.ToInt32();
                if (code == 1 || code == 2 || code == 4 || code == 6 || code == 13 || code == 14 || code == 0x8004 || (code & 0x7FFF) == 4)
                {
                    TriggerFastTaskRefresh();
                }
            }

            if (message == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id >= 0x9100 && id <= 0x9108)
                {
                    SwitchToWorkspace(id - 0x9100);
                    handled = true;
                    return IntPtr.Zero;
                }
                if (id >= 0x9200 && id <= 0x9208)
                {
                    SwitchToWorkspace(id - 0x9200);
                    handled = true;
                    return IntPtr.Zero;
                }
            }

            if (message == MouseActivate)
            {
                // Recebe o clique sem tomar o foco do aplicativo. Isso permite
                // que um segundo clique no ícone minimize a janela ativa.
                IntPtr candidate = GetForegroundWindow();
                uint candidateProcess;
                GetWindowThreadProcessId(candidate, out candidateProcess);
                foregroundBeforeDockClick = candidateProcess == currentProcessId
                    ? lastExternalForegroundWindow
                    : candidate;
                handled = true;
                return new IntPtr(MouseActivateNoActivate);
            }
            if (message == PowerBroadcast && (wParam.ToInt32() == ResumeSuspend || wParam.ToInt32() == ResumeAutomatic))
                ScheduleShellRecovery();
            if ((uint)message == appBarCallbackMessage && wParam.ToInt32() == 1)
            {
                Dispatcher.BeginInvoke(new Action(delegate { SetAppBarPosition(); }));
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void UpdateAppBarRegistration()
        {
            if (dockHandle == IntPtr.Zero) return;
            // No modo auto-ocultar a Dock se comporta como a barra do Windows:
            // janelas usam a tela inteira e a Dock revelada aparece por cima.
            if ((!keepWindowsOffDock && !startMenuReservationActive) || fullscreenSuppressed ||
                (autoHideEnabled && !startMenuReservationActive))
            {
                RemoveAppBar();
                return;
            }
            if (!appBarRegistered)
            {
                var registration = new AppBarData
                {
                    Size = Marshal.SizeOf(typeof(AppBarData)),
                    Window = dockHandle,
                    CallbackMessage = appBarCallbackMessage
                };
                appBarRegistered = SHAppBarMessage(0, ref registration) != UIntPtr.Zero;
            }
            if (appBarRegistered) SetAppBarPosition();
        }

        private void SetAppBarPosition()
        {
            if (!appBarRegistered || fullscreenSuppressed) return;
            var screen = Forms.Screen.PrimaryScreen.Bounds;
            double dpiY = 1;
            var presentation = PresentationSource.FromVisual(this);
            if (presentation != null && presentation.CompositionTarget != null)
                dpiY = presentation.CompositionTarget.TransformToDevice.M22;
            double visualHeight = dockShell == null || dockShell.ActualHeight <= 0 ? 72 : dockShell.ActualHeight;
            int requestedHeight = Math.Max(72, (int)Math.Round((visualHeight + dockBottomGap + 6) * dpiY));
            var data = new AppBarData
            {
                Size = Marshal.SizeOf(typeof(AppBarData)),
                Window = dockHandle,
                Edge = 3,
                Rectangle = new NativeRect { Left = screen.Left, Right = screen.Right, Bottom = screen.Bottom, Top = screen.Bottom - requestedHeight }
            };
            SHAppBarMessage(2, ref data);
            data.Rectangle.Top = data.Rectangle.Bottom - requestedHeight;
            SHAppBarMessage(3, ref data);

            Point topLeft = new Point(data.Rectangle.Left, data.Rectangle.Top);
            Point bottomRight = new Point(data.Rectangle.Right, data.Rectangle.Bottom);
            if (presentation != null && presentation.CompositionTarget != null)
            {
                topLeft = presentation.CompositionTarget.TransformFromDevice.Transform(topLeft);
                bottomRight = presentation.CompositionTarget.TransformFromDevice.Transform(bottomRight);
            }
            appBarArea = new Rect(topLeft, bottomRight);
            SchedulePositionDock();
        }

        private void RemoveAppBar()
        {
            if (!appBarRegistered || dockHandle == IntPtr.Zero) return;
            var data = new AppBarData { Size = Marshal.SizeOf(typeof(AppBarData)), Window = dockHandle };
            SHAppBarMessage(1, ref data);
            appBarRegistered = false;
            appBarArea = Rect.Empty;
        }

        private void PositionDock()
        {
            var area = taskbarHidden ? new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight) : SystemParameters.WorkArea;
            double dockWidth = ActualWidth;
            double dockHeight = ActualHeight;
            if (dockWidth <= 0 || dockHeight <= 0 || Double.IsNaN(dockWidth) || Double.IsNaN(dockHeight)) return;
            Left = area.Left + Math.Max(0, (area.Width - dockWidth) / 2);
            double targetTop = dockAutoHidden && autoHideEnabled ? HiddenDockTop() : RevealedDockTop();
            BeginAnimation(TopProperty, null);
            Top = targetTop;
        }

        private void SchedulePositionDock()
        {
            if (positionDockPending || isClosing) return;
            positionDockPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
            {
                positionDockPending = false;
                PositionDock();
            }));
        }

        private void EnableAcrylic()
        {
            var accent = new AccentPolicy { AccentState = 4, AccentFlags = 2, GradientColor = unchecked((int)0xC8EDE7DD) };
            int size = Marshal.SizeOf(accent);
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pointer, false);
                var data = new WindowCompositionAttributeData { Attribute = 19, SizeOfData = size, Data = pointer };
                SetWindowCompositionAttribute(new WindowInteropHelper(this).Handle, ref data);
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        private static void SetTaskbarVisible(bool visible)
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero) ShowWindow(taskbar, visible ? 5 : 0);
            IntPtr secondary = IntPtr.Zero;
            while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                ShowWindow(secondary, visible ? 5 : 0);
        }

        private static List<IntPtr> GetTaskbarHandles()
        {
            var handles = new List<IntPtr>();
            IntPtr primary = FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero) handles.Add(primary);
            IntPtr secondary = IntPtr.Zero;
            while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
                handles.Add(secondary);
            return handles;
        }

        private void ApplyCompleteTaskbarHide()
        {
            foreach (IntPtr taskbarWindow in GetTaskbarHandles())
            {
                int style;
                if (!taskbarOriginalExtendedStyles.TryGetValue(taskbarWindow, out style))
                {
                    style = GetWindowLong(taskbarWindow, -20) & ~0x00080000;
                    taskbarOriginalExtendedStyles[taskbarWindow] = style;
                }
                SetWindowLong(taskbarWindow, -20, style | 0x00080000); // WS_EX_LAYERED
                SetLayeredWindowAttributes(taskbarWindow, 0, 0, 0x00000002); // LWA_ALPHA
            }
            SetTaskbarVisible(false);
        }

        private void RestoreOriginalTaskbarState()
        {
            foreach (var item in taskbarOriginalExtendedStyles.ToList())
            {
                SetLayeredWindowAttributes(item.Key, 0, 255, 0x00000002);
                SetWindowLong(item.Key, -20, item.Value);
            }
            taskbarOriginalExtendedStyles.Clear();
            SetTaskbarVisible(true);
        }

        private void TriggerLaunchRefreshBurst()
        {
            int[] delays = new[] { 100, 300, 650, 1300, 2200 };
            foreach (int d in delays)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(d) };
                t.Tick += delegate
                {
                    t.Stop();
                    nextTaskRefreshUtc = DateTime.MinValue;
                    RefreshTaskItems();
                };
                t.Start();
            }
        }

        private void Launch(string target, string arguments = null)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(target)) return;

                if (String.Equals(target, "explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                    !String.IsNullOrWhiteSpace(arguments) &&
                    arguments.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start("explorer.exe", arguments);
                    TriggerLaunchRefreshBurst();
                    return;
                }

                var psi = new ProcessStartInfo(target, arguments ?? String.Empty) { UseShellExecute = true };
                try
                {
                    if (File.Exists(target))
                    {
                        string dir = System.IO.Path.GetDirectoryName(target);
                        if (!String.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        {
                            psi.WorkingDirectory = dir;
                        }
                    }
                }
                catch { }

                Process.Start(psi);
                TriggerLaunchRefreshBurst();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível abrir este atalho.\n\n" + ex.Message, "B.M Flow", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenQuickSettings(FrameworkElement anchor)
        {
            // Win+A abre o mesmo painel rápido acionado pelo conjunto de
            // bateria, rede e volume da taskbar do Windows 11.
            autoHideTimer.Stop();
            quickSettingsActive = true;
            ShowDockAnimated();
            PrepareQuickSettingsPositioning(anchor);
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            keybd_event(0x41, 0, 0, UIntPtr.Zero);
            keybd_event(0x41, 0, 2, UIntPtr.Zero);
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);
            ScheduleQuickSettingsPositioning();
            MonitorQuickSettingsLifetime();
        }

        private static void OpenCalendarPanel()
        {
            // Win+N abre o painel nativo de notificações, calendário e data do
            // Windows 11 na posição definida pelo próprio Shell.
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            keybd_event(0x4E, 0, 0, UIntPtr.Zero);
            keybd_event(0x4E, 0, 2, UIntPtr.Zero);
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        }

        private void OpenStart()
        {
            autoHideTimer.Stop();
            startMenuActive = true;
            ShowDockAnimated();
            // sobrepor a Dock. Reservar uma AppBar aqui faz o Windows recalcular
            // tanto a janela maximizada quanto o Start e causa o salto/corte.
            startMenuReservationActive = false;
            PrepareStartMenuPositioning();
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
            delay.Tick += delegate
            {
                delay.Stop();
                keybd_event(0x5B, 0, 0, UIntPtr.Zero);
                keybd_event(0x5B, 0, 2, UIntPtr.Zero);
                ScheduleStartMenuPositioning();
                MonitorNativeStartMenu();
            };
            delay.Start();
        }

        private void ActivateDockForExternalStart(IntPtr startWindow)
        {
            if (isClosing || startWindow == IntPtr.Zero) return;
            autoHideTimer.Stop();
            edgeRevealPending = false;
            if (fullscreenSuppressed)
            {
                fullscreenSuppressed = false;
                Visibility = Visibility.Visible;
                UpdateAppBarRegistration();
            }
            if (!startMenuActive)
            {
                startMenuActive = true;
                ShowDockAnimated();
                startMenuReservationActive = false;
                PrepareStartMenuPositioning();
                ScheduleStartMenuPositioning();
                MonitorNativeStartMenu();
            }
            PositionStartMenuWindow(startWindow);
        }

        private void MonitorNativeStartMenu()
        {
            int attempts = 0;
            int missingTicks = 0;
            bool startSeen = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += delegate
            {
                attempts++;
                bool isStart = IsStartMenuWindow(GetForegroundWindow());
                if (isStart)
                {
                    startSeen = true;
                    missingTicks = 0;
                    // Desloca o host inteiro; nunca altera sua altura. O rodapé
                    // de usuário/energia continua completo e termina logo acima
                    // da superfície visível da Dock.
                    PositionStartMenuWindow(GetForegroundWindow());
                }
                else if (startSeen) missingTicks++;

                if ((startSeen && missingTicks >= 4) || (!startSeen && attempts >= 30))
                {
                    timer.Stop();
                    startMenuActive = false;
                    RestoreDockTopmost();
                    ReleaseStartMenuHook();
                    if (autoHideEnabled) ScheduleAutoHide();
                }
            };
            timer.Start();
        }

        private void PlaceDockBehindWindow(IntPtr frontWindow)
        {
            if (dockHandle == IntPtr.Zero || frontWindow == IntPtr.Zero || frontWindow == dockHandle) return;
            const uint KeepGeometryNoActivate = 0x0001 | 0x0002 | 0x0010;
            SetWindowPos(dockHandle, new IntPtr(-2), 0, 0, 0, 0, KeepGeometryNoActivate); // HWND_NOTOPMOST
            SetWindowPos(dockHandle, frontWindow, 0, 0, 0, 0, KeepGeometryNoActivate);
        }

        private void RestoreDockTopmost()
        {
            if (dockHandle == IntPtr.Zero) return;
            const uint KeepGeometryNoActivate = 0x0001 | 0x0002 | 0x0010;
            SetWindowPos(dockHandle, new IntPtr(-1), 0, 0, 0, 0, KeepGeometryNoActivate); // HWND_TOPMOST
        }

        private void PrepareStartMenuPositioning()
        {
            try
            {
                Point bar = dockShell.PointToScreen(new Point(0, 0));
                // PointToScreen inclui o TranslateTransform da animação de
                // entrada (10 DIP). Use a posição final da superfície para o
                // Start não nascer 12–15 pixels baixo demais em telas com DPI.
                var reveal = dockShell.RenderTransform as TranslateTransform;
                if (reveal != null && Math.Abs(reveal.Y) > .01)
                {
                    double dpiY = 1.0;
                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                        dpiY = source.CompositionTarget.TransformToDevice.M22;
                    bar.Y -= reveal.Y * dpiY;
                }
                var screen = Forms.Screen.FromPoint(new Drawing.Point((int)Math.Round(bar.X), (int)Math.Round(bar.Y)));
                startMenuBarTop = (int)Math.Round(bar.Y);
                startMenuWorkTop = screen.WorkingArea.Top;
                ReleaseStartMenuHook();
                int generation = ++startMenuHookGeneration;
                startMenuEventDelegate = OnStartMenuWindowEvent;
                startMenuEventHook = SetWinEventHook(0x8002, 0x800B, IntPtr.Zero, startMenuEventDelegate, 0, 0, 0x0002);
                var release = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                release.Tick += delegate
                {
                    release.Stop();
                    if (generation == startMenuHookGeneration) ReleaseStartMenuHook();
                };
                release.Start();
            }
            catch { ReleaseStartMenuHook(); }
        }

        private void OnStartMenuWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (IsStartMenuWindow(window)) PositionStartMenuWindow(window);
        }

        private bool IsStartMenuWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            if (!String.Equals(className.ToString(), "Windows.UI.Core.CoreWindow", StringComparison.Ordinal)) return false;
            try
            {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                using (Process process = Process.GetProcessById((int)processId))
                {
                    string name = process.ProcessName;
                    return name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                           name.Equals("ShellHost", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private bool PositionStartMenuWindow(IntPtr window)
        {
            NativeRect bounds;
            if (!GetWindowRect(window, out bounds)) return false;
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            if (width < 420 || height < 350) return false;
            // O Start redesenhado do 26H2 não reorganiza o conteúdo quando a
            // janela é redimensionada: a área de usuário/energia fica cortada.
            // Preservamos a altura nativa e permitimos que só a moldura superior
            // ultrapasse alguns pixels da tela para manter o rodapé inteiro.
            int top = startMenuBarTop - height - 9;
            return SetWindowPos(window, IntPtr.Zero, bounds.Left, top, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }

        private void ScheduleStartMenuPositioning()
        {
            int attempts = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(12) };
            timer.Tick += delegate
            {
                attempts++;
                IntPtr start = FindStartMenuWindow();
                if (start != IntPtr.Zero) PositionStartMenuWindow(start);
                if (attempts >= 80) timer.Stop();
            };
            timer.Start();
        }

        private IntPtr FindStartMenuWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            if (IsStartMenuWindow(foreground)) return foreground;
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (IsStartMenuWindow(window))
                {
                    found = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private void ReleaseStartMenuHook()
        {
            if (startMenuEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(startMenuEventHook);
                startMenuEventHook = IntPtr.Zero;
            }
            startMenuEventDelegate = null;
        }

        private void UpdateTaskbarVisibilityGuard()
        {
            ReleaseTaskbarVisibilityGuard();
            if (!taskbarHidden || isClosing) return;
            taskbarVisibilityEventDelegate = OnTaskbarVisibilityEvent;
            taskbarVisibilityEventHook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, taskbarVisibilityEventDelegate, 0, 0, 0x0002);
        }

        private void OnTaskbarVisibilityEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (!taskbarHidden || window == IntPtr.Zero) return;
            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            string value = className.ToString();
            if (value == "Shell_TrayWnd" || value == "Shell_SecondaryTrayWnd")
            {
                int style;
                if (!taskbarOriginalExtendedStyles.TryGetValue(window, out style))
                {
                    style = GetWindowLong(window, -20) & ~0x00080000;
                    taskbarOriginalExtendedStyles[window] = style;
                }
                SetWindowLong(window, -20, style | 0x00080000);
                SetLayeredWindowAttributes(window, 0, 0, 0x00000002);
                ShowWindow(window, 0);
            }
        }

        private void ReleaseTaskbarVisibilityGuard()
        {
            if (taskbarVisibilityEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(taskbarVisibilityEventHook);
                taskbarVisibilityEventHook = IntPtr.Zero;
            }
            taskbarVisibilityEventDelegate = null;
        }

        private void PrepareQuickSettingsPositioning(FrameworkElement anchor)
        {
            try
            {
                Point anchorPoint = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
                Point barPoint = dockShell.PointToScreen(new Point(0, 0));
                var reveal = dockShell.RenderTransform as TranslateTransform;
                if (reveal != null && Math.Abs(reveal.Y) > .01)
                {
                    double dpiY = 1;
                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                        dpiY = source.CompositionTarget.TransformToDevice.M22;
                    barPoint.Y -= reveal.Y * dpiY;
                }
                Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point((int)Math.Round(anchorPoint.X), (int)Math.Round(anchorPoint.Y)));
                quickSettingsAnchorX = (int)Math.Round(anchorPoint.X);
                quickSettingsBarTop = (int)Math.Round(barPoint.Y);
                quickSettingsWorkLeft = screen.WorkingArea.Left;
                quickSettingsWorkRight = screen.WorkingArea.Right;
                quickSettingsWorkTop = screen.WorkingArea.Top;

                ReleaseQuickSettingsHook();
                int generation = ++quickSettingsHookGeneration;
                quickSettingsEventDelegate = OnQuickSettingsWindowEvent;
                quickSettingsEventHook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, quickSettingsEventDelegate, 0, 0, 0x0002);
                var release = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                release.Tick += delegate
                {
                    release.Stop();
                    if (generation == quickSettingsHookGeneration) ReleaseQuickSettingsHook();
                };
                release.Start();
            }
            catch { ReleaseQuickSettingsHook(); }
        }

        private void OnQuickSettingsWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (!IsQuickSettingsWindow(window)) return;
            Dispatcher.BeginInvoke(new Action(delegate { PositionQuickSettingsWindow(window); }));
        }

        private static bool IsQuickSettingsWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            if (!String.Equals(className.ToString(), "ControlCenterWindow", StringComparison.Ordinal)) return false;
            try
            {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                using (Process owner = Process.GetProcessById((int)processId))
                {
                    string process = owner.ProcessName;
                    return process.Equals("ShellHost", StringComparison.OrdinalIgnoreCase) ||
                           process.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private IntPtr FindQuickSettingsWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            if (IsQuickSettingsWindow(foreground)) return foreground;
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (!IsQuickSettingsWindow(window) || !IsWindowVisible(window)) return true;
                int cloaked;
                if (DwmGetWindowAttribute(window, 14, out cloaked, Marshal.SizeOf(typeof(int))) == 0 && cloaked != 0) return true;
                found = window;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private bool PositionQuickSettingsWindow(IntPtr window)
        {
            if (!IsQuickSettingsWindow(window)) return false;
            NativeRect bounds;
            if (!GetWindowRect(window, out bounds)) return false;
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            if (width < 220 || height < 180) return false;
            int left = quickSettingsAnchorX - width / 2;
            left = Math.Max(quickSettingsWorkLeft + 8, Math.Min(left, quickSettingsWorkRight - width - 8));
            int top = Math.Max(quickSettingsWorkTop + 8, quickSettingsBarTop - height - 38);
            return SetWindowPos(window, IntPtr.Zero, left, top, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }

        private void ScheduleQuickSettingsPositioning()
        {
            int attempts = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += delegate
            {
                attempts++;
                IntPtr window = FindQuickSettingsWindow();
                if (window != IntPtr.Zero) PositionQuickSettingsWindow(window);
                // O Shell aplica sua posição final depois da animação inicial;
                // mantemos a âncora até esse ciclo terminar por completo.
                if (attempts >= 70) timer.Stop();
            };
            timer.Start();
        }

        private void MonitorQuickSettingsLifetime()
        {
            if (quickSettingsMonitorTimer != null) quickSettingsMonitorTimer.Stop();
            int attempts = 0;
            int missingTicks = 0;
            bool panelSeen = false;
            quickSettingsMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            quickSettingsMonitorTimer.Tick += delegate
            {
                attempts++;
                IntPtr panel = FindQuickSettingsWindow();
                if (panel != IntPtr.Zero)
                {
                    panelSeen = true;
                    missingTicks = 0;
                }
                else if (panelSeen) missingTicks++;

                if ((panelSeen && missingTicks >= 3) || (!panelSeen && attempts >= 12))
                {
                    quickSettingsMonitorTimer.Stop();
                    quickSettingsActive = false;
                    ScheduleAutoHide();
                }
            };
            quickSettingsMonitorTimer.Start();
        }

        private void ReleaseQuickSettingsHook()
        {
            if (quickSettingsEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(quickSettingsEventHook);
                quickSettingsEventHook = IntPtr.Zero;
            }
            quickSettingsEventDelegate = null;
        }

        private void OpenTrayOverflow()
        {
            PrepareTrayOverflowHook();
            if (TryInvokeTrayChevron())
            {
                ScheduleTrayOverflowPosition();
                return;
            }

            // Atalho nativo de acessibilidade: leva o foco à bandeja. Um pequeno
            // atraso permite ao Explorer selecionar o chevron antes do Enter.
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            keybd_event(0x42, 0, 0, UIntPtr.Zero);
            keybd_event(0x42, 0, 2, UIntPtr.Zero);
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            delay.Tick += delegate
            {
                delay.Stop();
                keybd_event(0x0D, 0, 0, UIntPtr.Zero);
                keybd_event(0x0D, 0, 2, UIntPtr.Zero);
                ScheduleTrayOverflowPosition();
            };
            delay.Start();
        }

        private void PrepareTrayOverflowHook()
        {
            try
            {
                Point arrow = trayArrowText.PointToScreen(new Point(trayArrowText.ActualWidth / 2, 0));
                Point bar = dockShell.PointToScreen(new Point(0, 0));
                var screen = Forms.Screen.FromPoint(new Drawing.Point((int)Math.Round(arrow.X), (int)Math.Round(arrow.Y)));
                trayPopupAnchorX = (int)Math.Round(arrow.X);
                trayPopupBarTop = (int)Math.Round(bar.Y);
                trayPopupWorkLeft = screen.WorkingArea.Left;
                trayPopupWorkRight = screen.WorkingArea.Right;
                trayPopupWorkTop = screen.WorkingArea.Top;

                ReleaseTrayOverflowHook();
                int hookGeneration = ++trayOverflowHookGeneration;
                trayOverflowEventDelegate = OnTrayOverflowWindowEvent;
                trayOverflowEventHook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, trayOverflowEventDelegate, 0, 0, 0x0002);

                var release = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                release.Tick += delegate
                {
                    release.Stop();
                    if (hookGeneration == trayOverflowHookGeneration) ReleaseTrayOverflowHook();
                };
                release.Start();
            }
            catch { ReleaseTrayOverflowHook(); }
        }

        private void OnTrayOverflowWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (window == IntPtr.Zero) return;
            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            string value = className.ToString();
            if (!String.Equals(value, "TopLevelWindowForOverflowXamlIsland", StringComparison.Ordinal) &&
                !String.Equals(value, "NotifyIconOverflowWindow", StringComparison.Ordinal)) return;
            PositionTrayOverflowWindow(window);
        }

        private void ReleaseTrayOverflowHook()
        {
            if (trayOverflowEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(trayOverflowEventHook);
                trayOverflowEventHook = IntPtr.Zero;
            }
            trayOverflowEventDelegate = null;
        }

        private void ScheduleTrayOverflowPosition()
        {
            int attempts = 0;
            int positioned = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(12) };
            timer.Tick += delegate
            {
                attempts++;
                if (TryPositionTrayOverflow()) positioned++;
                if (attempts >= 14 || positioned >= 4) timer.Stop();
            };
            timer.Start();
        }

        private bool TryPositionTrayOverflow()
        {
            try
            {
                IntPtr overflow = FindWindow("TopLevelWindowForOverflowXamlIsland", null);
                if (overflow == IntPtr.Zero) overflow = FindWindow("NotifyIconOverflowWindow", null);
                return overflow != IntPtr.Zero && PositionTrayOverflowWindow(overflow);
            }
            catch { return false; }
        }

        private bool PositionTrayOverflowWindow(IntPtr overflow)
        {
            NativeRect bounds;
            if (!GetWindowRect(overflow, out bounds)) return false;
            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            if (width <= 0 || height <= 0) return false;
            int left = trayPopupAnchorX - width / 2;
            left = Math.Max(trayPopupWorkLeft + 6, Math.Min(left, trayPopupWorkRight - width - 6));
            int top = Math.Max(trayPopupWorkTop + 6, trayPopupBarTop - height - 9);
            return SetWindowPos(overflow, IntPtr.Zero, left, top, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }

        private static bool TryInvokeTrayChevron()
        {
            try
            {
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar == IntPtr.Zero) return false;
                IntPtr provider = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                if (provider == IntPtr.Zero) provider = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null);
                AutomationElement root = AutomationElement.FromHandle(provider == IntPtr.Zero ? taskbar : provider);
                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants, condition);
                foreach (AutomationElement button in buttons)
                {
                    string name = (button.Current.Name ?? String.Empty).ToLowerInvariant();
                    bool chevron = name.Contains("mostrar ícones ocultos") || name.Contains("ocultar ícones ocultos") ||
                                   name.Contains("show hidden icons") || name.Contains("hide hidden icons") ||
                                   name.Contains("notification chevron") || name.Contains("tray overflow");
                    if (!chevron) continue;
                    object pattern;
                    if (button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                    {
                        ((InvokePattern)pattern).Invoke();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void OpenRecycleBin()
        {
            Launch("explorer.exe", "shell:RecycleBinFolder");
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr window, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int valueSize);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItemImageFactory item);

        [DllImport("shell32.dll", PreserveSig = true)]
        private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid interfaceId, out IPropertyStore propertyStore);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr objectHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetStockIconInfo(uint stockIconId, uint flags, ref StockIconInfo stockIconInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string rootPath, ref RecycleBinInfo recycleBinInfo);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr PointerValue;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint propertyCount);
            [PreserveSig] int GetAt(uint propertyIndex, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [ComImport, Guid("3C594F9F-9F30-47A1-979A-C9E83D3D0A06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationDocumentLists
        {
            [PreserveSig] int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId);
            [PreserveSig] int GetList(int listType, uint desiredItems, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object list);
        }

        [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectArray
        {
            [PreserveSig] int GetCount(out uint objectCount);
            [PreserveSig] int GetAt(uint index, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object value);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr value);
            [PreserveSig] int GetParent([MarshalAs(UnmanagedType.Interface)] out object parent);
            [PreserveSig] int GetDisplayName(uint displayNameType, out IntPtr name);
            [PreserveSig] int GetAttributes(uint mask, out uint attributes);
            [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StockIconInfo
        {
            public int Size;
            public IntPtr Icon;
            public int SystemImageIndex;
            public int IconIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string Path;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RecycleBinInfo
        {
            public int Size;
            public long TotalSize;
            public long ItemCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public int Size;
            public IntPtr Window;
            public uint CallbackMessage;
            public uint Edge;
            public NativeRect Rectangle;
            public IntPtr Parameter;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo monitorInfo);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

        private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMinimum, uint eventMaximum, IntPtr eventHookModule, WinEventDelegate callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern bool RegisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool DeregisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr window, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        public void SwitchToWorkspace(int index)
        {
            if (workspaceManager == null || index < 0 || index >= workspaceManager.Workspaces.Count) return;
            if (workspaceManager.ActiveIndex == index)
            {
                var current = workspaceManager.ActiveWorkspace;
                if (current != null)
                {
                    WorkspaceHudWindow.ShowHud(
                        String.IsNullOrWhiteSpace(current.Icon) ? "💼" : current.Icon,
                        current.Name,
                        "Espaço " + (index + 1) + " • Ativo (Ctrl+Alt+" + (index + 1) + ")",
                        Left, ActualWidth, Top);
                }
                return;
            }

            workspaceManager.ActiveIndex = index;
            var ws = workspaceManager.ActiveWorkspace;
            if (ws != null)
            {
                WorkspaceHudWindow.ShowHud(
                    String.IsNullOrWhiteSpace(ws.Icon) ? "💼" : ws.Icon,
                    ws.Name,
                    "Espaço " + (index + 1) + " • Ctrl+Alt+" + (index + 1),
                    Left, ActualWidth, Top);
            }

            // Atualização imediata (0ms de delay!)
            RefreshTaskItemsImmediate();

            // Salva o índice ativo em background para não bloquear o input
            ThreadPool.QueueUserWorkItem(delegate { workspaceManager.Save(); });
        }

        public void TriggerDockRebuild()
        {
            RefreshTaskItemsImmediate();
        }

        internal void LaunchOrRestoreWorkspaceApps(DockWorkspace ws)
        {
            if (ws == null) ws = workspaceManager != null ? workspaceManager.ActiveWorkspace : null;
            if (ws == null) return;
            var appsToProcess = (ws.Apps.Count > 0)
                ? ws.Apps
                : (ws.SyncWithWindows ? GetInitialPinnedForWorkspace() : new List<WorkspaceAppItem>());
            if (appsToProcess.Count == 0) return;

            var openWindows = EnumerateTaskbarWindows().ToList();
            int launched = 0;
            int restored = 0;

            foreach (var app in appsToProcess)
            {
                bool broughtToFront = false;
                foreach (IntPtr hWnd in openWindows)
                {
                    try
                    {
                        uint pid;
                        GetWindowThreadProcessId(hWnd, out pid);
                        if (pid == 0) continue;
                        using (var proc = Process.GetProcessById((int)pid))
                        {
                            string path = proc.MainModule == null ? String.Empty : proc.MainModule.FileName;
                            string procName = proc.ProcessName;
                            string appId = GetWindowAppUserModelId(hWnd);

                            bool isSteamMatch = (String.Equals(procName, "steamwebhelper", StringComparison.OrdinalIgnoreCase) || String.Equals(procName, "steam", StringComparison.OrdinalIgnoreCase)) &&
                                                (String.Equals(app.Name, "Steam", StringComparison.OrdinalIgnoreCase) || String.Equals(System.IO.Path.GetFileNameWithoutExtension(app.Target), "steam", StringComparison.OrdinalIgnoreCase));

                            bool match = isSteamMatch ||
                                         (!String.IsNullOrWhiteSpace(app.AppUserModelId) && String.Equals(appId, app.AppUserModelId, StringComparison.OrdinalIgnoreCase)) ||
                                         SameTarget(app.Target, path) ||
                                         String.Equals(System.IO.Path.GetFileNameWithoutExtension(app.Target), procName, StringComparison.OrdinalIgnoreCase);

                            if (match)
                            {
                                WindowCommands.Activate(hWnd);
                                broughtToFront = true;
                                restored++;
                            }
                        }
                    }
                    catch { }
                }

                if (!broughtToFront)
                {
                    try
                    {
                        if (File.Exists(app.Target))
                        {
                            Launch(app.Target, app.Arguments);
                            launched++;
                        }
                        else if (!String.IsNullOrWhiteSpace(app.AppUserModelId) && app.AppUserModelId.IndexOf('\\') < 0 && app.AppUserModelId.IndexOf(':') < 0 && !app.AppUserModelId.StartsWith("{"))
                        {
                            Launch("explorer.exe", @"shell:AppsFolder\" + app.AppUserModelId);
                            launched++;
                        }
                        else if (!String.IsNullOrWhiteSpace(app.Target))
                        {
                            Launch(app.Target, app.Arguments);
                            launched++;
                        }
                    }
                    catch { }
                }
            }

            WorkspaceHudWindow.ShowHud(
                ws.Icon ?? "🚀",
                "Espaço: " + ws.Name,
                restored > 0 ? (restored + " janela(s) restaurada(s), " + launched + " aberta(s)") : (launched + " aplicativo(s) iniciado(s)"),
                Left, ActualWidth, Top);
        }

        private void RegisterWorkspaceHotKeys()
        {
            if (dockHandle == IntPtr.Zero) return;
            UnregisterWorkspaceHotKeys();
            uint modsWithNoRepeat = 0x0001 | 0x0002 | 0x4000;
            uint modsSimple = 0x0001 | 0x0002;

            for (int i = 0; i < 9; i++)
            {
                uint vkDigit = (uint)(0x31 + i);
                uint vkNumpad = (uint)(0x61 + i);
                int idDigit = 0x9100 + i;
                int idNumpad = 0x9200 + i;

                if (!RegisterHotKey(dockHandle, idDigit, modsWithNoRepeat, vkDigit))
                    RegisterHotKey(dockHandle, idDigit, modsSimple, vkDigit);

                if (!RegisterHotKey(dockHandle, idNumpad, modsWithNoRepeat, vkNumpad))
                    RegisterHotKey(dockHandle, idNumpad, modsSimple, vkNumpad);
            }
        }

        private void UnregisterWorkspaceHotKeys()
        {
            if (dockHandle == IntPtr.Zero) return;
            for (int i = 0; i < 9; i++)
            {
                try { UnregisterHotKey(dockHandle, 0x9100 + i); } catch { }
                try { UnregisterHotKey(dockHandle, 0x9200 + i); } catch { }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }
    }

    /// <summary>
    /// Comandos de janela compartilhados entre a Dock e as prévias. Clicar no
    /// ícone, numa miniatura ou usar o menu sempre chega aos mesmos caminhos,
    /// então restaurar, minimizar e fechar se comportam de forma idêntica.
    /// </summary>
    internal static class WindowCommands
    {
        private const uint SystemCommand = 0x0112; // WM_SYSCOMMAND
        private static readonly IntPtr MinimizeCommand = new IntPtr(0xF020); // SC_MINIMIZE
        private static readonly IntPtr CloseCommand = new IntPtr(0xF060); // SC_CLOSE

        public static bool IsAlive(IntPtr window)
        {
            return window != IntPtr.Zero && IsWindow(window);
        }

        public static bool IsMinimized(IntPtr window)
        {
            return IsAlive(window) && IsIconic(window);
        }

        public static void Activate(IntPtr window)
        {
            if (!IsAlive(window)) return;
            // SW_RESTORE em uma janela maximizada a transforma em janela normal.
            // Só restauramos quando ela está minimizada; nos demais casos SW_SHOW
            // preserva o tamanho e o estado de maximização.
            int command = IsIconic(window) ? 9 : 5;
            // Uma janela travada bloquearia ShowWindow; a versão assíncrona
            // deixa o fantasma do DWM tratar o pedido sem congelar a Dock.
            if (IsHungAppWindow(window)) ShowWindowAsync(window, command);
            else ShowWindow(window, command);
            SetForegroundWindow(window);
        }

        public static void Restore(IntPtr window)
        {
            if (!IsMinimized(window)) return;
            ShowWindowAsync(window, 9); // SW_RESTORE
        }

        public static void Minimize(IntPtr window)
        {
            if (!IsAlive(window) || IsIconic(window)) return;
            // SC_MINIMIZE mantém a animação e as regras do próprio aplicativo,
            // exatamente como o botão da barra de título.
            if (IsHungAppWindow(window)) ShowWindowAsync(window, 6); // SW_MINIMIZE
            else PostMessage(window, SystemCommand, MinimizeCommand, IntPtr.Zero);
        }

        public static void Close(IntPtr window)
        {
            if (!IsAlive(window)) return;
            // SC_CLOSE equivale ao X da barra de título: respeita janelas que
            // desabilitam o fechamento e permite ao app perguntar se deve salvar.
            PostMessage(window, SystemCommand, CloseCommand, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsHungAppWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }

    internal sealed class PreviewWindow : Window
    {
        private const double HeaderHeight = 40;
        private const int MaximumColumns = 6;
        private const int MaximumRows = 2;

        private static readonly Brush CardBackground = Frozen(new SolidColorBrush(Color.FromRgb(31, 31, 34)));
        private static readonly Brush CardSelectedBackground = Frozen(new SolidColorBrush(Color.FromRgb(44, 48, 58)));
        private static readonly Brush CardBorder = Frozen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)));
        private static readonly Brush CardSelectedBorder = Frozen(new SolidColorBrush(Color.FromArgb(235, 25, 120, 225)));
        private static readonly Brush SecondaryText = Frozen(new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)));

        private readonly List<IntPtr> sourceWindows = new List<IntPtr>();
        private readonly List<string> sourceTitles = new List<string>();
        private readonly HashSet<IntPtr> closingWindows = new HashSet<IntPtr>();
        private readonly string appName;
        private readonly Size maximumArea;
        private readonly Canvas root;

        // Apenas os cartões da página visível existem e possuem thumbnail DWM
        // registrado. Grupos grandes não multiplicam o custo de composição.
        private readonly List<IntPtr> cardWindows = new List<IntPtr>();
        private readonly List<Border> previewCards = new List<Border>();
        private readonly List<TextBlock> cardLabels = new List<TextBlock>();
        private readonly List<Border> cardMinimizeButtons = new List<Border>();
        private readonly List<double> previewCardBaseTops = new List<double>();
        private readonly List<double> previewCardCurrentTops = new List<double>();
        private readonly List<double> previewCardTargetTops = new List<double>();
        private readonly List<IntPtr> thumbnails = new List<IntPtr>();
        private readonly List<NativeRect> thumbnailBaseDestinations = new List<NativeRect>();

        private Border header;
        private StackPanel headerPanel;
        private Border headerPageSeparator;
        private TextBlock headerTitle;
        private TextBlock headerCount;
        private TextBlock headerPage;
        private Border headerPrevious;
        private Border headerNext;
        private TextBlock headerPreviousGlyph;
        private TextBlock headerNextGlyph;
        private Brush headerButtonHover;
        private Brush cardHoverBorder;
        private readonly Color? accentColor;

        private double cardWidth = 255;
        private double cardHeight = 190;
        private double waveHeadroom;
        private double headerSpace;
        private int columns = 1;
        private int rows = 1;
        private int pageSize = 1;
        private int pageIndex;
        private int pageCount = 1;

        private readonly DispatcherTimer peekTimer;
        private readonly DispatcherTimer previewWaveTimer;
        private readonly DispatcherTimer validationTimer;
        private double thumbnailDpiX = 1;
        private double thumbnailDpiY = 1;
        private double previewPointerX;
        private IntPtr selectedWindow;
        private IntPtr pendingPeekTarget;
        private IntPtr peekTarget;
        private IntPtr peekPreviousForeground;
        private bool peekTargetWasMinimized;
        private bool peekTargetWasTopmost;
        private bool isClosed;

        // Navegação por teclado: o gancho só existe enquanto a prévia está
        // visível e só consome teclas depois que o usuário começa a navegar.
        private bool keyboardMode;
        private IntPtr keyboardForeground;
        private int lastOwnActionTick;
        private LowLevelKeyboardProc keyboardProc;
        private IntPtr keyboardHook;
        private readonly HashSet<int> capturedKeys = new HashSet<int>();

        /// <summary>Disparado quando o tamanho da prévia muda e ela precisa ser reposicionada.</summary>
        public event EventHandler LayoutChanged;

        /// <summary>Disparado quando janelas do grupo foram fechadas ou desapareceram.</summary>
        public event EventHandler WindowsChanged;

        public bool KeyboardActive { get { return keyboardMode && !isClosed; } }

        public PreviewWindow(IEnumerable<IntPtr> sources, IList<string> titles, string appName, Size maximumArea, Color? accentColor = null)
        {
            this.appName = appName ?? String.Empty;
            this.maximumArea = maximumArea.Width >= 200 && maximumArea.Height >= 200
                ? maximumArea
                : new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
            this.accentColor = accentColor;
            ReadSources(sources, titles, sourceWindows, sourceTitles);
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Brushes.Transparent;

            peekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            peekTimer.Tick += delegate
            {
                peekTimer.Stop();
                if (pendingPeekTarget != IntPtr.Zero) BeginPeek(pendingPeekTarget);
            };

            previewWaveTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            previewWaveTimer.Tick += delegate { RenderPreviewWave(); };

            // Verificação leve do estado das janelas (fechada, minimizada). Só
            // roda enquanto a prévia está visível.
            validationTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            validationTimer.Tick += delegate { ValidateSources(); };

            // A Window serve apenas como superfície para os cartões e thumbnails;
            // todo o espaço entre eles permanece visualmente transparente.
            root = new Canvas { Background = Brushes.Transparent };
            root.MouseMove += delegate(object sender, MouseEventArgs args)
            {
                previewPointerX = args.GetPosition(root).X;
                UpdatePreviewWaveTargets();
                if (!previewWaveTimer.IsEnabled) previewWaveTimer.Start();
            };
            root.MouseWheel += delegate(object sender, MouseWheelEventArgs args)
            {
                if (pageCount <= 1) return;
                args.Handled = true;
                ChangePage(args.Delta < 0 ? 1 : -1);
            };
            Content = root;
            BuildHeader();
            ApplyLayout(false);

            IsVisibleChanged += delegate
            {
                if (isClosed) return;
                if (IsVisible)
                {
                    RegisterThumbnails();
                    InstallKeyboardHook();
                    validationTimer.Start();
                }
                else
                {
                    // Prévia oculta (por exemplo, junto com a Dock): nada de
                    // thumbnails vivos, timers ou gancho de teclado.
                    validationTimer.Stop();
                    previewWaveTimer.Stop();
                    CancelPeek();
                    RemoveKeyboardHook();
                    UnregisterThumbnails();
                }
            };
            Closed += delegate
            {
                isClosed = true;
                validationTimer.Stop();
                previewWaveTimer.Stop();
                RemoveKeyboardHook();
                EndPeek(true);
                UnregisterThumbnails();
            };
        }

        /// <summary>
        /// Atualiza o grupo exibido sem recriar a janela. Se nada mudou, apenas
        /// os títulos são atualizados e nenhum thumbnail é registrado de novo.
        /// </summary>
        public void UpdateSources(IEnumerable<IntPtr> sources, IList<string> titles)
        {
            if (isClosed) return;
            var windows = new List<IntPtr>();
            var names = new List<string>();
            ReadSources(sources, titles, windows, names);
            if (windows.Count == 0)
            {
                Close();
                return;
            }
            if (windows.SequenceEqual(sourceWindows))
            {
                sourceTitles.Clear();
                sourceTitles.AddRange(names);
                for (int index = 0; index < cardWindows.Count; index++)
                {
                    int source = sourceWindows.IndexOf(cardWindows[index]);
                    if (source < 0) continue;
                    string title = String.IsNullOrWhiteSpace(sourceTitles[source]) ? appName : sourceTitles[source];
                    if (cardLabels[index].Text != title) cardLabels[index].Text = title;
                }
                return;
            }
            int selectedIndex = sourceWindows.IndexOf(selectedWindow);
            sourceWindows.Clear();
            sourceWindows.AddRange(windows);
            sourceTitles.Clear();
            sourceTitles.AddRange(names);
            closingWindows.RemoveWhere(window => !sourceWindows.Contains(window));
            if (!sourceWindows.Contains(selectedWindow))
                selectedWindow = keyboardMode && selectedIndex >= 0
                    ? sourceWindows[Math.Min(selectedIndex, sourceWindows.Count - 1)]
                    : IntPtr.Zero;
            ApplyLayout(true);
        }

        private static void ReadSources(IEnumerable<IntPtr> sources, IList<string> titles, List<IntPtr> windows, List<string> names)
        {
            windows.Clear();
            names.Clear();
            if (sources == null) return;
            int index = 0;
            foreach (IntPtr handle in sources)
            {
                // O título acompanha o índice original, mesmo quando um handle
                // repetido ou nulo é descartado.
                string title = titles != null && index < titles.Count ? titles[index] : null;
                index++;
                if (handle == IntPtr.Zero || windows.Contains(handle)) continue;
                windows.Add(handle);
                names.Add(title);
            }
        }

        private void ComputeLayout()
        {
            int count = Math.Max(1, sourceWindows.Count);
            // Cartões ficam mais compactos conforme o grupo cresce, mantendo
            // várias janelas legíveis lado a lado.
            if (count <= 3)
            {
                cardWidth = 255;
                cardHeight = 190;
            }
            else if (count <= 6)
            {
                cardWidth = 222;
                cardHeight = 168;
            }
            else
            {
                cardWidth = 196;
                cardHeight = 150;
            }
            headerSpace = sourceWindows.Count >= 1 ? HeaderHeight : 0;
            waveHeadroom = count > 1 ? 18 : 0;

            int maxColumns = (int)Math.Floor((maximumArea.Width - 24) / (cardWidth + 8));
            maxColumns = Math.Max(1, Math.Min(MaximumColumns, maxColumns));
            double availableHeight = maximumArea.Height * .62 - headerSpace - waveHeadroom - 16;
            int maxRows = (int)Math.Floor(availableHeight / (cardHeight + 8));
            maxRows = Math.Max(1, Math.Min(MaximumRows, maxRows));
            int capacity = maxColumns * maxRows;

            if (count <= capacity)
            {
                // Distribui em linhas equilibradas (ex.: 7 janelas = 4 + 3,
                // em vez de 6 + 1).
                rows = (int)Math.Ceiling((double)count / maxColumns);
                columns = (int)Math.Ceiling((double)count / rows);
                pageSize = count;
                pageCount = 1;
            }
            else
            {
                // Grupos maiores que a tela são paginados. O tamanho da janela
                // permanece fixo entre páginas para não "pular" sobre a Dock.
                columns = maxColumns;
                rows = maxRows;
                pageSize = capacity;
                pageCount = (int)Math.Ceiling((double)count / capacity);
            }
            pageIndex = Math.Max(0, Math.Min(pageIndex, pageCount - 1));
            Width = 8 + columns * (cardWidth + 8);
            Height = 8 + headerSpace + waveHeadroom + rows * (cardHeight + 8);
        }

        private void ApplyLayout(bool notify)
        {
            double oldWidth = Width;
            double oldHeight = Height;
            ComputeLayout();
            UpdateHeader();
            BuildPage();
            if (!notify || LayoutChanged == null) return;
            if (Double.IsNaN(oldWidth) || Double.IsNaN(oldHeight) ||
                Math.Abs(oldWidth - Width) > .5 || Math.Abs(oldHeight - Height) > .5)
                LayoutChanged(this, EventArgs.Empty);
        }

        private void BuildHeader()
        {
            headerTitle = new TextBlock
            {
                Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            headerCount = new TextBlock
            {
                Foreground = SecondaryText, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0)
            };
            headerPrevious = HeaderButton("\uE76B", "Página anterior", out headerPreviousGlyph);
            headerPrevious.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                args.Handled = true;
                ChangePage(-1);
            };
            headerPage = new TextBlock
            {
                Foreground = SecondaryText, FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0)
            };
            headerNext = HeaderButton("\uE76C", "Próxima página", out headerNextGlyph);
            headerNext.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                args.Handled = true;
                ChangePage(1);
            };
            headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };
            headerPanel.Children.Add(headerTitle);
            headerPanel.Children.Add(headerCount);
            headerPageSeparator = new Border { Width = 8, Visibility = Visibility.Collapsed };
            headerPanel.Children.Add(headerPageSeparator);
            headerPanel.Children.Add(headerPrevious);
            headerPanel.Children.Add(headerPage);
            headerPanel.Children.Add(headerNext);
            header = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromArgb(238, 31, 31, 34)),
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 2,
                    Direction = 270,
                    Opacity = 0.38,
                    Color = Color.FromRgb(0, 0, 0)
                },
                Child = headerPanel,
                Visibility = Visibility.Collapsed
            };
            ApplyHeaderTheme(accentColor);
            header.SizeChanged += delegate { CenterHeader(); };
            Canvas.SetTop(header, 2);
            root.Children.Add(header);
        }

        private Border HeaderButton(string glyph, string tip, out TextBlock glyphBlock)
        {
            glyphBlock = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 9,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var button = new Border
            {
                Width = 22,
                Height = 20,
                CornerRadius = new CornerRadius(5),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                Child = glyphBlock
            };
            button.MouseEnter += delegate { button.Background = headerButtonHover ?? Brushes.Transparent; };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            return button;
        }

        private void ApplyHeaderTheme(Color? accent)
        {
            if (header == null) return;
            if (!accent.HasValue)
            {
                cardHoverBorder = CardSelectedBorder;
                header.Background = new SolidColorBrush(Color.FromArgb(238, 31, 31, 34));
                header.BorderBrush = CardBorder;
                headerTitle.Foreground = Brushes.White;
                headerCount.Foreground = SecondaryText;
                headerPage.Foreground = SecondaryText;
                if (headerPreviousGlyph != null) headerPreviousGlyph.Foreground = Brushes.White;
                if (headerNextGlyph != null) headerNextGlyph.Foreground = Brushes.White;
                headerButtonHover = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
                return;
            }

            Color c = accent.Value;
            byte r = c.R;
            byte g = c.G;
            byte b = c.B;
            if (r < 35 && g < 35 && b < 35)
            {
                r = 38; g = 40; b = 46;
            }

            double lum = (0.299 * r + 0.587 * g + 0.114 * b);
            bool isLight = lum > 145;

            header.Background = new SolidColorBrush(Color.FromArgb(215, r, g, b));
            cardHoverBorder = new SolidColorBrush(Color.FromArgb(245, r, g, b));

            if (isLight)
            {
                byte br = (byte)Math.Max(0, r - 45);
                byte bg = (byte)Math.Max(0, g - 45);
                byte bb = (byte)Math.Max(0, b - 45);
                header.BorderBrush = new SolidColorBrush(Color.FromArgb(245, br, bg, bb));

                Brush darkText = Frozen(new SolidColorBrush(Color.FromRgb(20, 20, 24)));
                Brush darkSecText = Frozen(new SolidColorBrush(Color.FromArgb(190, 20, 20, 24)));
                headerTitle.Foreground = darkText;
                headerCount.Foreground = darkSecText;
                headerPage.Foreground = darkSecText;
                if (headerPreviousGlyph != null) headerPreviousGlyph.Foreground = darkText;
                if (headerNextGlyph != null) headerNextGlyph.Foreground = darkText;
                headerButtonHover = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
            }
            else
            {
                byte br = (byte)Math.Min(255, r + 40);
                byte bg = (byte)Math.Min(255, g + 40);
                byte bb = (byte)Math.Min(255, b + 40);
                header.BorderBrush = new SolidColorBrush(Color.FromArgb(245, br, bg, bb));

                Brush whiteSecText = Frozen(new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)));
                headerTitle.Foreground = Brushes.White;
                headerCount.Foreground = whiteSecText;
                headerPage.Foreground = whiteSecText;
                if (headerPreviousGlyph != null) headerPreviousGlyph.Foreground = Brushes.White;
                if (headerNextGlyph != null) headerNextGlyph.Foreground = Brushes.White;
                headerButtonHover = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            }
        }

        private void CenterHeader()
        {
            if (header == null) return;
            header.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double headerWidth = header.DesiredSize.Width > 0 ? header.DesiredSize.Width : header.ActualWidth;
            double windowWidth = Double.IsNaN(Width) ? 300 : Width;
            double left = Math.Max(8, (windowWidth - headerWidth) / 2.0);
            Canvas.SetLeft(header, left);
        }

        private void UpdateHeader()
        {
            bool visible = sourceWindows.Count >= 1;
            header.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) return;
            bool paged = pageCount > 1;
            headerTitle.Text = String.IsNullOrWhiteSpace(appName) ? "Janelas" : appName;
            headerCount.Text = sourceWindows.Count == 1 ? "1 janela" : sourceWindows.Count.ToString(CultureInfo.CurrentCulture) + " janelas";
            Visibility navigation = paged ? Visibility.Visible : Visibility.Collapsed;
            headerPageSeparator.Visibility = navigation;
            headerPrevious.Visibility = navigation;
            headerPage.Visibility = navigation;
            headerNext.Visibility = navigation;
            headerPanel.Margin = paged ? new Thickness(12, 0, 6, 0) : new Thickness(12, 0, 12, 0);
            headerPage.Text = (pageIndex + 1).ToString(CultureInfo.CurrentCulture) + "/" + pageCount.ToString(CultureInfo.CurrentCulture);
            double width = Double.IsNaN(Width) ? 300 : Width;
            headerTitle.MaxWidth = Math.Max(60, width - (paged ? 200 : 130));
            CenterHeader();
        }

        private void BuildPage()
        {
            CancelPeek();
            UnregisterThumbnails();
            foreach (Border card in previewCards) root.Children.Remove(card);
            cardWindows.Clear();
            previewCards.Clear();
            cardLabels.Clear();
            cardMinimizeButtons.Clear();
            previewCardBaseTops.Clear();
            previewCardCurrentTops.Clear();
            previewCardTargetTops.Clear();

            int start = pageIndex * pageSize;
            int end = Math.Min(sourceWindows.Count, start + pageSize);
            for (int index = start; index < end; index++)
            {
                int slot = index - start;
                int column = slot % columns;
                int row = slot / columns;
                // A última linha incompleta fica centralizada sobre o ícone.
                int itemsInRow = Math.Min(columns, end - start - row * columns);
                double rowOffset = (columns - itemsInRow) * (cardWidth + 8) / 2;
                double centerColumn = (itemsInRow - 1) / 2.0;
                double edgeDistance = Math.Max(1, centerColumn);
                double centrality = Math.Max(0, 1 - Math.Abs(column - centerColumn) / edgeDistance);
                double centralityLift = sourceWindows.Count > 1 ? centrality * 8 : 0;
                double cardTop = 6 + headerSpace + waveHeadroom + row * (cardHeight + 8) - centralityLift;
                double cardLeft = 8 + rowOffset + column * (cardWidth + 8);

                IntPtr window = sourceWindows[index];
                string title = !String.IsNullOrWhiteSpace(sourceTitles[index]) ? sourceTitles[index] : appName;
                Border card = BuildCard(window, title);
                Canvas.SetLeft(card, cardLeft);
                Canvas.SetTop(card, cardTop);
                cardWindows.Add(window);
                previewCards.Add(card);
                previewCardBaseTops.Add(cardTop);
                previewCardCurrentTops.Add(cardTop);
                previewCardTargetTops.Add(cardTop);
                root.Children.Add(card);
            }
            UpdateCardStates();
            UpdateSelectionVisuals();
            if (IsVisible) RegisterThumbnails();
        }

        private Border BuildCard(IntPtr sourceWindow, string title)
        {
            var card = new Border
            {
                Width = cardWidth,
                Height = cardHeight,
                CornerRadius = new CornerRadius(8),
                Background = CardBackground,
                BorderBrush = CardBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            var titleBar = new Grid();
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            var label = new TextBlock { Text = title, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Margin = new Thickness(11, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            titleBar.Children.Add(label);

            Border minimize = CaptionButton("\uE921", "Minimizar janela", Color.FromArgb(60, 255, 255, 255));
            minimize.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                args.Handled = true;
                ToggleMinimize(sourceWindow);
            };
            Grid.SetColumn(minimize, 1);
            titleBar.Children.Add(minimize);

            Border close = CaptionButton("\uE8BB", "Fechar janela", Color.FromRgb(232, 54, 48));
            close.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                args.Handled = true;
                CloseSourceWindow(sourceWindow);
            };
            Grid.SetColumn(close, 2);
            titleBar.Children.Add(close);

            Grid.SetRow(titleBar, 1);
            grid.Children.Add(titleBar);
            card.Child = grid;
            card.MouseEnter += delegate
            {
                SelectWindow(sourceWindow, false);
                SchedulePeek(sourceWindow);
            };
            card.MouseLeave += delegate
            {
                CancelPeek();
                if (!keyboardMode)
                {
                    selectedWindow = IntPtr.Zero;
                    UpdateSelectionVisuals();
                }
            };
            card.MouseLeftButtonUp += delegate { ActivateAndDismiss(sourceWindow); };
            // Clique do meio fecha a janela, como na barra de tarefas.
            card.MouseUp += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.ChangedButton != MouseButton.Middle) return;
                args.Handled = true;
                CloseSourceWindow(sourceWindow);
            };
            cardLabels.Add(label);
            cardMinimizeButtons.Add(minimize);
            return card;
        }

        private static Border CaptionButton(string glyph, string tip, Color hoverColor)
        {
            var button = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(1, 4, 4, 4),
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
                    Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            var hover = new SolidColorBrush(hoverColor);
            button.MouseEnter += delegate { button.Background = hover; };
            button.MouseLeave += delegate { button.Background = Brushes.Transparent; };
            return button;
        }

        private bool IsMinimizedForUser(IntPtr window)
        {
            // Durante a espiada a janela minimizada fica temporariamente visível;
            // para o usuário ela continua minimizada.
            return window == peekTarget ? peekTargetWasMinimized : WindowCommands.IsMinimized(window);
        }

        private void UpdateCardStates()
        {
            for (int index = 0; index < cardWindows.Count; index++)
            {
                IntPtr window = cardWindows[index];
                bool minimized = IsMinimizedForUser(window);
                Border button = cardMinimizeButtons[index];
                var glyph = button.Child as TextBlock;
                string text = minimized ? "\uE923" : "\uE921";
                if (glyph != null && glyph.Text != text)
                {
                    glyph.Text = text;
                    button.ToolTip = minimized ? "Restaurar janela" : "Minimizar janela";
                }
                double opacity = closingWindows.Contains(window) ? .45 : (minimized ? .72 : 1);
                if (Math.Abs(cardLabels[index].Opacity - opacity) > .01) cardLabels[index].Opacity = opacity;
            }
        }

        private void UpdateSelectionVisuals()
        {
            Brush borderBrush = cardHoverBorder ?? CardSelectedBorder;
            for (int index = 0; index < previewCards.Count; index++)
            {
                bool selected = cardWindows[index] == selectedWindow;
                previewCards[index].BorderBrush = selected ? borderBrush : CardBorder;
                previewCards[index].Background = selected ? CardSelectedBackground : CardBackground;
            }
        }

        private void SelectWindow(IntPtr window, bool fromKeyboard)
        {
            if (!sourceWindows.Contains(window)) return;
            selectedWindow = window;
            if (fromKeyboard)
            {
                int page = sourceWindows.IndexOf(window) / Math.Max(1, pageSize);
                if (page != pageIndex)
                {
                    pageIndex = page;
                    UpdateHeader();
                    BuildPage();
                }
                int slot = cardWindows.IndexOf(window);
                if (slot >= 0)
                {
                    // A onda acompanha a seleção do teclado como acompanha o mouse.
                    previewPointerX = Canvas.GetLeft(previewCards[slot]) + cardWidth / 2;
                    UpdatePreviewWaveTargets();
                    if (!previewWaveTimer.IsEnabled) previewWaveTimer.Start();
                }
                SchedulePeek(window);
            }
            UpdateSelectionVisuals();
        }

        private void ChangePage(int delta)
        {
            if (pageCount <= 1) return;
            int next = ((pageIndex + delta) % pageCount + pageCount) % pageCount;
            if (next == pageIndex) return;
            pageIndex = next;
            UpdateHeader();
            BuildPage();
            if (keyboardMode && cardWindows.Count > 0) SelectWindow(cardWindows[0], true);
        }

        private void ActivateAndDismiss(IntPtr window)
        {
            EndPeek(false);
            lastOwnActionTick = Environment.TickCount;
            WindowCommands.Activate(window);
            Close();
        }

        private void ToggleMinimize(IntPtr window)
        {
            // Minimizada: restaurar leva o usuário até ela, igual a clicar no cartão.
            if (IsMinimizedForUser(window))
            {
                ActivateAndDismiss(window);
                return;
            }
            peekTimer.Stop();
            pendingPeekTarget = IntPtr.Zero;
            EndPeek(peekTarget != window);
            lastOwnActionTick = Environment.TickCount;
            WindowCommands.Minimize(window);
            ScheduleQuickValidation();
        }

        private void CloseSourceWindow(IntPtr window)
        {
            peekTimer.Stop();
            pendingPeekTarget = IntPtr.Zero;
            EndPeek(peekTarget != window);
            lastOwnActionTick = Environment.TickCount;
            closingWindows.Add(window);
            WindowCommands.Close(window);
            // O cartão só sai quando a janela realmente fecha: se o aplicativo
            // perguntar "deseja salvar?", a miniatura continua disponível.
            int slot = cardWindows.IndexOf(window);
            if (slot >= 0) SetThumbnailOpacity(slot, 110);
            UpdateCardStates();
            ScheduleQuickValidation();
            if (WindowsChanged != null) WindowsChanged(this, EventArgs.Empty);
        }

        private void ScheduleQuickValidation()
        {
            validationTimer.Stop();
            validationTimer.Interval = TimeSpan.FromMilliseconds(150);
            if (IsVisible && !isClosed) validationTimer.Start();
        }

        private void ValidateSources()
        {
            validationTimer.Interval = TimeSpan.FromMilliseconds(400);
            if (isClosed) return;
            int selectedIndex = sourceWindows.IndexOf(selectedWindow);
            bool removed = false;
            for (int index = sourceWindows.Count - 1; index >= 0; index--)
            {
                IntPtr window = sourceWindows[index];
                if (WindowCommands.IsAlive(window) && IsWindowVisible(window)) continue;
                if (peekTarget == window) ForgetPeek();
                if (pendingPeekTarget == window) pendingPeekTarget = IntPtr.Zero;
                sourceWindows.RemoveAt(index);
                sourceTitles.RemoveAt(index);
                closingWindows.Remove(window);
                removed = true;
            }
            if (sourceWindows.Count == 0)
            {
                Close();
                return;
            }
            if (removed)
            {
                if (!sourceWindows.Contains(selectedWindow))
                    selectedWindow = keyboardMode && selectedIndex >= 0
                        ? sourceWindows[Math.Min(selectedIndex, sourceWindows.Count - 1)]
                        : IntPtr.Zero;
                ApplyLayout(true);
                if (WindowsChanged != null) WindowsChanged(this, EventArgs.Empty);
            }
            else UpdateCardStates();

            if (!keyboardMode) return;
            // Se o usuário trocou de aplicativo por outro meio enquanto navegava
            // pelo teclado, a prévia sai do caminho.
            IntPtr foreground = GetForegroundWindow();
            if (unchecked(Environment.TickCount - lastOwnActionTick) < 1500) keyboardForeground = foreground;
            else if (foreground != keyboardForeground && !IsMouseOver) Close();
        }

        private void UpdatePreviewWaveTargets()
        {
            if (previewCards.Count < 2) return;
            double radius = cardWidth * 1.30;
            for (int index = 0; index < previewCards.Count; index++)
            {
                double center = Canvas.GetLeft(previewCards[index]) + cardWidth / 2;
                double distance = Math.Abs(center - previewPointerX);
                double influence = distance >= radius
                    ? 0
                    : (Math.Cos(Math.PI * distance / radius) + 1) / 2;
                // A curvatura de repouso deixa o centro levemente elevado. Esta
                // segunda elevação percorre os cartões seguindo o mouse.
                previewCardTargetTops[index] = previewCardBaseTops[index] - 13 * influence;
            }
        }

        private void RenderPreviewWave()
        {
            bool moving = false;
            for (int index = 0; index < previewCards.Count; index++)
            {
                double current = previewCardCurrentTops[index];
                double target = previewCardTargetTops[index];
                double next = current + (target - current) * .30;
                if (Math.Abs(target - next) < .08) next = target;
                else moving = true;
                if (Math.Abs(next - current) < .01) continue;
                previewCardCurrentTops[index] = next;
                Canvas.SetTop(previewCards[index], next);
                UpdateThumbnailVerticalPosition(index, next - previewCardBaseTops[index]);
            }
            if (!moving) previewWaveTimer.Stop();
        }

        private void UpdateThumbnailVerticalPosition(int index, double offset)
        {
            if (index < 0 || index >= thumbnails.Count || index >= thumbnailBaseDestinations.Count) return;
            IntPtr thumbnail = thumbnails[index];
            if (thumbnail == IntPtr.Zero) return;
            NativeRect baseline = thumbnailBaseDestinations[index];
            int shift = (int)Math.Round(offset * thumbnailDpiY);
            baseline.Top += shift;
            baseline.Bottom += shift;
            var properties = new ThumbnailProperties
            {
                Flags = 1, // DWM_TNP_RECTDESTINATION
                Destination = baseline
            };
            DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }

        private void SetThumbnailOpacity(int index, byte opacity)
        {
            if (index < 0 || index >= thumbnails.Count || thumbnails[index] == IntPtr.Zero) return;
            var properties = new ThumbnailProperties { Flags = 4, Opacity = opacity }; // DWM_TNP_OPACITY
            DwmUpdateThumbnailProperties(thumbnails[index], ref properties);
        }

        private void SchedulePeek(IntPtr sourceWindow)
        {
            if (sourceWindow == IntPtr.Zero || sourceWindow == peekTarget || closingWindows.Contains(sourceWindow)) return;
            peekTimer.Stop();
            if (peekTarget != IntPtr.Zero) EndPeek(true);
            pendingPeekTarget = sourceWindow;
            peekTimer.Start();
        }

        private void CancelPeek()
        {
            peekTimer.Stop();
            pendingPeekTarget = IntPtr.Zero;
            EndPeek(true);
        }

        private void BeginPeek(IntPtr sourceWindow)
        {
            EndPeek(true);
            pendingPeekTarget = IntPtr.Zero;
            if (isClosed || !WindowCommands.IsAlive(sourceWindow)) return;
            peekTarget = sourceWindow;
            peekPreviousForeground = GetForegroundWindow();
            peekTargetWasMinimized = IsIconic(sourceWindow);
            peekTargetWasTopmost = (GetWindowLong(sourceWindow, -20) & 0x00000008) != 0; // WS_EX_TOPMOST
            if (peekTargetWasMinimized) ShowWindow(sourceWindow, 4); // SW_SHOWNOACTIVATE
            const uint KeepGeometryNoActivateShow = 0x0001 | 0x0002 | 0x0010 | 0x0040;
            // Inserir primeiro como HWND_TOPMOST fazia a janela cobrir a Dock por
            // um único quadro antes de recolocarmos a prévia acima. Inserimos o
            // aplicativo diretamente atrás da própria Dock: a troca fica atômica
            // aos olhos do DWM, sem piscar e sem roubar o foco.
            IntPtr previewHandle = new WindowInteropHelper(this).Handle;
            IntPtr dockHandle = Owner == null ? IntPtr.Zero : new WindowInteropHelper(Owner).Handle;
            IntPtr insertBehind = dockHandle != IntPtr.Zero ? dockHandle : previewHandle;
            SetWindowPos(sourceWindow, insertBehind, 0, 0, 0, 0, KeepGeometryNoActivateShow);
        }

        private void ForgetPeek()
        {
            peekTarget = IntPtr.Zero;
            peekPreviousForeground = IntPtr.Zero;
            peekTargetWasMinimized = false;
            peekTargetWasTopmost = false;
        }

        private void EndPeek(bool restorePrevious)
        {
            peekTimer.Stop();
            if (peekTarget == IntPtr.Zero) return;
            IntPtr target = peekTarget;
            bool wasMinimized = peekTargetWasMinimized;
            IntPtr previous = peekPreviousForeground;
            bool wasTopmost = peekTargetWasTopmost;
            ForgetPeek();
            if (!WindowCommands.IsAlive(target)) return;

            // Nunca deixa o aplicativo preso em "sempre no topo" depois da
            // visualização temporária ou depois de clicar na miniatura.
            if (!wasTopmost)
                SetWindowPos(target, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_NOTOPMOST
            if (!restorePrevious) return;

            // SW_SHOWMINNOACTIVE devolve a janela ao estado minimizado sem
            // ativar outra janela no lugar, preservando o foco atual.
            if (wasMinimized) ShowWindow(target, 7);
            else if (previous != IntPtr.Zero && previous != target)
                SetWindowPos(previous, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        private void UpdateDpi()
        {
            thumbnailDpiX = 1;
            thumbnailDpiY = 1;
            var presentation = PresentationSource.FromVisual(this);
            if (presentation != null && presentation.CompositionTarget != null)
            {
                thumbnailDpiX = presentation.CompositionTarget.TransformToDevice.M11;
                thumbnailDpiY = presentation.CompositionTarget.TransformToDevice.M22;
            }
        }

        private void RegisterThumbnails()
        {
            UnregisterThumbnails();
            if (isClosed) return;
            IntPtr destination = new WindowInteropHelper(this).Handle;
            if (destination == IntPtr.Zero) return;
            UpdateDpi();
            double dpiX = thumbnailDpiX;
            double dpiY = thumbnailDpiY;
            for (int index = 0; index < cardWindows.Count; index++)
            {
                thumbnails.Add(IntPtr.Zero);
                thumbnailBaseDestinations.Add(default(NativeRect));
                IntPtr thumbnail;
                if (DwmRegisterThumbnail(destination, cardWindows[index], out thumbnail) != 0 || thumbnail == IntPtr.Zero) continue;
                thumbnails[index] = thumbnail;
                NativeSize sourceSize;
                DwmQueryThumbnailSourceSize(thumbnail, out sourceSize);
                int availableWidth = Math.Max(1, (int)Math.Round((cardWidth - 16) * dpiX));
                int availableHeight = Math.Max(1, (int)Math.Round((cardHeight - 46) * dpiY));
                double scale = Math.Min((double)availableWidth / Math.Max(1, sourceSize.Width), (double)availableHeight / Math.Max(1, sourceSize.Height));
                int width = Math.Max(1, (int)(sourceSize.Width * scale));
                int height = Math.Max(1, (int)(sourceSize.Height * scale));
                int cardLeft = (int)Math.Round(Canvas.GetLeft(previewCards[index]) * dpiX);
                int cardTop = (int)Math.Round(previewCardBaseTops[index] * dpiY);
                int innerPaddingX = (int)Math.Round(8 * dpiX);
                int innerPaddingY = (int)Math.Round(4 * dpiY);
                int left = cardLeft + innerPaddingX + (availableWidth - width) / 2;
                int top = cardTop + innerPaddingY + (availableHeight - height) / 2;
                NativeRect destinationRect = new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height };
                thumbnailBaseDestinations[index] = destinationRect;
                // A onda pode estar em andamento: aplica o deslocamento atual.
                int shift = (int)Math.Round((previewCardCurrentTops[index] - previewCardBaseTops[index]) * dpiY);
                destinationRect.Top += shift;
                destinationRect.Bottom += shift;
                byte opacity = closingWindows.Contains(cardWindows[index]) ? (byte)110 : (byte)255;
                var properties = new ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Destination = destinationRect, Opacity = opacity, Visible = true, SourceClientAreaOnly = false };
                DwmUpdateThumbnailProperties(thumbnail, ref properties);
            }
        }

        private void UnregisterThumbnails()
        {
            foreach (IntPtr thumbnail in thumbnails) if (thumbnail != IntPtr.Zero) DwmUnregisterThumbnail(thumbnail);
            thumbnails.Clear();
            thumbnailBaseDestinations.Clear();
        }

        private void InstallKeyboardHook()
        {
            if (keyboardHook != IntPtr.Zero || isClosed) return;
            keyboardProc = KeyboardHookCallback;
            keyboardHook = SetWindowsHookEx(13, keyboardProc, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
        }

        private void RemoveKeyboardHook()
        {
            if (keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(keyboardHook);
                keyboardHook = IntPtr.Zero;
            }
            keyboardProc = null;
            capturedKeys.Clear();
            keyboardMode = false;
        }

        private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !isClosed)
            {
                try
                {
                    int message = wParam.ToInt32();
                    int virtualKey = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
                    bool keyDown = message == 0x0100 || message == 0x0104;
                    bool keyUp = message == 0x0101 || message == 0x0105;
                    // A soltura de uma tecla consumida também é consumida, para o
                    // aplicativo em foco nunca receber um "key up" órfão.
                    if (keyUp && capturedKeys.Remove(virtualKey)) return new IntPtr(1);
                    if (keyDown && ShouldCaptureKey(virtualKey, message))
                    {
                        capturedKeys.Add(virtualKey);
                        bool shift = IsKeyPressed(0x10);
                        int key = virtualKey;
                        // O gancho precisa responder rápido; a ação roda logo em seguida.
                        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { HandleKey(key, shift); }));
                        return new IntPtr(1);
                    }
                }
                catch { }
            }
            return CallNextHookEx(keyboardHook, code, wParam, lParam);
        }

        private bool ShouldCaptureKey(int virtualKey, int message)
        {
            if (message == 0x0104) return false; // WM_SYSKEYDOWN: Alt pressionado
            // Atalhos com Ctrl, Alt ou Windows continuam indo para o sistema.
            if (IsKeyPressed(0x11) || IsKeyPressed(0x12) || IsKeyPressed(0x5B) || IsKeyPressed(0x5C)) return false;
            bool startsNavigation = virtualKey == 0x25 || virtualKey == 0x26 || virtualKey == 0x27 || virtualKey == 0x28 ||
                virtualKey == 0x24 || virtualKey == 0x23 || virtualKey == 0x21 || virtualKey == 0x22;
            bool modeKey = virtualKey == 0x09 || virtualKey == 0x0D || virtualKey == 0x20 ||
                virtualKey == 0x2E || virtualKey == 0x1B || virtualKey == 0x4D;
            if (keyboardMode) return startsNavigation || modeKey;
            // Fora do modo teclado, só setas/Home/End/PgUp/PgDn com o cursor
            // sobre a Dock ou a prévia iniciam a navegação. Digitação comum no
            // aplicativo em foco nunca é interceptada.
            if (!startsNavigation || !IsVisible || !IsCursorOverDockOrPreview()) return false;
            keyboardMode = true;
            keyboardForeground = GetForegroundWindow();
            lastOwnActionTick = Environment.TickCount;
            return true;
        }

        private bool IsCursorOverDockOrPreview()
        {
            CursorPoint cursor;
            if (!GetCursorPos(out cursor)) return false;
            if (WindowContains(new WindowInteropHelper(this).Handle, cursor)) return true;
            return Owner != null && WindowContains(new WindowInteropHelper(Owner).Handle, cursor);
        }

        private static bool WindowContains(IntPtr window, CursorPoint point)
        {
            NativeRect rect;
            if (window == IntPtr.Zero || !GetWindowRect(window, out rect)) return false;
            return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
        }

        private static bool IsKeyPressed(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private void HandleKey(int virtualKey, bool shift)
        {
            if (isClosed || sourceWindows.Count == 0) return;
            int count = sourceWindows.Count;
            int index = sourceWindows.IndexOf(selectedWindow);
            int target;
            switch (virtualKey)
            {
                case 0x25: // Esquerda
                    target = index < 0 ? 0 : (index - 1 + count) % count;
                    break;
                case 0x27: // Direita
                    target = index < 0 ? 0 : (index + 1) % count;
                    break;
                case 0x09: // Tab / Shift+Tab
                    target = index < 0 ? 0 : (index + (shift ? count - 1 : 1)) % count;
                    break;
                case 0x26: // Cima
                    target = index < 0 ? 0 : (index - columns >= 0 ? index - columns : index);
                    break;
                case 0x28: // Baixo
                    target = index < 0 ? 0 : (index + columns < count ? index + columns : index);
                    break;
                case 0x24: // Home
                    target = 0;
                    break;
                case 0x23: // End
                    target = count - 1;
                    break;
                case 0x21: // Page Up
                    target = index < 0 ? 0 : Math.Max(0, index - pageSize);
                    break;
                case 0x22: // Page Down
                    target = index < 0 ? 0 : Math.Min(count - 1, index + pageSize);
                    break;
                case 0x0D: // Enter
                case 0x20: // Espaço
                    if (index >= 0) ActivateAndDismiss(selectedWindow);
                    return;
                case 0x2E: // Delete
                    if (index >= 0) CloseSourceWindow(selectedWindow);
                    return;
                case 0x4D: // M
                    if (index >= 0) ToggleMinimize(selectedWindow);
                    return;
                case 0x1B: // Esc
                    Close();
                    return;
                default:
                    return;
            }
            if (target >= 0 && target < count) SelectWindow(sourceWindows[target], true);
        }

        private static Brush Frozen(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int Width, Height; }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorPoint { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ThumbnailProperties
        {
            public int Flags;
            public NativeRect Destination;
            public NativeRect Source;
            public byte Opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
            [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);

        [DllImport("dwmapi.dll")]
        private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

        [DllImport("dwmapi.dll")]
        private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);

        [DllImport("dwmapi.dll")]
        private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out NativeSize size);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint point);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);
    }

    internal static class MacCursorService
    {
        private const uint IMAGE_CURSOR = 2;
        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        private const uint OCR_NORMAL = 32512;
        private const uint OCR_IBEAM = 32513;
        private const uint OCR_WAIT = 32514;
        private const uint OCR_CROSS = 32515;
        private const uint OCR_UP = 32516;
        private const uint OCR_SIZENWSE = 32642;
        private const uint OCR_SIZENESW = 32643;
        private const uint OCR_SIZEWE = 32644;
        private const uint OCR_SIZENS = 32645;
        private const uint OCR_SIZEALL = 32646;
        private const uint OCR_NO = 32648;
        private const uint OCR_HAND = 32649;
        private const uint OCR_APPSTARTING = 32650;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadCursorFromFile(string lpFileName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CopyImage(IntPtr h, uint type, int cx, int cy, uint flags);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const string CursorsRegKey = @"Control Panel\Cursors";
        private const string BackupRegKey = @"Control Panel\Cursors\BMFlow_Original_Backup";

        public static bool IsEnabled { get; set; }
        public static string CurrentTheme { get; set; }
        public static string CurrentSize { get; set; }

        static MacCursorService()
        {
            CurrentTheme = "macOS";
            CurrentSize = "Regular";
        }

        private static readonly Dictionary<string, uint> RoleToOcr = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            { "Arrow", OCR_NORMAL },
            { "IBeam", OCR_IBEAM },
            { "Wait", OCR_WAIT },
            { "Crosshair", OCR_CROSS },
            { "UpArrow", OCR_UP },
            { "SizeNWSE", OCR_SIZENWSE },
            { "SizeNESW", OCR_SIZENESW },
            { "SizeWE", OCR_SIZEWE },
            { "SizeNS", OCR_SIZENS },
            { "SizeAll", OCR_SIZEALL },
            { "No", OCR_NO },
            { "Hand", OCR_HAND },
            { "AppStarting", OCR_APPSTARTING }
        };

        private static readonly Dictionary<string, string[]> RoleCandidates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Arrow", new[] { "arrow.cur", "pointer.cur", "Arrow.cur", "Pointer.cur" } },
            { "Help", new[] { "help.cur", "Help.cur" } },
            { "AppStarting", new[] { "appstarting.ani", "work.ani", "working.ani", "appstarting.cur", "work.cur" } },
            { "Wait", new[] { "wait.ani", "busy.ani", "wait.cur", "busy.cur" } },
            { "Crosshair", new[] { "crosshair.cur", "cross.cur", "precisionhair.cur", "Crosshair.cur", "Cross.cur" } },
            { "IBeam", new[] { "ibeam.cur", "text.cur", "IBeam.cur", "Text.cur" } },
            { "NWPen", new[] { "nwpen.cur", "handwriting.cur", "pen.cur" } },
            { "No", new[] { "no.cur", "unavailiable.cur", "unavailable.cur", "No.cur" } },
            { "SizeNS", new[] { "sizens.cur", "vert.cur", "SizeNS.cur", "Vert.cur" } },
            { "SizeWE", new[] { "sizewe.cur", "horz.cur", "SizeWE.cur", "Horz.cur" } },
            { "SizeNWSE", new[] { "sizenwse.cur", "dng1.cur" } },
            { "SizeNESW", new[] { "sizenesw.cur", "dng2.cur" } },
            { "SizeAll", new[] { "sizeall.cur", "move.cur", "SizeAll.cur", "Move.cur" } },
            { "UpArrow", new[] { "uparrow.cur", "alternate.cur", "UpArrow.cur" } },
            { "Hand", new[] { "hand.cur", "link.cur", "Hand.cur", "Link.cur" } },
            { "Pin", new[] { "pin.cur", "Pin.cur" } },
            { "Person", new[] { "person.cur", "Person.cur" } }
        };

        private static readonly Dictionary<string, ImageSource> PreviewCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static string GetFolderForSize(string size)
        {
            if (String.Equals(size, "Grande", StringComparison.OrdinalIgnoreCase) || String.Equals(size, "Large", StringComparison.OrdinalIgnoreCase))
                return "macOS-Large-Windows";
            if (String.Equals(size, "Extra Grande", StringComparison.OrdinalIgnoreCase) || String.Equals(size, "ExtraLarge", StringComparison.OrdinalIgnoreCase))
                return "macOS-Extra-Large-Windows";
            return "macOS-Regular-Windows";
        }

        public static string ResolveThemeDir(string theme, string size = "Regular")
        {
            string rel = "";
            if (String.Equals(theme, "FluentDark", StringComparison.OrdinalIgnoreCase))
                rel = System.IO.Path.Combine("Cursors", "DarkCursors");
            else if (String.Equals(theme, "FluentLight", StringComparison.OrdinalIgnoreCase))
                rel = System.IO.Path.Combine("Cursors", "LightCursors");
            else if (String.Equals(theme, "FluentArteffect", StringComparison.OrdinalIgnoreCase))
                rel = System.IO.Path.Combine("Cursors", "Fluent-v3-Arteffect");
            else
                rel = System.IO.Path.Combine("Cursors", "macOS", GetFolderForSize(size));

            string appDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rel);
            if (Directory.Exists(appDir)) return appDir;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDataDir = System.IO.Path.Combine(appData, "BMFlow", rel);
            if (Directory.Exists(appDataDir)) return appDataDir;

            return appDir;
        }

        public static bool HasCursors(string dir)
        {
            if (String.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            try
            {
                return Directory.GetFiles(dir, "*.cur").Length > 0 || Directory.GetFiles(dir, "*.ani").Length > 0;
            }
            catch { return false; }
        }

        public static string FindCursorFile(string dir, string role)
        {
            if (String.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            string[] candidates;
            if (RoleCandidates.TryGetValue(role, out candidates))
            {
                foreach (string candidate in candidates)
                {
                    string full = System.IO.Path.Combine(dir, candidate);
                    if (File.Exists(full)) return full;
                }
            }
            try
            {
                foreach (string file in Directory.GetFiles(dir))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (String.Equals(name, role, StringComparison.OrdinalIgnoreCase)) return file;
                }
            }
            catch { }
            return null;
        }

        private static void BackupOriginalCursors()
        {
            try
            {
                using (RegistryKey src = Registry.CurrentUser.OpenSubKey(CursorsRegKey, false))
                {
                    if (src == null) return;
                    using (RegistryKey dst = Registry.CurrentUser.CreateSubKey(BackupRegKey))
                    {
                        if (dst == null) return;
                        if (dst.ValueCount == 0)
                        {
                            foreach (string valName in src.GetValueNames())
                            {
                                object val = src.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                                RegistryValueKind kind = src.GetValueKind(valName);
                                if (val != null) dst.SetValue(valName, val, kind);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static void Apply(string theme = null, string size = null)
        {
            if (!String.IsNullOrWhiteSpace(theme)) CurrentTheme = theme;
            if (!String.IsNullOrWhiteSpace(size)) CurrentSize = size;
            string dir = ResolveThemeDir(CurrentTheme, CurrentSize);
            if (!Directory.Exists(dir) || !HasCursors(dir)) return;

            BackupOriginalCursors();

            try
            {
                string schemeName = "BMFlow (" + CurrentTheme + ")";
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CursorsRegKey))
                {
                    if (key != null)
                    {
                        key.SetValue("", schemeName, RegistryValueKind.String);
                        key.SetValue("Scheme Source", 1, RegistryValueKind.DWord);
                        foreach (string role in RoleCandidates.Keys)
                        {
                            string file = FindCursorFile(dir, role);
                            if (!String.IsNullOrEmpty(file))
                            {
                                key.SetValue(role, file, RegistryValueKind.ExpandString);
                            }
                        }
                    }
                }

                foreach (var pair in RoleToOcr)
                {
                    string filePath = FindCursorFile(dir, pair.Key);
                    if (!String.IsNullOrEmpty(filePath))
                    {
                        IntPtr hCur = LoadCursorFromFile(filePath);
                        if (hCur != IntPtr.Zero)
                        {
                            IntPtr hCopy = CopyImage(hCur, IMAGE_CURSOR, 0, 0, 0);
                            if (hCopy != IntPtr.Zero)
                            {
                                SetSystemCursor(hCopy, pair.Value);
                            }
                            DestroyIcon(hCur);
                        }
                    }
                }

                SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                IsEnabled = true;
            }
            catch { }
        }

        public static void Restore()
        {
            try
            {
                using (RegistryKey backup = Registry.CurrentUser.OpenSubKey(BackupRegKey, false))
                using (RegistryKey dst = Registry.CurrentUser.CreateSubKey(CursorsRegKey))
                {
                    if (backup != null && dst != null && backup.ValueCount > 0)
                    {
                        foreach (string role in RoleCandidates.Keys)
                        {
                            dst.DeleteValue(role, false);
                        }
                        foreach (string valName in backup.GetValueNames())
                        {
                            object val = backup.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            RegistryValueKind kind = backup.GetValueKind(valName);
                            if (val != null) dst.SetValue(valName, val, kind);
                        }
                        try { Registry.CurrentUser.DeleteSubKeyTree(BackupRegKey, false); } catch { }
                    }
                    else if (dst != null)
                    {
                        foreach (string role in RoleCandidates.Keys)
                        {
                            dst.SetValue(role, "", RegistryValueKind.String);
                        }
                        dst.SetValue("", "Windows Padrão", RegistryValueKind.String);
                        dst.SetValue("Scheme Source", 0, RegistryValueKind.DWord);
                    }
                }

                SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                IsEnabled = false;
            }
            catch { }
        }

        public static ImageSource LoadCursorImage(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                lock (PreviewCache)
                {
                    ImageSource cached;
                    if (PreviewCache.TryGetValue(filePath, out cached)) return cached;
                }
                IntPtr hCur = LoadCursorFromFile(filePath);
                if (hCur == IntPtr.Zero) return null;
                try
                {
                    BitmapSource src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(hCur, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    lock (PreviewCache)
                    {
                        PreviewCache[filePath] = src;
                    }
                    return src;
                }
                finally
                {
                    DestroyIcon(hCur);
                }
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class WorkspaceAppItem
    {
        public string Name;
        public string Target;
        public string Arguments;
        public string AppUserModelId;
        public string Identity;

        public void NormalizeExecutable()
        {
            if (String.Equals(Target, "explorer.exe", StringComparison.OrdinalIgnoreCase) || String.IsNullOrWhiteSpace(Target))
            {
                string candidate = Arguments ?? String.Empty;
                if (candidate.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidate.Substring("shell:AppsFolder\\".Length);
                }
                if (File.Exists(candidate))
                {
                    Target = candidate;
                    Arguments = String.Empty;
                    return;
                }
                if (!String.IsNullOrWhiteSpace(Identity) && File.Exists(Identity))
                {
                    Target = Identity;
                    Arguments = String.Empty;
                    return;
                }

                string lowerName = (Name ?? String.Empty).ToLowerInvariant();
                string lowerId = (AppUserModelId ?? Identity ?? String.Empty).ToLowerInvariant();

                if (lowerName.Contains("edge") || lowerId.Contains("msedge"))
                {
                    string edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
                    if (File.Exists(edgePath)) { Target = edgePath; Arguments = String.Empty; return; }
                    edgePath = @"C:\Program Files\Microsoft\Edge\Application\msedge.exe";
                    if (File.Exists(edgePath)) { Target = edgePath; Arguments = String.Empty; return; }
                }
                if (lowerName.Contains("steam") || lowerId.Contains("steam"))
                {
                    string steamPath = @"C:\Program Files (x86)\Steam\steam.exe";
                    if (File.Exists(steamPath)) { Target = steamPath; Arguments = String.Empty; return; }
                    steamPath = @"C:\Program Files\Steam\steam.exe";
                    if (File.Exists(steamPath)) { Target = steamPath; Arguments = String.Empty; return; }
                }
                if (lowerName.Contains("obs") || lowerId.Contains("obs"))
                {
                    string obsPath = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";
                    if (File.Exists(obsPath)) { Target = obsPath; Arguments = String.Empty; return; }
                }
                if (lowerName.Contains("spotify") || lowerId.Contains("spotify"))
                {
                    string spotPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
                    if (File.Exists(spotPath)) { Target = spotPath; Arguments = String.Empty; return; }
                }
            }

            if (!String.IsNullOrWhiteSpace(AppUserModelId) && (AppUserModelId.StartsWith("{") || AppUserModelId.Contains(":") || AppUserModelId.Contains("\\")))
            {
                if (File.Exists(Target))
                {
                    AppUserModelId = String.Empty;
                }
            }
        }

        public string Serialize()
        {
            return String.Format("{0}|{1}|{2}|{3}|{4}",
                Escape(Name), Escape(Target), Escape(Arguments), Escape(AppUserModelId), Escape(Identity));
        }

        public static WorkspaceAppItem Deserialize(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return null;
            string[] parts = raw.Split('|');
            if (parts.Length == 0) return null;
            var item = new WorkspaceAppItem
            {
                Name = Unescape(parts[0]),
                Target = parts.Length > 1 ? Unescape(parts[1]) : String.Empty,
                Arguments = parts.Length > 2 ? Unescape(parts[2]) : String.Empty,
                AppUserModelId = parts.Length > 3 ? Unescape(parts[3]) : String.Empty,
                Identity = parts.Length > 4 ? Unescape(parts[4]) : String.Empty
            };
            item.NormalizeExecutable();
            return item;
        }

        private static string Escape(string s) { return Uri.EscapeDataString(s ?? String.Empty); }
        private static string Unescape(string s) { try { return Uri.UnescapeDataString(s ?? String.Empty); } catch { return s ?? String.Empty; } }
    }

    internal sealed class DockWorkspace
    {
        public string Id;
        public string Name;
        public string Icon;
        public bool IsolateWorkspace;
        public bool SyncWithWindows;
        public readonly List<WorkspaceAppItem> Apps = new List<WorkspaceAppItem>();

        public DockWorkspace()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Name = "Espaço";
            Icon = "💼";
            IsolateWorkspace = false;
            SyncWithWindows = false;
        }

        public DockWorkspace(string id, string name, string icon, bool isolate = false, bool syncWithWindows = false)
        {
            Id = id;
            Name = name;
            Icon = icon;
            IsolateWorkspace = isolate;
            SyncWithWindows = syncWithWindows;
        }
    }

    internal sealed class WorkspaceManager
    {
        public readonly List<DockWorkspace> Workspaces = new List<DockWorkspace>();
        public int ActiveIndex = 0;
        public bool WorkspacesEnabled = true;

        public DockWorkspace ActiveWorkspace
        {
            get
            {
                if (Workspaces.Count == 0) return null;
                if (ActiveIndex < 0 || ActiveIndex >= Workspaces.Count) ActiveIndex = 0;
                return Workspaces[ActiveIndex];
            }
        }

        private static string WorkspacesFilePath
        {
            get
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMFlow");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "workspaces.ini");
            }
        }

        public void Save()
        {
            try
            {
                string file = WorkspacesFilePath;
                var lines = new List<string>();
                lines.Add("# B.M Flow Workspaces configuration");
                lines.Add("ActiveIndex=" + ActiveIndex.ToString(CultureInfo.InvariantCulture));
                lines.Add("WorkspacesEnabled=" + WorkspacesEnabled.ToString());
                lines.Add("Count=" + Workspaces.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < Workspaces.Count; i++)
                {
                    var ws = Workspaces[i];
                    lines.Add(String.Format("[Workspace_{0}]", i));
                    lines.Add("Id=" + ws.Id);
                    lines.Add("Name=" + ws.Name);
                    lines.Add("Icon=" + ws.Icon);
                    lines.Add("Isolate=" + ws.IsolateWorkspace.ToString());
                    lines.Add("SyncWithWindows=" + ws.SyncWithWindows.ToString());
                    lines.Add("AppCount=" + ws.Apps.Count.ToString(CultureInfo.InvariantCulture));
                    for (int a = 0; a < ws.Apps.Count; a++)
                    {
                        lines.Add(String.Format("App_{0}={1}", a, ws.Apps[a].Serialize()));
                    }
                }
                File.WriteAllLines(file, lines, Encoding.UTF8);
            }
            catch { }
        }

        public void Load(DockWindow dock)
        {
            try
            {
                string file = WorkspacesFilePath;
                if (!File.Exists(file))
                {
                    CreateDefaultWorkspaces(dock);
                    Save();
                    return;
                }

                var lines = File.ReadAllLines(file, Encoding.UTF8);
                Workspaces.Clear();
                DockWorkspace currentWs = null;
                int activeIdx = 0;
                bool enabled = true;

                foreach (var rawLine in lines)
                {
                    string line = (rawLine ?? String.Empty).Trim();
                    if (line.StartsWith("#") || String.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        if (currentWs != null) Workspaces.Add(currentWs);
                        currentWs = new DockWorkspace();
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (currentWs == null)
                    {
                        if (key.Equals("ActiveIndex", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out activeIdx);
                        }
                        else if (key.Equals("WorkspacesEnabled", StringComparison.OrdinalIgnoreCase))
                        {
                            bool.TryParse(val, out enabled);
                        }
                    }
                    else
                    {
                        if (key.Equals("Id", StringComparison.OrdinalIgnoreCase)) currentWs.Id = val;
                        else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) currentWs.Name = val;
                        else if (key.Equals("Icon", StringComparison.OrdinalIgnoreCase)) currentWs.Icon = val;
                        else if (key.Equals("Isolate", StringComparison.OrdinalIgnoreCase))
                        {
                            bool iso;
                            if (bool.TryParse(val, out iso)) currentWs.IsolateWorkspace = iso;
                        }
                        else if (key.Equals("SyncWithWindows", StringComparison.OrdinalIgnoreCase))
                        {
                            bool sync;
                            if (bool.TryParse(val, out sync)) currentWs.SyncWithWindows = sync;
                        }
                        else if (key.StartsWith("App_", StringComparison.OrdinalIgnoreCase))
                        {
                            var app = WorkspaceAppItem.Deserialize(val);
                            if (app != null) currentWs.Apps.Add(app);
                        }
                    }
                }

                if (currentWs != null) Workspaces.Add(currentWs);

                if (Workspaces.Count == 0)
                {
                    CreateDefaultWorkspaces(dock);
                    Save();
                    return;
                }

                // Garante que o espaço Padrão (que puxa os apps da barra original do Windows) esteja presente
                if (!Workspaces.Any(w => String.Equals(w.Id, "default", StringComparison.OrdinalIgnoreCase) || w.SyncWithWindows))
                {
                    var wsDefault = new DockWorkspace("default", "Padrão", "🪟", false, true);
                    Workspaces.Insert(0, wsDefault);
                    activeIdx = 0;
                    Save();
                }

                ActiveIndex = Math.Max(0, Math.Min(activeIdx, Workspaces.Count - 1));
                WorkspacesEnabled = enabled;
            }
            catch
            {
                if (Workspaces.Count == 0) CreateDefaultWorkspaces(dock);
            }
        }

        private void CreateDefaultWorkspaces(DockWindow dock)
        {
            Workspaces.Clear();

            var wsDefault = new DockWorkspace("default", "Padrão", "🪟", false, true);
            var wsWork = new DockWorkspace("work", "Trabalho", "💼");
            var wsStudy = new DockWorkspace("study", "Estudo", "📚");
            var wsGames = new DockWorkspace("games", "Jogos", "🎮");

            // 1. Trabalho: se houver atalhos fixados atualmente na barra, importa-os!
            if (dock != null)
            {
                var pinnedInitial = dock.GetInitialPinnedForWorkspace();
                if (pinnedInitial != null && pinnedInitial.Count > 0)
                {
                    wsWork.Apps.AddRange(pinnedInitial);
                }
            }

            var installed = GetInstalledApplications();

            // Se wsWork estiver vazio, busca apps de produtividade
            if (wsWork.Apps.Count == 0)
            {
                AddAppIfFound(wsWork, installed, "Visual Studio Code", "Code");
                AddAppIfFound(wsWork, installed, "Brave", "Google Chrome", "Microsoft Edge");
                AddAppIfFound(wsWork, installed, "Terminal", "PowerShell", "Prompt de Comando");
                AddAppIfFound(wsWork, installed, "Explorador de Arquivos", "File Explorer");
            }

            // 2. Estudo
            AddAppIfFound(wsStudy, installed, "Microsoft Edge", "Brave", "Google Chrome");
            AddAppIfFound(wsStudy, installed, "OneNote", "Bloco de Notas", "Notepad");
            AddAppIfFound(wsStudy, installed, "Calculadora", "Calculator");
            AddAppIfFound(wsStudy, installed, "Word", "Documentos");

            // 3. Jogos
            AddAppIfFound(wsGames, installed, "Steam");
            AddAppIfFound(wsGames, installed, "Discord");
            AddAppIfFound(wsGames, installed, "OBS Studio", "OBS");
            AddAppIfFound(wsGames, installed, "Spotify");

            Workspaces.Add(wsDefault);
            Workspaces.Add(wsWork);
            Workspaces.Add(wsStudy);
            Workspaces.Add(wsGames);
            ActiveIndex = 0;
            WorkspacesEnabled = true;
        }

        private static void AddAppIfFound(DockWorkspace ws, List<WorkspaceAppItem> catalog, params string[] names)
        {
            if (catalog == null) return;
            foreach (var n in names)
            {
                var match = catalog.FirstOrDefault(a => (a.Name ?? "").IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        (a.Target ?? "").IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null && !ws.Apps.Any(a => String.Equals(a.Name, match.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    ws.Apps.Add(new WorkspaceAppItem
                    {
                        Name = match.Name,
                        Target = match.Target,
                        Arguments = match.Arguments,
                        AppUserModelId = match.AppUserModelId,
                        Identity = match.Identity
                    });
                    return;
                }
            }
        }

        private static List<WorkspaceAppItem> cachedInstalledApps;
        private static DateTime cachedInstalledAppsExpires = DateTime.MinValue;

        public static List<WorkspaceAppItem> GetInstalledApplications()
        {
            if (cachedInstalledApps != null && DateTime.UtcNow < cachedInstalledAppsExpires)
                return cachedInstalledApps;

            var list = new List<WorkspaceAppItem>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Packaged Apps
            try
            {
                var packaged = DockWindow.ReadPackagedAppsPublic();
                foreach (var p in packaged)
                {
                    if (String.IsNullOrWhiteSpace(p.Name) || IsNoiseAppName(p.Name)) continue;
                    string key = "pkg:" + (p.AppUserModelId ?? p.Identity ?? p.Name);
                    if (!seenKeys.Contains(key))
                    {
                        seenKeys.Add(key);
                        list.Add(new WorkspaceAppItem
                        {
                            Name = p.Name,
                            Target = p.Target ?? "explorer.exe",
                            Arguments = p.Arguments,
                            AppUserModelId = p.AppUserModelId ?? p.Identity,
                            Identity = p.Identity
                        });
                    }
                }
            }
            catch { }

            // 2. Start Menu Shortcuts
            foreach (string folder in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            })
            {
                if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                try
                {
                    foreach (string lnk in Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories))
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(lnk);
                        if (IsNoiseAppName(name)) continue;

                        var resolved = DockWindow.ResolveShortcutPublic(lnk);
                        if (resolved != null && !String.IsNullOrWhiteSpace(resolved.Target))
                        {
                            if (IsNoiseAppTarget(resolved.Target)) continue;
                            string key = resolved.Target.ToLowerInvariant();
                            if (!seenKeys.Contains(key) && !seenKeys.Contains("name:" + name.ToLowerInvariant()))
                            {
                                seenKeys.Add(key);
                                seenKeys.Add("name:" + name.ToLowerInvariant());
                                list.Add(new WorkspaceAppItem
                                {
                                    Name = String.IsNullOrWhiteSpace(resolved.Name) ? name : resolved.Name,
                                    Target = resolved.Target,
                                    Arguments = resolved.Arguments
                                });
                            }
                        }
                    }
                }
                catch { }
            }

            var result = list.OrderBy(a => a.Name).ToList();
            cachedInstalledApps = result;
            cachedInstalledAppsExpires = DateTime.UtcNow.AddMinutes(10);
            return result;
        }

        private static bool IsNoiseAppName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return true;
            string n = name.ToLowerInvariant();
            return n.Contains("uninstall") || n.Contains("desinstalar") ||
                   n.Contains("readme") || n.Contains("leia-me") ||
                   n.Contains("help") || n.Contains("ajuda") ||
                   n.Contains("manual") || n.Contains("documentation") ||
                   n.Contains("license") || n.Contains("licença") ||
                   n.Contains("release notes") || n.Contains("web site") ||
                   n.Contains("guia do usuário") || n.EndsWith(".url");
        }

        private static bool IsNoiseAppTarget(string target)
        {
            if (String.IsNullOrWhiteSpace(target)) return true;
            string t = target.ToLowerInvariant();
            return t.EndsWith(".url") || t.Contains("unins") || t.Contains("setup") || t.Contains("installer");
        }
    }

    internal sealed class WorkspaceHudWindow : Window
    {
        private static WorkspaceHudWindow activeHud;
        private readonly DispatcherTimer fadeTimer;
        private readonly TextBlock iconBlock;
        private readonly TextBlock titleBlock;
        private readonly TextBlock subBlock;

        public WorkspaceHudWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Focusable = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 28, 28, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22, 14, 26, 14),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Opacity = 0.5,
                    Color = Colors.Black
                }
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            iconBlock = new TextBlock
            {
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            sp.Children.Add(iconBlock);

            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleBlock = new TextBlock
            {
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            textPanel.Children.Add(titleBlock);

            subBlock = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 185)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            textPanel.Children.Add(subBlock);

            sp.Children.Add(textPanel);
            border.Child = sp;
            Content = border;

            fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            fadeTimer.Tick += delegate
            {
                fadeTimer.Stop();
                var anim = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(250)));
                anim.Completed += delegate { Close(); if (activeHud == this) activeHud = null; };
                BeginAnimation(OpacityProperty, anim);
            };
        }

        public static void ShowHud(string icon, string title, string subtitle, double dockLeft, double dockWidth, double dockTop)
        {
            if (activeHud != null)
            {
                try { activeHud.Close(); } catch { }
                activeHud = null;
            }

            var hud = new WorkspaceHudWindow();
            activeHud = hud;
            hud.iconBlock.Text = icon;
            hud.titleBlock.Text = title;
            hud.subBlock.Text = subtitle;

            hud.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = hud.DesiredSize.Width > 0 ? hud.DesiredSize.Width : 220;
            double h = hud.DesiredSize.Height > 0 ? hud.DesiredSize.Height : 70;

            hud.Left = dockLeft + (dockWidth - w) / 2;
            hud.Top = Math.Max(40, dockTop - h - 25);
            hud.Opacity = 0;
            hud.Show();

            var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(150)));
            hud.BeginAnimation(OpacityProperty, anim);
            hud.fadeTimer.Start();
        }
    }

    internal sealed class DesktopWidgetWindow : Window
    {
        private readonly DockWindow ownerDock;
        private readonly DispatcherTimer timer;
        private readonly TextBlock timeBlock;
        private readonly TextBlock dayBlock;
        private readonly TextBlock dateBlock;
        private readonly StackPanel textStack;
        private readonly Border rootBorder;
        private readonly ScaleTransform scaleTransform;

        private bool isLocked;
        private bool showSeconds;
        private bool use24Hour;
        private string currentLanguage;
        private string currentTheme;
        private double currentScale;

        public bool IsLocked { get { return isLocked; } }
        public bool ShowSeconds { get { return showSeconds; } }
        public bool Use24Hour { get { return use24Hour; } }
        public string CurrentLanguage { get { return currentLanguage; } }
        public string CurrentTheme { get { return currentTheme; } }
        public double CurrentScale { get { return currentScale; } }

        public DesktopWidgetWindow(DockWindow owner, double initialX, double initialY, bool locked, bool seconds, bool h24, string language, string theme, double scale)
        {
            ownerDock = owner;
            isLocked = locked;
            showSeconds = seconds;
            use24Hour = h24;
            currentLanguage = String.IsNullOrWhiteSpace(language) ? "Inglês" : language;
            currentTheme = String.IsNullOrWhiteSpace(theme) ? "Ciano da Referência" : theme;
            currentScale = scale > 0.4 ? scale : 1.0;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Focusable = false;

            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            scaleTransform = new ScaleTransform(currentScale, currentScale);

            var fontFamily = new FontFamily("Century Gothic, Segoe UI Light, Segoe UI Variable Display, Aptos Light, Segoe UI, sans-serif");

            timeBlock = new TextBlock
            {
                FontSize = 24,
                FontFamily = fontFamily,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(6, 0, 0, 0)
            };

            dayBlock = new TextBlock
            {
                FontSize = 74,
                FontFamily = fontFamily,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -8, 0, -8)
            };

            dateBlock = new TextBlock
            {
                FontSize = 26,
                FontFamily = fontFamily,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0)
            };

            textStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                ClipToBounds = false,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            textStack.Children.Add(timeBlock);
            textStack.Children.Add(dayBlock);
            textStack.Children.Add(dateBlock);

            rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Padding = new Thickness(16),
                ClipToBounds = false,
                Child = textStack,
                Cursor = isLocked ? Cursors.Arrow : Cursors.SizeAll,
                LayoutTransform = scaleTransform
            };

            Content = rootBorder;

            ApplyThemeAndColors();
            UpdateTimeDisplay();

            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += delegate { UpdateTimeDisplay(); };
            timer.Start();

            rootBorder.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed && !isLocked)
                {
                    try
                    {
                        DragMove();
                        SavePosition();
                    }
                    catch { }
                }
            };

            rootBorder.ContextMenuOpening += delegate { rootBorder.ContextMenu = BuildContextMenu(); };

            if (double.IsNaN(initialX) || double.IsNaN(initialY))
            {
                Left = Math.Max(40, SystemParameters.WorkArea.Right - 440);
                Top = Math.Max(40, SystemParameters.WorkArea.Top + 80);
            }
            else
            {
                Left = initialX;
                Top = initialY;
            }
        }

        public void ApplyThemeAndColors()
        {
            SolidColorBrush primaryBrush;
            SolidColorBrush secondaryBrush;
            DropShadowEffect shadow;

            if (currentTheme == "Ciano da Referência")
            {
                primaryBrush = new SolidColorBrush(Color.FromRgb(20, 95, 105));
                secondaryBrush = new SolidColorBrush(Color.FromRgb(158, 216, 219));
                shadow = new DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 1.5,
                    Opacity = 0.35,
                    Color = Color.FromRgb(0, 40, 45),
                    Direction = 315
                };
            }
            else if (currentTheme == "Ciano Vibrante")
            {
                primaryBrush = new SolidColorBrush(Color.FromRgb(14, 230, 210));
                secondaryBrush = new SolidColorBrush(Color.FromRgb(212, 250, 246));
                shadow = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black, Direction = 315 };
            }
            else if (currentTheme == "Branco Minimalista")
            {
                primaryBrush = Brushes.White;
                secondaryBrush = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255));
                shadow = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.55, Color = Colors.Black, Direction = 315 };
            }
            else if (currentTheme == "Escuro Elegante")
            {
                primaryBrush = new SolidColorBrush(Color.FromRgb(26, 29, 32));
                secondaryBrush = new SolidColorBrush(Color.FromRgb(95, 105, 117));
                shadow = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.4, Color = Color.FromArgb(100, 255, 255, 255), Direction = 315 };
            }
            else if (currentTheme == "Neon Synthwave")
            {
                primaryBrush = new SolidColorBrush(Color.FromRgb(255, 112, 166));
                secondaryBrush = new SolidColorBrush(Color.FromRgb(112, 214, 255));
                shadow = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.6, Color = Colors.Black, Direction = 315 };
            }
            else // "Âmbar Dourado"
            {
                primaryBrush = new SolidColorBrush(Color.FromRgb(255, 183, 3));
                secondaryBrush = new SolidColorBrush(Color.FromRgb(251, 133, 0));
                shadow = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black, Direction = 315 };
            }

            dayBlock.Foreground = primaryBrush;
            timeBlock.Foreground = secondaryBrush;
            dateBlock.Foreground = secondaryBrush;
            textStack.Effect = shadow;
        }

        public void UpdateTimeDisplay()
        {
            DateTime now = DateTime.Now;

            string timePattern = showSeconds
                ? (use24Hour ? "HH:mm:ss" : "hh:mm:ss")
                : (use24Hour ? "HH:mm" : "hh:mm");
            timeBlock.Text = now.ToString(timePattern, CultureInfo.InvariantCulture);

            if (currentLanguage == "Português")
            {
                var ci = new CultureInfo("pt-BR");
                string rawDay = now.ToString("dddd", ci);
                dayBlock.Text = char.ToUpper(rawDay[0]) + rawDay.Substring(1);

                string rawMonth = now.ToString("MMMM", ci);
                string monthCap = char.ToUpper(rawMonth[0]) + rawMonth.Substring(1);
                dateBlock.Text = monthCap + "  |  " + now.ToString("dd");
            }
            else
            {
                var ci = new CultureInfo("en-US");
                string rawDay = now.ToString("dddd", ci);
                dayBlock.Text = rawDay;

                string rawMonth = now.ToString("MMMM", ci);
                dateBlock.Text = rawMonth + "  |  " + now.ToString("dd");
            }
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();

            var lockItem = new MenuItem { Header = "Bloquear posição", IsCheckable = true, IsChecked = isLocked };
            lockItem.Click += delegate { SetLocked(lockItem.IsChecked); };
            menu.Items.Add(lockItem);

            var secItem = new MenuItem { Header = "Exibir segundos", IsCheckable = true, IsChecked = showSeconds };
            secItem.Click += delegate { SetShowSeconds(secItem.IsChecked); };
            menu.Items.Add(secItem);

            var h24Item = new MenuItem { Header = "Formato 24 horas", IsCheckable = true, IsChecked = use24Hour };
            h24Item.Click += delegate { Set24Hour(h24Item.IsChecked); };
            menu.Items.Add(h24Item);

            menu.Items.Add(new Separator());

            var langMenu = new MenuItem { Header = "Idioma" };
            var enItem = new MenuItem { Header = "Inglês (Tuesday / June | 06)", IsCheckable = true, IsChecked = currentLanguage == "Inglês" };
            enItem.Click += delegate { SetLanguage("Inglês"); };
            var ptItem = new MenuItem { Header = "Português (Terça-feira / Junho | 06)", IsCheckable = true, IsChecked = currentLanguage == "Português" };
            ptItem.Click += delegate { SetLanguage("Português"); };
            langMenu.Items.Add(enItem);
            langMenu.Items.Add(ptItem);
            menu.Items.Add(langMenu);

            var themeMenu = new MenuItem { Header = "Estilo de Cor" };
            foreach (string t in new[] { "Ciano da Referência", "Ciano Vibrante", "Branco Minimalista", "Escuro Elegante", "Neon Synthwave", "Âmbar Dourado" })
            {
                string th = t;
                var item = new MenuItem { Header = th, IsCheckable = true, IsChecked = currentTheme == th };
                item.Click += delegate { SetTheme(th); };
                themeMenu.Items.Add(item);
            }
            menu.Items.Add(themeMenu);

            var scaleMenu = new MenuItem { Header = "Tamanho" };
            var scales = new Tuple<string, double>[]
            {
                Tuple.Create("Pequeno (80%)", 0.8),
                Tuple.Create("Normal (100%)", 1.0),
                Tuple.Create("Grande (125%)", 1.25),
                Tuple.Create("Extra Grande (150%)", 1.5)
            };
            foreach (var s in scales)
            {
                var tuple = s;
                var item = new MenuItem { Header = tuple.Item1, IsCheckable = true, IsChecked = Math.Abs(currentScale - tuple.Item2) < 0.05 };
                item.Click += delegate { SetScale(tuple.Item2); };
                scaleMenu.Items.Add(item);
            }
            menu.Items.Add(scaleMenu);

            menu.Items.Add(new Separator());

            var resetPos = new MenuItem { Header = "Centralizar na tela" };
            resetPos.Click += delegate { CenterOnScreen(); };
            menu.Items.Add(resetPos);

            var configItem = new MenuItem { Header = "Configurações do B.M Flow…" };
            configItem.Click += delegate { ownerDock.OpenSettingsWindow("widgets"); };
            menu.Items.Add(configItem);

            menu.Items.Add(new Separator());

            var hideItem = new MenuItem { Header = "Ocultar Widget" };
            hideItem.Click += delegate { ownerDock.SetShowDesktopWidget(false); };
            menu.Items.Add(hideItem);

            return menu;
        }

        public void SetLocked(bool locked)
        {
            isLocked = locked;
            rootBorder.Cursor = isLocked ? Cursors.Arrow : Cursors.SizeAll;
            ownerDock.NotifyWidgetChanged();
        }

        public void SetShowSeconds(bool seconds)
        {
            showSeconds = seconds;
            UpdateTimeDisplay();
            ownerDock.NotifyWidgetChanged();
        }

        public void Set24Hour(bool h24)
        {
            use24Hour = h24;
            UpdateTimeDisplay();
            ownerDock.NotifyWidgetChanged();
        }

        public void SetLanguage(string lang)
        {
            currentLanguage = lang;
            UpdateTimeDisplay();
            ownerDock.NotifyWidgetChanged();
        }

        public void SetTheme(string theme)
        {
            currentTheme = theme;
            ApplyThemeAndColors();
            ownerDock.NotifyWidgetChanged();
        }

        public void SetScale(double scale)
        {
            currentScale = Math.Max(0.5, Math.Min(2.5, scale));
            scaleTransform.ScaleX = currentScale;
            scaleTransform.ScaleY = currentScale;
            ownerDock.NotifyWidgetChanged();
        }

        public void CenterOnScreen()
        {
            double w = ActualWidth > 0 ? ActualWidth * currentScale : 300 * currentScale;
            double h = ActualHeight > 0 ? ActualHeight * currentScale : 150 * currentScale;
            Left = (SystemParameters.WorkArea.Width - w) / 2 + SystemParameters.WorkArea.Left;
            Top = (SystemParameters.WorkArea.Height - h) / 2 + SystemParameters.WorkArea.Top;
            SavePosition();
        }

        public void SavePosition()
        {
            ownerDock.SetWidgetPosition(Left, Top);
        }
    }

    internal sealed class DockMascot
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idleTime, out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

        private static ulong FileTimeToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
        }

        private readonly DockWindow ownerDock;
        private readonly Canvas mascotCanvas;
        private readonly Border dockShell;
        private readonly Grid mascotHost;
        private readonly System.Windows.Controls.Image mascotImage;
        private readonly Border speechBubble;
        private readonly TextBlock speechText;
        private readonly ScaleTransform scaleTransform;
        private readonly TranslateTransform translateTransform;
        private readonly TransformGroup transformGroup;
        private readonly DispatcherTimer animTimer;
        private readonly DispatcherTimer bubbleTimer;
        private readonly DispatcherTimer jumpTimer;

        private bool enabled = true;
        private string mascotType = "CatBlack";
        private string mascotMode = "PatrolRight";
        private double mascotScale = 1.0;

        private double posX = 0;
        private double posY = 0;
        private int direction = -1; // -1 = facing left (as in reference image), +1 = facing right
        private string state = "Walk"; // Walk, Idle, Sleep, Jump
        private int stateTicks = 0;
        private int walkFrameIndex = 0;
        private int idleFrameIndex = 0;
        private int sleepFrameIndex = 0;
        private int jumpStep = 0;
        private bool isHovered = false;
        private readonly Random rand = new Random();

        // CPU tracking for RunCat mode
        private ulong lastIdleTime;
        private ulong lastKernelTime;
        private ulong lastUserTime;
        private DateTime lastCpuCheck = DateTime.MinValue;
        private double cachedCpuPercent = 10.0;

        // Cached frames
        private BitmapSource[] walkFrames;
        private BitmapSource[] idleFrames;
        private BitmapSource[] sleepFrames;
        private BitmapSource[] jumpFrames;
        private bool defaultFacingRight = false;

        public DockMascot(DockWindow owner, Canvas canvas, Border shell, bool isEnabled, string type, string mode, double scale)
        {
            ownerDock = owner;
            mascotCanvas = canvas;
            dockShell = shell;
            enabled = isEnabled;
            mascotType = type ?? "CatBlack";
            mascotMode = mode ?? "PatrolRight";
            mascotScale = scale;

            scaleTransform = new ScaleTransform(1.0, 1.0);
            translateTransform = new TranslateTransform(0, 0);
            transformGroup = new TransformGroup();
            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);

            mascotHost = new Grid
            {
                Width = 48,
                Height = 32,
                ClipToBounds = false,
                RenderTransform = transformGroup,
                RenderTransformOrigin = new Point(0.5, 1.0),
                Cursor = Cursors.Hand
            };

            mascotImage = new System.Windows.Controls.Image
            {
                Width = 48,
                Height = 32,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(mascotImage, BitmapScalingMode.NearestNeighbor);
            mascotHost.Children.Add(mascotImage);

            speechText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            speechBubble = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 26, 28, 35)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 3, 7, 3),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 2, Opacity = 0.45 },
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = speechText
            };

            mascotHost.MouseEnter += delegate { isHovered = true; };
            mascotHost.MouseLeave += delegate { isHovered = false; };
            mascotHost.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                PerformJumpAndReaction();
                e.Handled = true;
            };
            mascotHost.MouseRightButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                ShowMascotContextMenu();
                e.Handled = true;
            };

            mascotCanvas.Children.Add(mascotHost);
            mascotCanvas.Children.Add(speechBubble);

            // Mais quadros por segundo e deslocamento menor deixam a passada
            // legível sem fazer o pixel-art parecer deslizar pela barra.
            // DispatcherPriority.Normal garante cadência estável de 90ms mesmo quando
            // prévias com DWM thumbnails e animações de renderização estão ativas na tela.
            animTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(90) };
            animTimer.Tick += OnAnimTick;

            bubbleTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(1800) };
            bubbleTimer.Tick += delegate
            {
                bubbleTimer.Stop();
                speechBubble.Visibility = Visibility.Collapsed;
            };

            int[] hopOffsets = new int[] { -2, -5, -9, -12, -14, -14, -12, -9, -5, -2, 0 };
            jumpTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(55) };
            jumpTimer.Tick += delegate
            {
                if (jumpFrames != null && jumpFrames.Length > 0)
                {
                    translateTransform.Y = 0;
                    if (jumpStep < jumpFrames.Length)
                    {
                        mascotImage.Source = jumpFrames[jumpStep++];
                    }
                    else
                    {
                        jumpTimer.Stop();
                        if (state == "Jump") state = mascotMode == "Sleep" ? "Sleep" : "Walk";
                        if (walkFrames != null && walkFrames.Length > 0)
                            mascotImage.Source = walkFrames[0];
                    }
                }
                else
                {
                    if (jumpStep < hopOffsets.Length)
                    {
                        translateTransform.Y = hopOffsets[jumpStep++];
                    }
                    else
                    {
                        jumpTimer.Stop();
                        translateTransform.Y = 0;
                        if (state == "Jump") state = mascotMode == "Sleep" ? "Sleep" : "Walk";
                    }
                }
            };

            ReloadSprites();
            UpdateVisibility();
        }

        private void UpdateBubblePosition()
        {
            if (speechBubble == null || speechBubble.Visibility != Visibility.Visible) return;
            speechBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double bw = speechBubble.DesiredSize.Width > 0 ? speechBubble.DesiredSize.Width : 40.0;
            double bh = speechBubble.DesiredSize.Height > 0 ? speechBubble.DesiredSize.Height : 22.0;
            double hostW = (mascotImage.Width > 0 ? mascotImage.Width : 48.0) * mascotScale;
            double baseH = (mascotImage.Height > 0 ? mascotImage.Height : 32.0);
            double topOfMascot = posY - (baseH * (mascotScale - 1.0));
            double bx = posX + (hostW / 2.0) - (bw / 2.0);
            double by = topOfMascot - bh - 6.0;
            Canvas.SetLeft(speechBubble, bx);
            Canvas.SetTop(speechBubble, by);
        }

        public void Start()
        {
            if (enabled)
            {
                animTimer.Start();
                NotifyLayoutUpdated();
            }
        }

        public void Stop()
        {
            animTimer.Stop();
            bubbleTimer.Stop();
            jumpTimer.Stop();
        }

        public void SetEnabled(bool isEnabled)
        {
            enabled = isEnabled;
            UpdateVisibility();
            if (enabled)
            {
                animTimer.Start();
                NotifyLayoutUpdated();
            }
            else
            {
                animTimer.Stop();
                bubbleTimer.Stop();
                jumpTimer.Stop();
            }
        }

        public void SetType(string type)
        {
            mascotType = type;
            ReloadSprites();
            NotifyLayoutUpdated();
        }

        public void SetMode(string mode)
        {
            mascotMode = mode;
            if (mascotMode != "RunCat")
                animTimer.Interval = TimeSpan.FromMilliseconds(90);
            if (mascotMode == "Sleep") state = "Sleep";
            else if (state == "Sleep") state = "Walk";
            NotifyLayoutUpdated();
        }

        public void SetScale(double scale)
        {
            mascotScale = scale;
            double faceSign = defaultFacingRight ? (direction > 0 ? 1.0 : -1.0) : (direction < 0 ? 1.0 : -1.0);
            scaleTransform.ScaleX = faceSign * mascotScale;
            scaleTransform.ScaleY = mascotScale;
            NotifyLayoutUpdated();
        }

        private void UpdateVisibility()
        {
            mascotHost.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            if (!enabled && speechBubble != null) speechBubble.Visibility = Visibility.Collapsed;
        }

        public void NotifyLayoutUpdated()
        {
            if (!enabled) return;
            double rootW = ownerDock.ActualWidth;
            double rootH = ownerDock.ActualHeight;
            double dockH = ownerDock.DockShellActualHeight;
            if (rootW < 50 || rootH < 50 || dockH < 30) return;

            double dockLeft = 5.0;
            double dockRight = rootW - 5.0;
            double topEdgeY = rootH - dockH - 5.0;

            double baseH = (mascotImage.Height > 0 ? mascotImage.Height : 32.0);
            double spriteW = (mascotImage.Width > 0 ? mascotImage.Width : 48.0) * mascotScale;
            posY = topEdgeY - baseH + 2.0;

            double minX, maxX;
            if (mascotMode == "PatrolRight" || mascotMode == "RunCat")
            {
                minX = Math.Max(dockLeft + 20, dockRight - 230);
                maxX = Math.Max(minX + 40, dockRight - 130);
            }
            else if (mascotMode == "Sleep")
            {
                minX = Math.Max(dockLeft + 20, dockRight - 180);
                maxX = minX;
            }
            else // Wander
            {
                minX = dockLeft + 25;
                maxX = Math.Max(minX + 50, dockRight - spriteW - 25);
            }

            if (posX < minX || posX > maxX || posX == 0)
            {
                posX = (minX + maxX) / 2.0;
            }

            Canvas.SetLeft(mascotHost, posX);
            Canvas.SetTop(mascotHost, posY);
            UpdateBubblePosition();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            if (!enabled || mascotHost.Visibility != Visibility.Visible) return;

            double rootW = ownerDock.ActualWidth;
            double rootH = ownerDock.ActualHeight;
            double dockH = ownerDock.DockShellActualHeight;
            if (rootW < 50 || rootH < 50 || dockH < 30) return;

            double dockLeft = 5.0;
            double dockRight = rootW - 5.0;
            double topEdgeY = rootH - dockH - 5.0;
            double baseH = (mascotImage.Height > 0 ? mascotImage.Height : 32.0);
            double spriteW = (mascotImage.Width > 0 ? mascotImage.Width : 48.0) * mascotScale;
            posY = topEdgeY - baseH + 2.0;

            double minX, maxX;
            if (mascotMode == "PatrolRight")
            {
                minX = Math.Max(dockLeft + 20, dockRight - 230);
                maxX = Math.Max(minX + 40, dockRight - 130);
            }
            else if (mascotMode == "RunCat")
            {
                minX = Math.Max(dockLeft + 20, dockRight - 230);
                maxX = Math.Max(minX + 40, dockRight - 130);

                double cpu = GetCurrentCpuPercent();
                int ms = (int)Math.Max(35, Math.Min(220, 220 - (cpu * 1.85)));
                if (animTimer.Interval.TotalMilliseconds != ms)
                    animTimer.Interval = TimeSpan.FromMilliseconds(ms);
            }
            else if (mascotMode == "Sleep")
            {
                minX = Math.Max(dockLeft + 20, dockRight - 180);
                maxX = minX;
                state = "Sleep";
            }
            else // Wander
            {
                minX = dockLeft + 25;
                maxX = Math.Max(minX + 50, dockRight - spriteW - 25);
            }

            // State machine
            if (state == "Sleep")
            {
                stateTicks++;
                if (stateTicks % 5 == 0) sleepFrameIndex = (sleepFrameIndex + 1) % (sleepFrames != null && sleepFrames.Length > 0 ? sleepFrames.Length : 1);
                if (sleepFrames != null && sleepFrames.Length > sleepFrameIndex)
                    mascotImage.Source = sleepFrames[sleepFrameIndex];

                if (stateTicks % 28 == 0 && speechBubble.Visibility != Visibility.Visible)
                {
                    speechText.Text = "Zzz... 💤";
                    speechBubble.Visibility = Visibility.Visible;
                    UpdateBubblePosition();
                    bubbleTimer.Stop();
                    bubbleTimer.Start();
                }
            }
            else if (state == "Idle")
            {
                stateTicks--;
                if (stateTicks % 8 == 0) idleFrameIndex = (idleFrameIndex + 1) % (idleFrames != null && idleFrames.Length > 0 ? idleFrames.Length : 1);
                if (idleFrames != null && idleFrames.Length > idleFrameIndex)
                    mascotImage.Source = idleFrames[idleFrameIndex];

                if (stateTicks <= 0)
                {
                    state = "Walk";
                    if (rand.Next(10) < 4) direction = -direction;
                }
            }
            else if (state == "Walk")
            {
                if (isHovered)
                {
                    if (idleFrames != null && idleFrames.Length > 0)
                        mascotImage.Source = idleFrames[0];
                }
                else
                {
                    walkFrameIndex = (walkFrameIndex + 1) % (walkFrames != null && walkFrames.Length > 0 ? walkFrames.Length : 1);
                    if (walkFrames != null && walkFrames.Length > walkFrameIndex)
                        mascotImage.Source = walkFrames[walkFrameIndex];

                    posX += direction * (1.55 * mascotScale);

                    if (direction > 0 && posX >= maxX)
                    {
                        posX = maxX;
                        direction = -1;
                        state = "Idle";
                        stateTicks = rand.Next(15, 30);
                    }
                    else if (direction < 0 && posX <= minX)
                    {
                        posX = minX;
                        direction = 1;
                        state = "Idle";
                        stateTicks = rand.Next(15, 30);
                    }
                    else if (mascotMode != "RunCat" && rand.Next(100) < 3)
                    {
                        state = "Idle";
                        stateTicks = rand.Next(18, 32);
                    }
                }
            }

            double faceSign = defaultFacingRight ? (direction > 0 ? 1.0 : -1.0) : (direction < 0 ? 1.0 : -1.0);
            scaleTransform.ScaleX = faceSign * mascotScale;
            scaleTransform.ScaleY = mascotScale;

            Canvas.SetLeft(mascotHost, posX);
            Canvas.SetTop(mascotHost, posY);
            if (speechBubble.Visibility == Visibility.Visible)
                UpdateBubblePosition();
        }

        private void PerformJumpAndReaction()
        {
            jumpStep = 0;
            state = "Jump";
            if (jumpFrames != null && jumpFrames.Length > 0)
                mascotImage.Source = jumpFrames[0];
            jumpTimer.Start();

            string reaction = PickReaction();
            speechText.Text = reaction;
            speechBubble.Visibility = Visibility.Visible;
            UpdateBubblePosition();
            bubbleTimer.Stop();
            bubbleTimer.Start();
        }

        private string PickReaction()
        {
            if (mascotType == "Fox")
            {
                string[] foxes = new string[] { "Yip! 🦊", "❤️", "Awoo! ✨", "🦊🐾", "Kip! 🦊", "✨" };
                return foxes[rand.Next(foxes.Length)];
            }
            if (mascotType.StartsWith("Cat"))
            {
                string[] cats = new string[] { "Miau! 🐾", "❤️", "Purr... ✨", "Meow! 🐱", "nya~ 🐾", "✨" };
                return cats[rand.Next(cats.Length)];
            }
            if (mascotType == "Dog")
            {
                string[] dogs = new string[] { "Au au! 🐶", "❤️", "Woof! 🐾", "Latido! ✨", "🐾" };
                return dogs[rand.Next(dogs.Length)];
            }
            string[] ducks = new string[] { "Quack! 🦆", "HONK! 🪿", "❤️", "Quack quack! ✨" };
            return ducks[rand.Next(ducks.Length)];
        }

        private void ShowMascotContextMenu()
        {
            var menu = new ContextMenu();
            menu.Background = new SolidColorBrush(Color.FromRgb(36, 33, 33));
            menu.Foreground = Brushes.White;
            menu.BorderBrush = new SolidColorBrush(Color.FromRgb(65, 59, 58));

            var titleItem = new MenuItem { Header = "🐾 Mascote da Barra", IsEnabled = false, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 100)) };
            menu.Items.Add(titleItem);
            menu.Items.Add(new System.Windows.Controls.Separator());

            var mascotsMenu = new MenuItem { Header = "Escolher Mascote" };
            mascotsMenu.Items.Add(CreateCheckItem("🦊 Raposinha (Pixel Fox)", mascotType == "Fox", delegate { ownerDock.SetMascotType("Fox"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐱 Gatinho Meia-Noite (Preto)", mascotType == "CatBlack", delegate { ownerDock.SetMascotType("CatBlack"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐈 Gatinho Tigrão (Laranja)", mascotType == "CatOrange", delegate { ownerDock.SetMascotType("CatOrange"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🤍 Gatinho Neve (Branco)", mascotType == "CatWhite", delegate { ownerDock.SetMascotType("CatWhite"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐧 Gatinho Frajola (Tuxedo)", mascotType == "CatTuxedo", delegate { ownerDock.SetMascotType("CatTuxedo"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐶 Cachorrinho Caramelo", mascotType == "Dog", delegate { ownerDock.SetMascotType("Dog"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🦆 Gansinho / Pato", mascotType == "Duck", delegate { ownerDock.SetMascotType("Duck"); }));
            menu.Items.Add(mascotsMenu);

            var modesMenu = new MenuItem { Header = "Comportamento" };
            modesMenu.Items.Add(CreateCheckItem("📍 Canto Direito (Acima da Lixeira)", mascotMode == "PatrolRight", delegate { ownerDock.SetMascotMode("PatrolRight"); }));
            modesMenu.Items.Add(CreateCheckItem("🚶 Passear pela Barra toda", mascotMode == "Wander", delegate { ownerDock.SetMascotMode("Wander"); }));
            modesMenu.Items.Add(CreateCheckItem("⚡ Ritmo pela CPU (RunCat)", mascotMode == "RunCat", delegate { ownerDock.SetMascotMode("RunCat"); }));
            modesMenu.Items.Add(CreateCheckItem("💤 Dormir / Descansar", mascotMode == "Sleep", delegate { ownerDock.SetMascotMode("Sleep"); }));
            menu.Items.Add(modesMenu);

            var sizeMenu = new MenuItem { Header = "Tamanho" };
            sizeMenu.Items.Add(CreateCheckItem("Pequeno (80%)", mascotScale <= 0.85, delegate { ownerDock.SetMascotScale(0.8); }));
            sizeMenu.Items.Add(CreateCheckItem("Padrão (100%)", mascotScale > 0.85 && mascotScale < 1.15, delegate { ownerDock.SetMascotScale(1.0); }));
            sizeMenu.Items.Add(CreateCheckItem("Médio (125%)", mascotScale >= 1.15 && mascotScale < 1.4, delegate { ownerDock.SetMascotScale(1.25); }));
            sizeMenu.Items.Add(CreateCheckItem("Grande (150%)", mascotScale >= 1.4 && mascotScale < 1.7, delegate { ownerDock.SetMascotScale(1.5); }));
            sizeMenu.Items.Add(CreateCheckItem("Extra Grande (180%)", mascotScale >= 1.7 && mascotScale < 2.05, delegate { ownerDock.SetMascotScale(1.8); }));
            sizeMenu.Items.Add(CreateCheckItem("Gigante (220%)", mascotScale >= 2.05, delegate { ownerDock.SetMascotScale(2.2); }));
            menu.Items.Add(sizeMenu);

            menu.Items.Add(new System.Windows.Controls.Separator());

            var petActionItem = new MenuItem { Header = "Fazer Carinho 💖" };
            petActionItem.Click += delegate { PerformJumpAndReaction(); };
            menu.Items.Add(petActionItem);

            var settingsItem = new MenuItem { Header = "⚙️ Configurações…" };
            settingsItem.Click += delegate { ownerDock.OpenSettingsWindow("widgets"); };
            menu.Items.Add(settingsItem);

            var hideItem = new MenuItem { Header = "❌ Ocultar Mascote" };
            hideItem.Click += delegate { ownerDock.SetShowMascot(false); };
            menu.Items.Add(hideItem);

            menu.IsOpen = true;
        }

        private static MenuItem CreateCheckItem(string text, bool isChecked, RoutedEventHandler onClick)
        {
            var item = new MenuItem { Header = text, IsCheckable = true, IsChecked = isChecked };
            item.Click += onClick;
            return item;
        }

        private double GetCurrentCpuPercent()
        {
            if ((DateTime.UtcNow - lastCpuCheck).TotalMilliseconds < 800)
                return cachedCpuPercent;

            System.Runtime.InteropServices.ComTypes.FILETIME idleTime, kernelTime, userTime;
            if (GetSystemTimes(out idleTime, out kernelTime, out userTime))
            {
                ulong idle = FileTimeToUInt64(idleTime);
                ulong kernel = FileTimeToUInt64(kernelTime);
                ulong user = FileTimeToUInt64(userTime);

                if (lastKernelTime > 0)
                {
                    ulong deltaIdle = idle - lastIdleTime;
                    ulong deltaKernel = kernel - lastKernelTime;
                    ulong deltaUser = user - lastUserTime;
                    ulong deltaTotal = deltaKernel + deltaUser;

                    if (deltaTotal > 0)
                    {
                        double busy = 1.0 - ((double)deltaIdle / (double)deltaTotal);
                        cachedCpuPercent = Math.Max(0.0, Math.Min(100.0, busy * 100.0));
                    }
                }

                lastIdleTime = idle;
                lastKernelTime = kernel;
                lastUserTime = user;
                lastCpuCheck = DateTime.UtcNow;
            }
            return cachedCpuPercent;
        }

        private static BitmapSource[] LoadCustomFrames(string dir, string prefix)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                string[] files = Directory.GetFiles(dir, prefix + "*.png");
                if (files == null || files.Length == 0) return null;

                var matching = new List<string>();
                for (int i = 0; i < files.Length; i++)
                {
                    string f = files[i];
                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        matching.Add(f);
                    }
                }
                if (matching.Count == 0) return null;

                matching.Sort(delegate (string a, string b)
                {
                    int na = ExtractTrailingIndex(a);
                    int nb = ExtractTrailingIndex(b);
                    if (na != nb) return na.CompareTo(nb);
                    return StringComparer.OrdinalIgnoreCase.Compare(a, b);
                });

                var list = new List<BitmapSource>();
                for (int i = 0; i < matching.Count; i++)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(matching[i], UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    list.Add(bi);
                }
                return list.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static int ExtractTrailingIndex(string path)
        {
            try
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                int idx = name.LastIndexOf('_');
                if (idx >= 0 && idx + 1 < name.Length)
                {
                    int n;
                    if (int.TryParse(name.Substring(idx + 1), out n)) return n;
                }
            }
            catch { }
            return 0;
        }

        private void ReloadSprites()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string customDir = System.IO.Path.Combine(appData, "BMFlow", "Mascots", mascotType);
                if (!Directory.Exists(customDir))
                {
                    string localDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mascots", mascotType);
                    if (Directory.Exists(localDir)) customDir = localDir;
                }

                if (Directory.Exists(customDir))
                {
                    var customWalk = LoadCustomFrames(customDir, "walk");
                    if (customWalk != null && customWalk.Length > 0)
                    {
                        var customIdle = LoadCustomFrames(customDir, "idle");
                        var customSleep = LoadCustomFrames(customDir, "sleep");
                        var customJump = LoadCustomFrames(customDir, "jump");
                        walkFrames = customWalk;
                        idleFrames = customIdle != null && customIdle.Length > 0 ? customIdle : new BitmapSource[] { customWalk[0] };
                        sleepFrames = customSleep != null && customSleep.Length > 0 ? customSleep : idleFrames;
                        jumpFrames = customJump;
                        defaultFacingRight = (mascotType == "Fox" || mascotType.StartsWith("Cat"));
                        int customScale = (walkFrames[0].PixelHeight <= 32) ? 2 : 1;
                        mascotImage.Width = walkFrames[0].PixelWidth * customScale;
                        mascotImage.Height = walkFrames[0].PixelHeight * customScale;
                        mascotHost.Width = mascotImage.Width;
                        mascotHost.Height = mascotImage.Height;
                        mascotImage.Source = walkFrames[0];
                        return;
                    }
                }
            }
            catch { }

            defaultFacingRight = false;
            mascotImage.Width = 48;
            mascotImage.Height = 32;
            mascotHost.Width = 48;
            mascotHost.Height = 32;
            jumpFrames = null;

            Dictionary<char, Color> palette = GetPalette(mascotType);
            if (mascotType == "Dog")
            {
                walkFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(DogWalk0, palette, 2),
                    CreateSpriteBitmap(DogWalk1, palette, 2),
                    CreateSpriteBitmap(DogWalk2, palette, 2),
                    CreateSpriteBitmap(DogWalk3, palette, 2)
                };
                idleFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(DogIdle0, palette, 2),
                    CreateSpriteBitmap(DogIdle1, palette, 2)
                };
                sleepFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(DogSleep0, palette, 2),
                    CreateSpriteBitmap(DogSleep1, palette, 2)
                };
            }
            else if (mascotType == "Duck")
            {
                walkFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(DuckWalk0, palette, 2),
                    CreateSpriteBitmap(DuckWalk1, palette, 2),
                    CreateSpriteBitmap(DuckWalk2, palette, 2),
                    CreateSpriteBitmap(DuckWalk3, palette, 2)
                };
                idleFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(DuckIdle0, palette, 2),
                    CreateSpriteBitmap(DuckIdle1, palette, 2)
                };
                sleepFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(DuckSleep0, palette, 2),
                    CreateSpriteBitmap(DuckSleep1, palette, 2)
                };
            }
            else // Cats
            {
                walkFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(CatWalk0, palette, 2),
                    CreateSpriteBitmap(CatWalk1, palette, 2),
                    CreateSpriteBitmap(CatWalk2, palette, 2),
                    CreateSpriteBitmap(CatWalk3, palette, 2)
                };
                idleFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(CatIdle0, palette, 2),
                    CreateSpriteBitmap(CatIdle1, palette, 2)
                };
                sleepFrames = new BitmapSource[]
                {
                    CreateSpriteBitmap(CatSleep0, palette, 2),
                    CreateSpriteBitmap(CatSleep1, palette, 2)
                };
            }

            if (walkFrames != null && walkFrames.Length > 0)
                mascotImage.Source = walkFrames[0];
        }

        private static Dictionary<char, Color> GetPalette(string type)
        {
            var dict = new Dictionary<char, Color>();
            if (type == "CatOrange")
            {
                dict['O'] = Color.FromRgb(24, 24, 26);
                dict['B'] = Color.FromRgb(235, 172, 82);
                dict['D'] = Color.FromRgb(220, 118, 43);
                dict['E'] = Color.FromRgb(24, 24, 26);
                dict['L'] = Color.FromRgb(24, 24, 26);
                dict['P'] = Color.FromRgb(250, 200, 160);
                dict['N'] = Color.FromRgb(235, 120, 130);
                dict['W'] = Color.FromRgb(255, 255, 255);
            }
            else if (type == "CatWhite")
            {
                // O branco puro apagava a silhueta e transformava o mascote em
                // uma mancha nas escalas menores. Mantemos o pelo claro, mas
                // com contorno grafite e sombras suficientemente contrastantes.
                dict['O'] = Color.FromRgb(35, 39, 48);
                dict['B'] = Color.FromRgb(232, 236, 243);
                dict['D'] = Color.FromRgb(112, 124, 145);
                dict['E'] = Color.FromRgb(18, 105, 196);
                dict['L'] = Color.FromRgb(245, 184, 36);
                dict['P'] = Color.FromRgb(255, 139, 164);
                dict['N'] = Color.FromRgb(218, 55, 102);
                dict['W'] = Color.FromRgb(174, 187, 207);
            }
            else if (type == "CatTuxedo")
            {
                dict['O'] = Color.FromRgb(8, 9, 12);
                dict['B'] = Color.FromRgb(31, 32, 38);
                dict['D'] = Color.FromRgb(235, 237, 241);
                dict['E'] = Color.FromRgb(241, 196, 15);
                dict['L'] = Color.FromRgb(241, 196, 15);
                dict['P'] = Color.FromRgb(248, 165, 194);
                dict['N'] = Color.FromRgb(232, 67, 147);
                dict['W'] = Colors.White;
            }
            else if (type == "Dog")
            {
                dict['B'] = Color.FromRgb(229, 152, 102);
                dict['D'] = Color.FromRgb(160, 64, 0);
                dict['E'] = Color.FromRgb(44, 62, 80);
                dict['P'] = Color.FromRgb(250, 219, 216);
                dict['N'] = Color.FromRgb(44, 62, 80);
                dict['W'] = Color.FromRgb(254, 249, 231);
            }
            else if (type == "Duck")
            {
                dict['B'] = Colors.White;
                dict['D'] = Color.FromRgb(224, 224, 224);
                dict['E'] = Color.FromRgb(44, 62, 80);
                dict['P'] = Color.FromRgb(243, 156, 18);
                dict['N'] = Color.FromRgb(211, 84, 0);
                dict['W'] = Color.FromRgb(243, 156, 18);
            }
            else // CatBlack (Reference Image!)
            {
                dict['O'] = Color.FromRgb(8, 10, 15);
                dict['B'] = Color.FromRgb(41, 43, 52);
                dict['D'] = Color.FromRgb(29, 31, 38);
                dict['E'] = Color.FromRgb(255, 211, 42);
                dict['L'] = Color.FromRgb(70, 214, 92);
                dict['P'] = Color.FromRgb(248, 165, 194);
                dict['N'] = Color.FromRgb(232, 67, 147);
                dict['W'] = Color.FromRgb(62, 66, 83);
            }
            return dict;
        }

        private static BitmapSource CreateSpriteBitmap(string[] rows, Dictionary<char, Color> palette, int scale)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            int outW = w * scale;
            int outH = h * scale;
            int stride = outW * 4;
            byte[] pixels = new byte[stride * outH];

            for (int y = 0; y < h; y++)
            {
                string row = rows[y];
                for (int x = 0; x < w; x++)
                {
                    char c = row[x];
                    Color col;
                    if (!palette.TryGetValue(c, out col) || col.A == 0) continue;

                    for (int sy = 0; sy < scale; sy++)
                    {
                        int py = y * scale + sy;
                        for (int sx = 0; sx < scale; sx++)
                        {
                            int px = x * scale + sx;
                            int idx = py * stride + px * 4;
                            pixels[idx + 0] = col.B;
                            pixels[idx + 1] = col.G;
                            pixels[idx + 2] = col.R;
                            pixels[idx + 3] = col.A;
                        }
                    }
                }
            }

            var bs = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bs.Freeze();
            return bs;
        }

        // --- Sprite Pixel Art Data (24x16 grids) ---
        private static readonly string[] CatWalk0 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            ".....OO.....OO..........",
            ".....OBO...OBO..........",
            ".....OBBOOOBBO..........",
            "....OBBBDBDBBBO..OO.....",
            "....OBBBBBBBBBO..OBO....",
            ".....OBEBBBEBBO..ODO....",
            "....OBBWWWBBBBO.OBBO....",
            ".....OWWWWWBBOOOBDO.....",
            "......OOOOOOODBDBO......",
            "......OBBBBBBDBDO.......",
            "......OBBBBBBBBBO.......",
            ".....O.O.......O.O......"
        };

        private static readonly string[] CatWalk1 = new string[] {
            "........................",
            "........................",
            "........................",
            "..................OO....",
            ".....OO.....OO....OBO...",
            ".....OBO...OBO....ODO...",
            ".....OBBOOOBBO...OBBO...",
            "....OBBBDBDBBBO.ODBO....",
            "....OBBBBBBBBBOOBBO.....",
            ".....OBEBBBEBBOBBO......",
            "....OBBWWWBBBBOBBO......",
            ".....OWWWWWBBOOO........",
            "......OOOOOOODBDBO......",
            "......OBBBBBBDBDO.......",
            "......OBBBBBBBBBO.......",
            "......OO........OO......"
        };

        private static readonly string[] CatWalk2 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            ".....OO.....OO..........",
            ".....OBO...OBO..........",
            ".....OBBOOOBBO..........",
            "....OBBBDBDBBBO..OO.....",
            "....OBBBBBBBBBO..OBO....",
            ".....OBEBBBEBBO..ODO....",
            "....OBBWWWBBBBO.OBBO....",
            ".....OWWWWWBBOOOBDO.....",
            "......OOOOOOODBDBO......",
            "......OBBBBBBDBDO.......",
            "......OBBBBBBBBBO.......",
            "......O.O.......O.O....."
        };

        private static readonly string[] CatWalk3 = new string[] {
            "........................",
            "........................",
            "........................",
            "..................OO....",
            ".....OO.....OO....OBO...",
            ".....OBO...OBO....ODO...",
            ".....OBBOOOBBO...OBBO...",
            "....OBBBDBDBBBO.ODBO....",
            "....OBBBBBBBBBOOBBO.....",
            ".....OBEBBBEBBOBBO......",
            "....OBBWWWBBBBOBBO......",
            ".....OWWWWWBBOOO........",
            "......OOOOOOODBDBO......",
            "......OBBBBBBDBDO.......",
            "......OBBBBBBBBBO.......",
            ".......OO........OO....."
        };

        private static readonly string[] CatIdle0 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            ".....OO.....OO..........",
            ".....OBO...OBO..........",
            ".....OBBOOOBBO..........",
            "....OBBBDBDBBBO..OO.....",
            "....OBBBBBBBBBO..OBO....",
            ".....OBEBBBEBBO..ODO....",
            "....OBBWWWBBBBO.OBBO....",
            ".....OWWWWWBBOOOBDO.....",
            "......OOOOOOODBDBO......",
            "......OBBBBBBDBDO.......",
            "......OBBBBBBBBBO.......",
            ".....O.O.......O.O......"
        };

        private static readonly string[] CatIdle1 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            ".....OO.....OO..........",
            ".....OBO...OBO..........",
            ".....OBBOOOBBO..........",
            "....OBBBDBDBBBO..OO.....",
            "....OBBBBBBBBBO..OBO....",
            ".....OBDDBBBDDO..ODO....",
            "....OBBWWWBBBBO.OBBO....",
            ".....OWWWWWBBOOOBDO.....",
            "......OOOOOOODBDBO......",
            "......OBBBBBBDBDO.......",
            "......OBBBBBBBBBO.......",
            ".....O.O.......O.O......"
        };

        private static readonly string[] CatSleep0 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "......OO...OO...........",
            "......OBO..OBO..........",
            ".....OBBOOOBBO..........",
            "....OBBBDBDBBBOOOOOO....",
            "....ODDBBDBBOODDBBBO....",
            "....OBBWWWBBBOBBBBBBBO..",
            "....OBBBBBOOBBBBBBDDO...",
            ".....OBBBBBBBBBBBBBO....",
            "......OOOOOOOOOOOOOO...."
        };

        private static readonly string[] CatSleep1 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "......OO...OO...........",
            "......OBO..OBO..........",
            ".....OBBOOOBBO..........",
            "....OBBBDBDBBBOOOOOO....",
            "....OBEBBBEBBOODDBBBO...",
            "....OBBWWWBBBOBBBBBBBO..",
            "....OBBBBBOOBBBBBBDDO...",
            ".....OBBBBBBBBBBBBBO....",
            "......OOOOOOOOOOOOOO...."
        };
        private static readonly string[] DogWalk0 = new string[] {
            "......DD................",
            ".....DPD................",
            "....DDBD................",
            "...BBBBBBBBB............",
            "..BBEBBBEBBBB........BB.",
            "..BBEBBBEBBBB.......BBB.",
            "..BBBBNBBBBBB......BBBB.",
            "..BBBWBBBBBBBB....BBBB..",
            "...BBBBBBBBBBBBBBBBBB...",
            "....BBBBBBBBBBBBBBBB....",
            "....BDDBBBDDBBBDDBBB....",
            "....BBBBBBBBBBBBBBBB....",
            "....BBBBBBBBBBBBBBBB....",
            "....BB...BB....BB...BB..",
            "....BB...BB....BB...BB..",
            "....WW...WW....WW...WW.."
        };

        private static readonly string[] DogWalk1 = new string[] {
            "......DD................",
            ".....DPD................",
            "....DDBD................",
            "...BBBBBBBBB............",
            "..BBEBBBEBBBB.........B.",
            "..BBEBBBEBBBB........BB.",
            "..BBBBNBBBBBB.......BBB.",
            "..BBBWBBBBBBBB.....BBBB.",
            "...BBBBBBBBBBBBBBBBBB...",
            "....BBBBBBBBBBBBBBBB....",
            "....BDDBBBDDBBBDDBBB....",
            "....BBBBBBBBBBBBBBBB....",
            "....BBBBBBBBBBBBBBBB....",
            "...BB.....BB..BB.....BB.",
            "..BB.......BB..BB.....BB",
            "..WW.......WW..WW.....WW"
        };

        private static readonly string[] DogWalk2 = new string[] {
            "......DD................",
            ".....DPD................",
            "....DDBD................",
            "...BBBBBBBBB............",
            "..BBEBBBEBBBB........BB.",
            "..BBEBBBEBBBB.......BBB.",
            "..BBBBNBBBBBB......BBBB.",
            "..BBBWBBBBBBBB....BBBB..",
            "...BBBBBBBBBBBBBBBBBB...",
            "....BBBBBBBBBBBBBBBB....",
            "....BDDBBBDDBBBDDBBB....",
            "....BBBBBBBBBBBBBBBB....",
            "....BBBBBBBBBBBBBBBB....",
            ".....BB.BB......BB.BB...",
            ".....BB.BB......BB.BB...",
            ".....WW.WW......WW.WW..."
        };

        private static readonly string[] DogWalk3 = new string[] {
            "......DD................",
            ".....DPD................",
            "....DDBD................",
            "...BBBBBBBBB............",
            "..BBEBBBEBBBB.......BBB.",
            "..BBEBBBEBBBB......BBBB.",
            "..BBBBNBBBBBB.....BBBB..",
            "..BBBWBBBBBBBB...BBBB...",
            "...BBBBBBBBBBBBBBBBBB...",
            "....BBBBBBBBBBBBBBBB....",
            "....BDDBBBDDBBBDDBBB....",
            "....BBBBBBBBBBBBBBBB....",
            "....BBBBBBBBBBBBBBBB....",
            ".....BB...BB....BB...BB.",
            "....BB.....BB....BB...BB",
            "....WW.....WW....WW...WW"
        };

        private static readonly string[] DogIdle0 = new string[] {
            "......DD................",
            ".....DPD................",
            "....DDBD................",
            "...BBBBBBBBB............",
            "..BBEBBBEBBBB........BB.",
            "..BBEBBBEBBBB.......BBB.",
            "..BBBBNBBBBBB......BBBB.",
            "..BBBWBBBBBBBB....BBBB..",
            "...BBBBBBBBBBBBBBBBBB...",
            "....BBBBBBBBBBBBBBBB....",
            "....BDDBBBDDBBBDDBBB....",
            "....BBBBBBBBBBBBBBBB....",
            "....BBBBBBBBBBBBBBBBB...",
            "....BB...BB....BBBBBB...",
            "....BB...BB...BBBBBBB...",
            "....WW...WW..WWWW..WW..."
        };

        private static readonly string[] DogIdle1 = new string[] {
            "......DD................",
            ".....DPD................",
            "....DDBD................",
            "...BBBBBBBBB............",
            "..BBDDBBDDBBB.......BBB.",
            "..BBDDBBDDBBB......BBBB.",
            "..BBBBNBBBBBB.....BBBB..",
            "..BBBWBBBBBBBB...BBBB...",
            "...BBBBBBBBBBBBBBBBBB...",
            "....BBBBBBBBBBBBBBBB....",
            "....BDDBBBDDBBBDDBBB....",
            "....BBBBBBBBBBBBBBBB....",
            "....BBBBBBBBBBBBBBBBB...",
            "....BB...BB....BBBBBB...",
            "....WW...WW..WWWW..WW...",
            "....WW...WW..WWWW..WW..."
        };

        private static readonly string[] DogSleep0 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            ".....DD.................",
            "....DPD.................",
            "...DDBD.................",
            "..BBBBBBBBBBB...........",
            "..BBDDBBDDBBB...........",
            "..BBBBBBBBBBBBBBBB......",
            "..BDDBBBBDDBBBBDDBBB....",
            ".BBBBBBBBBBBBBBBBBBBB...",
            ".BBDDDBBDDDBBDDDBBBBB...",
            ".BBBBBBBBBBBBBBBBBBBB...",
            ".WWWWWWWWWWWWWWWWWWWW...",
            "........................"
        };

        private static readonly string[] DogSleep1 = new string[] {
            "........................",
            "........................",
            "........................",
            ".....DD.................",
            "....DPD.................",
            "...DDBD.................",
            "..BBBBBBBBBBB...........",
            "..BBDDBBDDBBB...........",
            "..BBBBBBBBBBBBBBBB......",
            "..BDDBBBBDDBBBBDDBBB....",
            ".BBBBBBBBBBBBBBBBBBBB...",
            ".BBDDDBBDDDBBDDDBBBBB...",
            ".BBBBBBBBBBBBBBBBBBBB...",
            ".WWWWWWWWWWWWWWWWWWWW...",
            "........................",
            "........................"
        };

        private static readonly string[] DuckWalk0 = new string[] {
            "........................",
            "........................",
            ".......BBBBB............",
            ".....PPBBEBB............",
            "....NNNBBBBB............",
            ".......BBBBB............",
            "......BBBBBB............",
            ".....BBBBBBBBBBB........",
            "....BBBBBDDDBBBBB.......",
            "...BBBBBBDDDBBBBBB......",
            "...BBBBBBBBBBBBBBB......",
            "...BBBBBBBBBBBBBB.......",
            "....BBBBBBBBBBBB........",
            ".......WW....WW.........",
            ".......WW....WW.........",
            "......WWW...WWW........."
        };

        private static readonly string[] DuckWalk1 = new string[] {
            "........................",
            "........................",
            ".......BBBBB............",
            ".....PPBBEBB............",
            "....NNNBBBBB............",
            ".......BBBBB............",
            "......BBBBBB............",
            ".....BBBBBBBBBBB........",
            "....BBBBBDDDBBBBB.......",
            "...BBBBBBDDDBBBBBB......",
            "...BBBBBBBBBBBBBBB......",
            "...BBBBBBBBBBBBBB.......",
            "....BBBBBBBBBBBB........",
            "......WW......WW........",
            ".....WW........WW.......",
            "....WWW.......WWW......."
        };

        private static readonly string[] DuckWalk2 = new string[] {
            "........................",
            "........................",
            ".......BBBBB............",
            ".....PPBBEBB............",
            "....NNNBBBBB............",
            ".......BBBBB............",
            "......BBBBBB............",
            ".....BBBBBBBBBBB........",
            "....BBBBBDDDBBBBB.......",
            "...BBBBBBDDDBBBBBB......",
            "...BBBBBBBBBBBBBBB......",
            "...BBBBBBBBBBBBBB.......",
            "....BBBBBBBBBBBB........",
            "........WW..WW..........",
            "........WW..WW..........",
            ".......WWW..WWW........."
        };

        private static readonly string[] DuckWalk3 = new string[] {
            "........................",
            "........................",
            ".......BBBBB............",
            ".....PPBBEBB............",
            "....NNNBBBBB............",
            ".......BBBBB............",
            "......BBBBBB............",
            ".....BBBBBBBBBBB........",
            "....BBBBBDDDBBBBB.......",
            "...BBBBBBDDDBBBBBB......",
            "...BBBBBBBBBBBBBBB......",
            "...BBBBBBBBBBBBBB.......",
            "....BBBBBBBBBBBB........",
            "........WW....WW........",
            ".......WW......WW.......",
            "......WWW......WWW......"
        };

        private static readonly string[] DuckIdle0 = new string[] {
            "........................",
            "........................",
            ".......BBBBB............",
            ".....PPBBEBB............",
            "....NNNBBBBB............",
            ".......BBBBB............",
            "......BBBBBB............",
            ".....BBBBBBBBBBB........",
            "....BBBBBDDDBBBBB.......",
            "...BBBBBBDDDBBBBBB......",
            "...BBBBBBBBBBBBBBB......",
            "...BBBBBBBBBBBBBB.......",
            "....BBBBBBBBBBBB........",
            ".......WW....WW.........",
            ".......WW....WW.........",
            "......WWW...WWW........."
        };

        private static readonly string[] DuckIdle1 = new string[] {
            "........................",
            "........................",
            "........................",
            ".......BBBBB............",
            ".....PPBBDDB............",
            "....NNNBBBBB............",
            ".......BBBBB............",
            "......BBBBBB............",
            ".....BBBBBBBBBBB........",
            "....BBBBBDDDBBBBB.......",
            "...BBBBBBDDDBBBBBB......",
            "...BBBBBBBBBBBBBBB......",
            "...BBBBBBBBBBBBBB.......",
            "....BBBBBBBBBBBB........",
            ".......WW....WW.........",
            "......WWW...WWW........."
        };

        private static readonly string[] DuckSleep0 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            ".......BBBBB............",
            "......BBDDBBBBBB........",
            ".....PPBBDDDBBBBB.......",
            "....NNNBBBDDBBBBBB......",
            "....BBBBBBBBBBBBBB......",
            "....BBBBBBBBBBBBBB......",
            ".....BBBBBBBBBBBB.......",
            "......WWWWWWWWWW........",
            "........................",
            "........................"
        };

        private static readonly string[] DuckSleep1 = new string[] {
            "........................",
            "........................",
            "........................",
            "........................",
            "........................",
            ".......BBBBB............",
            "......BBDDBBBBBB........",
            ".....PPBBDDDBBBBB.......",
            "....NNNBBBDDBBBBBB......",
            "....BBBBBBBBBBBBBB......",
            "....BBBBBBBBBBBBBB......",
            ".....BBBBBBBBBBBB.......",
            "......WWWWWWWWWW........",
            "........................",
            "........................",
            "........................"
        };
    }

    internal sealed class DockSettingsWindow : Window
    {
        private readonly DockWindow dock;
        private readonly Dictionary<string, Border> themeCards = new Dictionary<string, Border>();
        private readonly Dictionary<string, Border> navigationItems = new Dictionary<string, Border>();
        private CheckBox keepWindowsCheck;
        private Grid settingsPageHost;
        private int selectedWorkspaceIndex = 0;
        private string workspaceAppCatalogSearch = "";

        public void ShowSettingsPageExternal(string page)
        {
            ShowSettingsPage(page);
        }

        public DockSettingsWindow(DockWindow ownerDock, string initialPage = "general")
        {
            dock = ownerDock;
            Title = "Configurações da B.M Flow";
            Width = 1050;
            Height = 740;
            MinWidth = 900;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(31, 29, 29));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");
            Topmost = true;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = new Grid { Background = new SolidColorBrush(Color.FromRgb(35, 32, 32)) };
            Grid.SetColumnSpan(titleBar, 2);
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.LeftButton == MouseButtonState.Pressed && args.GetPosition(titleBar).X < titleBar.ActualWidth - 44) DragMove();
            };
            var close = new Border { Width = 42, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            close.Child = new TextBlock { Text = "×", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(220, 216, 214)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            close.MouseEnter += delegate { close.Background = new SolidColorBrush(Color.FromRgb(196, 43, 38)); };
            close.MouseLeave += delegate { close.Background = Brushes.Transparent; };
            close.MouseLeftButtonUp += delegate { Close(); };
            titleBar.Children.Add(close);
            root.Children.Add(titleBar);

            var sidebar = new Grid { Background = new SolidColorBrush(Color.FromRgb(31, 29, 30)), Margin = new Thickness(0) };
            sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(sidebar, 1);

            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(25, 22, 20, 20) };
            brand.Children.Add(new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(255, 105, 82)),
                Child = new TextBlock { Text = "▦", FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            var brandText = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            brandText.Children.Add(new TextBlock { Text = "B.M Flow", FontSize = 19, FontWeight = FontWeights.SemiBold });
            brandText.Children.Add(new TextBlock { Text = "Configurações", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(173, 168, 168)) });
            brand.Children.Add(brandText);
            sidebar.Children.Add(brand);

            var search = new TextBox
            {
                Text = "Buscar nos ajustes", Foreground = new SolidColorBrush(Color.FromRgb(155, 150, 150)),
                Background = new SolidColorBrush(Color.FromRgb(48, 45, 45)), BorderBrush = new SolidColorBrush(Color.FromRgb(66, 62, 62)),
                BorderThickness = new Thickness(1), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(20, 0, 20, 18)
            };
            search.GotFocus += delegate { if (search.Text == "Buscar nos ajustes") { search.Text = String.Empty; search.Foreground = Brushes.White; } };
            search.LostFocus += delegate { if (String.IsNullOrWhiteSpace(search.Text)) { search.Text = "Buscar nos ajustes"; search.Foreground = new SolidColorBrush(Color.FromRgb(155, 150, 150)); } };
            search.TextChanged += delegate
            {
                if (!search.IsKeyboardFocused) return;
                string query = (search.Text ?? String.Empty).Trim().ToLowerInvariant();
                if (query.Contains("tema")) ShowSettingsPage("themes");
                else if (query.Contains("espaco") || query.Contains("espaço") || query.Contains("workspace") || query.Contains("trabalho") || query.Contains("estudo") || query.Contains("jogos")) ShowSettingsPage("workspaces");
                else if (query.Contains("tamanho") || query.Contains("posição") || query.Contains("indicador")) ShowSettingsPage("size");
                else if (query.Contains("apar") || query.Contains("opacidade") || query.Contains("fundo")) ShowSettingsPage("appearance");
                else if (query.Contains("widget") || query.Contains("mouse") || query.Contains("cursor") || query.Contains("mac") || query.Contains("personaliz") || query.Contains("mascote") || query.Contains("pet") || query.Contains("gato") || query.Contains("gatinho") || query.Contains("cachorro")) ShowSettingsPage("widgets");
                else if (query.Contains("sobre") || query.Contains("versão")) ShowSettingsPage("about");
                else if (query.Length >= 3) ShowSettingsPage("general");
            };
            Grid.SetRow(search, 1);
            sidebar.Children.Add(search);

            var navigation = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
            navigation.Children.Add(CreateNavigationItem("general", "\uE713", "Geral"));
            navigation.Children.Add(CreateNavigationItem("workspaces", "\uE8A7", "Espaços"));
            navigation.Children.Add(CreateNavigationItem("appearance", "\uE790", "Aparência"));
            navigation.Children.Add(CreateNavigationItem("themes", "\uE771", "Temas"));
            navigation.Children.Add(CreateNavigationItem("size", "\uE799", "Tamanho e posição"));
            navigation.Children.Add(CreateNavigationItem("widgets", "\uE74C", "Personalização"));
            navigation.Children.Add(CreateNavigationItem("about", "\uE946", "Sobre"));
            Grid.SetRow(navigation, 2);
            sidebar.Children.Add(navigation);
            root.Children.Add(sidebar);

            settingsPageHost = new Grid { Background = new SolidColorBrush(Color.FromRgb(43, 39, 38)) };
            Grid.SetColumn(settingsPageHost, 1);
            Grid.SetRow(settingsPageHost, 1);
            root.Children.Add(settingsPageHost);
            Content = root;
            ShowSettingsPage(initialPage ?? "general");
        }

        private Border CreateNavigationItem(string key, string glyph, string label)
        {
            var accent = new Border { Width = 4, Height = 22, CornerRadius = new CornerRadius(2), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(accent);
            var icon = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(210, 207, 207)), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);
            var text = new TextBlock { Text = label, FontSize = 14.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
            var item = new Border
            {
                Height = 49, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, Margin = new Thickness(0, 2, 0, 2), Child = grid, Tag = accent
            };
            item.MouseEnter += delegate { if (item.Background == Brushes.Transparent) item.Background = new SolidColorBrush(Color.FromRgb(47, 44, 44)); };
            item.MouseLeave += delegate { if (((Border)item.Tag).Background == Brushes.Transparent) item.Background = Brushes.Transparent; };
            item.MouseLeftButtonUp += delegate { ShowSettingsPage(key); };
            navigationItems[key] = item;
            return item;
        }

        private void ShowSettingsPage(string page)
        {
            if (settingsPageHost == null) return;
            foreach (var entry in navigationItems)
            {
                bool active = entry.Key == page;
                entry.Value.Background = active ? new SolidColorBrush(Color.FromRgb(58, 54, 54)) : Brushes.Transparent;
                var accent = entry.Value.Tag as Border;
                if (accent != null) accent.Background = active ? new SolidColorBrush(Color.FromRgb(255, 101, 75)) : Brushes.Transparent;
            }

            var content = new StackPanel { Margin = new Thickness(42, 18, 42, 36) };
            if (page == "themes") BuildThemesPage(content);
            else if (page == "workspaces") BuildWorkspacesPage(content);
            else if (page == "appearance") BuildAppearancePage(content);
            else if (page == "size") BuildSizePage(content);
            else if (page == "widgets") BuildWidgetsPage(content);
            else if (page == "about") BuildAboutPage(content);
            else BuildGeneralPage(content);
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
            settingsPageHost.Children.Clear();
            settingsPageHost.Children.Add(scroll);
        }

        private static FrameworkElement PageHeader(string glyph, string title, string subtitle)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 24) };
            panel.Children.Add(new Border
            {
                Width = 58, Height = 58, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(103, 107, 117)),
                Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 27, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            var text = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromRgb(178, 172, 171)), Margin = new Thickness(0, 3, 0, 0) });
            panel.Children.Add(text);
            return panel;
        }

        private void BuildWorkspacesPage(StackPanel content)
        {
            var wsMgr = dock.CurrentWorkspaceManager;
            if (wsMgr == null) return;

            content.Children.Add(PageHeader("\uE8A7", "Espaços de Trabalho", "Configure docks diferentes para Trabalho, Estudo e Jogos. Alterne instantaneamente com Ctrl+Alt+1/2/3."));

            if (selectedWorkspaceIndex < 0 || selectedWorkspaceIndex >= wsMgr.Workspaces.Count)
                selectedWorkspaceIndex = Math.Max(0, Math.Min(wsMgr.ActiveIndex, wsMgr.Workspaces.Count - 1));

            var dynamicHost = new StackPanel();
            RenderWorkspaceEditor(dynamicHost, wsMgr);
            content.Children.Add(dynamicHost);
        }

        private void RenderWorkspaceEditor(StackPanel host, WorkspaceManager wsMgr)
        {
            Action refreshView = delegate
            {
                host.Children.Clear();
                RenderWorkspaceEditor(host, wsMgr);
            };

            // 1. Barra de seleção de espaços
            host.Children.Add(SectionTitle("Selecione ou Crie um Espaço", "Cada espaço possui seus próprios aplicativos fixados na Dock"));

            var pillsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            for (int i = 0; i < wsMgr.Workspaces.Count; i++)
            {
                int index = i;
                var ws = wsMgr.Workspaces[i];
                bool isSelected = index == selectedWorkspaceIndex;
                bool isActive = index == wsMgr.ActiveIndex;

                var pill = new Border
                {
                    Height = 44,
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(isSelected ? Color.FromRgb(255, 105, 82) : Color.FromRgb(58, 54, 54)),
                    BorderBrush = new SolidColorBrush(isActive ? Color.FromRgb(40, 205, 65) : (isSelected ? Color.FromRgb(255, 140, 120) : Color.FromRgb(80, 75, 75))),
                    BorderThickness = new Thickness(isActive ? 2 : 1),
                    Padding = new Thickness(16, 0, 16, 0),
                    Margin = new Thickness(0, 0, 10, 10),
                    Cursor = Cursors.Hand
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(new TextBlock
                {
                    Text = ws.Icon ?? "💼",
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = ws.Name,
                    FontSize = 14,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                });
                sp.Children.Add(new TextBlock
                {
                    Text = " (" + (index + 1) + ")",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(isSelected ? Color.FromArgb(220, 255, 255, 255) : Color.FromRgb(180, 175, 175)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                if (isActive)
                {
                    var badge = new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(Color.FromArgb(50, 40, 205, 65)),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = new TextBlock
                    {
                        Text = "ATIVO",
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(70, 230, 95))
                    };
                    sp.Children.Add(badge);
                }

                pill.Child = sp;
                pill.MouseLeftButtonUp += delegate
                {
                    selectedWorkspaceIndex = index;
                    refreshView();
                };
                pillsPanel.Children.Add(pill);
            }

            // Botão para criar novo espaço
var addWsBtn = new Border
{
Height = 44,
CornerRadius = new CornerRadius(12),
Background = new SolidColorBrush(Color.FromRgb(48, 45, 45)),
BorderBrush = new SolidColorBrush(Color.FromRgb(100, 95, 95)),
BorderThickness = new Thickness(1),
Padding = new Thickness(16, 0, 16, 0),
Margin = new Thickness(0, 0, 10, 10),
Cursor = Cursors.Hand
};
var addSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
addSp.Children.Add(new TextBlock { Text = "＋", FontSize = 16, Foreground = Brushes.White, Margin = new Thickness(0, 0, 6, 0) });
addSp.Children.Add(new TextBlock { Text = "Novo Espaço", FontSize = 13.5, Foreground = Brushes.White });
addWsBtn.Child = addSp;
addWsBtn.MouseEnter += delegate { addWsBtn.Background = new SolidColorBrush(Color.FromRgb(65, 60, 60)); };
addWsBtn.MouseLeave += delegate { addWsBtn.Background = new SolidColorBrush(Color.FromRgb(48, 45, 45)); };
addWsBtn.MouseLeftButtonUp += delegate
{
var newWs = new DockWorkspace(Guid.NewGuid().ToString("N").Substring(0, 8), "Espaço " + (wsMgr.Workspaces.Count + 1), "🚀");
wsMgr.Workspaces.Add(newWs);
wsMgr.Save();
selectedWorkspaceIndex = wsMgr.Workspaces.Count - 1;
refreshView();
};
pillsPanel.Children.Add(addWsBtn);
host.Children.Add(pillsPanel);

// 2. Detalhes do espaço selecionado
if (selectedWorkspaceIndex < 0 || selectedWorkspaceIndex >= wsMgr.Workspaces.Count)
selectedWorkspaceIndex = 0;
var currentWs = wsMgr.Workspaces[selectedWorkspaceIndex];
bool isCurrentActive = selectedWorkspaceIndex == wsMgr.ActiveIndex;

var card = SettingsCard();
var cardStack = new StackPanel();
card.Child = cardStack;

// Header do card: Nome, Ícone e Ações
var headerGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

var iconBox = new Border
{
Width = 46, Height = 46, CornerRadius = new CornerRadius(12),
Background = new SolidColorBrush(Color.FromRgb(52, 48, 48)),
BorderBrush = new SolidColorBrush(Color.FromRgb(80, 75, 75)),
BorderThickness = new Thickness(1),
Margin = new Thickness(0, 0, 14, 0),
Child = new TextBlock
{
Text = currentWs.Icon ?? "💼",
FontSize = 24,
HorizontalAlignment = HorizontalAlignment.Center,
VerticalAlignment = VerticalAlignment.Center
}
};
Grid.SetColumn(iconBox, 0);
headerGrid.Children.Add(iconBox);

var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
var nameBox = new TextBox
{
Text = currentWs.Name,
FontSize = 16,
FontWeight = FontWeights.SemiBold,
Foreground = Brushes.White,
Background = new SolidColorBrush(Color.FromRgb(48, 45, 45)),
BorderBrush = new SolidColorBrush(Color.FromRgb(80, 75, 75)),
BorderThickness = new Thickness(1),
Padding = new Thickness(10, 6, 10, 6),
MaxWidth = 260,
HorizontalAlignment = HorizontalAlignment.Left
};
nameBox.TextChanged += delegate
{
if (!String.IsNullOrWhiteSpace(nameBox.Text))
{
currentWs.Name = nameBox.Text;
wsMgr.Save();
}
};
nameStack.Children.Add(nameBox);
var shortcutHint = new TextBlock
{
Text = "Atalho do teclado: Ctrl + Alt + " + (selectedWorkspaceIndex + 1),
FontSize = 11.5,
Foreground = new SolidColorBrush(Color.FromRgb(180, 175, 175)),
Margin = new Thickness(2, 4, 0, 0)
};
nameStack.Children.Add(shortcutHint);
Grid.SetColumn(nameStack, 1);
headerGrid.Children.Add(nameStack);

// Ações: Ativar, Abrir Todos, Excluir
var rightActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

if (!isCurrentActive)
{
var activateBtn = CreateActionButton("▶ Ativar na Dock", Color.FromRgb(40, 150, 230), delegate
{
dock.SwitchToWorkspace(selectedWorkspaceIndex);
refreshView();
});
rightActions.Children.Add(activateBtn);
}

var launchAllBtn = CreateActionButton("🚀 Abrir / Restaurar todos os apps", Color.FromRgb(35, 160, 90), delegate
{
dock.LaunchOrRestoreWorkspaceApps(currentWs);
});
rightActions.Children.Add(launchAllBtn);

if (wsMgr.Workspaces.Count > 1)
{
var deleteBtn = CreateActionButton("🗑 Excluir", Color.FromRgb(190, 50, 50), delegate
{
if (MessageBox.Show("Tem certeza que deseja excluir o espaço '" + currentWs.Name + "'?", "Excluir Espaço", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
{
wsMgr.Workspaces.RemoveAt(selectedWorkspaceIndex);
if (wsMgr.ActiveIndex >= wsMgr.Workspaces.Count) wsMgr.ActiveIndex = 0;
selectedWorkspaceIndex = Math.Max(0, selectedWorkspaceIndex - 1);
wsMgr.Save();
dock.TriggerDockRebuild();
refreshView();
}
});
rightActions.Children.Add(deleteBtn);
}

Grid.SetColumn(rightActions, 2);
headerGrid.Children.Add(rightActions);
cardStack.Children.Add(headerGrid);

// Seletor de ícone / emoji
var emojiRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
emojiRow.Children.Add(new TextBlock
{
Text = "Ícone:",
FontSize = 12.5,
Foreground = new SolidColorBrush(Color.FromRgb(180, 175, 175)),
VerticalAlignment = VerticalAlignment.Center,
Margin = new Thickness(0, 0, 10, 0)
});
foreach (string emoji in new[] { "💼", "📚", "🎮", "🚀", "🎨", "💻", "🎵", "🎯", "☕", "⚡", "📁", "🔥" })
{
string em = emoji;
var emBtn = new Border
{
Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
Background = new SolidColorBrush(currentWs.Icon == em ? Color.FromRgb(255, 105, 82) : Color.FromRgb(52, 48, 48)),
Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
Child = new TextBlock
{
Text = em, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
}
};
emBtn.MouseLeftButtonUp += delegate
{
currentWs.Icon = em;
wsMgr.Save();
refreshView();
};
emojiRow.Children.Add(emBtn);
}
cardStack.Children.Add(emojiRow);

cardStack.Children.Add(Divider());

// Alternância de isolamento
var isolateToggle = Toggle("Isolar Dock deste espaço", "Oculta outros aplicativos em execução que não pertencem a este espaço", currentWs.IsolateWorkspace);
isolateToggle.Checked += delegate { currentWs.IsolateWorkspace = true; wsMgr.Save(); if (isCurrentActive) dock.TriggerDockRebuild(); };
isolateToggle.Unchecked += delegate { currentWs.IsolateWorkspace = false; wsMgr.Save(); if (isCurrentActive) dock.TriggerDockRebuild(); };
cardStack.Children.Add(isolateToggle);

cardStack.Children.Add(Divider());

// Aplicativos fixados neste espaço
var appsSection = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
var appsTitleGrid = new Grid();
appsTitleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
appsTitleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
appsTitleGrid.Children.Add(new TextBlock
{
Text = "Aplicativos Fixados na Dock deste Espaço (" + currentWs.Apps.Count + ")",
FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
});
var hintReorder = new TextBlock
{
Text = "Use as setas para reorganizar a ordem dos ícones",
FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(170, 165, 165))
};
Grid.SetColumn(hintReorder, 1);
appsTitleGrid.Children.Add(hintReorder);
appsSection.Children.Add(appsTitleGrid);

var appsWrap = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
if (currentWs.Apps.Count == 0)
{
appsWrap.Children.Add(new TextBlock
{
Text = "Nenhum aplicativo neste espaço ainda. Adicione aplicativos usando o catálogo abaixo!",
FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(180, 175, 175)),
Margin = new Thickness(4, 8, 0, 8)
});
}
else
{
for (int a = 0; a < currentWs.Apps.Count; a++)
{
int appIdx = a;
var appItem = currentWs.Apps[a];

var appCard = new Border
{
CornerRadius = new CornerRadius(10),
Background = new SolidColorBrush(Color.FromRgb(50, 46, 46)),
BorderBrush = new SolidColorBrush(Color.FromRgb(75, 70, 70)),
BorderThickness = new Thickness(1),
Padding = new Thickness(10, 8, 10, 8),
Margin = new Thickness(0, 0, 10, 10)
};

var cardContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
var iconImg = new System.Windows.Controls.Image
{
Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0),
Source = DockWindow.ResolveAppIcon(appItem.Target, appItem.AppUserModelId)
};
cardContent.Children.Add(iconImg);

var nameLabel = new TextBlock
{
Text = appItem.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
Foreground = Brushes.White, MaxWidth = 120, TextTrimming = TextTrimming.CharacterEllipsis,
VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
};
cardContent.Children.Add(nameLabel);

if (appIdx > 0)
{
var leftBtn = CreateSmallIconButton("◀", delegate
{
var tmp = currentWs.Apps[appIdx - 1];
currentWs.Apps[appIdx - 1] = currentWs.Apps[appIdx];
currentWs.Apps[appIdx] = tmp;
wsMgr.Save();
if (isCurrentActive) dock.TriggerDockRebuild();
refreshView();
});
cardContent.Children.Add(leftBtn);
}

if (appIdx < currentWs.Apps.Count - 1)
{
var rightBtn = CreateSmallIconButton("▶", delegate
{
var tmp = currentWs.Apps[appIdx + 1];
currentWs.Apps[appIdx + 1] = currentWs.Apps[appIdx];
currentWs.Apps[appIdx] = tmp;
wsMgr.Save();
if (isCurrentActive) dock.TriggerDockRebuild();
refreshView();
});
cardContent.Children.Add(rightBtn);
}

var delBtn = CreateSmallIconButton("✕", delegate
{
currentWs.Apps.RemoveAt(appIdx);
wsMgr.Save();
if (isCurrentActive) dock.TriggerDockRebuild();
refreshView();
}, Color.FromRgb(220, 60, 60));
cardContent.Children.Add(delBtn);

appCard.Child = cardContent;
appsWrap.Children.Add(appCard);
}
}
appsSection.Children.Add(appsWrap);
cardStack.Children.Add(appsSection);

host.Children.Add(card);

// 3. Catálogo de aplicativos instalados
host.Children.Add(SectionTitle("Adicionar Aplicativos Instalados", "Procure qualquer aplicativo instalado no seu Windows ou selecione um .exe"));

var catalogCard = SettingsCard();
var catalogStack = new StackPanel();
catalogCard.Child = catalogStack;

var filterBar = new Grid { Margin = new Thickness(0, 4, 0, 12) };
filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
filterBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

var searchCatalogBox = new TextBox
{
Text = workspaceAppCatalogSearch,
Foreground = String.IsNullOrWhiteSpace(workspaceAppCatalogSearch) ? new SolidColorBrush(Color.FromRgb(155, 150, 150)) : Brushes.White,
Background = new SolidColorBrush(Color.FromRgb(48, 45, 45)),
BorderBrush = new SolidColorBrush(Color.FromRgb(66, 62, 62)),
BorderThickness = new Thickness(1),
Padding = new Thickness(12, 9, 12, 9)
};
if (String.IsNullOrWhiteSpace(workspaceAppCatalogSearch)) searchCatalogBox.Text = "Buscar aplicativo (ex: VS Code, Chrome, Spotify, Steam...)";

searchCatalogBox.GotFocus += delegate
{
if (searchCatalogBox.Text == "Buscar aplicativo (ex: VS Code, Chrome, Spotify, Steam...)")
{
searchCatalogBox.Text = String.Empty;
searchCatalogBox.Foreground = Brushes.White;
}
};
searchCatalogBox.LostFocus += delegate
{
if (String.IsNullOrWhiteSpace(searchCatalogBox.Text))
{
searchCatalogBox.Text = "Buscar aplicativo (ex: VS Code, Chrome, Spotify, Steam...)";
searchCatalogBox.Foreground = new SolidColorBrush(Color.FromRgb(155, 150, 150));
}
};

var catalogScrollHost = new StackPanel();

Action updateCatalogView = delegate
{
catalogScrollHost.Children.Clear();
string filter = (workspaceAppCatalogSearch ?? String.Empty).Trim().ToLowerInvariant();
if (filter == "buscar aplicativo (ex: vs code, chrome, spotify, steam...)") filter = "";

var allApps = WorkspaceManager.GetInstalledApplications();
var filtered = String.IsNullOrWhiteSpace(filter)
? allApps
: allApps.Where(a => (a.Name ?? "").ToLowerInvariant().Contains(filter) ||
(a.Target ?? "").ToLowerInvariant().Contains(filter)).ToList();

if (filtered.Count == 0)
{
catalogScrollHost.Children.Add(new TextBlock
{
Text = "Nenhum aplicativo encontrado com esse nome.",
FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(170, 165, 165)),
Margin = new Thickness(10, 15, 10, 15)
});
}
else
{
foreach (var app in filtered.Take(40))
{
var appRef = app;
bool alreadyAdded = currentWs.Apps.Any(a => DockWindow.SameTarget(a.Target, appRef.Target) ||
(!String.IsNullOrWhiteSpace(a.AppUserModelId) && String.Equals(a.AppUserModelId, appRef.AppUserModelId, StringComparison.OrdinalIgnoreCase)) ||
String.Equals(a.Name, appRef.Name, StringComparison.OrdinalIgnoreCase));

var row = new Border
{
CornerRadius = new CornerRadius(8),
Background = new SolidColorBrush(Color.FromRgb(52, 48, 48)),
Padding = new Thickness(12, 7, 12, 7),
Margin = new Thickness(0, 0, 0, 6)
};

var rowGrid = new Grid();
rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

var rowIcon = new System.Windows.Controls.Image
{
Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center,
Source = DockWindow.ResolveAppIcon(appRef.Target, appRef.AppUserModelId)
};
rowGrid.Children.Add(rowIcon);

var rowText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
rowText.Children.Add(new TextBlock
{
Text = appRef.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
});
rowText.Children.Add(new TextBlock
{
Text = !String.IsNullOrWhiteSpace(appRef.Target) ? appRef.Target : appRef.AppUserModelId,
FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(150, 145, 145)),
TextTrimming = TextTrimming.CharacterEllipsis
});
Grid.SetColumn(rowText, 1);
rowGrid.Children.Add(rowText);

var addActionBtn = new Border
{
CornerRadius = new CornerRadius(6),
Background = new SolidColorBrush(alreadyAdded ? Color.FromRgb(40, 50, 45) : Color.FromRgb(255, 105, 82)),
Padding = new Thickness(12, 5, 12, 5),
Cursor = alreadyAdded ? Cursors.Arrow : Cursors.Hand,
VerticalAlignment = VerticalAlignment.Center
};
addActionBtn.Child = new TextBlock
{
Text = alreadyAdded ? "✓ Na Dock" : "+ Adicionar",
FontSize = 12,
FontWeight = FontWeights.SemiBold,
Foreground = alreadyAdded ? new SolidColorBrush(Color.FromRgb(120, 200, 140)) : Brushes.White
};
if (!alreadyAdded)
{
addActionBtn.MouseLeftButtonUp += delegate
{
currentWs.Apps.Add(new WorkspaceAppItem
{
Name = appRef.Name,
Target = appRef.Target,
Arguments = appRef.Arguments,
AppUserModelId = appRef.AppUserModelId,
Identity = appRef.Identity
});
wsMgr.Save();
if (isCurrentActive) dock.TriggerDockRebuild();
refreshView();
};
}
Grid.SetColumn(addActionBtn, 2);
rowGrid.Children.Add(addActionBtn);

row.Child = rowGrid;
catalogScrollHost.Children.Add(row);
}
}
};

searchCatalogBox.TextChanged += delegate
{
if (searchCatalogBox.IsKeyboardFocused)
{
workspaceAppCatalogSearch = searchCatalogBox.Text;
updateCatalogView();
}
};
Grid.SetColumn(searchCatalogBox, 0);
filterBar.Children.Add(searchCatalogBox);

var browseExeBtn = new Border
{
CornerRadius = new CornerRadius(8),
Background = new SolidColorBrush(Color.FromRgb(48, 45, 45)),
BorderBrush = new SolidColorBrush(Color.FromRgb(80, 75, 75)),
BorderThickness = new Thickness(1),
Padding = new Thickness(14, 0, 14, 0),
Margin = new Thickness(10, 0, 0, 0),
Cursor = Cursors.Hand
};
var browseSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
browseSp.Children.Add(new TextBlock { Text = "📂", FontSize = 14, Margin = new Thickness(0, 0, 6, 0) });
browseSp.Children.Add(new TextBlock { Text = "Escolher arquivo .exe…", FontSize = 13, Foreground = Brushes.White });
browseExeBtn.Child = browseSp;
browseExeBtn.MouseLeftButtonUp += delegate
{
var ofd = new Microsoft.Win32.OpenFileDialog
{
Filter = "Executáveis e Atalhos (*.exe;*.lnk)|*.exe;*.lnk|Todos os arquivos (*.*)|*.*",
Title = "Selecionar aplicativo para o espaço"
};
if (ofd.ShowDialog() == true)
{
string path = ofd.FileName;
string name = System.IO.Path.GetFileNameWithoutExtension(path);
string args = "";
if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
{
var resolved = DockWindow.ResolveShortcutPublic(path);
if (resolved != null && !String.IsNullOrWhiteSpace(resolved.Target))
{
path = resolved.Target;
args = resolved.Arguments ?? "";
if (!String.IsNullOrWhiteSpace(resolved.Name)) name = resolved.Name;
}
}
currentWs.Apps.Add(new WorkspaceAppItem { Name = name, Target = path, Arguments = args });
wsMgr.Save();
if (isCurrentActive) dock.TriggerDockRebuild();
refreshView();
}
};
Grid.SetColumn(browseExeBtn, 1);
filterBar.Children.Add(browseExeBtn);

catalogStack.Children.Add(filterBar);

var catalogScroll = new ScrollViewer
{
MaxHeight = 260,
VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
Content = catalogScrollHost
};
updateCatalogView();
catalogStack.Children.Add(catalogScroll);

host.Children.Add(catalogCard);
}

private static Border CreateActionButton(string label, Color bg, Action action)
{
var btn = new Border
{
CornerRadius = new CornerRadius(8),
Background = new SolidColorBrush(bg),
Padding = new Thickness(12, 8, 12, 8),
Margin = new Thickness(6, 0, 0, 0),
Cursor = Cursors.Hand
};
btn.Child = new TextBlock
{
Text = label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
};
btn.MouseLeftButtonUp += delegate { action(); };
return btn;
}

private static Border CreateSmallIconButton(string label, Action action, Color? fgColor = null)
{
var btn = new Border
{
Width = 22, Height = 22, CornerRadius = new CornerRadius(5),
Background = new SolidColorBrush(Color.FromRgb(65, 60, 60)),
Margin = new Thickness(2, 0, 2, 0), Cursor = Cursors.Hand
};
btn.Child = new TextBlock
{
Text = label, FontSize = 11, FontWeight = FontWeights.Bold,
Foreground = new SolidColorBrush(fgColor ?? Color.FromRgb(210, 205, 205)),
HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
};
btn.MouseLeftButtonUp += delegate { action(); };
return btn;
}

private void BuildGeneralPage(StackPanel content)
        {
            content.Children.Add(PageHeader("\uE713", "Geral", "Como a B.M Flow se encaixa no Windows."));
            content.Children.Add(SectionTitle("Integração com o Windows", "Inicialização, ocultação e espaço de trabalho"));
            var card = SettingsCard();
            var stack = new StackPanel();
            card.Child = stack;
            var startup = Toggle("Iniciar com o Windows", "Abre a Dock automaticamente ao entrar na sua conta", dock.CurrentStartWithWindows);
            startup.Checked += delegate
            {
                if (!dock.SetStartWithWindows(true)) { startup.IsChecked = false; MessageBox.Show("Não foi possível configurar a inicialização automática.", "B.M Flow"); }
            };
            startup.Unchecked += delegate { dock.SetStartWithWindows(false); };
            stack.Children.Add(startup);
            stack.Children.Add(Divider());
            var autoHide = Toggle("Ocultar automaticamente", "Revela a Dock ao encostar o cursor na borda inferior", dock.CurrentAutoHide);
            autoHide.Checked += delegate { dock.SetAutoHideEnabled(true); if (keepWindowsCheck != null) keepWindowsCheck.IsEnabled = false; };
            autoHide.Unchecked += delegate { dock.SetAutoHideEnabled(false); if (keepWindowsCheck != null) keepWindowsCheck.IsEnabled = true; };
            stack.Children.Add(autoHide);
            stack.Children.Add(Divider());
            keepWindowsCheck = Toggle("Manter janelas acima da Dock", "Reserva espaço quando a Dock permanece fixa", dock.CurrentKeepWindowsOff);
            keepWindowsCheck.IsEnabled = !dock.CurrentAutoHide;
            keepWindowsCheck.Checked += delegate { dock.SetKeepWindowsOffDock(true); };
            keepWindowsCheck.Unchecked += delegate { dock.SetKeepWindowsOffDock(false); };
            stack.Children.Add(keepWindowsCheck);
            stack.Children.Add(Divider());
            var hideTaskbar = Toggle("Ocultar barra de tarefas original", "Mantém apenas a B.M Flow e restaura a barra ao sair", dock.CurrentHideTaskbar);
            hideTaskbar.Checked += delegate { dock.SetTaskbarHidden(true); };
            hideTaskbar.Unchecked += delegate { dock.SetTaskbarHidden(false); };
            stack.Children.Add(hideTaskbar);
            content.Children.Add(card);

            content.Children.Add(SectionTitle("Assistente", "Aplicativos compatíveis instalados no Windows"));
            var assistantCard = SettingsCard();
            var assistantStack = new StackPanel();
            assistantCard.Child = assistantStack;
            assistantStack.Children.Add(ChoiceControl("Assistente padrão", dock.AssistantChoices,
                String.IsNullOrWhiteSpace(dock.CurrentAssistantName) ? "Nenhum assistente detectado" : dock.CurrentAssistantName,
                dock.SetAssistantName));
            assistantStack.Children.Add(Divider());
            assistantStack.Children.Add(ChoiceControl("Posição na Dock",
                new[] { "Após Iniciar", "Após aplicativos", "Antes da bandeja" },
                dock.CurrentAssistantPosition, dock.SetAssistantPosition));
            content.Children.Add(assistantCard);
        }

        private void BuildThemesPage(StackPanel content)
        {
            content.Children.Add(PageHeader("\uE771", "Temas", "Visuais prontos para transformar a Dock."));
            content.Children.Add(SectionTitle("Tema da Dock", "Escolha uma predefinição; a alteração é aplicada imediatamente"));
            themeCards.Clear();
            var themes = new WrapPanel { Margin = new Thickness(-5, 0, 0, 8) };
            foreach (string name in new[] { "Nativo", "Liquid Glass", "Meia-noite", "Rocket", "Prateleira Aqua", "Neon", "Canarinho", "Papel", "Flutuante" })
                themes.Children.Add(BuildThemeCard(name));
            content.Children.Add(themes);
            UpdateThemeSelection();
        }

        private void BuildAppearancePage(StackPanel content)
        {
            content.Children.Add(PageHeader("\uE790", "Aparência", "Cores e transparência dos elementos da Dock."));
            content.Children.Add(SectionTitle("Estilo visual", "Ajustes aplicados em tempo real"));
            var card = SettingsCard();
            var stack = new StackPanel();
            card.Child = stack;
            stack.Children.Add(ChoiceControl("Fundo dos ícones", new[] { "Branco", "Escuro", "Transparente", "Seguir o tema" }, dock.CurrentIconTileStyle, dock.SetIconTileStyle));
            stack.Children.Add(Divider());
            stack.Children.Add(SliderControl("Opacidade da Dock", "%", 80, 255, dock.CurrentOpacity, delegate(double value) { dock.SetDockOpacity(value); }, true));
            content.Children.Add(card);
        }

        private void BuildSizePage(StackPanel content)
        {
            content.Children.Add(PageHeader("\uE799", "Tamanho e posição", "Ajuste as proporções e o encaixe na tela."));
            content.Children.Add(SectionTitle("Dimensões", "A Dock é atualizada enquanto você movimenta os controles"));
            var card = SettingsCard();
            var stack = new StackPanel();
            card.Child = stack;
            stack.Children.Add(SliderControl("Tamanho dos ícones", "px", 40, 64, dock.CurrentIconSize, dock.SetDockIconSize));
            stack.Children.Add(Divider());
            stack.Children.Add(SliderControl("Largura do indicador", "px", 4, 18, dock.CurrentIndicatorWidth, dock.SetRunningIndicatorWidth));
            stack.Children.Add(Divider());
            stack.Children.Add(SliderControl("Espaço inferior", "px", 0, 20, dock.CurrentBottomGap, dock.SetDockBottomGap));
            content.Children.Add(card);
            content.Children.Add(SectionTitle("Organização dos aplicativos", "A ordem personalizada é salva automaticamente"));
            var hint = SettingsCard();
            hint.Child = new TextBlock
            {
                Text = "Clique, segure e arraste um aplicativo diretamente na Dock para movê-lo para a esquerda ou para a direita.",
                TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(220, 216, 214)), FontSize = 13.5
            };
            content.Children.Add(hint);
        }

        private void BuildWidgetsPage(StackPanel content)
        {
            content.Children.Add(PageHeader("\uE74C", "Widgets e Personalização", "Complementos visuais e utilitários para enriquecer a sua experiência no Windows."));
            content.Children.Add(SectionTitle("Temas de Cursor", "Alterne entre os ponteiros do macOS e Windows 11 Fluent"));

            var card = SettingsCard();
            var stack = new StackPanel();
            card.Child = stack;

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBorder = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(32, 120, 215)),
                Child = new TextBlock
                {
                    Text = "\uE8B0", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18,
                    Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            headerGrid.Children.Add(iconBorder);

            var titleStack = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Customização de Cursores", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Substitui os ponteiros do Windows por estilos refinados (macOS, Windows 11 Fluent v3 por Arteffect10520 e variações).",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(185, 180, 178)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            var controlsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var statusBadge = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            statusBadge.Child = statusText;

            Action updateStatusBadge = delegate
            {
                bool active = dock.CurrentMacCursorEnabled;
                statusBadge.Background = active ? new SolidColorBrush(Color.FromArgb(45, 46, 204, 113)) : new SolidColorBrush(Color.FromArgb(35, 160, 160, 160));
                statusBadge.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(46, 204, 113)) : new SolidColorBrush(Color.FromRgb(120, 120, 120));
                statusBadge.BorderThickness = new Thickness(1);
                statusText.Text = active ? "✓ Ativo" : "Padrão do Windows";
                statusText.Foreground = active ? new SolidColorBrush(Color.FromRgb(70, 230, 140)) : new SolidColorBrush(Color.FromRgb(190, 185, 185));
            };
            updateStatusBadge();
            controlsStack.Children.Add(statusBadge);

            var toggleCheck = new CheckBox
            {
                IsChecked = dock.CurrentMacCursorEnabled,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            controlsStack.Children.Add(toggleCheck);
            Grid.SetColumn(controlsStack, 2);
            headerGrid.Children.Add(controlsStack);

            stack.Children.Add(headerGrid);
            stack.Children.Add(Divider());

            // 1. Theme Selector
            var themeGrid = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            themeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            themeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var themeLabel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            themeLabel.Children.Add(new TextBlock { Text = "Pacote de cursores", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            themeLabel.Children.Add(new TextBlock { Text = "Selecione o estilo visual do cursor para o seu sistema", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(175, 170, 169)) });
            themeGrid.Children.Add(themeLabel);

            var themeButtons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            var themePairs = new[]
            {
                new { Key = "macOS", Label = "macOS Apple" },
                new { Key = "FluentDark", Label = "Fluent Dark" },
                new { Key = "FluentLight", Label = "Fluent Light" },
                new { Key = "FluentArteffect", Label = "Fluent v3 (Arteffect10520)" }
            };
            var themePills = new Dictionary<string, Border>();

            var sizeGrid = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            var arteffectBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 0, 150, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 150, 255)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 8, 0, 8)
            };
            var gallery = new WrapPanel { Margin = new Thickness(-4, 0, 0, 4) };

            Action refreshGallery = delegate
            {
                gallery.Children.Clear();
                string curTheme = dock.CurrentCursorTheme;
                string activeSize = dock.CurrentMacCursorSize;
                gallery.Children.Add(BuildCursorPreviewItem("Arrow", "Normal", curTheme, activeSize));
                gallery.Children.Add(BuildCursorPreviewItem("Hand", "Link", curTheme, activeSize));
                gallery.Children.Add(BuildCursorPreviewItem("Wait", "Ocupado", curTheme, activeSize));
                gallery.Children.Add(BuildCursorPreviewItem("AppStarting", "2º Plano", curTheme, activeSize));
                gallery.Children.Add(BuildCursorPreviewItem("IBeam", "Texto", curTheme, activeSize));
                gallery.Children.Add(BuildCursorPreviewItem("SizeAll", "Mover", curTheme, activeSize));
                gallery.Children.Add(BuildCursorPreviewItem("SizeNS", "Vertical", curTheme, activeSize));
                gallery.Children.Add(BuildCursorPreviewItem("SizeWE", "Horizontal", curTheme, activeSize));
            };

            Action updateThemeSelection = delegate
            {
                string curTheme = dock.CurrentCursorTheme;
                foreach (var pair in themePills)
                {
                    bool isSel = String.Equals(pair.Key, curTheme, StringComparison.OrdinalIgnoreCase);
                    pair.Value.Background = isSel ? new SolidColorBrush(Color.FromRgb(255, 101, 75)) : new SolidColorBrush(Color.FromRgb(55, 50, 48));
                    pair.Value.BorderBrush = isSel ? new SolidColorBrush(Color.FromRgb(255, 130, 105)) : new SolidColorBrush(Color.FromRgb(85, 78, 75));
                    var tb = pair.Value.Child as TextBlock;
                    if (tb != null) tb.Foreground = isSel ? Brushes.White : new SolidColorBrush(Color.FromRgb(210, 205, 205));
                }

                sizeGrid.Visibility = String.Equals(curTheme, "macOS", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

                if (String.Equals(curTheme, "FluentArteffect", StringComparison.OrdinalIgnoreCase))
                {
                    string artDir = MacCursorService.ResolveThemeDir("FluentArteffect");
                    bool has = MacCursorService.HasCursors(artDir);
                    arteffectBanner.Visibility = Visibility.Visible;
                    var panel = arteffectBanner.Child as StackPanel;
                    if (panel != null)
                    {
                        var statusHeader = panel.Children.OfType<TextBlock>().FirstOrDefault();
                        if (statusHeader != null)
                        {
                            statusHeader.Text = has ? "✓ Pacote Arteffect10520 v3 pronto e ativo!" : "Windows 11 Fluent Cursor v3 por Arteffect10520:";
                            statusHeader.Foreground = has ? new SolidColorBrush(Color.FromRgb(70, 230, 140)) : Brushes.White;
                        }
                    }
                }
                else
                {
                    arteffectBanner.Visibility = Visibility.Collapsed;
                }

                refreshGallery();
            };

            foreach (var item in themePairs)
            {
                string tKey = item.Key;
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(11, 6, 11, 6),
                    Margin = new Thickness(4, 3, 0, 3),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = item.Label, FontSize = 12, FontWeight = FontWeights.Medium }
                };
                pill.MouseLeftButtonUp += delegate
                {
                    dock.SetCursorTheme(tKey);
                    updateThemeSelection();
                };
                themePills[tKey] = pill;
                themeButtons.Children.Add(pill);
            }
            Grid.SetColumn(themeButtons, 1);
            themeGrid.Children.Add(themeButtons);
            stack.Children.Add(themeGrid);

            // Arteffect helper banner
            var bannerStack = new StackPanel();
            bannerStack.Children.Add(new TextBlock
            {
                Text = "Windows 11 Fluent Cursor v3 por Arteffect10520:",
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
            });
            bannerStack.Children.Add(new TextBlock
            {
                Text = "O pacote v3 do Arteffect10520 é disponibilizado pelo autor no DeviantArt. Para usá-lo, clique no botão para abrir a pasta de cursores e cole os arquivos (.cur/.ani) extraídos.",
                FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(210, 225, 245)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 8)
            });

            var bannerButtons = new StackPanel { Orientation = Orientation.Horizontal };
            var openFolderBtn = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = "📂 Abrir pasta do tema", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }
            };
            openFolderBtn.MouseLeftButtonUp += delegate
            {
                string dir = MacCursorService.ResolveThemeDir("FluentArteffect");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            };
            bannerButtons.Children.Add(openFolderBtn);

            var devArtBtn = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 60)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 90, 105)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "🌐 Baixar no DeviantArt (Arteffect10520)", FontSize = 11.5, Foreground = Brushes.White }
            };
            devArtBtn.MouseLeftButtonUp += delegate
            {
                Process.Start(new ProcessStartInfo("https://www.deviantart.com/arteffect10520/art/Windows-11-Fluent-Cursor-v3-1009751409") { UseShellExecute = true });
            };
            bannerButtons.Children.Add(devArtBtn);
            bannerStack.Children.Add(bannerButtons);
            arteffectBanner.Child = bannerStack;
            stack.Children.Add(arteffectBanner);

            // Size Selector (Visible for macOS)
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sizeLabel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sizeLabel.Children.Add(new TextBlock { Text = "Tamanho do cursor (macOS)", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            sizeLabel.Children.Add(new TextBlock { Text = "Escolha a escala visual que melhor se adapta à resolução da sua tela", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(175, 170, 169)) });
            sizeGrid.Children.Add(sizeLabel);

            var sizeButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string[] sizes = new[] { "Regular", "Grande", "Extra Grande" };
            var sizePills = new Dictionary<string, Border>();

            Action updateSizeSelection = delegate
            {
                string cur = dock.CurrentMacCursorSize;
                foreach (var pair in sizePills)
                {
                    bool isSel = String.Equals(pair.Key, cur, StringComparison.OrdinalIgnoreCase);
                    pair.Value.Background = isSel ? new SolidColorBrush(Color.FromRgb(255, 101, 75)) : new SolidColorBrush(Color.FromRgb(55, 50, 48));
                    pair.Value.BorderBrush = isSel ? new SolidColorBrush(Color.FromRgb(255, 130, 105)) : new SolidColorBrush(Color.FromRgb(85, 78, 75));
                    var tb = pair.Value.Child as TextBlock;
                    if (tb != null) tb.Foreground = isSel ? Brushes.White : new SolidColorBrush(Color.FromRgb(210, 205, 205));
                }
            };

            foreach (string sz in sizes)
            {
                string targetSize = sz;
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(5, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = targetSize, FontSize = 12.5, FontWeight = FontWeights.Medium }
                };
                pill.MouseLeftButtonUp += delegate
                {
                    dock.SetMacCursorSize(targetSize);
                    updateSizeSelection();
                    refreshGallery();
                };
                sizePills[targetSize] = pill;
                sizeButtons.Children.Add(pill);
            }
            updateSizeSelection();
            Grid.SetColumn(sizeButtons, 1);
            sizeGrid.Children.Add(sizeButtons);
            stack.Children.Add(sizeGrid);

            stack.Children.Add(Divider());

            stack.Children.Add(new TextBlock
            {
                Text = "Prévia dos cursores:",
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(225, 220, 218)),
                Margin = new Thickness(0, 4, 0, 8)
            });

            stack.Children.Add(gallery);
            stack.Children.Add(Divider());

            var restoreRow = new Grid { Margin = new Thickness(0, 4, 0, 2) };
            restoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            restoreRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            restoreRow.Children.Add(new TextBlock
            {
                Text = "Deseja voltar aos cursores normais do Windows?",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(175, 170, 169)),
                VerticalAlignment = VerticalAlignment.Center
            });
            var restoreBtn = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(55, 50, 48)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(85, 78, 75)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 6, 14, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = "Restaurar padrão do Windows", FontSize = 12, Foreground = Brushes.White }
            };
            restoreBtn.MouseEnter += delegate { restoreBtn.Background = new SolidColorBrush(Color.FromRgb(70, 64, 62)); };
            restoreBtn.MouseLeave += delegate { restoreBtn.Background = new SolidColorBrush(Color.FromRgb(55, 50, 48)); };
            restoreBtn.MouseLeftButtonUp += delegate
            {
                toggleCheck.IsChecked = false;
                dock.SetMacCursorEnabled(false);
                updateStatusBadge();
            };
            Grid.SetColumn(restoreBtn, 1);
            restoreRow.Children.Add(restoreBtn);
            stack.Children.Add(restoreRow);

            toggleCheck.Checked += delegate
            {
                dock.SetMacCursorEnabled(true);
                updateStatusBadge();
            };
            toggleCheck.Unchecked += delegate
            {
                dock.SetMacCursorEnabled(false);
                updateStatusBadge();
            };

            updateThemeSelection();
            content.Children.Add(card);

            content.Children.Add(SectionTitle("Galeria de Extensões e Widgets", "Novos recursos planejados para a B.M Flow"));
            var futureCard = SettingsCard();
            var futureGrid = new Grid();
            futureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            futureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var starIcon = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromRgb(65, 58, 75)),
                Child = new TextBlock
                {
                    Text = "\uE734", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 17,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 205, 80)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            futureGrid.Children.Add(starIcon);

            var futureText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            futureText.Children.Add(new TextBlock { Text = "Novos widgets em desenvolvimento", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            futureText.Children.Add(new TextBlock
            {
                Text = "Widgets de monitoramento de recursos da máquina, relógio flutuante estilo macOS e atalhos dinâmicos estão sendo desenvolvidos para as próximas versões.",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(170, 165, 165)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(futureText, 1);
            futureGrid.Children.Add(futureText);
            futureCard.Child = futureGrid;
            content.Children.Add(futureCard);
        }

        private static FrameworkElement BuildCursorPreviewItem(string role, string label, string theme, string size)
        {
            var item = new Border
            {
                Width = 92, Height = 74, CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(52, 47, 46)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(72, 65, 63)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4),
                Padding = new Thickness(4)
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            string cursorDir = MacCursorService.ResolveThemeDir(theme, size);
            string fullPath = MacCursorService.FindCursorFile(cursorDir, role);
            ImageSource img = !String.IsNullOrEmpty(fullPath) ? MacCursorService.LoadCursorImage(fullPath) : null;

            if (img != null)
            {
                stack.Children.Add(new Image
                {
                    Source = img, Width = 28, Height = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Stretch = Stretch.Uniform
                });
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "⮹", FontSize = 22, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(200, 195, 194)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 84
            });

            item.Child = stack;
            return item;
        }


        private void BuildAboutPage(StackPanel content)
        {
            content.Children.Add(PageHeader("\uE946", "Sobre", "Informações sobre esta instalação da B.M Flow."));
            var card = SettingsCard();
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "B.M Flow para Windows", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            stack.Children.Add(new TextBlock { Text = "Uma Dock nativa, integrada à barra de tarefas, às prévias de janela e aos painéis do Windows 11.", FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromRgb(190, 184, 182)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 5) });
            stack.Children.Add(new TextBlock { Text = "Configurações salvas automaticamente no seu perfil do Windows.", FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(157, 152, 151)), Margin = new Thickness(0, 8, 0, 0) });
            card.Child = stack;
            content.Children.Add(card);
        }

        private static FrameworkElement SectionTitle(string title, string subtitle)
        {
            var panel = new StackPanel { Margin = new Thickness(4, 18, 4, 10) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(174, 168, 167)), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        private static Border SettingsCard()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(67, 61, 59)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(82, 75, 72)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15),
                Padding = new Thickness(20, 14, 20, 14), Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static CheckBox Toggle(string title, string subtitle, bool value)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(185, 180, 178)), TextWrapping = TextWrapping.Wrap });
            return new CheckBox
            {
                Content = text, IsChecked = value, Foreground = Brushes.White,
                Margin = new Thickness(0, 7, 0, 7), Cursor = Cursors.Hand, VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private Border BuildThemeCard(string name)
        {
            Color background = Color.FromRgb(13, 62, 66);
            Color dockColor = Color.FromRgb(43, 47, 56);
            Color borderColor = Color.FromRgb(120, 130, 145);
            if (name == "Liquid Glass") { dockColor = Color.FromRgb(105, 139, 153); borderColor = Color.FromRgb(225, 252, 255); }
            else if (name == "Meia-noite") { background = Color.FromRgb(7, 48, 55); dockColor = Color.FromRgb(13, 18, 31); borderColor = Color.FromRgb(135, 153, 175); }
            else if (name == "Rocket") { dockColor = Color.FromRgb(27, 34, 54); borderColor = Color.FromRgb(45, 118, 255); }
            else if (name == "Prateleira Aqua") { dockColor = Color.FromRgb(65, 105, 107); borderColor = Color.FromRgb(157, 245, 238); }
            else if (name == "Neon") { dockColor = Color.FromRgb(10, 15, 27); borderColor = Color.FromRgb(0, 238, 255); }
            else if (name == "Canarinho") { dockColor = Color.FromRgb(139, 164, 31); borderColor = Color.FromRgb(245, 226, 43); }
            else if (name == "Papel") { dockColor = Color.FromRgb(250, 249, 244); borderColor = Color.FromRgb(225, 225, 220); }
            else if (name == "Flutuante") { dockColor = Color.FromArgb(25, 20, 23, 29); borderColor = Colors.Transparent; }

            var card = new Border
            {
                Width = 214, Height = 142, Margin = new Thickness(5), Padding = new Thickness(8),
                CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(67, 61, 59)),
                BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, Tag = name
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            var preview = new Border { CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(background), Margin = new Thickness(0, 0, 0, 5) };
            var miniDock = new Border
            {
                Width = 146, Height = 48, CornerRadius = new CornerRadius(name == "Prateleira Aqua" ? 7 : 15),
                Background = new SolidColorBrush(dockColor), BorderBrush = new SolidColorBrush(borderColor), BorderThickness = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            foreach (Color color in new[] { Color.FromRgb(32, 132, 235), Color.FromRgb(248, 248, 246), Color.FromRgb(32, 132, 235) })
                icons.Children.Add(new Border { Width = 29, Height = 29, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(color), Margin = new Thickness(5, 0, 5, 0) });
            miniDock.Child = icons;
            preview.Child = miniDock;
            grid.Children.Add(preview);
            var label = new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontSize = 13 };
            Grid.SetRow(label, 1);
            grid.Children.Add(label);
            card.Child = grid;
            card.MouseLeftButtonUp += delegate { dock.SelectTheme(name); UpdateThemeSelection(); };
            themeCards[name] = card;
            return card;
        }

        private void UpdateThemeSelection()
        {
            foreach (var item in themeCards)
            {
                bool selected = item.Key == dock.CurrentTheme;
                item.Value.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(255, 103, 76)) : Brushes.Transparent;
                item.Value.Background = new SolidColorBrush(selected ? Color.FromRgb(82, 66, 61) : Color.FromRgb(67, 61, 59));
            }
        }

        private static FrameworkElement SliderControl(string title, string suffix, double minimum, double maximum, double value, Action<double> changed, bool percentage = false)
        {
            var panel = new StackPanel { Margin = new Thickness(2, 7, 2, 7) };
            var caption = new Grid();
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            caption.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            var valueText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(190, 184, 182)), FontSize = 13 };
            Grid.SetColumn(valueText, 1);
            caption.Children.Add(valueText);
            panel.Children.Add(caption);
            var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Margin = new Thickness(0, 10, 0, 4) };
            Action updateLabel = delegate
            {
                valueText.Text = percentage ? Math.Round(slider.Value / 255.0 * 100) + " " + suffix : Math.Round(slider.Value) + " " + suffix;
            };
            updateLabel();
            slider.ValueChanged += delegate { updateLabel(); changed(slider.Value); };
            panel.Children.Add(slider);
            return panel;
        }

        private static FrameworkElement ChoiceControl(string title, IEnumerable<string> choices, string value, Action<string> changed)
        {
            var grid = new Grid { Margin = new Thickness(2, 7, 2, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.Children.Add(new TextBlock
            {
                Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
            });
            var combo = new ComboBox
            {
                ItemsSource = choices.ToList(), SelectedItem = value,
                Padding = new Thickness(8, 5, 8, 5), Cursor = Cursors.Hand
            };
            combo.SelectionChanged += delegate
            {
                string selected = combo.SelectedItem as string;
                if (!String.IsNullOrWhiteSpace(selected)) changed(selected);
            };
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);
            return grid;
        }

        private static Border Divider()
        {
            return new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(88, 81, 78)), Margin = new Thickness(0, 7, 0, 7) };
        }
    }

    internal sealed class TaskApp
    {
        public string Name;
        public string Target;
        public string Arguments;
        public string Identity;
        public string AppUserModelId;
        public IntPtr WindowHandle;
        public readonly List<IntPtr> WindowHandles = new List<IntPtr>();
        public readonly List<string> WindowTitles = new List<string>();
        public ImageSource Icon;
        public Color? AccentColor;
        public bool IsPinned;
        public bool IsRunningInBackground;

        public Color? GetAccentColor()
        {
            if (AccentColor.HasValue) return AccentColor;
            if (Icon != null)
            {
                AccentColor = DockWindow.ExtractAccentColor(Icon);
            }
            return AccentColor;
        }
    }

    internal sealed class RecentDestination
    {
        public string Name;
        public string Path;
        public bool IsFolder;
    }

    internal static class AudioController
    {
        private const int Render = 0;
        private const int Multimedia = 1;

        public static bool TryGetState(out float volume, out bool muted)
        {
            volume = 0;
            muted = false;
            IAudioEndpointVolume endpoint = GetEndpoint();
            if (endpoint == null) return false;
            try
            {
                endpoint.GetMasterVolumeLevelScalar(out volume);
                endpoint.GetMute(out muted);
                return true;
            }
            catch { return false; }
            finally { Marshal.FinalReleaseComObject(endpoint); }
        }

        public static void ToggleMute()
        {
            IAudioEndpointVolume endpoint = GetEndpoint();
            if (endpoint == null) return;
            try
            {
                bool muted;
                endpoint.GetMute(out muted);
                Guid context = Guid.Empty;
                endpoint.SetMute(!muted, ref context);
            }
            catch { }
            finally { Marshal.FinalReleaseComObject(endpoint); }
        }

        private static IAudioEndpointVolume GetEndpoint()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out device));
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object endpoint;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
                return (IAudioEndpointVolume)endpoint;
            }
            catch { return null; }
            finally
            {
                if (device != null) Marshal.FinalReleaseComObject(device);
                if (enumerator != null) Marshal.FinalReleaseComObject(enumerator);
            }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid id, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        private interface IAudioEndpointVolume
        {
            int NotImpl1();
            int NotImpl2();
            int GetChannelCount(out int channelCount);
            int SetMasterVolumeLevel(float levelDB, ref Guid eventContext);
            int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            int GetMasterVolumeLevel(out float levelDB);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint channelNumber, float levelDB, ref Guid eventContext);
            int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);
            int GetChannelVolumeLevel(uint channelNumber, out float levelDB);
            int GetChannelVolumeLevelScalar(uint channelNumber, out float level);
            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
        }
    }
}
