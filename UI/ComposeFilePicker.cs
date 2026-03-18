namespace DocMan.UI;

/// <summary>
/// Interactive file browser overlay for selecting a docker-compose file to run.
/// </summary>
public static class ComposeFilePicker
{
    private static readonly HashSet<string> ComposeFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml"
    };

    private const int OverlayRow    = 3;
    private const int OverlayWidth  = 82;
    private const int ListRows      = 15;
    private const int OverlayHeight = ListRows + 5; // top border + dir + sep + list + hint + bottom

    /// <summary>
    /// Shows an interactive file picker and returns the selected compose file path,
    /// or null if the user cancelled.
    /// </summary>
    public static string? Pick()
    {
        string currentDir;
        try   { currentDir = Directory.GetCurrentDirectory(); }
        catch { currentDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }

        int selected = 0;
        int scroll   = 0;

        while (true)
        {
            var entries = GetEntries(currentDir);
            selected = Math.Clamp(selected, 0, Math.Max(0, entries.Count - 1));
            if (selected < scroll) scroll = selected;
            if (selected >= scroll + ListRows) scroll = selected - ListRows + 1;
            scroll = Math.Max(0, scroll);

            Render(currentDir, entries, selected, scroll);

            var key = Console.ReadKey(true);
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    ClearOverlay();
                    return null;

                case ConsoleKey.UpArrow:
                    if (entries.Count > 0)
                        selected = selected > 0 ? selected - 1 : entries.Count - 1;
                    break;

                case ConsoleKey.DownArrow:
                    if (entries.Count > 0)
                        selected = selected < entries.Count - 1 ? selected + 1 : 0;
                    break;

                case ConsoleKey.PageUp:
                    selected = Math.Max(0, selected - ListRows);
                    break;

                case ConsoleKey.PageDown:
                    if (entries.Count > 0) selected = Math.Min(entries.Count - 1, selected + ListRows);
                    break;

                case ConsoleKey.RightArrow:
                    if (entries.Count > 0)
                    {
                        var (_, rPath, rIsDir) = entries[selected];
                        if (rIsDir) { currentDir = rPath; selected = 0; scroll = 0; }
                    }
                    break;

                case ConsoleKey.Enter:
                    if (entries.Count == 0) break;
                    var (_, fullPath, isDir) = entries[selected];
                    if (isDir)
                    {
                        currentDir = fullPath;
                        selected = 0; scroll = 0;
                    }
                    else
                    {
                        ClearOverlay();
                        return fullPath;
                    }
                    break;

                case ConsoleKey.Backspace:
                case ConsoleKey.LeftArrow:
                    var parent = Directory.GetParent(currentDir);
                    if (parent != null) { currentDir = parent.FullName; selected = 0; scroll = 0; }
                    break;
            }
        }
    }

    private static List<(string name, string fullPath, bool isDir)> GetEntries(string dir)
    {
        var result = new List<(string, string, bool)>();
        try
        {
            var parent = Directory.GetParent(dir);
            if (parent != null)
                result.Add(("..", parent.FullName, true));

            foreach (var d in Directory.GetDirectories(dir)
                .Select(d => (name: Path.GetFileName(d) ?? "", path: d))
                .Where(d => d.name.Length > 0 && d.name[0] != '.')
                .OrderBy(d => d.name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add((d.name + "/", d.path, true));
            }

            foreach (var f in Directory.GetFiles(dir)
                .Where(f => ComposeFileNames.Contains(Path.GetFileName(f)))
                .OrderBy(Path.GetFileName))
            {
                result.Add((Path.GetFileName(f)!, f, false));
            }
        }
        catch { /* access denied, invalid path, etc. */ }
        return result;
    }

    private static void Render(string currentDir, List<(string name, string fullPath, bool isDir)> entries, int selected, int scroll)
    {
        const int w        = OverlayWidth;
        const int contentW = w - 4; // border + space + content + space + border

        // Top border
        Console.SetCursorPosition(0, OverlayRow);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.BackgroundColor = ConsoleColor.Black;
        const string title = " Choose Compose File ";
        int dashes = w - 2 - title.Length;
        int leftD  = dashes / 2;
        int rightD = dashes - leftD;
        Console.Write("+" + new string('-', leftD) + title + new string('-', rightD) + "+");
        Console.ResetColor();

        // Directory path row
        DrawLine(OverlayRow + 1, TruncateLeft(currentDir, contentW), ConsoleColor.White);

        // Separator
        Console.SetCursorPosition(0, OverlayRow + 2);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Write("|" + new string('-', w - 2) + "|");
        Console.ResetColor();

        // List rows
        for (int i = 0; i < ListRows; i++)
        {
            int idx = scroll + i;
            int row = OverlayRow + 3 + i;

            Console.SetCursorPosition(0, row);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write("|");

            if (idx < entries.Count)
            {
                var (name, _, isDir) = entries[idx];
                bool isSelected = idx == selected;

                var display = "  " + name;
                if (display.Length > contentW) display = display[..contentW];

                if (isSelected)
                {
                    Console.BackgroundColor = ConsoleColor.DarkCyan;
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.BackgroundColor = ConsoleColor.DarkBlue;
                    Console.ForegroundColor = name == ".."  ? ConsoleColor.DarkGray
                                            : isDir         ? ConsoleColor.Cyan
                                                            : ConsoleColor.Green;
                }
                Console.Write(" " + display.PadRight(contentW) + " ");
            }
            else
            {
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.Write(new string(' ', w - 2));
            }

            Screen.SetCursorPosition(w - 1, row);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write("|");
            Console.ResetColor();
        }

        // Hint row
        int total = entries.Count;
        var hint = $"  ← /BS: Up  → /ENTER: Open  ↑↓/PgUp/PgDn: Navigate (wraps)  ESC: Cancel  [{(total > 0 ? selected + 1 : 0)}/{total}]";
        DrawLine(OverlayRow + 3 + ListRows, hint, ConsoleColor.Gray);

        // Bottom border
        Console.SetCursorPosition(0, OverlayRow + OverlayHeight - 1);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.BackgroundColor = ConsoleColor.Black;
        Console.Write("+" + new string('-', w - 2) + "+");
        Console.ResetColor();
    }

    private static void DrawLine(int row, string text, ConsoleColor textColor)
    {
        const int w        = OverlayWidth;
        const int contentW = w - 4;

        Console.SetCursorPosition(0, row);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Write("|");

        Console.ForegroundColor = textColor;
        var display = text.Length > contentW ? text[..contentW] : text;
        Console.Write(" " + display.PadRight(contentW) + " ");

        Screen.SetCursorPosition(w - 1, row);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Write("|");
        Console.ResetColor();
    }

    private static void ClearOverlay()
    {
        Console.ResetColor();
        for (int i = 0; i < OverlayHeight; i++)
        {
            Console.SetCursorPosition(0, OverlayRow + i);
            Console.Write(new string(' ', OverlayWidth));
        }
        while (Console.KeyAvailable) Console.ReadKey(true);
    }

    /// <summary>Truncates a string from the left, adding "..." prefix if needed.</summary>
    private static string TruncateLeft(string s, int maxLen)
    {
        if (s.Length <= maxLen) return s;
        const string ellipsis = "...";
        return ellipsis + s[(s.Length - maxLen + ellipsis.Length)..];
    }
}
