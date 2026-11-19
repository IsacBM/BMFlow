using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DockBar
{
    public sealed partial class DockWindow
    {
        private void RefreshState()
        {
            var now = DateTime.Now;
            if (dayText != null) dayText.Text = now.ToString("ddd", new CultureInfo("pt-BR")).ToUpperInvariant().Replace(".", "");
            if (dateText != null) dateText.Text = now.Day.ToString(CultureInfo.InvariantCulture);
            RefreshClock(now);
            RefreshTaskItems();
            if (DateTime.UtcNow >= nextSystemInfoRefreshUtc)
            {
                nextSystemInfoRefreshUtc = DateTime.UtcNow.AddSeconds(5);
                RefreshSystemInfoAsync();
            }
        }

        private void RefreshClock(DateTime now)
        {
            var culture = new CultureInfo("pt-BR");
            string time = now.ToString("HH:mm", culture);
            string date = now.ToString("dd/MM", culture);
            if (clockText != null && clockText.Text != time) clockText.Text = time;
            if (clockDateText != null && clockDateText.Text != date) clockDateText.Text = date;
            string fullDate = now.ToString("dddd, d 'de' MMMM 'de' yyyy", culture);
            if (fullDate.Length > 0) fullDate = Char.ToUpper(fullDate[0], culture) + fullDate.Substring(1);
            var clockStack = clockText == null ? null : clockText.Parent as FrameworkElement;
            SetToolTipText(clockStack, fullDate);
        }

        private void SampleAdaptiveContrastBackground(object ignored)
        {
            if (isClosing || dockHandle == IntPtr.Zero || dockAutoHidden || fullscreenSuppressed) return;
            if (dockTheme != "Flutuante" && dockOpacity > 80) return;
            if (Interlocked.CompareExchange(ref adaptiveContrastSampling, 1, 0) != 0) return;

            IntPtr desktopDc = IntPtr.Zero;
            try
            {
                NativeRect bounds;
                if (!GetWindowRect(dockHandle, out bounds) || bounds.Right - bounds.Left < 20) return;
                int sampleY = bounds.Top + 3;
                long luminance = 0;
                int samples = 0;
                desktopDc = GetDC(IntPtr.Zero);
                if (desktopDc == IntPtr.Zero) return;
                for (int index = 0; index < 9; index++)
                {
                    int x = bounds.Left + 10 + (int)Math.Round((bounds.Right - bounds.Left - 20) * index / 8.0);
                    uint pixel = GetPixel(desktopDc, x, sampleY);
                    if (pixel == 0xFFFFFFFF) continue;
                    byte red = (byte)(pixel & 0xFF);
                    byte green = (byte)((pixel >> 8) & 0xFF);
                    byte blue = (byte)((pixel >> 16) & 0xFF);
                    luminance += (red * 299L + green * 587L + blue * 114L) / 1000L;
                    samples++;
                }
                if (samples == 0) return;
                long threshold = adaptiveLightSurface == true ? 150 : 174;
                bool lightSurface = luminance / samples >= threshold;
                if (adaptiveLightSurface.HasValue && adaptiveLightSurface.Value == lightSurface) return;

                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing || (adaptiveLightSurface.HasValue && adaptiveLightSurface.Value == lightSurface)) return;
                    adaptiveLightSurface = lightSurface;
                    ApplyReadableSystemStyle();
                }));
            }
            catch { }
            finally
            {
                if (desktopDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, desktopDc);
                Interlocked.Exchange(ref adaptiveContrastSampling, 0);
            }
        }

        private void ApplyReadableSystemStyle()
        {
            bool darkForeground = !darkMode || adaptiveLightSurface == true;
            Brush foreground = darkForeground
                ? new SolidColorBrush(Color.FromRgb(30, 32, 36))
                : Brushes.White;
            Effect halo = darkForeground ? lightTextHalo : darkTextHalo;

            foreach (TextBlock text in themedText)
            {
                // A bateria conserva verde/laranja/vermelho para comunicar o
                // estado; o contorno escuro já a mantém visível em fundo claro.
                if (text != batteryText) text.Foreground = foreground;
                text.Effect = text == batteryText ? darkTextHalo : halo;
            }

            foreach (Border separator in themedSeparators)
                separator.Background = darkForeground
                    ? new SolidColorBrush(Color.FromArgb(105, 30, 32, 36))
                    : new SolidColorBrush(Color.FromArgb(92, 255, 255, 255));
        }

        private static string VolumeGlyphFor(float volume, bool muted)
        {
            if (muted || volume <= .001f) return "\uE74F";
            int percent = (int)Math.Round(volume * 100);
            if (percent <= 32) return "\uE993"; // uma onda
            if (percent <= 65) return "\uE994"; // duas ondas
            return "\uE995";                    // três ondas
        }

        private void MonitorVolumeBackground(object ignored)
        {
            if (isClosing || Interlocked.CompareExchange(ref volumeMonitorRunning, 1, 0) != 0) return;
            try
            {
                bool muted;
                float volume;
                if (!AudioController.TryGetState(out volume, out muted)) return;
                int percent = muted ? 0 : (int)Math.Round(volume * 100);
                string glyph = VolumeGlyphFor(volume, muted);
                if (glyph == lastVolumeGlyph && percent == lastVolumePercent && muted == lastVolumeMuted) return;
                lastVolumeGlyph = glyph;
                lastVolumePercent = percent;
                lastVolumeMuted = muted;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing || volumeText == null) return;
                    bool glyphChanged = volumeText.Text != glyph;
                    if (glyphChanged)
                    {
                        volumeText.Text = glyph;
                        volumeText.BeginAnimation(OpacityProperty, new DoubleAnimation(.58, 1, TimeSpan.FromMilliseconds(115))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                    }
                    SetToolTipText(volumeText, "Volume " + percent.ToString(CultureInfo.InvariantCulture) + "%");
                }));
            }
            catch { }
            finally { Interlocked.Exchange(ref volumeMonitorRunning, 0); }
        }

        private static BatteryVisualState ReadBatteryVisualState()
        {
            var power = Forms.SystemInformation.PowerStatus;
            int percent = power.BatteryLifePercent < 0 ? -1 : (int)Math.Round(power.BatteryLifePercent * 100);
            Forms.BatteryChargeStatus chargeStatus = power.BatteryChargeStatus;
            bool noBattery = percent < 0 || (chargeStatus & Forms.BatteryChargeStatus.NoSystemBattery) != 0;
            bool pluggedIn = power.PowerLineStatus == Forms.PowerLineStatus.Online;
            bool charging = !noBattery && ((chargeStatus & Forms.BatteryChargeStatus.Charging) != 0 || pluggedIn);
            int batteryLevel = percent < 0 ? 0 : Math.Max(0, Math.Min(10, (int)Math.Round(percent / 10.0)));
            string glyph;
            if (noBattery) glyph = charging ? "\uEB17" : "\uE996";
            else if (charging) glyph = Char.ConvertFromUtf32(0xEBAB + batteryLevel); // MobBatteryCharging0–10
            else glyph = Char.ConvertFromUtf32(0xEBA0 + batteryLevel);               // MobBattery0–10

            bool critical = !noBattery && ((chargeStatus & Forms.BatteryChargeStatus.Critical) != 0 || percent <= 10);
            bool low = !noBattery && !critical && ((chargeStatus & Forms.BatteryChargeStatus.Low) != 0 || percent <= 20);
            string label = noBattery ? "Bateria não detectada" : "Bateria " + percent + "%";
            if (charging && percent < 100) label += " - Carregando";
            else if (charging) label += " - Carregada e conectada à energia";
            else if (critical) label += " - Nível crítico";
            else if (low) label += " - Bateria fraca";
            return new BatteryVisualState { Glyph = glyph, Label = label, Critical = critical, Low = low, Charging = charging };
        }

        private void MonitorBatteryBackground(object ignored)
        {
            if (isClosing || Interlocked.CompareExchange(ref batteryMonitorRunning, 1, 0) != 0) return;
            try
            {
                BatteryVisualState state = ReadBatteryVisualState();
                if (state.Glyph == lastBatteryGlyph && state.Label == lastBatteryLabel) return;
                lastBatteryGlyph = state.Glyph;
                lastBatteryLabel = state.Label;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing || batteryText == null) return;
                    bool glyphChanged = batteryText.Text != state.Glyph;
                    if (glyphChanged)
                    {
                        batteryText.Text = state.Glyph;
                        batteryText.BeginAnimation(OpacityProperty, new DoubleAnimation(.58, 1, TimeSpan.FromMilliseconds(130))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                    }
                    batteryText.Foreground = state.Critical ? new SolidColorBrush(Color.FromRgb(255, 80, 80))
                        : state.Low ? new SolidColorBrush(Color.FromRgb(255, 184, 71))
                        : state.Charging ? new SolidColorBrush(Color.FromRgb(105, 220, 130))
                        : darkMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(45, 45, 45));
                    SetToolTipText(batteryText, state.Label);
                }));
            }
            catch { }
            finally { Interlocked.Exchange(ref batteryMonitorRunning, 0); }
        }

        private static NetworkVisualState ReadNetworkVisualState()
        {
            bool hasWifi = false;
            bool hasEthernet = false;
            bool wifiHasGateway = false;
            bool ethernetHasGateway = false;
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    bool hasGateway = false;
                    try
                    {
                        hasGateway = adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
                            gateway.Address != null &&
                            !gateway.Address.Equals(System.Net.IPAddress.Any) &&
                            !gateway.Address.Equals(System.Net.IPAddress.IPv6Any));
                    }
                    catch { }
                    switch (adapter.NetworkInterfaceType)
                    {
                        case System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211:
                            hasWifi = true;
                            wifiHasGateway = wifiHasGateway || hasGateway;
                            break;
                        case System.Net.NetworkInformation.NetworkInterfaceType.Ethernet:
                        case System.Net.NetworkInformation.NetworkInterfaceType.Ethernet3Megabit:
                        case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx:
                        case System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT:
                        case System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet:
                            string identity = ((adapter.Name ?? String.Empty) + " " + (adapter.Description ?? String.Empty)).ToLowerInvariant();
                            bool virtualAdapter = identity.Contains("virtual") || identity.Contains("hyper-v") ||
                                identity.Contains("vethernet") || identity.Contains("wsl") || identity.Contains("vmware") ||
                                identity.Contains("tap-") || identity.Contains("vpn") || identity.Contains("tunnel");
                            if (!virtualAdapter)
                            {
                                hasEthernet = true;
                                ethernetHasGateway = ethernetHasGateway || hasGateway;
                            }
                            break;
                    }
                }
            }
            catch { }

            bool connected = hasWifi || hasEthernet;
            bool internet = false;
            object managerObject = null;
            try
            {
                Type managerType = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
                if (managerType != null)
                {
                    managerObject = Activator.CreateInstance(managerType);
                    dynamic manager = managerObject;
                    connected = connected || (bool)manager.IsConnected;
                    internet = (bool)manager.IsConnectedToInternet;
                }
            }
            catch { internet = connected && System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); }
            finally { if (managerObject != null && Marshal.IsComObject(managerObject)) Marshal.FinalReleaseComObject(managerObject); }

            bool useEthernet = ethernetHasGateway || (!wifiHasGateway && hasEthernet && !hasWifi);
            if (useEthernet)
                return new NetworkVisualState
                {
                    Glyph = internet ? "\uE839" : "\uEB56",
                    Label = internet ? "Ethernet - Conectado" : "Ethernet - Sem Internet"
                };
            if (hasWifi)
                return new NetworkVisualState
                {
                    Glyph = internet ? "\uE701" : "\uEB63",
                    Label = internet ? "Wi-Fi - Conectado" : "Wi-Fi - Sem Internet"
                };
            return new NetworkVisualState
            {
                Glyph = connected ? "\uE998" : "\uE774",
                Label = connected ? "Rede - Sem Internet" : "Sem conexão de rede"
            };
        }

        private void MonitorNetworkBackground(object ignored)
        {
            if (isClosing || Interlocked.CompareExchange(ref networkMonitorRunning, 1, 0) != 0) return;
            try
            {
                NetworkVisualState state = ReadNetworkVisualState();
                if (state.Glyph == lastNetworkGlyph && state.Label == lastNetworkLabel) return;
                lastNetworkGlyph = state.Glyph;
                lastNetworkLabel = state.Label;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing || networkText == null) return;
                    if (networkText.Text != state.Glyph)
                    {
                        networkText.Text = state.Glyph;
                        networkText.BeginAnimation(OpacityProperty, new DoubleAnimation(.58, 1, TimeSpan.FromMilliseconds(130))
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        });
                    }
                    SetToolTipText(networkText, state.Label);
                }));
            }
            catch { }
            finally { Interlocked.Exchange(ref networkMonitorRunning, 0); }
        }

        private void RefreshSystemInfoAsync()
        {
            if (Interlocked.CompareExchange(ref systemRefreshRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                SystemSnapshot snapshot = null;
                try { snapshot = ReadSystemSnapshot(); }
                catch { }
                if (isClosing)
                {
                    Interlocked.Exchange(ref systemRefreshRunning, 0);
                    return;
                }
                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        try { if (!isClosing && snapshot != null) ApplySystemSnapshot(snapshot); }
                        finally { Interlocked.Exchange(ref systemRefreshRunning, 0); }
                    }));
                }
                catch { Interlocked.Exchange(ref systemRefreshRunning, 0); }
            });
        }

        private SystemSnapshot ReadSystemSnapshot()
        {
            var snapshot = new SystemSnapshot
            {
                ProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };

            Process[] processes = null;
            try
            {
                processes = Process.GetProcesses();
                foreach (Process process in processes)
                {
                    try { snapshot.ProcessNames.Add(process.ProcessName); }
                    catch { }
                }
            }
            finally
            {
                if (processes != null)
                    foreach (Process process in processes) process.Dispose();
            }

            if (recycleBinImage != null)
            {
                var recycleInfo = new RecycleBinInfo { Size = Marshal.SizeOf(typeof(RecycleBinInfo)) };
                if (SHQueryRecycleBin(null, ref recycleInfo) == 0)
                {
                    snapshot.RecycleFull = recycleInfo.ItemCount > 0;
                    if (!recycleBinFull.HasValue || recycleBinFull.Value != snapshot.RecycleFull.Value)
                        snapshot.RecycleSource = LoadRecycleBinIcon(snapshot.RecycleFull.Value);
                }
            }
            return snapshot;
        }

        private void ApplySystemSnapshot(SystemSnapshot snapshot)
        {
            foreach (var item in runningDots)
                item.Value.Visibility = snapshot.ProcessNames.Contains(item.Key) ? Visibility.Visible : Visibility.Hidden;
            SetToolTipText(trayArrowText, "Ícones ocultos");

            if (snapshot.RecycleFull.HasValue)
            {
                if (snapshot.RecycleSource != null) recycleBinImage.Source = snapshot.RecycleSource;
                recycleBinFull = snapshot.RecycleFull.Value;
            }
        }

    }
}
