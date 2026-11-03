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
                else if (query.Contains("mascote") || query.Contains("pet") || query.Contains("gato") || query.Contains("gatinho") || query.Contains("cachorro") || query.Contains("raposa") || query.Contains("pato") || query.Contains("ganso") || query.Contains("comportamento") || query.Contains("runcat")) ShowSettingsPage("mascot");
                else if (query.Contains("widget") || query.Contains("mouse") || query.Contains("cursor") || query.Contains("mac") || query.Contains("personaliz")) ShowSettingsPage("widgets");
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
            navigation.Children.Add(CreateNavigationItem("mascot", "\uE734", "Mascote"));
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
                Background = new SolidColorBrush(Color.FromRgb(255, 101, 75)),
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

}
