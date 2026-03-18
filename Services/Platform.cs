using System.Diagnostics;

namespace DocMan.Services;

/// <summary>
/// Abstracts platform differences so the rest of the codebase can stay identical.
/// Windows: all shell commands are routed through "wsl".
/// Linux:   all shell commands are routed through "sh -c".
/// </summary>
internal static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>
    /// Converts a host file-system path to a Linux path suitable for use inside a WSL docker command.
    /// On Linux this is a no-op. On Windows it handles:
    ///   \\wsl.localhost\Distro\path  →  /path
    ///   \\wsl$\Distro\path          →  /path
    ///   C:\path                     →  /mnt/c/path
    /// </summary>
    public static string NormalizePathForDockerCommand(string path)
    {
        if (!IsWindows) return path;

        // UNC WSL paths: \\wsl.localhost\Distro\rest  or  \\wsl$\Distro\rest
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // Strip leading backslashes, then split: [server, distro, ...rest]
            var segments = path.TrimStart('\\').Split('\\');
            if (segments.Length > 2)
                return "/" + string.Join("/", segments[2..]);
            return "/";
        }

        // Drive-letter path: C:\path  →  /mnt/c/path
        if (path.Length >= 2 && path[1] == ':')
        {
            var drive = char.ToLowerInvariant(path[0]);
            var rest  = (path.Length > 2 ? path[2..] : "").Replace('\\', '/').TrimStart('/');
            return $"/mnt/{drive}/{rest}";
        }

        return path;
    }

    /// <summary>
    /// Creates a ProcessStartInfo for running a shell command with captured output.
    /// </summary>
    public static ProcessStartInfo ShellCommand(string command) =>
        new ProcessStartInfo(
            IsWindows ? "wsl" : "/bin/sh",
            IsWindows ? command : $"-c \"{command.Replace("\"", "\\\"")}\"")
        {
            UseShellExecute       = false,
            CreateNoWindow        = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
}
