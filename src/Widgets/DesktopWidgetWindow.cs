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

}
