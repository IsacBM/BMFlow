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
    internal sealed class WorkspaceAppItem
    {
        public string Name;
        public string Target;
        public string Arguments;
        public string AppUserModelId;
        public string Identity;

        public void NormalizeExecutable()
        {
            if (String.Equals(Target, "explorer.exe", StringComparison.OrdinalIgnoreCase) || String.IsNullOrWhiteSpace(Target))
            {
                string candidate = Arguments ?? String.Empty;
                if (candidate.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidate.Substring("shell:AppsFolder\\".Length);
                }
                if (File.Exists(candidate))
                {
                    Target = candidate;
                    Arguments = String.Empty;
                    return;
                }
                if (!String.IsNullOrWhiteSpace(Identity) && File.Exists(Identity))
                {
                    Target = Identity;
                    Arguments = String.Empty;
                    return;
                }

                string lowerName = (Name ?? String.Empty).ToLowerInvariant();
                string lowerId = (AppUserModelId ?? Identity ?? String.Empty).ToLowerInvariant();

                if (lowerName.Contains("edge") || lowerId.Contains("msedge"))
                {
                    string edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
                    if (File.Exists(edgePath)) { Target = edgePath; Arguments = String.Empty; return; }
                    edgePath = @"C:\Program Files\Microsoft\Edge\Application\msedge.exe";
                    if (File.Exists(edgePath)) { Target = edgePath; Arguments = String.Empty; return; }
                }
                if (lowerName.Contains("steam") || lowerId.Contains("steam"))
                {
                    string steamPath = @"C:\Program Files (x86)\Steam\steam.exe";
                    if (File.Exists(steamPath)) { Target = steamPath; Arguments = String.Empty; return; }
                    steamPath = @"C:\Program Files\Steam\steam.exe";
                    if (File.Exists(steamPath)) { Target = steamPath; Arguments = String.Empty; return; }
                }
                if (lowerName.Contains("obs") || lowerId.Contains("obs"))
                {
                    string obsPath = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";
                    if (File.Exists(obsPath)) { Target = obsPath; Arguments = String.Empty; return; }
                }
                if (lowerName.Contains("spotify") || lowerId.Contains("spotify"))
                {
                    string spotPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
                    if (File.Exists(spotPath)) { Target = spotPath; Arguments = String.Empty; return; }
                }
            }

            if (!String.IsNullOrWhiteSpace(AppUserModelId) && (AppUserModelId.StartsWith("{") || AppUserModelId.Contains(":") || AppUserModelId.Contains("\\")))
            {
                if (File.Exists(Target))
                {
                    AppUserModelId = String.Empty;
                }
            }
        }

        public string Serialize()
        {
            return String.Format("{0}|{1}|{2}|{3}|{4}",
                Escape(Name), Escape(Target), Escape(Arguments), Escape(AppUserModelId), Escape(Identity));
        }

        public static WorkspaceAppItem Deserialize(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return null;
            string[] parts = raw.Split('|');
            if (parts.Length == 0) return null;
            var item = new WorkspaceAppItem
            {
                Name = Unescape(parts[0]),
                Target = parts.Length > 1 ? Unescape(parts[1]) : String.Empty,
                Arguments = parts.Length > 2 ? Unescape(parts[2]) : String.Empty,
                AppUserModelId = parts.Length > 3 ? Unescape(parts[3]) : String.Empty,
                Identity = parts.Length > 4 ? Unescape(parts[4]) : String.Empty
            };
            item.NormalizeExecutable();
            return item;
        }

        private static string Escape(string s) { return Uri.EscapeDataString(s ?? String.Empty); }
        private static string Unescape(string s) { try { return Uri.UnescapeDataString(s ?? String.Empty); } catch { return s ?? String.Empty; } }
    }

    internal sealed class DockWorkspace
    {
        public string Id;
        public string Name;
        public string Icon;
        public bool IsolateWorkspace;
        public bool SyncWithWindows;
        public readonly List<WorkspaceAppItem> Apps = new List<WorkspaceAppItem>();

        public DockWorkspace()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Name = "Espaço";
            Icon = "💼";
            IsolateWorkspace = false;
            SyncWithWindows = false;
        }

        public DockWorkspace(string id, string name, string icon, bool isolate = false, bool syncWithWindows = false)
        {
            Id = id;
            Name = name;
            Icon = icon;
            IsolateWorkspace = isolate;
            SyncWithWindows = syncWithWindows;
        }
    }

    internal sealed class WorkspaceManager
    {
        public readonly List<DockWorkspace> Workspaces = new List<DockWorkspace>();
        public int ActiveIndex = 0;
        public bool WorkspacesEnabled = true;

        public DockWorkspace ActiveWorkspace
        {
            get
            {
                if (Workspaces.Count == 0) return null;
                if (ActiveIndex < 0 || ActiveIndex >= Workspaces.Count) ActiveIndex = 0;
                return Workspaces[ActiveIndex];
            }
        }

        private static string WorkspacesFilePath
        {
            get
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BMFlow");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "workspaces.ini");
            }
        }

        public void Save()
        {
            try
            {
                string file = WorkspacesFilePath;
                var lines = new List<string>();
                lines.Add("# B.M Flow Workspaces configuration");
                lines.Add("ActiveIndex=" + ActiveIndex.ToString(CultureInfo.InvariantCulture));
                lines.Add("WorkspacesEnabled=" + WorkspacesEnabled.ToString());
                lines.Add("Count=" + Workspaces.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < Workspaces.Count; i++)
                {
                    var ws = Workspaces[i];
                    lines.Add(String.Format("[Workspace_{0}]", i));
                    lines.Add("Id=" + ws.Id);
                    lines.Add("Name=" + ws.Name);
                    lines.Add("Icon=" + ws.Icon);
                    lines.Add("Isolate=" + ws.IsolateWorkspace.ToString());
                    lines.Add("SyncWithWindows=" + ws.SyncWithWindows.ToString());
                    lines.Add("AppCount=" + ws.Apps.Count.ToString(CultureInfo.InvariantCulture));
                    for (int a = 0; a < ws.Apps.Count; a++)
                    {
                        lines.Add(String.Format("App_{0}={1}", a, ws.Apps[a].Serialize()));
                    }
                }
                File.WriteAllLines(file, lines, Encoding.UTF8);
            }
            catch { }
        }

        public void Load(DockWindow dock)
        {
            try
            {
                string file = WorkspacesFilePath;
                if (!File.Exists(file))
                {
                    CreateDefaultWorkspaces(dock);
                    Save();
                    return;
                }

                var lines = File.ReadAllLines(file, Encoding.UTF8);
                Workspaces.Clear();
                DockWorkspace currentWs = null;
                int activeIdx = 0;
                bool enabled = true;

                foreach (var rawLine in lines)
                {
                    string line = (rawLine ?? String.Empty).Trim();
                    if (line.StartsWith("#") || String.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        if (currentWs != null) Workspaces.Add(currentWs);
                        currentWs = new DockWorkspace();
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (currentWs == null)
                    {
                        if (key.Equals("ActiveIndex", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out activeIdx);
                        }
                        else if (key.Equals("WorkspacesEnabled", StringComparison.OrdinalIgnoreCase))
                        {
                            bool.TryParse(val, out enabled);
                        }
                    }
                    else
                    {
                        if (key.Equals("Id", StringComparison.OrdinalIgnoreCase)) currentWs.Id = val;
                        else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) currentWs.Name = val;
                        else if (key.Equals("Icon", StringComparison.OrdinalIgnoreCase)) currentWs.Icon = val;
                        else if (key.Equals("Isolate", StringComparison.OrdinalIgnoreCase))
                        {
                            bool iso;
                            if (bool.TryParse(val, out iso)) currentWs.IsolateWorkspace = iso;
                        }
                        else if (key.Equals("SyncWithWindows", StringComparison.OrdinalIgnoreCase))
                        {
                            bool sync;
                            if (bool.TryParse(val, out sync)) currentWs.SyncWithWindows = sync;
                        }
                        else if (key.StartsWith("App_", StringComparison.OrdinalIgnoreCase))
                        {
                            var app = WorkspaceAppItem.Deserialize(val);
                            if (app != null) currentWs.Apps.Add(app);
                        }
                    }
                }

                if (currentWs != null) Workspaces.Add(currentWs);

                if (Workspaces.Count == 0)
                {
                    CreateDefaultWorkspaces(dock);
                    Save();
                    return;
                }

                // Garante que o espaço Padrão (que puxa os apps da barra original do Windows) esteja presente
                if (!Workspaces.Any(w => String.Equals(w.Id, "default", StringComparison.OrdinalIgnoreCase) || w.SyncWithWindows))
                {
                    var wsDefault = new DockWorkspace("default", "Padrão", "🪟", false, true);
                    Workspaces.Insert(0, wsDefault);
                    activeIdx = 0;
                    Save();
                }

                ActiveIndex = Math.Max(0, Math.Min(activeIdx, Workspaces.Count - 1));
                WorkspacesEnabled = enabled;
            }
            catch
            {
                if (Workspaces.Count == 0) CreateDefaultWorkspaces(dock);
            }
        }

        private void CreateDefaultWorkspaces(DockWindow dock)
        {
            Workspaces.Clear();

            var wsDefault = new DockWorkspace("default", "Padrão", "🪟", false, true);
            var wsWork = new DockWorkspace("work", "Trabalho", "💼");
            var wsStudy = new DockWorkspace("study", "Estudo", "📚");
            var wsGames = new DockWorkspace("games", "Jogos", "🎮");

            // 1. Trabalho: se houver atalhos fixados atualmente na barra, importa-os!
            if (dock != null)
            {
                var pinnedInitial = dock.GetInitialPinnedForWorkspace();
                if (pinnedInitial != null && pinnedInitial.Count > 0)
                {
                    wsWork.Apps.AddRange(pinnedInitial);
                }
            }

            var installed = GetInstalledApplications();

            // Se wsWork estiver vazio, busca apps de produtividade
            if (wsWork.Apps.Count == 0)
            {
                AddAppIfFound(wsWork, installed, "Visual Studio Code", "Code");
                AddAppIfFound(wsWork, installed, "Brave", "Google Chrome", "Microsoft Edge");
                AddAppIfFound(wsWork, installed, "Terminal", "PowerShell", "Prompt de Comando");
                AddAppIfFound(wsWork, installed, "Explorador de Arquivos", "File Explorer");
            }

            // 2. Estudo
            AddAppIfFound(wsStudy, installed, "Microsoft Edge", "Brave", "Google Chrome");
            AddAppIfFound(wsStudy, installed, "OneNote", "Bloco de Notas", "Notepad");
            AddAppIfFound(wsStudy, installed, "Calculadora", "Calculator");
            AddAppIfFound(wsStudy, installed, "Word", "Documentos");

            // 3. Jogos
            AddAppIfFound(wsGames, installed, "Steam");
            AddAppIfFound(wsGames, installed, "Discord");
            AddAppIfFound(wsGames, installed, "OBS Studio", "OBS");
            AddAppIfFound(wsGames, installed, "Spotify");

            Workspaces.Add(wsDefault);
            Workspaces.Add(wsWork);
            Workspaces.Add(wsStudy);
            Workspaces.Add(wsGames);
            ActiveIndex = 0;
            WorkspacesEnabled = true;
        }

        private static void AddAppIfFound(DockWorkspace ws, List<WorkspaceAppItem> catalog, params string[] names)
        {
            if (catalog == null) return;
            foreach (var n in names)
            {
                var match = catalog.FirstOrDefault(a => (a.Name ?? "").IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        (a.Target ?? "").IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null && !ws.Apps.Any(a => String.Equals(a.Name, match.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    ws.Apps.Add(new WorkspaceAppItem
                    {
                        Name = match.Name,
                        Target = match.Target,
                        Arguments = match.Arguments,
                        AppUserModelId = match.AppUserModelId,
                        Identity = match.Identity
                    });
                    return;
                }
            }
        }

        private static List<WorkspaceAppItem> cachedInstalledApps;
        private static DateTime cachedInstalledAppsExpires = DateTime.MinValue;

        public static List<WorkspaceAppItem> GetInstalledApplications()
        {
            if (cachedInstalledApps != null && DateTime.UtcNow < cachedInstalledAppsExpires)
                return cachedInstalledApps;

            var list = new List<WorkspaceAppItem>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Packaged Apps
            try
            {
                var packaged = DockWindow.ReadPackagedAppsPublic();
                foreach (var p in packaged)
                {
                    if (String.IsNullOrWhiteSpace(p.Name) || IsNoiseAppName(p.Name)) continue;
                    string key = "pkg:" + (p.AppUserModelId ?? p.Identity ?? p.Name);
                    if (!seenKeys.Contains(key))
                    {
                        seenKeys.Add(key);
                        list.Add(new WorkspaceAppItem
                        {
                            Name = p.Name,
                            Target = p.Target ?? "explorer.exe",
                            Arguments = p.Arguments,
                            AppUserModelId = p.AppUserModelId ?? p.Identity,
                            Identity = p.Identity
                        });
                    }
                }
            }
            catch { }

            // 2. Start Menu Shortcuts
            foreach (string folder in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
            })
            {
                if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                try
                {
                    foreach (string lnk in Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories))
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(lnk);
                        if (IsNoiseAppName(name)) continue;

                        var resolved = DockWindow.ResolveShortcutPublic(lnk);
                        if (resolved != null && !String.IsNullOrWhiteSpace(resolved.Target))
                        {
                            if (IsNoiseAppTarget(resolved.Target)) continue;
                            string key = resolved.Target.ToLowerInvariant();
                            if (!seenKeys.Contains(key) && !seenKeys.Contains("name:" + name.ToLowerInvariant()))
                            {
                                seenKeys.Add(key);
                                seenKeys.Add("name:" + name.ToLowerInvariant());
                                list.Add(new WorkspaceAppItem
                                {
                                    Name = String.IsNullOrWhiteSpace(resolved.Name) ? name : resolved.Name,
                                    Target = resolved.Target,
                                    Arguments = resolved.Arguments
                                });
                            }
                        }
                    }
                }
                catch { }
            }

            var result = list.OrderBy(a => a.Name).ToList();
            cachedInstalledApps = result;
            cachedInstalledAppsExpires = DateTime.UtcNow.AddMinutes(10);
            return result;
        }

        private static bool IsNoiseAppName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return true;
            string n = name.ToLowerInvariant();
            return n.Contains("uninstall") || n.Contains("desinstalar") ||
                   n.Contains("readme") || n.Contains("leia-me") ||
                   n.Contains("help") || n.Contains("ajuda") ||
                   n.Contains("manual") || n.Contains("documentation") ||
                   n.Contains("license") || n.Contains("licença") ||
                   n.Contains("release notes") || n.Contains("web site") ||
                   n.Contains("guia do usuário") || n.EndsWith(".url");
        }

        private static bool IsNoiseAppTarget(string target)
        {
            if (String.IsNullOrWhiteSpace(target)) return true;
            string t = target.ToLowerInvariant();
            return t.EndsWith(".url") || t.Contains("unins") || t.Contains("setup") || t.Contains("installer");
        }
    }

    internal enum WorkspaceHudSide
    {
        Above,
        Below,
        Left,
        Right
    }

    internal sealed class WorkspaceHudWindow : Window
    {
        private static WorkspaceHudWindow activeHud;
        private readonly DispatcherTimer fadeTimer;
        private readonly TextBlock iconBlock;
        private readonly TextBlock titleBlock;
        private readonly TextBlock subBlock;

        public WorkspaceHudWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Focusable = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 28, 28, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22, 14, 26, 14),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Opacity = 0.5,
                    Color = Colors.Black
                }
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            iconBlock = new TextBlock
            {
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            sp.Children.Add(iconBlock);

            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleBlock = new TextBlock
            {
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            textPanel.Children.Add(titleBlock);

            subBlock = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 185)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            textPanel.Children.Add(subBlock);

            sp.Children.Add(textPanel);
            border.Child = sp;
            Content = border;

            fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            fadeTimer.Tick += delegate
            {
                fadeTimer.Stop();
                var anim = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(250)));
                anim.Completed += delegate { Close(); if (activeHud == this) activeHud = null; };
                BeginAnimation(OpacityProperty, anim);
            };
        }

        public static void ShowHud(string icon, string title, string subtitle, double anchorX, double anchorY, WorkspaceHudSide side)
        {
            if (activeHud != null)
            {
                try { activeHud.Close(); } catch { }
                activeHud = null;
            }

            var hud = new WorkspaceHudWindow();
            activeHud = hud;
            hud.iconBlock.Text = icon;
            hud.titleBlock.Text = title;
            hud.subBlock.Text = subtitle;

            hud.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = hud.DesiredSize.Width > 0 ? hud.DesiredSize.Width : 220;
            double h = hud.DesiredSize.Height > 0 ? hud.DesiredSize.Height : 70;

            if (side == WorkspaceHudSide.Right)
            {
                hud.Left = anchorX;
                hud.Top = anchorY - h / 2.0;
            }
            else if (side == WorkspaceHudSide.Left)
            {
                hud.Left = anchorX - w;
                hud.Top = anchorY - h / 2.0;
            }
            else
            {
                hud.Left = anchorX - w / 2.0;
                hud.Top = side == WorkspaceHudSide.Below ? anchorY : anchorY - h;
            }
            hud.Left = Math.Max(SystemParameters.VirtualScreenLeft + 8,
                Math.Min(hud.Left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - w - 8));
            hud.Top = Math.Max(SystemParameters.VirtualScreenTop + 8,
                Math.Min(hud.Top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - h - 8));
            hud.Opacity = 0;
            hud.Show();

            var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(150)));
            hud.BeginAnimation(OpacityProperty, anim);
            hud.fadeTimer.Start();
        }
    }

}
