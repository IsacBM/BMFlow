using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DockBar
{
    public sealed partial class DockWindow
    {
        public void SwitchToWorkspace(int index)
        {
            if (workspaceManager == null || index < 0 || index >= workspaceManager.Workspaces.Count) return;
            if (workspaceManager.ActiveIndex == index)
            {
                var current = workspaceManager.ActiveWorkspace;
                if (current != null)
                {
                    ShowWorkspaceHud(
                        String.IsNullOrWhiteSpace(current.Icon) ? "💼" : current.Icon,
                        current.Name,
                        "Espaço " + (index + 1) + " • Ativo (Ctrl+Alt+" + (index + 1) + ")");
                }
                return;
            }

            workspaceManager.ActiveIndex = index;
            RefreshTopBarWorkspaces();
            var ws = workspaceManager.ActiveWorkspace;
            if (ws != null)
            {
                ShowWorkspaceHud(
                    String.IsNullOrWhiteSpace(ws.Icon) ? "💼" : ws.Icon,
                    ws.Name,
                    "Espaço " + (index + 1) + " • Ctrl+Alt+" + (index + 1));
            }

            // Atualização imediata (0ms de delay!)
            RefreshTaskItemsImmediate();

            // Salva o índice ativo em background para não bloquear o input
            ThreadPool.QueueUserWorkItem(delegate { workspaceManager.Save(); });
        }

        public void TriggerDockRebuild()
        {
            RefreshTaskItemsImmediate();
        }

        internal void LaunchOrRestoreWorkspaceApps(DockWorkspace ws)
        {
            if (ws == null) ws = workspaceManager != null ? workspaceManager.ActiveWorkspace : null;
            if (ws == null) return;
            var appsToProcess = (ws.Apps.Count > 0)
                ? ws.Apps
                : (ws.SyncWithWindows ? GetInitialPinnedForWorkspace() : new List<WorkspaceAppItem>());
            if (appsToProcess.Count == 0) return;

            var openWindows = EnumerateTaskbarWindows().ToList();
            int launched = 0;
            int restored = 0;

            foreach (var app in appsToProcess)
            {
                bool broughtToFront = false;
                foreach (IntPtr hWnd in openWindows)
                {
                    try
                    {
                        uint pid;
                        GetWindowThreadProcessId(hWnd, out pid);
                        if (pid == 0) continue;
                        using (var proc = Process.GetProcessById((int)pid))
                        {
                            string path = proc.MainModule == null ? String.Empty : proc.MainModule.FileName;
                            string procName = proc.ProcessName;
                            string appId = GetWindowAppUserModelId(hWnd);

                            bool isSteamMatch = (String.Equals(procName, "steamwebhelper", StringComparison.OrdinalIgnoreCase) || String.Equals(procName, "steam", StringComparison.OrdinalIgnoreCase)) &&
                                                (String.Equals(app.Name, "Steam", StringComparison.OrdinalIgnoreCase) || String.Equals(System.IO.Path.GetFileNameWithoutExtension(app.Target), "steam", StringComparison.OrdinalIgnoreCase));

                            bool match = isSteamMatch ||
                                         (!String.IsNullOrWhiteSpace(app.AppUserModelId) && String.Equals(appId, app.AppUserModelId, StringComparison.OrdinalIgnoreCase)) ||
                                         SameTarget(app.Target, path) ||
                                         String.Equals(System.IO.Path.GetFileNameWithoutExtension(app.Target), procName, StringComparison.OrdinalIgnoreCase);

                            if (match)
                            {
                                WindowCommands.Activate(hWnd);
                                broughtToFront = true;
                                restored++;
                            }
                        }
                    }
                    catch { }
                }

                if (!broughtToFront)
                {
                    try
                    {
                        if (File.Exists(app.Target))
                        {
                            Launch(app.Target, app.Arguments);
                            launched++;
                        }
                        else if (!String.IsNullOrWhiteSpace(app.AppUserModelId) && app.AppUserModelId.IndexOf('\\') < 0 && app.AppUserModelId.IndexOf(':') < 0 && !app.AppUserModelId.StartsWith("{"))
                        {
                            Launch("explorer.exe", @"shell:AppsFolder\" + app.AppUserModelId);
                            launched++;
                        }
                        else if (!String.IsNullOrWhiteSpace(app.Target))
                        {
                            Launch(app.Target, app.Arguments);
                            launched++;
                        }
                    }
                    catch { }
                }
            }

            ShowWorkspaceHud(
                ws.Icon ?? "🚀",
                "Espaço: " + ws.Name,
                restored > 0 ? (restored + " janela(s) restaurada(s), " + launched + " aberta(s)") : (launched + " aplicativo(s) iniciado(s)"));
        }

        private void RegisterWorkspaceHotKeys()
        {
            if (dockHandle == IntPtr.Zero) return;
            UnregisterWorkspaceHotKeys();
            uint modsWithNoRepeat = 0x0001 | 0x0002 | 0x4000;
            uint modsSimple = 0x0001 | 0x0002;

            for (int i = 0; i < 9; i++)
            {
                uint vkDigit = (uint)(0x31 + i);
                uint vkNumpad = (uint)(0x61 + i);
                int idDigit = 0x9100 + i;
                int idNumpad = 0x9200 + i;

                if (!RegisterHotKey(dockHandle, idDigit, modsWithNoRepeat, vkDigit))
                    RegisterHotKey(dockHandle, idDigit, modsSimple, vkDigit);

                if (!RegisterHotKey(dockHandle, idNumpad, modsWithNoRepeat, vkNumpad))
                    RegisterHotKey(dockHandle, idNumpad, modsSimple, vkNumpad);
            }
        }

        private void UnregisterWorkspaceHotKeys()
        {
            if (dockHandle == IntPtr.Zero) return;
            for (int i = 0; i < 9; i++)
            {
                try { UnregisterHotKey(dockHandle, 0x9100 + i); } catch { }
                try { UnregisterHotKey(dockHandle, 0x9200 + i); } catch { }
            }
        }

    }
}
