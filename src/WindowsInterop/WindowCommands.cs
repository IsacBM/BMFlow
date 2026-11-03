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
    internal static class WindowCommands
    {
        private const uint SystemCommand = 0x0112; // WM_SYSCOMMAND
        private static readonly IntPtr MinimizeCommand = new IntPtr(0xF020); // SC_MINIMIZE
        private static readonly IntPtr CloseCommand = new IntPtr(0xF060); // SC_CLOSE

        public static void ExcludeFromApplicationSwitcher(Window window)
        {
            if (window == null) return;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            const int ExtendedStyle = -20; // GWL_EXSTYLE
            const int ToolWindow = 0x00000080; // WS_EX_TOOLWINDOW
            const int AppWindow = 0x00040000; // WS_EX_APPWINDOW
            int style = GetWindowLong(handle, ExtendedStyle);
            int backgroundStyle = (style | ToolWindow) & ~AppWindow;
            if (backgroundStyle != style) SetWindowLong(handle, ExtendedStyle, backgroundStyle);

            // Notifica o Shell imediatamente para remover qualquer miniatura já
            // armazenada no Alt+Tab/Visão de Tarefas, sem mover ou ativar a janela.
            const uint RefreshFrame = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, RefreshFrame);
        }

        public static bool IsAlive(IntPtr window)
        {
            return window != IntPtr.Zero && IsWindow(window);
        }

        public static bool IsMinimized(IntPtr window)
        {
            return IsAlive(window) && IsIconic(window);
        }

        public static void Activate(IntPtr window)
        {
            if (!IsAlive(window)) return;
            // SW_RESTORE em uma janela maximizada a transforma em janela normal.
            // Só restauramos quando ela está minimizada; nos demais casos SW_SHOW
            // preserva o tamanho e o estado de maximização.
            int command = IsIconic(window) ? 9 : 5;
            // Uma janela travada bloquearia ShowWindow; a versão assíncrona
            // deixa o fantasma do DWM tratar o pedido sem congelar a Dock.
            if (IsHungAppWindow(window)) ShowWindowAsync(window, command);
            else ShowWindow(window, command);
            SetForegroundWindow(window);
        }

        public static void Restore(IntPtr window)
        {
            if (!IsMinimized(window)) return;
            ShowWindowAsync(window, 9); // SW_RESTORE
        }

        public static void Minimize(IntPtr window)
        {
            if (!IsAlive(window) || IsIconic(window)) return;
            // SC_MINIMIZE mantém a animação e as regras do próprio aplicativo,
            // exatamente como o botão da barra de título.
            if (IsHungAppWindow(window)) ShowWindowAsync(window, 6); // SW_MINIMIZE
            else PostMessage(window, SystemCommand, MinimizeCommand, IntPtr.Zero);
        }

        public static void Close(IntPtr window)
        {
            if (!IsAlive(window)) return;
            // SC_CLOSE equivale ao X da barra de título: respeita janelas que
            // desabilitam o fechamento e permite ao app perguntar se deve salvar.
            PostMessage(window, SystemCommand, CloseCommand, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsHungAppWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr window, int index, int newStyle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }

}
