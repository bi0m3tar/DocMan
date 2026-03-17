using System.Text.Json;
using DocMan.Models;
using DocMan.Services;

namespace DocMan.UI;

public class VolumesViewer
{
    private readonly DockerService _dockerService;

    public VolumesViewer(DockerService dockerService) => _dockerService = dockerService;

    public async Task<AppPage> ShowAsync(string dockerSummary = "")
    {
        List<VolumeInfo>? volumes   = null;
        string?           loadError = null;
        var alive         = true;
        var needsRender   = true;
        var selectedIndex = 0;
        var markedNames   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filterMode = 0; // 0=all, 1=in use, 2=unused

        _ = Task.Run(async () =>
        {
            while (alive)
            {
                try   { volumes = await _dockerService.GetVolumesAsync(); loadError = null; }
                catch (Exception ex) { loadError = ex.Message; }
                await Task.Delay(5000);
            }
        });

        List<VolumeInfo>? lastVolumes = null;
        string?           lastError   = null;

        while (true)
        {
            if (!ReferenceEquals(volumes, lastVolumes) || loadError != lastError)
            {
                lastVolumes = volumes;
                lastError   = loadError;
                needsRender = true;
            }

            if (needsRender)
            {
                var visible = GetVisible(volumes, filterMode);
                selectedIndex = visible == null ? 0 : Math.Clamp(selectedIndex, 0, Math.Max(0, visible.Count - 1));
                Render(visible, selectedIndex, markedNames, loadError, dockerSummary, filterMode);
                needsRender = false;
            }

            if (Console.KeyAvailable)
            {
                var key     = Console.ReadKey(true);
                var visible = GetVisible(volumes, filterMode);
                var count   = visible?.Count ?? 0;

                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                    case ConsoleKey.C:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Containers;

                    case ConsoleKey.Q:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Quit;

                    case ConsoleKey.V:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Volumes;

                    case ConsoleKey.RightArrow:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Containers;

                    case ConsoleKey.LeftArrow:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Images;

                    case ConsoleKey.N:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Networks;

                    case ConsoleKey.I:
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        {
                            if (visible != null && count > 0)
                            { await ShowDetail(visible[selectedIndex]); needsRender = true; }
                        }
                        else { alive = false; Console.ResetColor(); Console.Clear(); return AppPage.Images; }
                        break;

                    case ConsoleKey.H:
                        await HelpViewer.ShowAsync();
                        needsRender = true; break;

                    case ConsoleKey.UpArrow:
                        if (selectedIndex > 0) { selectedIndex--; needsRender = true; } break;
                    case ConsoleKey.DownArrow:
                        if (selectedIndex < count - 1) { selectedIndex++; needsRender = true; } break;

                    case ConsoleKey.Spacebar:
                        if (visible != null && count > 0)
                        {
                            var name = visible[selectedIndex].Name;
                            if (!markedNames.Remove(name)) markedNames.Add(name);
                            needsRender = true;
                        }
                        break;

                    case ConsoleKey.T:
                        filterMode    = (filterMode + 1) % 3; // cycle: in use(1) → unused(2) → all(0)
                        selectedIndex = 0;
                        needsRender   = true;
                        break;

                    case ConsoleKey.D:
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        {
                            if (visible != null && count > 0)
                                await ConfirmDeleteOne(visible[selectedIndex]);
                            volumes     = null;
                            needsRender = true;
                        }
                        else if (markedNames.Count > 0 && volumes != null)
                        {
                            await DeleteMarked(volumes, markedNames);
                            volumes     = null;
                            needsRender = true;
                        }
                        else if (visible != null && count > 0)
                        {
                            await ShowActionMenu(visible[selectedIndex]);
                            volumes     = null;
                            needsRender = true;
                        }
                        break;

                    case ConsoleKey.X:
                        await PruneUnused();
                        volumes     = null;
                        needsRender = true;
                        break;

                    case ConsoleKey.Enter:
                        if (visible != null && count > 0)
                        {
                            await ShowActionMenu(visible[selectedIndex]);
                            volumes     = null;
                            needsRender = true;
                        }
                        break;

                    case ConsoleKey.U: case ConsoleKey.W:
                        break;
                }
            }

            await Task.Delay(50);
        }
    }

    private static List<VolumeInfo>? GetVisible(List<VolumeInfo>? all, int mode) =>
        all == null ? null :
        mode == 1 ? all.Where(v => !v.Dangling).OrderBy(v => v.Name).ToList() :
        mode == 2 ? all.Where(v => v.Dangling).OrderBy(v => v.Name).ToList() :
        all.OrderBy(v => v.Name).ToList();

    private async Task ShowActionMenu(VolumeInfo vol)
    {
        var options = new[] { "Detailed Info", !vol.Dangling ? "Delete  ⚠ in use!" : "Delete" };
        var ov      = new Overlay(5, 56, 11);
        var sel     = 0;

        void Draw()
        {
            var lines = new List<string> { "", $"  {Truncate(vol.Name, 50)}", "" };
            for (int i = 0; i < options.Length; i++)
                lines.Add(i == sel ? $"  > {i + 1}. {options[i]}" : $"    {i + 1}. {options[i]}");
            lines.Add(""); lines.Add("  ↑↓/1/2:Select  ENTER:Confirm  ESC:Cancel");
            ov.Update(lines);
        }

        ov.Show("Volume Actions", new List<string>());
        Draw();

        while (true)
        {
            if (!Console.KeyAvailable) { await Task.Delay(30); continue; }
            var k = Console.ReadKey(true);
            if (k.Key is ConsoleKey.Escape or ConsoleKey.C) { ov.Hide(); Console.ResetColor(); Console.Clear(); return; }
            if (k.Key == ConsoleKey.UpArrow)   { sel = (sel - 1 + options.Length) % options.Length; Draw(); continue; }
            if (k.Key == ConsoleKey.DownArrow) { sel = (sel + 1) % options.Length; Draw(); continue; }
            if (k.Key == ConsoleKey.D1) sel = 0;
            else if (k.Key == ConsoleKey.D2) sel = 1;
            else if (k.Key != ConsoleKey.Enter) continue;
            ov.Hide(); Console.ResetColor(); Console.Clear();
            if (sel == 0) await ShowDetail(vol);
            else          await ConfirmDeleteOne(vol);
            return;
        }
    }

    private async Task DeleteMarked(List<VolumeInfo> all, HashSet<string> names)
    {
        var targets = all.Where(v => names.Contains(v.Name)).ToList();
        var ov      = new Overlay(5, 62, Math.Min(targets.Count + 8, 24));
        var lines   = new List<string> { "", $"  Deleting {targets.Count} volume(s)...", "" };
        ov.Show("Delete Volumes", lines);
        foreach (var v in targets)
        {
            var (ok, err) = await _dockerService.DeleteVolumeAsync(v.Name);
            lines.Add(ok ? $"  ✓ {Truncate(v.Name, 54)}" : $"  ✗ {Truncate(v.Name, 30)}: {Truncate(err, 22)}");
            ov.Update(lines);
        }
        names.Clear();
        lines.Add(""); lines.Add("  Press any key...");
        ov.Update(lines); Console.ReadKey(true); ov.Hide();
        Console.ResetColor(); Console.Clear();
    }

    private async Task ShowDetail(VolumeInfo vol)
    {
        var json  = await _dockerService.GetVolumeDetailJsonAsync(vol.Name);
        var lines = BuildDetailLines(vol, json);
        await ShowScrollable($"VOLUME: {Truncate(vol.Name, 50)}", lines);
    }

    private async Task ConfirmDeleteOne(VolumeInfo vol)
    {
        var ov = new Overlay(6, 62, 8);
        ov.Show("Delete Volume", new List<string>
        {
            "", $"  Delete: {Truncate(vol.Name, 50)}", "",
            vol.Dangling ? "  This volume is unused." : "  ⚠ Volume may be in use by a container!",
            "", "  Y to confirm, any other key to cancel"
        });
        var k = Console.ReadKey(true); ov.Hide();
        if (k.Key != ConsoleKey.Y) { Console.ResetColor(); Console.Clear(); return; }

        var (ok, err) = await _dockerService.DeleteVolumeAsync(vol.Name);
        var res = new Overlay(6, 62, 7);
        res.Show("Delete Volume", new List<string>
        { "", ok ? $"  ✓ Deleted {Truncate(vol.Name, 50)}" : $"  ✗ Failed: {Truncate(err, 50)}", "", "  Press any key..." });
        Console.ReadKey(true); res.Hide();
        Console.ResetColor(); Console.Clear();
    }

    private static List<(string text, ConsoleColor color)> BuildDetailLines(VolumeInfo vol, string json)
    {
        var lines = new List<(string text, ConsoleColor color)>();
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(($"  Name       : {vol.Name}",   ConsoleColor.White));
        lines.Add(($"  Driver     : {vol.Driver}",   ConsoleColor.White));
        lines.Add(($"  Scope      : {vol.Scope}",    ConsoleColor.White));
        lines.Add(($"  Created    : {vol.Created}",  ConsoleColor.Gray));
        lines.Add(($"  In Use     : {(vol.Dangling ? "no ⚠" : "yes")}", vol.Dangling ? ConsoleColor.Yellow : ConsoleColor.White));
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(("  ── Mountpoint ─────────────────────────────", ConsoleColor.Cyan));
        lines.Add(($"    {vol.Mountpoint}", ConsoleColor.Gray));
        lines.Add(("", ConsoleColor.Gray));
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;
            if (el.TryGetProperty("Options", out var opts) && opts.ValueKind == JsonValueKind.Object && opts.EnumerateObject().Any())
            {
                lines.Add(("  ── Options ────────────────────────────────", ConsoleColor.Cyan));
                foreach (var o in opts.EnumerateObject()) lines.Add(($"    {o.Name} = {o.Value.GetString()}", ConsoleColor.Gray));
                lines.Add(("", ConsoleColor.Gray));
            }
            if (el.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object && labels.EnumerateObject().Any())
            {
                lines.Add(("  ── Labels ─────────────────────────────────", ConsoleColor.Cyan));
                foreach (var lbl in labels.EnumerateObject()) lines.Add(($"    {lbl.Name} = {lbl.Value.GetString()}", ConsoleColor.Gray));
            }
        }
        catch { }
        return lines;
    }

    private async Task PruneUnused()
    {
        var ov    = new Overlay(6, 70, 10);
        var lines = new List<string> { "", "  Pruning all dangling Docker volumes...", "", "  ESC/Enter to close (continues in background)" };
        ov.Show("Prune Volumes", lines);
        var task = _dockerService.PruneVolumesAsync();
        while (!task.IsCompleted)
        {
            if (Console.KeyAvailable) { var k = Console.ReadKey(true); if (k.Key is ConsoleKey.Escape or ConsoleKey.Enter) { ov.Hide(); return; } }
            await Task.Delay(100);
        }
        if (task.IsCompletedSuccessfully)
        {
            lines.RemoveAt(3);
            lines.AddRange(task.Result);
            lines.Add(""); lines.Add("  Press any key...");
            ov.Update(lines); Console.ReadKey(true);
        }
        ov.Hide(); Console.ResetColor(); Console.Clear();
    }

    private void Render(List<VolumeInfo>? volumes, int selectedIndex, HashSet<string> markedNames,
                        string? error, string dockerSummary, int filterMode)
    {
        var width  = Console.WindowWidth;
        var height = Console.WindowHeight;

        IList<(string text, ConsoleColor color)>? titleFlags =
            filterMode == 1 ? new[] { ("  [IN USE]",  ConsoleColor.Green) } :
            filterMode == 2 ? new[] { ("  [UNUSED]",  ConsoleColor.Yellow) } : null;
        AppNav.RenderTitleBar(dockerSummary, width, titleFlags);
        AppNav.RenderGlobalNav(AppPage.Volumes, width);

        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"↑↓:Navigate     │  SPACE:Mark  ENTER:Actions  │  D:Delete Marked  X:Prune  T:Toggle Status".PadRight(width));
        Console.ResetColor();

        Console.SetCursorPosition(0, 3);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('-', 184));
        Console.SetCursorPosition(0, 4);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(string.Format("{0,-3} {1,-60} {2,-10} {3,-10} {4,-10} {5}",
            " M", "NAME", "STATUS", "DRIVER", "SCOPE", "CREATED").PadRight(184));
        Console.SetCursorPosition(0, 5);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('-', 184));
        Console.ResetColor();

        if (error != null)
        { Console.SetCursorPosition(0, 6); Console.ForegroundColor = ConsoleColor.Red; Console.Write($"  Error: {error}".PadRight(width)); return; }
        if (volumes == null)
        { Console.SetCursorPosition(0, 6); Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("  Loading...".PadRight(width)); return; }

        var dataRow     = 6;
        var contentRows = height - 2 - dataRow;
        for (int i = 0; i < volumes.Count && i < contentRows; i++, dataRow++)
        {
            var v      = volumes[i];
            var mIcon  = markedNames.Contains(v.Name) ? "[x]" : "[ ]";
            var uIcon  = " ";
            var status = v.Dangling ? "unused" : "in use";
            var fg     = v.Dangling ? ConsoleColor.Red : ConsoleColor.Green;

            Console.SetCursorPosition(0, dataRow);
            if (i == selectedIndex) { Console.BackgroundColor = ConsoleColor.DarkCyan; Console.ForegroundColor = ConsoleColor.White; }
            else { Console.ResetColor(); Console.ForegroundColor = markedNames.Contains(v.Name) ? ConsoleColor.Cyan : fg; }

            Console.Write(string.Format("{0,-3} {1,-60} {2,-10} {3,-10} {4,-10} {5}",
                mIcon, Truncate(v.Name, 60), status,
                Truncate(v.Driver, 10), Truncate(v.Scope, 10), Truncate(v.Created, 16)).PadRight(width));
        }

        Console.ResetColor();
        for (int row = dataRow; row < height - 1; row++)
        { Console.SetCursorPosition(0, row); Console.Write(new string(' ', width)); }

        Console.SetCursorPosition(0, height - 1);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        var unusedCount = volumes.Count(v => v.Dangling);
        var footer      = $"  {volumes.Count} volume(s)";
        if (markedNames.Count > 0) footer += $"  │  {markedNames.Count} marked";
        if (unusedCount > 0) footer += $"  │  {unusedCount} unused";
        footer += "  │  auto-refreshes every 5s";
        Console.Write(footer.PadRight(width));
        Console.ResetColor();
    }

    private static async Task ShowScrollable(string title, List<(string text, ConsoleColor color)> lines)
    {
        Console.Clear(); var scrollOffset = 0;
        var appVersion = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");
        void Render()
        {
            var w = Console.WindowWidth; var h = Console.WindowHeight; var ch = h - 3;
            Console.SetCursorPosition(0, 0); Console.ForegroundColor = ConsoleColor.Green; Console.Write(("DocMan - DOcker Container MANager  " + appVersion).PadRight(w));
            Console.SetCursorPosition(0, 1); Console.ForegroundColor = ConsoleColor.White;
            var si = lines.Count > ch ? $"  [{scrollOffset + 1}-{Math.Min(scrollOffset + ch, lines.Count)}/{lines.Count}]" : "";
            Console.Write($"--- {title} --- ↑↓/PgUp/PgDn/Home/End  │  ESC/Enter to close ---{si}".PadRight(w));
            Console.SetCursorPosition(0, 2); Console.Write(new string('-', w)); Console.ResetColor();
            for (int i = 0; i < ch; i++) { Console.SetCursorPosition(0, i + 3); var idx = scrollOffset + i; if (idx < lines.Count) { var (text, color) = lines[idx]; Console.ForegroundColor = color; var vis = Screen.StripAnsi(text).Length; Console.Write(vis > w ? text[..w] : text); Console.Write(new string(' ', Math.Max(0, w - vis))); } else Console.Write(new string(' ', w)); }
            Console.ResetColor();
        }
        Render();
        while (true)
        {
            if (!Console.KeyAvailable) { await Task.Delay(50); continue; }
            var k = Console.ReadKey(true); if (k.Key is ConsoleKey.Escape or ConsoleKey.Enter) break;
            var ch = Console.WindowHeight - 3;
            switch (k.Key) { case ConsoleKey.UpArrow: if (scrollOffset > 0) scrollOffset--; break; case ConsoleKey.DownArrow: if (scrollOffset < lines.Count - ch) scrollOffset++; break; case ConsoleKey.PageUp: scrollOffset = Math.Max(0, scrollOffset - ch); break; case ConsoleKey.PageDown: scrollOffset = Math.Max(0, Math.Min(scrollOffset + ch, lines.Count - ch)); break; case ConsoleKey.Home: scrollOffset = 0; break; case ConsoleKey.End: scrollOffset = Math.Max(0, lines.Count - ch); break; }
            Render();
        }
        Console.ResetColor(); Console.Clear();
    }

    private static string Truncate(string s, int max) =>
        max <= 0 ? s : s.Length <= max ? s : s[..(max - 1)] + "…";
}
