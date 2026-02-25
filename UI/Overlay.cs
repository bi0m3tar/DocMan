namespace DocMan.UI;

public class Overlay
{
    private readonly int _startLine;
    private readonly int _width;
    private readonly int _height;
    private readonly List<string> _savedContent = new();

    // Regex to strip ANSI escape codes
    private static readonly System.Text.RegularExpressions.Regex AnsiRegex =
        new(@"\x1B\[[0-9;]*[a-zA-Z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public Overlay(int startLine, int width, int height)
    {
        _startLine = startLine;
        _width = width;
        _height = height;
    }

    public void Show(string title, List<string> lines)
    {
        var actualWidth = Math.Min(_width, Screen.Width);
        DrawBox(title, actualWidth);
        DrawContent(lines, actualWidth);
    }

    public void Update(List<string> lines)
    {
        var actualWidth = Math.Min(_width, Screen.Width);
        DrawContent(lines, actualWidth);
    }

    private void DrawContent(List<string> lines, int actualWidth)
    {
        var maxLineLength = actualWidth - 4; // -2 for borders, -2 for padding spaces
        var rightBorderCol = actualWidth - 1;

        for (int i = 0; i < _height - 2; i++)
        {
            var row = _startLine + i + 1;

            // Strip ANSI codes and expand tabs to get accurate display length
            var rawLine = i < lines.Count ? lines[i] : "";
            var displayLine = AnsiRegex.Replace(rawLine, "");
            displayLine = displayLine.Replace("\t", "    ");
            displayLine = new string(displayLine.Where(c => !char.IsControl(c)).ToArray());

            if (displayLine.Length > maxLineLength)
                displayLine = displayLine[..maxLineLength];

            // Write left border - use DarkBlue background to match content area
            Screen.SetCursorPosition(0, row);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write("|");

            // Write content (padded to exact width)
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write(" " + displayLine.PadRight(maxLineLength) + " ");

            // Write right border at ABSOLUTE position - always at the same column
            // Do NOT write anything after it to avoid line wrapping
            Screen.SetCursorPosition(rightBorderCol, row);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Write("|");

            Console.ResetColor();
        }
    }

    public void Hide()
    {
        Console.ResetColor();
        var w = Math.Min(_width, Screen.Width);
        for (int i = 0; i < _height; i++)
        {
            var row = _startLine + i;
            if (row < Screen.Height)
            {
                Console.SetCursorPosition(0, row);
                Console.Write(new string(' ', w));
            }
        }
        // Discard any keys pressed while overlay was visible
        while (Console.KeyAvailable) Console.ReadKey(true);
    }

    private void DrawBox(string title, int actualWidth)
    {
        // Top border
        Screen.SetCursorPosition(0, _startLine);
        var topBorder = "+" + new string('-', actualWidth - 2) + "+";
        if (title.Length > 0)
        {
            var titleText = $" {title} ";
            var remainingWidth = actualWidth - titleText.Length - 2;
            if (remainingWidth > 0)
            {
                var leftDashes = remainingWidth / 2;
                var rightDashes = remainingWidth - leftDashes;
                topBorder = "+" + new string('-', leftDashes) + titleText + new string('-', rightDashes) + "+";
            }
        }
        Screen.Write(topBorder, ConsoleColor.Yellow, ConsoleColor.Black);

        // Bottom border
        Screen.SetCursorPosition(0, _startLine + _height - 1);
        Screen.Write("+" + new string('-', actualWidth - 2) + "+", ConsoleColor.Yellow, ConsoleColor.Black);
    }

    private void SaveContent()
    {
        _savedContent.Clear();
    }
}
