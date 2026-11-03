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
    internal enum PreviewDockSide
    {
        Bottom,
        Top,
        Left,
        Right
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
        private readonly PreviewDockSide dockSide;
        private readonly bool verticalLayout;

        // Apenas os cartões da página visível existem e possuem thumbnail DWM
        // registrado. Grupos grandes não multiplicam o custo de composição.
        private readonly List<IntPtr> cardWindows = new List<IntPtr>();
        private readonly List<Border> previewCards = new List<Border>();
        private readonly List<TextBlock> cardLabels = new List<TextBlock>();
        private readonly List<Border> cardMinimizeButtons = new List<Border>();
        private readonly List<double> previewCardBaseTops = new List<double>();
        private readonly List<double> previewCardCurrentTops = new List<double>();
        private readonly List<double> previewCardTargetTops = new List<double>();
        private readonly List<double> previewCardBaseLefts = new List<double>();
        private readonly List<double> previewCardCurrentLefts = new List<double>();
        private readonly List<double> previewCardTargetLefts = new List<double>();
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
        private double previewPointerY;
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

        public PreviewWindow(IEnumerable<IntPtr> sources, IList<string> titles, string appName, Size maximumArea, PreviewDockSide dockSide, Color? accentColor = null)
        {
            this.appName = appName ?? String.Empty;
            this.maximumArea = maximumArea.Width >= 200 && maximumArea.Height >= 200
                ? maximumArea
                : new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
            this.accentColor = accentColor;
            this.dockSide = dockSide;
            verticalLayout = dockSide == PreviewDockSide.Left || dockSide == PreviewDockSide.Right;
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
                Point pointer = args.GetPosition(root);
                previewPointerX = pointer.X;
                previewPointerY = pointer.Y;
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

            if (verticalLayout)
            {
                int maxVerticalRows = (int)Math.Floor((maximumArea.Height - headerSpace - 24) / (cardHeight + 8));
                maxVerticalRows = Math.Max(1, Math.Min(4, maxVerticalRows));
                columns = 1;
                rows = Math.Min(count, maxVerticalRows);
                pageSize = rows;
                pageCount = (int)Math.Ceiling((double)count / Math.Max(1, pageSize));
                pageIndex = Math.Max(0, Math.Min(pageIndex, pageCount - 1));
                Width = 16 + waveHeadroom + cardWidth;
                Height = 8 + headerSpace + rows * (cardHeight + 8);
                return;
            }

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
            previewCardBaseLefts.Clear();
            previewCardCurrentLefts.Clear();
            previewCardTargetLefts.Clear();

            int start = pageIndex * pageSize;
            int end = Math.Min(sourceWindows.Count, start + pageSize);
            for (int index = start; index < end; index++)
            {
                int slot = index - start;
                int column = verticalLayout ? 0 : slot % columns;
                int row = verticalLayout ? slot : slot / columns;
                // A última linha incompleta fica centralizada sobre o ícone.
                int itemsInRow = Math.Min(columns, end - start - row * columns);
                double rowOffset = verticalLayout ? 0 : (columns - itemsInRow) * (cardWidth + 8) / 2;
                double centerColumn = (itemsInRow - 1) / 2.0;
                double edgeDistance = Math.Max(1, centerColumn);
                double centrality = Math.Max(0, 1 - Math.Abs(column - centerColumn) / edgeDistance);
                double centralityLift = !verticalLayout && sourceWindows.Count > 1 ? centrality * 8 : 0;
                double cardTop = 6 + headerSpace + (verticalLayout ? 0 : waveHeadroom) + row * (cardHeight + 8) - centralityLift;
                double cardLeft = 8 + rowOffset + column * (cardWidth + 8);
                if (verticalLayout && dockSide == PreviewDockSide.Right) cardLeft += waveHeadroom;

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
                previewCardBaseLefts.Add(cardLeft);
                previewCardCurrentLefts.Add(cardLeft);
                previewCardTargetLefts.Add(cardLeft);
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
                    previewPointerY = Canvas.GetTop(previewCards[slot]) + cardHeight / 2;
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
            double radius = (verticalLayout ? cardHeight : cardWidth) * 1.30;
            for (int index = 0; index < previewCards.Count; index++)
            {
                double center = verticalLayout
                    ? previewCardBaseTops[index] + cardHeight / 2
                    : previewCardBaseLefts[index] + cardWidth / 2;
                double distance = Math.Abs(center - (verticalLayout ? previewPointerY : previewPointerX));
                double influence = distance >= radius
                    ? 0
                    : (Math.Cos(Math.PI * distance / radius) + 1) / 2;
                if (verticalLayout)
                {
                    double direction = dockSide == PreviewDockSide.Left ? 1 : -1;
                    previewCardTargetLefts[index] = previewCardBaseLefts[index] + direction * 13 * influence;
                    previewCardTargetTops[index] = previewCardBaseTops[index];
                }
                else
                {
                    // A curvatura de repouso deixa o centro levemente elevado. Esta
                    // segunda elevação percorre os cartões seguindo o mouse.
                    previewCardTargetTops[index] = previewCardBaseTops[index] - 13 * influence;
                    previewCardTargetLefts[index] = previewCardBaseLefts[index];
                }
            }
        }

        private void RenderPreviewWave()
        {
            bool moving = false;
            for (int index = 0; index < previewCards.Count; index++)
            {
                double currentTop = previewCardCurrentTops[index];
                double currentLeft = previewCardCurrentLefts[index];
                double targetTop = previewCardTargetTops[index];
                double targetLeft = previewCardTargetLefts[index];
                double nextTop = currentTop + (targetTop - currentTop) * .30;
                double nextLeft = currentLeft + (targetLeft - currentLeft) * .30;
                if (Math.Abs(targetTop - nextTop) < .08) nextTop = targetTop; else moving = true;
                if (Math.Abs(targetLeft - nextLeft) < .08) nextLeft = targetLeft; else moving = true;
                if (Math.Abs(nextTop - currentTop) < .01 && Math.Abs(nextLeft - currentLeft) < .01) continue;
                previewCardCurrentTops[index] = nextTop;
                previewCardCurrentLefts[index] = nextLeft;
                Canvas.SetTop(previewCards[index], nextTop);
                Canvas.SetLeft(previewCards[index], nextLeft);
                UpdateThumbnailPosition(index, nextLeft - previewCardBaseLefts[index], nextTop - previewCardBaseTops[index]);
            }
            if (!moving) previewWaveTimer.Stop();
        }

        private void UpdateThumbnailPosition(int index, double offsetX, double offsetY)
        {
            if (index < 0 || index >= thumbnails.Count || index >= thumbnailBaseDestinations.Count) return;
            IntPtr thumbnail = thumbnails[index];
            if (thumbnail == IntPtr.Zero) return;
            NativeRect baseline = thumbnailBaseDestinations[index];
            int shiftX = (int)Math.Round(offsetX * thumbnailDpiX);
            int shiftY = (int)Math.Round(offsetY * thumbnailDpiY);
            baseline.Left += shiftX;
            baseline.Right += shiftX;
            baseline.Top += shiftY;
            baseline.Bottom += shiftY;
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
                int cardLeft = (int)Math.Round(previewCardBaseLefts[index] * dpiX);
                int cardTop = (int)Math.Round(previewCardBaseTops[index] * dpiY);
                int innerPaddingX = (int)Math.Round(8 * dpiX);
                int innerPaddingY = (int)Math.Round(4 * dpiY);
                int left = cardLeft + innerPaddingX + (availableWidth - width) / 2;
                int top = cardTop + innerPaddingY + (availableHeight - height) / 2;
                NativeRect destinationRect = new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height };
                thumbnailBaseDestinations[index] = destinationRect;
                // A onda pode estar em andamento: aplica o deslocamento atual.
                int shiftX = (int)Math.Round((previewCardCurrentLefts[index] - previewCardBaseLefts[index]) * dpiX);
                int shiftY = (int)Math.Round((previewCardCurrentTops[index] - previewCardBaseTops[index]) * dpiY);
                destinationRect.Left += shiftX;
                destinationRect.Right += shiftX;
                destinationRect.Top += shiftY;
                destinationRect.Bottom += shiftY;
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

}
