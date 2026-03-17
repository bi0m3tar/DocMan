using DocMan.Models;

namespace DocMan.UI;

public class ContainerListView
{
    private const int HeaderLines = 3;
    public const int LogPanelLines = 18;
    private const int LogPanelHeight = LogPanelLines + 2; // separator + label + log lines
    private int _topRow;
    private int _statsRow = -1;
    private int _dockerVersionRow = -1;
    private int _runningCount = -1;
    private int _totalCount = -1;

    public List<DisplayRow> BuildDisplayRows(List<ContainerInfo> containers)
    {
        var displayRows = new List<DisplayRow>();

        // Standalone containers first (single row each, no project header)
        var standalone = containers.Where(c => c.IsStandalone).OrderBy(c => c.Name);
        foreach (var container in standalone)
        {
            displayRows.Add(new DisplayRow
            {
                IsProjectRow = false,
                IsStandalone = true,
                Project = container.Project,
                Container = container,
                ProjectContainers = new List<ContainerInfo> { container }
            });
        }

        // Compose projects (header + indented containers)
        var grouped = containers.Where(c => !c.IsStandalone).GroupBy(c => c.Project).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            var projectContainers = group.ToList();

            displayRows.Add(new DisplayRow
            {
                IsProjectRow = true,
                Project = group.Key,
                ProjectContainers = projectContainers
            });

            foreach (var container in projectContainers)
            {
                displayRows.Add(new DisplayRow
                {
                    IsProjectRow = false,
                    Project = group.Key,
                    Container = container,
                    ProjectContainers = projectContainers
                });
            }
        }

