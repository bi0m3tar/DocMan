using System.Diagnostics;
using System.Text.Json;
using DocMan.Models;
using DocMan.Utilities;

namespace DocMan.Services;

public class DockerService
{
    private int _cachedCores = 0;

    private static async Task<(string output, string stderr, int exitCode)> RunWslDetailedAsync(string arguments)
    {
        using var proc = Process.Start(Platform.ShellCommand(arguments))!;
        var outputTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (((await outputTask).Trim()), ((await stderrTask).Trim()), proc.ExitCode);
    }

    private static async Task<string> RunWslAsync(string arguments)
    {
        var (output, _, _) = await RunWslDetailedAsync(arguments);
        return output;
    }

    /// <summary>
    /// Checks prerequisites before the UI starts.
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    public static async Task<string?> CheckPrerequisitesAsync()
    {
        if (Platform.IsWindows)
        {
            // 1. Check if wsl.exe is available
            try
            {
                var wslVersionPsi = new ProcessStartInfo("wsl", "--version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var vProc = Process.Start(wslVersionPsi);
                if (vProc == null)
                    return "WSL is not installed or not accessible.\nInstall WSL: https://aka.ms/wsl";
                await vProc.WaitForExitAsync();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return "WSL is not installed.\nInstall it by running:  wsl --install\nSee: https://aka.ms/wsl";
            }

            // 2. Check if WSL can actually launch a shell
            var (echoOut, echoErr, echoExit) = await RunWslDetailedAsync("echo __wsl_ok__");
            if (echoExit != 0 || !echoOut.Contains("__wsl_ok__"))
            {
                var hint = string.IsNullOrWhiteSpace(echoErr) ? "" : $"\n  {echoErr.Split('\n')[0]}";
                return $"WSL is installed but could not start.{hint}\n\nPossible fixes:\n  wsl --install\n  wsl --set-default <distro-name>";
            }

            // 3. Check if docker is available inside WSL
            var (dockerOut, _, dockerExit) = await RunWslDetailedAsync("docker --version");
            if (dockerExit != 0 || !dockerOut.Contains("Docker"))
                return "Docker is not installed in WSL.\nInstall it inside your WSL distro:\n  curl -fsSL https://get.docker.com | sudo sh";
        }
        else
        {
            // Linux: just verify docker is installed and reachable
            try
            {
                var psi = new ProcessStartInfo("docker", "--version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                    return "Docker is not installed or not in PATH.\nInstall it: curl -fsSL https://get.docker.com | sudo sh";
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0 || !output.Contains("Docker"))
                    return "Docker is not installed or not in PATH.\nInstall it: curl -fsSL https://get.docker.com | sudo sh";
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return "Docker is not installed or not in PATH.\nInstall it: curl -fsSL https://get.docker.com | sudo sh";
            }
        }

        return null;
    }

    public async Task<List<ContainerInfo>> GetContainersAsync()
    {
        var (idsRaw, idsErr, idsExit) = await RunWslDetailedAsync("docker ps -aq --no-trunc");

        if (idsExit != 0 || (!string.IsNullOrWhiteSpace(idsErr) && string.IsNullOrWhiteSpace(idsRaw)))
        {
            if (Platform.IsWindows)
            {
                // Detect Docker Desktop binary used without Docker Desktop running
                var (whichOut, _, _) = await RunWslDetailedAsync("-- which docker");
                if (whichOut.Contains("/mnt/c/"))
                    throw new Exception("Docker Engine not installed in WSL. Run: curl -fsSL https://get.docker.com | sudo sh");
            }

            // Try to start the Docker daemon
            var startCmd = Platform.IsWindows ? "-u root -- service docker start" : "sudo -n service docker start";
            var (_, startErr, startExit) = await RunWslDetailedAsync(startCmd);
            if (startExit != 0 && !string.IsNullOrWhiteSpace(startErr))
                throw new Exception($"Could not start Docker daemon: {startErr.Split('\n')[0]}");

            await Task.Delay(3000);

            (idsRaw, idsErr, idsExit) = await RunWslDetailedAsync("docker ps -aq --no-trunc");
            if (idsExit != 0 || (!string.IsNullOrWhiteSpace(idsErr) && string.IsNullOrWhiteSpace(idsRaw)))
                throw new Exception("Docker daemon did not start in time. Retrying...");
        }

        if (string.IsNullOrWhiteSpace(idsRaw))
            return new List<ContainerInfo>();

        var ids = idsRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (inspectJson, inspectErr, inspectExit) = await RunWslDetailedAsync($"docker inspect {string.Join(' ', ids)}");

        if (inspectExit != 0 || !inspectJson.TrimStart().StartsWith('['))
        {
            var msg = string.IsNullOrWhiteSpace(inspectErr) ? "docker inspect failed" : inspectErr.Split('\n')[0];
            throw new Exception(msg);
        }

        using var doc = JsonDocument.Parse(inspectJson);
        var result = new List<ContainerInfo>();

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = el.GetProperty("Id").GetString() ?? "";
            var name = el.GetProperty("Name").GetString()?.TrimStart('/') ?? "unknown";

            var labels = new Dictionary<string, string>();
            if (el.TryGetProperty("Config", out var config) &&
                config.TryGetProperty("Labels", out var labelsEl) &&
                labelsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var label in labelsEl.EnumerateObject())
                    labels[label.Name] = label.Value.GetString() ?? "";
            }

            string project, service;
            bool isStandalone;
            if (labels.TryGetValue("com.docker.compose.project", out var composeProject) &&
                labels.TryGetValue("com.docker.compose.service", out var composeService))
            {
                project = composeProject;
                service = composeService;
                isStandalone = false;
            }
            else
            {
                project = name;
                service = name;
                isStandalone = true;
            }

            var state = el.GetProperty("State");
            var stateStatus = state.GetProperty("Status").GetString() ?? "unknown";
            var isRunning = stateStatus == "running";
            var status = FormatStatus(state);

            var health = "none";
            if (isRunning &&
                state.TryGetProperty("Health", out var healthEl) &&
                healthEl.ValueKind == JsonValueKind.Object &&
                healthEl.TryGetProperty("Status", out var healthStatus))
            {
                health = healthStatus.GetString() ?? "none";
            }

            var image = config.TryGetProperty("Image", out var imageEl)
                ? CleanImageName(imageEl.GetString() ?? "")
                : "";

            var ports = "";
            if (el.TryGetProperty("NetworkSettings", out var ns) &&
                ns.TryGetProperty("Ports", out var portsEl) &&
                portsEl.ValueKind == JsonValueKind.Object)
            {
                var portMappings = new List<string>();
                foreach (var portEntry in portsEl.EnumerateObject())
                {
                    if (portEntry.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var binding in portEntry.Value.EnumerateArray())
                        {
                            if (binding.TryGetProperty("HostPort", out var hp))
                            {
                                var containerPort = portEntry.Name.Split('/')[0];
                                portMappings.Add($"{hp.GetString()}→{containerPort}");
                            }
                        }
                    }
                }
                var distinctMappings = portMappings.Distinct().ToList();
                ports = string.Join(", ", distinctMappings.Take(2));
                if (distinctMappings.Count > 2) ports += "...";
            }

