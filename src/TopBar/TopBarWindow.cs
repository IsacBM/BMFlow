using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace DockBar
{
    internal sealed class TopBarWindow : Window
    {
        private const int BarHeight = 30;
        private const int AppBarNew = 0x00000000;
        private const int AppBarRemove = 0x00000001;
        private const int AppBarQueryPos = 0x00000002;
        private const int AppBarSetPos = 0x00000003;
        private const int AppBarEdgeTop = 1;
        private const int WindowStyleIndex = -20;
        private const int ToolWindowStyle = 0x00000080;
        private const int DwmColorizationColorChanged = 0x0320;

        private readonly DockWindow dock;
        private readonly DispatcherTimer refreshTimer;
        private readonly Border surface;
        private readonly Image activeIcon;
        private readonly TextBlock activeTitle;
        private readonly StackPanel workspaceStrip;
        private readonly TextBlock networkText;
        private readonly TextBlock bluetoothText;
        private readonly TextBlock volumeText;
        private readonly TextBlock batteryText;
        private readonly TextBlock languageText;
        private readonly TextBlock clockText;
        private IntPtr handle;
        private bool appBarRegistered;
        private string lastTheme = String.Empty;
        private string lastActiveTitle = String.Empty;
        private int lastWorkspaceCount = -1;
        private int lastWorkspaceIndex = -1;
        private int refreshTick;
        private HwndSource windowSource;
        private Color systemAccentColor = Color.FromRgb(180, 92, 222);

        internal IntPtr NativeHandle { get { return handle; } }

        internal TopBarWindow(DockWindow dockWindow)
        {
            dock = dockWindow;
            Title = "B.M Flow - Barra superior";
            Height = BarHeight;
            Width = SystemParameters.PrimaryScreenWidth;
            Left = 0;
            Top = 0;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SnapsToDevicePixels = true;

            var root = new Grid { Height = BarHeight };
            surface = new Border
            {
                Height = BarHeight,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 0, 10, 0),
                Child = root
            };
            Content = surface;

            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            left.Children.Add(BuildUserAvatar());
            var user = ItemText(Environment.UserName, 11.5, FontWeights.SemiBold);
            user.Cursor = Cursors.Hand;
            user.ToolTip = "Abrir configurações do B.M Flow";
            user.MouseLeftButtonUp += delegate { dock.OpenSettingsWindow("general"); };
            left.Children.Add(user);
            left.Children.Add(Divider());
            activeIcon = new Image
            {
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            left.Children.Add(activeIcon);
            activeTitle = ItemText("Área de trabalho", 11.5, FontWeights.SemiBold);
            activeTitle.MaxWidth = 430;
            activeTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            left.Children.Add(activeTitle);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            workspaceStrip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(workspaceStrip, 1);
            root.Children.Add(workspaceStrip);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            languageText = ItemText(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToUpperInvariant(), 10.5, FontWeights.SemiBold);
            languageText.Opacity = .88;
            languageText.ToolTip = "Idioma atual";
            right.Children.Add(languageText);
            right.Children.Add(Divider());
            networkText = GlyphText("\uE701", "Rede");
            networkText.MouseLeftButtonUp += delegate { OpenSystemPanel(0x41); };
            right.Children.Add(networkText);
            bluetoothText = GlyphText("\uE702", "Bluetooth");
            bluetoothText.MouseLeftButtonUp += delegate { OpenSystemPanel(0x41); };
            right.Children.Add(bluetoothText);
            volumeText = GlyphText("\uE995", "Volume — abrir ajustes de áudio");
            volumeText.MouseLeftButtonUp += delegate { OpenSystemPanel(0x41); };
            right.Children.Add(volumeText);
            batteryText = GlyphText("\uEBA0", "Bateria");
            batteryText.MouseLeftButtonUp += delegate { OpenSystemPanel(0x41); };
            right.Children.Add(batteryText);
            clockText = ItemText(String.Empty, 11, FontWeights.SemiBold);
            clockText.Margin = new Thickness(8, 0, 0, 0);
            clockText.Cursor = Cursors.Hand;
            clockText.MouseLeftButtonUp += delegate { OpenSystemPanel(0x4E); };
            right.Children.Add(clockText);
            Grid.SetColumn(right, 2);
            root.Children.Add(right);

            ContextMenu = BuildContextMenu();
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate
            {
                ApplyTheme();
                RefreshAll();
                RegisterAppBar();
            };
            Closed += delegate
            {
                refreshTimer.Stop();
                if (windowSource != null) windowSource.RemoveHook(WindowMessageHook);
                RemoveAppBar();
            };
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Closed += delegate { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; };

            refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            refreshTimer.Tick += delegate { RefreshAll(); };
            refreshTimer.Start();
        }

        internal void RefreshWorkspaces()
        {
            lastWorkspaceCount = -1;
            RefreshWorkspaceStrip();
        }

        internal void RefreshTheme()
        {
            lastTheme = String.Empty;
            ApplyTheme();
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            var settings = new MenuItem { Header = "Configurações da barra superior" };
            settings.Click += delegate { dock.OpenSettingsWindow("size"); };
            menu.Items.Add(settings);
            var hide = new MenuItem { Header = "Ocultar barra superior" };
            hide.Click += delegate { dock.SetTopBarEnabled(false); };
            menu.Items.Add(hide);
            return menu;
        }

        private void RefreshAll()
        {
            if (!IsVisible) return;
            ApplyTheme();
            RefreshActiveWindow();
            RefreshWorkspaceStrip();
            if (refreshTick++ % 5 == 0) RefreshSystemState();
            DateTime now = DateTime.Now;
            clockText.Text = now.ToString("ddd d MMM  HH:mm", new CultureInfo("pt-BR")).Replace(".", String.Empty);
            clockText.ToolTip = now.ToString("dddd, d 'de' MMMM 'de' yyyy, HH:mm", new CultureInfo("pt-BR"));
        }

        private void RefreshActiveWindow()
        {
            TopBarActiveWindow state = dock.ReadTopBarActiveWindow();
            if (state == null) return;
            string title = String.IsNullOrWhiteSpace(state.Title) ? "Área de trabalho" : state.Title;
            if (title == lastActiveTitle && ReferenceEquals(activeIcon.Source, state == null ? null : state.Icon)) return;
            lastActiveTitle = title;
            activeTitle.Text = title;
            activeTitle.ToolTip = title;
            activeIcon.Source = state == null ? null : state.Icon;
            activeIcon.Visibility = activeIcon.Source == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RefreshWorkspaceStrip()
        {
            IList<TopBarWorkspace> workspaces = dock.ReadTopBarWorkspaces();
            int active = dock.TopBarActiveWorkspaceIndex;
            if (workspaces.Count == lastWorkspaceCount && active == lastWorkspaceIndex) return;
            lastWorkspaceCount = workspaces.Count;
            lastWorkspaceIndex = active;
            workspaceStrip.Children.Clear();
            for (int index = 0; index < workspaces.Count; index++)
            {
                int selectedIndex = index;
                TopBarWorkspace workspace = workspaces[index];
                bool selected = index == active;
                var dot = new Border
                {
                    Width = selected ? 21 : 7,
                    Height = 7,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(4, 0, 4, 0),
                    Background = selected
                        ? new SolidColorBrush(systemAccentColor)
                        : new SolidColorBrush(Color.FromArgb(190, 235, 235, 240)),
                    Cursor = Cursors.Hand,
                    ToolTip = workspace.Name
                };
                dot.MouseLeftButtonUp += delegate { dock.SwitchToWorkspace(selectedIndex); };
                workspaceStrip.Children.Add(dot);
            }
        }

        private void RefreshSystemState()
        {
            TopBarSystemState state = dock.ReadTopBarSystemState();
            networkText.Text = state.NetworkGlyph;
            networkText.ToolTip = state.NetworkLabel;
            bluetoothText.ToolTip = state.BluetoothLabel;
            bluetoothText.Opacity = state.BluetoothAvailable ? (state.BluetoothEnabled ? 1 : .48) : .28;
            bluetoothText.Foreground = state.BluetoothEnabled
                ? new SolidColorBrush(Color.FromRgb(76, 166, 255))
                : (lastTheme == "Nativo" || lastTheme == "Papel")
                    ? new SolidColorBrush(Color.FromRgb(31, 32, 38))
                    : Brushes.White;
            volumeText.Text = state.VolumeGlyph;
            volumeText.ToolTip = state.VolumeLabel;
            batteryText.Text = state.BatteryGlyph;
            batteryText.ToolTip = state.BatteryLabel;
            batteryText.Foreground = state.BatteryCritical
                ? new SolidColorBrush(Color.FromRgb(255, 91, 91))
                : state.BatteryLow
                    ? new SolidColorBrush(Color.FromRgb(255, 191, 84))
                    : state.BatteryCharging
                        ? new SolidColorBrush(Color.FromRgb(110, 224, 137))
                        : Brushes.White;
        }

        private void ApplyTheme()
        {
            string theme = dock.CurrentTopBarTheme;
            if (theme == lastTheme) return;
            lastTheme = theme;
            bool light = theme == "Nativo" || theme == "Papel";
            surface.Background = light
                ? new SolidColorBrush(Color.FromArgb(246, 239, 239, 241))
                : new SolidColorBrush(Color.FromArgb(244, 18, 19, 24));
            surface.BorderBrush = light
                ? new SolidColorBrush(Color.FromArgb(90, 40, 42, 48))
                : new SolidColorBrush(Color.FromArgb(75, 255, 255, 255));
            Brush foreground = light ? new SolidColorBrush(Color.FromRgb(31, 32, 38)) : Brushes.White;
            SetForegroundRecursive(surface, foreground, batteryText);
        }

        private static void SetForegroundRecursive(DependencyObject parent, Brush brush, TextBlock exception)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                TextBlock text = child as TextBlock;
                if (text != null && text != exception) text.Foreground = brush;
                SetForegroundRecursive(child, brush, exception);
            }
        }

        private static TextBlock ItemText(string text, double size, FontWeight weight)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = size,
                FontWeight = weight,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static FrameworkElement BuildUserAvatar()
        {
            ImageSource picture = LoadUserAccountPicture();
            if (picture != null)
            {
                return new Ellipse
                {
                    Width = 17,
                    Height = 17,
                    Margin = new Thickness(0, 0, 6, 0),
                    Fill = new ImageBrush(picture) { Stretch = Stretch.UniformToFill },
                    Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                    StrokeThickness = 1,
                    ToolTip = "Foto do perfil do Windows"
                };
            }

            var avatar = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(8.5),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                ToolTip = "Foto do perfil do Windows"
            };
            avatar.Child = new TextBlock
            {
                Text = "●",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 198, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            return avatar;
        }

        private static ImageSource LoadUserAccountPicture()
        {
            try
            {
                string sid = WindowsIdentity.GetCurrent().User == null
                    ? null
                    : WindowsIdentity.GetCurrent().User.Value;
                if (!String.IsNullOrWhiteSpace(sid))
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid))
                    {
                        if (key != null)
                        {
                            foreach (string valueName in new[] { "Image448", "Image240", "Image192", "Image96", "Image48", "Image40", "Image32" })
                            {
                                ImageSource registered = LoadPictureFile(key.GetValue(valueName) as string);
                                if (registered != null) return registered;
                            }
                        }
                    }

                    string publicPictures = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Microsoft", "User Account Pictures", sid);
                    ImageSource publicPicture = LoadBestPictureFromDirectory(publicPictures);
                    if (publicPicture != null) return publicPicture;
                }

                foreach (string directory in new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "AccountPictures"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "AccountPictures")
                })
                {
                    ImageSource localPicture = LoadBestPictureFromDirectory(directory);
                    if (localPicture != null) return localPicture;
                }

                string defaults = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Microsoft", "User Account Pictures");
                foreach (string fileName in new[] { "user-192.png", "user-48.png", "user.png" })
                {
                    ImageSource fallback = LoadPictureFile(Path.Combine(defaults, fileName));
                    if (fallback != null) return fallback;
                }
            }
            catch { }
            return null;
        }

        private static ImageSource LoadBestPictureFromDirectory(string directory)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
                string path = Directory.EnumerateFiles(directory)
                    .Where(file => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(file => new FileInfo(file).Length)
                    .FirstOrDefault();
                return LoadPictureFile(path);
            }
            catch { return null; }
        }

        private static ImageSource LoadPictureFile(string path)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 64;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { return null; }
        }

        private static TextBlock GlyphText(string glyph, string tooltip)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13.5,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Width = 25,
                Cursor = Cursors.Hand,
                ToolTip = tooltip
            };
        }

        private static Border Divider()
        {
            return new Border
            {
                Width = 1,
                Height = 14,
                Margin = new Thickness(9, 0, 9, 0),
                Background = new SolidColorBrush(Color.FromArgb(80, 210, 210, 220)),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void OnSourceInitialized(object sender, EventArgs args)
        {
            handle = new WindowInteropHelper(this).Handle;
            int styles = GetWindowLong(handle, WindowStyleIndex);
            SetWindowLong(handle, WindowStyleIndex, styles | ToolWindowStyle);
            windowSource = HwndSource.FromHwnd(handle);
            if (windowSource != null) windowSource.AddHook(WindowMessageHook);
            RefreshSystemAccentColor();
        }

        private IntPtr WindowMessageHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == DwmColorizationColorChanged)
                ApplySystemAccentColor(unchecked((uint)wParam.ToInt64()));
            return IntPtr.Zero;
        }

        private void RefreshSystemAccentColor()
        {
            uint color;
            bool opaque;
            if (DwmGetColorizationColor(out color, out opaque) == 0) ApplySystemAccentColor(color);
        }

        private void ApplySystemAccentColor(uint color)
        {
            Color next = Color.FromRgb(
                (byte)((color >> 16) & 0xFF),
                (byte)((color >> 8) & 0xFF),
                (byte)(color & 0xFF));
            if (next == systemAccentColor) return;
            systemAccentColor = next;
            lastWorkspaceCount = -1;
            RefreshWorkspaceStrip();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs args)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                RemoveAppBar();
                RegisterAppBar();
            }));
        }

        private void RegisterAppBar()
        {
            if (handle == IntPtr.Zero || appBarRegistered) return;
            var data = NewAppBarData();
            SHAppBarMessage(AppBarNew, ref data);
            appBarRegistered = true;
            PositionAppBar();
        }

        private void PositionAppBar()
        {
            if (!appBarRegistered) return;
            Forms.Screen screen = Forms.Screen.PrimaryScreen;
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix toDevice = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            int barHeightPixels = Math.Max(1, (int)Math.Round(BarHeight * toDevice.M22));
            var data = NewAppBarData();
            data.Edge = AppBarEdgeTop;
            data.Rectangle = new AppBarRectangle
            {
                Left = screen.Bounds.Left,
                Top = screen.Bounds.Top,
                Right = screen.Bounds.Right,
                Bottom = screen.Bounds.Top + barHeightPixels
            };
            SHAppBarMessage(AppBarQueryPos, ref data);
            data.Rectangle.Bottom = data.Rectangle.Top + barHeightPixels;
            SHAppBarMessage(AppBarSetPos, ref data);

            Matrix fromDevice = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            Point topLeft = fromDevice.Transform(new Point(data.Rectangle.Left, data.Rectangle.Top));
            Point bottomRight = fromDevice.Transform(new Point(data.Rectangle.Right, data.Rectangle.Top + barHeightPixels));
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = Math.Max(1, bottomRight.X - topLeft.X);
            Height = Math.Max(1, bottomRight.Y - topLeft.Y);
        }

        private void RemoveAppBar()
        {
            if (!appBarRegistered || handle == IntPtr.Zero) return;
            var data = NewAppBarData();
            SHAppBarMessage(AppBarRemove, ref data);
            appBarRegistered = false;
        }

        private AppBarData NewAppBarData()
        {
            return new AppBarData { Size = Marshal.SizeOf(typeof(AppBarData)), Window = handle };
        }

        private static void OpenSystemPanel(byte key)
        {
            keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 2, UIntPtr.Zero);
            keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public int Size;
            public IntPtr Window;
            public uint CallbackMessage;
            public uint Edge;
            public AppBarRectangle Rectangle;
            public IntPtr Parameter;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr window, int index, int style);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);
    }

    internal sealed class TopBarActiveWindow
    {
        public string Title;
        public ImageSource Icon;
    }

    internal sealed class TopBarWorkspace
    {
        public string Name;
    }

    internal sealed class TopBarSystemState
    {
        public string NetworkGlyph;
        public string NetworkLabel;
        public bool BluetoothAvailable;
        public bool BluetoothEnabled;
        public string BluetoothLabel;
        public string VolumeGlyph;
        public string VolumeLabel;
        public string BatteryGlyph;
        public string BatteryLabel;
        public bool BatteryCritical;
        public bool BatteryLow;
        public bool BatteryCharging;
    }
}
