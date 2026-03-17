using DocMan.Models;
using DocMan.Services;
using DocMan.UI;
using System.Diagnostics;

namespace DocMan;

class Program
{
    static async Task Main(string[] args)
    {
        var refreshInterval = 3; // seconds

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
        var runningFilter = 0; // 0=all, 1=running only, 2=not running
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
        var updatePromptShown = false;  // show at most once per session
        var showUpdatePrompt  = false;  // set when version task finds an available update

        // Background refresh tasks (each runs independently so slow operations don't block container updates)
        Task<(List<ContainerInfo> containers, DateTime fetchedAt)>? pendingContainerTask = null;
        Task<(double cpu, double memMb, double limitMb, int cores)>? pendingStatsTask = null;
        Task<(string installed, string candidate)>? pendingVersionTask = null;

        // Live log state
        var liveLogMode = false;
        var liveLogLines = new List<string>();
        var liveLogLabel = "";
        var liveLogServiceKey = ""; // "{project}/{service}" — tracks the watched service across container recreations
        var liveLogContainerId = "";
        Process? liveLogProcess = null;
        CancellationTokenSource? liveLogCts = null;
        var lastLiveLogRender = DateTime.MinValue;

        void StartLiveLogStream(ContainerInfo container)
        {
            StopLiveLogStream();
            liveLogLabel = $"{container.Project} / {container.Service}";
            liveLogServiceKey = $"{container.Project}/{container.Service}";
            liveLogContainerId = container.Id;
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

            ProcessStartInfo psi;
            if (Platform.IsWindows)
            {
                psi = new ProcessStartInfo("wsl",
                    $"docker exec -it {container.Id} sh -c 'command -v bash >/dev/null 2>&1 && exec bash || exec sh'")
                {
                    UseShellExecute = false
                };
            }
            else
            {
                psi = new ProcessStartInfo("docker",
                    $"exec -it {container.Id} sh -c 'command -v bash >/dev/null 2>&1 && exec bash || exec sh'")
                {
                    UseShellExecute = false
                };
            }

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
            liveLogServiceKey = "";
            liveLogContainerId = "";
        }

        async Task RunDockerUpdateAsync()
        {
            var uOverlay = new Overlay(6, 82, 24);
            var uLines = new List<string> { "", "Running: apt-get install --only-upgrade docker-ce ...", "", "ESC/Enter to dismiss (update continues in background)" };
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
                // Replace content with a clean result screen so the status is always visible
                var resultLines = new List<string>
                {
                    "",
                    uProc.ExitCode == 0 ? "  ✓ Update complete!" : $"  ✗ Update failed (exit code {uProc.ExitCode})",
                    "",
                    "  Press any key to close..."
                };
                uOverlay.Update(resultLines); Console.ReadKey(true); uOverlay.Hide();
            }
            uProc.Dispose();
            lastDockerVersionRefresh = DateTime.MinValue;
            containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
            needsRender = false; lastRefresh = DateTime.MinValue;
        }

        static bool IsVersionNewer(string candidate, string installed)
        {
            try { return !string.IsNullOrEmpty(candidate) && !string.IsNullOrEmpty(installed) && Version.Parse(candidate) > Version.Parse(installed); }
            catch { return false; }
        }

        string GetDockerSummary()
        {
            var running    = displayRows.Count(r => !r.IsProjectRow && r.Container?.IsRunning == true);
            var total      = displayRows.Count(r => !r.IsProjectRow);
            if (dockerInstalled.Length == 0) return "";
            var updateBadge = dockerVersionReady && IsVersionNewer(dockerCandidate, dockerInstalled) ? "(update available)" : "";
            return $"docker: {dockerInstalled}{updateBadge}  │  {running}/{total} running";
        }

        var navigateTo = AppPage.Containers;

