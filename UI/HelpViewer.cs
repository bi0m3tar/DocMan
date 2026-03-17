namespace DocMan.UI;

public static class HelpViewer
{
    public static async Task ShowAsync()
    {
        Render();

        while (true)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter ||
                    key.Key == ConsoleKey.H || key.Key == ConsoleKey.Q)
                    break;
            }
            await Task.Delay(50);
        }

        Console.ResetColor();
        Console.Clear();
    }

    private static void Render()
    {
        var width = Console.WindowWidth;
        var height = Console.WindowHeight;

        Console.Clear();

        // Row 0: title bar
        Console.SetCursorPosition(0, 0);
        Console.ForegroundColor = ConsoleColor.Green;
        var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "?");
        Console.Write(("DocMan - DOcker Container MANager  " + appVersion).PadRight(width));

        // Row 1: header
        Console.SetCursorPosition(0, 1);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("--- KEYBOARD SHORTCUTS --- ESC/Enter to close ---".PadRight(width));

        // Row 2: separator
        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(new string('-', width));

        var sections = new List<(string heading, List<(string key, string desc)> items)>
        {
            ("Navigation", new()
            {
                ("↑ / ↓",       "Move selection up/down"),
                ("← / →",       "Switch to previous / next page  (Containers → Networks → Images → Volumes)"),
                ("SPACE",        "Mark/unmark container for batch action"),
                ("ENTER",        "Open Container Action menu for selected/marked container(s)"),
            }),
            ("Live Log Panel", new()
            {
                ("L",            "Toggle live log panel for highlighted container"),
            }),
            ("Highlighted Container Actions  (Shift + key)", new()
            {
                ("Shift+L",      "Fullscreen live logs for highlighted container"),
                ("Shift+I",      "Fullscreen info for highlighted container"),
                ("Shift+T",      "Open terminal in highlighted container (running only)"),
                ("Shift+P",      "Start highlighted container (or all containers in project)"),
                ("Shift+S",      "Stop highlighted container (or all containers in project)"),
                ("Shift+K",      "Kill highlighted container (or all containers in project)"),
                ("Shift+R",      "Restart highlighted container (or all containers in project)"),
                ("Shift+U",      "Recreate highlighted container (or all containers in project)"),
                ("Shift+D",      "Delete highlighted container (with confirmation)"),
            }),
            ("Resource Viewers (Networks / Images / Volumes)", new()
            {
                ("N",            "Open Networks viewer (from any page)"),
                ("I",            "Open Images viewer (from any page)"),
                ("V",            "Open Volumes viewer (from any page)"),
                ("C / ESC",      "Return to Containers page (from viewer)"),
                ("← →",          "Cycle to previous / next page"),
                ("SPACE",        "Mark / unmark selected item (multi-select)"),
                ("ENTER",        "Open action menu for selected item"),
                ("T",            "Toggle Status filter (In use → Unused → All)"),
                ("D",            "Delete marked items (Networks / Volumes)  │  Shift+D: Delete selected item"),
                ("P",            "Delete marked images  │  Shift+D: Delete selected image"),
                ("X",            "Prune unused items (images, networks, volumes)"),
                ("Shift+I",      "Open Detailed Info for selected item (Networks / Images / Volumes)"),
            }),
            ("Global Actions", new()
            {
                ("P",            "Start ALL stopped containers"),
                ("S",            "Stop ALL running containers"),
                ("D",            "Delete marked containers (with confirmation)"),
                ("T",            "Toggle Status filter: All → Running Only → Not Running"),
                ("U",            "Update Docker Engine"),
                ("W",            "Restart WSL (Windows) / Restart Docker daemon (Linux)"),
                ("H",            "Show this help page"),
                ("Q",            "Quit DocMan"),
            }),
            ("Container Action Menu  (open with ENTER)", new()
            {
                ("1",            "Start"),
                ("2",            "Stop"),
                ("3",            "Restart"),
                ("4",            "Recreate (up --force-recreate)  — compose projects only"),
                ("5",            "Kill"),
                ("6",            "Delete"),
                ("7",            "Live Logs  — individual containers only"),
                ("8",            "Detailed Info  — individual containers only"),
                ("9",            "Terminal  — running individual containers only"),
                ("C / ESC",      "Cancel menu"),
            }),
        };

        int row = 3;
        foreach (var (heading, items) in sections)
        {
            if (row >= height - 1) break;

            Console.SetCursorPosition(0, row++);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  {heading}".PadRight(width));

            foreach (var (key, desc) in items)
            {
                if (row >= height - 1) break;
                Console.SetCursorPosition(0, row++);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"    {key,-20}");
                Console.ForegroundColor = ConsoleColor.Gray;
                var truncated = desc.Length > width - 26 ? desc[..(width - 29)] + "..." : desc;
                Console.Write(truncated.PadRight(width - 24));
            }

            // blank line between sections
            if (row < height - 1)
            {
                Console.SetCursorPosition(0, row++);
                Console.Write(new string(' ', width));
            }
        }

        // Clear remaining rows
        for (int i = row; i < height; i++)
        {
            Console.SetCursorPosition(0, i);
            Console.Write(new string(' ', width));
        }

        Console.ResetColor();
    }
}