            labels.TryGetValue("com.docker.compose.project.config_files", out var composeFile);
            labels.TryGetValue("com.docker.compose.project.working_dir", out var workingDir);

            result.Add(new ContainerInfo
            {
                Id = id,
                Name = name,
                Project = project,
                Service = service,
                Status = status,
                Health = health,
                Image = image,
                Ports = ports,
                IsRunning = isRunning,
                IsStandalone = isStandalone,
                ComposeFile = string.IsNullOrEmpty(composeFile) ? null : composeFile,
                WorkingDir = string.IsNullOrEmpty(workingDir) ? null : workingDir
            });
        }

        return result.OrderBy(c => c.Project).ThenBy(c => c.Service).ToList();
    }

    private static string FormatStatus(JsonElement state)
    {
        var status = state.GetProperty("Status").GetString() ?? "unknown";
        try
        {
            return status switch
            {
                "running" => state.TryGetProperty("StartedAt", out var startedAt)
                    ? $"Up {FormatDuration(DateTime.UtcNow - DateTime.Parse(startedAt.GetString()!).ToUniversalTime())}"
                    : "Up",
                "exited" => state.TryGetProperty("FinishedAt", out var finishedAt)
                    ? $"Exited {FormatDuration(DateTime.UtcNow - DateTime.Parse(finishedAt.GetString()!).ToUniversalTime())} ago"
                    : "Exited",
                _ => status
            };
        }
        catch
        {
            return status;
        }
    }

    private static string FormatDuration(TimeSpan d)
    {
        if (d.TotalSeconds < 90) return $"{(int)d.TotalSeconds} seconds";
        if (d.TotalMinutes < 90) return $"{(int)d.TotalMinutes} minutes";
        if (d.TotalHours < 48) return $"{(int)d.TotalHours} hours";
        if (d.TotalDays < 60) return $"{(int)d.TotalDays} days";
        if (d.TotalDays < 548) return $"{(int)(d.TotalDays / 30)} months";
        return $"{(int)(d.TotalDays / 365)} years";
    }

    private static string CleanImageName(string image)
    {
        if (image.Contains(':')) image = image.Split(':')[0];
        if (image.Contains('@')) image = image.Split('@')[0];
        if (image.Contains("docker"))
            return image[image.IndexOf("docker")..];
        if (image.Contains('/'))
            return image.Split('/')[^1];
        return image;
    }

    public async Task StopContainerAsync(string id) =>
        await RunWslAsync($"docker stop --time 5 {id}");

    public async Task StartContainerAsync(string id) =>
        await RunWslAsync($"docker start {id}");

    public async Task RestartContainerAsync(string id) =>
        await RunWslAsync($"docker restart {id}");

    public async Task DeleteContainerAsync(string id) =>
        await RunWslAsync($"docker rm {id}");

    public async Task KillContainerAsync(string id) =>
        await RunWslAsync($"docker kill {id}");

    public async Task<string> ReadComposeFileAsync(string composeFile)
    {
        var (output, stderr, exitCode) = await RunWslDetailedAsync($"cat \"{composeFile}\"");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return stderr.Length > 0 ? $"Error reading file: {stderr}" : "(empty)";
        return output;
    }

    public async Task<List<string>> PruneImagesAsync()
    {
        var (output, stderr, _) = await RunWslDetailedAsync("docker image prune --all --force");
        var raw = string.IsNullOrWhiteSpace(output) ? stderr : output;

        var namedImages = new List<string>();
        var deletedLayers = 0;
        string? totalSpace = null;

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("untagged:", StringComparison.OrdinalIgnoreCase) && !line.Contains("@sha256:"))
                namedImages.Add(line["untagged:".Length..].Trim());
            else if (line.StartsWith("deleted:", StringComparison.OrdinalIgnoreCase))
                deletedLayers++;
            else if (line.StartsWith("Total reclaimed space:", StringComparison.OrdinalIgnoreCase))
                totalSpace = line;
        }

        var result = new List<string>();

        if (namedImages.Count == 0 && deletedLayers == 0)
        {
            result.Add("No images to prune.");
            return result;
        }

        if (namedImages.Count > 0)
        {
            result.Add($"Removed {namedImages.Count} image(s):");
            foreach (var img in namedImages)
                result.Add($"  - {img}");
        }

        if (deletedLayers > 0)
        {
            if (namedImages.Count > 0) result.Add("");
            result.Add(namedImages.Count == 0
                ? $"Removed {deletedLayers} unnamed/dangling image layer(s)."
                : $"({deletedLayers} layer(s) deleted)");
        }

        if (totalSpace != null)
        {
            result.Add("");
            result.Add(totalSpace);
        }

        return result;
    }

    public async Task<List<string>> PruneNetworksAsync()
    {
        var (output, stderr, _) = await RunWslDetailedAsync("docker network prune --force");
        var raw = string.IsNullOrWhiteSpace(output) ? stderr : output;
        var result = new List<string>();
        var deleted = new List<string>();
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("deleted:", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
                deleted.Add(line);
        }
        if (deleted.Count == 0) { result.Add("No unused networks to prune."); return result; }
        result.Add($"Removed {deleted.Count} unused network(s):");
        foreach (var n in deleted) result.Add($"  - {n}");
        return result;
    }

    public async Task<List<string>> PruneVolumesAsync()
    {
        var (output, stderr, _) = await RunWslDetailedAsync("docker volume prune --force");
        var raw = string.IsNullOrWhiteSpace(output) ? stderr : output;
        var result = new List<string>();
        var deleted = new List<string>();
        string? totalSpace = null;
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("Total reclaimed space:", StringComparison.OrdinalIgnoreCase)) totalSpace = line;
            else if (!line.StartsWith("deleted:", StringComparison.OrdinalIgnoreCase)) deleted.Add(line);
        }
        if (deleted.Count == 0) { result.Add("No dangling volumes to prune."); return result; }
        result.Add($"Removed {deleted.Count} dangling volume(s):");
        foreach (var v in deleted) result.Add($"  - {v}");
        if (totalSpace != null) { result.Add(""); result.Add(totalSpace); }
        return result;
    }


    public async Task<(double cpuPercent, double memoryMb, double memoryLimitMb, int cores)> GetTotalStatsAsync()
    {
        // Use JSON format - avoids bash interpreting special chars like |
        var output = await RunWslAsync("docker stats --no-stream --format '{{json .}}'");
        if (string.IsNullOrWhiteSpace(output)) return (0, 0, 0, 0);

        double totalCpu = 0;
        double totalMem = 0;
        double totalLimit = 0;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // CPUPerc: "12.34%"
                if (root.TryGetProperty("CPUPerc", out var cpuEl))
                {
                    var cpuStr = cpuEl.GetString()?.TrimEnd('%') ?? "0";
                    if (double.TryParse(cpuStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cpu))
                        totalCpu += cpu;
                }

                // MemUsage: "123.4MiB / 15.6GiB"
                if (root.TryGetProperty("MemUsage", out var memEl))
                {
                    var memStr = memEl.GetString() ?? "";
                    var parts = memStr.Split('/');
                    if (parts.Length >= 1) totalMem += ParseMemory(parts[0].Trim());
                    if (parts.Length >= 2) totalLimit = ParseMemory(parts[1].Trim());
                }
            }
            catch { }
        }

        if (_cachedCores == 0)
        {
            var nprocStr = await RunWslAsync("nproc");
            int.TryParse(nprocStr.Trim(), out _cachedCores);
        }
        return (totalCpu, totalMem, totalLimit, _cachedCores);
    }

    private static double ParseMemory(string s)
    {
        s = s.Trim();
        if (s.EndsWith("GiB", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v * 1024 : 0;
        if (s.EndsWith("MiB", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        if (s.EndsWith("kB", StringComparison.OrdinalIgnoreCase) || s.EndsWith("KiB", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v / 1024 : 0;
        return 0;
    }

    public async Task<(string installed, string candidate)> GetDockerVersionInfoAsync()
    {
        var output = await RunWslAsync("apt policy docker-ce 2>/dev/null");
        var installed = "";
        var candidate = "";
        var versionRegex = new System.Text.RegularExpressions.Regex(@"(\d+\.\d+\.\d+)");
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Installed:"))
            {
                var m = versionRegex.Match(trimmed); if (m.Success) installed = m.Value;
            }
            else if (trimmed.StartsWith("Candidate:"))
            {
                var m = versionRegex.Match(trimmed); if (m.Success) candidate = m.Value;
            }
        }
        return (installed, candidate);
    }

    public Process StartUpdateProcess()
    {
        var psi = Platform.IsWindows
            ? Platform.ShellCommand("-u root -- apt-get install --only-upgrade -y docker-ce docker-ce-cli containerd.io")
            : Platform.ShellCommand("sudo apt-get install --only-upgrade -y docker-ce docker-ce-cli containerd.io");
        return Process.Start(psi)!;
    }

    public async Task RecreateContainerAsync(ContainerInfo container)
    {
        if (!string.IsNullOrEmpty(container.ComposeFile))
            await RunWslAsync($"docker compose -f \"{container.ComposeFile}\" up -d --force-recreate --no-deps {container.Service}");
    }

    public async Task<ContainerDetail> GetContainerDetailAsync(string id)
    {
        var (json, _, _) = await RunWslDetailedAsync($"docker inspect {id}");
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement[0];

        var name = el.GetProperty("Name").GetString()?.TrimStart('/') ?? id;

        var config = el.GetProperty("Config");
        var image = config.TryGetProperty("Image", out var imgEl) ? imgEl.GetString() ?? "" : "";

        var created = "";
        if (el.TryGetProperty("Created", out var createdEl) &&
            DateTime.TryParse(createdEl.GetString(), out var createdDt))
        {
            var ago = FormatDuration(DateTime.UtcNow - createdDt.ToUniversalTime());
            created = $"{ago} ago  ({createdDt.ToLocalTime():yyyy-MM-dd HH:mm})";
        }

        var status = FormatStatus(el.GetProperty("State"));

        string? memLimit = null, cpuLimit = null;
        if (el.TryGetProperty("HostConfig", out var hc))
        {
            if (hc.TryGetProperty("Memory", out var memEl) && memEl.GetInt64() > 0)
                memLimit = FormatBytes(memEl.GetInt64());
            if (hc.TryGetProperty("NanoCpus", out var cpuEl) && cpuEl.GetInt64() > 0)
                cpuLimit = $"{cpuEl.GetInt64() / 1_000_000_000.0:F2} CPUs";
        }

        var mounts = new List<MountInfo>();
        if (el.TryGetProperty("Mounts", out var mountsEl))
            foreach (var m in mountsEl.EnumerateArray())
            {
                var src  = m.TryGetProperty("Source",      out var s)  ? s.GetString()  ?? "" : "";
                var dst  = m.TryGetProperty("Destination", out var d)  ? d.GetString()  ?? "" : "";
                var mode = m.TryGetProperty("RW",          out var rw) ? (rw.GetBoolean() ? "rw" : "ro") : "rw";
                if (!string.IsNullOrEmpty(src) || !string.IsNullOrEmpty(dst))
                    mounts.Add(new MountInfo(src, dst, mode));
            }

        var networks = new List<NetworkInfo>();
        if (el.TryGetProperty("NetworkSettings", out var ns) &&
            ns.TryGetProperty("Networks", out var netsEl))
            foreach (var net in netsEl.EnumerateObject())
            {
                var ip = net.Value.TryGetProperty("IPAddress", out var ipEl) ? ipEl.GetString() ?? "" : "";
                var gw = net.Value.TryGetProperty("Gateway",   out var gwEl) ? gwEl.GetString() ?? "" : "";
                networks.Add(new NetworkInfo(net.Name, ip, gw));
            }

        var ports = new List<string>();
        if (el.TryGetProperty("NetworkSettings", out var ns2) &&
            ns2.TryGetProperty("Ports", out var portsEl) &&
            portsEl.ValueKind == JsonValueKind.Object)
            foreach (var portEntry in portsEl.EnumerateObject())
                if (portEntry.Value.ValueKind == JsonValueKind.Array)
                    foreach (var binding in portEntry.Value.EnumerateArray())
                        if (binding.TryGetProperty("HostPort", out var hp))
                        {
                            var parts = portEntry.Name.Split('/');
                            ports.Add($"{hp.GetString()} → {parts[0]}/{(parts.Length > 1 ? parts[1] : "tcp")}");
                        }
        ports = ports.Distinct().ToList();

        // Restart policy
        string? restartPolicy = null;
        if (el.TryGetProperty("HostConfig", out var hc2) &&
            hc2.TryGetProperty("RestartPolicy", out var rpEl) &&
            rpEl.TryGetProperty("Name", out var rpName))
        {
            var rpStr = rpName.GetString() ?? "";
            if (!string.IsNullOrEmpty(rpStr) && rpStr != "no")
            {
                restartPolicy = rpStr;
                if (rpStr == "on-failure" && rpEl.TryGetProperty("MaximumRetryCount", out var retries) && retries.GetInt32() > 0)
                    restartPolicy += $":{retries.GetInt32()}";
            }
        }

        // Command
        string? command = null;
        if (config.TryGetProperty("Cmd", out var cmdEl) && cmdEl.ValueKind == JsonValueKind.Array)
        {
            var parts = cmdEl.EnumerateArray().Select(c => c.GetString() ?? "").ToList();
            if (parts.Count > 0) command = string.Join(" ", parts);
        }

        // State details: exit code, OOM, started/finished
        string? exitCode = null;
        bool oomKilled = false;
        string? startedAt = null;
        string? finishedAt = null;
        if (el.TryGetProperty("State", out var stateEl))
        {
            if (stateEl.TryGetProperty("ExitCode", out var ecEl))
                exitCode = ecEl.GetInt32().ToString();
            if (stateEl.TryGetProperty("OOMKilled", out var oomEl))
                oomKilled = oomEl.GetBoolean();
            if (stateEl.TryGetProperty("StartedAt", out var saEl) &&
                DateTime.TryParse(saEl.GetString(), out var saDt) && saDt.Year > 1)
                startedAt = saDt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            if (stateEl.TryGetProperty("FinishedAt", out var faEl) &&
                DateTime.TryParse(faEl.GetString(), out var faDt) && faDt.Year > 1)
                finishedAt = faDt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        // Environment variables (filter out common noise)
        var envVars = new List<string>();
        if (config.TryGetProperty("Env", out var envEl) && envEl.ValueKind == JsonValueKind.Array)
            foreach (var e in envEl.EnumerateArray())
            {
                var s = e.GetString() ?? "";
                if (!string.IsNullOrEmpty(s)) envVars.Add(s);
            }

        return new ContainerDetail(id, name, image, created, status, memLimit, cpuLimit, mounts, networks, ports,
            restartPolicy, command, exitCode, oomKilled, startedAt, finishedAt, envVars);
    }

    public async Task<ContainerStats?> GetContainerStatsDetailAsync(string id)
    {
        var output = await RunWslAsync($"docker stats --no-stream --format '{{{{json .}}}}' {id}");
        if (string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            using var doc = JsonDocument.Parse(output.Trim().Split('\n')[0]);
            var r = doc.RootElement;
            var netIo    = r.TryGetProperty("NetIO",    out var n) ? n.GetString() ?? "" : "";
            var blockIo  = r.TryGetProperty("BlockIO",  out var b) ? b.GetString() ?? "" : "";
            var netParts   = netIo.Split('/');
            var blockParts = blockIo.Split('/');
            var memRaw = r.TryGetProperty("MemUsage", out var mu) ? mu.GetString() ?? "" : "";
            var memParts = memRaw.Split('/');
            return new ContainerStats(
                CpuPercent: r.TryGetProperty("CPUPerc", out var cpu)  ? cpu.GetString()  ?? "" : "",
                MemUsage:   memParts.Length > 0 ? memParts[0].Trim() : "",
                MemLimit:   memParts.Length > 1 ? memParts[1].Trim() : "",
                MemPercent: r.TryGetProperty("MemPerc", out var mp)   ? mp.GetString()   ?? "" : "",
                NetIn:      netParts.Length   > 0 ? netParts[0].Trim()   : "",
                NetOut:     netParts.Length   > 1 ? netParts[1].Trim()   : "",
                BlockIn:    blockParts.Length > 0 ? blockParts[0].Trim() : "",
                BlockOut:   blockParts.Length > 1 ? blockParts[1].Trim() : "",
                Pids:       r.TryGetProperty("PIDs", out var pids) ? pids.GetString() ?? "" : ""
            );
        }
        catch { return null; }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GiB";
        if (bytes >= 1024 * 1024)         return $"{bytes / (1024.0 * 1024):F0} MiB";
        return $"{bytes / 1024.0:F0} KiB";
    }

    public Process StartLogsProcess(string id)    {
        var psi = Platform.ShellCommand($"docker logs -f --tail 20 {id}");
        return Process.Start(psi)!;
    }

    public async Task<List<DockerNetworkInfo>> GetNetworksAsync()
    {
        var (idsRaw, _, idsExit) = await RunWslDetailedAsync("docker network ls -q");
        if (idsExit != 0 || string.IsNullOrWhiteSpace(idsRaw)) return new();

        var ids = string.Join(" ", idsRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var (json, _, exit) = await RunWslDetailedAsync($"docker network inspect {ids}");
        if (exit != 0 || !json.TrimStart().StartsWith('[')) return new();

        var result = new List<DockerNetworkInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id      = el.TryGetProperty("Id",     out var idEl)     ? (idEl.GetString()     ?? "")[..Math.Min(12, idEl.GetString()?.Length ?? 0)]  : "";
                var name    = el.TryGetProperty("Name",   out var nameEl)   ? nameEl.GetString()   ?? "" : "";
                var driver  = el.TryGetProperty("Driver", out var driverEl) ? driverEl.GetString() ?? "" : "";
                var scope   = el.TryGetProperty("Scope",  out var scopeEl)  ? scopeEl.GetString()  ?? "" : "";
                var created = el.TryGetProperty("Created", out var createdEl) ? FormatRelativeTime(createdEl.GetString() ?? "") : "";
                var internalNet = el.TryGetProperty("Internal", out var internalEl) && internalEl.GetBoolean();

                var subnet = ""; var gateway = "";
                if (el.TryGetProperty("IPAM", out var ipam) &&
                    ipam.TryGetProperty("Config", out var configs) &&
                    configs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cfg in configs.EnumerateArray())
                    {
                        if (subnet  == "" && cfg.TryGetProperty("Subnet",  out var s)) subnet  = s.GetString() ?? "";
                        if (gateway == "" && cfg.TryGetProperty("Gateway", out var g)) gateway = g.GetString() ?? "";
                    }
                }

                var containerCount = 0;
                if (el.TryGetProperty("Containers", out var containers) && containers.ValueKind == JsonValueKind.Object)
                    containerCount = containers.EnumerateObject().Count();

                result.Add(new DockerNetworkInfo(id, name, driver, scope, subnet, gateway, internalNet, containerCount, created));
            }
        }
        catch { }
        return result;
    }

    public async Task<List<ImageInfo>> GetImagesAsync()
    {
        // Get image references currently used by containers (running or stopped)
        var (usedRaw, _, _) = await RunWslDetailedAsync("docker ps -a --format \"{{.Image}}\"");
        var usedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var img in usedRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            usedImages.Add(img);
            // Normalise tagless references (e.g. "nginx" → also try "nginx:latest")
            if (!img.Contains(':') && !img.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                usedImages.Add(img + ":latest");
        }

        var (output, _, exit) = await RunWslDetailedAsync(
            "docker image ls --no-trunc --format \"{{.ID}}\\t{{.Repository}}\\t{{.Tag}}\\t{{.Size}}\\t{{.CreatedSince}}\"");
        if (exit != 0 || string.IsNullOrWhiteSpace(output)) return new();

        var result = new List<ImageInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 5) continue;
            var fullId  = parts[0].StartsWith("sha256:") ? parts[0][7..] : parts[0];
            var shortId = fullId[..Math.Min(12, fullId.Length)];
            var repo    = parts[1];
            var tag     = parts[2];
            bool inUse  = repo != "<none>" &&
                          (usedImages.Contains($"{repo}:{tag}") || usedImages.Contains(repo));
            result.Add(new ImageInfo(shortId, repo, tag, parts[3], parts[4], inUse));
        }
        return result;
    }

    public async Task<List<VolumeInfo>> GetVolumesAsync()
    {
        // Get dangling (unused) volume names
        var (danglingRaw, _, _) = await RunWslDetailedAsync("docker volume ls --filter dangling=true -q");
        var danglingNames = danglingRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                       .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var (namesRaw, _, namesExit) = await RunWslDetailedAsync("docker volume ls -q");
        if (namesExit != 0 || string.IsNullOrWhiteSpace(namesRaw)) return new();

        var names = string.Join(" ", namesRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                             .Select(n => $"\"{n}\""));
        var (json, _, exit) = await RunWslDetailedAsync($"docker volume inspect {names}");
        if (exit != 0 || !json.TrimStart().StartsWith('[')) return new();

        var result = new List<VolumeInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name       = el.TryGetProperty("Name",       out var nameEl)   ? nameEl.GetString()   ?? "" : "";
                var driver     = el.TryGetProperty("Driver",     out var driverEl) ? driverEl.GetString() ?? "" : "";
                var mountpoint = el.TryGetProperty("Mountpoint", out var mpEl)     ? mpEl.GetString()     ?? "" : "";
                var scope      = el.TryGetProperty("Scope",      out var scopeEl)  ? scopeEl.GetString()  ?? "" : "";
                var created    = el.TryGetProperty("CreatedAt",  out var createdEl) ? FormatRelativeTime(createdEl.GetString() ?? "") : "";
                result.Add(new VolumeInfo(name, driver, mountpoint, scope, created, danglingNames.Contains(name)));
            }
        }
        catch { }
        return result;
    }

    public async Task<(bool success, string error)> DeleteNetworkAsync(string id)
    {
        var (_, stderr, exit) = await RunWslDetailedAsync($"docker network rm {id}");
        return exit == 0 ? (true, "") : (false, stderr.Split('\n')[0]);
    }

    public async Task<(bool success, string error)> DeleteImageAsync(string id)
    {
        var (_, stderr, exit) = await RunWslDetailedAsync($"docker image rm {id}");
        return exit == 0 ? (true, "") : (false, stderr.Split('\n')[0]);
    }

    public async Task<(bool success, string error)> DeleteVolumeAsync(string name)
    {
        var (_, stderr, exit) = await RunWslDetailedAsync($"docker volume rm \"{name}\"");
        return exit == 0 ? (true, "") : (false, stderr.Split('\n')[0]);
    }

    public async Task<string> GetNetworkDetailJsonAsync(string id)
        => (await RunWslDetailedAsync($"docker network inspect {id}")).output;

    public async Task<string> GetImageDetailJsonAsync(string id)
        => (await RunWslDetailedAsync($"docker image inspect {id}")).output;

    public async Task<string> GetVolumeDetailJsonAsync(string name)
        => (await RunWslDetailedAsync($"docker volume inspect \"{name}\"")).output;

    private static string FormatRelativeTime(string iso)
    {
        if (!DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)) return iso;
        var age = DateTime.UtcNow - dt.ToUniversalTime();
        if (age.TotalDays  >= 365) return $"{(int)(age.TotalDays / 365)}y ago";
        if (age.TotalDays  >= 30)  return $"{(int)(age.TotalDays / 30)}mo ago";
        if (age.TotalDays  >= 1)   return $"{(int)age.TotalDays}d ago";
        if (age.TotalHours >= 1)   return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalMinutes}m ago";
    }
}
