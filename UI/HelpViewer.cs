namespace DocMan.UI;

public static class HelpViewer
{
    public static async Task ShowAsync()
    {
        var lines  = BuildLines();
        var scroll = 0;

        RenderAll(lines, scroll);

        while (true)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                var ch  = Console.WindowHeight - 3;
                var prev = scroll;
                switch (key.Key)
                {
                    case ConsoleKey.Escape: case ConsoleKey.Enter:
                    case ConsoleKey.H:      case ConsoleKey.Q:
                        Console.ResetColor(); Console.Clear(); return;
                    case ConsoleKey.UpArrow:   if (scroll > 0) scroll--; break;
                    case ConsoleKey.DownArrow: if (scroll < lines.Count - ch) scroll++; break;
                    case ConsoleKey.PageUp:    scroll = Math.Max(0, scroll - ch); break;
                    case ConsoleKey.PageDown:  scroll = Math.Max(0, Math.Min(scroll + ch, lines.Count - ch)); break;
                    case ConsoleKey.Home:      scroll = 0; break;
                    case ConsoleKey.End:       scroll = Math.Max(0, lines.Count - ch); break;
                }
                if (scroll != prev) RenderAll(lines, scroll);
            }
            await Task.Delay(50);
        }
    }

    private static void RenderAll(List<(string text, ConsoleColor color)> lines, int scroll)
    {
        var width  = Console.WindowWidth;
        var height = Console.WindowHeight;
        var ch     = height - 3; // content rows

        // Row 0: title
        Console.SetCursorPosition(0, 0);
        Console.ForegroundColor = ConsoleColor.Green;
        var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "?");
        Console.Write(("DocMan - DOcker Container MANager  " + appVersion).PadRight(width));

        // Row 1: header with scroll indicator
        Console.SetCursorPosition(0, 1);
        Console.ForegroundColor = ConsoleColor.White;
        var si = lines.Count > ch
            ? $"  [{scroll + 1}-{Math.Min(scroll + ch, lines.Count)}/{lines.Count}]" : "";
        Console.Write($"--- KEYBOARD SHORTCUTS --- ↑↓/PgUp/PgDn/Home/End  │  ESC/Enter to close ---{si}".PadRight(width));

        // Row 2: separator
        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('-', width));

        // Content rows
        for (int i = 0; i < ch; i++)
        {
            Console.SetCursorPosition(0, i + 3);
            var idx = scroll + i;
            if (idx < lines.Count)
            {
                var (text, color) = lines[idx];
                if (text.Length > 0 && text[0] == '\x01')
                {
                    // key row: yellow key + gray description
                    var sep = text.IndexOf('\x02');
                    var keyPart  = "    " + text[1..sep];
                    var descPart = sep + 1 < text.Length ? text[(sep + 1)..] : "";
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(keyPart);
                    Console.ForegroundColor = ConsoleColor.Gray;
                    var remaining = width - keyPart.Length;
                    var desc = descPart.Length > remaining ? descPart[..(remaining - 1)] + "…" : descPart;
                    Console.Write(desc.PadRight(Math.Max(0, remaining)));
                }
                else
                {
                    Console.ForegroundColor = color;
                    var padded = text.Length < width ? text + new string(' ', width - text.Length) : text[..width];
                    Console.Write(padded);
                }
            }
            else
            {
                Console.Write(new string(' ', width));
            }
        }

        Console.ResetColor();
    }

    private static List<(string text, ConsoleColor color)> BuildLines()
    {
        var lines = new List<(string text, ConsoleColor color)>();

        var sections = new List<(string heading, List<(string key, string desc)> items)>
        {
            ("All Pages", new()
            {
                ("↑ / ↓",        "Move selection up / down"),
                ("← / →",        "Switch to previous / next page  (Containers → Networks → Images → Volumes)"),
                ("SPACE",         "Mark / unmark selected item"),
                ("ENTER",         "Open action menu for selected item"),
                ("T",             "Toggle Status filter (cycles states per page)"),
                ("N",             "Go to Networks page"),
                ("I",             "Go to Images page"),
                ("V",             "Go to Volumes page"),
                ("C / ESC",       "Go to Containers page"),
                ("U",             "Update Docker Engine"),
                ("W",             "Restart WSL (Windows) / Restart Docker daemon (Linux)"),
                ("H",             "Show this help page"),
                ("Q",             "Quit DocMan"),
            }),
            ("Containers Page", new()
            {
                ("L",             "Toggle live log panel for highlighted container"),
                ("P",             "Start ALL stopped containers"),
                ("S",             "Stop ALL running containers (with confirmation)"),
                ("D",             "Delete marked containers"),
                ("F",             "Browse filesystem and run a selected compose file  (docker compose up -d)"),
                ("T",             "Cycle filter: All → Running Only → Not Running"),
            }),
            ("Containers Page — Shift Hotkeys  (act on highlighted container/project)", new()
            {
                ("Shift+L",       "Fullscreen live logs"),
                ("Shift+I",       "Fullscreen info"),
                ("Shift+T",       "Open terminal (running containers only)"),
                ("Shift+P",       "Start"),
                ("Shift+S",       "Stop"),
                ("Shift+K",       "Kill"),
                ("Shift+R",       "Restart"),
                ("Shift+U",       "Recreate (compose projects only)"),
                ("Shift+D",       "Delete (with confirmation)"),
            }),
            ("Networks / Images / Volumes Pages", new()
            {
                ("D",             "Delete marked items  (Networks / Images / Volumes)"),
                ("X",             "Prune unused items"),
                ("T",             "Toggle Status filter (In Use → Unused → All  /  + Dangling for Images)"),
                ("Shift+I",       "Open Detailed Info for selected item"),
                ("Shift+D",       "Delete selected item (with confirmation)"),
            }),
            ("Container Action Menu  (open with ENTER)", new()
            {
                ("1",             "Start"),
                ("2",             "Stop"),
                ("3",             "Restart"),
                ("4",             "Recreate (up --force-recreate)  — compose projects only"),
                ("5",             "Kill"),
                ("6",             "Delete"),
                ("7",             "Live Logs  — individual containers only"),
                ("8",             "Detailed Info  — individual containers only"),
                ("9",             "Terminal  — running individual containers only"),
                ("C / ESC",       "Cancel / close menu"),
            }),
        };

        foreach (var (heading, items) in sections)
        {
            lines.Add(($"  {heading}", ConsoleColor.Cyan));
            foreach (var (key, desc) in items)
            {
                // Encode key+desc as a single string with a separator the renderer splits on
                lines.Add(($"\x01{key,-20}\x02{desc}", ConsoleColor.Gray));
            }
            lines.Add(("", ConsoleColor.Gray));
        }

        return lines;
    }
}

