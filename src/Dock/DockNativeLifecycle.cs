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

    }
}
