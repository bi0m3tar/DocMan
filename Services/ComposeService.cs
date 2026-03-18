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

    public async Task RunComposeFileAsync(string filePath)
    {
        var dockerPath  = Platform.NormalizePathForDockerCommand(filePath);
        var projectName = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? Path.GetFileNameWithoutExtension(filePath);

        // Use near-full width so long image names / pull progress aren't truncated
        var overlayWidth = Math.Min(Screen.Width, 160);
        var overlay = new Overlay(5, overlayWidth, Screen.Height - 8);
        var statusLines = new List<string>
        {
            "",
            $"Project : {projectName}",
            $"File    : {Path.GetFileName(filePath)}",
            $"Dir     : {Path.GetDirectoryName(filePath)}",
            ""
        };
        overlay.Show("Run Compose File", statusLines);

        // Verify the file exists before attempting
        if (!File.Exists(filePath))
        {
            statusLines.Add($"✗ File not found: {filePath}");
            statusLines.Add(""); statusLines.Add("Press any key to close...");
            overlay.Update(statusLines); Console.ReadKey(true); overlay.Hide(); return;
        }

        statusLines.Add($"Command: docker compose -f \"{dockerPath}\" up -d");
        statusLines.Add("");
        statusLines.Add("Note: large images (e.g. databases) may take several minutes to pull.");
        statusLines.Add("ESC/Enter to dismiss — compose continues running in background.");
        statusLines.Add("");
        overlay.Update(statusLines);

        try
        {
            // Use ArgumentList to bypass shell-escaping issues entirely
            ProcessStartInfo psi;
            if (Platform.IsWindows)
            {
                psi = new ProcessStartInfo("wsl") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("docker"); psi.ArgumentList.Add("compose");
                psi.ArgumentList.Add("-f");     psi.ArgumentList.Add(dockerPath);
                psi.ArgumentList.Add("up");     psi.ArgumentList.Add("-d");
            }
            else
            {
                psi = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("compose");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(filePath);
                psi.ArgumentList.Add("up"); psi.ArgumentList.Add("-d");
            }

            var process   = new Process { StartInfo = psi };
            process.Start();

            var appendLock  = new object();
            var overlayOpen = true;

            // docker compose writes progress to stderr, not stdout — read both streams
            // Use overlay content width so lines aren't truncated shorter than necessary
            var maxLine = overlayWidth - 4;
            void AppendLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                lock (appendLock)
                {
                    statusLines.Add(line.Length > maxLine ? line[..maxLine] : line);
                    if (overlayOpen) overlay.Update(statusLines);
                }
            }

            var stdoutTask  = Task.Run(async () => { while (!process.StandardOutput.EndOfStream) AppendLine(await process.StandardOutput.ReadLineAsync()); });
            var stderrTask  = Task.Run(async () => { while (!process.StandardError.EndOfStream)  AppendLine(await process.StandardError.ReadLineAsync()); });
            var processDone = process.WaitForExitAsync();

            while (!processDone.IsCompleted)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                    {
                        overlayOpen = false;
                        overlay.Hide();
                        _ = Task.WhenAll(stdoutTask, stderrTask, processDone).ContinueWith(_ => process.Dispose());
                        return;
                    }
                }
                await Task.Delay(50);
            }

            await Task.WhenAll(stdoutTask, stderrTask);

            lock (appendLock)
            {
                statusLines.Add("");
                statusLines.Add(process.ExitCode == 0
                    ? "✓ Compose project started successfully"
                    : $"✗ Failed with exit code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            statusLines.Add(""); statusLines.Add($"✗ Error: {ex.Message}");
        }

        statusLines.Add(""); statusLines.Add("Press any key to close...");
        overlay.Update(statusLines);
        Console.ReadKey(true);
        overlay.Hide();
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
                StartInfo = Platform.ShellCommand($"docker compose -f \"{composeFile}\" up -d{(forceRecreate ? " --force-recreate" : "")}")
            };

            process.Start();

            var overlayOpen = true;

            // docker compose writes progress to stderr, not stdout — read both streams
            void AppendLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                statusLines.Add(line.Length > 85 ? line[..85] : line);
                if (overlayOpen) overlay.Update(statusLines);
            }

            var stdoutTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                    AppendLine(await process.StandardOutput.ReadLineAsync());
            });

            var stderrTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                    AppendLine(await process.StandardError.ReadLineAsync());
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
                        _ = Task.WhenAll(stdoutTask, stderrTask, processDone)
                                .ContinueWith(_ => process.Dispose());
                        return;
                    }
                }
                await Task.Delay(50);
            }

            await Task.WhenAll(stdoutTask, stderrTask);

            statusLines.Add("");
            statusLines.Add(process.ExitCode == 0
                ? $"✓ Project {action.ToLower()}d successfully"
                : $"✗ Failed with exit code {process.ExitCode}");
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
                StartInfo = Platform.ShellCommand($"docker compose -f \"{composeFile}\" {command} {arguments}")
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
            Platform.IsWindows ? "Restarting Docker Desktop..." : "Restarting Docker service...",
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

        statusLines = new List<string> { "", "Restarting Docker...", "" };
        overlay.Update(statusLines);

        try
        {
            if (Platform.IsWindows)
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
            else
            {
                using var restartProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = "systemctl restart docker",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                var err = await restartProcess!.StandardError.ReadToEndAsync();
                await restartProcess.WaitForExitAsync();
                if (restartProcess.ExitCode == 0)
                    statusLines.Add("✓ Docker service restarted");
                else
                    statusLines.Add($"✗ Failed: {(string.IsNullOrWhiteSpace(err) ? $"exit {restartProcess.ExitCode}" : err.Split('\n')[0])}");
            }
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
        if (!Platform.IsWindows)
        {
            await RestartDockerAsync();
            return;
        }

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
