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
            string downloadsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            row.Children.Add(ImageTile(IconForShellPath(downloadsPath), "Downloads", delegate { Launch("explorer.exe", downloadsPath); }));
            row.Children.Add(RecycleBinTile());
            row.Children.Add(Separator());
            systemCardHolder = SystemCard();
            row.Children.Add(systemCardHolder);
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
                if (values.TryGetValue("DockPosition", out text) && IsValidDockPosition(text))
                    dockPosition = text;
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
                if (values.TryGetValue("MascotMode", out text) && !String.IsNullOrWhiteSpace(text)) mascotMode = (text == "Sleep") ? "PatrolRight" : text;
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
                    "DockPosition=" + dockPosition,
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
            if (show && !IsMascotPositionSupported) show = false;
            showMascot = show;
            if (dockMascot != null) dockMascot.SetEnabled(show);
            if (mascotMenuItem != null)
            {
                mascotMenuItem.IsChecked = show;
                mascotMenuItem.IsEnabled = IsMascotPositionSupported;
            }
            SaveSettings();
        }

        private void EnforceMascotPositionPolicy()
        {
            bool supported = IsMascotPositionSupported;
            bool disabledNow = !supported && showMascot;
            if (disabledNow) showMascot = false;
            if (dockMascot != null) dockMascot.SetEnabled(showMascot && supported);
            if (mascotMenuItem != null)
            {
                mascotMenuItem.IsChecked = showMascot && supported;
                mascotMenuItem.IsEnabled = supported;
                mascotMenuItem.ToolTip = supported ? null : "O mascote está disponível somente com a Dock na borda inferior.";
            }
            if (disabledNow) SaveSettings();
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
            else SetMascotMode("PatrolRight");
        }

        internal void MascotTakeNap()
        {
            if (dockMascot != null) dockMascot.TakeNap();
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
                ScheduleMemoryTrim();
            };
            settingsWindow.Show();
        }

        internal void ScheduleMemoryTrim(int delayMilliseconds = 900)
        {
            var previous = Interlocked.Exchange(ref memoryTrimTimer, null);
            if (previous != null) previous.Dispose();
            memoryTrimTimer = new System.Threading.Timer(delegate
            {
                try
                {
                    if (isClosing) return;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                    using (Process process = Process.GetCurrentProcess())
                        EmptyWorkingSet(process.Handle);
                }
                catch { }
                finally
                {
                    var completed = Interlocked.Exchange(ref memoryTrimTimer, null);
                    if (completed != null) completed.Dispose();
                }
            }, null, Math.Max(250, delayMilliseconds), Timeout.Infinite);
        }

        private void PositionSettingsWindowAboveDock(Window window)
        {
            if (window == null) return;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            Rect area = SystemParameters.WorkArea;
            if (IsDockTop)
            {
                window.Left = area.Left + Math.Max(8, (area.Width - window.Width) / 2);
                window.Top = Math.Min(area.Bottom - window.Height - 8, Top + ActualHeight + 10);
            }
            else if (IsDockLeft)
            {
                window.Left = Math.Min(area.Right - window.Width - 8, Left + ActualWidth + 10);
                window.Top = area.Top + Math.Max(8, (area.Height - window.Height) / 2);
            }
            else if (IsDockRight)
            {
                window.Left = Math.Max(area.Left + 8, Left - window.Width - 10);
                window.Top = area.Top + Math.Max(8, (area.Height - window.Height) / 2);
            }
            else
            {
                double dockTop = RevealedDockTop();
                double availableHeight = Math.Max(0, dockTop - area.Top - 18);
                if (availableHeight < window.Height && availableHeight >= window.MinHeight)
                    window.Height = availableHeight;
                window.Left = area.Left + Math.Max(8, (area.Width - window.Width) / 2);
                window.Top = Math.Max(area.Top + 8, dockTop - window.Height - 10);
            }
        }

        private void ApplyWindows11MenuStyle(ContextMenu menu)
        {
            if (menu == null) return;
            bool isDark = darkMode;
            Color menuBgColor = isDark ? Color.FromArgb(238, 35, 36, 41) : Color.FromArgb(238, 245, 248, 253);
            Color menuBorderColor = isDark ? Color.FromArgb(145, 91, 94, 104) : Color.FromArgb(70, 50, 78, 118);
            Brush textBrush = isDark ? new SolidColorBrush(Color.FromRgb(230, 236, 248)) : new SolidColorBrush(Color.FromRgb(24, 35, 52));
            Brush secondaryTextBrush = isDark ? new SolidColorBrush(Color.FromRgb(125, 137, 160)) : new SolidColorBrush(Color.FromRgb(112, 121, 137));
            Brush hoverBrush = isDark ? new SolidColorBrush(Color.FromArgb(210, 25, 58, 104)) : new SolidColorBrush(Color.FromArgb(48, 64, 139, 230));
            Brush separatorBrush = isDark ? new SolidColorBrush(Color.FromArgb(105, 112, 132, 165)) : new SolidColorBrush(Color.FromArgb(65, 76, 97, 126));

            menu.Background = new SolidColorBrush(menuBgColor);
            menu.Foreground = textBrush;
            menu.BorderBrush = new SolidColorBrush(menuBorderColor);
            menu.BorderThickness = new Thickness(1);
            menu.Padding = new Thickness(8);
            menu.FontFamily = new FontFamily("Segoe UI");
            menu.FontSize = 13.5;
            menu.SnapsToDevicePixels = true;
            menu.HasDropShadow = true;

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 7,
                Direction = 270,
                Opacity = 0.58,
                Color = Color.FromRgb(0, 0, 0)
            });
            border.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            menu.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };

            var itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 38.0));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1, 1.5, 1, 1.5)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new EventSetter(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnFluentSubmenuOpened)));

            var itemRoot = new FrameworkElementFactory(typeof(Border));
            itemRoot.Name = "ItemBorder";
            itemRoot.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            itemRoot.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
            itemRoot.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));
            itemRoot.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var itemPanel = new FrameworkElementFactory(typeof(DockPanel));
            itemPanel.SetValue(DockPanel.LastChildFillProperty, true);

            var leadingSlot = new FrameworkElementFactory(typeof(Grid));
            leadingSlot.SetValue(FrameworkElement.WidthProperty, 30.0);
            leadingSlot.SetValue(FrameworkElement.HeightProperty, 22.0);
            leadingSlot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            leadingSlot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            leadingSlot.SetValue(DockPanel.DockProperty, Dock.Left);

            var iconPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            iconPresenter.Name = "IconPresenter";
            iconPresenter.SetValue(ContentPresenter.ContentSourceProperty, "Icon");
            iconPresenter.SetValue(FrameworkElement.WidthProperty, 20.0);
            iconPresenter.SetValue(FrameworkElement.HeightProperty, 20.0);
            iconPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            iconPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            leadingSlot.AppendChild(iconPresenter);

            var checkBox = new FrameworkElementFactory(typeof(Border));
            checkBox.Name = "CheckBoxVisual";
            checkBox.SetValue(FrameworkElement.WidthProperty, 18.0);
            checkBox.SetValue(FrameworkElement.HeightProperty, 18.0);
            checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBox.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            checkBox.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(90, 72, 84, 108)));
            checkBox.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(120, 132, 149, 180)));
            checkBox.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            checkBox.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

            var check = new FrameworkElementFactory(typeof(TextBlock));
            check.Name = "CheckMark";
            check.SetValue(TextBlock.TextProperty, "✓");
            check.SetValue(TextBlock.FontSizeProperty, 12.0);
            check.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            check.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBox.AppendChild(check);
            leadingSlot.AppendChild(checkBox);
            itemPanel.AppendChild(leadingSlot);

            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.Name = "SubmenuArrow";
            arrow.SetValue(TextBlock.TextProperty, "\uE76C");
            arrow.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
            arrow.SetValue(TextBlock.FontSizeProperty, 10.0);
            arrow.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(215, 226, 244)));
            arrow.SetValue(FrameworkElement.WidthProperty, 18.0);
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
            popup.SetValue(Popup.HorizontalOffsetProperty, 5.0);
            popup.SetValue(Popup.VerticalOffsetProperty, -8.0);
            popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsSubmenuOpen") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent, Mode = System.Windows.Data.BindingMode.TwoWay });
            popup.SetBinding(Popup.PlacementTargetProperty, new System.Windows.Data.Binding { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var submenuBorder = new FrameworkElementFactory(typeof(Border));
            submenuBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(menuBgColor));
            submenuBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(menuBorderColor));
            submenuBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            submenuBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            submenuBorder.SetValue(Border.PaddingProperty, new Thickness(8));
            submenuBorder.SetValue(FrameworkElement.MinWidthProperty, 225.0);
            submenuBorder.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 7,
                Direction = 270,
                Opacity = 0.58,
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

            var checkableTrigger = new Trigger { Property = MenuItem.IsCheckableProperty, Value = true };
            checkableTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckBoxVisual"));
            checkableTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "IconPresenter"));
            itemTemplate.Triggers.Add(checkableTrigger);

            var checkedTrigger = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(42, 139, 255)), "CheckBoxVisual"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(80, 169, 255)), "CheckBoxVisual"));
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
            separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(12, 6, 12, 6)));
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

                int tint = isDark ? unchecked((int)0xD02B2927) : unchecked((int)0xD8F8F5F0);
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
                if (IsDockLeft)
                    return new[] { new CustomPopupPlacement(new Point(targetSize.Width + 10, (targetSize.Height - popupSize.Height) / 2.0), PopupPrimaryAxis.Vertical) };
                if (IsDockRight)
                    return new[] { new CustomPopupPlacement(new Point(-popupSize.Width - 10, (targetSize.Height - popupSize.Height) / 2.0), PopupPrimaryAxis.Vertical) };
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
                double top = IsDockTop ? targetSize.Height + 10 : -popupSize.Height - 10;
                return new[] { new CustomPopupPlacement(new Point(left, top), PopupPrimaryAxis.Horizontal) };
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

        internal void ConfigureMascotContextMenu(ContextMenu menu, FrameworkElement target)
        {
            ConfigureDockContextMenu(menu, target);
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
                int centerY = cursor.Y;
                var target = menu.PlacementTarget as FrameworkElement;
                if (target != null && target != dockShell && target.IsLoaded && target.ActualWidth > 0 && target.ActualHeight > 0)
                {
                    try
                    {
                        Point targetScreen = target.PointToScreen(new Point(target.ActualWidth / 2.0, target.ActualHeight / 2.0));
                        centerX = (int)Math.Round(targetScreen.X);
                        centerY = (int)Math.Round(targetScreen.Y);
                    }
                    catch { }
                }

                Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point(cursor.X, cursor.Y));
                int left;
                int top;
                if (IsDockLeft)
                {
                    left = (int)Math.Round(dockTop.X + dockShell.ActualWidth + 10);
                    top = centerY - height / 2;
                }
                else if (IsDockRight)
                {
                    left = (int)Math.Round(dockTop.X) - width - 10;
                    top = centerY - height / 2;
                }
                else
                {
                    left = centerX - width / 2;
                    top = IsDockTop
                        ? (int)Math.Round(dockTop.Y + dockShell.ActualHeight + 10)
                        : (int)Math.Round(dockTop.Y) - height - 10;
                }
                left = Math.Max(screen.Bounds.Left + 6, Math.Min(left, screen.Bounds.Right - width - 6));
                top = Math.Max(screen.Bounds.Top + 6, Math.Min(top, screen.Bounds.Bottom - height - 6));
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
                else if (header == "Exibir mascote na barra")
                {
                    item.IsChecked = showMascot && IsMascotPositionSupported;
                    item.IsEnabled = IsMascotPositionSupported;
                }
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

        private void RefreshState()
        {
            var now = DateTime.Now;
            if (dayText != null) dayText.Text = now.ToString("ddd", new CultureInfo("pt-BR")).ToUpperInvariant().Replace(".", "");
            if (dateText != null) dateText.Text = now.Day.ToString(CultureInfo.InvariantCulture);
            RefreshClock(now);
            RefreshTaskItems();
            if (DateTime.UtcNow >= nextSystemInfoRefreshUtc)
            {
                nextSystemInfoRefreshUtc = DateTime.UtcNow.AddSeconds(5);
                RefreshSystemInfoAsync();
            }
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
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            SetToolTipText(trayArrowText, "Ícones ocultos");

            if (snapshot.RecycleFull.HasValue)
            {
                if (snapshot.RecycleSource != null) recycleBinImage.Source = snapshot.RecycleSource;
                recycleBinFull = snapshot.RecycleFull.Value;
            }
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
            nextTaskRefreshUtc = now.AddMilliseconds(dockAutoHidden ? 8000 : 2500);
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
            ReleaseTaskItemVisuals();
            taskItems.Children.Clear();
            foreach (var app in apps) taskItems.Children.Add(TaskTile(app));
            waveLayoutDirty = true;
        }

        private void ReleaseTaskItemVisuals()
        {
            var released = new HashSet<FrameworkElement>();
            foreach (FrameworkElement item in taskItems.Children.OfType<FrameworkElement>())
            {
                released.Add(item);
                var panel = item as Panel;
                if (panel == null) continue;
                foreach (FrameworkElement child in panel.Children.OfType<FrameworkElement>())
                    released.Add(child);
            }
            if (released.Count == 0) return;

            magnifyIcons.RemoveAll(item => released.Contains(item));
            foreach (FrameworkElement item in released) magnifyGrowth.Remove(item);
            wavePassives.RemoveAll(item => released.Contains(item));
            themedTiles.RemoveAll(item => released.Contains(item));
            themedText.RemoveAll(item => released.Contains(item));
            themedSeparators.RemoveAll(item => released.Contains(item));
            waveSamples.RemoveAll(sample =>
                (sample.Icon != null && released.Contains(sample.Icon)) ||
                (sample.Container != null && released.Contains(sample.Container)));
            if (waveTopContainer != null && released.Contains(waveTopContainer)) waveTopContainer = null;
        }

        private List<TaskApp> ReadTaskApps()
        {
            var apps = new List<TaskApp>();

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

            List<TaskApp> runningPackagedApps = null;
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
                    string processName = process.ProcessName;
                    string path = String.Empty;
                    try { path = process.MainModule == null ? String.Empty : process.MainModule.FileName; }
                    catch { }
                    string windowAppId = GetWindowAppUserModelId(window);
                    string windowClass = GetWindowClassName(window);
                    bool isWindowsTerminal = String.Equals(processName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
                                             (String.Equals(processName, "OpenConsole", StringComparison.OrdinalIgnoreCase) &&
                                              (windowClass.IndexOf("CASCADIA", StringComparison.OrdinalIgnoreCase) >= 0 || IsWindowsTerminalIdentity(path))) ||
                                             windowClass.IndexOf("CASCADIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             IsWindowsTerminalIdentity(windowAppId);
                    if (isWindowsTerminal)
                    {
                        path = ResolveWindowsTerminalTarget(path);
                        if (String.IsNullOrWhiteSpace(windowAppId)) windowAppId = WindowsTerminalAppId;
                    }
                    bool isExplorerWindow = String.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                                            SameTarget(path, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
                    bool isSteamWindow = String.Equals(processName, "steamwebhelper", StringComparison.OrdinalIgnoreCase) ||
                                         String.Equals(processName, "steam", StringComparison.OrdinalIgnoreCase);
                    string runningIdentity = BuildRunningIdentity(windowAppId, processName, processId);
                    var match = apps.FirstOrDefault(a => RunningWindowMatchesApp(a, isWindowsTerminal, isExplorerWindow,
                        isSteamWindow, windowAppId, runningIdentity, path, processName));
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
                    else
                    {
                        bool alreadyInApps = apps.Any(a => RunningWindowMatchesApp(a, isWindowsTerminal, isExplorerWindow,
                            isSteamWindow, windowAppId, runningIdentity, path, processName));

                        if (!alreadyInApps)
                        {
                            if (activeWs != null && activeWs.IsolateWorkspace) continue;
                            TaskApp packagedApp = null;
                            if (!String.IsNullOrWhiteSpace(windowAppId))
                            {
                                if (runningPackagedApps == null) runningPackagedApps = ReadPackagedApps();
                                packagedApp = runningPackagedApps.FirstOrDefault(item =>
                                    String.Equals(item.AppUserModelId, windowAppId, StringComparison.OrdinalIgnoreCase) ||
                                    String.Equals(item.Identity, windowAppId, StringComparison.OrdinalIgnoreCase));
                            }
                            string description = String.Empty;
                            try { description = process.MainModule.FileVersionInfo.FileDescription; } catch { }
                            string displayName = isWindowsTerminal ? "Terminal"
                                : packagedApp != null && !String.IsNullOrWhiteSpace(packagedApp.Name) ? packagedApp.Name
                                : String.IsNullOrWhiteSpace(description) ? processName : description;
                            string launchArguments = String.Empty;
                            string resolvedIdentity = !String.IsNullOrWhiteSpace(runningIdentity) ? runningIdentity : windowAppId;
                            if (isWindowsTerminal)
                            {
                                windowAppId = WindowsTerminalAppId;
                                resolvedIdentity = WindowsTerminalAppId;
                            }
                            else if (isSteamWindow)
                            {
                                displayName = "Steam";
                                string steamExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steam.exe");
                                if (!File.Exists(steamExe)) steamExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Steam\steam.exe");
                                if (File.Exists(steamExe)) path = steamExe;
                            }
                            else if (packagedApp != null)
                            {
                                if (String.IsNullOrWhiteSpace(path) || IsSharedRuntimeProcess(processName)) path = packagedApp.Target;
                                launchArguments = packagedApp.Arguments;
                                if (!String.IsNullOrWhiteSpace(packagedApp.Identity)) resolvedIdentity = packagedApp.Identity;
                            }
                            else if (!String.IsNullOrWhiteSpace(windowTitle)) displayName = windowTitle;

                            ImageSource runningIcon = !String.IsNullOrWhiteSpace(windowAppId)
                                ? (IconForShellItem(windowAppId) ?? IconFor(path))
                                : IconFor(path);
                            var runningApp = new TaskApp
                            {
                                Name = displayName,
                                Target = path,
                                Arguments = launchArguments,
                                AppUserModelId = windowAppId,
                                Identity = resolvedIdentity,
                                WindowHandle = window,
                                Icon = runningIcon
                            };
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
                bool isCascadiaWindow = GetWindowClassName(window).IndexOf("CASCADIA", StringComparison.OrdinalIgnoreCase) >= 0;
                if (IsTaskbarWindow(window) &&
                    (!String.IsNullOrWhiteSpace(GetWindowTitle(window)) || IsExplicitTaskbarWindow(window) || isCascadiaWindow))
                    windows.Add(window);
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

            string windowClass = GetWindowClassName(window);
            bool isCascadiaWindow = windowClass.IndexOf("CASCADIA", StringComparison.OrdinalIgnoreCase) >= 0;

            int cloaked = 0;
            int size = Marshal.SizeOf(typeof(int));
            if (DwmGetWindowAttribute(window, 14, out cloaked, size) == 0 && cloaked != 0) return false;

            long extendedStyle = GetWindowLong(window, -20);
            const long ToolWindow = 0x00000080L;
            const long AppWindow = 0x00040000L;
            bool explicitlyOnTaskbar = (extendedStyle & AppWindow) != 0;
            if ((extendedStyle & ToolWindow) != 0 && !explicitlyOnTaskbar && !isCascadiaWindow) return false;
            if (GetWindow(window, 4) != IntPtr.Zero && !explicitlyOnTaskbar && !isCascadiaWindow) return false;

            // Janelas filhas de outras janelas não aparecem na barra
            if (GetAncestor(window, 2) != window && !explicitlyOnTaskbar && !isCascadiaWindow) return false;

            string cls = windowClass;
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

        private static string GetWindowClassName(IntPtr window)
        {
            if (window == IntPtr.Zero) return String.Empty;
            var className = new StringBuilder(160);
            return GetClassName(window, className, className.Capacity) > 0 ? className.ToString() : String.Empty;
        }

        private static bool IsWindowsTerminalIdentity(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            return value.IndexOf("WindowsTerminal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   String.Equals(System.IO.Path.GetFileNameWithoutExtension(value), "wt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsTerminalApp(TaskApp app)
        {
            if (app == null) return false;
            return IsWindowsTerminalIdentity(app.AppUserModelId) ||
                   IsWindowsTerminalIdentity(app.Identity) ||
                   IsWindowsTerminalIdentity(app.Target) ||
                   (!String.IsNullOrWhiteSpace(app.Name) && app.Name.IndexOf("Terminal", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsSharedRuntimeProcess(string processName)
        {
            if (String.IsNullOrWhiteSpace(processName)) return false;
            switch (processName.ToLowerInvariant())
            {
                case "applicationframehost":
                case "runtimebroker":
                case "dllhost":
                case "rundll32":
                case "java":
                case "javaw":
                case "python":
                case "pythonw":
                case "node":
                case "electron":
                case "openconsole":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBrowserPwaIdentity(string appUserModelId)
        {
            if (String.IsNullOrWhiteSpace(appUserModelId)) return false;
            string value = appUserModelId.ToLowerInvariant();
            return value.Contains("_crx_") || value.Contains(".pwa.") || value.StartsWith("pwa:", StringComparison.Ordinal);
        }

        private static string BuildRunningIdentity(string appUserModelId, string processName, uint processId)
        {
            if (!String.IsNullOrWhiteSpace(appUserModelId)) return "aumid:" + appUserModelId.ToLowerInvariant();
            return "process:" + (processName ?? "application").ToLowerInvariant() + ":" + processId.ToString(CultureInfo.InvariantCulture);
        }

        private static bool RunningWindowMatchesApp(TaskApp app, bool isWindowsTerminal, bool isExplorerWindow,
            bool isSteamWindow, string windowAppId, string runningIdentity, string path, string processName)
        {
            if (app == null) return false;
            if (isWindowsTerminal) return IsWindowsTerminalApp(app);
            if (isExplorerWindow)
            {
                return String.Equals(app.Name, "Explorador de Arquivos", StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(app.Name, "File Explorer", StringComparison.OrdinalIgnoreCase) ||
                       (SameTarget(app.Target, path) && String.IsNullOrWhiteSpace(app.Arguments) && String.IsNullOrWhiteSpace(app.AppUserModelId));
            }
            if (isSteamWindow)
            {
                return String.Equals(app.Name, "Steam", StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(System.IO.Path.GetFileNameWithoutExtension(app.Target), "steam", StringComparison.OrdinalIgnoreCase) ||
                       (!String.IsNullOrWhiteSpace(app.Target) && app.Target.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!String.IsNullOrWhiteSpace(windowAppId))
            {
                if (String.Equals(app.AppUserModelId, windowAppId, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(app.Identity, windowAppId, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(app.Identity, runningIdentity, StringComparison.OrdinalIgnoreCase))
                    return true;

                // PWAs possuem o mesmo executável do navegador, mas são itens
                // independentes na barra do Windows.
                if (IsBrowserPwaIdentity(windowAppId)) return false;
            }

            if (!String.IsNullOrWhiteSpace(runningIdentity) &&
                String.Equals(app.Identity, runningIdentity, StringComparison.OrdinalIgnoreCase)) return true;

            bool usesExplorerLauncher = !String.IsNullOrWhiteSpace(app.Arguments) &&
                                        app.Arguments.IndexOf("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) >= 0;
            bool sharedRuntime = IsSharedRuntimeProcess(processName);
            if (!sharedRuntime && !usesExplorerLauncher && !String.IsNullOrWhiteSpace(path) && SameTarget(app.Target, path)) return true;
            if (!sharedRuntime && !usesExplorerLauncher &&
                String.Equals(System.IO.Path.GetFileNameWithoutExtension(app.Target), processName, StringComparison.OrdinalIgnoreCase)) return true;

            bool structuredIdentity = !String.IsNullOrWhiteSpace(app.Identity) &&
                                      (app.Identity.StartsWith("process:", StringComparison.OrdinalIgnoreCase) ||
                                       app.Identity.StartsWith("host:", StringComparison.OrdinalIgnoreCase) ||
                                       app.Identity.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase));
            if (!sharedRuntime && !structuredIdentity && !String.IsNullOrWhiteSpace(app.Identity) &&
                (processName.IndexOf(app.Identity, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 app.Identity.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0)) return true;

            return !sharedRuntime && !String.IsNullOrWhiteSpace(app.Name) &&
                   String.Equals(app.Name, processName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveWindowsTerminalTarget(string currentPath)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(currentPath))
                {
                    string fileName = System.IO.Path.GetFileName(currentPath);
                    if (String.Equals(fileName, "WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)) return currentPath;
                    string sibling = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(currentPath) ?? String.Empty, "WindowsTerminal.exe");
                    if (File.Exists(sibling)) return sibling;
                }
                string alias = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps", "wt.exe");
                if (File.Exists(alias)) return alias;
            }
            catch { }
            return currentPath ?? String.Empty;
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

        private static ImageSource GetCachedIcon(string key)
        {
            lock (iconCacheLock)
            {
                WeakReference reference;
                if (!iconCache.TryGetValue(key, out reference)) return null;
                var source = reference.Target as ImageSource;
                if (source == null) iconCache.Remove(key);
                return source;
            }
        }

        private static void CacheIcon(string key, ImageSource source)
        {
            if (source == null) return;
            lock (iconCacheLock)
            {
                iconCache[key] = new WeakReference(source);
                if (iconCache.Count <= 128) return;
                foreach (string deadKey in iconCache.Where(item => !item.Value.IsAlive).Select(item => item.Key).ToList())
                    iconCache.Remove(deadKey);
                if (iconCache.Count > 192)
                    foreach (string oldKey in iconCache.Keys.Take(iconCache.Count - 160).ToList())
                        iconCache.Remove(oldKey);
            }
        }

        private static ImageSource IconFor(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string cacheKey = "file:" + path;
            ImageSource cached = GetCachedIcon(cacheKey);
            if (cached != null) return cached;
            try
            {
                using (Drawing.Icon icon = Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(40, 40));
                    source.Freeze();
                    CacheIcon(cacheKey, source);
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
            ImageSource cached = GetCachedIcon(cacheKey);
            if (cached != null) return cached;
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
                CacheIcon(cacheKey, source);
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
            Drawing.Rectangle bounds = taskbarHidden ? screen.Bounds : screen.WorkingArea;
            int revealLine = IsDockTop ? bounds.Top
                : IsDockLeft ? bounds.Left
                : IsDockRight ? bounds.Right
                : bounds.Bottom;
            int cursorCoordinate = IsVerticalDock ? cursor.X : cursor.Y;
            if (dockAutoHidden)
            {
                if (cursorCoordinate < revealLine - 3 || cursorCoordinate > revealLine + 3) return;
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
            if (cursorCoordinate < revealLine - 9 || cursorCoordinate > revealLine + 9)
            {
                edgeRevealPending = false;
                edgeRevealTimer.Stop();
                ScheduleAutoHide();
            }
        }

        private double HiddenDockTop()
        {
            return HiddenDockTopForPosition();
        }

        private double RevealedDockTop()
        {
            return RevealedDockTopForPosition();
        }

        private void ShowDockAnimated()
        {
            if (fullscreenSuppressed || !IsLoaded) return;
            edgeRevealTimer.Stop();
            autoHideTimer.Stop();
            double targetLeft = RevealedDockLeft();
            double targetTop = RevealedDockTopForPosition();
            if (!dockAutoHidden && Math.Abs(Left - targetLeft) < .5 && Math.Abs(Top - targetTop) < .5) return;
            // Uma Window quase toda fora da tela pode ser temporariamente
            // desacelerada pelo DWM. Trazemos a janela de volta imediatamente e
            // animamos somente o conteúdo, evitando atraso no primeiro quadro.
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = targetLeft;
            Top = targetTop;
            AnimateDockRevealElement(dockShell, IsDockLeft ? -10 : IsDockRight ? 10 : 0, IsDockTop ? -10 : IsDockBottom ? 10 : 0);
            AnimateDockRevealElement(magnifyHost, IsDockLeft ? -10 : IsDockRight ? 10 : 0, IsDockTop ? -10 : IsDockBottom ? 10 : 0);
            dockAutoHidden = false;
        }

        private static void AnimateDockRevealElement(FrameworkElement element, double fromX, double fromY)
        {
            if (element == null) return;
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
            var animationX = new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(115))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var animationY = new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(115))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            if (Math.Abs(fromX) > .01) translate.BeginAnimation(TranslateTransform.XProperty, animationX);
            if (Math.Abs(fromY) > .01) translate.BeginAnimation(TranslateTransform.YProperty, animationY);
        }

        private void HideDockAnimated()
        {
            if (fullscreenSuppressed || dockAutoHidden || !IsLoaded || IsAnyContextMenuOpen()) return;
            bool vertical = IsVerticalDock;
            double target = vertical ? HiddenDockLeft() : HiddenDockTopForPosition();
            double start = vertical ? Left : Top;
            DependencyProperty positionProperty = vertical ? LeftProperty : TopProperty;
            BeginAnimation(positionProperty, null);
            if (vertical) Left = target; else Top = target;
            var animation = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(220))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            BeginAnimation(positionProperty, animation);
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
            WindowCommands.ExcludeFromApplicationSwitcher(this);
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
            double dpiX = 1;
            double dpiY = 1;
            var presentation = PresentationSource.FromVisual(this);
            if (presentation != null && presentation.CompositionTarget != null)
            {
                dpiX = presentation.CompositionTarget.TransformToDevice.M11;
                dpiY = presentation.CompositionTarget.TransformToDevice.M22;
            }
            double visualThickness = IsVerticalDock
                ? (dockShell == null || dockShell.ActualWidth <= 0 ? 72 : dockShell.ActualWidth)
                : (dockShell == null || dockShell.ActualHeight <= 0 ? 72 : dockShell.ActualHeight);
            double dpi = IsVerticalDock ? dpiX : dpiY;
            int requestedThickness = Math.Max(72, (int)Math.Round((visualThickness + dockBottomGap + 6) * dpi));
            uint edge = IsDockLeft ? 0u : IsDockTop ? 1u : IsDockRight ? 2u : 3u;
            var data = new AppBarData
            {
                Size = Marshal.SizeOf(typeof(AppBarData)),
                Window = dockHandle,
                Edge = edge,
                Rectangle = new NativeRect { Left = screen.Left, Right = screen.Right, Top = screen.Top, Bottom = screen.Bottom }
            };
            SHAppBarMessage(2, ref data);
            if (IsDockLeft) data.Rectangle.Right = data.Rectangle.Left + requestedThickness;
            else if (IsDockRight) data.Rectangle.Left = data.Rectangle.Right - requestedThickness;
            else if (IsDockTop) data.Rectangle.Bottom = data.Rectangle.Top + requestedThickness;
            else data.Rectangle.Top = data.Rectangle.Bottom - requestedThickness;
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
            double dockWidth = ActualWidth;
            double dockHeight = ActualHeight;
            if (dockWidth <= 0 || dockHeight <= 0 || Double.IsNaN(dockWidth) || Double.IsNaN(dockHeight)) return;
            double targetLeft = dockAutoHidden && autoHideEnabled ? HiddenDockLeft() : RevealedDockLeft();
            double targetTop = dockAutoHidden && autoHideEnabled ? HiddenDockTopForPosition() : RevealedDockTopForPosition();
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            Left = targetLeft;
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
            // O Windows só oferece uma âncora inferior nativa para o menu
            // Iniciar. Nas demais bordas, deixe o Shell escolher a posição
            // para não deslocar uma janela do sistema para fora da tela.
            if (!IsDockBottom)
            {
                ReleaseStartMenuHook();
                return;
            }

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
            if (!IsDockBottom) return false;

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
            if (!IsDockBottom) return;

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
                Point anchorPoint = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2));
                Point barPoint = dockShell.PointToScreen(new Point(0, 0));
                Point barBottomRight = dockShell.PointToScreen(new Point(dockShell.ActualWidth, dockShell.ActualHeight));
                var reveal = dockShell.RenderTransform as TranslateTransform;
                if (reveal != null && (Math.Abs(reveal.X) > .01 || Math.Abs(reveal.Y) > .01))
                {
                    double dpiX = 1;
                    double dpiY = 1;
                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                    {
                        dpiX = source.CompositionTarget.TransformToDevice.M11;
                        dpiY = source.CompositionTarget.TransformToDevice.M22;
                    }
                    barPoint.X -= reveal.X * dpiX;
                    barPoint.Y -= reveal.Y * dpiY;
                    barBottomRight.X -= reveal.X * dpiX;
                    barBottomRight.Y -= reveal.Y * dpiY;
                }
                Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point((int)Math.Round(anchorPoint.X), (int)Math.Round(anchorPoint.Y)));
                quickSettingsAnchorX = (int)Math.Round(anchorPoint.X);
                quickSettingsAnchorY = (int)Math.Round(anchorPoint.Y);
                quickSettingsBarLeft = (int)Math.Round(barPoint.X);
                quickSettingsBarTop = (int)Math.Round(barPoint.Y);
                quickSettingsBarRight = (int)Math.Round(barBottomRight.X);
                quickSettingsBarBottom = (int)Math.Round(barBottomRight.Y);
                quickSettingsWorkLeft = screen.WorkingArea.Left;
                quickSettingsWorkRight = screen.WorkingArea.Right;
                quickSettingsWorkTop = screen.WorkingArea.Top;
                quickSettingsWorkBottom = screen.WorkingArea.Bottom;

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
            int left;
            int top;
            if (IsDockLeft)
            {
                left = quickSettingsBarRight + 8;
                top = quickSettingsAnchorY - height / 2;
            }
            else if (IsDockRight)
            {
                left = quickSettingsBarLeft - width - 8;
                top = quickSettingsAnchorY - height / 2;
            }
            else
            {
                left = quickSettingsAnchorX - width / 2;
                top = IsDockTop ? quickSettingsBarBottom + 8 : quickSettingsBarTop - height - 38;
            }
            left = Math.Max(quickSettingsWorkLeft + 8, Math.Min(left, quickSettingsWorkRight - width - 8));
            top = Math.Max(quickSettingsWorkTop + 8, Math.Min(top, quickSettingsWorkBottom - height - 8));
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
                Point arrow = trayArrowText.PointToScreen(new Point(trayArrowText.ActualWidth / 2, trayArrowText.ActualHeight / 2));
                Point bar = dockShell.PointToScreen(new Point(0, 0));
                Point barBottomRight = dockShell.PointToScreen(new Point(dockShell.ActualWidth, dockShell.ActualHeight));
                var screen = Forms.Screen.FromPoint(new Drawing.Point((int)Math.Round(arrow.X), (int)Math.Round(arrow.Y)));
                trayPopupAnchorX = (int)Math.Round(arrow.X);
                trayPopupAnchorY = (int)Math.Round(arrow.Y);
                trayPopupBarLeft = (int)Math.Round(bar.X);
                trayPopupBarTop = (int)Math.Round(bar.Y);
                trayPopupBarRight = (int)Math.Round(barBottomRight.X);
                trayPopupBarBottom = (int)Math.Round(barBottomRight.Y);
                trayPopupWorkLeft = screen.WorkingArea.Left;
                trayPopupWorkRight = screen.WorkingArea.Right;
                trayPopupWorkTop = screen.WorkingArea.Top;
                trayPopupWorkBottom = screen.WorkingArea.Bottom;

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
            int left;
            int top;
            if (IsDockLeft)
            {
                left = trayPopupBarRight + 8;
                top = trayPopupAnchorY - height / 2;
            }
            else if (IsDockRight)
            {
                left = trayPopupBarLeft - width - 8;
                top = trayPopupAnchorY - height / 2;
            }
            else
            {
                left = trayPopupAnchorX - width / 2;
                top = IsDockTop ? trayPopupBarBottom + 8 : trayPopupBarTop - height - 9;
            }
            left = Math.Max(trayPopupWorkLeft + 6, Math.Min(left, trayPopupWorkRight - width - 6));
            top = Math.Max(trayPopupWorkTop + 6, Math.Min(top, trayPopupWorkBottom - height - 6));
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
                    ShowWorkspaceHud(
                        String.IsNullOrWhiteSpace(current.Icon) ? "💼" : current.Icon,
                        current.Name,
                        "Espaço " + (index + 1) + " • Ativo (Ctrl+Alt+" + (index + 1) + ")");
                }
                return;
            }

            workspaceManager.ActiveIndex = index;
            var ws = workspaceManager.ActiveWorkspace;
            if (ws != null)
            {
                ShowWorkspaceHud(
                    String.IsNullOrWhiteSpace(ws.Icon) ? "💼" : ws.Icon,
                    ws.Name,
                    "Espaço " + (index + 1) + " • Ctrl+Alt+" + (index + 1));
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

            ShowWorkspaceHud(
                ws.Icon ?? "🚀",
                "Espaço: " + ws.Name,
                restored > 0 ? (restored + " janela(s) restaurada(s), " + launched + " aberta(s)") : (launched + " aplicativo(s) iniciado(s)"));
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
