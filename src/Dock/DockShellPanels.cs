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
    public sealed partial class DockWindow
    {
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

    }
}
