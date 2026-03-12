using DocMan.Models;
using DocMan.Services;

namespace DocMan.UI;

public class ProjectInfoViewer
{
    private readonly DockerService _dockerService;

    public ProjectInfoViewer(DockerService dockerService)
    {
        _dockerService = dockerService;
    }

    public async Task ShowAsync(string projectName, List<ContainerInfo> containers)
    {
        var composeFile = containers.FirstOrDefault()?.ComposeFile;
        var workingDir  = containers.FirstOrDefault()?.WorkingDir;

        string? composeContent = null;
        string? loadError = null;

        if (!string.IsNullOrEmpty(composeFile))
        {
            _ = Task.Run(async () =>
            {
                try   { composeContent = await _dockerService.ReadComposeFileAsync(composeFile); }
                catch (Exception ex) { loadError = ex.Message; }
            });
        }
        else
        {
            composeContent = "";
        }

        var scrollOffset = 0;
        var totalLines = 0;
        var needsRender = true;
        string? lastRenderedContent = null;
        string? lastRenderedError = null;

        while (true)
        {
            // Detect when background load completes → trigger re-render
            if (!ReferenceEquals(composeContent, lastRenderedContent) || loadError != lastRenderedError)
            {
                lastRenderedContent = composeContent;
                lastRenderedError   = loadError;
                needsRender = true;
            }

            if (needsRender)
            {
                var lines = BuildLines(projectName, containers, composeFile, workingDir, composeContent, loadError);
                totalLines = lines.Count;
                var contentHeight = Console.WindowHeight - 3;
                scrollOffset = Math.Max(0, Math.Min(scrollOffset, Math.Max(0, totalLines - contentHeight)));
                Render(projectName, lines, scrollOffset);
                needsRender = false;
            }

            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                var contentHeight = Console.WindowHeight - 3;

                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                    case ConsoleKey.Enter:
                        Console.ResetColor();
                        Console.Clear();
                        return;

                    case ConsoleKey.UpArrow:
                        if (scrollOffset > 0) { scrollOffset--; needsRender = true; }
                        break;
                    case ConsoleKey.DownArrow:
                        if (scrollOffset < totalLines - contentHeight) { scrollOffset++; needsRender = true; }
                        break;
                    case ConsoleKey.PageUp:
                        scrollOffset = Math.Max(0, scrollOffset - contentHeight);
                        needsRender = true;
                        break;
                    case ConsoleKey.PageDown:
                        scrollOffset = Math.Max(0, Math.Min(scrollOffset + contentHeight, totalLines - contentHeight));
                        needsRender = true;
                        break;
                    case ConsoleKey.Home:
                        scrollOffset = 0;
                        needsRender = true;
                        break;
                    case ConsoleKey.End:
                        scrollOffset = Math.Max(0, totalLines - contentHeight);
                        needsRender = true;
                        break;
                }
            }

            await Task.Delay(50);
        }
    }

    private static List<(string text, ConsoleColor color)> BuildLines(
        string projectName, List<ContainerInfo> containers,
        string? composeFile, string? workingDir, string? composeContent, string? error)
    {
        var width = Console.WindowWidth;
        var lines = new List<(string text, ConsoleColor color)>();

        // Project header
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(($"  {"Project",-14}{projectName}", ConsoleColor.White));
        if (!string.IsNullOrEmpty(workingDir))
            lines.Add(($"  {"Working dir",-14}{workingDir}", ConsoleColor.Gray));
        if (!string.IsNullOrEmpty(composeFile))
            lines.Add(($"  {"Compose file",-14}{composeFile}", ConsoleColor.Gray));

        // Services summary
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(("  SERVICES", ConsoleColor.Cyan));
        foreach (var c in containers)
        {
            var statusColor = Screen.GetStatusColor(c.Status);
            var ports = string.IsNullOrEmpty(c.Ports) ? "" : $"  [{c.Ports}]";
            var shortId = c.Id.Length > 12 ? c.Id[..12] : c.Id;
            lines.Add(($"    {c.Service,-22}{c.Status,-28}{shortId}  {c.Image}{ports}", statusColor));
        }

        // Compose file content
        lines.Add(("", ConsoleColor.Gray));
        if (string.IsNullOrEmpty(composeFile))
        {
            lines.Add(("  COMPOSE FILE", ConsoleColor.Cyan));
            lines.Add(("    No compose file associated with this project.", ConsoleColor.DarkGray));
        }
        else if (error != null)
        {
            lines.Add(("  COMPOSE FILE", ConsoleColor.Cyan));
            lines.Add(($"    Error: {error}", ConsoleColor.Red));
        }
        else if (composeContent == null)
        {
            lines.Add(("  COMPOSE FILE", ConsoleColor.Cyan));
            lines.Add(("    Loading...", ConsoleColor.DarkGray));
        }
        else if (composeContent.Length == 0)
        {
            lines.Add(("  COMPOSE FILE", ConsoleColor.Cyan));
            lines.Add(("    (empty)", ConsoleColor.DarkGray));
        }
        else
        {
            lines.Add(($"  COMPOSE FILE  {composeFile}", ConsoleColor.Cyan));
            lines.Add(("", ConsoleColor.Gray));
            foreach (var rawLine in composeContent.Split('\n'))
            {
                var trimmed = rawLine.TrimEnd('\r');
                // Expand to full line (no truncation — scrolling handles it)
                var color = trimmed.TrimStart().StartsWith('#') ? ConsoleColor.DarkGreen
                          : trimmed.IndexOf(':') > 0 && !trimmed.TrimStart().StartsWith('-') ? ConsoleColor.DarkCyan
                          : ConsoleColor.Gray;
                lines.Add(($"    {trimmed}", color));
            }
        }

        return lines;
    }

    private static void Render(string projectName, List<(string text, ConsoleColor color)> lines, int scrollOffset)
    {
        var width  = Console.WindowWidth;
        var height = Console.WindowHeight;
        var contentHeight = height - 3;
        var totalLines = lines.Count;

        // Row 0: title bar
        Console.SetCursorPosition(0, 0);
        Console.ForegroundColor = ConsoleColor.Green;
        var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "?");
        Console.Write(("DocMan - DOcker Container MANager  " + appVersion).PadRight(width));

        // Row 1: header with scroll position
        Console.SetCursorPosition(0, 1);
        Console.ForegroundColor = ConsoleColor.White;
        var scrollInfo = totalLines > contentHeight
            ? $"  ↑↓/PgUp/PgDn/Home/End to scroll  [{scrollOffset + 1}-{Math.Min(scrollOffset + contentHeight, totalLines)}/{totalLines}]"
            : "";
        Console.Write($"--- PROJECT INFO: {projectName} --- ESC/Enter to close ---{scrollInfo}".PadRight(width));

        // Row 2: separator
        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(new string('-', width));
        Console.ResetColor();

        // Content rows
        for (int i = 0; i < contentHeight; i++)
        {
            Console.SetCursorPosition(0, i + 3);
            var lineIdx = scrollOffset + i;
            if (lineIdx < lines.Count)
            {
                var (text, color) = lines[lineIdx];
                Console.ForegroundColor = color;
                var visibleLen = Screen.StripAnsi(text).Length;
                // Truncate to screen width
                var display = visibleLen > width ? text[..width] : text;
                Console.Write(display);
                Console.Write(new string(' ', Math.Max(0, width - visibleLen)));
            }
            else
            {
                Console.Write(new string(' ', width));
            }
        }

        Console.ResetColor();
    }
}