        return displayRows;
    }

    public void Render(List<DisplayRow> displayRows, int selectedIndex, HashSet<int> markedIndices, int runningFilter, (double cpu, double memMb, double limitMb, int cores) stats = default, bool statsReady = false, bool liveLogMode = false, List<string>? liveLogLines = null, string? liveLogLabel = null, string dockerInstalled = "", string dockerCandidate = "", bool dockerVersionReady = false)
    {
        Screen.SetCursorPosition(0, 0);
        var width = Console.WindowWidth;

        // Row 0: title bar with docker info right-aligned
        var running = displayRows.Count(r => !r.IsProjectRow && r.Container?.IsRunning == true);
        var total   = displayRows.Count(r => !r.IsProjectRow);
        var outdated = dockerVersionReady && IsVersionNewer(dockerCandidate, dockerInstalled);
        var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");
        var titleBase  = $"DocMan - DOcker Container MANager  {appVersion}";
        var flagParts  = new List<(string text, ConsoleColor color)>();
        if (runningFilter == 1) flagParts.Add(("  [RUNNING ONLY]", ConsoleColor.Green));
        else if (runningFilter == 2) flagParts.Add(("  [NOT RUNNING]", ConsoleColor.Yellow));
        if (liveLogMode)     flagParts.Add(("  [LIVE LOGS ON]",  ConsoleColor.Yellow));
        int flagsLen = flagParts.Sum(p => p.text.Length);

        // Build right-side summary segments (version, optional update badge, running count)
        string summaryBase   = dockerInstalled.Length > 0 ? $"  docker: {dockerInstalled}" : "";
        string updateBadge   = (outdated && dockerInstalled.Length > 0) ? "(update available)" : "";
        string summarySuffix = dockerInstalled.Length > 0 ? $"  │  {running}/{total} running  " : "";
        int    rightLen      = summaryBase.Length + updateBadge.Length + summarySuffix.Length;
        int    padding       = Math.Max(0, 184 - titleBase.Length - flagsLen - rightLen);

        Screen.Write(titleBase, ConsoleColor.Green);
        foreach (var (text, color) in flagParts) Screen.Write(text, color);
        Screen.Write(new string(' ', padding), ConsoleColor.Green);
        if (dockerInstalled.Length > 0)
        {
            Screen.Write(summaryBase, ConsoleColor.DarkGray);
            if (updateBadge.Length > 0) Screen.Write(updateBadge, ConsoleColor.Red);
            Screen.Write(summarySuffix, ConsoleColor.DarkGray);
        }
        Screen.Write("\n");

        // Row 1: global nav
        AppNav.RenderGlobalNav(AppPage.Containers, width);
        Screen.Write("\n");

        // Row 2: container-specific controls
        var controls2 = "↑↓:Navigate     │  SPACE:Mark  ENTER:Actions  │  P:Start All  S:Stop All  D:Delete Marked  L:Live Logs  T:Toggle Status";
        Screen.WriteLine(controls2.PadRight(184), ConsoleColor.Cyan);
        
        // Column headers
        Screen.WriteLine(new string('-', 184), ConsoleColor.DarkGray);
        Screen.WriteLine(string.Format("{0,-3} {1,-41} {2,-12} {3,-59} {4,-24} {5,-28} {6,-10}", 
            " M", "NAME", "ID", "IMAGE", "PORTS", "STATUS", "HEALTH").PadRight(184), ConsoleColor.Green);
        Screen.WriteLine(new string('-', 184), ConsoleColor.DarkGray);

        _topRow = 6;

        for (int i = 0; i < displayRows.Count; i++)
        {
            var row = displayRows[i];
            var marker = markedIndices.Contains(i) ? "[x]" : "[ ]";

            string line;
            ConsoleColor contentColor;
            
            if (row.IsProjectRow)
            {
                // Project header row
                var name = row.Project;
                if (name.Length > 41) name = name[..41];

                line = string.Format("{0,-3} {1,-41} {2,-12} {3,-59} {4,-24} {5,-28} {6,-10}",
                    marker, name, "", "", "", "", "");
                contentColor = ConsoleColor.Cyan;
            }
            else if (row.IsStandalone)
            {
                // Standalone container — name in magenta, rest in status color
                var container = row.Container!;

                var name = container.Name;
                if (name.Length > 41) name = name[..41];

                var image = container.Image;
                if (image.Length > 59) image = image[..59];

                var status = container.Status;
                if (status.Length > 28) status = status[..28];

                var ports = container.Ports;
                if (ports.Length > 24) ports = ports[..24];

                var health = container.Health == "none" ? "-" : container.Health;
                var shortId = container.Id.Length > 12 ? container.Id[..12] : container.Id;
                var statusColor = Screen.GetStatusColor(container.Status);

                var part1 = string.Format("{0,-3} {1,-41}", marker, name);
                var part2 = string.Format(" {0,-12} {1,-59} {2,-23} {3,-28} {4,-10}",
                    shortId, image, ports, status, health);

                if (i == selectedIndex)
                {
                    Screen.Write((part1 + part2).PadRight(184) + "\n", ConsoleColor.Black, ConsoleColor.White);
                }
                else
                {
                    Screen.Write(part1.PadRight(part1.Length), ConsoleColor.Yellow);
                    Screen.Write(part2.PadRight(184 - part1.Length) + "\n", statusColor);
                }
                continue;
            }
            else
            {
                // Container row - indent service name by 2 spaces
                var container = row.Container!;
                
                var name = "  " + container.Service;
                if (name.Length > 41) name = name[..41];

                var image = container.Image;
                if (image.Length > 59) image = image[..59];

                var status = container.Status;
                if (status.Length > 28) status = status[..28];

                var ports = container.Ports;
                if (ports.Length > 24) ports = ports[..24];

                line = string.Format("{0,-3} {1,-41} {2,-12} {3,-59} {4,-24} {5,-28} {6,-10}",
                    marker,
                    name,
                    container.Id.Length > 12 ? container.Id[..12] : container.Id,
                    image,
                    ports,
                    status,
                    container.Health == "none" ? "-" : container.Health);
                
                contentColor = Screen.GetStatusColor(container.Status);
            }

            // Highlight selected row with inverse video
            if (i == selectedIndex)
            {
                Screen.WriteLine(line.PadRight(184), ConsoleColor.Black, ConsoleColor.White);
            }
            else
            {
                Screen.WriteLine(line.PadRight(184), contentColor);
            }
        }

        // Stats pinned to bottom footer; docker version shown in title bar — no mid-screen version row
        var afterRows   = _topRow + displayRows.Count;
        var clearBottom = liveLogMode ? Screen.Height - LogPanelHeight - 1 : Screen.Height - 1;
        for (int i = afterRows; i < clearBottom; i++) Screen.ClearLine(i);

        _statsRow         = liveLogMode
            ? (Screen.Height - LogPanelHeight - 1 >= 0 ? Screen.Height - LogPanelHeight - 1 : -1)
            : Screen.Height - 1;
        _dockerVersionRow = -1; // version shown in title bar
        _runningCount     = displayRows.Count(r => !r.IsProjectRow && r.Container != null && r.Container.IsRunning);
        _totalCount       = displayRows.Count(r => !r.IsProjectRow && r.Container != null);

        if (_dockerVersionRow >= 0) RenderDockerVersion(dockerInstalled, dockerCandidate, dockerVersionReady);
        if (_statsRow >= 0)         RenderStats(stats, statsReady);
        // Render live log panel as part of the full render
        if (liveLogMode && liveLogLines != null)
        {
            List<string> snapshot;
            lock (liveLogLines) { snapshot = liveLogLines.TakeLast(LogPanelLines).ToList(); }
            RenderLiveLogPanel(snapshot, liveLogLabel ?? "");
        }
    }

    public void RenderDockerVersion(string installed, string candidate, bool versionReady = false)
    {
        if (_dockerVersionRow < 0) return;
        Console.SetCursorPosition(0, _dockerVersionRow);
        if (!versionReady)
        {
            Screen.Write("  Docker version: gathering...".PadRight(184), ConsoleColor.DarkCyan);
            return;
        }
        if (string.IsNullOrEmpty(installed))
        {
            Screen.Write("  Docker version: not found (docker-ce not installed via apt)".PadRight(184), ConsoleColor.DarkGray);
            return;
        }
        bool outdated = IsVersionNewer(candidate, installed);
        var prefix = "  Docker version: ";
        var suffix = $"  /  Latest: {candidate}";
        Screen.Write(prefix, ConsoleColor.DarkCyan);
        Screen.Write(installed, outdated ? ConsoleColor.Red : ConsoleColor.DarkCyan);
        Screen.Write(suffix.PadRight(184 - prefix.Length - installed.Length), ConsoleColor.DarkCyan);
    }

    private static bool IsVersionNewer(string candidate, string installed)
    {
        try { return Version.Parse(candidate) > Version.Parse(installed); }
        catch { return false; }
    }

    public void RenderStats((double cpu, double memMb, double limitMb, int cores) stats, bool statsReady = false)
    {
        if (_statsRow < 0) return;
        string statsText;
        if (!statsReady)
        {
            statsText = $"  {_runningCount}/{_totalCount} running  │  CPU: gathering...  │  Memory: gathering...  │  auto-refreshes every 5s";
        }
        else if (stats.memMb > 0 || stats.cpu > 0)
        {
            var memStr   = stats.memMb   >= 1024 ? $"{stats.memMb   / 1024:F1} GiB" : $"{stats.memMb:F0} MiB";
            var limitStr = stats.limitMb >= 1024 ? $"{stats.limitMb / 1024:F1} GiB" : $"{stats.limitMb:F0} MiB";
            var coresStr = stats.cores > 0 ? $" ({stats.cores} cores)" : "";
            statsText = $"  {_runningCount}/{_totalCount} running  │  CPU: {stats.cpu:F1}%{coresStr}  │  Memory: {memStr} / {limitStr}  │  auto-refreshes every 5s";
        }
        else
        {
            statsText = $"  {_runningCount}/{_totalCount} running  │  CPU: --  │  Memory: --  │  auto-refreshes every 5s";
        }
        Console.SetCursorPosition(0, _statsRow);
        Screen.Write(statsText.PadRight(Console.WindowWidth), ConsoleColor.DarkGray);
    }

    public void RenderLiveLogPanel(List<string> snapshot, string label)
    {
        var width = Screen.Width;
        int logStart = Screen.Height - LogPanelHeight;
        if (logStart < 0) return;

        // Separator
        Screen.SetCursorPosition(0, logStart);
        Screen.Write(new string('─', width), ConsoleColor.DarkGray);

        // Label
        Screen.SetCursorPosition(0, logStart + 1);
        Screen.Write($" Live logs: {label}  (L to close)".PadRight(width), ConsoleColor.Yellow);

        // Log lines
        for (int i = 0; i < LogPanelLines; i++)
        {
            int row = logStart + 2 + i;
            if (row >= Screen.Height) break;
            Screen.SetCursorPosition(0, row);
            if (i < snapshot.Count)
            {
                var line = Screen.StripAnsi(snapshot[i]);
                if (line.Length > width) line = line[..width];
                Screen.Write(line.PadRight(width), ConsoleColor.DarkYellow);
            }
            else
            {
                Screen.Write(new string(' ', width));
            }
        }
    }

    public int GetContentHeight(int rowCount)
    {
        return HeaderLines + rowCount;
    }
}
