using System.Diagnostics;
using DocMan.Models;
using DocMan.UI;

namespace DocMan.Services;

public class ComposeService
{
    public async Task<string?> FindComposeFileAsync()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var files = Directory.GetFiles(currentDir, "docker-compose.yml", SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public async Task StartProjectAsync(string projectName, string? composeFile, string? workingDir, bool forceRecreate = false)
    {
        var overlay = new Overlay(6, 90, 21);
        var action = forceRecreate ? "Recreating" : "Starting";
        
        var statusLines = new List<string>
        {
            "",
            $"{action} project '{projectName}' using docker-compose...",
            ""
        };

        overlay.Show($"{action} Project", statusLines);

        try
        {
            if (string.IsNullOrEmpty(composeFile) || string.IsNullOrEmpty(workingDir))
            {
                statusLines.Add("✗ No compose file found for this project");
                statusLines.Add("");
                statusLines.Add("Falling back to individual container start...");
                overlay.Update(statusLines);
                await Task.Delay(2000);
                overlay.Hide();
                return;
            }

            statusLines.Add($"Compose file: {Path.GetFileName(composeFile)}");
            statusLines.Add("");
            statusLines.Add("ESC/Enter to close window (process continues in background)");
            statusLines.Add("");
            overlay.Update(statusLines);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = $"docker compose -f \"{composeFile}\" up -d{(forceRecreate ? " --force-recreate" : "")}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var stderrTask = process.StandardError.ReadToEndAsync();
            var overlayOpen = true;

            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        statusLines.Add(line.Length > 85 ? line[..85] : line);
                        if (overlayOpen) overlay.Update(statusLines);
                    }
                }
            });

            var processDone = process.WaitForExitAsync();

            // Wait for process to finish OR user to press ESC/Enter
            while (!processDone.IsCompleted)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                    {
                        overlayOpen = false;
                        overlay.Hide();
                        // Let process + tasks finish in background; dispose when done
                        _ = Task.WhenAll(outputTask, stderrTask, processDone)
                                .ContinueWith(_ => process.Dispose());
                        return;
                    }
                }
                await Task.Delay(50);
            }

            await Task.WhenAll(outputTask, stderrTask);

            if (process.ExitCode == 0)
            {
                statusLines.Add("");
                statusLines.Add($"✓ Project {action.ToLower()}d successfully");
            }
            else
            {
                var stderr = await stderrTask;
                statusLines.Add("");
                statusLines.Add($"✗ Failed with exit code {process.ExitCode}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    statusLines.Add(stderr.Split('\n')[0].Trim().Length > 85
                        ? stderr.Split('\n')[0].Trim()[..85]
                        : stderr.Split('\n')[0].Trim());
            }
        }
        catch (Exception ex)
        {
            statusLines.Add("");
            statusLines.Add($"✗ Error: {ex.Message}");
        }

        statusLines.Add("");
        statusLines.Add("Press any key to close...");
        overlay.Update(statusLines);

        Console.ReadKey(true);
        overlay.Hide();
    }

    public async Task ExecuteComposeCommandAsync(string composeFile, string command, string arguments = "")
    {
        var overlay = new Overlay(8, 60, 8);
        
        var statusLines = new List<string>
        {
            "",
            $"Executing: docker compose {command} {arguments}",
            "",
            "Please wait..."
        };

        overlay.Show($"Compose {command}", statusLines);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = $"docker compose -f \"{composeFile}\" {command} {arguments}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            // Drain both streams to prevent buffer deadlock
            var drainOut = process.StandardOutput.ReadToEndAsync();
            var drainErr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(drainOut, drainErr);

            if (process.ExitCode == 0)
            {
                statusLines.Add("");
                statusLines.Add("✓ Command completed successfully");
            }
            else
            {
                statusLines.Add("");
                statusLines.Add($"✗ Command failed with exit code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            statusLines.Add("");
            statusLines.Add($"✗ Error: {ex.Message}");
        }

        statusLines.Add("");
        statusLines.Add("Press any key to continue...");
        overlay.Update(statusLines);

        Console.ReadKey(true);
        overlay.Hide();
    }

    public async Task RestartDockerAsync()
    {
        var overlay = new Overlay(8, 60, 10);
        
        var statusLines = new List<string>
        {
            "",
            "Restarting Docker Desktop...",
            "",
            "This may take a minute.",
            "",
            "Press Y to confirm or any other key to cancel"
        };

        overlay.Show("Restart Docker", statusLines);

        var key = Console.ReadKey(true);
        if (key.Key != ConsoleKey.Y)
        {
            overlay.Hide();
            return;
        }

        statusLines = new List<string>
        {
            "",
            "Stopping Docker service...",
            ""
        };
        overlay.Update(statusLines);

        try
        {
            using var stopProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-Command \"Stop-Service com.docker.service -Force\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            });
            await stopProcess!.WaitForExitAsync();

            statusLines.Add("✓ Service stopped");
            statusLines.Add("");
            statusLines.Add("Starting Docker service...");
            overlay.Update(statusLines);

            using var startProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-Command \"Start-Service com.docker.service\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            });
            await startProcess!.WaitForExitAsync();

            statusLines.Add("✓ Service started");
        }
        catch (Exception ex)
        {
            statusLines.Add($"✗ Error: {ex.Message}");
        }

        statusLines.Add("");
        statusLines.Add("Press any key to continue...");
        overlay.Update(statusLines);

        Console.ReadKey(true);
        overlay.Hide();
    }

    public async Task RestartWSLAsync()
    {
        var overlay = new Overlay(8, 60, 10);
        
        var statusLines = new List<string>
        {
            "",
            "Restarting WSL...",
            "",
            "This may take a minute.",
            "",
            "Press Y to confirm or any other key to cancel"
        };

        overlay.Show("Restart WSL", statusLines);

        var key = Console.ReadKey(true);
        if (key.Key != ConsoleKey.Y)
        {
            overlay.Hide();
            return;
        }

        statusLines = new List<string>
        {
            "",
            "Shutting down WSL...",
            ""
        };
        overlay.Update(statusLines);

        try
        {
            using var shutdownProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = "--shutdown",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            await shutdownProcess!.WaitForExitAsync();

            statusLines.Add("✓ WSL shut down");
            statusLines.Add("");
            statusLines.Add("WSL will restart automatically on next use");
        }
        catch (Exception ex)
        {
            statusLines.Add($"✗ Error: {ex.Message}");
        }

        statusLines.Add("");
        statusLines.Add("Press any key to continue...");
        overlay.Update(statusLines);

        Console.ReadKey(true);
        overlay.Hide();
    }
}
