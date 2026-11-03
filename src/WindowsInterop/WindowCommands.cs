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
    }

}
