using DocMan.Models;
using DocMan.Services;

namespace DocMan.UI;

public class InfoViewer
{
    private readonly DockerService _dockerService;

    public InfoViewer(DockerService dockerService)
    {
        _dockerService = dockerService;
    }

    public async Task ShowAsync(ContainerInfo container)
    {
        ContainerDetail? detail = null;
        ContainerStats? stats = null;
        string? loadError = null;
        var alive = true;

        // Fetch static detail once
        _ = Task.Run(async () =>
        {
            try   { detail = await _dockerService.GetContainerDetailAsync(container.Id); }
            catch (Exception ex) { loadError = ex.Message; }
        });

        // Fetch stats in a loop while viewer is open
        _ = Task.Run(async () =>
        {
            while (alive)
            {
                try   { stats = await _dockerService.GetContainerStatsDetailAsync(container.Id); }
                catch { stats = null; }
                await Task.Delay(2000);
            }
        });

        var scrollOffset = 0;
        var totalLines   = 0;
        var needsRender  = true;

        ContainerDetail? lastDetail = null;
        ContainerStats?  lastStats  = null;
        string?          lastError  = null;

        while (true)
        {
            // Trigger re-render whenever background data changes
            if (!ReferenceEquals(detail, lastDetail) || !ReferenceEquals(stats, lastStats) || loadError != lastError)
            {
                lastDetail = detail;
                lastStats  = stats;
                lastError  = loadError;
                needsRender = true;
            }

            if (needsRender)
            {
                var lines = BuildLines(container, detail, stats, loadError);
                totalLines = lines.Count;
                var contentHeight = Console.WindowHeight - 3;
                scrollOffset = Math.Max(0, Math.Min(scrollOffset, Math.Max(0, totalLines - contentHeight)));
                Render(container, lines, scrollOffset);
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
                        alive = false;
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
        ContainerInfo container, ContainerDetail? detail, ContainerStats? stats, string? error)
    {
        var lines = new List<(string text, ConsoleColor color)>();

        if (error != null)
        {
            lines.Add(("", ConsoleColor.Gray));
            lines.Add(($"  Error loading container info: {error}", ConsoleColor.Red));
            return lines;
        }

        if (detail == null)
        {
            lines.Add(("", ConsoleColor.Gray));
            lines.Add(("  Loading...", ConsoleColor.DarkGray));
            return lines;
        }

        var width = Console.WindowWidth;

        lines.Add(("", ConsoleColor.Gray));
        lines.Add(($"  {"Name",-12}{detail.Name}", ConsoleColor.White));
        lines.Add(($"  {"Image",-12}{detail.Image}", ConsoleColor.White));
        lines.Add(($"  {"Created",-12}{detail.Created}", ConsoleColor.Gray));
        lines.Add(($"  {"Status",-12}{detail.Status}", Screen.GetStatusColor(detail.Status)));
        if (detail.StartedAt != null)
            lines.Add(($"  {"Started",-12}{detail.StartedAt}", ConsoleColor.Gray));
        if (detail.FinishedAt != null && detail.Status.StartsWith("Exited"))
            lines.Add(($"  {"Stopped",-12}{detail.FinishedAt}", ConsoleColor.Gray));
        if (detail.ExitCode != null && detail.Status.StartsWith("Exited"))
        {
            var exitColor = detail.ExitCode == "0" ? ConsoleColor.Green : ConsoleColor.Red;
            var exitText  = $"  {"Exit code",-12}{detail.ExitCode}" + (detail.OomKilled ? "  ⚠ OOM killed" : "");
            lines.Add((exitText, exitColor));
        }
        if (detail.RestartPolicy != null)
            lines.Add(($"  {"Restart",-12}{detail.RestartPolicy}", ConsoleColor.DarkCyan));
        if (detail.Command != null)
            lines.Add(($"  {"Command",-12}{detail.Command}", ConsoleColor.DarkGray));

        if (detail.CpuLimit != null || detail.MemoryLimit != null)
        {
            var limits = string.Join("   ", new[]
            {
                detail.CpuLimit    != null ? $"CPU limit: {detail.CpuLimit}"       : null,
                detail.MemoryLimit != null ? $"Memory limit: {detail.MemoryLimit}" : null
            }.Where(x => x != null));
            lines.Add(($"  {"Limits",-12}{limits}", ConsoleColor.DarkCyan));
        }

        // Network
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(("  NETWORK", ConsoleColor.Cyan));
        if (detail.Networks.Count == 0)
            lines.Add(("    none", ConsoleColor.DarkGray));
        else
            foreach (var net in detail.Networks)
            {
                var ipGw = string.IsNullOrEmpty(net.IpAddress) ? "" :
                    $"  IP: {net.IpAddress}" + (string.IsNullOrEmpty(net.Gateway) ? "" : $"  GW: {net.Gateway}");
                lines.Add(($"    {net.Name}{ipGw}", ConsoleColor.Gray));
            }

        if (detail.Ports.Count > 0)
        {
            var portLinks = detail.Ports.Select(p =>
            {
                var hostPort = p.Split('→')[0].Trim().Split(' ')[0];
                return int.TryParse(hostPort, out _)
                    ? Screen.Hyperlink($"http://localhost:{hostPort}", p)
                    : p;
            });
            lines.Add(($"    Ports: {string.Join("  ", portLinks)}", ConsoleColor.Gray));
        }

        // Volumes
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(("  VOLUMES", ConsoleColor.Cyan));
        if (detail.Mounts.Count == 0)
            lines.Add(("    none", ConsoleColor.DarkGray));
        else
            foreach (var m in detail.Mounts)
            {
                var src = m.Source.Length > 45 ? "..." + m.Source[^42..] : m.Source;
                lines.Add(($"    {src}  →  {m.Destination}  ({m.Mode})", ConsoleColor.Gray));
            }

        // Resource usage
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(("  RESOURCE USAGE", ConsoleColor.Cyan));
        if (stats == null)
        {
            lines.Add(("    Loading stats...", ConsoleColor.DarkGray));
        }
        else
        {
            lines.Add(($"    {"CPU",-14}{stats.CpuPercent}", ConsoleColor.White));
            lines.Add(($"    {"Memory",-14}{stats.MemUsage} / {stats.MemLimit}  ({stats.MemPercent})", ConsoleColor.White));
            lines.Add(($"    {"Net I/O",-14}↑ {stats.NetOut}  ↓ {stats.NetIn}", ConsoleColor.White));
            lines.Add(($"    {"Disk I/O",-14}R {stats.BlockIn}  W {stats.BlockOut}", ConsoleColor.White));
            lines.Add(($"    {"PIDs",-14}{stats.Pids}", ConsoleColor.White));
        }

        // Environment variables
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(("  ENVIRONMENT", ConsoleColor.Cyan));
        if (detail.EnvVars.Count == 0)
            lines.Add(("    none", ConsoleColor.DarkGray));
        else
            foreach (var env in detail.EnvVars)
            {
                var display = env.Length > width - 6 ? env[..(width - 9)] + "..." : env;
                var eq = display.IndexOf('=');
                if (eq > 0)
                    lines.Add(($"    {display[..eq]}={display[(eq + 1)..]}", ConsoleColor.DarkGray));
                else
                    lines.Add(($"    {display}", ConsoleColor.DarkGray));
            }

        return lines;
    }

    private static void Render(ContainerInfo container, List<(string text, ConsoleColor color)> lines, int scrollOffset)
    {
        var width         = Console.WindowWidth;
        var height        = Console.WindowHeight;
        var contentHeight = height - 3;
        var totalLines    = lines.Count;

        // Row 0: title bar
        Console.SetCursorPosition(0, 0);
        Console.ForegroundColor = ConsoleColor.Green;
        var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "?");
        Console.Write(("DocMan - DOcker Container MANager  " + appVersion).PadRight(width));

        // Row 1: header with scroll indicator
        Console.SetCursorPosition(0, 1);
        Console.ForegroundColor = ConsoleColor.White;
        var scrollInfo = totalLines > contentHeight
            ? $"  ↑↓/PgUp/PgDn/Home/End  [{scrollOffset + 1}-{Math.Min(scrollOffset + contentHeight, totalLines)}/{totalLines}]"
            : "";
        Console.Write($"--- INFO: {container.Project} / {container.Service} --- ESC/Enter to close ---{scrollInfo}".PadRight(width));

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
                var display    = visibleLen > width ? text[..width] : text;
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
