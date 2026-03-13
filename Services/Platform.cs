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
