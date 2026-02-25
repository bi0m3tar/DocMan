using DocMan.Models;
using DocMan.Services;
using DocMan.UI;
using System.Diagnostics;

namespace DocMan;

class Program
{
    static async Task Main(string[] args)
    {
        var refreshInterval = 5; // seconds
        
        // Parse command line arguments
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-Interval" && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out refreshInterval);
            }
        }

        var prereqError = await DockerService.CheckPrerequisitesAsync();
        if (prereqError != null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("DocMan cannot start:");
            Console.ResetColor();
            Console.Error.WriteLine();
            Console.Error.WriteLine(prereqError);
            Console.Error.WriteLine();
            Environment.Exit(1);
        }

        var dockerService = new DockerService();
        var composeService = new ComposeService();
        var containerListView = new ContainerListView();
        var actionMenu = new ActionMenu(dockerService, composeService);

        Screen.Initialize();

        var containers = new List<ContainerInfo>();
        var displayRows = new List<DisplayRow>();
        var selectedIndex = 0;
        var markedIndices = new HashSet<int>();
        var lastRefresh = DateTime.MinValue;
        var lastStatsRefresh = DateTime.Now; // defer stats until after first render
        var showOnlyRunning = false;
        (double cpu, double memMb, double limitMb, int cores) stats = default;
        var needsRender = true;
        var needsStatsRender = false;
        var lastContainerFingerprint = "";
        var lastStats = stats;
        var statsReady = false;

        // Docker version state
        var dockerInstalled = "";
        var dockerCandidate = "";
        var dockerVersionReady = false;
        var lastDockerVersionRefresh = DateTime.MinValue;
        var needsDockerVersionRender = false;

        // Live log state
        var liveLogMode = false;
        var liveLogLines = new List<string>();
        var liveLogLabel = "";
        Process? liveLogProcess = null;
        CancellationTokenSource? liveLogCts = null;
        var lastLiveLogRender = DateTime.MinValue;

        void StartLiveLogStream(ContainerInfo container)
        {
            StopLiveLogStream();
            liveLogLabel = $"{container.Project} / {container.Service}";
            lock (liveLogLines) { liveLogLines.Clear(); }
            liveLogCts = new CancellationTokenSource();
            var token = liveLogCts.Token;
            liveLogProcess = dockerService.StartLogsProcess(container.Id);
            var proc = liveLogProcess;
            _ = Task.Run(async () =>
            {
                try
                {
                    var t1 = Task.Run(async () => {
                        while (!proc.StandardOutput.EndOfStream && !token.IsCancellationRequested)
                        { var l = await proc.StandardOutput.ReadLineAsync(); if (l != null) lock (liveLogLines) { liveLogLines.Add(l); if (liveLogLines.Count > 500) liveLogLines.RemoveAt(0); } }
                    });
                    var t2 = Task.Run(async () => {
                        while (!proc.StandardError.EndOfStream && !token.IsCancellationRequested)
                        { var l = await proc.StandardError.ReadLineAsync(); if (l != null) lock (liveLogLines) { liveLogLines.Add(l); if (liveLogLines.Count > 500) liveLogLines.RemoveAt(0); } }
                    });
                    await Task.WhenAll(t1, t2);
                }
                catch { }
            }, token);
        }

        async Task OpenTerminalAsync(ContainerInfo container)
        {
            StopLiveLogStream();

            Console.Clear();
            Console.CursorVisible = true;
            Console.ResetColor();

            var width = Console.WindowWidth;
            var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "?");

            // Row 0: title bar — same green style as the main TUI
            Console.SetCursorPosition(0, 0);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(("DocMan - DOcker Container MANager  " + appVersion).PadRight(width));

            // Row 1: container info — white, like Inspect mode
            Console.SetCursorPosition(0, 1);
            Console.ForegroundColor = ConsoleColor.White;
            var header = $"--- TERMINAL: {container.Project} / {container.Service} --- type 'exit' or Ctrl+D to return ---";
            Console.Write(header.PadRight(width));

            // Row 2: separator
            Console.SetCursorPosition(0, 2);
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(new string('-', width));
            Console.ResetColor();

            // Freeze rows 0-2; all scrolling confined to row 3 downwards
            Screen.SetScrollRegion(3);

            var psi = new ProcessStartInfo("wsl",
                $"docker exec -it {container.Id} sh -c 'command -v bash >/dev/null 2>&1 && exec bash || exec sh'")
            {
                UseShellExecute = false
            };

            try
            {
                using var proc = Process.Start(psi)!;
                await proc.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine("  Press any key to return...");
                Console.ReadKey(true);
            }

            Screen.ResetScrollRegion();
            Screen.Initialize();
        }

        void StopLiveLogStream()
        {
            try { liveLogCts?.Cancel(); } catch { }
            try { if (liveLogProcess != null && !liveLogProcess.HasExited) liveLogProcess.Kill(); } catch { }
            liveLogProcess?.Dispose();
            liveLogProcess = null;
            liveLogCts = null;
        }

        try
        {
            while (true)
            {
                // Refresh container list
                if ((DateTime.Now - lastRefresh).TotalSeconds >= refreshInterval)
                {
                    try
                    {
                        containers = await dockerService.GetContainersAsync();
                        
                        // Build display rows with ALL containers (so project rows have all containers)
                        // Then filter the display if needed
                        var allDisplayRows = containerListView.BuildDisplayRows(containers);
                        
                        var newDisplayRows = showOnlyRunning 
                            ? allDisplayRows.Where(r =>
                                (r.IsProjectRow && r.ProjectContainers.Any(c => c.IsRunning)) ||
                                (!r.IsProjectRow && r.Container != null && r.Container.IsRunning)).ToList()
                            : allDisplayRows;
                            
                        lastRefresh = DateTime.Now;

                        // Only re-render if container data actually changed
                        var newFingerprint = string.Join("|", newDisplayRows
                            .Where(r => r.Container != null)
                            .Select(r => $"{r.Container!.Id}:{r.Container.Status}:{r.Container.Health}:{r.Container.Ports}"));
                        if (newFingerprint != lastContainerFingerprint)
                        {
                            displayRows = newDisplayRows;
                            lastContainerFingerprint = newFingerprint;
                            needsRender = true;
                        }

                        // Refresh stats every 10 seconds (docker stats is slow)
                        if ((DateTime.Now - lastStatsRefresh).TotalSeconds >= 10)
                        {
                            try
                            {
                                var newStats = await dockerService.GetTotalStatsAsync();
                                var wasReady = statsReady;
                                statsReady = true;
                                if (newStats != lastStats || !wasReady)
                                {
                                    stats = newStats;
                                    lastStats = newStats;
                                    needsStatsRender = true;
                                }
                            }
                            catch { }
                            lastStatsRefresh = DateTime.Now;
                        }

                        // Refresh docker version every 60 seconds
                        if ((DateTime.Now - lastDockerVersionRefresh).TotalSeconds >= 60)
                        {
                            try
                            {
                                var (inst, cand) = await dockerService.GetDockerVersionInfoAsync();
                                if (inst != dockerInstalled || cand != dockerCandidate || !dockerVersionReady)
                                {
                                    dockerInstalled = inst;
                                    dockerCandidate = cand;
                                    dockerVersionReady = true;
                                    needsDockerVersionRender = true;
                                }
                                else { dockerVersionReady = true; }
                            }
                            catch { dockerVersionReady = true; }
                            lastDockerVersionRefresh = DateTime.Now;
                        }

                        // Adjust selection if out of bounds
                        if (selectedIndex >= displayRows.Count && displayRows.Count > 0)
                        {
                            selectedIndex = displayRows.Count - 1;
                        }
                        
                        // Clean up marked indices
                        markedIndices.RemoveWhere(i => i >= displayRows.Count);
                    }
                    catch (Exception ex)
                    {
                        Screen.SetCursorPosition(0, 2);
                        Screen.WriteLine($"  {ex.Message}".PadRight(80), ConsoleColor.Yellow);
                        await Task.Delay(1000);
                        continue;
                    }
                }

                // Render UI only when something changed
                if (needsRender)
                {
                    containerListView.Render(displayRows, selectedIndex, markedIndices, showOnlyRunning, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                    needsRender = false;
                    needsStatsRender = false;
                    needsDockerVersionRender = false;
                }
                else if (needsStatsRender)
                {
                    containerListView.RenderStats(stats, statsReady);
                    needsStatsRender = false;
                }
                else if (needsDockerVersionRender)
                {
                    containerListView.RenderDockerVersion(dockerInstalled, dockerCandidate, dockerVersionReady);
                    needsDockerVersionRender = false;
                }

                // Handle input
                var timeout = TimeSpan.FromMilliseconds(100);
                var start = DateTime.Now;

                while ((DateTime.Now - start) < timeout)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        needsRender = true;

                        switch (key.Key)
                        {
                            case ConsoleKey.UpArrow:
                                if (selectedIndex > 0)
                                {
                                    selectedIndex--;
                                    if (liveLogMode)
                                    {
                                        var nr = displayRows[selectedIndex];
                                        if (!nr.IsProjectRow && nr.Container != null) StartLiveLogStream(nr.Container);
                                        else lock (liveLogLines) { liveLogLines.Clear(); }
                                    }
                                }
                                break;

                            case ConsoleKey.DownArrow:
                                if (selectedIndex < displayRows.Count - 1)
                                {
                                    selectedIndex++;
                                    if (liveLogMode)
                                    {
                                        var nr = displayRows[selectedIndex];
                                        if (!nr.IsProjectRow && nr.Container != null) StartLiveLogStream(nr.Container);
                                        else lock (liveLogLines) { liveLogLines.Clear(); }
                                    }
                                }
                                break;

                            case ConsoleKey.Spacebar:
                                var currentRow = displayRows[selectedIndex];
                                if (markedIndices.Contains(selectedIndex))
                                {
                                    // Unmark current row
                                    markedIndices.Remove(selectedIndex);
                                    
                                    // If it's a project row, unmark all its services
                                    if (currentRow.IsProjectRow)
                                    {
                                        for (int i = selectedIndex + 1; i < displayRows.Count; i++)
                                        {
                                            if (displayRows[i].IsProjectRow) break;
                                            markedIndices.Remove(i);
                                        }
                                    }
                                }
                                else
                                {
                                    // Mark current row
                                    markedIndices.Add(selectedIndex);
                                    
                                    // If it's a project row, mark all its services
                                    if (currentRow.IsProjectRow)
                                    {
                                        for (int i = selectedIndex + 1; i < displayRows.Count; i++)
                                        {
                                            if (displayRows[i].IsProjectRow) break;
                                            markedIndices.Add(i);
                                        }
                                    }
                                }
                                break;

                            case ConsoleKey.Enter:
                                var selected = markedIndices.Count > 0
                                    ? markedIndices.Select(i => displayRows[i]).ToList()
                                    : new List<DisplayRow> { displayRows[selectedIndex] };

                                var terminalContainer = await actionMenu.ShowAsync(selected);
                                markedIndices.Clear();

                                // Restore background immediately with cached data, then force Docker refresh
                                containerListView.Render(displayRows, selectedIndex, markedIndices, showOnlyRunning, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false;
                                lastRefresh = DateTime.MinValue;

                                if (terminalContainer != null)
                                {
                                    await OpenTerminalAsync(terminalContainer);
                                    lastRefresh = DateTime.MinValue;
                                    needsRender = true;
                                }
                                break;

                            case ConsoleKey.R:
                                // Toggle show only running containers
                                showOnlyRunning = !showOnlyRunning;
                                lastRefresh = DateTime.MinValue;
                                selectedIndex = 0;
                                markedIndices.Clear();
                                break;

                            case ConsoleKey.W:
                                await composeService.RestartWSLAsync();
                                containerListView.Render(displayRows, selectedIndex, markedIndices, showOnlyRunning, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false;
                                lastRefresh = DateTime.MinValue;
                                break;

                            case ConsoleKey.I:
                                liveLogMode = !liveLogMode;
                                if (liveLogMode)
                                {
                                    var iRow = displayRows[selectedIndex];
                                    if (!iRow.IsProjectRow && iRow.Container != null) StartLiveLogStream(iRow.Container);
                                    else lock (liveLogLines) { liveLogLines.Clear(); }
                                }
                                else { StopLiveLogStream(); }
                                break;

                            case ConsoleKey.U:
                                var uOverlay = new Overlay(6, 82, 24);
                                var uLines = new List<string> { "", "Update Docker Engine in WSL", "", "Press Y to confirm, any other key to cancel" };
                                uOverlay.Show("Update Docker", uLines);
                                var uConfirm = Console.ReadKey(true);
                                if (uConfirm.Key != ConsoleKey.Y) { uOverlay.Hide(); break; }
                                uLines = new List<string> { "", "Running: apt-get install --only-upgrade docker-ce ...", "", "ESC/Enter to dismiss (update continues in background)" };
                                uOverlay.Show("Update Docker", uLines);
                                var uProc = dockerService.StartUpdateProcess();
                                var uDone = uProc.WaitForExitAsync();
                                var uEarly = false;
                                _ = Task.Run(async () =>
                                {
                                    var t1 = Task.Run(async () => { while (!uProc.StandardOutput.EndOfStream) { var l = await uProc.StandardOutput.ReadLineAsync(); if (l != null) lock (uLines) { uLines.Add(l.Length > 77 ? l[..77] : l); } } });
                                    var t2 = Task.Run(async () => { while (!uProc.StandardError.EndOfStream) { var l = await uProc.StandardError.ReadLineAsync(); if (l != null) lock (uLines) { uLines.Add(l.Length > 77 ? l[..77] : l); } } });
                                    await Task.WhenAll(t1, t2);
                                });
                                while (!uDone.IsCompleted)
                                {
                                    if (Console.KeyAvailable) { var k = Console.ReadKey(true); if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.Enter) { uEarly = true; uOverlay.Hide(); break; } }
                                    List<string> uSnap; lock (uLines) { uSnap = new List<string>(uLines); }
                                    uOverlay.Update(uSnap);
                                    await Task.Delay(150);
                                }
                                if (!uEarly)
                                {
                                    await uDone;
                                    List<string> uFinal; lock (uLines) { uFinal = new List<string>(uLines); }
                                    uFinal.Add(""); uFinal.Add(uProc.ExitCode == 0 ? "✓ Update complete" : $"✗ Failed (exit {uProc.ExitCode})");
                                    uFinal.Add(""); uFinal.Add("Press any key to close...");
                                    uOverlay.Update(uFinal); Console.ReadKey(true); uOverlay.Hide();
                                }
                                uProc.Dispose();
                                lastDockerVersionRefresh = DateTime.MinValue;
                                containerListView.Render(displayRows, selectedIndex, markedIndices, showOnlyRunning, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false; lastRefresh = DateTime.MinValue;
                                break;

                            case ConsoleKey.S: // Stop All running containers
                                var sRunning = containers.Where(c => c.IsRunning).ToList();
                                if (sRunning.Count == 0) { needsRender = false; break; }
                                var sOv = new Overlay(6, 62, Math.Min(sRunning.Count + 8, 26));
                                var sLn = new List<string> { "", $"Stopping {sRunning.Count} running container(s)...", "" };
                                sOv.Show("Stop All", sLn);
                                foreach (var sc in sRunning)
                                {
                                    try { await dockerService.StopContainerAsync(sc.Id); sLn.Add($"✓ {sc.Service}"); }
                                    catch (Exception ex) { sLn.Add($"✗ {sc.Service}: {ex.Message}"); }
                                    sOv.Update(sLn);
                                }
                                sLn.Add(""); sLn.Add("Press any key to close...");
                                sOv.Update(sLn); Console.ReadKey(true); sOv.Hide();
                                containerListView.Render(displayRows, selectedIndex, markedIndices, showOnlyRunning, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false; lastRefresh = DateTime.MinValue;
                                break;

                            case ConsoleKey.P: // Start All stopped containers
                                var pStopped = containers.Where(c => !c.IsRunning).ToList();
                                if (pStopped.Count == 0) { needsRender = false; break; }
                                var pOv = new Overlay(6, 62, Math.Min(pStopped.Count + 8, 26));
                                var pLn = new List<string> { "", $"Starting {pStopped.Count} stopped container(s)...", "" };
                                pOv.Show("Start All", pLn);
                                foreach (var pc in pStopped)
                                {
                                    try { await dockerService.StartContainerAsync(pc.Id); pLn.Add($"✓ {pc.Service}"); }
                                    catch (Exception ex) { pLn.Add($"✗ {pc.Service}: {ex.Message}"); }
                                    pOv.Update(pLn);
                                }
                                pLn.Add(""); pLn.Add("Press any key to close...");
                                pOv.Update(pLn); Console.ReadKey(true); pOv.Hide();
                                containerListView.Render(displayRows, selectedIndex, markedIndices, showOnlyRunning, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false; lastRefresh = DateTime.MinValue;
                                break;

                            case ConsoleKey.D: // Delete All containers (with confirmation)
                                var dAll = containers.ToList();
                                if (dAll.Count == 0) { needsRender = false; break; }
                                var dConfOv = new Overlay(6, 64, 10);
                                var dConfLn = new List<string> { "", $"Delete ALL {dAll.Count} container(s)?", "", "Running containers will be stopped first.", "", "Press Y to confirm, any other key to cancel" };
                                dConfOv.Show("Delete All", dConfLn);
                                var dConf = Console.ReadKey(true);
                                dConfOv.Hide();
                                if (dConf.Key != ConsoleKey.Y) { needsRender = false; break; }
                                var dOv = new Overlay(6, 64, Math.Min(dAll.Count + 8, 26));
                                var dLn = new List<string> { "", $"Deleting {dAll.Count} container(s)...", "" };
                                dOv.Show("Delete All", dLn);
                                foreach (var dc in dAll.Where(c => c.IsRunning))
                                {
                                    try { await dockerService.StopContainerAsync(dc.Id); } catch { }
                                }
                                foreach (var dc in dAll)
                                {
                                    try { await dockerService.DeleteContainerAsync(dc.Id); dLn.Add($"✓ {dc.Service}"); }
                                    catch (Exception ex) { dLn.Add($"✗ {dc.Service}: {ex.Message}"); }
                                    dOv.Update(dLn);
                                }
                                dLn.Add(""); dLn.Add("Press any key to close...");
                                dOv.Update(dLn); Console.ReadKey(true); dOv.Hide();
                                containerListView.Render(displayRows, selectedIndex, markedIndices, showOnlyRunning, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false; lastRefresh = DateTime.MinValue;
                                break;

                            case ConsoleKey.Q:
                                StopLiveLogStream();
                                return;
                        }

                        break;
                    }

                    await Task.Delay(10);
                }

                // Refresh live log panel independently of container list refresh
                if (liveLogMode && (DateTime.Now - lastLiveLogRender).TotalMilliseconds >= 200)
                {
                    List<string> snapshot;
                    lock (liveLogLines) { snapshot = liveLogLines.TakeLast(ContainerListView.LogPanelLines).ToList(); }
                    containerListView.RenderLiveLogPanel(snapshot, liveLogLabel);
                    lastLiveLogRender = DateTime.Now;
                }
            }
        }
        finally
        {
            StopLiveLogStream();
            Console.Clear();
            Console.CursorVisible = true;
        }
    }
}
