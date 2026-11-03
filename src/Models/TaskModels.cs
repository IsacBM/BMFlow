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
    internal sealed class TaskApp
    {
        public string Name;
        public string Target;
        public string Arguments;
        public string Identity;
        public string AppUserModelId;
        public IntPtr WindowHandle;
        public readonly List<IntPtr> WindowHandles = new List<IntPtr>();
        public readonly List<string> WindowTitles = new List<string>();
        public ImageSource Icon;
        public Color? AccentColor;
        public bool IsPinned;
        public bool IsRunningInBackground;

        public Color? GetAccentColor()
        {
            if (AccentColor.HasValue) return AccentColor;
            if (Icon != null)
            {
                AccentColor = DockWindow.ExtractAccentColor(Icon);
            }
            return AccentColor;
        }
    }

    internal sealed class RecentDestination
    {
        public string Name;
        public string Path;
        public bool IsFolder;
    }

}
