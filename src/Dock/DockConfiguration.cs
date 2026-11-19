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
        private void LoadSettings()
        {
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path) && File.Exists(LegacySettingsPath))
                {
                    string migrationDirectory = System.IO.Path.GetDirectoryName(path);
                    if (!Directory.Exists(migrationDirectory)) Directory.CreateDirectory(migrationDirectory);
                    File.Copy(LegacySettingsPath, path, false);
                }
                if (!File.Exists(path)) return;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    if (String.IsNullOrWhiteSpace(rawLine) || rawLine.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                    int separator = rawLine.IndexOf('=');
                    if (separator <= 0) continue;
                    values[rawLine.Substring(0, separator).Trim()] = rawLine.Substring(separator + 1).Trim();
                }

                string text;
                bool flag;
                double number;
                int integer;
                if (values.TryGetValue("Theme", out text) && !String.IsNullOrWhiteSpace(text)) dockTheme = text;
                if (values.TryGetValue("IconTileStyle", out text) && !String.IsNullOrWhiteSpace(text)) iconTileStyle = text;
                darkMode = dockTheme != "Nativo" && dockTheme != "Papel";
                if (values.TryGetValue("Opacity", out text) && Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                    dockOpacity = (byte)Math.Max(80, Math.Min(255, integer));
                if (values.TryGetValue("IconSize", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    dockIconSize = Math.Max(40, Math.Min(64, number));
                if (values.TryGetValue("DockPosition", out text) && IsValidDockPosition(text))
                    dockPosition = text;
                if (values.TryGetValue("TopBarEnabled", out text) && Boolean.TryParse(text, out flag)) showTopBar = flag;
                if (values.TryGetValue("BottomGap", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    dockBottomGap = Math.Max(0, Math.Min(20, number));
                if (values.TryGetValue("IndicatorWidth", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    runningIndicatorWidth = Math.Max(4, Math.Min(18, number));
                if (values.TryGetValue("AutoHide", out text) && Boolean.TryParse(text, out flag)) autoHideEnabled = flag;
                if (values.TryGetValue("KeepWindowsOffDock", out text) && Boolean.TryParse(text, out flag)) keepWindowsOffDock = flag;
                if (values.TryGetValue("HideTaskbar", out text) && Boolean.TryParse(text, out flag)) taskbarHidden = flag;
                if (values.TryGetValue("MacCursorEnabled", out text) && Boolean.TryParse(text, out flag)) macCursorEnabled = flag;
                if (values.TryGetValue("CursorTheme", out text) && !String.IsNullOrWhiteSpace(text)) cursorTheme = text;
                if (values.TryGetValue("MacCursorSize", out text) && !String.IsNullOrWhiteSpace(text)) macCursorSize = text;
                if (values.TryGetValue("Assistant", out text)) assistantName = text;
                if (values.TryGetValue("AssistantPosition", out text) &&
                    new[] { "Após Iniciar", "Após aplicativos", "Antes da bandeja" }.Contains(text)) assistantPosition = text;
                if (values.TryGetValue("ShowAssistant", out text) && Boolean.TryParse(text, out flag)) showAssistant = flag;
                if (values.TryGetValue("MediaPlayerEnabled", out text) && Boolean.TryParse(text, out flag)) showMediaPlayer = flag;
                if (values.TryGetValue("MediaPlayerHideInactive", out text) && Boolean.TryParse(text, out flag)) hideMediaWhenInactive = flag;
                if (values.TryGetValue("WidgetEnabled", out text) && Boolean.TryParse(text, out flag)) showDesktopWidget = flag;
                if (values.TryGetValue("WidgetX", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) widgetX = number;
                if (values.TryGetValue("WidgetY", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) widgetY = number;
                if (values.TryGetValue("WidgetLocked", out text) && Boolean.TryParse(text, out flag)) widgetLocked = flag;
                if (values.TryGetValue("WidgetSeconds", out text) && Boolean.TryParse(text, out flag)) widgetSeconds = flag;
                if (values.TryGetValue("Widget24Hour", out text) && Boolean.TryParse(text, out flag)) widget24Hour = flag;
                if (values.TryGetValue("WidgetLanguage", out text) && !String.IsNullOrWhiteSpace(text)) widgetLanguage = text;
                if (values.TryGetValue("WidgetTheme", out text) && !String.IsNullOrWhiteSpace(text)) widgetTheme = text;
                if (values.TryGetValue("WidgetScale", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) widgetScale = Math.Max(0.5, Math.Min(2.5, number));
                if (values.TryGetValue("ShowMascot", out text) && Boolean.TryParse(text, out flag)) showMascot = flag;
                if (values.TryGetValue("MascotType", out text) && !String.IsNullOrWhiteSpace(text)) mascotType = text;
                if (values.TryGetValue("MascotMode", out text) && !String.IsNullOrWhiteSpace(text)) mascotMode = (text == "Sleep") ? "PatrolRight" : text;
                if (values.TryGetValue("MascotScale", out text) && Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) mascotScale = Math.Max(0.6, Math.Min(3.0, number));
                if (values.TryGetValue("AppOrder", out text) && !String.IsNullOrWhiteSpace(text))
                {
                    appOrder.Clear();
                    foreach (string encoded in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        try { appOrder.Add(Uri.UnescapeDataString(encoded)); } catch { }
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                string path = SettingsPath;
                string directory = System.IO.Path.GetDirectoryName(path);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string temporary = path + ".tmp";
                File.WriteAllLines(temporary, new[]
                {
                    "# B.M Flow settings",
                    "Version=1",
                    "Theme=" + dockTheme,
                    "IconTileStyle=" + iconTileStyle,
                    "Opacity=" + dockOpacity.ToString(CultureInfo.InvariantCulture),
                    "IconSize=" + dockIconSize.ToString("0", CultureInfo.InvariantCulture),
                    "DockPosition=" + dockPosition,
                    "TopBarEnabled=" + showTopBar.ToString(),
                    "BottomGap=" + dockBottomGap.ToString("0", CultureInfo.InvariantCulture),
                    "IndicatorWidth=" + runningIndicatorWidth.ToString("0", CultureInfo.InvariantCulture),
                    "AutoHide=" + autoHideEnabled.ToString(),
                    "KeepWindowsOffDock=" + keepWindowsOffDock.ToString(),
                    "HideTaskbar=" + taskbarHidden.ToString(),
                    "MacCursorEnabled=" + macCursorEnabled.ToString(),
                    "CursorTheme=" + cursorTheme,
                    "MacCursorSize=" + macCursorSize,
                    "Assistant=" + assistantName,
                    "AssistantPosition=" + assistantPosition,
                    "ShowAssistant=" + showAssistant.ToString(),
                    "MediaPlayerEnabled=" + showMediaPlayer.ToString(),
                    "MediaPlayerHideInactive=" + hideMediaWhenInactive.ToString(),
                    "WidgetEnabled=" + showDesktopWidget.ToString(),
                    "WidgetX=" + (desktopWidget != null ? desktopWidget.Left.ToString("0", CultureInfo.InvariantCulture) : widgetX.ToString("0", CultureInfo.InvariantCulture)),
                    "WidgetY=" + (desktopWidget != null ? desktopWidget.Top.ToString("0", CultureInfo.InvariantCulture) : widgetY.ToString("0", CultureInfo.InvariantCulture)),
                    "WidgetLocked=" + (desktopWidget != null ? desktopWidget.IsLocked.ToString() : widgetLocked.ToString()),
                    "WidgetSeconds=" + (desktopWidget != null ? desktopWidget.ShowSeconds.ToString() : widgetSeconds.ToString()),
                    "Widget24Hour=" + (desktopWidget != null ? desktopWidget.Use24Hour.ToString() : widget24Hour.ToString()),
                    "WidgetLanguage=" + (desktopWidget != null ? desktopWidget.CurrentLanguage : widgetLanguage),
                    "WidgetTheme=" + (desktopWidget != null ? desktopWidget.CurrentTheme : widgetTheme),
                    "WidgetScale=" + (desktopWidget != null ? desktopWidget.CurrentScale.ToString("0.00", CultureInfo.InvariantCulture) : widgetScale.ToString("0.00", CultureInfo.InvariantCulture)),
                    "ShowMascot=" + showMascot.ToString(),
                    "MascotType=" + mascotType,
                    "MascotMode=" + mascotMode,
                    "MascotScale=" + mascotScale.ToString("0.00", CultureInfo.InvariantCulture),
                    "AppOrder=" + EncodeAppOrder()
                });
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch { }
        }

        private string EncodeAppOrder()
        {
            lock (appOrder) return String.Join(";", appOrder.Select(Uri.EscapeDataString));
        }

        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupRegistryName = "B.M Flow";
        private const string LegacyStartupRegistryName = "DockBar";

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
                {
                    string value = key.GetValue(StartupRegistryName) as string;
                    string executable = Process.GetCurrentProcess().MainModule.FileName;
                    if (!String.IsNullOrWhiteSpace(value) &&
                        String.Equals(value.Trim().Trim('"'), executable, StringComparison.OrdinalIgnoreCase)) return true;
                    string legacyValue = key.GetValue(LegacyStartupRegistryName) as string;
                    if (!String.IsNullOrWhiteSpace(legacyValue))
                    {
                        key.SetValue(StartupRegistryName, "\"" + executable + "\"", RegistryValueKind.String);
                        key.DeleteValue(LegacyStartupRegistryName, false);
                        return true;
                    }
                    return false;
                }
            }
            catch { return false; }
        }

        internal bool SetStartWithWindows(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
                {
                    if (enabled)
                    {
                        string executable = Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(StartupRegistryName, "\"" + executable + "\"", RegistryValueKind.String);
                    }
                    else key.DeleteValue(StartupRegistryName, false);
                    key.DeleteValue(LegacyStartupRegistryName, false);
                }
                startWithWindows = enabled;
                return true;
            }
            catch { return false; }
        }

        internal string CurrentTheme { get { return dockTheme; } }
        internal string CurrentIconTileStyle { get { return iconTileStyle; } }
        internal byte CurrentOpacity { get { return dockOpacity; } }
        internal double CurrentIconSize { get { return dockIconSize; } }
        internal double CurrentBottomGap { get { return dockBottomGap; } }
        internal double CurrentIndicatorWidth { get { return runningIndicatorWidth; } }
        internal bool CurrentAutoHide { get { return autoHideEnabled; } }
        internal bool CurrentKeepWindowsOff { get { return keepWindowsOffDock; } }
        internal bool CurrentHideTaskbar { get { return taskbarHidden; } }
        internal bool CurrentStartWithWindows { get { return startWithWindows; } }
        internal bool CurrentMacCursorEnabled { get { return macCursorEnabled; } }
        internal string CurrentCursorTheme { get { return cursorTheme; } }
        internal string CurrentMacCursorSize { get { return macCursorSize; } }
        internal string CurrentAssistantName { get { return assistantName; } }
        internal string CurrentAssistantPosition { get { return assistantPosition; } }
        internal bool CurrentShowAssistant { get { return showAssistant; } }
        internal IEnumerable<string> AssistantChoices
        {
            get
            {
                var names = installedAssistants.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (names.Count == 0) names.Add("Nenhum assistente detectado");
                return names;
            }
        }

        internal void SetAssistantName(string name)
        {
            if (!installedAssistants.Any(item => String.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            assistantName = name;
            UpdateAssistantButton();
            SaveSettings();
        }

        internal void SetAssistantPosition(string position)
        {
            if (!new[] { "Após Iniciar", "Após aplicativos", "Antes da bandeja" }.Contains(position)) return;
            assistantPosition = position;
            PositionAssistantGroup();
            SaveSettings();
        }

        internal void SetShowAssistant(bool show)
        {
            if (showAssistant == show) return;
            showAssistant = show;
            PositionAssistantGroup();
            SaveSettings();
        }

        internal bool CurrentShowDesktopWidget { get { return showDesktopWidget; } }
        internal bool DesktopWidgetLocked { get { return desktopWidget != null ? desktopWidget.IsLocked : widgetLocked; } }
        internal bool DesktopWidgetSeconds { get { return desktopWidget != null ? desktopWidget.ShowSeconds : widgetSeconds; } }
        internal bool DesktopWidget24Hour { get { return desktopWidget != null ? desktopWidget.Use24Hour : widget24Hour; } }
        internal string DesktopWidgetLanguage { get { return desktopWidget != null ? desktopWidget.CurrentLanguage : widgetLanguage; } }
        internal string DesktopWidgetTheme { get { return desktopWidget != null ? desktopWidget.CurrentTheme : widgetTheme; } }
        internal double DesktopWidgetScale { get { return desktopWidget != null ? desktopWidget.CurrentScale : widgetScale; } }
        internal string DesktopWidgetScaleLabel
        {
            get
            {
                double s = DesktopWidgetScale;
                if (Math.Abs(s - 0.8) < 0.05) return "80% (Pequeno)";
                if (Math.Abs(s - 1.25) < 0.05) return "125% (Grande)";
                if (Math.Abs(s - 1.5) < 0.05) return "150% (Extra Grande)";
                return "100% (Normal)";
            }
        }

        internal void SetShowDesktopWidget(bool show)
        {
            showDesktopWidget = show;
            UpdateDesktopWidgetVisibility();
            SaveSettings();
        }

        internal void SetDesktopWidgetLocked(bool locked)
        {
            widgetLocked = locked;
            if (desktopWidget != null) desktopWidget.SetLocked(locked);
            else SaveSettings();
        }

        internal void SetDesktopWidgetSeconds(bool seconds)
        {
            widgetSeconds = seconds;
            if (desktopWidget != null) desktopWidget.SetShowSeconds(seconds);
            else SaveSettings();
        }

        internal void SetDesktopWidget24Hour(bool h24)
        {
            widget24Hour = h24;
            if (desktopWidget != null) desktopWidget.Set24Hour(h24);
            else SaveSettings();
        }

        internal void SetDesktopWidgetLanguage(string lang)
        {
            widgetLanguage = lang;
            if (desktopWidget != null) desktopWidget.SetLanguage(lang);
            else SaveSettings();
        }

        internal void SetDesktopWidgetTheme(string theme)
        {
            widgetTheme = theme;
            if (desktopWidget != null) desktopWidget.SetTheme(theme);
            else SaveSettings();
        }

        internal void SetDesktopWidgetScaleFromLabel(string label)
        {
            double scale = 1.0;
            if (label.StartsWith("80%")) scale = 0.8;
            else if (label.StartsWith("125%")) scale = 1.25;
            else if (label.StartsWith("150%")) scale = 1.5;
            widgetScale = scale;
            if (desktopWidget != null) desktopWidget.SetScale(scale);
            else SaveSettings();
        }

        internal void CenterDesktopWidget()
        {
            if (desktopWidget != null) desktopWidget.CenterOnScreen();
        }

        internal void SetWidgetPosition(double x, double y)
        {
            widgetX = x;
            widgetY = y;
            SaveSettings();
        }

        internal void NotifyWidgetChanged()
        {
            if (desktopWidget != null)
            {
                widgetLocked = desktopWidget.IsLocked;
                widgetSeconds = desktopWidget.ShowSeconds;
                widget24Hour = desktopWidget.Use24Hour;
                widgetLanguage = desktopWidget.CurrentLanguage;
                widgetTheme = desktopWidget.CurrentTheme;
                widgetScale = desktopWidget.CurrentScale;
            }
            SaveSettings();
        }

        internal double DockShellActualWidth { get { return dockShell != null ? dockShell.ActualWidth : 0; } }
        internal double DockShellActualHeight { get { return dockShell != null ? dockShell.ActualHeight : 0; } }

        internal bool ShowMascot { get { return showMascot; } }
        internal string MascotType { get { return mascotType; } }
        internal string MascotMode { get { return mascotMode; } }
        internal double MascotScale { get { return mascotScale; } }

        internal string MascotTypeDisplay
        {
            get
            {
                switch (mascotType)
                {
                    case "Fox": return "Raposinha (Pixel Fox)";
                    case "CatOrange": return "Gatinho Tigrão (Laranja)";
                    case "CatWhite": return "Gatinho Neve (Branco)";
                    case "CatTuxedo": return "Gatinho Frajola (Tuxedo)";
                    case "Dog": return "Cachorrinho Caramelo";
                    case "Duck": return "Gansinho / Pato";
                    default: return "Gatinho Meia-Noite (Preto)";
                }
            }
        }

        internal string MascotModeDisplay
        {
            get
            {
                switch (mascotMode)
                {
                    case "Wander": return "Passear pela Barra toda";
                    case "RunCat": return "Ritmo pela CPU (RunCat)";
                    default: return "Canto Direito (Acima da Lixeira)";
                }
            }
        }

        internal string MascotScaleLabel
        {
            get
            {
                if (mascotScale <= 0.85) return "80% (Pequeno)";
                if (mascotScale >= 2.05) return "220% (Gigante)";
                if (mascotScale >= 1.7) return "180% (Extra Grande)";
                if (mascotScale >= 1.4) return "150% (Grande)";
                if (mascotScale >= 1.15) return "125% (Médio)";
                return "100% (Padrão)";
            }
        }

        internal void SetShowMascot(bool show)
        {
            if (show && !IsMascotPositionSupported) show = false;
            showMascot = show;
            if (dockMascot != null) dockMascot.SetEnabled(show);
            if (mascotMenuItem != null)
            {
                mascotMenuItem.IsChecked = show;
                mascotMenuItem.IsEnabled = IsMascotPositionSupported;
            }
            SaveSettings();
        }

        private void EnforceMascotPositionPolicy()
        {
            bool supported = IsMascotPositionSupported;
            bool disabledNow = !supported && showMascot;
            if (disabledNow) showMascot = false;
            if (dockMascot != null) dockMascot.SetEnabled(showMascot && supported);
            if (mascotMenuItem != null)
            {
                mascotMenuItem.IsChecked = showMascot && supported;
                mascotMenuItem.IsEnabled = supported;
                mascotMenuItem.ToolTip = supported ? null : "O mascote está disponível somente com a Dock na borda inferior.";
            }
            if (disabledNow) SaveSettings();
        }

        internal void SetMascotType(string type)
        {
            mascotType = type;
            if (dockMascot != null) dockMascot.SetType(type);
            SaveSettings();
        }

        internal void SetMascotTypeFromDisplay(string display)
        {
            if (display.Contains("Raposinha") || display.Contains("Fox")) SetMascotType("Fox");
            else if (display.Contains("Laranja") || display.Contains("Tigrão")) SetMascotType("CatOrange");
            else if (display.Contains("Branco") || display.Contains("Neve")) SetMascotType("CatWhite");
            else if (display.Contains("Frajola") || display.Contains("Tuxedo")) SetMascotType("CatTuxedo");
            else if (display.Contains("Cachorro") || display.Contains("Caramelo")) SetMascotType("Dog");
            else if (display.Contains("Ganso") || display.Contains("Pato")) SetMascotType("Duck");
            else SetMascotType("CatBlack");
        }

        internal void SetMascotMode(string mode)
        {
            mascotMode = mode;
            if (dockMascot != null) dockMascot.SetMode(mode);
            SaveSettings();
        }

        internal void SetMascotModeFromDisplay(string display)
        {
            if (display.Contains("Passear")) SetMascotMode("Wander");
            else if (display.Contains("CPU") || display.Contains("RunCat")) SetMascotMode("RunCat");
            else SetMascotMode("PatrolRight");
        }

        internal void MascotTakeNap()
        {
            if (dockMascot != null) dockMascot.TakeNap();
        }

        internal void SetMascotScale(double scale)
        {
            mascotScale = Math.Max(0.6, Math.Min(3.0, scale));
            if (dockMascot != null) dockMascot.SetScale(mascotScale);
            SaveSettings();
        }

        internal void SetMascotScaleFromLabel(string label)
        {
            if (label.StartsWith("80%")) SetMascotScale(0.8);
            else if (label.StartsWith("125%")) SetMascotScale(1.25);
            else if (label.StartsWith("150%")) SetMascotScale(1.5);
            else if (label.StartsWith("180%")) SetMascotScale(1.8);
            else if (label.StartsWith("220%")) SetMascotScale(2.2);
            else SetMascotScale(1.0);
        }

        private void UpdateDesktopWidgetVisibility()
        {
            if (showDesktopWidget)
            {
                if (desktopWidget == null)
                {
                    try
                    {
                        desktopWidget = new DesktopWidgetWindow(this, widgetX, widgetY, widgetLocked, widgetSeconds, widget24Hour, widgetLanguage, widgetTheme, widgetScale);
                        desktopWidget.Show();
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    desktopWidget.Show();
                }
            }
            else
            {
                if (desktopWidget != null)
                {
                    desktopWidget.Hide();
                }
            }
        }

        internal void SelectTheme(string name)
        {
            dockTheme = name;
            darkMode = name != "Nativo" && name != "Papel";
            ApplyTheme();
            SaveSettings();
        }

        internal void SetIconTileStyle(string style)
        {
            iconTileStyle = style;
            foreach (Border tile in themedTiles) ApplyTileTheme(tile);
            SaveSettings();
        }

        internal void SetDockOpacity(double value)
        {
            dockOpacity = (byte)Math.Max(80, Math.Min(255, (int)Math.Round(value)));
            ApplyTheme();
            SaveSettings();
        }

        internal void SetDockIconSize(double value)
        {
            dockIconSize = Math.Max(40, Math.Min(64, Math.Round(value)));
            ApplyDockSizing();
            SaveSettings();
        }

        internal void SetDockBottomGap(double value)
        {
            dockBottomGap = Math.Max(0, Math.Min(20, Math.Round(value)));
            UpdateAppBarRegistration();
            SchedulePositionDock();
            SaveSettings();
        }

        internal void SetRunningIndicatorWidth(double value)
        {
            runningIndicatorWidth = Math.Max(4, Math.Min(18, Math.Round(value)));
            foreach (Border indicator in runningDots.Values) indicator.Width = runningIndicatorWidth;
            if (taskItems != null)
                foreach (StackPanel tile in taskItems.Children.OfType<StackPanel>())
                    foreach (Border indicator in tile.Children.OfType<Border>())
                        if (String.Equals(indicator.Tag as string, "RunningIndicator", StringComparison.Ordinal))
                            indicator.Width = runningIndicatorWidth;
            SaveSettings();
        }

        internal void SetAutoHideEnabled(bool enabled)
        {
            autoHideEnabled = enabled;
            if (keepWindowsOffItem != null) keepWindowsOffItem.IsEnabled = !autoHideEnabled;
            UpdateAppBarRegistration();
            SchedulePositionDock();
            if (autoHideEnabled) ScheduleAutoHide(); else ShowDockAnimated();
            SaveSettings();
        }

        internal void SetKeepWindowsOffDock(bool enabled)
        {
            keepWindowsOffDock = enabled;
            UpdateAppBarRegistration();
            SchedulePositionDock();
            SaveSettings();
        }

        internal void SetTaskbarHidden(bool hidden)
        {
            taskbarHidden = hidden;
            UpdateTaskbarVisibilityGuard();
            if (taskbarHidden) ApplyCompleteTaskbarHide(); else RestoreOriginalTaskbarState();
            UpdateAppBarRegistration();
            PositionDock();
            SaveSettings();
        }

        internal void SetMacCursorEnabled(bool enabled)
        {
            macCursorEnabled = enabled;
            MacCursorService.IsEnabled = enabled;
            if (enabled)
            {
                MacCursorService.Apply(cursorTheme, macCursorSize);
            }
            else
            {
                MacCursorService.Restore();
            }
            SaveSettings();
        }

        internal void SetCursorTheme(string theme)
        {
            cursorTheme = theme;
            MacCursorService.CurrentTheme = theme;
            if (macCursorEnabled)
            {
                MacCursorService.Apply(cursorTheme, macCursorSize);
            }
            SaveSettings();
        }

        internal void SetMacCursorSize(string size)
        {
            macCursorSize = size;
            MacCursorService.CurrentSize = size;
            if (macCursorEnabled)
            {
                MacCursorService.Apply(cursorTheme, macCursorSize);
            }
            SaveSettings();
        }

        public void OpenSettingsWindow(string initialPage = null)
        {
            autoHideTimer.Stop();
            ShowDockAnimated();
            if (settingsWindow != null)
            {
                if (!String.IsNullOrWhiteSpace(initialPage)) settingsWindow.ShowSettingsPageExternal(initialPage);
                settingsWindow.Activate();
                return;
            }
            settingsWindow = new DockSettingsWindow(this, initialPage ?? "general");
            settingsWindow.Owner = this;
            PositionSettingsWindowAboveDock(settingsWindow);
            settingsWindow.Closed += delegate
            {
                settingsWindow = null;
                ScheduleAutoHide();
                ScheduleMemoryTrim();
            };
            settingsWindow.Show();
        }

        internal void ScheduleMemoryTrim(int delayMilliseconds = 900)
        {
            var previous = Interlocked.Exchange(ref memoryTrimTimer, null);
            if (previous != null) previous.Dispose();
            memoryTrimTimer = new System.Threading.Timer(delegate
            {
                try
                {
                    if (isClosing) return;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                    using (Process process = Process.GetCurrentProcess())
                        EmptyWorkingSet(process.Handle);
                }
                catch { }
                finally
                {
                    var completed = Interlocked.Exchange(ref memoryTrimTimer, null);
                    if (completed != null) completed.Dispose();
                }
            }, null, Math.Max(250, delayMilliseconds), Timeout.Infinite);
        }

        private void PositionSettingsWindowAboveDock(Window window)
        {
            if (window == null) return;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            Rect area = SystemParameters.WorkArea;
            if (IsDockTop)
            {
                window.Left = area.Left + Math.Max(8, (area.Width - window.Width) / 2);
                window.Top = Math.Min(area.Bottom - window.Height - 8, Top + ActualHeight + 10);
            }
            else if (IsDockLeft)
            {
                window.Left = Math.Min(area.Right - window.Width - 8, Left + ActualWidth + 10);
                window.Top = area.Top + Math.Max(8, (area.Height - window.Height) / 2);
            }
            else if (IsDockRight)
            {
                window.Left = Math.Max(area.Left + 8, Left - window.Width - 10);
                window.Top = area.Top + Math.Max(8, (area.Height - window.Height) / 2);
            }
            else
            {
                double dockTop = RevealedDockTop();
                double availableHeight = Math.Max(0, dockTop - area.Top - 18);
                if (availableHeight < window.Height && availableHeight >= window.MinHeight)
                    window.Height = availableHeight;
                window.Left = area.Left + Math.Max(8, (area.Width - window.Width) / 2);
                window.Top = Math.Max(area.Top + 8, dockTop - window.Height - 10);
            }
        }

        private void ApplyWindows11MenuStyle(ContextMenu menu)
        {
            if (menu == null) return;
            bool isDark = darkMode;
            Color menuBgColor = isDark ? Color.FromArgb(238, 35, 36, 41) : Color.FromArgb(238, 245, 248, 253);
            Color menuBorderColor = isDark ? Color.FromArgb(145, 91, 94, 104) : Color.FromArgb(70, 50, 78, 118);
            Brush textBrush = isDark ? new SolidColorBrush(Color.FromRgb(230, 236, 248)) : new SolidColorBrush(Color.FromRgb(24, 35, 52));
            Brush secondaryTextBrush = isDark ? new SolidColorBrush(Color.FromRgb(125, 137, 160)) : new SolidColorBrush(Color.FromRgb(112, 121, 137));
            Brush hoverBrush = isDark ? new SolidColorBrush(Color.FromArgb(210, 25, 58, 104)) : new SolidColorBrush(Color.FromArgb(48, 64, 139, 230));
            Brush separatorBrush = isDark ? new SolidColorBrush(Color.FromArgb(105, 112, 132, 165)) : new SolidColorBrush(Color.FromArgb(65, 76, 97, 126));

            menu.Background = new SolidColorBrush(menuBgColor);
            menu.Foreground = textBrush;
            menu.BorderBrush = new SolidColorBrush(menuBorderColor);
            menu.BorderThickness = new Thickness(1);
            menu.Padding = new Thickness(8);
            menu.FontFamily = new FontFamily("Segoe UI");
            menu.FontSize = 13.5;
            menu.SnapsToDevicePixels = true;
            menu.HasDropShadow = true;

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            border.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 7,
                Direction = 270,
                Opacity = 0.58,
                Color = Color.FromRgb(0, 0, 0)
            });
            border.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            menu.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = border };

            var itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, textBrush));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 38.0));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1, 1.5, 1, 1.5)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new EventSetter(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnFluentSubmenuOpened)));

            var itemRoot = new FrameworkElementFactory(typeof(Border));
            itemRoot.Name = "ItemBorder";
            itemRoot.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            itemRoot.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
            itemRoot.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));
            itemRoot.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var itemPanel = new FrameworkElementFactory(typeof(DockPanel));
            itemPanel.SetValue(DockPanel.LastChildFillProperty, true);

            var leadingSlot = new FrameworkElementFactory(typeof(Grid));
            leadingSlot.SetValue(FrameworkElement.WidthProperty, 30.0);
            leadingSlot.SetValue(FrameworkElement.HeightProperty, 22.0);
            leadingSlot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            leadingSlot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            leadingSlot.SetValue(DockPanel.DockProperty, Dock.Left);

            var iconPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            iconPresenter.Name = "IconPresenter";
            iconPresenter.SetValue(ContentPresenter.ContentSourceProperty, "Icon");
            iconPresenter.SetValue(FrameworkElement.WidthProperty, 20.0);
            iconPresenter.SetValue(FrameworkElement.HeightProperty, 20.0);
            iconPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            iconPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            leadingSlot.AppendChild(iconPresenter);

            var checkBox = new FrameworkElementFactory(typeof(Border));
            checkBox.Name = "CheckBoxVisual";
            checkBox.SetValue(FrameworkElement.WidthProperty, 18.0);
            checkBox.SetValue(FrameworkElement.HeightProperty, 18.0);
            checkBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBox.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            checkBox.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(90, 72, 84, 108)));
            checkBox.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(120, 132, 149, 180)));
            checkBox.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            checkBox.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

            var check = new FrameworkElementFactory(typeof(TextBlock));
            check.Name = "CheckMark";
            check.SetValue(TextBlock.TextProperty, "✓");
            check.SetValue(TextBlock.FontSizeProperty, 12.0);
            check.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            check.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBox.AppendChild(check);
            leadingSlot.AppendChild(checkBox);
            itemPanel.AppendChild(leadingSlot);

            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.Name = "SubmenuArrow";
            arrow.SetValue(TextBlock.TextProperty, "\uE76C");
            arrow.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
            arrow.SetValue(TextBlock.FontSizeProperty, 10.0);
            arrow.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(215, 226, 244)));
            arrow.SetValue(FrameworkElement.WidthProperty, 18.0);
            arrow.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            arrow.SetValue(DockPanel.DockProperty, Dock.Right);
            itemPanel.AppendChild(arrow);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            itemPanel.AppendChild(content);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
            popup.SetValue(Popup.HorizontalOffsetProperty, 5.0);
            popup.SetValue(Popup.VerticalOffsetProperty, -8.0);
            popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsSubmenuOpen") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent, Mode = System.Windows.Data.BindingMode.TwoWay });
            popup.SetBinding(Popup.PlacementTargetProperty, new System.Windows.Data.Binding { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

            var submenuBorder = new FrameworkElementFactory(typeof(Border));
            submenuBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(menuBgColor));
            submenuBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(menuBorderColor));
            submenuBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            submenuBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            submenuBorder.SetValue(Border.PaddingProperty, new Thickness(8));
            submenuBorder.SetValue(FrameworkElement.MinWidthProperty, 225.0);
            submenuBorder.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 7,
                Direction = 270,
                Opacity = 0.58,
                Color = Color.FromRgb(0, 0, 0)
            });
            var submenuItems = new FrameworkElementFactory(typeof(StackPanel));
            submenuItems.SetValue(Panel.IsItemsHostProperty, true);
            submenuItems.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
            submenuBorder.AppendChild(submenuItems);
            popup.AppendChild(submenuBorder);
            itemPanel.AppendChild(popup);

            itemRoot.AppendChild(itemPanel);

            var itemTemplate = new ControlTemplate(typeof(MenuItem)) { VisualTree = itemRoot };
            var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "ItemBorder"));
            itemTemplate.Triggers.Add(highlighted);

            var checkableTrigger = new Trigger { Property = MenuItem.IsCheckableProperty, Value = true };
            checkableTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckBoxVisual"));
            checkableTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "IconPresenter"));
            itemTemplate.Triggers.Add(checkableTrigger);

            var checkedTrigger = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(42, 139, 255)), "CheckBoxVisual"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(80, 169, 255)), "CheckBoxVisual"));
            itemTemplate.Triggers.Add(checkedTrigger);

            var submenuTrigger = new Trigger { Property = MenuItem.HasItemsProperty, Value = true };
            submenuTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SubmenuArrow"));
            itemTemplate.Triggers.Add(submenuTrigger);

            var noIconTrigger = new Trigger { Property = MenuItem.IconProperty, Value = null };
            noIconTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "IconPresenter"));
            itemTemplate.Triggers.Add(noIconTrigger);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .55));
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, secondaryTextBrush));
            itemStyle.Triggers.Add(disabled);

            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
            menu.Resources[typeof(MenuItem)] = itemStyle;

            var separatorLine = new FrameworkElementFactory(typeof(Border));
            separatorLine.SetValue(FrameworkElement.HeightProperty, 1.0);
            separatorLine.SetValue(Border.BackgroundProperty, separatorBrush);
            var separatorTemplate = new ControlTemplate(typeof(System.Windows.Controls.Separator)) { VisualTree = separatorLine };
            var separatorStyle = new Style(typeof(System.Windows.Controls.Separator));
            separatorStyle.Setters.Add(new Setter(Control.TemplateProperty, separatorTemplate));
            separatorStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(12, 6, 12, 6)));
            menu.Resources[typeof(System.Windows.Controls.Separator)] = separatorStyle;
            menu.Resources[SystemColors.HighlightBrushKey] = hoverBrush;
            menu.Resources[SystemColors.HighlightTextBrushKey] = textBrush;
            menu.Resources[SystemColors.MenuBrushKey] = new SolidColorBrush(menuBgColor);
            menu.Resources[SystemColors.MenuTextBrushKey] = textBrush;
        }

        private static bool ApplyBackdropToWindowHandle(IntPtr handle, HwndSource source, bool isDark)
        {
            if (handle == IntPtr.Zero) return false;
            try
            {
                if (source != null && source.CompositionTarget != null)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                }

                int cornerPreference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, 33, ref cornerPreference, Marshal.SizeOf(typeof(int)));

                int darkVal = isDark ? 1 : 0;
                DwmSetWindowAttribute(handle, 20, ref darkVal, Marshal.SizeOf(typeof(int)));

                int backdropType = 3; // DWMSBT_TRANSIENTWINDOW (Acrylic)
                DwmSetWindowAttribute(handle, 38, ref backdropType, Marshal.SizeOf(typeof(int)));

                int tint = isDark ? unchecked((int)0xD02B2927) : unchecked((int)0xD8F8F5F0);
                ApplyAcrylicToHandle(handle, tint);

                return true;
            }
            catch { return false; }
        }

        private void OnFluentSubmenuOpened(object sender, RoutedEventArgs args)
        {
            var item = sender as MenuItem;
            if (item == null) return;
            int attempts = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            timer.Tick += delegate
            {
                attempts++;
                bool done = false;
                try
                {
                    var popup = item.Template.FindName("PART_Popup", item) as Popup;
                    Visual visual = popup == null ? null : popup.Child as Visual;
                    HwndSource source = visual == null ? null : PresentationSource.FromVisual(visual) as HwndSource;
                    if (source != null && source.Handle != IntPtr.Zero)
                    {
                        done = ApplyBackdropToWindowHandle(source.Handle, source, darkMode);
                    }
                }
                catch { }
                if (done || attempts >= 8 || !item.IsSubmenuOpen) timer.Stop();
            };
            try
            {
                var popup = item.Template.FindName("PART_Popup", item) as Popup;
                Visual visual = popup == null ? null : popup.Child as Visual;
                HwndSource immediate = visual == null ? null : PresentationSource.FromVisual(visual) as HwndSource;
                if (immediate != null && immediate.Handle != IntPtr.Zero)
                {
                    ApplyBackdropToWindowHandle(immediate.Handle, immediate, darkMode);
                    return;
                }
            }
            catch { }
            timer.Start();
        }

        private void ApplyContextMenuBackdrop(ContextMenu menu)
        {
            if (menu == null) return;
            int attempts = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            timer.Tick += delegate
            {
                attempts++;
                bool done = false;
                try
                {
                    HwndSource source = PresentationSource.FromVisual(menu) as HwndSource;
                    if (source != null && source.Handle != IntPtr.Zero)
                    {
                        done = ApplyBackdropToWindowHandle(source.Handle, source, darkMode);
                    }
                }
                catch { }
                if (done || attempts >= 8 || !menu.IsOpen) timer.Stop();
            };
            try
            {
                HwndSource immediate = PresentationSource.FromVisual(menu) as HwndSource;
                if (immediate != null && immediate.Handle != IntPtr.Zero)
                {
                    ApplyBackdropToWindowHandle(immediate.Handle, immediate, darkMode);
                    return;
                }
            }
            catch { }
            timer.Start();
        }

        private void ConfigureDockContextMenu(ContextMenu menu, FrameworkElement target = null)
        {
            ApplyWindows11MenuStyle(menu);
            menu.PlacementTarget = target ?? dockShell;
            menu.Placement = PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = delegate(Size popupSize, Size targetSize, Point offset)
            {
                if (IsDockLeft)
                    return new[] { new CustomPopupPlacement(new Point(targetSize.Width + 10, (targetSize.Height - popupSize.Height) / 2.0), PopupPrimaryAxis.Vertical) };
                if (IsDockRight)
                    return new[] { new CustomPopupPlacement(new Point(-popupSize.Width - 10, (targetSize.Height - popupSize.Height) / 2.0), PopupPrimaryAxis.Vertical) };
                double left;
                if (target != null && target != dockShell)
                {
                    left = (targetSize.Width - popupSize.Width) / 2.0;
                }
                else
                {
                    double pointerX;
                    try { pointerX = Mouse.GetPosition(dockShell).X; }
                    catch { pointerX = targetSize.Width / 2.0; }
                    left = pointerX - popupSize.Width / 2.0;
                }
                if (dockShell != null)
                {
                    left = Math.Max(4, Math.Min(left, dockShell.ActualWidth - popupSize.Width - 4));
                }
                double top = IsDockTop ? targetSize.Height + 10 : -popupSize.Height - 10;
                return new[] { new CustomPopupPlacement(new Point(left, top), PopupPrimaryAxis.Horizontal) };
            };
            menu.Opened += delegate
            {
                activeContextMenu = menu;
                autoHideTimer.Stop();
                ShowDockAnimated();
                HidePreview();
                ApplyContextMenuBackdrop(menu);
                ScheduleDockContextMenuPosition(menu);
            };
            menu.Closed += delegate
            {
                if (ReferenceEquals(activeContextMenu, menu)) activeContextMenu = null;
                ScheduleAutoHide();
            };
        }

        internal void ConfigureMascotContextMenu(ContextMenu menu, FrameworkElement target)
        {
            ConfigureDockContextMenu(menu, target);
        }

        private void ScheduleDockContextMenuPosition(ContextMenu menu)
        {
            int attempts = 0;
            var positionTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(14)
            };
            positionTimer.Tick += delegate
            {
                attempts++;
                TryPositionDockContextMenu(menu);
                if (attempts >= 6 || menu == null || !menu.IsOpen) positionTimer.Stop();
            };
            // Uma primeira tentativa imediata reduz a piscada; as seguintes
            // vencem o reposicionamento tardio feito internamente pelo WPF.
            TryPositionDockContextMenu(menu);
            positionTimer.Start();
        }

        private bool TryPositionDockContextMenu(ContextMenu menu)
        {
            if (menu == null || !menu.IsOpen || dockShell == null) return false;
            try
            {
                HwndSource source = PresentationSource.FromVisual(menu) as HwndSource;
                if (source == null || source.Handle == IntPtr.Zero) return false;
                NativeRect bounds;
                if (!GetWindowRect(source.Handle, out bounds)) return false;
                int width = bounds.Right - bounds.Left;
                int height = bounds.Bottom - bounds.Top;
                if (width <= 0 || height <= 0) return false;

                Point dockTop = dockShell.PointToScreen(new Point(0, 0));
                CursorPoint cursor;
                if (!GetCursorPos(out cursor)) cursor = new CursorPoint { X = (int)dockTop.X + width / 2, Y = (int)dockTop.Y };

                int centerX = cursor.X;
                int centerY = cursor.Y;
                var target = menu.PlacementTarget as FrameworkElement;
                if (target != null && target != dockShell && target.IsLoaded && target.ActualWidth > 0 && target.ActualHeight > 0)
                {
                    try
                    {
                        Point targetScreen = target.PointToScreen(new Point(target.ActualWidth / 2.0, target.ActualHeight / 2.0));
                        centerX = (int)Math.Round(targetScreen.X);
                        centerY = (int)Math.Round(targetScreen.Y);
                    }
                    catch { }
                }

                Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point(cursor.X, cursor.Y));
                int left;
                int top;
                if (IsDockLeft)
                {
                    left = (int)Math.Round(dockTop.X + dockShell.ActualWidth + 10);
                    top = centerY - height / 2;
                }
                else if (IsDockRight)
                {
                    left = (int)Math.Round(dockTop.X) - width - 10;
                    top = centerY - height / 2;
                }
                else
                {
                    left = centerX - width / 2;
                    top = IsDockTop
                        ? (int)Math.Round(dockTop.Y + dockShell.ActualHeight + 10)
                        : (int)Math.Round(dockTop.Y) - height - 10;
                }
                left = Math.Max(screen.Bounds.Left + 6, Math.Min(left, screen.Bounds.Right - width - 6));
                top = Math.Max(screen.Bounds.Top + 6, Math.Min(top, screen.Bounds.Bottom - height - 6));
                return SetWindowPos(source.Handle, new IntPtr(-1), left, top, 0, 0, 0x0001 | 0x0010);
            }
            catch { return false; }
        }

        private static void ApplyAcrylicToHandle(IntPtr handle, int tint)
        {
            if (handle == IntPtr.Zero) return;
            var accent = new AccentPolicy { AccentState = 4, AccentFlags = 2, GradientColor = tint };
            int size = Marshal.SizeOf(accent);
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pointer, false);
                var data = new WindowCompositionAttributeData { Attribute = 19, SizeOfData = size, Data = pointer };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        private MenuItem SliderMenuItem(string label, string suffix, double minimum, double maximum, double value, Action<double> changed)
        {
            var panel = new StackPanel { Width = 250, Margin = new Thickness(5, 4, 5, 5) };
            var caption = new Grid();
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            caption.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var valueText = new TextBlock { Text = Math.Round(value) + " " + suffix, Opacity = .72, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(valueText, 1);
            caption.Children.Add(valueText);
            panel.Children.Add(caption);
            var slider = new Slider
            {
                Minimum = minimum, Maximum = maximum, Value = value,
                TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 9, 0, 0),
                IsMoveToPointEnabled = true
            };
            slider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> args)
            {
                valueText.Text = Math.Round(args.NewValue) + " " + suffix;
                changed(args.NewValue);
            };
            panel.Children.Add(slider);
            return new MenuItem { Header = panel, StaysOpenOnClick = true, Tag = label };
        }

        private void SyncDockMenu()
        {
            if (dockMenu == null) return;
            foreach (MenuItem item in dockMenu.Items.OfType<MenuItem>())
            {
                string header = item.Header as string;
                if (header == "Espaços de Trabalho" && workspaceManager != null)
                {
                    item.Items.Clear();
                    for (int i = 0; i < workspaceManager.Workspaces.Count; i++)
                    {
                        int idx = i;
                        var ws = workspaceManager.Workspaces[i];
                        bool isActive = i == workspaceManager.ActiveIndex;
                        var wsItem = new MenuItem
                        {
                            Header = (isActive ? "✓  " : "    ") + (ws.Icon ?? "💼") + " " + ws.Name + "  (Ctrl+Alt+" + (i + 1) + ")",
                            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
                        };
                        wsItem.Click += delegate { SwitchToWorkspace(idx); };
                        item.Items.Add(wsItem);
                    }
                    item.Items.Add(new System.Windows.Controls.Separator());
                    var activeWs = workspaceManager.ActiveWorkspace;
                    string actName = activeWs != null ? activeWs.Name : "Espaço";
                    var launchAll = new MenuItem
                    {
                        Header = "🚀 Abrir / Restaurar todos os apps (" + actName + ")"
                    };
                    launchAll.Click += delegate { LaunchOrRestoreWorkspaceApps(activeWs); };
                    item.Items.Add(launchAll);

                    var manageItem = new MenuItem
                    {
                        Header = "⚙️ Gerenciar espaços…"
                    };
                    manageItem.Click += delegate { OpenSettingsWindow("workspaces"); };
                    item.Items.Add(manageItem);
                }
                else if (header == "Tema")
                {
                    foreach (MenuItem child in item.Items.OfType<MenuItem>()) child.IsChecked = String.Equals(child.Tag as string, dockTheme, StringComparison.Ordinal);
                }
                else if (header == "Transparência")
                {
                    foreach (MenuItem child in item.Items.OfType<MenuItem>()) child.IsChecked = child.Tag is byte && (byte)child.Tag == dockOpacity;
                }
                else if (header == "Tamanho e posição")
                {
                    foreach (MenuItem child in item.Items.OfType<MenuItem>())
                    {
                        var panel = child.Header as StackPanel;
                        var slider = panel == null ? null : panel.Children.OfType<Slider>().FirstOrDefault();
                        if (slider == null) continue;
                        string key = child.Tag as string;
                        slider.Value = key == "Tamanho dos ícones" ? dockIconSize
                            : key == "Largura do indicador" ? runningIndicatorWidth
                            : dockBottomGap;
                    }
                }
                else if (header == "Ocultar automaticamente") item.IsChecked = autoHideEnabled;
                else if (header == "Manter janelas acima da Dock") { item.IsChecked = keepWindowsOffDock; item.IsEnabled = !autoHideEnabled; }
                else if (header == "Ocultar barra de tarefas original") item.IsChecked = taskbarHidden;
                else if (header == "Exibir assistente") item.IsChecked = showAssistant;
                else if (header == "Exibir widget na área de trabalho") item.IsChecked = showDesktopWidget;
                else if (header == "Exibir mascote na barra")
                {
                    item.IsChecked = showMascot && IsMascotPositionSupported;
                    item.IsEnabled = IsMascotPositionSupported;
                }
            }
        }

    }
}
