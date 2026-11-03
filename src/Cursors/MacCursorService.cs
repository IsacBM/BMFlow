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
    internal static class MacCursorService
    {
        private const uint IMAGE_CURSOR = 2;
        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        private const uint OCR_NORMAL = 32512;
        private const uint OCR_IBEAM = 32513;
        private const uint OCR_WAIT = 32514;
        private const uint OCR_CROSS = 32515;
        private const uint OCR_UP = 32516;
        private const uint OCR_SIZENWSE = 32642;
        private const uint OCR_SIZENESW = 32643;
        private const uint OCR_SIZEWE = 32644;
        private const uint OCR_SIZENS = 32645;
        private const uint OCR_SIZEALL = 32646;
        private const uint OCR_NO = 32648;
        private const uint OCR_HAND = 32649;
        private const uint OCR_APPSTARTING = 32650;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadCursorFromFile(string lpFileName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CopyImage(IntPtr h, uint type, int cx, int cy, uint flags);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const string CursorsRegKey = @"Control Panel\Cursors";
        private const string BackupRegKey = @"Control Panel\Cursors\BMFlow_Original_Backup";

        public static bool IsEnabled { get; set; }
        public static string CurrentTheme { get; set; }
        public static string CurrentSize { get; set; }

        static MacCursorService()
        {
            CurrentTheme = "macOS";
            CurrentSize = "Regular";
        }

        private static readonly Dictionary<string, uint> RoleToOcr = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            { "Arrow", OCR_NORMAL },
            { "IBeam", OCR_IBEAM },
            { "Wait", OCR_WAIT },
            { "Crosshair", OCR_CROSS },
            { "UpArrow", OCR_UP },
            { "SizeNWSE", OCR_SIZENWSE },
            { "SizeNESW", OCR_SIZENESW },
            { "SizeWE", OCR_SIZEWE },
            { "SizeNS", OCR_SIZENS },
            { "SizeAll", OCR_SIZEALL },
            { "No", OCR_NO },
            { "Hand", OCR_HAND },
            { "AppStarting", OCR_APPSTARTING }
        };

        private static readonly Dictionary<string, string[]> RoleCandidates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Arrow", new[] { "arrow.cur", "pointer.cur", "Arrow.cur", "Pointer.cur" } },
            { "Help", new[] { "help.cur", "Help.cur" } },
            { "AppStarting", new[] { "appstarting.ani", "work.ani", "working.ani", "appstarting.cur", "work.cur" } },
            { "Wait", new[] { "wait.ani", "busy.ani", "wait.cur", "busy.cur" } },
            { "Crosshair", new[] { "crosshair.cur", "cross.cur", "precisionhair.cur", "Crosshair.cur", "Cross.cur" } },
            { "IBeam", new[] { "ibeam.cur", "text.cur", "IBeam.cur", "Text.cur" } },
            { "NWPen", new[] { "nwpen.cur", "handwriting.cur", "pen.cur" } },
            { "No", new[] { "no.cur", "unavailiable.cur", "unavailable.cur", "No.cur" } },
            { "SizeNS", new[] { "sizens.cur", "vert.cur", "SizeNS.cur", "Vert.cur" } },
            { "SizeWE", new[] { "sizewe.cur", "horz.cur", "SizeWE.cur", "Horz.cur" } },
            { "SizeNWSE", new[] { "sizenwse.cur", "dng1.cur" } },
            { "SizeNESW", new[] { "sizenesw.cur", "dng2.cur" } },
            { "SizeAll", new[] { "sizeall.cur", "move.cur", "SizeAll.cur", "Move.cur" } },
            { "UpArrow", new[] { "uparrow.cur", "alternate.cur", "UpArrow.cur" } },
            { "Hand", new[] { "hand.cur", "link.cur", "Hand.cur", "Link.cur" } },
            { "Pin", new[] { "pin.cur", "Pin.cur" } },
            { "Person", new[] { "person.cur", "Person.cur" } }
        };

        private static readonly Dictionary<string, ImageSource> PreviewCache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static string GetFolderForSize(string size)
        {
            if (String.Equals(size, "Grande", StringComparison.OrdinalIgnoreCase) || String.Equals(size, "Large", StringComparison.OrdinalIgnoreCase))
                return "macOS-Large-Windows";
            if (String.Equals(size, "Extra Grande", StringComparison.OrdinalIgnoreCase) || String.Equals(size, "ExtraLarge", StringComparison.OrdinalIgnoreCase))
                return "macOS-Extra-Large-Windows";
            return "macOS-Regular-Windows";
        }

        public static string ResolveThemeDir(string theme, string size = "Regular")
        {
            string rel = "";
            if (String.Equals(theme, "FluentDark", StringComparison.OrdinalIgnoreCase))
                rel = System.IO.Path.Combine("Cursors", "DarkCursors");
            else if (String.Equals(theme, "FluentLight", StringComparison.OrdinalIgnoreCase))
                rel = System.IO.Path.Combine("Cursors", "LightCursors");
            else if (String.Equals(theme, "FluentArteffect", StringComparison.OrdinalIgnoreCase))
                rel = System.IO.Path.Combine("Cursors", "Fluent-v3-Arteffect");
            else
                rel = System.IO.Path.Combine("Cursors", "macOS", GetFolderForSize(size));

            string appDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rel);
            if (Directory.Exists(appDir)) return appDir;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDataDir = System.IO.Path.Combine(appData, "BMFlow", rel);
            if (Directory.Exists(appDataDir)) return appDataDir;

            return appDir;
        }

        public static bool HasCursors(string dir)
        {
            if (String.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            try
            {
                return Directory.GetFiles(dir, "*.cur").Length > 0 || Directory.GetFiles(dir, "*.ani").Length > 0;
            }
            catch { return false; }
        }

        public static string FindCursorFile(string dir, string role)
        {
            if (String.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            string[] candidates;
            if (RoleCandidates.TryGetValue(role, out candidates))
            {
                foreach (string candidate in candidates)
                {
                    string full = System.IO.Path.Combine(dir, candidate);
                    if (File.Exists(full)) return full;
                }
            }
            try
            {
                foreach (string file in Directory.GetFiles(dir))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (String.Equals(name, role, StringComparison.OrdinalIgnoreCase)) return file;
                }
            }
            catch { }
            return null;
        }

        private static void BackupOriginalCursors()
        {
            try
            {
                using (RegistryKey src = Registry.CurrentUser.OpenSubKey(CursorsRegKey, false))
                {
                    if (src == null) return;
                    using (RegistryKey dst = Registry.CurrentUser.CreateSubKey(BackupRegKey))
                    {
                        if (dst == null) return;
                        if (dst.ValueCount == 0)
                        {
                            foreach (string valName in src.GetValueNames())
                            {
                                object val = src.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                                RegistryValueKind kind = src.GetValueKind(valName);
                                if (val != null) dst.SetValue(valName, val, kind);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static void Apply(string theme = null, string size = null)
        {
            if (!String.IsNullOrWhiteSpace(theme)) CurrentTheme = theme;
            if (!String.IsNullOrWhiteSpace(size)) CurrentSize = size;
            string dir = ResolveThemeDir(CurrentTheme, CurrentSize);
            if (!Directory.Exists(dir) || !HasCursors(dir)) return;

            BackupOriginalCursors();

            try
            {
                string schemeName = "BMFlow (" + CurrentTheme + ")";
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CursorsRegKey))
                {
                    if (key != null)
                    {
                        key.SetValue("", schemeName, RegistryValueKind.String);
                        key.SetValue("Scheme Source", 1, RegistryValueKind.DWord);
                        foreach (string role in RoleCandidates.Keys)
                        {
                            string file = FindCursorFile(dir, role);
                            if (!String.IsNullOrEmpty(file))
                            {
                                key.SetValue(role, file, RegistryValueKind.ExpandString);
                            }
                        }
                    }
                }

                foreach (var pair in RoleToOcr)
                {
                    string filePath = FindCursorFile(dir, pair.Key);
                    if (!String.IsNullOrEmpty(filePath))
                    {
                        IntPtr hCur = LoadCursorFromFile(filePath);
                        if (hCur != IntPtr.Zero)
                        {
                            IntPtr hCopy = CopyImage(hCur, IMAGE_CURSOR, 0, 0, 0);
                            if (hCopy != IntPtr.Zero)
                            {
                                SetSystemCursor(hCopy, pair.Value);
                            }
                            DestroyIcon(hCur);
                        }
                    }
                }

                SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                IsEnabled = true;
            }
            catch { }
        }

        public static void Restore()
        {
            try
            {
                using (RegistryKey backup = Registry.CurrentUser.OpenSubKey(BackupRegKey, false))
                using (RegistryKey dst = Registry.CurrentUser.CreateSubKey(CursorsRegKey))
                {
                    if (backup != null && dst != null && backup.ValueCount > 0)
                    {
                        foreach (string role in RoleCandidates.Keys)
                        {
                            dst.DeleteValue(role, false);
                        }
                        foreach (string valName in backup.GetValueNames())
                        {
                            object val = backup.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            RegistryValueKind kind = backup.GetValueKind(valName);
                            if (val != null) dst.SetValue(valName, val, kind);
                        }
                        try { Registry.CurrentUser.DeleteSubKeyTree(BackupRegKey, false); } catch { }
                    }
                    else if (dst != null)
                    {
                        foreach (string role in RoleCandidates.Keys)
                        {
                            dst.SetValue(role, "", RegistryValueKind.String);
                        }
                        dst.SetValue("", "Windows Padrão", RegistryValueKind.String);
                        dst.SetValue("Scheme Source", 0, RegistryValueKind.DWord);
                    }
                }

                SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                IsEnabled = false;
            }
            catch { }
        }

        public static ImageSource LoadCursorImage(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                lock (PreviewCache)
                {
                    ImageSource cached;
                    if (PreviewCache.TryGetValue(filePath, out cached)) return cached;
                }
                IntPtr hCur = LoadCursorFromFile(filePath);
                if (hCur == IntPtr.Zero) return null;
                try
                {
                    BitmapSource src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(hCur, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    lock (PreviewCache)
                    {
                        PreviewCache[filePath] = src;
                    }
                    return src;
                }
                finally
                {
                    DestroyIcon(hCur);
                }
            }
            catch
            {
                return null;
            }
        }
    }

}
