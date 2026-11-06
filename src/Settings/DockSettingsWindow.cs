using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
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
    internal sealed class DockSettingsWindow : Window
    {
        private readonly DockWindow dock;
        private readonly Dictionary<string, Border> themeCards = new Dictionary<string, Border>();
        private readonly Dictionary<string, Border> navigationItems = new Dictionary<string, Border>();
        private static readonly ControlTemplate toggleTemplate = CreateToggleTemplate();
        private static readonly ControlTemplate comboBoxTemplate = CreateComboBoxTemplate();
        private static readonly Style comboBoxItemStyle = CreateComboBoxItemStyle();
        private static readonly ControlTemplate sliderTemplate = CreateSliderTemplate();
        private static readonly Style scrollBarStyle = CreateScrollBarStyle();
        private CheckBox keepWindowsCheck;
        private Grid settingsPageHost;
        private List<ImageSource> themePreviewIcons = new List<ImageSource>();
        private static ImageSource themeStartIcon;
        private string currentSettingsPage = "general";
        private int selectedWorkspaceIndex = 0;
        private string workspaceAppCatalogSearch = "";

        private sealed class UpdateCheckResult
        {
            public bool Success;
            public bool UpdateAvailable;
            public string InstalledVersion;
            public string LatestTag;
            public string ReleaseUrl;
            public string ErrorMessage;
        }

        public void ShowSettingsPageExternal(string page)
        {
            ShowSettingsPage(page);
        }

        public DockSettingsWindow(DockWindow ownerDock, string initialPage = "general")
        {
            dock = ownerDock;
            Title = "Configurações da B.M Flow";
            Width = 1080;
            Height = 760;
            MinWidth = 900;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(24, 23, 25));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");
            Topmost = true;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(258) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 27, 29))
            };
            Grid.SetColumnSpan(titleBar, 2);
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                if (args.LeftButton == MouseButtonState.Pressed && args.GetPosition(titleBar).X < titleBar.ActualWidth - 92) DragMove();
            };

            var titleIdentity = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(18, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleIdentity.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(75, 155, 255)),
                Margin = new Thickness(0, 0, 9, 0)
            });
            titleIdentity.Children.Add(new TextBlock
            {
                Text = "B.M Flow",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(228, 225, 226)),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleIdentity.Children.Add(new TextBlock
            {
                Text = "  /  Configurações",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(145, 141, 145)),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleBar.Children.Add(titleIdentity);

            var windowControls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var minimize = WindowControl("\uE921", "Minimizar");
            minimize.MouseEnter += delegate { minimize.Background = new SolidColorBrush(Color.FromRgb(49, 47, 51)); };
            minimize.MouseLeave += delegate { minimize.Background = Brushes.Transparent; };
            minimize.MouseLeftButtonUp += delegate { WindowState = WindowState.Minimized; };
            var close = WindowControl("\uE8BB", "Fechar");
            close.MouseEnter += delegate { close.Background = new SolidColorBrush(Color.FromRgb(196, 43, 38)); };
            close.MouseLeave += delegate { close.Background = Brushes.Transparent; };
            close.MouseLeftButtonUp += delegate { Close(); };
            windowControls.Children.Add(minimize);
            windowControls.Children.Add(close);
            titleBar.Children.Add(windowControls);
            titleBar.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(49, 47, 51)),
                VerticalAlignment = VerticalAlignment.Bottom
            });
            root.Children.Add(titleBar);

            var sidebar = new Grid { Background = new SolidColorBrush(Color.FromRgb(25, 24, 26)), Margin = new Thickness(0) };
            sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(sidebar, 1);

            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 24, 18, 22) };
            brand.Children.Add(new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(13), Background = new SolidColorBrush(Color.FromRgb(75, 155, 255)),
                Effect = new DropShadowEffect { Color = Color.FromRgb(35, 135, 255), BlurRadius = 18, ShadowDepth = 0, Opacity = .32 },
                Child = new TextBlock { Text = "\uE80F", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            var brandText = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            brandText.Children.Add(new TextBlock { Text = "B.M Flow", FontSize = 19, FontWeight = FontWeights.SemiBold });
            brandText.Children.Add(new TextBlock { Text = "Configurações", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(173, 168, 168)) });
            brand.Children.Add(brandText);
            sidebar.Children.Add(brand);

            var search = new TextBox
            {
                Text = "Buscar nos ajustes", Foreground = new SolidColorBrush(Color.FromRgb(155, 150, 150)),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 8, 10, 8), VerticalContentAlignment = VerticalAlignment.Center
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
                else if (query.Contains("mascote") || query.Contains("pet") || query.Contains("gato") || query.Contains("gatinho") || query.Contains("cachorro") || query.Contains("raposa") || query.Contains("pato") || query.Contains("ganso") || query.Contains("comportamento") || query.Contains("runcat")) ShowSettingsPage("mascot");
                else if (query.Contains("widget") || query.Contains("mouse") || query.Contains("cursor") || query.Contains("mac") || query.Contains("personaliz")) ShowSettingsPage("widgets");
                else if (query.Contains("sobre") || query.Contains("versão")) ShowSettingsPage("about");
                else if (query.Length >= 3) ShowSettingsPage("general");
            };
            var searchShell = new Border
            {
                Height = 42,
                Margin = new Thickness(18, 0, 18, 18),
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromRgb(37, 36, 39)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(57, 55, 60)),
                BorderThickness = new Thickness(1)
            };
            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.Children.Add(new TextBlock
            {
                Text = "\uE721", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(151, 147, 153)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(search, 1);
            searchGrid.Children.Add(search);
            searchShell.Child = searchGrid;
            search.GotKeyboardFocus += delegate { searchShell.BorderBrush = new SolidColorBrush(Color.FromRgb(75, 155, 255)); };
            search.LostKeyboardFocus += delegate { searchShell.BorderBrush = new SolidColorBrush(Color.FromRgb(57, 55, 60)); };
            Grid.SetRow(searchShell, 1);
            sidebar.Children.Add(searchShell);

            var navigation = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
            navigation.Children.Add(CreateNavigationItem("general", "\uE713", "Geral"));
            navigation.Children.Add(CreateNavigationItem("workspaces", "\uE8A7", "Espaços"));
            navigation.Children.Add(CreateNavigationItem("appearance", "\uE790", "Aparência"));
            navigation.Children.Add(CreateNavigationItem("themes", "\uE771", "Temas"));
            navigation.Children.Add(CreateNavigationItem("size", "\uE799", "Tamanho e posição"));
            navigation.Children.Add(CreateNavigationItem("mascot", "\uE734", "Mascote"));
            navigation.Children.Add(CreateNavigationItem("widgets", "\uE74C", "Personalização"));
            navigation.Children.Add(CreateNavigationItem("about", "\uE946", "Sobre"));
            Grid.SetRow(navigation, 2);
            sidebar.Children.Add(navigation);
            root.Children.Add(sidebar);

            settingsPageHost = new Grid { Background = new SolidColorBrush(Color.FromRgb(32, 31, 34)) };
            Grid.SetColumn(settingsPageHost, 1);
            Grid.SetRow(settingsPageHost, 1);
            root.Children.Add(settingsPageHost);
            Content = root;
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape) Close();
            };
            StateChanged += delegate
            {
                if (WindowState == WindowState.Minimized)
                {
                    ReleaseCurrentPageVisuals();
                    dock.ScheduleMemoryTrim();
                }
                else if (settingsPageHost != null && settingsPageHost.Children.Count == 0)
                {
                    ShowSettingsPage(currentSettingsPage);
                }
            };
            Closed += delegate
            {
                ReleaseCurrentPageVisuals();
                navigationItems.Clear();
                settingsPageHost = null;
                Content = null;
            };
            ShowSettingsPage(initialPage ?? "general");
        }

        private static Border WindowControl(string glyph, string toolTip)
        {
            return new Border
            {
                Width = 46,
                Height = 49,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = toolTip,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(218, 215, 219)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private Border CreateNavigationItem(string key, string glyph, string label)
        {
            var accent = new Border { Width = 3, Height = 24, CornerRadius = new CornerRadius(2), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(accent);
            var icon = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(201, 198, 204)), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);
            var text = new TextBlock { Text = label, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(232, 230, 234)), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
            var item = new Border
            {
                Height = 48, CornerRadius = new CornerRadius(11), Background = Brushes.Transparent,
                Cursor = Cursors.Hand, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(4, 0, 6, 0), Child = grid, Tag = accent
            };
            item.MouseEnter += delegate { if (item.Background == Brushes.Transparent) item.Background = new SolidColorBrush(Color.FromRgb(39, 38, 42)); };
            item.MouseLeave += delegate { if (((Border)item.Tag).Background == Brushes.Transparent) item.Background = Brushes.Transparent; };
            item.MouseLeftButtonUp += delegate { ShowSettingsPage(key); };
            navigationItems[key] = item;
            return item;
        }

        private void ShowSettingsPage(string page)
        {
            if (settingsPageHost == null) return;
            currentSettingsPage = String.IsNullOrWhiteSpace(page) ? "general" : page;
            if (!String.Equals(currentSettingsPage, "themes", StringComparison.OrdinalIgnoreCase))
            {
                themeCards.Clear();
                themePreviewIcons.Clear();
            }
            foreach (var entry in navigationItems)
            {
                bool active = entry.Key == page;
                entry.Value.Background = active ? new SolidColorBrush(Color.FromRgb(51, 49, 54)) : Brushes.Transparent;
                var accent = entry.Value.Tag as Border;
                if (accent != null) accent.Background = active ? new SolidColorBrush(Color.FromRgb(75, 155, 255)) : Brushes.Transparent;
            }

            var content = new StackPanel { Margin = new Thickness(40, 20, 40, 38) };
            if (page == "themes") BuildThemesPage(content);
            else if (page == "workspaces") BuildWorkspacesPage(content);
            else if (page == "appearance") BuildAppearancePage(content);
            else if (page == "size") BuildSizePage(content);
            else if (page == "mascot") BuildMascotPage(content);
            else if (page == "widgets") BuildWidgetsPage(content);
            else if (page == "about") BuildAboutPage(content);
            else BuildGeneralPage(content);
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
            scroll.Resources[typeof(ScrollBar)] = scrollBarStyle;
            settingsPageHost.Children.Clear();
            settingsPageHost.Children.Add(scroll);
        }

        private void ReleaseCurrentPageVisuals()
        {
            if (settingsPageHost != null) settingsPageHost.Children.Clear();
            themeCards.Clear();
            themePreviewIcons.Clear();
        }

        private static FrameworkElement PageHeader(string glyph, string title, string subtitle)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 26) };
            panel.Children.Add(new Border
            {
                Width = 56, Height = 56, CornerRadius = new CornerRadius(17),
                Background = new SolidColorBrush(Color.FromRgb(43, 54, 70)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(61, 103, 158)), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Opacity = .22 },
                Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 25, Foreground = new SolidColorBrush(Color.FromRgb(106, 175, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
            var text = new StackPanel { Margin = new Thickness(17, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(246, 244, 247)) });
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromRgb(174, 169, 176)), Margin = new Thickness(0, 4, 0, 0) });
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
                    Background = new SolidColorBrush(isSelected ? Color.FromRgb(75, 155, 255) : Color.FromRgb(54, 56, 61)),
                    BorderBrush = new SolidColorBrush(isActive ? Color.FromRgb(40, 205, 65) : (isSelected ? Color.FromRgb(120, 187, 255) : Color.FromRgb(75, 78, 85))),
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
Background = new SolidColorBrush(currentWs.Icon == em ? Color.FromRgb(75, 155, 255) : Color.FromRgb(49, 51, 56)),
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
Background = new SolidColorBrush(alreadyAdded ? Color.FromRgb(40, 50, 45) : Color.FromRgb(75, 155, 255)),
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
            themePreviewIcons = ResolveThemePreviewIcons();
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
            stack.Children.Add(ChoiceControl("Posição na tela", DockWindow.DockPositionChoices, dock.CurrentDockPosition, dock.SetDockPosition));
            stack.Children.Add(Divider());
            stack.Children.Add(SliderControl("Tamanho dos ícones", "px", 40, 64, dock.CurrentIconSize, dock.SetDockIconSize));
            stack.Children.Add(Divider());
            stack.Children.Add(SliderControl("Largura do indicador", "px", 4, 18, dock.CurrentIndicatorWidth, dock.SetRunningIndicatorWidth));
            stack.Children.Add(Divider());
            stack.Children.Add(SliderControl("Espaço da borda", "px", 0, 20, dock.CurrentBottomGap, dock.SetDockBottomGap));
            content.Children.Add(card);
            content.Children.Add(SectionTitle("Organização dos aplicativos", "A ordem personalizada é salva automaticamente"));
            var hint = SettingsCard();
            hint.Child = new TextBlock
            {
                Text = "Clique, segure e arraste um aplicativo diretamente na Dock para reorganizá-lo na direção da barra.",
                TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(220, 216, 214)), FontSize = 13.5
            };
            content.Children.Add(hint);
        }

        private void BuildMascotPage(StackPanel content)
        {
            content.Children.Add(PageHeader("\uE734", "Mascote da Barra", "Escolha seu companheiro, o comportamento e o tamanho na Dock."));
            content.Children.Add(SectionTitle("Exibição", "As alterações são aplicadas e salvas imediatamente"));

            var visibilityCard = SettingsCard();
            var visibilityStack = new StackPanel();
            visibilityCard.Child = visibilityStack;

            var showMascotCheck = Toggle(
                "Exibir mascote na barra",
                dock.IsMascotPositionSupported
                    ? "Mostra o companheiro animado sobre a Dock e permite interagir com ele."
                    : "Disponível somente quando a Dock está posicionada na borda inferior.",
                dock.ShowMascot);
            showMascotCheck.IsEnabled = dock.IsMascotPositionSupported;
            showMascotCheck.Checked += delegate { dock.SetShowMascot(true); };
            showMascotCheck.Unchecked += delegate { dock.SetShowMascot(false); };
            visibilityStack.Children.Add(showMascotCheck);
            content.Children.Add(visibilityCard);

            content.Children.Add(SectionTitle("Aparência e comportamento", "Personalize o mascote sem precisar abrir o menu de contexto"));
            var optionsCard = SettingsCard();
            var optionsStack = new StackPanel();
            optionsCard.Child = optionsStack;

            optionsStack.Children.Add(ChoiceControl(
                "Mascote",
                new[]
                {
                    "Raposinha (Pixel Fox)",
                    "Gatinho Meia-Noite (Preto)",
                    "Gatinho Tigrão (Laranja)",
                    "Gatinho Neve (Branco)",
                    "Gatinho Frajola (Tuxedo)",
                    "Cachorrinho Caramelo",
                    "Gansinho / Pato"
                },
                dock.MascotTypeDisplay,
                dock.SetMascotTypeFromDisplay));

            optionsStack.Children.Add(Divider());
            optionsStack.Children.Add(ChoiceControl(
                "Comportamento",
                new[]
                {
                    "Canto Direito (Acima da Lixeira)",
                    "Passear pela Barra toda",
                    "Ritmo pela CPU (RunCat)"
                },
                dock.MascotModeDisplay,
                dock.SetMascotModeFromDisplay));

            optionsStack.Children.Add(Divider());
            optionsStack.Children.Add(ChoiceControl(
                "Tamanho",
                new[]
                {
                    "80% (Pequeno)",
                    "100% (Padrão)",
                    "125% (Médio)",
                    "150% (Grande)",
                    "180% (Extra Grande)",
                    "220% (Gigante)"
                },
                dock.MascotScaleLabel,
                dock.SetMascotScaleFromLabel));

            content.Children.Add(optionsCard);

            content.Children.Add(SectionTitle("Interação", "Um pequeno companheiro para deixar a área de trabalho mais viva"));
            var interactionCard = SettingsCard();
            var interactionGrid = new Grid();
            interactionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            interactionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            interactionGrid.Children.Add(new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(75, 155, 255)),
                Child = new TextBlock
                {
                    Text = "♥",
                    FontSize = 18,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var interactionText = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            interactionText.Children.Add(new TextBlock
            {
                Text = "Clique para fazer carinho",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            interactionText.Children.Add(new TextBlock
            {
                Text = "Clique no mascote para ver uma reação e use o botão direito para abrir o menu rápido. Ao mover a Dock para a borda superior ou para uma lateral, o mascote é desativado e não volta sozinho.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(185, 180, 178)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(interactionText, 1);
            interactionGrid.Children.Add(interactionText);
            interactionCard.Child = interactionGrid;
            content.Children.Add(interactionCard);
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
                    pair.Value.Background = isSel ? new SolidColorBrush(Color.FromRgb(75, 155, 255)) : new SolidColorBrush(Color.FromRgb(51, 53, 58));
                    pair.Value.BorderBrush = isSel ? new SolidColorBrush(Color.FromRgb(120, 187, 255)) : new SolidColorBrush(Color.FromRgb(78, 81, 88));
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
                    pair.Value.Background = isSel ? new SolidColorBrush(Color.FromRgb(75, 155, 255)) : new SolidColorBrush(Color.FromRgb(51, 53, 58));
                    pair.Value.BorderBrush = isSel ? new SolidColorBrush(Color.FromRgb(120, 187, 255)) : new SolidColorBrush(Color.FromRgb(78, 81, 88));
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
            content.Children.Add(PageHeader("\uE946", "Sobre", "Conheça a B.M Flow"));

            var hero = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromRgb(67, 116, 177)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(24, 22, 24, 20),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new LinearGradientBrush(
                    Color.FromRgb(44, 78, 120),
                    Color.FromRgb(31, 48, 72),
                    new Point(0, 0), new Point(1, 1)),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 4, Opacity = .18 }
            };
            var heroStack = new StackPanel();
            var heroHeader = new Grid();
            heroHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            heroHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heroHeader.Children.Add(new Border
            {
                Width = 60, Height = 60, CornerRadius = new CornerRadius(15),
                Background = new LinearGradientBrush(Color.FromRgb(104, 177, 255), Color.FromRgb(45, 126, 239), 45),
                Effect = new DropShadowEffect { Color = Color.FromRgb(25, 105, 225), BlurRadius = 15, ShadowDepth = 2, Opacity = .35 },
                Child = new TextBlock
                {
                    Text = "\uE80F", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 27,
                    Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var heroIdentity = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            heroIdentity.Children.Add(new TextBlock
            {
                Text = "B.M Flow", FontSize = 24, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            heroIdentity.Children.Add(new TextBlock
            {
                Text = "Sua área de trabalho em movimento.", FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 235, 255)), Margin = new Thickness(0, 2, 0, 7)
            });
            var heroBadges = new StackPanel { Orientation = Orientation.Horizontal };
            heroBadges.Children.Add(AboutPill("", "Windows 11", true));
            heroBadges.Children.Add(AboutPill("", "Código aberto", false));
            heroIdentity.Children.Add(heroBadges);
            Grid.SetColumn(heroIdentity, 1);
            heroHeader.Children.Add(heroIdentity);
            heroStack.Children.Add(heroHeader);
            heroStack.Children.Add(new TextBlock
            {
                Text = "Uma dock nativa e personalizável que transforma sua experiência com o Windows, reunindo aplicativos, espaços de trabalho e recursos do sistema em um só lugar.",
                FontSize = 12.5, LineHeight = 20, Foreground = new SolidColorBrush(Color.FromRgb(236, 242, 251)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0)
            });
            hero.Child = heroStack;
            content.Children.Add(hero);

            var infoCard = AboutCard();
            var infoStack = new StackPanel();
            var infoHeader = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            infoHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            infoHeader.Children.Add(new TextBlock
            {
                Text = "Informações do aplicativo", FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            var localLabel = new TextBlock
            {
                Text = "Instalação local", FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(126, 177, 241)), VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(localLabel, 1);
            infoHeader.Children.Add(localLabel);
            infoStack.Children.Add(infoHeader);

            string installedVersion = GetInstalledVersionText();
            var infoGrid = new Grid();
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddAboutInfoTile(infoGrid, 0, 0, "Versão instalada", installedVersion);
            AddAboutInfoTile(infoGrid, 0, 1, "Plataforma", "Windows 11");
            AddAboutInfoTile(infoGrid, 1, 0, "Tecnologia", "C# · WPF");
            AddAboutInfoTile(infoGrid, 1, 1, "Licença do código", "MIT");
            infoStack.Children.Add(infoGrid);
            infoCard.Child = infoStack;
            content.Children.Add(infoCard);

            var purposeCard = AboutCard();
            var purposeStack = new StackPanel();
            purposeStack.Children.Add(new TextBlock
            {
                Text = "Feito para personalizar seu Windows", FontSize = 16,
                FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
            });
            purposeStack.Children.Add(new TextBlock
            {
                Text = "Do gerenciamento das suas janelas aos pequenos detalhes da área de trabalho, a B.M Flow foi criada para dar mais liberdade ao seu fluxo.",
                FontSize = 12, LineHeight = 19, Foreground = new SolidColorBrush(Color.FromRgb(180, 185, 198)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12)
            });
            var features = new WrapPanel();
            features.Children.Add(AboutPill("\uE8A7", "Espaços", false));
            features.Children.Add(AboutPill("\uE771", "Temas", false));
            features.Children.Add(AboutPill("\uE734", "Mascotes", false));
            features.Children.Add(AboutPill("\uE74C", "Widgets", false));
            purposeStack.Children.Add(features);
            purposeCard.Child = purposeStack;
            content.Children.Add(purposeCard);

            var communityCard = AboutCard();
            var communityStack = new StackPanel();
            communityStack.Children.Add(new TextBlock
            {
                Text = "Comunidade e desenvolvimento", FontSize = 16,
                FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
            });
            communityStack.Children.Add(new TextBlock
            {
                Text = "Um projeto independente, aberto a ideias, melhorias e contribuições da comunidade.",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(180, 185, 198)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 13)
            });

            var updateTitle = new TextBlock
            {
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
            };
            var updateDetail = new TextBlock
            {
                FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(190, 199, 216)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
            };
            var updateActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var updateStatusStack = new StackPanel();
            updateStatusStack.Children.Add(updateTitle);
            updateStatusStack.Children.Add(updateDetail);
            updateStatusStack.Children.Add(updateActions);
            var updateStatus = new Border
            {
                Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
                Padding = new Thickness(13, 11, 13, 11), Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(39, 53, 72)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(58, 91, 132)),
                Child = updateStatusStack
            };
            communityStack.Children.Add(updateStatus);

            var actions = new WrapPanel();
            Border checkUpdateButton = null;
            checkUpdateButton = AboutActionButton("\uE895", "Verificar atualização", false, delegate
            {
                BeginManualUpdateCheck(updateStatus, updateTitle, updateDetail, updateActions, checkUpdateButton);
            });
            actions.Children.Add(checkUpdateButton);
            actions.Children.Add(AboutLinkButton("\uE8A7", "Ver no GitHub", "https://github.com/IsacBM/BMFlow", true));
            actions.Children.Add(AboutLinkButton("\uE895", "Novidades", "https://github.com/IsacBM/BMFlow/releases", false));
            actions.Children.Add(AboutLinkButton("\uE7BA", "Reportar problema", "https://github.com/IsacBM/BMFlow/issues/new", false));
            communityStack.Children.Add(actions);
            communityCard.Child = communityStack;
            content.Children.Add(communityCard);

            content.Children.Add(new TextBlock
            {
                Text = "Desenvolvido por Isac B. Matos  ·  Feito com 💙 para a comunidade",
                FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(170, 175, 188)),
                HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 2)
            });
        }

        private static Border AboutCard()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(42, 42, 48)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 64, 73)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private static void AddAboutInfoTile(Grid grid, int row, int column, string label, string value)
        {
            var tile = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(49, 49, 57)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 82)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(column == 0 ? 0 : 5, 5, column == 0 ? 5 : 0, 0)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(159, 166, 183))
            });
            stack.Children.Add(new TextBlock
            {
                Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 0)
            });
            tile.Child = stack;
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            grid.Children.Add(tile);
        }

        private static Border AboutPill(string glyph, string text, bool blue)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            if (!String.IsNullOrWhiteSpace(glyph))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
                    Foreground = blue ? Brushes.White : new SolidColorBrush(Color.FromRgb(210, 224, 246)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
                });
            }
            panel.Children.Add(new TextBlock
            {
                Text = text, FontSize = 11.5, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            return new Border
            {
                Height = 27, CornerRadius = new CornerRadius(13.5),
                Background = new SolidColorBrush(blue ? Color.FromRgb(45, 120, 180) : Color.FromRgb(55, 61, 75)),
                BorderBrush = new SolidColorBrush(blue ? Color.FromRgb(79, 165, 225) : Color.FromRgb(78, 86, 103)),
                BorderThickness = new Thickness(1), Padding = new Thickness(11, 0, 11, 0),
                Margin = new Thickness(0, 0, 7, 6), Child = panel
            };
        }

        private static Border AboutLinkButton(string glyph, string text, string url, bool primary)
        {
            return AboutActionButton(glyph, text, primary, delegate
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            });
        }

        private static Border AboutActionButton(string glyph, string text, bool primary, Action action)
        {
            var normal = new SolidColorBrush(primary ? Color.FromRgb(57, 139, 255) : Color.FromRgb(55, 55, 65));
            var hover = new SolidColorBrush(primary ? Color.FromRgb(76, 154, 255) : Color.FromRgb(67, 68, 79));
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10.5,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            });
            var label = new TextBlock
            {
                Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(label);
            var button = new Border
            {
                Height = 34, CornerRadius = new CornerRadius(8), Background = normal,
                BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(75, 155, 255) : Color.FromRgb(75, 76, 88)),
                BorderThickness = new Thickness(1), Padding = new Thickness(13, 0, 13, 0),
                Margin = new Thickness(0, 0, 8, 4), Cursor = Cursors.Hand, Child = panel, Tag = label
            };
            button.MouseEnter += delegate { if (button.IsEnabled) button.Background = hover; };
            button.MouseLeave += delegate { button.Background = normal; };
            button.MouseLeftButtonUp += delegate
            {
                if (button.IsEnabled && action != null) action();
            };
            return button;
        }

        private void BeginManualUpdateCheck(Border status, TextBlock title, TextBlock detail, WrapPanel actions, Border checkButton)
        {
            if (status == null || title == null || detail == null || actions == null || checkButton == null) return;
            var buttonLabel = checkButton.Tag as TextBlock;
            checkButton.IsEnabled = false;
            checkButton.Opacity = .62;
            checkButton.Cursor = Cursors.Arrow;
            if (buttonLabel != null) buttonLabel.Text = "Verificando…";
            actions.Children.Clear();
            status.Visibility = Visibility.Visible;
            status.Background = new SolidColorBrush(Color.FromRgb(39, 53, 72));
            status.BorderBrush = new SolidColorBrush(Color.FromRgb(58, 91, 132));
            title.Text = "Buscando a versão mais recente…";
            detail.Text = "Consultando as releases oficiais da B.M Flow no GitHub.";

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult result = FetchLatestRelease();
                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        if (!IsLoaded || settingsPageHost == null) return;
                        checkButton.IsEnabled = true;
                        checkButton.Opacity = 1;
                        checkButton.Cursor = Cursors.Hand;
                        if (buttonLabel != null) buttonLabel.Text = "Verificar novamente";
                        actions.Children.Clear();

                        if (!result.Success)
                        {
                            status.Background = new SolidColorBrush(Color.FromRgb(67, 49, 43));
                            status.BorderBrush = new SolidColorBrush(Color.FromRgb(132, 82, 62));
                            title.Text = "Não foi possível verificar agora";
                            detail.Text = result.ErrorMessage;
                            return;
                        }

                        if (result.UpdateAvailable)
                        {
                            status.Background = new SolidColorBrush(Color.FromRgb(38, 60, 88));
                            status.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 121, 191));
                            title.Text = "Nova versão disponível: " + result.LatestTag;
                            detail.Text = "Instalada: v" + result.InstalledVersion + ". Abra a página oficial para conferir as notas e baixar a atualização.";
                            actions.Children.Add(AboutLinkButton("\uE896", "Abrir atualização", result.ReleaseUrl, true));
                        }
                        else
                        {
                            status.Background = new SolidColorBrush(Color.FromRgb(39, 65, 55));
                            status.BorderBrush = new SolidColorBrush(Color.FromRgb(66, 126, 96));
                            title.Text = "B.M Flow está atualizada";
                            detail.Text = "Você já está usando a versão mais recente (v" + result.InstalledVersion + ").";
                        }
                    }));
                }
                catch { }
            });
        }

        private static UpdateCheckResult FetchLatestRelease()
        {
            var result = new UpdateCheckResult { InstalledVersion = GetInstalledVersionText() };
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/IsacBM/BMFlow/releases/latest");
                request.Method = "GET";
                request.Accept = "application/vnd.github+json";
                request.UserAgent = "BMFlow/" + result.InstalledVersion;
                request.Headers["X-GitHub-Api-Version"] = "2026-03-10";
                request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();

                string latestTag = ReadJsonStringProperty(json, "tag_name");
                if (String.IsNullOrWhiteSpace(latestTag)) throw new InvalidDataException("A resposta do GitHub não contém uma versão válida.");

                Version installed = ParseReleaseVersion(result.InstalledVersion);
                Version latest = ParseReleaseVersion(latestTag);
                if (installed == null || latest == null) throw new InvalidDataException("Não foi possível comparar as versões.");

                result.Success = true;
                result.LatestTag = latestTag;
                result.UpdateAvailable = latest > installed;
                result.ReleaseUrl = "https://github.com/IsacBM/BMFlow/releases/tag/" + Uri.EscapeDataString(latestTag);
            }
            catch (WebException)
            {
                result.ErrorMessage = "Confira sua conexão com a internet e tente novamente.";
            }
            catch
            {
                result.ErrorMessage = "O GitHub respondeu de uma forma inesperada. Tente novamente mais tarde.";
            }
            return result;
        }

        private static string GetInstalledVersionText()
        {
            Version version = typeof(DockSettingsWindow).Assembly.GetName().Version;
            return version == null
                ? "0.0.0"
                : String.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", version.Major, version.Minor, Math.Max(0, version.Build));
        }

        private static Version ParseReleaseVersion(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(1);
            int suffix = normalized.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) normalized = normalized.Substring(0, suffix);
            string[] parts = normalized.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return null;
            while (parts.Length < 4) parts = parts.Concat(new[] { "0" }).ToArray();
            Version parsed;
            return Version.TryParse(String.Join(".", parts), out parsed) ? parsed : null;
        }

        private static string ReadJsonStringProperty(string json, string propertyName)
        {
            if (String.IsNullOrWhiteSpace(json) || String.IsNullOrWhiteSpace(propertyName)) return null;
            string marker = "\"" + propertyName + "\"";
            int index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return null;
            index = json.IndexOf(':', index + marker.Length);
            if (index < 0) return null;
            index++;
            while (index < json.Length && Char.IsWhiteSpace(json[index])) index++;
            if (index >= json.Length || json[index] != '"') return null;
            index++;

            var value = new StringBuilder();
            while (index < json.Length)
            {
                char current = json[index++];
                if (current == '"') return value.ToString();
                if (current != '\\')
                {
                    value.Append(current);
                    continue;
                }
                if (index >= json.Length) break;
                char escaped = json[index++];
                switch (escaped)
                {
                    case '"': value.Append('"'); break;
                    case '\\': value.Append('\\'); break;
                    case '/': value.Append('/'); break;
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'u':
                        if (index + 4 <= json.Length)
                        {
                            int code;
                            if (Int32.TryParse(json.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                value.Append((char)code);
                            index += 4;
                        }
                        break;
                    default: value.Append(escaped); break;
                }
            }
            return null;
        }

        private static FrameworkElement SectionTitle(string title, string subtitle)
        {
            var panel = new StackPanel { Margin = new Thickness(4, 18, 4, 11) };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 17.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(243, 241, 244)) });
            panel.Children.Add(new TextBlock { Text = subtitle, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(164, 159, 167)), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        private static Border SettingsCard()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(43, 41, 45)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(66, 63, 70)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22, 16, 22, 16), Margin = new Thickness(0, 0, 0, 9),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 4, Opacity = .16 }
            };
        }

        private static ControlTemplate CreateToggleTemplate()
        {
            var template = new ControlTemplate(typeof(CheckBox));
            var root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetValue(DockPanel.LastChildFillProperty, true);

            var track = new FrameworkElementFactory(typeof(Border));
            track.Name = "SwitchTrack";
            track.SetValue(DockPanel.DockProperty, Dock.Right);
            track.SetValue(FrameworkElement.WidthProperty, 44.0);
            track.SetValue(FrameworkElement.HeightProperty, 24.0);
            track.SetValue(FrameworkElement.MarginProperty, new Thickness(18, 0, 0, 0));
            track.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            track.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(86, 82, 90)));

            var thumb = new FrameworkElementFactory(typeof(Border));
            thumb.Name = "SwitchThumb";
            thumb.SetValue(FrameworkElement.WidthProperty, 18.0);
            thumb.SetValue(FrameworkElement.HeightProperty, 18.0);
            thumb.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
            thumb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            thumb.SetValue(Border.BackgroundProperty, Brushes.White);
            thumb.SetValue(FrameworkElement.EffectProperty, new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 1, Opacity = .28 });
            track.AppendChild(thumb);
            root.AppendChild(track);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(presenter);
            template.VisualTree = root;

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(75, 155, 255)), "SwitchTrack"));
            checkedTrigger.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right, "SwitchThumb"));
            template.Triggers.Add(checkedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, .45));
            template.Triggers.Add(disabledTrigger);
            return template;
        }

        private static ControlTemplate CreateComboBoxTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));
            var root = new FrameworkElementFactory(typeof(Grid));
            root.Name = "ComboRoot";

            var field = new FrameworkElementFactory(typeof(Border));
            field.Name = "ComboField";
            field.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(35, 34, 38)));
            field.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(73, 70, 78)));
            field.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            field.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));

            var fieldGrid = new FrameworkElementFactory(typeof(Grid));
            var selection = new FrameworkElementFactory(typeof(ContentPresenter));
            selection.Name = "SelectionContent";
            selection.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            selection.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            selection.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 38, 0));
            selection.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            selection.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(239, 237, 241)));
            fieldGrid.AppendChild(selection);

            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "\uE70D");
            arrow.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
            arrow.SetValue(TextBlock.FontSizeProperty, 11.0);
            arrow.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(190, 185, 194)));
            arrow.SetValue(FrameworkElement.WidthProperty, 38.0);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            fieldGrid.AppendChild(arrow);
            field.AppendChild(fieldGrid);
            root.AppendChild(field);

            var clickTarget = new FrameworkElementFactory(typeof(ToggleButton));
            clickTarget.Name = "DropDownToggle";
            clickTarget.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            clickTarget.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            clickTarget.SetValue(UIElement.FocusableProperty, false);
            clickTarget.SetValue(Control.TemplateProperty, CreateComboToggleButtonTemplate());
            clickTarget.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsDropDownOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = System.Windows.Data.BindingMode.TwoWay
            });
            root.AppendChild(clickTarget);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.StaysOpenProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsDropDownOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = System.Windows.Data.BindingMode.TwoWay
            });

            var dropDown = new FrameworkElementFactory(typeof(Border));
            dropDown.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(45, 43, 48)));
            dropDown.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(81, 77, 87)));
            dropDown.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            dropDown.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            dropDown.SetValue(Border.PaddingProperty, new Thickness(4));
            dropDown.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
            dropDown.SetValue(FrameworkElement.MinWidthProperty, 120.0);
            dropDown.SetValue(FrameworkElement.MaxHeightProperty, 260.0);
            dropDown.SetValue(FrameworkElement.EffectProperty, new DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 18, ShadowDepth = 5, Opacity = .38
            });
            dropDown.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(items);
            dropDown.AppendChild(scroll);
            popup.AppendChild(dropDown);
            root.AppendChild(popup);
            template.VisualTree = root;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(103, 98, 110)), "ComboField"));
            template.Triggers.Add(hoverTrigger);

            var openTrigger = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
            openTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(75, 155, 255)), "ComboField"));
            template.Triggers.Add(openTrigger);

            var focusTrigger = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(75, 155, 255)), "ComboField"));
            template.Triggers.Add(focusTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, .45, "ComboRoot"));
            template.Triggers.Add(disabledTrigger);
            return template;
        }

        private static ControlTemplate CreateComboToggleButtonTemplate()
        {
            var template = new ControlTemplate(typeof(ToggleButton));
            var hitArea = new FrameworkElementFactory(typeof(Border));
            hitArea.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            template.VisualTree = hitArea;
            return template;
        }

        private static Style CreateComboBoxItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(239, 237, 241))));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));

            var template = new ControlTemplate(typeof(ComboBoxItem));
            var itemBorder = new FrameworkElementFactory(typeof(Border));
            itemBorder.Name = "ItemBackground";
            itemBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            itemBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            itemBorder.AppendChild(content);
            template.VisualTree = itemBorder;

            var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(74, 70, 78)), "ItemBackground"));
            template.Triggers.Add(highlighted);

            var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(47, 75, 109)), "ItemBackground"));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            template.Triggers.Add(selected);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4, "ItemBackground"));
            template.Triggers.Add(disabled);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static ControlTemplate CreateSliderTemplate()
        {
            const string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='{x:Type Slider}'>
  <Grid Height='26' Margin='2,0'>
    <Track x:Name='PART_Track'
           Height='20'
           VerticalAlignment='Center'
           Orientation='Horizontal'
           IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'>
          <RepeatButton.Template>
            <ControlTemplate TargetType='{x:Type RepeatButton}'>
              <Border Height='5' VerticalAlignment='Center' CornerRadius='2.5' Background='#4B9BFF'/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.DecreaseRepeatButton>
      <Track.Thumb>
        <Thumb Width='18' Height='18'>
          <Thumb.Template>
            <ControlTemplate TargetType='{x:Type Thumb}'>
              <Border x:Name='ThumbVisual'
                      Background='#4B9BFF'
                      BorderBrush='#DCEEFF'
                      BorderThickness='2'
                      CornerRadius='9'>
                <Border.Effect>
                  <DropShadowEffect Color='Black' BlurRadius='8' ShadowDepth='2' Opacity='.38'/>
                </Border.Effect>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'>
                  <Setter TargetName='ThumbVisual' Property='Background' Value='#70B0FF'/>
                  <Setter TargetName='ThumbVisual' Property='BorderBrush' Value='White'/>
                </Trigger>
                <Trigger Property='IsDragging' Value='True'>
                  <Setter TargetName='ThumbVisual' Property='Background' Value='#2F7EE6'/>
                  <Setter TargetName='ThumbVisual' Property='Effect'>
                    <Setter.Value>
                      <DropShadowEffect Color='#4B9BFF' BlurRadius='14' ShadowDepth='0' Opacity='.58'/>
                    </Setter.Value>
                  </Setter>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'>
          <RepeatButton.Template>
            <ControlTemplate TargetType='{x:Type RepeatButton}'>
              <Border Height='5' VerticalAlignment='Center' CornerRadius='2.5' Background='#534F58'/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.IncreaseRepeatButton>
    </Track>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter Property='Opacity' Value='.42'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        private static Style CreateScrollBarStyle()
        {
            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ScrollBar}'>
  <Setter Property='Width' Value='12'/>
  <Setter Property='MinWidth' Value='12'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type ScrollBar}'>
        <Grid Width='12' Background='Transparent'>
          <Track x:Name='PART_Track'
                 Margin='0,5'
                 Orientation='Vertical'
                 IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton>
              <RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'>
                <RepeatButton.Template>
                  <ControlTemplate TargetType='{x:Type RepeatButton}'>
                    <Border Background='Transparent'/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb MinHeight='34' Width='10'>
                <Thumb.Template>
                  <ControlTemplate TargetType='{x:Type Thumb}'>
                    <Grid Background='Transparent'>
                      <Border x:Name='ThumbVisual'
                              Width='5'
                              HorizontalAlignment='Center'
                              CornerRadius='2.5'
                              Background='#626975'/>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='ThumbVisual' Property='Width' Value='7'/>
                        <Setter TargetName='ThumbVisual' Property='CornerRadius' Value='3.5'/>
                        <Setter TargetName='ThumbVisual' Property='Background' Value='#4B9BFF'/>
                      </Trigger>
                      <Trigger Property='IsDragging' Value='True'>
                        <Setter TargetName='ThumbVisual' Property='Width' Value='7'/>
                        <Setter TargetName='ThumbVisual' Property='CornerRadius' Value='3.5'/>
                        <Setter TargetName='ThumbVisual' Property='Background' Value='#2F7EE6'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'>
                <RepeatButton.Template>
                  <ControlTemplate TargetType='{x:Type RepeatButton}'>
                    <Border Background='Transparent'/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsEnabled' Value='False'>
            <Setter Property='Opacity' Value='.35'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        private static CheckBox Toggle(string title, string subtitle, bool value)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(242, 240, 243)) });
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(174, 169, 176)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            return new CheckBox
            {
                Content = text, IsChecked = value, Foreground = Brushes.White,
                Template = toggleTemplate,
                Margin = new Thickness(0, 8, 0, 8), Cursor = Cursors.Hand,
                MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
        }

        private Border BuildThemeCard(string name)
        {
            Color dockColor = Color.FromRgb(31, 31, 35);
            Color borderColor = Colors.White;
            byte dockAlpha = 238;
            byte borderAlpha = 75;
            double borderWidth = 1;
            double dockRadius = 15;
            Color glowColor = Color.FromRgb(30, 40, 55);
            double glowOpacity = .24;
            double glowBlur = 13;

            if (name == "Nativo")
            {
                dockColor = Color.FromRgb(210, 214, 220); borderColor = Color.FromRgb(245, 248, 255);
                borderAlpha = 180;
            }
            else if (name == "Liquid Glass")
            {
                dockColor = Color.FromRgb(42, 65, 78); borderColor = Color.FromRgb(210, 250, 255);
                dockAlpha = 150; borderAlpha = 220; borderWidth = 1.5; dockRadius = 18;
                glowColor = Color.FromRgb(100, 235, 255); glowOpacity = .5; glowBlur = 18;
            }
            else if (name == "Meia-noite")
            {
                dockColor = Color.FromRgb(31, 31, 35); borderColor = Color.FromRgb(145, 155, 178);
                dockAlpha = 245; borderAlpha = 95;
            }
            else if (name == "Rocket")
            {
                dockColor = Color.FromRgb(23, 29, 45); borderColor = Color.FromRgb(49, 125, 255);
                borderAlpha = 220; borderWidth = 1.5; glowColor = Color.FromRgb(24, 108, 255); glowOpacity = .68; glowBlur = 18;
            }
            else if (name == "Prateleira Aqua")
            {
                dockColor = Color.FromRgb(36, 73, 78); borderColor = Color.FromRgb(157, 245, 238);
                borderAlpha = 180; borderWidth = 1.5; dockRadius = 8; glowColor = Color.FromRgb(52, 208, 211); glowOpacity = .42;
            }
            else if (name == "Neon")
            {
                dockColor = Color.FromRgb(10, 15, 27); borderColor = Color.FromRgb(0, 238, 255);
                borderAlpha = 245; borderWidth = 1.5; dockRadius = 19; glowColor = Color.FromRgb(255, 0, 190); glowOpacity = .8; glowBlur = 20;
            }
            else if (name == "Canarinho")
            {
                dockColor = Color.FromRgb(103, 139, 19); borderColor = Color.FromRgb(245, 226, 43);
                borderAlpha = 230; borderWidth = 1.5; dockRadius = 18; glowColor = Color.FromRgb(205, 231, 48); glowOpacity = .56;
            }
            else if (name == "Papel")
            {
                dockColor = Color.FromRgb(250, 249, 244); borderColor = Color.FromRgb(218, 218, 212);
                dockAlpha = 248; borderAlpha = 245; glowColor = Color.FromRgb(30, 30, 35); glowOpacity = .2;
            }
            else if (name == "Flutuante")
            {
                dockColor = Color.FromRgb(20, 23, 29); borderColor = Colors.Transparent;
                dockAlpha = 28; borderAlpha = 0; borderWidth = 0; glowOpacity = 0;
            }

            Color tileColor = Color.FromArgb(205, 45, 45, 50);
            Color tileBorder = Color.FromArgb(65, 255, 255, 255);
            double tileRadius = 8;
            if (name == "Liquid Glass") { tileColor = Color.FromArgb(145, 225, 248, 255); tileBorder = Color.FromArgb(210, 235, 255, 255); tileRadius = 10; }
            else if (name == "Rocket") { tileColor = Color.FromArgb(225, 27, 34, 53); tileBorder = Color.FromArgb(150, 66, 130, 255); }
            else if (name == "Prateleira Aqua") { tileColor = Color.FromArgb(205, 225, 245, 241); tileBorder = Color.FromArgb(120, 51, 179, 184); tileRadius = 6; }
            else if (name == "Neon") { tileColor = Color.FromArgb(225, 23, 25, 38); tileBorder = Color.FromArgb(220, 0, 235, 255); tileRadius = 10; }
            else if (name == "Canarinho") { tileColor = Color.FromArgb(225, 188, 205, 48); tileBorder = Color.FromArgb(225, 250, 229, 55); tileRadius = 10; }
            else if (name == "Papel") { tileColor = Color.FromArgb(245, 255, 255, 252); tileBorder = Color.FromArgb(85, 110, 110, 105); tileRadius = 9; }
            else if (name == "Flutuante") { tileColor = Color.FromArgb(220, 36, 39, 48); tileBorder = Color.FromArgb(55, 255, 255, 255); tileRadius = 9; }

            if (dock.CurrentIconTileStyle == "Branco") { tileColor = Color.FromArgb(238, 255, 255, 255); tileBorder = Color.FromArgb(75, 110, 110, 115); }
            else if (dock.CurrentIconTileStyle == "Escuro") { tileColor = Color.FromArgb(225, 38, 40, 48); tileBorder = Color.FromArgb(75, 255, 255, 255); }
            else if (dock.CurrentIconTileStyle == "Transparente") { tileColor = Color.FromArgb(35, 255, 255, 255); tileBorder = Color.FromArgb(48, 255, 255, 255); }

            var card = new Border
            {
                Width = 214, Height = 158, Margin = new Thickness(5), Padding = new Thickness(8),
                CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(61, 62, 67)),
                BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand, Tag = name
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            var preview = new Border
            {
                CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 0, 5), ClipToBounds = true,
                Background = new LinearGradientBrush(Color.FromRgb(16, 67, 79), Color.FromRgb(11, 39, 56), 35)
            };
            var previewCanvas = new Grid();
            previewCanvas.Children.Add(new Ellipse
            {
                Width = 95, Height = 95, Fill = new SolidColorBrush(Color.FromArgb(30, 66, 175, 255)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -42, -24, 0)
            });
            previewCanvas.Children.Add(new Ellipse
            {
                Width = 75, Height = 75, Fill = new SolidColorBrush(Color.FromArgb(24, 114, 70, 255)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(-27, 0, 0, -38)
            });
            var miniDock = new Border
            {
                Width = 178, Height = 55, CornerRadius = new CornerRadius(dockRadius),
                Background = new SolidColorBrush(Color.FromArgb(dockAlpha, dockColor.R, dockColor.G, dockColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(borderAlpha, borderColor.R, borderColor.G, borderColor.B)),
                BorderThickness = new Thickness(borderWidth),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = glowOpacity <= 0 ? null : new DropShadowEffect
                {
                    Color = glowColor, BlurRadius = glowBlur, ShadowDepth = name == "Prateleira Aqua" ? 3 : 1, Opacity = glowOpacity
                }
            };
            var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            icons.Children.Add(CreateThemePreviewTile(themeStartIcon ?? LoadThemeStartIcon(), tileColor, tileBorder, tileRadius, false, name));
            for (int i = 0; i < Math.Min(3, themePreviewIcons.Count); i++)
                icons.Children.Add(CreateThemePreviewTile(themePreviewIcons[i], tileColor, tileBorder, tileRadius, i != 1, name));
            miniDock.Child = icons;
            previewCanvas.Children.Add(miniDock);
            preview.Child = previewCanvas;
            grid.Children.Add(preview);
            var label = new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontSize = 13 };
            Grid.SetRow(label, 1);
            grid.Children.Add(label);
            card.Child = grid;
            card.MouseLeftButtonUp += delegate { dock.SelectTheme(name); UpdateThemeSelection(); };
            themeCards[name] = card;
            return card;
        }

        private static FrameworkElement CreateThemePreviewTile(ImageSource source, Color background, Color border, double radius, bool running, string theme)
        {
            var stack = new Grid { Width = 41, Height = 41, VerticalAlignment = VerticalAlignment.Center };
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            var tile = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(radius),
                Background = new SolidColorBrush(background), BorderBrush = new SolidColorBrush(border),
                BorderThickness = new Thickness(.7), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (source != null)
            {
                tile.Child = new Image { Source = source, Width = 25, Height = 25, Stretch = Stretch.Uniform };
            }
            else
            {
                tile.Child = new TextBlock
                {
                    Text = "\uE7C3", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20,
                    Foreground = new SolidColorBrush(Color.FromRgb(75, 155, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
            }
            Grid.SetRow(tile, 0);
            stack.Children.Add(tile);
            Color indicatorColor = theme == "Papel" ? Color.FromRgb(45, 111, 205)
                : theme == "Canarinho" ? Color.FromRgb(255, 249, 176)
                : theme == "Neon" ? Color.FromRgb(0, 238, 255)
                : Color.FromRgb(92, 174, 255);
            var indicator = new Border
            {
                Width = running ? 10 : 0, Height = 2, CornerRadius = new CornerRadius(1),
                Background = running ? new SolidColorBrush(indicatorColor) : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(indicator, 1);
            stack.Children.Add(indicator);
            return stack;
        }

        private List<ImageSource> ResolveThemePreviewIcons()
        {
            var icons = new List<ImageSource>();
            try
            {
                var manager = dock.CurrentWorkspaceManager;
                var workspace = manager == null ? null : manager.ActiveWorkspace;
                if (workspace != null)
                {
                    foreach (var app in workspace.Apps)
                    {
                        ImageSource icon = DockWindow.ResolveAppIcon(app.Target, app.AppUserModelId);
                        if (icon != null) icons.Add(icon);
                        if (icons.Count >= 3) break;
                    }
                }

                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string[] fallbackTargets =
                {
                    System.IO.Path.Combine(windows, "explorer.exe"),
                    System.IO.Path.Combine(windows, "System32", "notepad.exe"),
                    @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                    Process.GetCurrentProcess().MainModule.FileName
                };
                foreach (string target in fallbackTargets)
                {
                    if (icons.Count >= 3) break;
                    if (!File.Exists(target)) continue;
                    ImageSource icon = DockWindow.ResolveAppIcon(target, null);
                    if (icon != null) icons.Add(icon);
                }
            }
            catch { }
            return icons;
        }

        private static ImageSource LoadThemeStartIcon()
        {
            if (themeStartIcon != null) return themeStartIcon;
            try
            {
                using (Stream stream = typeof(DockSettingsWindow).Assembly.GetManifestResourceStream("windows11.png"))
                {
                    if (stream == null) return null;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    themeStartIcon = bitmap;
                }
            }
            catch { }
            return themeStartIcon;
        }

        private void UpdateThemeSelection()
        {
            foreach (var item in themeCards)
            {
                bool selected = item.Key == dock.CurrentTheme;
                item.Value.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(75, 155, 255)) : Brushes.Transparent;
                item.Value.Background = new SolidColorBrush(selected ? Color.FromRgb(52, 68, 88) : Color.FromRgb(61, 62, 67));
            }
        }

        private static FrameworkElement SliderControl(string title, string suffix, double minimum, double maximum, double value, Action<double> changed, bool percentage = false)
        {
            var panel = new StackPanel { Margin = new Thickness(2, 7, 2, 7) };
            var caption = new Grid();
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            caption.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            var valueText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(183, 217, 255)),
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var valuePill = new Border
            {
                MinWidth = 56, Height = 27, Padding = new Thickness(9, 0, 9, 0),
                CornerRadius = new CornerRadius(13.5),
                Background = new SolidColorBrush(Color.FromRgb(41, 56, 76)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(57, 92, 134)),
                BorderThickness = new Thickness(1), Child = valueText
            };
            Grid.SetColumn(valuePill, 1);
            caption.Children.Add(valuePill);
            panel.Children.Add(caption);
            var slider = new Slider
            {
                Minimum = minimum, Maximum = maximum, Value = value,
                TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true,
                Template = sliderTemplate, Cursor = Cursors.Hand,
                Margin = new Thickness(0, 7, 0, 2)
            };
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
                Height = 36, Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand,
                Template = comboBoxTemplate,
                ItemContainerStyle = comboBoxItemStyle,
                Background = new SolidColorBrush(Color.FromRgb(35, 34, 38)),
                Foreground = new SolidColorBrush(Color.FromRgb(239, 237, 241)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(73, 70, 78)),
                BorderThickness = new Thickness(1)
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
            return new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(63, 60, 67)), Margin = new Thickness(0, 8, 0, 8) };
        }
    }

}
