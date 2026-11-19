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
    public sealed partial class DockWindow : Window
    {
        private static readonly int currentProcessId = ReadCurrentProcessId();
        private const string WindowsTerminalAppId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
        private static readonly FontFamily windowsSystemIconFont = new FontFamily("Segoe Fluent Icons");
        private readonly Dictionary<string, Border> runningDots = new Dictionary<string, Border>();
        private static readonly Dictionary<string, WeakReference> iconCache = new Dictionary<string, WeakReference>(StringComparer.OrdinalIgnoreCase);
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
        private System.Threading.Timer memoryTrimTimer;
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
        private DateTime nextSystemInfoRefreshUtc = DateTime.MinValue;
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
        private int trayPopupAnchorY;
        private int trayPopupBarLeft;
        private int trayPopupBarTop;
        private int trayPopupBarRight;
        private int trayPopupBarBottom;
        private int trayPopupWorkLeft;
        private int trayPopupWorkRight;
        private int trayPopupWorkTop;
        private int trayPopupWorkBottom;
        private WinEventDelegate quickSettingsEventDelegate;
        private IntPtr quickSettingsEventHook;
        private int quickSettingsHookGeneration;
        private int quickSettingsAnchorX;
        private int quickSettingsAnchorY;
        private int quickSettingsBarLeft;
        private int quickSettingsBarTop;
        private int quickSettingsBarRight;
        private int quickSettingsBarBottom;
        private int quickSettingsWorkLeft;
        private int quickSettingsWorkRight;
        private int quickSettingsWorkTop;
        private int quickSettingsWorkBottom;
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
            public Border Indicator;
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
                InitializeMediaPlayer();
                UpdateTopBarVisibility();
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
                CloseTopBar();
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
                DisposeMediaPlayer();
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
                if (memoryTrimTimer != null) memoryTrimTimer.Dispose();
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
            adaptiveContrastTimer = new System.Threading.Timer(SampleAdaptiveContrastBackground, null, 300, 1000);
            volumeMonitorTimer = new System.Threading.Timer(MonitorVolumeBackground, null, 450, 1500);
            batteryMonitorTimer = new System.Threading.Timer(MonitorBatteryBackground, null, 900, 10000);
            networkMonitorTimer = new System.Threading.Timer(MonitorNetworkBackground, null, 1200, 10000);
            pinnedStateMonitorTimer = new System.Threading.Timer(MonitorPinnedStateBackground, null, 1800, 15000);

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
                nextTaskRefreshUtc = DateTime.MinValue;
                RefreshTaskItems();
            };
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            ScheduleMemoryTrim(7000);
        }

        private UIElement BuildDock()
        {
            var root = new Grid { ClipToBounds = false, Background = Brushes.Transparent };
            dockRoot = root;
            root.MouseEnter += delegate
            {
                // Com a Dock recolhida, apenas a borda física da tela deve
                // revelá-la. Assim o mascote continua interativo sem abrir a
                // barra quando o cursor passa sobre ele.
                if (autoHideEnabled && dockAutoHidden) return;
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
            menu.MinWidth = 300;
            dockMenu = menu;
            menu.Opened += delegate { SyncDockMenu(); };
            var settingsItem = new MenuItem
            {
                Header = "Configurações…",
                Icon = DockMenuIcon("\uE713"),
                Background = new SolidColorBrush(Color.FromArgb(170, 27, 66, 119))
            };
            settingsItem.Click += delegate { OpenSettingsWindow(); };
            menu.Items.Add(settingsItem);
            menu.Items.Add(new System.Windows.Controls.Separator());
            var wsMenu = new MenuItem { Header = "Espaços de Trabalho", Icon = DockMenuIcon("\uE8B7") };
            menu.Items.Add(wsMenu);
            var themeMenu = new MenuItem { Header = "Tema", Icon = DockMenuIcon("\uE790") };
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
            var sizeMenu = new MenuItem { Header = "Tamanho e posição", Icon = DockMenuIcon("\uE799") };
            var positionMenu = new MenuItem { Header = "Posição na tela" };
            foreach (string position in DockPositionChoices)
            {
                string selectedPosition = position;
                var positionItem = new MenuItem
                {
                    Header = selectedPosition,
                    IsCheckable = true,
                    IsChecked = String.Equals(dockPosition, selectedPosition, StringComparison.OrdinalIgnoreCase)
                };
                positionItem.Click += delegate { SetDockPosition(selectedPosition); };
                dockPositionMenuItems[selectedPosition] = positionItem;
                positionMenu.Items.Add(positionItem);
            }
            sizeMenu.Items.Add(positionMenu);
            sizeMenu.Items.Add(new System.Windows.Controls.Separator());
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
            sizeMenu.Items.Add(SliderMenuItem("Espaço da borda", "px", 0, 20, dockBottomGap, delegate(double value)
            {
                dockBottomGap = value;
                UpdateAppBarRegistration();
                SchedulePositionDock();
                SaveSettings();
            }));
            menu.Items.Add(sizeMenu);
            var transparencyMenu = new MenuItem { Header = "Transparência", Icon = DockMenuIcon("\uE8A7") };
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
            menu.Items.Add(new System.Windows.Controls.Separator());
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
            var closeItem = new MenuItem
            {
                Header = "Fechar B.M Flow",
                Icon = DockMenuIcon("\uE8BB"),
                Background = new SolidColorBrush(Color.FromArgb(175, 53, 55, 63)),
                FontWeight = FontWeights.SemiBold
            };
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
            row.Children.Add(BuildMediaPlayerCard());
            string downloadsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            row.Children.Add(ImageTile(IconForShellPath(downloadsPath), "Downloads", delegate { Launch("explorer.exe", downloadsPath); }));
            row.Children.Add(RecycleBinTile());
            systemCardSeparator = Separator();
            row.Children.Add(systemCardSeparator);
            systemCardHolder = SystemCard();
            row.Children.Add(systemCardHolder);
            UpdateDockSystemCardVisibility();
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
            ApplyDockOrientation();
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

                int mediaIndex = mediaPlayerHolder == null ? -1 : dockRow.Children.IndexOf(mediaPlayerHolder);
                int index = mediaIndex >= 0
                    ? Math.Min(dockRow.Children.Count, mediaIndex + 1)
                    : Math.Min(dockRow.Children.Count, dockRow.Children.IndexOf(taskItems) + 1);
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

        private static TextBlock DockMenuIcon(string glyph)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 17,
                Foreground = new SolidColorBrush(Color.FromRgb(202, 216, 240)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
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

        private void ApplyDockSizing()
        {
            if (magnifyHost == null || dockShell == null) return;
            ApplyDockOrientation();
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
                    stack.Cursor = IsVerticalDock ? Cursors.SizeNS : Cursors.SizeWE;
                    Mouse.Capture(stack, CaptureMode.SubTree);
                }
                double pointer = IsVerticalDock ? current.Y : current.X;
                MoveTaskTileToPointer(stack, pointer);
                PositionDraggedTaskTile(stack, pointer);
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
            var indicator = new Border
            {
                Width = app.WindowHandles.Count > 0 ? runningIndicatorWidth : 0,
                Height = app.WindowHandles.Count > 0 ? 4 : 0,
                Background = new SolidColorBrush(tileAccent),
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = "RunningIndicator"
            };
            PlaceRunningIndicator(stack, indicator);
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

        private void MoveTaskTileToPointer(FrameworkElement dragged, double pointer)
        {
            if (dragged == null || taskItems == null || !taskItems.Children.Contains(dragged)) return;
            int insertIndex = 0;
            foreach (FrameworkElement element in taskItems.Children.OfType<FrameworkElement>())
            {
                if (element == dragged) continue;
                Vector offset = VisualTreeHelper.GetOffset(element);
                double center = IsVerticalDock ? offset.Y + element.ActualHeight / 2 : offset.X + element.ActualWidth / 2;
                if (pointer >= center) insertIndex++;
            }

            int oldIndex = taskItems.Children.IndexOf(dragged);
            if (oldIndex == insertIndex) return;
            var oldPositions = new Dictionary<FrameworkElement, double>();
            foreach (FrameworkElement element in taskItems.Children.OfType<FrameworkElement>())
            {
                if (element != dragged)
                {
                    Point position = element.TranslatePoint(new Point(0, 0), taskItems);
                    oldPositions[element] = IsVerticalDock ? position.Y : position.X;
                }
            }
            taskItems.Children.Remove(dragged);
            insertIndex = Math.Max(0, Math.Min(insertIndex, taskItems.Children.Count));
            taskItems.Children.Insert(insertIndex, dragged);
            taskItems.UpdateLayout();
            foreach (var item in oldPositions)
            {
                Point newPosition = item.Key.TranslatePoint(new Point(0, 0), taskItems);
                double newCoordinate = IsVerticalDock ? newPosition.Y : newPosition.X;
                double delta = item.Value - newCoordinate;
                if (Math.Abs(delta) < .5) continue;
                var translate = item.Key.RenderTransform as TranslateTransform;
                if (translate == null)
                {
                    translate = new TranslateTransform();
                    item.Key.RenderTransform = translate;
                }
                DependencyProperty property = IsVerticalDock ? TranslateTransform.YProperty : TranslateTransform.XProperty;
                translate.BeginAnimation(property, null);
                if (IsVerticalDock) translate.Y = 0; else translate.X = 0;
                translate.BeginAnimation(property, new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(145))
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
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.X = 0;
                translate.Y = 0;
            }
            dragged.Opacity = .96;
            Panel.SetZIndex(dragged, 1000);
        }

        private void PositionDraggedTaskTile(FrameworkElement dragged, double pointer)
        {
            if (dragged == null || taskItems == null) return;
            Vector offset = VisualTreeHelper.GetOffset(dragged);
            double center = IsVerticalDock ? offset.Y + dragged.ActualHeight / 2 : offset.X + dragged.ActualWidth / 2;
            var translate = dragged.RenderTransform as TranslateTransform;
            if (translate == null)
            {
                translate = new TranslateTransform();
                dragged.RenderTransform = translate;
            }
            DependencyProperty property = IsVerticalDock ? TranslateTransform.YProperty : TranslateTransform.XProperty;
            translate.BeginAnimation(property, null);
            if (IsVerticalDock) translate.Y = pointer - center; else translate.X = pointer - center;
        }

        private void FinishTaskTileDrag(FrameworkElement dragged)
        {
            if (dragged == null) return;
            dragged.Opacity = 1;
            Panel.SetZIndex(dragged, 0);
            var translate = dragged.RenderTransform as TranslateTransform;
            if (translate == null) return;
            DependencyProperty property = IsVerticalDock ? TranslateTransform.YProperty : TranslateTransform.XProperty;
            double start = IsVerticalDock ? translate.Y : translate.X;
            translate.BeginAnimation(property, null);
            if (IsVerticalDock) translate.Y = 0; else translate.X = 0;
            translate.BeginAnimation(property, new DoubleAnimation(start, 0, TimeSpan.FromMilliseconds(120))
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
            PreviewDockSide previewSide = IsDockLeft ? PreviewDockSide.Left
                : IsDockRight ? PreviewDockSide.Right
                : IsDockTop ? PreviewDockSide.Top
                : PreviewDockSide.Bottom;
            previewWindow = new PreviewWindow(pendingPreview.WindowHandles, pendingPreview.WindowTitles, pendingPreview.Name, screenArea, previewSide, accent);
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

            Point localAnchor = IsDockLeft
                ? new Point(activePreviewAnchor.ActualWidth, activePreviewAnchor.ActualHeight / 2.0)
                : IsDockRight
                    ? new Point(0, activePreviewAnchor.ActualHeight / 2.0)
                    : IsDockTop
                        ? new Point(activePreviewAnchor.ActualWidth / 2.0, activePreviewAnchor.ActualHeight)
                        : new Point(activePreviewAnchor.ActualWidth / 2.0, 0);
            Point screenPoint = activePreviewAnchor.PointToScreen(localAnchor);
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
                screenPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);

            double left = IsDockLeft ? screenPoint.X + 7
                : IsDockRight ? screenPoint.X - previewWindow.Width - 7
                : screenPoint.X - previewWindow.Width / 2;
            double top = IsDockTop ? screenPoint.Y + 7
                : IsDockBottom ? screenPoint.Y - previewWindow.Height - 7
                : screenPoint.Y - previewWindow.Height / 2;
            previewWindow.Left = Math.Max(SystemParameters.VirtualScreenLeft + 6, Math.Min(left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - previewWindow.Width - 6));
            previewWindow.Top = Math.Max(SystemParameters.VirtualScreenTop + 6, Math.Min(top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - previewWindow.Height - 6));
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
                Point localAnchor = IsDockLeft
                    ? new Point(activePreviewAnchor.ActualWidth, activePreviewAnchor.ActualHeight / 2.0)
                    : IsDockRight
                        ? new Point(0, activePreviewAnchor.ActualHeight / 2.0)
                        : IsDockTop
                            ? new Point(activePreviewAnchor.ActualWidth / 2.0, activePreviewAnchor.ActualHeight)
                            : new Point(activePreviewAnchor.ActualWidth / 2.0, 0);
                Point anchor = activePreviewAnchor.PointToScreen(localAnchor);
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
                int previewLeft = IsDockLeft
                    ? (int)Math.Round(anchor.X + 7 * dpiX)
                    : IsDockRight
                        ? (int)Math.Round(anchor.X - width - 7 * dpiX)
                        : (int)Math.Round(anchor.X - width / 2.0);
                int previewTop = IsDockTop
                    ? (int)Math.Round(anchor.Y + 7 * dpiY)
                    : IsDockBottom
                        ? (int)Math.Round(anchor.Y - height - 7 * dpiY)
                        : (int)Math.Round(anchor.Y - height / 2.0);
                previewLeft = Math.Max(screen.WorkingArea.Left + 6,
                    Math.Min(previewLeft, screen.WorkingArea.Right - width - 6));
                previewTop = Math.Max(screen.WorkingArea.Top + 6,
                    Math.Min(previewTop, screen.WorkingArea.Bottom - height - 6));
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
                Background = new SolidColorBrush(Color.FromRgb(25, 120, 225)),
                CornerRadius = new CornerRadius(1.5),
                Visibility = Visibility.Hidden,
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = "RunningIndicator"
            };
            PlaceRunningIndicator(stack, dot);
            if (!String.IsNullOrWhiteSpace(processName)) runningDots[processName] = dot;
            return stack;
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
            systemCardGrid = grid;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

            var clockStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand };
            systemClockStack = clockStack;
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
            systemTrayGrid = tray;
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
            element.RenderTransformOrigin = IsDockLeft ? new Point(0, .5)
                : IsDockRight ? new Point(1, .5)
                : IsDockTop ? new Point(.5, 0)
                : new Point(.5, 1);
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
            Point pointer = Mouse.GetPosition(magnifyHost);
            UpdateDockWave(magnifyHost, IsVerticalDock ? pointer.Y : pointer.X, elapsed);
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
            bool vertical = IsVerticalDock;
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
                Border indicator = FindRunningIndicator(container);
                var indicatorTranslate = indicator == null ? null : indicator.RenderTransform as TranslateTransform;
                double currentScale = scaleTransform == null ? 1 : scaleTransform.ScaleX;
                double currentOffset = translate == null ? 0 : (vertical ? translate.Y : translate.X);
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
                    translate.BeginAnimation(vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty, null);
                    if (vertical) translate.Y = currentOffset; else translate.X = currentOffset;
                }
                if (indicatorTranslate != null)
                {
                    double currentIndicatorOffset = indicatorTranslate.X;
                    indicatorTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                    indicatorTranslate.X = currentIndicatorOffset;
                }
                double center;
                try
                {
                    Point iconCenter = icon.TranslatePoint(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2), dockRow);
                    center = vertical ? iconCenter.Y : iconCenter.X;
                    center -= currentOffset;
                }
                catch { continue; }
                double growth;
                if (!magnifyGrowth.TryGetValue(icon, out growth)) growth = .38;
                waveSamples.Add(new WaveSample
                {
                    Icon = icon,
                    Container = container,
                    Indicator = indicator,
                    Center = center,
                    Scale = currentScale,
                    Growth = growth,
                    ExtraWidth = (vertical ? icon.ActualHeight : icon.ActualWidth) * (currentScale - 1),
                    Influence = 0,
                    Offset = currentOffset,
                    ZIndex = Panel.GetZIndex(container)
                });
            }

            foreach (var passive in wavePassives)
            {
                if (!passive.IsVisible) continue;
                var translate = passive.RenderTransform as TranslateTransform;
                double currentOffset = translate == null ? 0 : (vertical ? translate.Y : translate.X);
                if (translate == null)
                {
                    translate = new TranslateTransform();
                    passive.RenderTransform = translate;
                }
                else
                {
                    translate.BeginAnimation(vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty, null);
                    if (vertical) translate.Y = currentOffset; else translate.X = currentOffset;
                }
                double center;
                try
                {
                    Point passiveCenter = passive.TranslatePoint(new Point(passive.ActualWidth / 2, passive.ActualHeight / 2), dockRow);
                    center = vertical ? passiveCenter.Y : passiveCenter.X;
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

        private void UpdateDockWave(FrameworkElement dockRow, double pointerAxis, double elapsed)
        {
            if (waveLayoutDirty) RebuildWaveLayout(dockRow);
            bool vertical = IsVerticalDock;
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
                    double distance = Math.Abs(sample.Center - pointerAxis);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestContainer = sample.Container;
                    }
                    sample.Influence = distance >= radius ? 0 : (Math.Cos(Math.PI * distance / radius) + 1) / 2;
                    double targetScale = 1 + sample.Growth * sample.Influence;
                    sample.Scale += (targetScale - sample.Scale) * scaleBlend;
                    sample.ExtraWidth = (vertical ? sample.Icon.ActualHeight : sample.Icon.ActualWidth) * (sample.Scale - 1);
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
                    if (sample.Indicator != null)
                    {
                        var indicatorTranslate = sample.Indicator.RenderTransform as TranslateTransform;
                        if (indicatorTranslate == null)
                        {
                            indicatorTranslate = new TranslateTransform();
                            sample.Indicator.RenderTransform = indicatorTranslate;
                        }
                        double indicatorOffset = vertical
                            ? (IsDockLeft ? 1 : -1) * sample.Icon.ActualWidth * (sample.Scale - 1) / 2.0
                            : 0;
                        if (Math.Abs(indicatorTranslate.X - indicatorOffset) > .0001)
                            indicatorTranslate.X = indicatorOffset;
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
                double currentOffset = vertical ? translate.Y : translate.X;
                if (Math.Abs(currentOffset - sample.Offset) > .0001)
                {
                    if (vertical) translate.Y = sample.Offset; else translate.X = sample.Offset;
                }
                consumedExtra += sample.ExtraWidth;
                waveSamples[index] = sample;
            }
        }

        private void ResetDockWave()
        {
            bool vertical = IsVerticalDock;
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
                    translate.BeginAnimation(vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty, animation);
                    if (vertical) translate.X = 0; else translate.Y = 0;
                }
                Border indicator = FindRunningIndicator(container);
                var indicatorTranslate = indicator == null ? null : indicator.RenderTransform as TranslateTransform;
                if (indicatorTranslate != null)
                {
                    var indicatorAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(165)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    indicatorTranslate.BeginAnimation(TranslateTransform.XProperty, indicatorAnimation);
                }
                Panel.SetZIndex(container, 0);
            }
            foreach (var passive in wavePassives.Where(item => item.IsLoaded))
            {
                var translate = passive.RenderTransform as TranslateTransform;
                if (translate == null) continue;
                var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(165)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                translate.BeginAnimation(vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty, animation);
                if (vertical) translate.X = 0; else translate.Y = 0;
            }
        }

        private static Border FindRunningIndicator(FrameworkElement container)
        {
            var panel = container as Panel;
            if (panel == null) return null;
            return panel.Children.OfType<Border>().FirstOrDefault(delegate(Border item)
            {
                return String.Equals(item.Tag as string, "RunningIndicator", StringComparison.Ordinal);
            });
        }

        private void PlaceRunningIndicator(StackPanel stack, Border indicator)
        {
            if (stack == null || indicator == null) return;
            if (stack.Children.Contains(indicator)) stack.Children.Remove(indicator);
            indicator.Margin = IsDockTop ? new Thickness(0, 0, 0, 3) : new Thickness(0, 3, 0, 0);
            if (IsDockTop) stack.Children.Insert(0, indicator);
            else stack.Children.Add(indicator);
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

        [DllImport("psapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyWorkingSet(IntPtr process);

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
}