        try
        {
            while (navigateTo != AppPage.Quit)
            {
                // Route to non-container pages
                if (navigateTo != AppPage.Containers)
                {
                    var summary = GetDockerSummary();
                    navigateTo = navigateTo switch
                    {
                        AppPage.Networks => await new NetworksViewer(dockerService).ShowAsync(summary),
                        AppPage.Images   => await new ImagesViewer(dockerService).ShowAsync(summary),
                        AppPage.Volumes  => await new VolumesViewer(dockerService).ShowAsync(summary),
                        _ => AppPage.Containers
                    };
                    // Returning to containers — force refresh
                    lastRefresh = DateTime.MinValue;
                    needsRender = true;
                    continue;
                }

                navigateTo = AppPage.Containers; // ensure we start each iteration as containers

            while (navigateTo == AppPage.Containers)
            {
                // Container refresh — fast, runs every refreshInterval
                if (pendingContainerTask == null && (DateTime.Now - lastRefresh).TotalSeconds >= refreshInterval)
                {
                    pendingContainerTask = Task.Run(async () =>
                    {
                        var c = await dockerService.GetContainersAsync();
                        return (c, DateTime.Now);
                    });
                }

                if (pendingContainerTask != null && pendingContainerTask.IsCompleted)
                {
                    try
                    {
                        if (pendingContainerTask.IsCompletedSuccessfully)
                        {
                            var (newContainers, fetchedAt) = pendingContainerTask.Result;
                            containers = newContainers;
                            lastRefresh = fetchedAt;

                            var allDisplayRows = containerListView.BuildDisplayRows(containers);
                            var newDisplayRows = runningFilter == 1
                                ? allDisplayRows.Where(row =>
                                    (row.IsProjectRow && row.ProjectContainers.Any(c => c.IsRunning)) ||
                                    (!row.IsProjectRow && row.Container != null && row.Container.IsRunning)).ToList()
                                : runningFilter == 2
                                ? allDisplayRows.Where(row =>
                                    (row.IsProjectRow && row.ProjectContainers.Any(c => !c.IsRunning)) ||
                                    (!row.IsProjectRow && row.Container != null && !row.Container.IsRunning)).ToList()
                                : allDisplayRows;

                            var newFingerprint = string.Join("|", newDisplayRows
                                .Where(row => row.Container != null)
                                .Select(row => $"{row.Container!.Id}:{row.Container.Status}:{row.Container.Health}:{row.Container.Ports}"));
                            if (newFingerprint != lastContainerFingerprint)
                            {
                                displayRows = newDisplayRows;
                                lastContainerFingerprint = newFingerprint;
                                needsRender = true;

                                // Re-attach live log if the tracked service was recreated (new container ID).
                                // Uses service key rather than selectedIndex so index drift during recreation doesn't break it.
                                if (liveLogMode && !string.IsNullOrEmpty(liveLogServiceKey))
                                {
                                    var trackedRow = displayRows.FirstOrDefault(r => !r.IsProjectRow && r.Container != null &&
                                        $"{r.Container.Project}/{r.Container.Service}" == liveLogServiceKey);
                                    if (trackedRow?.Container != null && trackedRow.Container.Id != liveLogContainerId)
                                        StartLiveLogStream(trackedRow.Container);
                                }
                            }

                            if (selectedIndex >= displayRows.Count && displayRows.Count > 0)
                                selectedIndex = displayRows.Count - 1;
                            markedIndices.RemoveWhere(i => i >= displayRows.Count);
                        }
                        else if (pendingContainerTask.IsFaulted)
                        {
                            var ex = pendingContainerTask.Exception?.InnerException ?? pendingContainerTask.Exception;
                            Screen.SetCursorPosition(0, 2);
                            Screen.WriteLine($"  {ex?.Message}".PadRight(80), ConsoleColor.Yellow);
                            lastRefresh = DateTime.Now; // back off before retry
                        }
                    }
                    finally { pendingContainerTask = null; }
                }

                // Stats refresh — slow (docker stats --no-stream), runs every 10 s
                if (pendingStatsTask == null && (DateTime.Now - lastStatsRefresh).TotalSeconds >= 10)
                {
                    pendingStatsTask = Task.Run(() => dockerService.GetTotalStatsAsync());
                }

                if (pendingStatsTask != null && pendingStatsTask.IsCompleted)
                {
                    try
                    {
                        if (pendingStatsTask.IsCompletedSuccessfully)
                        {
                            var newStats = pendingStatsTask.Result;
                            var wasReady = statsReady;
                            statsReady = true;
                            if (newStats != lastStats || !wasReady)
                            {
                                stats = newStats;
                                lastStats = newStats;
                                needsStatsRender = true;
                            }
                        }
                        else { statsReady = true; }
                    }
                    finally { pendingStatsTask = null; lastStatsRefresh = DateTime.Now; }
                }

                // Docker version refresh — potentially very slow (apt policy), runs every 60 s
                if (pendingVersionTask == null && (DateTime.Now - lastDockerVersionRefresh).TotalSeconds >= 60)
                {
                    pendingVersionTask = Task.Run(() => dockerService.GetDockerVersionInfoAsync());
                }

                if (pendingVersionTask != null && pendingVersionTask.IsCompleted)
                {
                    try
                    {
                        if (pendingVersionTask.IsCompletedSuccessfully)
                        {
                            var (inst, cand) = pendingVersionTask.Result;
                            if (inst != dockerInstalled || cand != dockerCandidate || !dockerVersionReady)
                            {
                                dockerInstalled = inst;
                                dockerCandidate = cand;
                                needsDockerVersionRender = true;
                                needsRender = true; // re-render title bar with update badge
                            }
                            dockerVersionReady = true;

                            // Trigger one-time upgrade prompt if an update is available
                            if (!updatePromptShown && !string.IsNullOrEmpty(inst) && !string.IsNullOrEmpty(cand))
                            {
                                try { if (Version.Parse(cand) > Version.Parse(inst)) showUpdatePrompt = true; }
                                catch { }
                            }
                        }
                        else { dockerVersionReady = true; }
                    }
                    finally { pendingVersionTask = null; lastDockerVersionRefresh = DateTime.Now; }
                }

                // Render UI only when something changed
                if (needsRender)
                {
                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
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

                // One-time update available prompt (fires after version check, no startup lag)
                if (showUpdatePrompt)
                {
                    showUpdatePrompt  = false;
                    updatePromptShown = true;
                    var promptOv = new Overlay(6, 72, 11);
                    var promptLines = new List<string>
                    {
                        "",
                        "  A Docker update is available!",
                        "",
                        $"    Installed : {dockerInstalled}",
                        $"    Available : {dockerCandidate}",
                        "",
                        "  Press U to upgrade now, or any other key to dismiss."
                    };
                    promptOv.Show("Docker Update Available", promptLines);
                    var promptKey = Console.ReadKey(true);
                    promptOv.Hide();
                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                    needsRender = false;
                    if (promptKey.Key == ConsoleKey.U)
                        await RunDockerUpdateAsync();
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
                            case ConsoleKey.LeftArrow:
                                StopLiveLogStream();
                                navigateTo = AppPage.Volumes;
                                break;

                            case ConsoleKey.RightArrow:
                                StopLiveLogStream();
                                navigateTo = AppPage.Networks;
                                break;

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
                                containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
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
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    // Shift+R: Restart highlighted container or project
                                    var rRows = new List<DisplayRow> { displayRows[selectedIndex] };
                                    await actionMenu.ExecuteDirectAsync(2, rRows);
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                break;

                            case ConsoleKey.X: // Prune all unused images
                                var nOv = new Overlay(6, 80, Screen.Height - 8);
                                var nLn = new List<string> { "", "Pruning all unused Docker images...", "", "ESC/Enter to close" };
                                nOv.Show("Prune All", nLn);
                                var nTask = dockerService.PruneImagesAsync();
                                while (!nTask.IsCompleted)
                                {
                                    if (Console.KeyAvailable) { var k = Console.ReadKey(true); if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.Enter) { nOv.Hide(); break; } }
                                    nOv.Update(nLn);
                                    await Task.Delay(100);
                                }
                                if (nTask.IsCompletedSuccessfully)
                                {
                                    nLn.RemoveAt(3); // remove "ESC/Enter to close" placeholder
                                    nLn.Add("");
                                    nLn.AddRange(nTask.Result);
                                    nLn.Add("");
                                    nLn.Add("Press any key to close...");
                                    nOv.Update(nLn); Console.ReadKey(true); nOv.Hide();
                                }
                                containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false; lastRefresh = DateTime.MinValue;
                                break;

                            case ConsoleKey.N: // Networks viewer
                                StopLiveLogStream();
                                liveLogMode = false;
                                navigateTo = await new NetworksViewer(dockerService).ShowAsync(GetDockerSummary());
                                lastRefresh = DateTime.MinValue; needsRender = true;
                                break;

                            case ConsoleKey.V: // Volumes viewer
                                StopLiveLogStream();
                                liveLogMode = false;
                                navigateTo = await new VolumesViewer(dockerService).ShowAsync(GetDockerSummary());
                                lastRefresh = DateTime.MinValue; needsRender = true;
                                break;

                            case ConsoleKey.W:
                                await composeService.RestartWSLAsync();
                                containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false;
                                lastRefresh = DateTime.MinValue;
                                break;

                            case ConsoleKey.I:
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    // Shift+I: Fullscreen info for highlighted container or project
                                    var iiRow = displayRows[selectedIndex];
                                    if (iiRow.IsProjectRow)
                                    {
                                        var projectViewer = new ProjectInfoViewer(dockerService);
                                        await projectViewer.ShowAsync(iiRow.Project, iiRow.ProjectContainers);
                                    }
                                    else if (iiRow.Container != null)
                                    {
                                        var infoViewer = new InfoViewer(dockerService);
                                        await infoViewer.ShowAsync(iiRow.Container);
                                    }
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                else
                                {
                                    // I: Images viewer
                                    StopLiveLogStream();
                                    liveLogMode = false;
                                    navigateTo = await new ImagesViewer(dockerService).ShowAsync(GetDockerSummary());
                                    lastRefresh = DateTime.MinValue; needsRender = true;
                                }
                                break;

                            case ConsoleKey.L:
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    // Shift+L: Fullscreen live logs for highlighted container
                                    var llRow = displayRows[selectedIndex];
                                    if (!llRow.IsProjectRow && llRow.Container != null)
                                    {
                                        var logViewer = new LogViewer(dockerService);
                                        await logViewer.ShowAsync(llRow.Container);
                                        containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                        needsRender = false; lastRefresh = DateTime.MinValue;
                                    }
                                }
                                else
                                {
                                    // L: Toggle live log panel
                                    liveLogMode = !liveLogMode;
                                    if (liveLogMode)
                                    {
                                        var lRow = displayRows[selectedIndex];
                                        if (!lRow.IsProjectRow && lRow.Container != null) StartLiveLogStream(lRow.Container);
                                        else lock (liveLogLines) { liveLogLines.Clear(); }
                                    }
                                    else { StopLiveLogStream(); }
                                }
                                break;

                            case ConsoleKey.T:
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    // Shift+T: Terminal for highlighted container
                                    var tRow = displayRows[selectedIndex];
                                    if (!tRow.IsProjectRow && tRow.Container != null && tRow.Container.IsRunning)
                                    {
                                        await OpenTerminalAsync(tRow.Container);
                                        lastRefresh = DateTime.MinValue;
                                        needsRender = true;
                                    }
                                }
                                else
                                {
                                    // T: Toggle Status filter (All → Running Only → Not Running)
                                    runningFilter = (runningFilter + 1) % 3;
                                    lastRefresh = DateTime.MinValue;
                                    selectedIndex = 0;
                                    markedIndices.Clear();
                                }
                                break;

                            case ConsoleKey.K:
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    // Shift+K: Kill highlighted container or project
                                    var kRows = new List<DisplayRow> { displayRows[selectedIndex] };
                                    await actionMenu.ExecuteDirectAsync(6, kRows);
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                break;

                            case ConsoleKey.H:
                                StopLiveLogStream();
                                liveLogMode = false;
                                await HelpViewer.ShowAsync();
                                containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                needsRender = false;
                                break;

                            case ConsoleKey.U:
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    // Shift+U: Recreate highlighted container or project
                                    var uRows = new List<DisplayRow> { displayRows[selectedIndex] };
                                    await actionMenu.ExecuteDirectAsync(3, uRows);
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                else
                                {
                                    var uConfOv = new Overlay(6, 82, 8);
                                    uConfOv.Show("Update Docker", new List<string> { "", "Update Docker Engine in WSL", "", "Press Y to confirm, any other key to cancel" });
                                    var uConfirm = Console.ReadKey(true);
                                    uConfOv.Hide();
                                    if (uConfirm.Key == ConsoleKey.Y)
                                        await RunDockerUpdateAsync();
                                    else
                                    {
                                        containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                        needsRender = false;
                                    }
                                }
                                break;

                            case ConsoleKey.S: // Stop All running containers (or Shift+S: stop highlighted)
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    var sRows = new List<DisplayRow> { displayRows[selectedIndex] };
                                    await actionMenu.ExecuteDirectAsync(1, sRows);
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                else
                                {
                                    var sRunning = containers.Where(c => c.IsRunning).ToList();
                                    if (sRunning.Count == 0) { needsRender = false; break; }
                                    // Confirm before stopping all
                                    var sConfOv = new Overlay(6, 62, 7);
                                    sConfOv.Show("Stop All", new List<string>
                                    { "", $"  Stop all {sRunning.Count} running container(s)?", "", "  ENTER to confirm  │  ESC to abort" });
                                    var sConfKey = Console.ReadKey(true); sConfOv.Hide();
                                    if (sConfKey.Key != ConsoleKey.Enter) { needsRender = true; break; }
                                    var sOv = new Overlay(6, 62, Math.Min(sRunning.Count + 8, 26));
                                    var sLn = new List<string> { "", $"  Stopping {sRunning.Count} running container(s)...", "" };
                                    sOv.Show("Stop All", sLn);
                                    foreach (var sc in sRunning)
                                    {
                                        try { await dockerService.StopContainerAsync(sc.Id); sLn.Add($"  ✓ {sc.Service}"); }
                                        catch (Exception ex) { sLn.Add($"  ✗ {sc.Service}: {ex.Message}"); }
                                        sOv.Update(sLn);
                                    }
                                    sLn.Add(""); sLn.Add("  Press any key to close...");
                                    sOv.Update(sLn); Console.ReadKey(true); sOv.Hide();
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                break;

                            case ConsoleKey.P: // Start All stopped containers (or Shift+P: start highlighted)
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    var pRows = new List<DisplayRow> { displayRows[selectedIndex] };
                                    await actionMenu.ExecuteDirectAsync(0, pRows);
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                else
                                {
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
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                break;

                            case ConsoleKey.D: // Delete Marked containers (or Shift+D: delete highlighted)
                                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                                {
                                    var dRows = new List<DisplayRow> { displayRows[selectedIndex] };
                                    await actionMenu.ExecuteDirectAsync(5, dRows);
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                else
                                {
                                    // Delete marked containers
                                    if (markedIndices.Count == 0) { needsRender = false; break; }
                                    var dMarked = markedIndices.Select(i => displayRows[i])
                                        .Where(r => !r.IsProjectRow && r.Container != null)
                                        .Select(r => r.Container!).ToList();
                                    if (dMarked.Count == 0) { needsRender = false; break; }
                                    var dOv = new Overlay(6, 64, Math.Min(dMarked.Count + 8, 26));
                                    var dLn = new List<string> { "", $"Deleting {dMarked.Count} marked container(s)...", "" };
                                    dOv.Show("Delete Marked", dLn);
                                    foreach (var dc in dMarked.Where(c => c.IsRunning))
                                    {
                                        try { await dockerService.StopContainerAsync(dc.Id); } catch { }
                                    }
                                    foreach (var dc in dMarked)
                                    {
                                        try { await dockerService.DeleteContainerAsync(dc.Id); dLn.Add($"✓ {dc.Service}"); }
                                        catch (Exception ex) { dLn.Add($"✗ {dc.Service}: {ex.Message}"); }
                                        dOv.Update(dLn);
                                    }
                                    markedIndices.Clear();
                                    dLn.Add(""); dLn.Add("Press any key to close...");
                                    dOv.Update(dLn); Console.ReadKey(true); dOv.Hide();
                                    containerListView.Render(displayRows, selectedIndex, markedIndices, runningFilter, stats, statsReady, liveLogMode, liveLogLines, liveLogLabel, dockerInstalled, dockerCandidate, dockerVersionReady);
                                    needsRender = false; lastRefresh = DateTime.MinValue;
                                }
                                break;

                            case ConsoleKey.Q:
                                StopLiveLogStream();
                                navigateTo = AppPage.Quit;
                                break;
                        }

                        break;
                    }

                    await Task.Delay(10);
                }

                // Refresh live log panel independently of container list refresh
                if (liveLogMode && (DateTime.Now - lastLiveLogRender).TotalMilliseconds >= 200)
                {
                    // If the stream process died (container removed/recreated), re-attach as soon as the
                    // tracked service is running again — without waiting for the user to move the selection.
                    if (!string.IsNullOrEmpty(liveLogServiceKey) && liveLogProcess != null && liveLogProcess.HasExited)
                    {
                        var restartRow = displayRows.FirstOrDefault(r => !r.IsProjectRow && r.Container != null &&
                            $"{r.Container.Project}/{r.Container.Service}" == liveLogServiceKey &&
                            r.Container.IsRunning);
                        if (restartRow?.Container != null)
                            StartLiveLogStream(restartRow.Container);
                    }

                    List<string> snapshot;
                    lock (liveLogLines) { snapshot = liveLogLines.TakeLast(ContainerListView.LogPanelLines).ToList(); }
                    containerListView.RenderLiveLogPanel(snapshot, liveLogLabel);
                    lastLiveLogRender = DateTime.Now;
                }
            } // end inner container while

            } // end outer routing while
        }
        finally
        {
            StopLiveLogStream();
            Console.Clear();
            Console.CursorVisible = true;
        }
    }
}

