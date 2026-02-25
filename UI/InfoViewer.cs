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
        var running = true;
        ContainerDetail? detail = null;
        ContainerStats? stats = null;
        string? loadError = null;

        // Fetch static detail once
        _ = Task.Run(async () =>
        {
            try   { detail = await _dockerService.GetContainerDetailAsync(container.Id); }
            catch (Exception ex) { loadError = ex.Message; }
        });

        // Fetch stats in a loop while viewer is open
        _ = Task.Run(async () =>
        {
            while (running)
            {
                try   { stats = await _dockerService.GetContainerStatsDetailAsync(container.Id); }
                catch { stats = null; }
                await Task.Delay(2000);
            }
        });

        var renderTask = Task.Run(async () =>
        {
            while (running)
            {
                Render(container, detail, stats, loadError);
                await Task.Delay(500);
            }
        });

        while (running)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                    running = false;
            }
            await Task.Delay(50);
        }

        await Task.WhenAny(renderTask, Task.Delay(600));
        Console.ResetColor();
        Console.Clear();
    }

    private static void Render(ContainerInfo container, ContainerDetail? detail, ContainerStats? stats, string? error)
    {
        var width = Console.WindowWidth;
        var height = Console.WindowHeight;

        // Row 0: title bar
        Console.SetCursorPosition(0, 0);
        Console.ForegroundColor = ConsoleColor.Green;
        var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "?");
        Console.Write(("DocMan - DOcker Container MANager  " + appVersion).PadRight(width));

        // Row 1: info header
        Console.SetCursorPosition(0, 1);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"--- INFO: {container.Project} / {container.Service} --- ESC/Enter to close ---".PadRight(width));

        // Row 2: separator
        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(new string('-', width));
        Console.ResetColor();

        var lines = new List<(string text, ConsoleColor color)>();

        if (error != null)
        {
            lines.Add(("", ConsoleColor.Gray));
            lines.Add(($"  Error loading container info: {error}", ConsoleColor.Red));
        }
        else if (detail == null)
        {
            lines.Add(("", ConsoleColor.Gray));
            lines.Add(("  Loading...", ConsoleColor.DarkGray));
        }
        else
        {
            lines.Add(("", ConsoleColor.Gray));
            lines.Add(($"  {"Name",-12}{detail.Name}", ConsoleColor.White));
            lines.Add(($"  {"Image",-12}{detail.Image}", ConsoleColor.White));
            lines.Add(($"  {"Created",-12}{detail.Created}", ConsoleColor.Gray));
            lines.Add(($"  {"Status",-12}{detail.Status}", Screen.GetStatusColor(detail.Status)));

            if (detail.CpuLimit != null || detail.MemoryLimit != null)
            {
                var limits = string.Join("   ", new[]
                {
                    detail.CpuLimit  != null ? $"CPU limit: {detail.CpuLimit}"    : null,
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
                lines.Add(($"    Ports: {string.Join("  ", detail.Ports)}", ConsoleColor.Gray));
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
        }

        // Render lines starting at row 3
        for (int i = 0; i < height - 3; i++)
        {
            Console.SetCursorPosition(0, i + 3);
            if (i < lines.Count)
            {
                var (text, color) = lines[i];
                Console.ForegroundColor = color;
                var display = text.Length > width ? text[..width] : text;
                Console.Write(display.PadRight(width));
            }
            else
            {
                Console.Write(new string(' ', width));
            }
        }

        Console.ResetColor();
    }
}
