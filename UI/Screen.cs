namespace DocMan.UI;

public static class Screen
{
    public static int Width => Console.WindowWidth;
    public static int Height => Console.WindowHeight;

    public static void Initialize()
    {
        EnableVirtualTerminalProcessing();
        Console.Clear();
        Console.CursorVisible = false;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
    }

    /// <summary>Sets a scroll region so rows above <paramref name="firstScrollRow"/> are frozen.</summary>
    public static void SetScrollRegion(int firstScrollRow)
    {
        // ANSI rows are 1-based; firstScrollRow is 0-based
        Console.Write($"\x1B[{firstScrollRow + 1};{Height}r\x1B[{firstScrollRow + 1};1H");
    }

    /// <summary>Resets the scroll region to the full screen.</summary>
    public static void ResetScrollRegion() => Console.Write("\x1B[r");

    private static void EnableVirtualTerminalProcessing()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
            if (GetConsoleMode(handle, out var mode))
                SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern nint GetStdHandle(int n);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool GetConsoleMode(nint h, out uint m);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool SetConsoleMode(nint h, uint m);

    public static void Clear()
    {
        Console.Clear();
    }

    public static void SetCursorPosition(int left, int top)
    {
        if (left >= 0 && left < Width && top >= 0 && top < Height)
        {
            Console.SetCursorPosition(left, top);
        }
    }

    public static void Write(string text, ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        var oldFg = Console.ForegroundColor;
        var oldBg = Console.BackgroundColor;

        if (foreground.HasValue) Console.ForegroundColor = foreground.Value;
        if (background.HasValue) Console.BackgroundColor = background.Value;

        Console.Write(text);

        Console.ForegroundColor = oldFg;
        Console.BackgroundColor = oldBg;
    }

    public static void WriteLine(string text, ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        Write(text + "\n", foreground, background);
    }

    public static void ClearLine(int line)
    {
        SetCursorPosition(0, line);
        Write(new string(' ', Width));
        SetCursorPosition(0, line);
    }

    public static void ClearArea(int startLine, int endLine)
    {
        for (int i = startLine; i <= endLine && i < Height; i++)
        {
            ClearLine(i);
        }
    }

    public static ConsoleColor GetStatusColor(string status)
    {
        if (status.Contains("Up")) return ConsoleColor.Green;
        if (status.Contains("Restarting")) return ConsoleColor.Yellow;
        return ConsoleColor.Red;
    }

    public static ConsoleColor GetHealthColor(string health)
    {
        return health switch
        {
            "healthy" => ConsoleColor.Green,
            "unhealthy" => ConsoleColor.Red,
            _ => ConsoleColor.DarkGray
        };
    }

    private static readonly System.Text.RegularExpressions.Regex AnsiRegex =
        new(@"\x1B\[[0-9;]*[a-zA-Z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string StripAnsi(string line)
    {
        var s = AnsiRegex.Replace(line, "");
        s = s.Replace("\t", "    ");
        return new string(s.Where(c => !char.IsControl(c)).ToArray());
    }
}
