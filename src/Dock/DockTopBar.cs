using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Media;

namespace DockBar
{
    public sealed partial class DockWindow
    {
        private bool showTopBar = true;
        private TopBarWindow topBarWindow;
        private IntPtr topBarLastActiveWindow;
        private ImageSource topBarLastActiveIcon;

        internal bool ShowTopBar { get { return showTopBar; } }
        internal string CurrentTopBarTheme { get { return dockTheme; } }
        internal int TopBarActiveWorkspaceIndex
        {
            get { return workspaceManager == null ? -1 : workspaceManager.ActiveIndex; }
        }

        internal void SetTopBarEnabled(bool enabled)
        {
            if (showTopBar == enabled) return;
            showTopBar = enabled;
            UpdateTopBarVisibility();
            SaveSettings();
        }

        private void UpdateTopBarVisibility()
        {
            bool shouldShow = showTopBar && !IsDockTop && !isClosing;
            UpdateDockSystemCardVisibility(shouldShow);
            if (shouldShow)
            {
                if (topBarWindow == null)
                {
                    topBarWindow = new TopBarWindow(this);
                    topBarWindow.Show();
                }
                else if (!topBarWindow.IsVisible) topBarWindow.Show();
                return;
            }

            if (topBarWindow != null)
            {
                try { topBarWindow.Close(); } catch { }
                topBarWindow = null;
            }
        }

        private void UpdateDockSystemCardVisibility()
        {
            UpdateDockSystemCardVisibility(showTopBar && !IsDockTop && !isClosing);
        }

        private void UpdateDockSystemCardVisibility(bool topBarActive)
        {
            System.Windows.Visibility visibility = topBarActive
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
            if (systemCardSeparator != null) systemCardSeparator.Visibility = visibility;
            if (systemCardHolder != null) systemCardHolder.Visibility = visibility;
        }

        private void CloseTopBar()
        {
            if (topBarWindow == null) return;
            try { topBarWindow.Close(); } catch { }
            topBarWindow = null;
        }

        internal IList<TopBarWorkspace> ReadTopBarWorkspaces()
        {
            var result = new List<TopBarWorkspace>();
            if (workspaceManager == null || !workspaceManager.WorkspacesEnabled) return result;
            foreach (DockWorkspace workspace in workspaceManager.Workspaces)
                result.Add(new TopBarWorkspace { Name = workspace.Name });
            return result;
        }

        internal TopBarActiveWindow ReadTopBarActiveWindow()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (topBarWindow != null && window == topBarWindow.NativeHandle) return null;
                if (window == IntPtr.Zero || window == dockHandle) return new TopBarActiveWindow { Title = "Área de trabalho" };
                int length = GetWindowTextLength(window);
                var title = new StringBuilder(Math.Max(2, length + 1));
                GetWindowText(window, title, title.Capacity);
                string value = title.ToString().Trim();
                if (String.IsNullOrWhiteSpace(value)) return new TopBarActiveWindow { Title = "Área de trabalho" };

                ImageSource icon = window == topBarLastActiveWindow ? topBarLastActiveIcon : null;
                if (window != topBarLastActiveWindow)
                {
                    uint processId;
                    GetWindowThreadProcessId(window, out processId);
                    if (processId != 0)
                    {
                        try
                        {
                            using (Process process = Process.GetProcessById((int)processId))
                            {
                                string path = process.MainModule == null ? null : process.MainModule.FileName;
                                if (!String.IsNullOrWhiteSpace(path)) icon = IconFor(path);
                            }
                        }
                        catch { }
                    }
                    topBarLastActiveWindow = window;
                    topBarLastActiveIcon = icon;
                }
                return new TopBarActiveWindow { Title = value, Icon = icon };
            }
            catch { return new TopBarActiveWindow { Title = "Área de trabalho" }; }
        }

        internal TopBarSystemState ReadTopBarSystemState()
        {
            bool muted;
            float volume;
            if (!AudioController.TryGetState(out volume, out muted))
            {
                muted = false;
                volume = 0;
            }
            BatteryVisualState battery = ReadBatteryVisualState();
            NetworkVisualState network = ReadNetworkVisualState();
            BluetoothRadioStatus bluetooth = BluetoothRadioReader.Read();
            int volumePercent = muted ? 0 : (int)Math.Round(volume * 100);
            return new TopBarSystemState
            {
                NetworkGlyph = network.Glyph,
                NetworkLabel = network.Label,
                BluetoothAvailable = bluetooth.Available,
                BluetoothEnabled = bluetooth.Enabled,
                BluetoothLabel = bluetooth.Available
                    ? (bluetooth.Enabled ? "Bluetooth ativado" : "Bluetooth desativado")
                    : "Bluetooth indisponível",
                VolumeGlyph = VolumeGlyphFor(volume, muted),
                VolumeLabel = "Volume " + volumePercent.ToString(CultureInfo.InvariantCulture) + "%",
                BatteryGlyph = battery.Glyph,
                BatteryLabel = battery.Label,
                BatteryCritical = battery.Critical,
                BatteryLow = battery.Low,
                BatteryCharging = battery.Charging
            };
        }

        private void RefreshTopBarWorkspaces()
        {
            if (topBarWindow != null) topBarWindow.RefreshWorkspaces();
        }
    }
}
