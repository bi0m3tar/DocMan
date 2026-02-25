using DocMan.Models;
using DocMan.Services;

namespace DocMan.UI;

public class LogViewer
{
    private readonly DockerService _dockerService;

    public LogViewer(DockerService dockerService)
    {
        _dockerService = dockerService;
    }

    public async Task ShowAsync(ContainerInfo container)
    {
        var running = true;
        var logLines = new List<string>();
        var maxBuffer = 100;

        var process = _dockerService.StartLogsProcess(container.Id);
        var streamTask = Task.Run(async () =>
        {
            try
            {
                var readStdout = Task.Run(async () =>
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        var line = await process.StandardOutput.ReadLineAsync();
                        if (line != null)
                            lock (logLines) { logLines.Add(line); if (logLines.Count > maxBuffer) logLines.RemoveAt(0); }
                    }
                });
                var readStderr = Task.Run(async () =>
                {
                    while (!process.StandardError.EndOfStream)
                    {
                        var line = await process.StandardError.ReadLineAsync();
                        if (line != null)
                            lock (logLines) { logLines.Add(line); if (logLines.Count > maxBuffer) logLines.RemoveAt(0); }
                    }
                });
                await Task.WhenAll(readStdout, readStderr);
            }
            catch (Exception ex)
            {
                lock (logLines) { logLines.Add($"[stream error: {ex.Message}]"); }
            }
        });

        // After 1.5s with no output, add a diagnostic hint
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            lock (logLines)
            {
                if (logLines.Count == 0)
                    logLines.Add("[No log output — container may not write to stdout/stderr]");
            }
        });

        var displayTask = Task.Run(async () =>
        {
            while (running)
            {
                RenderLogs(container, logLines);
                await Task.Delay(200);
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

        try { if (!process.HasExited) process.Kill(); } catch { }
        await Task.WhenAny(streamTask, Task.Delay(1000));
        process.Dispose();
        ClearLogArea();
    }

    private static readonly System.Text.RegularExpressions.Regex AnsiRegex =
        new(@"\x1B\[[0-9;]*[a-zA-Z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private void RenderLogs(ContainerInfo container, List<string> logLines)
    {
        var width = Console.WindowWidth;
        var height = Console.WindowHeight;
        var maxLogLines = height - 4; // row 0=header, row 1=inspect line, row 2=separator, rows 3+=logs

        // Row 1: inspect header (white)
        Console.SetCursorPosition(0, 1);
        Console.ForegroundColor = ConsoleColor.White;
        var sep = $"--- INSPECT: {container.Project} / {container.Service} --- ESC/Enter to close ---";
        Console.Write(sep.PadRight(width));

        // Row 2: separator line (white)
        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(new string('-', width));
        Console.ResetColor();

        // Log lines starting at row 3
        List<string> snapshot;
        lock (logLines)
        {
            snapshot = logLines.TakeLast(maxLogLines).ToList();
        }

        for (int i = 0; i < maxLogLines; i++)
        {
            Console.SetCursorPosition(0, i + 3);
            if (i < snapshot.Count)
            {
                var line = AnsiRegex.Replace(snapshot[i], "");
                line = line.Replace("\t", "    ");
                line = new string(line.Where(c => !char.IsControl(c)).ToArray());
                if (line.Length > width) line = line[..width];
                Console.ResetColor();
                Console.Write(line.PadRight(width));
            }
            else
            {
                Console.Write(new string(' ', width));
            }
        }

        Console.ResetColor();
    }

    private void ClearLogArea()
    {
        Console.ResetColor();
        Console.Clear();
    }
}
