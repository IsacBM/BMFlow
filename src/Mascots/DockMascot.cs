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
        private string state = "Walk"; // Walk, Idle, Rest, Sleep, WakeUp, Jump
        private int stateTicks = 0;
        private int walkFrameIndex = 0;
        private int idleFrameIndex = 0;
        private int sleepFrameIndex = 0;
        private int restFrameIndex = 0;
        private int jumpStep = 0;
        private int sleepCooldownTicks = 200;
        private bool usingIdle2 = false;
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
        private BitmapSource[] idle2Frames;
        private BitmapSource[] restFrames;
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
                        state = "Walk";
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
                        state = "Walk";
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
            if (state == "Sleep" || state == "Rest" || state == "WakeUp") state = "Walk";
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
            bool visible = enabled && ownerDock.IsMascotPositionSupported;
            mascotHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible && speechBubble != null) speechBubble.Visibility = Visibility.Collapsed;
        }

        public void NotifyDockPositionChanged()
        {
            UpdateVisibility();
            if (mascotHost.Visibility == Visibility.Visible) NotifyLayoutUpdated();
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
            double topEdgeY = ownerDock.IsDockAtTop ? 5.0 + dockH : rootH - dockH - 5.0;

            double baseH = (mascotImage.Height > 0 ? mascotImage.Height : 32.0);
            double spriteW = (mascotImage.Width > 0 ? mascotImage.Width : 48.0) * mascotScale;
            posY = ownerDock.IsDockAtTop ? topEdgeY - 2.0 : topEdgeY - baseH + 2.0;

            double minX, maxX;
            if (mascotMode == "PatrolRight" || mascotMode == "RunCat")
            {
                minX = Math.Max(dockLeft + 20, dockRight - 230);
                maxX = Math.Max(minX + 40, dockRight - 130);
            }
            else // Wander
            {
                minX = dockLeft + 25;
                maxX = Math.Max(minX + 50, dockRight - spriteW - 25);
            }

            if (posX <= 0)
            {
                posX = (minX + maxX) / 2.0;
            }
            else
            {
                // Preserva o ponto exato onde o mascote está, impedindo que volte para a origem padrão
                posX = Math.Max(minX, Math.Min(maxX, posX));
            }

            Canvas.SetLeft(mascotHost, posX);
            Canvas.SetTop(mascotHost, posY);
            UpdateBubblePosition();
        }

        public void TakeNap()
        {
            StartNap();
        }

        private void StartNap()
        {
            if (jumpTimer.IsEnabled) jumpTimer.Stop();
            sleepCooldownTicks = rand.Next(350, 600); // 30 a 55 segundos de intervalo entre cochilos espontâneos

            if (restFrames != null && restFrames.Length > 0)
            {
                state = "Rest";
                restFrameIndex = 0;
                stateTicks = restFrames.Length * 2;
                mascotImage.Source = restFrames[0];
            }
            else
            {
                state = "Sleep";
                sleepFrameIndex = 0;
                stateTicks = rand.Next(80, 130); // ~7 a 11 segundos
                if (sleepFrames != null && sleepFrames.Length > 0)
                    mascotImage.Source = sleepFrames[0];
            }
        }

        private void EnterIdleState()
        {
            state = "Idle";
            idleFrameIndex = 0;
            if (idle2Frames != null && idle2Frames.Length > 0 && rand.Next(10) < 4)
            {
                usingIdle2 = true;
                stateTicks = idle2Frames.Length * 2;
            }
            else
            {
                usingIdle2 = false;
                stateTicks = rand.Next(18, 32);
            }
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
            double topEdgeY = ownerDock.IsDockAtTop ? 5.0 + dockH : rootH - dockH - 5.0;
            double baseH = (mascotImage.Height > 0 ? mascotImage.Height : 32.0);
            double spriteW = (mascotImage.Width > 0 ? mascotImage.Width : 48.0) * mascotScale;
            posY = ownerDock.IsDockAtTop ? topEdgeY - 2.0 : topEdgeY - baseH + 2.0;

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
            else // Wander
            {
                minX = dockLeft + 25;
                maxX = Math.Max(minX + 50, dockRight - spriteW - 25);
            }

            // Reduz o cooldown para a próxima soneca espontânea enquanto ativo
            if (mascotMode != "RunCat" && sleepCooldownTicks > 0)
            {
                sleepCooldownTicks--;
            }

            // State machine
            if (state == "Rest")
            {
                // Deitando suavemente (Fox e mascotes com frames de transição)
                if (restFrames != null && restFrames.Length > 0)
                {
                    stateTicks--;
                    if (stateTicks % 2 == 0 && restFrameIndex < restFrames.Length - 1)
                    {
                        restFrameIndex++;
                    }
                    if (restFrameIndex < restFrames.Length)
                        mascotImage.Source = restFrames[restFrameIndex];

                    if (stateTicks <= 0)
                    {
                        state = "Sleep";
                        sleepFrameIndex = 0;
                        stateTicks = rand.Next(80, 130); // Duração da soneca: ~7 a 11 segundos
                    }
                }
                else
                {
                    state = "Sleep";
                    sleepFrameIndex = 0;
                    stateTicks = rand.Next(80, 130);
                }
            }
            else if (state == "Sleep")
            {
                // Dormindo no local exato onde parou (posX permanece inalterado)
                stateTicks--;
                if (stateTicks % 5 == 0)
                {
                    int totalSleep = (sleepFrames != null && sleepFrames.Length > 0 ? sleepFrames.Length : 1);
                    sleepFrameIndex = (sleepFrameIndex + 1) % totalSleep;
                }
                if (sleepFrames != null && sleepFrames.Length > sleepFrameIndex)
                    mascotImage.Source = sleepFrames[sleepFrameIndex];

                if (stateTicks % 35 == 0 && speechBubble.Visibility != Visibility.Visible)
                {
                    speechText.Text = "Zzz... 💤";
                    speechBubble.Visibility = Visibility.Visible;
                    UpdateBubblePosition();
                    bubbleTimer.Stop();
                    bubbleTimer.Start();
                }

                if (stateTicks <= 0)
                {
                    // Fim do cochilo: acorda e levanta suavemente
                    if (restFrames != null && restFrames.Length > 0)
                    {
                        state = "WakeUp";
                        restFrameIndex = restFrames.Length - 1;
                        stateTicks = restFrames.Length * 2;
                    }
                    else
                    {
                        state = "Idle";
                        usingIdle2 = false;
                        idleFrameIndex = 0;
                        stateTicks = rand.Next(15, 30);
                    }
                }
            }
            else if (state == "WakeUp")
            {
                // Levantando após a soneca
                if (restFrames != null && restFrames.Length > 0)
                {
                    stateTicks--;
                    if (stateTicks % 2 == 0 && restFrameIndex > 0)
                    {
                        restFrameIndex--;
                    }
                    if (restFrameIndex >= 0 && restFrameIndex < restFrames.Length)
                        mascotImage.Source = restFrames[restFrameIndex];

                    if (stateTicks <= 0)
                    {
                        state = "Idle";
                        usingIdle2 = false;
                        idleFrameIndex = 0;
                        stateTicks = rand.Next(15, 30);
                        if (rand.Next(10) < 5) direction = -direction;
                    }
                }
                else
                {
                    state = "Idle";
                    usingIdle2 = false;
                    idleFrameIndex = 0;
                    stateTicks = rand.Next(15, 30);
                }
            }
            else if (state == "Idle")
            {
                stateTicks--;
                if (usingIdle2 && idle2Frames != null && idle2Frames.Length > 0)
                {
                    if (stateTicks % 2 == 0) idleFrameIndex = (idleFrameIndex + 1) % idle2Frames.Length;
                    if (idle2Frames.Length > idleFrameIndex)
                        mascotImage.Source = idle2Frames[idleFrameIndex];
                }
                else
                {
                    if (stateTicks % 8 == 0) idleFrameIndex = (idleFrameIndex + 1) % (idleFrames != null && idleFrames.Length > 0 ? idleFrames.Length : 1);
                    if (idleFrames != null && idleFrames.Length > idleFrameIndex)
                        mascotImage.Source = idleFrames[idleFrameIndex];
                }

                if (stateTicks <= 0)
                {
                    // Comportamento orgânico: após caminhar e parar, tem chance de tirar uma sonequinha fofa
                    if (mascotMode != "RunCat" && sleepCooldownTicks <= 0 && rand.Next(10) < 5)
                    {
                        StartNap();
                    }
                    else
                    {
                        state = "Walk";
                        if (rand.Next(10) < 4) direction = -direction;
                    }
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
                        EnterIdleState();
                    }
                    else if (direction < 0 && posX <= minX)
                    {
                        posX = minX;
                        direction = 1;
                        EnterIdleState();
                    }
                    else if (mascotMode != "RunCat" && rand.Next(100) < 3)
                    {
                        EnterIdleState();
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
            sleepCooldownTicks = rand.Next(300, 500);
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
            ownerDock.ConfigureMascotContextMenu(menu, mascotHost);

            var titleItem = new MenuItem { Header = "🐾 Mascote da Barra", IsEnabled = false, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 100)) };
            menu.Items.Add(titleItem);
            menu.Items.Add(new System.Windows.Controls.Separator());

            var mascotsMenu = new MenuItem { Header = "Escolher Mascote" };
            mascotsMenu.Items.Add(CreateCheckItem("🦊 Raposinha (Pixel Fox)", mascotType == "Fox", delegate { ownerDock.SetMascotType("Fox"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐱 Gatinho Meia-Noite (Preto)", mascotType == "CatBlack", delegate { ownerDock.SetMascotType("CatBlack"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐈 Gatinho Tigrão (Laranja)", mascotType == "CatOrange", delegate { ownerDock.SetMascotType("CatOrange"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐱 Gatinho Neve (Branco)", mascotType == "CatWhite", delegate { ownerDock.SetMascotType("CatWhite"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐱 Gatinho Frajola (Tuxedo)", mascotType == "CatTuxedo", delegate { ownerDock.SetMascotType("CatTuxedo"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🐶 Cachorrinho Caramelo", mascotType == "Dog", delegate { ownerDock.SetMascotType("Dog"); }));
            mascotsMenu.Items.Add(CreateCheckItem("🦆 Gansinho / Pato", mascotType == "Duck", delegate { ownerDock.SetMascotType("Duck"); }));
            menu.Items.Add(mascotsMenu);

            var modesMenu = new MenuItem { Header = "Comportamento" };
            modesMenu.Items.Add(CreateCheckItem("📍 Canto Direito (Acima da Lixeira)", mascotMode == "PatrolRight", delegate { ownerDock.SetMascotMode("PatrolRight"); }));
            modesMenu.Items.Add(CreateCheckItem("🚶 Passear pela Barra toda", mascotMode == "Wander", delegate { ownerDock.SetMascotMode("Wander"); }));
            modesMenu.Items.Add(CreateCheckItem("⚡ Ritmo pela CPU (RunCat)", mascotMode == "RunCat", delegate { ownerDock.SetMascotMode("RunCat"); }));
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

            var napActionItem = new MenuItem { Header = "Tirar uma Soneca 💤" };
            napActionItem.Click += delegate { TakeNap(); };
            menu.Items.Add(napActionItem);

            var settingsItem = new MenuItem { Header = "⚙️ Configurações…" };
            settingsItem.Click += delegate { ownerDock.OpenSettingsWindow("mascot"); };
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
                        var customIdle2 = LoadCustomFrames(customDir, "idle2");
                        var customRest = LoadCustomFrames(customDir, "rest");
                        var customSleep = LoadCustomFrames(customDir, "sleep");
                        var customJump = LoadCustomFrames(customDir, "jump");
                        walkFrames = customWalk;
                        idleFrames = customIdle != null && customIdle.Length > 0 ? customIdle : new BitmapSource[] { customWalk[0] };
                        idle2Frames = customIdle2;
                        restFrames = customRest;
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
            idle2Frames = null;
            restFrames = null;

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

}
