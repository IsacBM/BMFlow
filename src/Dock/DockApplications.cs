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
        public void RefreshTaskItemsImmediate()
        {
            if (isClosing) return;
            nextTaskRefreshUtc = DateTime.MinValue;
            taskSignature = String.Empty;
            try
            {
                var apps = ReadTaskApps();
                if (apps != null && !isClosing) ApplyTaskItems(apps);
            }
            catch { }
        }

        private void RefreshTaskItems()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextTaskRefreshUtc) return;
            nextTaskRefreshUtc = now.AddMilliseconds(dockAutoHidden ? 8000 : 2500);
            if (Interlocked.CompareExchange(ref taskRefreshRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<TaskApp> apps = null;
                try { apps = ReadTaskApps(); }
                catch { }

                if (isClosing)
                {
                    Interlocked.Exchange(ref taskRefreshRunning, 0);
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
                    {
                        try
                        {
                            if (!isClosing && apps != null) ApplyTaskItems(apps);
                        }
                        finally { Interlocked.Exchange(ref taskRefreshRunning, 0); }
                    }));
                }
                catch { Interlocked.Exchange(ref taskRefreshRunning, 0); }
            });
        }

        private void ApplyTaskItems(List<TaskApp> apps)
        {
            // Durante um arraste não podemos trocar a árvore visual. Durante a
            // ampliação, porém, a atualização precisa continuar: do contrário um
            // app recém-aberto fica sem prévia até o cursor sair da Dock. O método
            // já é chamado em prioridade Background para não disputar com frames.
            if (taskDragInProgress) return;
            string signature = String.Join("|", apps.Select(a => (a.Identity ?? a.Target) + ":" +
                String.Join(",", a.WindowHandles.Select(h => h.ToInt64()).OrderBy(h => h).Select(h => h.ToString(CultureInfo.InvariantCulture)))));
            if (signature == taskSignature) return;
            taskSignature = signature;
            ReleaseTaskItemVisuals();
            taskItems.Children.Clear();
            foreach (var app in apps) taskItems.Children.Add(TaskTile(app));
            waveLayoutDirty = true;
        }

        private void ReleaseTaskItemVisuals()
        {
            var released = new HashSet<FrameworkElement>();
            foreach (FrameworkElement item in taskItems.Children.OfType<FrameworkElement>())
            {
                released.Add(item);
                var panel = item as Panel;
                if (panel == null) continue;
                foreach (FrameworkElement child in panel.Children.OfType<FrameworkElement>())
                    released.Add(child);
            }
            if (released.Count == 0) return;

            magnifyIcons.RemoveAll(item => released.Contains(item));
            foreach (FrameworkElement item in released) magnifyGrowth.Remove(item);
            wavePassives.RemoveAll(item => released.Contains(item));
            themedTiles.RemoveAll(item => released.Contains(item));
            themedText.RemoveAll(item => released.Contains(item));
            themedSeparators.RemoveAll(item => released.Contains(item));
            waveSamples.RemoveAll(sample =>
                (sample.Icon != null && released.Contains(sample.Icon)) ||
                (sample.Container != null && released.Contains(sample.Container)));
            if (waveTopContainer != null && released.Contains(waveTopContainer)) waveTopContainer = null;
        }

        private List<TaskApp> ReadTaskApps()
        {
            var apps = new List<TaskApp>();

            DockWorkspace activeWs = (workspaceManager != null && workspaceManager.WorkspacesEnabled)
                ? workspaceManager.ActiveWorkspace
                : null;

            bool useWindowsTaskbarPinned = (activeWs == null || activeWs.SyncWithWindows || (activeWs.Apps.Count == 0 && activeWs.Id == "default"));

            if (!useWindowsTaskbarPinned && activeWs != null)
            {
                var packaged = ReadPackagedApps();
                foreach (var wApp in activeWs.Apps)
                {
                    wApp.NormalizeExecutable();
                    TaskApp item = null;
                    if (!String.IsNullOrWhiteSpace(wApp.Target) && !String.Equals(wApp.Target, "explorer.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        item = new TaskApp
                        {
                            Name = wApp.Name,
                            Target = wApp.Target,
                            Arguments = wApp.Arguments,
                            AppUserModelId = wApp.AppUserModelId,
                            Identity = wApp.Identity,
                            IsPinned = true,
                            Icon = IconFor(wApp.Target) ?? ResolveAppIcon(wApp.Target, wApp.AppUserModelId)
                        };
                    }
                    else if (!String.IsNullOrWhiteSpace(wApp.AppUserModelId))
                    {
                        item = packaged.FirstOrDefault(p => String.Equals(p.AppUserModelId, wApp.AppUserModelId, StringComparison.OrdinalIgnoreCase));
                        if (item != null)
                        {
                            item = new TaskApp
                            {
                                Name = wApp.Name,
                                Target = item.Target,
                                Arguments = item.Arguments,
                                Identity = item.Identity,
                                AppUserModelId = item.AppUserModelId,
                                IsPinned = true,
                                Icon = IconForShellItem(item.Identity)
                            };
                        }
                    }
                    if (item == null)
                    {
                        item = new TaskApp
                        {
                            Name = wApp.Name,
                            Target = wApp.Target,
                            Arguments = wApp.Arguments,
                            AppUserModelId = wApp.AppUserModelId,
                            Identity = wApp.Identity,
                            IsPinned = true,
                            Icon = ResolveAppIcon(wApp.Target, wApp.AppUserModelId) ?? IconFor(wApp.Target)
                        };
                    }
                    if (!apps.Any(a => SameTarget(a.Target, item.Target) && String.Equals(a.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        apps.Add(item);
                    }
                }
            }
            else
            {
                string pinned = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                if (Directory.Exists(pinned))
                {
                    foreach (string link in Directory.GetFiles(pinned, "*.lnk").OrderBy(p => File.GetCreationTimeUtc(p)))
                    {
                        var item = ResolveShortcut(link);
                        if (item != null && !apps.Any(a => SameTarget(a.Target, item.Target)))
                        {
                            item.IsPinned = true;
                            apps.Add(item);
                        }
                    }
                }

                var pinnedNames = ReadPinnedButtonNames();
                if (pinnedNames.Count > 0)
                {
                    var ordered = new List<TaskApp>();
                    var packaged = ReadPackagedApps();
                    foreach (string pinnedName in pinnedNames)
                    {
                        var item = apps.FirstOrDefault(a => PinnedNameMatches(pinnedName, a));
                        if (item == null && (pinnedName.StartsWith("Explorador", StringComparison.OrdinalIgnoreCase) || pinnedName.StartsWith("File Explorer", StringComparison.OrdinalIgnoreCase)))
                        {
                            string explorerPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                            item = new TaskApp { Name = "Explorador de Arquivos", Target = explorerPath, Icon = IconFor(explorerPath) };
                        }
                        if (item == null)
                        {
                            item = packaged.FirstOrDefault(a => PinnedNameMatches(pinnedName, a));
                            if (item != null) item.Icon = IconForShellItem(item.Identity);
                        }
                        if (item != null)
                        {
                            item.IsPinned = true;
                            if (!ordered.Contains(item)) ordered.Add(item);
                        }
                    }
                    foreach (var item in apps) if (!ordered.Contains(item)) ordered.Add(item);
                    apps = ordered;
                }
            }

            List<TaskApp> runningPackagedApps = null;
            foreach (IntPtr window in EnumerateTaskbarWindows())
            {
                Process process = null;
                try
                {
                    uint processId;
                    GetWindowThreadProcessId(window, out processId);
                    if (processId == 0 || processId == currentProcessId) continue;
                    process = Process.GetProcessById((int)processId);
                    string windowTitle = GetWindowTitle(window);
                    string processName = process.ProcessName;
                    string path = String.Empty;
                    try { path = process.MainModule == null ? String.Empty : process.MainModule.FileName; }
                    catch { }
                    string windowAppId = GetWindowAppUserModelId(window);
                    string windowClass = GetWindowClassName(window);
                    bool isWindowsTerminal = String.Equals(processName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
                                             (String.Equals(processName, "OpenConsole", StringComparison.OrdinalIgnoreCase) &&
                                              (windowClass.IndexOf("CASCADIA", StringComparison.OrdinalIgnoreCase) >= 0 || IsWindowsTerminalIdentity(path))) ||
                                             windowClass.IndexOf("CASCADIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             IsWindowsTerminalIdentity(windowAppId);
                    if (isWindowsTerminal)
                    {
                        path = ResolveWindowsTerminalTarget(path);
                        if (String.IsNullOrWhiteSpace(windowAppId)) windowAppId = WindowsTerminalAppId;
                    }
                    bool isExplorerWindow = String.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                                            SameTarget(path, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
                    bool isSteamWindow = String.Equals(processName, "steamwebhelper", StringComparison.OrdinalIgnoreCase) ||
                                         String.Equals(processName, "steam", StringComparison.OrdinalIgnoreCase);
                    string runningIdentity = BuildRunningIdentity(windowAppId, processName, processId);
                    var match = apps.FirstOrDefault(a => RunningWindowMatchesApp(a, isWindowsTerminal, isExplorerWindow,
                        isSteamWindow, windowAppId, runningIdentity, path, processName));
                    if (match != null)
                    {
                        if (!match.WindowHandles.Contains(window))
                        {
                            match.WindowHandles.Add(window);
                            match.WindowTitles.Add(windowTitle);
                        }
                        if (match.WindowHandle == IntPtr.Zero) match.WindowHandle = window;
                        if (String.IsNullOrWhiteSpace(match.AppUserModelId)) match.AppUserModelId = windowAppId;
                    }
                    else
                    {
                        bool alreadyInApps = apps.Any(a => RunningWindowMatchesApp(a, isWindowsTerminal, isExplorerWindow,
                            isSteamWindow, windowAppId, runningIdentity, path, processName));

                        if (!alreadyInApps)
                        {
                            if (activeWs != null && activeWs.IsolateWorkspace) continue;
                            TaskApp packagedApp = null;
                            if (!String.IsNullOrWhiteSpace(windowAppId))
                            {
                                if (runningPackagedApps == null) runningPackagedApps = ReadPackagedApps();
                                packagedApp = runningPackagedApps.FirstOrDefault(item =>
                                    String.Equals(item.AppUserModelId, windowAppId, StringComparison.OrdinalIgnoreCase) ||
                                    String.Equals(item.Identity, windowAppId, StringComparison.OrdinalIgnoreCase));
                            }
                            string description = String.Empty;
                            try { description = process.MainModule.FileVersionInfo.FileDescription; } catch { }
                            string displayName = isWindowsTerminal ? "Terminal"
                                : packagedApp != null && !String.IsNullOrWhiteSpace(packagedApp.Name) ? packagedApp.Name
                                : String.IsNullOrWhiteSpace(description) ? processName : description;
                            string launchArguments = String.Empty;
                            string resolvedIdentity = !String.IsNullOrWhiteSpace(runningIdentity) ? runningIdentity : windowAppId;
                            if (isWindowsTerminal)
                            {
                                windowAppId = WindowsTerminalAppId;
                                resolvedIdentity = WindowsTerminalAppId;
                            }
                            else if (isSteamWindow)
                            {
                                displayName = "Steam";
                                string steamExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steam.exe");
                                if (!File.Exists(steamExe)) steamExe = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Steam\steam.exe");
                                if (File.Exists(steamExe)) path = steamExe;
                            }
                            else if (packagedApp != null)
                            {
                                if (String.IsNullOrWhiteSpace(path) || IsSharedRuntimeProcess(processName)) path = packagedApp.Target;
                                launchArguments = packagedApp.Arguments;
                                if (!String.IsNullOrWhiteSpace(packagedApp.Identity)) resolvedIdentity = packagedApp.Identity;
                            }
                            else if (!String.IsNullOrWhiteSpace(windowTitle)) displayName = windowTitle;

                            ImageSource runningIcon = !String.IsNullOrWhiteSpace(windowAppId)
                                ? (IconForShellItem(windowAppId) ?? IconFor(path))
                                : IconFor(path);
                            var runningApp = new TaskApp
                            {
                                Name = displayName,
                                Target = path,
                                Arguments = launchArguments,
                                AppUserModelId = windowAppId,
                                Identity = resolvedIdentity,
                                WindowHandle = window,
                                Icon = runningIcon
                            };
                            runningApp.WindowHandles.Add(window);
                            runningApp.WindowTitles.Add(windowTitle);
                            apps.Add(runningApp);
                        }
                    }
                }
                catch { }
                finally { if (process != null) process.Dispose(); }
            }
            // Identifica se algum aplicativo fixado está em execução em segundo plano
            try
            {
                foreach (var app in apps)
                {
                    if (app.WindowHandles.Count > 0) continue;
                    string exeName = System.IO.Path.GetFileNameWithoutExtension(app.Target);
                    if (String.IsNullOrWhiteSpace(exeName)) continue;

                    // O processo explorer.exe é o shell permanente do Windows; nunca deve acender apps como rodando em segundo plano
                    if (String.Equals(exeName, "explorer", StringComparison.OrdinalIgnoreCase)) continue;

                    bool isWallpaperEngine = String.Equals(exeName, "wallpaper32", StringComparison.OrdinalIgnoreCase) ||
                                             String.Equals(exeName, "wallpaper64", StringComparison.OrdinalIgnoreCase) ||
                                             String.Equals(exeName, "ui32", StringComparison.OrdinalIgnoreCase) ||
                                             app.Name.IndexOf("Wallpaper Engine", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isWallpaperEngine)
                    {
                        if (Process.GetProcessesByName("wallpaper32").Length > 0 ||
                            Process.GetProcessesByName("wallpaper64").Length > 0 ||
                            Process.GetProcessesByName("ui32").Length > 0)
                        {
                            app.IsRunningInBackground = true;
                        }
                    }
                    else if (String.Equals(exeName, "steam", StringComparison.OrdinalIgnoreCase) ||
                             String.Equals(app.Name, "Steam", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Process.GetProcessesByName("steam").Length > 0 || Process.GetProcessesByName("steamwebhelper").Length > 0)
                        {
                            app.IsRunningInBackground = true;
                        }
                    }
                    else
                    {
                        if (Process.GetProcessesByName(exeName).Length > 0)
                        {
                            app.IsRunningInBackground = true;
                        }
                    }
                }
            }
            catch { }

            if (activeWs == null || activeWs.Apps.Count == 0)
            {
                ApplySavedAppOrder(apps);
            }
            return apps;
        }

        private void ApplySavedAppOrder(List<TaskApp> apps)
        {
            List<string> saved;
            lock (appOrder) saved = new List<string>(appOrder);
            if (saved.Count == 0 || apps.Count < 2) return;
            var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < saved.Count; index++)
                if (!positions.ContainsKey(saved[index])) positions[saved[index]] = index;

            var ordered = apps.Select((app, index) => new { App = app, Original = index, Key = TaskAppKey(app) })
                .OrderBy(item => positions.ContainsKey(item.Key) ? positions[item.Key] : Int32.MaxValue)
                .ThenBy(item => item.Original)
                .Select(item => item.App)
                .ToList();
            apps.Clear();
            apps.AddRange(ordered);
        }

        private static List<string> ReadPinnedButtonNames()
        {
            try
            {
                if (cachedPinnedNames.Count > 0 && DateTime.UtcNow < pinnedNamesCacheExpiresUtc)
                    return new List<string>(cachedPinnedNames);
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar == IntPtr.Zero) return new List<string>(cachedPinnedNames);
                var names = CollectPinnedButtonNames(taskbar);
                if (names.Count > 0)
                {
                    cachedPinnedNames.Clear();
                    cachedPinnedNames.AddRange(names);
                    pinnedNamesCacheExpiresUtc = DateTime.UtcNow.AddMinutes(5);
                    return names;
                }
                if (cachedPinnedNames.Count > 0)
                {
                    pinnedNamesCacheExpiresUtc = DateTime.UtcNow.AddSeconds(15);
                    return new List<string>(cachedPinnedNames);
                }
                bool wasVisible = IsWindowVisible(taskbar);
                ShowWindow(taskbar, 5);
                System.Threading.Thread.Sleep(30);
                names = CollectPinnedButtonNames(taskbar);
                if (!wasVisible) ShowWindow(taskbar, 0);
                if (names.Count > 0)
                {
                    cachedPinnedNames.Clear();
                    cachedPinnedNames.AddRange(names);
                    pinnedNamesCacheExpiresUtc = DateTime.UtcNow.AddMinutes(5);
                }
                return cachedPinnedNames.Count > 0 ? new List<string>(cachedPinnedNames) : names;
            }
            catch { return new List<string>(cachedPinnedNames); }
        }

        private void MonitorPinnedStateBackground(object ignored)
        {
            if (isClosing || Interlocked.CompareExchange(ref pinnedStateMonitorRunning, 1, 0) != 0) return;
            try
            {
                string signature = ReadPinnedStateSignature();
                if (String.IsNullOrWhiteSpace(signature)) return;
                if (String.IsNullOrWhiteSpace(pinnedStateSignature))
                {
                    pinnedStateSignature = signature;
                    return;
                }
                if (String.Equals(pinnedStateSignature, signature, StringComparison.Ordinal)) return;
                pinnedStateSignature = signature;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (isClosing) return;
                    pinnedRefreshDebounceTimer.Stop();
                    pinnedRefreshDebounceTimer.Interval = TimeSpan.FromMilliseconds(180);
                    pinnedRefreshDebounceTimer.Start();
                }));
            }
            catch { }
            finally { Interlocked.Exchange(ref pinnedStateMonitorRunning, 0); }
        }

        private static string ReadPinnedStateSignature()
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                Action<byte> append = value => { hash ^= value; hash *= 1099511628211UL; };
                string pinned = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                try
                {
                    if (Directory.Exists(pinned))
                        foreach (string file in Directory.GetFiles(pinned, "*.lnk").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                        {
                            var info = new FileInfo(file);
                            foreach (byte value in Encoding.Unicode.GetBytes(info.Name)) append(value);
                            foreach (byte value in BitConverter.GetBytes(info.Length)) append(value);
                            foreach (byte value in BitConverter.GetBytes(info.LastWriteTimeUtc.Ticks)) append(value);
                        }
                }
                catch { }

                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband", false))
                    {
                        if (key != null)
                            foreach (string valueName in new[] { "Favorites", "FavoritesResolve", "FavoritesVersion" })
                            {
                                object value = key.GetValue(valueName);
                                byte[] bytes = value as byte[];
                                if (bytes != null) foreach (byte item in bytes) append(item);
                                else if (value != null) foreach (byte item in Encoding.Unicode.GetBytes(value.ToString())) append(item);
                            }
                    }
                }
                catch { }
                return hash.ToString("X16", CultureInfo.InvariantCulture);
            }
        }

        private static List<string> CollectPinnedButtonNames(IntPtr taskbar)
        {
            var names = new List<string>();
            try
            {
                IntPtr provider = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                if (provider == IntPtr.Zero) provider = FindWindowEx(taskbar, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null);
                AutomationElement root = AutomationElement.FromHandle(provider == IntPtr.Zero ? taskbar : provider);
                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants, condition);
                foreach (AutomationElement button in buttons)
                {
                    string name = button.Current.Name;
                    if (!String.IsNullOrWhiteSpace(name) && (name.IndexOf("fixado", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("pinned", StringComparison.OrdinalIgnoreCase) >= 0)) names.Add(name);
                }
            }
            catch { }
            return names;
        }

        private static bool PinnedNameMatches(string taskbarName, TaskApp app)
        {
            if (String.IsNullOrWhiteSpace(taskbarName) || app == null || String.IsNullOrWhiteSpace(app.Name)) return false;
            if (taskbarName.StartsWith(app.Name, StringComparison.OrdinalIgnoreCase)) return true;
            string file = System.IO.Path.GetFileNameWithoutExtension(app.Target ?? String.Empty);
            return String.IsNullOrWhiteSpace(app.Identity) && String.Equals(file, "explorer", StringComparison.OrdinalIgnoreCase) &&
                (taskbarName.StartsWith("Explorador", StringComparison.OrdinalIgnoreCase) || taskbarName.StartsWith("File Explorer", StringComparison.OrdinalIgnoreCase));
        }

        private static List<TaskApp> ReadPackagedApps()
        {
            lock (packagedAppsCacheLock)
            {
                if (cachedPackagedApps.Count > 0 && DateTime.UtcNow < packagedAppsCacheExpiresUtc)
                    return ClonePackagedApps(cachedPackagedApps);
            }
            var apps = new List<TaskApp>();
            object shellObject = null;
            object folderObject = null;
            object itemsObject = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return apps;
                shellObject = Activator.CreateInstance(shellType);
                dynamic shell = shellObject;
                folderObject = shell.NameSpace("shell:AppsFolder");
                if (folderObject == null) return apps;
                dynamic folder = folderObject;
                itemsObject = folder.Items();
                dynamic items = itemsObject;
                int count = items.Count;
                for (int index = 0; index < count; index++)
                {
                    object itemObject = null;
                    try
                    {
                        itemObject = items.Item(index);
                        dynamic item = itemObject;
                        string name = item.Name as string;
                        string identity = item.Path as string;
                        if (!String.IsNullOrWhiteSpace(name) && !String.IsNullOrWhiteSpace(identity))
                            apps.Add(new TaskApp { Name = name, Target = "explorer.exe", Arguments = "shell:AppsFolder\\" + identity, Identity = identity, AppUserModelId = identity });
                    }
                    catch { }
                    finally { if (itemObject != null && Marshal.IsComObject(itemObject)) Marshal.FinalReleaseComObject(itemObject); }
                }
            }
            catch { }
            finally
            {
                if (itemsObject != null && Marshal.IsComObject(itemsObject)) Marshal.FinalReleaseComObject(itemsObject);
                if (folderObject != null && Marshal.IsComObject(folderObject)) Marshal.FinalReleaseComObject(folderObject);
                if (shellObject != null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
            }
            lock (packagedAppsCacheLock)
            {
                cachedPackagedApps.Clear();
                cachedPackagedApps.AddRange(ClonePackagedApps(apps));
                packagedAppsCacheExpiresUtc = DateTime.UtcNow.AddMinutes(5);
            }
            return apps;
        }

        private static List<TaskApp> ClonePackagedApps(IEnumerable<TaskApp> source)
        {
            return source.Select(app => new TaskApp
            {
                Name = app.Name,
                Target = app.Target,
                Arguments = app.Arguments,
                Identity = app.Identity,
                AppUserModelId = app.AppUserModelId,
                Icon = app.Icon
            }).ToList();
        }

        private static List<IntPtr> EnumerateTaskbarWindows()
        {
            var windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                bool isCascadiaWindow = GetWindowClassName(window).IndexOf("CASCADIA", StringComparison.OrdinalIgnoreCase) >= 0;
                if (IsTaskbarWindow(window) &&
                    (!String.IsNullOrWhiteSpace(GetWindowTitle(window)) || IsExplicitTaskbarWindow(window) || isCascadiaWindow))
                    windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static string GetWindowTitle(IntPtr window)
        {
            int length = GetWindowTextLength(window);
            if (length <= 0) return String.Empty;
            var text = new StringBuilder(length + 1);
            GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        private static bool IsTaskbarWindow(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindowVisible(window)) return false;

            string windowClass = GetWindowClassName(window);
            bool isCascadiaWindow = windowClass.IndexOf("CASCADIA", StringComparison.OrdinalIgnoreCase) >= 0;

            int cloaked = 0;
            int size = Marshal.SizeOf(typeof(int));
            if (DwmGetWindowAttribute(window, 14, out cloaked, size) == 0 && cloaked != 0) return false;

            long extendedStyle = GetWindowLong(window, -20);
            const long ToolWindow = 0x00000080L;
            const long AppWindow = 0x00040000L;
            bool explicitlyOnTaskbar = (extendedStyle & AppWindow) != 0;
            if ((extendedStyle & ToolWindow) != 0 && !explicitlyOnTaskbar && !isCascadiaWindow) return false;
            if (GetWindow(window, 4) != IntPtr.Zero && !explicitlyOnTaskbar && !isCascadiaWindow) return false;

            // Janelas filhas de outras janelas não aparecem na barra
            if (GetAncestor(window, 2) != window && !explicitlyOnTaskbar && !isCascadiaWindow) return false;

            string cls = windowClass;
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" || cls == "DesktopBackgroundClass" ||
                cls == "DummyDwmWallpaperWindow" || cls == "FolderViewHost")
                return false;

            // Ignora renderizadores em segundo plano do Wallpaper Engine
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid != 0)
            {
                try
                {
                    using (var proc = Process.GetProcessById((int)pid))
                    {
                        string pName = proc.ProcessName;
                        if (String.Equals(pName, "wallpaper32", StringComparison.OrdinalIgnoreCase) ||
                            String.Equals(pName, "wallpaper64", StringComparison.OrdinalIgnoreCase) ||
                            String.Equals(pName, "webwallpaper32", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
                catch { }
            }

            return true;
        }

        private static string GetWindowClassName(IntPtr window)
        {
            if (window == IntPtr.Zero) return String.Empty;
            var className = new StringBuilder(160);
            return GetClassName(window, className, className.Capacity) > 0 ? className.ToString() : String.Empty;
        }

        private static bool IsWindowsTerminalIdentity(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            return value.IndexOf("WindowsTerminal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   String.Equals(System.IO.Path.GetFileNameWithoutExtension(value), "wt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsTerminalApp(TaskApp app)
        {
            if (app == null) return false;
            return IsWindowsTerminalIdentity(app.AppUserModelId) ||
                   IsWindowsTerminalIdentity(app.Identity) ||
                   IsWindowsTerminalIdentity(app.Target) ||
                   (!String.IsNullOrWhiteSpace(app.Name) && app.Name.IndexOf("Terminal", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsSharedRuntimeProcess(string processName)
        {
            if (String.IsNullOrWhiteSpace(processName)) return false;
            switch (processName.ToLowerInvariant())
            {
                case "applicationframehost":
                case "runtimebroker":
                case "dllhost":
                case "rundll32":
                case "java":
                case "javaw":
                case "python":
                case "pythonw":
                case "node":
                case "electron":
                case "openconsole":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBrowserPwaIdentity(string appUserModelId)
        {
            if (String.IsNullOrWhiteSpace(appUserModelId)) return false;
            string value = appUserModelId.ToLowerInvariant();
            return value.Contains("_crx_") || value.Contains(".pwa.") || value.StartsWith("pwa:", StringComparison.Ordinal);
        }

        private static string BuildRunningIdentity(string appUserModelId, string processName, uint processId)
        {
            if (!String.IsNullOrWhiteSpace(appUserModelId)) return "aumid:" + appUserModelId.ToLowerInvariant();
            return "process:" + (processName ?? "application").ToLowerInvariant() + ":" + processId.ToString(CultureInfo.InvariantCulture);
        }

        private static bool RunningWindowMatchesApp(TaskApp app, bool isWindowsTerminal, bool isExplorerWindow,
            bool isSteamWindow, string windowAppId, string runningIdentity, string path, string processName)
        {
            if (app == null) return false;
            if (isWindowsTerminal) return IsWindowsTerminalApp(app);
            if (isExplorerWindow)
            {
                return String.Equals(app.Name, "Explorador de Arquivos", StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(app.Name, "File Explorer", StringComparison.OrdinalIgnoreCase) ||
                       (SameTarget(app.Target, path) && String.IsNullOrWhiteSpace(app.Arguments) && String.IsNullOrWhiteSpace(app.AppUserModelId));
            }
            if (isSteamWindow)
            {
                return String.Equals(app.Name, "Steam", StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(System.IO.Path.GetFileNameWithoutExtension(app.Target), "steam", StringComparison.OrdinalIgnoreCase) ||
                       (!String.IsNullOrWhiteSpace(app.Target) && app.Target.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!String.IsNullOrWhiteSpace(windowAppId))
            {
                if (String.Equals(app.AppUserModelId, windowAppId, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(app.Identity, windowAppId, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(app.Identity, runningIdentity, StringComparison.OrdinalIgnoreCase))
                    return true;

                // PWAs possuem o mesmo executável do navegador, mas são itens
                // independentes na barra do Windows.
                if (IsBrowserPwaIdentity(windowAppId)) return false;
            }

            if (!String.IsNullOrWhiteSpace(runningIdentity) &&
                String.Equals(app.Identity, runningIdentity, StringComparison.OrdinalIgnoreCase)) return true;

            bool usesExplorerLauncher = !String.IsNullOrWhiteSpace(app.Arguments) &&
                                        app.Arguments.IndexOf("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) >= 0;
            bool sharedRuntime = IsSharedRuntimeProcess(processName);
            if (!sharedRuntime && !usesExplorerLauncher && !String.IsNullOrWhiteSpace(path) && SameTarget(app.Target, path)) return true;
            if (!sharedRuntime && !usesExplorerLauncher &&
                String.Equals(System.IO.Path.GetFileNameWithoutExtension(app.Target), processName, StringComparison.OrdinalIgnoreCase)) return true;

            bool structuredIdentity = !String.IsNullOrWhiteSpace(app.Identity) &&
                                      (app.Identity.StartsWith("process:", StringComparison.OrdinalIgnoreCase) ||
                                       app.Identity.StartsWith("host:", StringComparison.OrdinalIgnoreCase) ||
                                       app.Identity.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase));
            if (!sharedRuntime && !structuredIdentity && !String.IsNullOrWhiteSpace(app.Identity) &&
                (processName.IndexOf(app.Identity, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 app.Identity.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0)) return true;

            return !sharedRuntime && !String.IsNullOrWhiteSpace(app.Name) &&
                   String.Equals(app.Name, processName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveWindowsTerminalTarget(string currentPath)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(currentPath))
                {
                    string fileName = System.IO.Path.GetFileName(currentPath);
                    if (String.Equals(fileName, "WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)) return currentPath;
                    string sibling = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(currentPath) ?? String.Empty, "WindowsTerminal.exe");
                    if (File.Exists(sibling)) return sibling;
                }
                string alias = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps", "wt.exe");
                if (File.Exists(alias)) return alias;
            }
            catch { }
            return currentPath ?? String.Empty;
        }

        private static bool IsExplicitTaskbarWindow(IntPtr window)
        {
            const long AppWindow = 0x00040000L;
            return window != IntPtr.Zero && (GetWindowLong(window, -20) & AppWindow) != 0;
        }

        private static string GetWindowAppUserModelId(IntPtr window)
        {
            IPropertyStore store = null;
            var value = new PropVariant();
            try
            {
                Guid storeId = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(window, ref storeId, out store) != 0 || store == null) return String.Empty;
                var key = new PropertyKey
                {
                    FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                    PropertyId = 5
                };
                if (store.GetValue(ref key, out value) != 0) return String.Empty;
                if (value.Type == 31 && value.PointerValue != IntPtr.Zero)
                    return Marshal.PtrToStringUni(value.PointerValue) ?? String.Empty;
                if (value.Type == 8 && value.PointerValue != IntPtr.Zero)
                    return Marshal.PtrToStringBSTR(value.PointerValue) ?? String.Empty;
            }
            catch { }
            finally
            {
                PropVariantClear(ref value);
                if (store != null && Marshal.IsComObject(store)) Marshal.FinalReleaseComObject(store);
            }
            return String.Empty;
        }

        private static TaskApp ResolveShortcut(string link)
        {
            object shell = null;
            object shortcutObject = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                shell = Activator.CreateInstance(type);
                dynamic dynamicShell = shell;
                shortcutObject = dynamicShell.CreateShortcut(link);
                dynamic shortcut = shortcutObject;
                string target = Environment.ExpandEnvironmentVariables((string)shortcut.TargetPath);
                string arguments = (string)shortcut.Arguments;
                string fileName = System.IO.Path.GetFileNameWithoutExtension(link);
                if (String.IsNullOrWhiteSpace(target))
                {
                    if (String.Equals(fileName, "File Explorer", StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(fileName, "Explorador de Arquivos", StringComparison.OrdinalIgnoreCase))
                    {
                        target = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    }
                    else
                    {
                        return null;
                    }
                }
                return new TaskApp { Name = fileName, Target = target, Arguments = arguments, Icon = IconFor(target) };
            }
            catch { return null; }
            finally
            {
                if (shortcutObject != null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static ImageSource GetCachedIcon(string key)
        {
            lock (iconCacheLock)
            {
                WeakReference reference;
                if (!iconCache.TryGetValue(key, out reference)) return null;
                var source = reference.Target as ImageSource;
                if (source == null) iconCache.Remove(key);
                return source;
            }
        }

        private static void CacheIcon(string key, ImageSource source)
        {
            if (source == null) return;
            lock (iconCacheLock)
            {
                iconCache[key] = new WeakReference(source);
                if (iconCache.Count <= 128) return;
                foreach (string deadKey in iconCache.Where(item => !item.Value.IsAlive).Select(item => item.Key).ToList())
                    iconCache.Remove(deadKey);
                if (iconCache.Count > 192)
                    foreach (string oldKey in iconCache.Keys.Take(iconCache.Count - 160).ToList())
                        iconCache.Remove(oldKey);
            }
        }

        private static ImageSource IconFor(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string cacheKey = "file:" + path;
            ImageSource cached = GetCachedIcon(cacheKey);
            if (cached != null) return cached;
            try
            {
                using (Drawing.Icon icon = Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(40, 40));
                    source.Freeze();
                    CacheIcon(cacheKey, source);
                    return source;
                }
            }
            catch { return null; }
        }

        private static bool IsMostlyLightIcon(ImageSource source)
        {
            BitmapSource bitmap = source as BitmapSource;
            if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return false;
            try
            {
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);
                long luminance = 0;
                int visible = 0;
                int stepX = Math.Max(1, width / 16);
                int stepY = Math.Max(1, height / 16);
                for (int y = 0; y < height; y += stepY)
                {
                    for (int x = 0; x < width; x += stepX)
                    {
                        int offset = y * stride + x * 4;
                        byte alpha = pixels[offset + 3];
                        if (alpha < 40) continue;
                        byte blue = pixels[offset];
                        byte green = pixels[offset + 1];
                        byte red = pixels[offset + 2];
                        luminance += (red * 299L + green * 587L + blue * 114L) / 1000L;
                        visible++;
                    }
                }
                return visible >= 8 && luminance / visible >= 205;
            }
            catch { return false; }
        }

        internal static Color? ExtractAccentColor(ImageSource source)
        {
            BitmapSource bitmap = source as BitmapSource;
            if (bitmap == null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return null;
            try
            {
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                int stepX = Math.Max(1, width / 32);
                int stepY = Math.Max(1, height / 32);

                double[] binWeights = new double[4096];
                long[] binR = new long[4096];
                long[] binG = new long[4096];
                long[] binB = new long[4096];

                double totalColoredWeight = 0;
                double totalWeight = 0;

                for (int y = 0; y < height; y += stepY)
                {
                    for (int x = 0; x < width; x += stepX)
                    {
                        int offset = y * stride + x * 4;
                        byte alpha = pixels[offset + 3];
                        if (alpha < 50) continue;

                        byte b = pixels[offset];
                        byte g = pixels[offset + 1];
                        byte r = pixels[offset + 2];

                        int max = Math.Max(r, Math.Max(g, b));
                        int min = Math.Min(r, Math.Min(g, b));
                        int delta = max - min;

                        bool isGray = delta < 20;
                        bool isTooDark = max < 35;
                        bool isTooBright = min > 235;

                        double weight;
                        if (isGray || isTooDark || isTooBright)
                        {
                            weight = isGray ? 0.2 : 0.05;
                        }
                        else
                        {
                            double sat = (double)delta / max;
                            weight = 1.0 + (sat * 4.0);
                            if (max >= 70 && min <= 200) weight *= 1.4;
                            totalColoredWeight += weight;
                        }

                        totalWeight += weight;
                        int bin = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                        binWeights[bin] += weight;
                        binR[bin] += (long)(r * weight);
                        binG[bin] += (long)(g * weight);
                        binB[bin] += (long)(b * weight);
                    }
                }

                if (totalWeight < 1.0) return null;

                bool preferColored = totalColoredWeight >= 4.0;
                int bestBin = -1;
                double bestWeight = -1;

                for (int bin = 0; bin < 4096; bin++)
                {
                    double w = binWeights[bin];
                    if (w <= 0.001) continue;

                    int rBin = (bin >> 8) & 0xF;
                    int gBin = (bin >> 4) & 0xF;
                    int bBin = bin & 0xF;

                    int maxBin = Math.Max(rBin, Math.Max(gBin, bBin));
                    int minBin = Math.Min(rBin, Math.Min(gBin, bBin));
                    bool isColoredBin = (maxBin - minBin) >= 2;

                    if (preferColored && !isColoredBin) continue;

                    if (w > bestWeight)
                    {
                        bestWeight = w;
                        bestBin = bin;
                    }
                }

                if (bestBin == -1)
                {
                    for (int bin = 0; bin < 4096; bin++)
                    {
                        if (binWeights[bin] > bestWeight)
                        {
                            bestWeight = binWeights[bin];
                            bestBin = bin;
                        }
                    }
                }

                if (bestBin == -1 || bestWeight <= 0.001) return null;

                byte finalR = (byte)Math.Min(255, Math.Max(0, binR[bestBin] / bestWeight));
                byte finalG = (byte)Math.Min(255, Math.Max(0, binG[bestBin] / bestWeight));
                byte finalB = (byte)Math.Min(255, Math.Max(0, binB[bestBin] / bestWeight));

                return Color.FromRgb(finalR, finalG, finalB);
            }
            catch { return null; }
        }

        private static ImageSource IconForShellItem(string identity)
        {
            return IconForShellPath("shell:AppsFolder\\" + identity);
        }

        private static ImageSource IconForShellPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string cacheKey = "shell:" + path;
            ImageSource cached = GetCachedIcon(cacheKey);
            if (cached != null) return cached;
            IShellItemImageFactory factory = null;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                Guid iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) != 0 || factory == null) return null;
                var size = new ShellSize { Width = 48, Height = 48 };
                if (factory.GetImage(size, 0x105, out bitmap) != 0 || bitmap == IntPtr.Zero) return null;
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(40, 40));
                source.Freeze();
                CacheIcon(cacheKey, source);
                return source;
            }
            catch { return null; }
            finally
            {
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (factory != null && Marshal.IsComObject(factory)) Marshal.FinalReleaseComObject(factory);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShellSize { public int Width, Height; }

        [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(ShellSize size, int flags, out IntPtr bitmap);
        }

        internal static bool SameTarget(string left, string right)
        {
            if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right)) return false;
            try { return String.Equals(System.IO.Path.GetFullPath(left), System.IO.Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); } catch { return String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        }

        internal static TaskApp ResolveShortcutPublic(string link) { return ResolveShortcut(link); }
        internal static List<TaskApp> ReadPackagedAppsPublic() { return ReadPackagedApps(); }

        public static ImageSource ResolveAppIcon(string target, string appUserModelId)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(appUserModelId))
                {
                    var img = IconForShellItem(appUserModelId);
                    if (img != null) return img;
                }
                if (!String.IsNullOrWhiteSpace(target))
                {
                    var img = IconFor(target);
                    if (img != null) return img;
                }
            }
            catch { }
            return null;
        }

        internal List<WorkspaceAppItem> GetInitialPinnedForWorkspace()
        {
            var list = new List<WorkspaceAppItem>();
            try
            {
                string pinned = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                var shortcutApps = new List<TaskApp>();
                if (Directory.Exists(pinned))
                {
                    foreach (string link in Directory.GetFiles(pinned, "*.lnk").OrderBy(p => File.GetCreationTimeUtc(p)))
                    {
                        var item = ResolveShortcut(link);
                        if (item != null && !shortcutApps.Any(a => SameTarget(a.Target, item.Target)))
                        {
                            shortcutApps.Add(item);
                        }
                    }
                }

                var pinnedNames = ReadPinnedButtonNames();
                var packaged = ReadPackagedApps();
                var orderedApps = new List<TaskApp>();

                if (pinnedNames.Count > 0)
                {
                    foreach (string pinnedName in pinnedNames)
                    {
                        var item = shortcutApps.FirstOrDefault(a => PinnedNameMatches(pinnedName, a));
                        if (item == null && (pinnedName.StartsWith("Explorador", StringComparison.OrdinalIgnoreCase) || pinnedName.StartsWith("File Explorer", StringComparison.OrdinalIgnoreCase)))
                        {
                            string explorerPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                            item = new TaskApp { Name = "Explorador de Arquivos", Target = explorerPath };
                        }
                        if (item == null)
                        {
                            item = packaged.FirstOrDefault(a => PinnedNameMatches(pinnedName, a));
                        }
                        if (item != null && !orderedApps.Any(a => SameTarget(a.Target, item.Target) && String.Equals(a.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            orderedApps.Add(item);
                        }
                    }
                }
                foreach (var item in shortcutApps)
                {
                    if (!orderedApps.Any(a => SameTarget(a.Target, item.Target))) orderedApps.Add(item);
                }

                foreach (var app in orderedApps)
                {
                    list.Add(new WorkspaceAppItem
                    {
                        Name = app.Name,
                        Target = app.Target,
                        Arguments = app.Arguments,
                        Identity = app.Identity,
                        AppUserModelId = app.AppUserModelId
                    });
                }
            }
            catch { }
            return list;
        }

        private void OpenTask(TaskApp app)
        {
            if (app.WindowHandles.Count > 0)
            {
                IntPtr foreground = foregroundBeforeDockClick != IntPtr.Zero ? foregroundBeforeDockClick : lastExternalForegroundWindow;
                foregroundBeforeDockClick = IntPtr.Zero;
                IntPtr activeTarget = MatchActiveAppWindow(app, foreground);

                if (app.WindowHandles.Count == 1)
                {
                    IntPtr target = app.WindowHandles[0];
                    if (activeTarget == target && !WindowCommands.IsMinimized(target))
                    {
                        WindowCommands.Minimize(target);
                    }
                    else
                    {
                        ActivateWindow(target);
                    }
                    return;
                }

                // Múltiplas janelas:
                // Se a janela em foco pertence ao app, alterna para a próxima janela do grupo
                if (activeTarget != IntPtr.Zero && !WindowCommands.IsMinimized(activeTarget))
                {
                    int currentIndex = app.WindowHandles.IndexOf(activeTarget);
                    if (currentIndex >= 0 && app.WindowHandles.Count > 1)
                    {
                        int nextIndex = (currentIndex + 1) % app.WindowHandles.Count;
                        ActivateWindow(app.WindowHandles[nextIndex]);
                        return;
                    }
                }

                // Nenhuma janela do app está em foco ativo: ativa a janela mais recente
                IntPtr mostRecent = app.WindowHandles
                    .OrderByDescending(h => windowActivationTicks.ContainsKey(h) ? windowActivationTicks[h] : 0)
                    .FirstOrDefault();
                if (mostRecent == IntPtr.Zero) mostRecent = app.WindowHandles[0];
                ActivateWindow(mostRecent);
                return;
            }
            if (app.IsRunningInBackground)
            {
                string exeName = System.IO.Path.GetFileNameWithoutExtension(app.Target);
                bool isWallpaperEngine = String.Equals(exeName, "wallpaper32", StringComparison.OrdinalIgnoreCase) ||
                                         String.Equals(exeName, "wallpaper64", StringComparison.OrdinalIgnoreCase) ||
                                         String.Equals(exeName, "ui32", StringComparison.OrdinalIgnoreCase) ||
                                         app.Name.IndexOf("Wallpaper Engine", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isWallpaperEngine)
                {
                    string dir = System.IO.Path.GetDirectoryName(app.Target);
                    string uiPath = System.IO.Path.Combine(dir ?? String.Empty, "ui32.exe");
                    if (File.Exists(uiPath))
                    {
                        Launch(uiPath, "-control open");
                        return;
                    }
                }
            }
            Launch(app.Target, app.Arguments);
        }

        private void MinimizeTask(TaskApp app)
        {
            if (app == null || app.WindowHandles.Count == 0) return;
            IntPtr foreground = foregroundBeforeDockClick != IntPtr.Zero ? foregroundBeforeDockClick : lastExternalForegroundWindow;
            foregroundBeforeDockClick = IntPtr.Zero;
            IntPtr target = MatchActiveAppWindow(app, foreground);
            if (target == IntPtr.Zero) target = app.WindowHandles.FirstOrDefault(handle => !WindowCommands.IsMinimized(handle));
            if (target == IntPtr.Zero) target = app.WindowHandles[0];
            WindowCommands.Minimize(target);
        }

        private void ToggleTaskGroupMinimized(TaskApp app)
        {
            if (app == null || app.WindowHandles.Count == 0) return;
            bool anyVisible = app.WindowHandles.Any(handle => WindowCommands.IsAlive(handle) && !WindowCommands.IsMinimized(handle));
            if (anyVisible)
            {
                foreach (IntPtr handle in app.WindowHandles)
                    WindowCommands.Minimize(handle);
            }
            else
            {
                var ordered = app.WindowHandles
                    .OrderBy(h => windowActivationTicks.ContainsKey(h) ? windowActivationTicks[h] : 0);
                foreach (IntPtr handle in ordered)
                    ActivateWindow(handle);
            }
            ScheduleTaskRefreshSoon();
        }

        private IntPtr MatchActiveAppWindow(TaskApp app, IntPtr candidate)
        {
            if (app == null || candidate == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = GetAncestor(candidate, 3); // GA_ROOTOWNER
            foreach (IntPtr handle in app.WindowHandles)
                if (handle == candidate || handle == root || GetAncestor(handle, 3) == root) return handle;

            uint candidateProcess;
            GetWindowThreadProcessId(candidate, out candidateProcess);
            if (candidateProcess == 0 || candidateProcess == currentProcessId) return IntPtr.Zero;
            foreach (IntPtr handle in app.WindowHandles)
            {
                uint processId;
                GetWindowThreadProcessId(handle, out processId);
                if (processId == candidateProcess) return handle;
            }
            return IntPtr.Zero;
        }

        private void StartForegroundTracking()
        {
            ReleaseForegroundTracking();
            foregroundEventDelegate = OnForegroundWindowChanged;
            foregroundEventHook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, foregroundEventDelegate, 0, 0, 0x0002);
            OnForegroundWindowChanged(IntPtr.Zero, 0x0003, GetForegroundWindow(), 0, 0, 0, 0);
        }

        private void OnForegroundWindowChanged(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (window == IntPtr.Zero) return;
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == 0 || processId == currentProcessId) return;
            if (IsStartMenuWindow(window))
            {
                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate
                    {
                        ActivateDockForExternalStart(window);
                    }));
                }
                catch { }
                return;
            }
            lastExternalForegroundWindow = window;
            windowActivationTicks[window] = Environment.TickCount;
            int now = Environment.TickCount;
            if (unchecked(now - lastForegroundTaskRefreshTick) < 100) return;
            lastForegroundTaskRefreshTick = now;
            nextTaskRefreshUtc = DateTime.MinValue;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    RefreshTaskItems();
                    RefreshFullscreenState();
                }));
            }
            catch { }
        }

        private void ReleaseForegroundTracking()
        {
            if (foregroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(foregroundEventHook);
                foregroundEventHook = IntPtr.Zero;
            }
            foregroundEventDelegate = null;
        }

        private void ActivateWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            windowActivationTicks[window] = Environment.TickCount;
            WindowCommands.Activate(window);
        }

    }
}
