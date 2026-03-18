using DocMan.Services;

namespace DocMan.UI;

public static class AppNav
{
    private static readonly (string label, AppPage page)[] Pages =
    {
        ("C:Containers", AppPage.Containers),
        ("N:Networks",   AppPage.Networks),
        ("I:Images",     AppPage.Images),
        ("V:Volumes",    AppPage.Volumes),
    };

    // Row 0: title bar with docker/container info right-aligned; "(update available)" rendered in red
    public static void RenderTitleBar(string dockerSummary, int width,
        IList<(string text, ConsoleColor color)>? flags = null)
    {
        var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "?");
        var title = $"DocMan - DOcker Container MANager  {appVersion}";

        const string updateMarker = "(update available)";
        int  markerIdx  = dockerSummary.IndexOf(updateMarker, StringComparison.Ordinal);
        bool hasUpdate  = markerIdx >= 0;

        var before  = hasUpdate ? $"  {dockerSummary[..markerIdx]}" : (dockerSummary.Length > 0 ? $"  {dockerSummary}  " : "");
        var after   = hasUpdate ? $"{dockerSummary[(markerIdx + updateMarker.Length)..]}  " : "";
        var rightLen = before.Length + (hasUpdate ? updateMarker.Length + after.Length : 0);
        int flagLen  = flags?.Sum(f => f.text.Length) ?? 0;
        var padding  = Math.Max(0, 184 - title.Length - flagLen - rightLen);

        Console.SetCursorPosition(0, 0);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(title);
        if (flags != null)
            foreach (var (text, color) in flags) { Console.ForegroundColor = color; Console.Write(text); }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(new string(' ', padding));
        if (dockerSummary.Length > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(before);
            if (hasUpdate)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(updateMarker);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(after);
            }
        }
        Console.ResetColor();
    }

    // Row 1: nav shortcuts + global actions  |  Row 3: page tabs (right above ---)
    public static void RenderGlobalNav(AppPage current, int width)
    {
        // Row 1 — ←→:Switch Page + global actions
        Console.SetCursorPosition(0, 1);
        Console.ForegroundColor = ConsoleColor.Cyan;
        var row1 = "←→:Switch Page  │  U:Update Docker  ";
        row1 += Platform.IsWindows ? "W:Restart WSL  " : "W:Restart Docker  ";
        row1 += "H:Help  Q:Quit";
        Console.Write(row1.PadRight(width));

        // Row 3 — page tabs
        Console.SetCursorPosition(0, 3);
        int written = 0;
        for (int i = 0; i < Pages.Length; i++)
        {
            var (label, page) = Pages[i];
            bool isLast   = i == Pages.Length - 1;
            bool active   = page == current;
            int slotWidth = label.Length + 2;
            string entry  = active ? $"[{label}]" : $" {label}";
            string padded = isLast ? entry.PadRight(slotWidth) : entry.PadRight(slotWidth + 2);
            Console.ForegroundColor = active ? ConsoleColor.White : ConsoleColor.Gray;
            Console.Write(padded);
            written += padded.Length;
        }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(new string(' ', Math.Max(0, width - written)));
        Console.ResetColor();
    }

    // Row 2: separator row
    public static void RenderSeparator(int width)
    {
        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(new string('-', width));
        Console.ResetColor();
    }

    // Cycles to the next page (→ key)
    public static AppPage Next(AppPage current) => current switch
    {
        AppPage.Containers => AppPage.Networks,
        AppPage.Networks   => AppPage.Images,
        AppPage.Images     => AppPage.Volumes,
        AppPage.Volumes    => AppPage.Containers,
        _                  => AppPage.Containers,
    };

    // Cycles to the previous page (← key)
    public static AppPage Prev(AppPage current) => current switch
    {
        AppPage.Containers => AppPage.Volumes,
        AppPage.Networks   => AppPage.Containers,
        AppPage.Images     => AppPage.Networks,
        AppPage.Volumes    => AppPage.Images,
        _                  => AppPage.Containers,
    };
}
