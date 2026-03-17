using System.Text.Json;
using DocMan.Models;
using DocMan.Services;

namespace DocMan.UI;

public class ImagesViewer
{
    private readonly DockerService _dockerService;

    public ImagesViewer(DockerService dockerService) => _dockerService = dockerService;

    public async Task<AppPage> ShowAsync(string dockerSummary = "")
    {
        List<ImageInfo>? images    = null;
        string?          loadError = null;
        var alive         = true;
        var needsRender   = true;
        var selectedIndex = 0;
        var markedIds     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filterMode    = 0; // 0=all, 1=in use, 2=unused, 3=dangling

        _ = Task.Run(async () =>
        {
            while (alive)
            {
                try   { images = await _dockerService.GetImagesAsync(); loadError = null; }
                catch (Exception ex) { loadError = ex.Message; }
                await Task.Delay(5000);
            }
        });

        List<ImageInfo>? lastImages = null;
        string?          lastError  = null;

        while (true)
        {
            if (!ReferenceEquals(images, lastImages) || loadError != lastError)
            {
                lastImages  = images;
                lastError   = loadError;
                needsRender = true;
            }

            if (needsRender)
            {
                var visible = GetVisible(images, filterMode);
                selectedIndex = visible == null ? 0 : Math.Clamp(selectedIndex, 0, Math.Max(0, visible.Count - 1));
                Render(visible, selectedIndex, markedIds, loadError, dockerSummary, filterMode);
                needsRender = false;
            }

            if (Console.KeyAvailable)
            {
                var key     = Console.ReadKey(true);
                var visible = GetVisible(images, filterMode);
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

                    case ConsoleKey.I:
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        {
                            if (visible != null && count > 0)
                            { await ShowDetail(visible[selectedIndex]); needsRender = true; }
                        }
                        else { alive = false; Console.ResetColor(); Console.Clear(); return AppPage.Images; }
                        break;

                    case ConsoleKey.RightArrow:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Volumes;

                    case ConsoleKey.LeftArrow:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Networks;

                    case ConsoleKey.N:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Networks;

                    case ConsoleKey.V:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Volumes;

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
                            var id = visible[selectedIndex].Id;
                            if (!markedIds.Remove(id)) markedIds.Add(id);
                            needsRender = true;
                        }
                        break;

                    case ConsoleKey.T:
                        filterMode    = (filterMode + 1) % 4;
                        selectedIndex = 0;
                        needsRender   = true;
                        break;

                    case ConsoleKey.D:
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Shift) && visible != null && count > 0)
                        {
                            await ConfirmDeleteOne(visible[selectedIndex]);
                            images      = null;
                            needsRender = true;
                        }
                        break;

                    case ConsoleKey.P: // Delete/Prune marked
                        if (markedIds.Count > 0 && images != null)
                            await PruneMarked(images, markedIds);
                        else if (visible != null && count > 0)
                            await ShowActionMenu(visible[selectedIndex]);
                        images      = null;
                        needsRender = true;
                        break;

                    case ConsoleKey.X: // Prune all unused/dangling
                        await PruneAll();
                        images      = null;
                        needsRender = true;
                        break;

                    case ConsoleKey.Enter:
                        if (visible != null && count > 0)
                        {
                            await ShowActionMenu(visible[selectedIndex]);
                            images      = null;
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

    private static List<ImageInfo>? GetVisible(List<ImageInfo>? all, int mode) =>
        all == null ? null :
        mode == 1 ? all.Where(i => i.InUse && i.Repository != "<none>").ToList() :
        mode == 2 ? all.Where(i => !i.InUse && i.Repository != "<none>").ToList() :
        mode == 3 ? all.Where(i => i.Repository == "<none>").ToList() :
        all.ToList();

    private async Task ShowActionMenu(ImageInfo img)
    {
        var label   = img.Repository == "<none>" ? $"<none>  {img.Id}" : $"{img.Repository}:{img.Tag}";
        var options = new[] { "Detailed Info", img.InUse ? "Delete  ⚠ in use!" : "Delete" };
        var ov      = new Overlay(5, 56, 11);
        var sel     = 0;

        void Draw()
        {
            var lines = new List<string> { "", $"  {Truncate(label, 50)}", "" };
            for (int i = 0; i < options.Length; i++)
                lines.Add(i == sel ? $"  > {i + 1}. {options[i]}" : $"    {i + 1}. {options[i]}");
            lines.Add(""); lines.Add("  ↑↓/1/2:Select  ENTER:Confirm  ESC:Cancel");
            ov.Update(lines);
        }

        ov.Show("Image Actions", new List<string>());
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
            if (sel == 0) await ShowDetail(img);
            else          await ConfirmDeleteOne(img);
            return;
        }
    }

    private async Task PruneMarked(List<ImageInfo> all, HashSet<string> ids)
    {
        var targets = all.Where(i => ids.Contains(i.Id)).ToList();
        var ov      = new Overlay(5, 62, Math.Min(targets.Count + 8, 24));
        var lines   = new List<string> { "", $"  Deleting {targets.Count} image(s)...", "" };
        ov.Show("Delete Images", lines);
        foreach (var img in targets)
        {
            var label     = img.Repository == "<none>" ? img.Id : $"{img.Repository}:{img.Tag}";
            var (ok, err) = await _dockerService.DeleteImageAsync(img.Id);
            lines.Add(ok ? $"  ✓ {Truncate(label, 54)}" : $"  ✗ {Truncate(label, 30)}: {Truncate(err, 22)}");
            ov.Update(lines);
        }
        ids.Clear();
        lines.Add(""); lines.Add("  Press any key...");
        ov.Update(lines); Console.ReadKey(true); ov.Hide();
        Console.ResetColor(); Console.Clear();
    }

    private async Task PruneAll()
    {
        var ov    = new Overlay(6, 70, 10);
        var lines = new List<string> { "", "  Pruning all unused Docker images...", "", "  ESC/Enter to close (continues in background)" };
        ov.Show("Prune Images", lines);
        var task = _dockerService.PruneImagesAsync();
        while (!task.IsCompleted)
        {
            if (Console.KeyAvailable) { var k = Console.ReadKey(true); if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.Enter) { ov.Hide(); return; } }
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

    private async Task ShowDetail(ImageInfo img)
    {
        var json  = await _dockerService.GetImageDetailJsonAsync(img.Id);
        var lines = BuildDetailLines(img, json);
        await ShowScrollable($"IMAGE: {img.Repository}:{img.Tag}", lines);
    }

    private async Task ConfirmDeleteOne(ImageInfo img)
    {
        var label = img.Repository == "<none>" ? img.Id : $"{img.Repository}:{img.Tag}";
        var ov    = new Overlay(6, 62, 7);
        ov.Show("Delete Image", new List<string>
        { "", $"  Delete: {Truncate(label, 50)}", "", "  Y to confirm, any other key to cancel" });
        var k = Console.ReadKey(true); ov.Hide();
        if (k.Key != ConsoleKey.Y) { Console.ResetColor(); Console.Clear(); return; }

        var (ok, err) = await _dockerService.DeleteImageAsync(img.Id);
        var res = new Overlay(6, 62, 7);
        res.Show("Delete Image", new List<string>
        { "", ok ? $"  ✓ Deleted {Truncate(label, 50)}" : $"  ✗ Failed: {Truncate(err, 50)}", "", "  Press any key..." });
        Console.ReadKey(true); res.Hide();
        Console.ResetColor(); Console.Clear();
    }

    private static List<(string text, ConsoleColor color)> BuildDetailLines(ImageInfo img, string json)
    {
        var lines = new List<(string text, ConsoleColor color)>();
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(($"  Repository : {img.Repository}", ConsoleColor.White));
        lines.Add(($"  Tag        : {img.Tag}",        ConsoleColor.White));
        lines.Add(($"  ID         : {img.Id}",         ConsoleColor.Gray));
        lines.Add(($"  Size       : {img.Size}",       ConsoleColor.White));
        lines.Add(($"  Created    : {img.Created}",    ConsoleColor.Gray));
        lines.Add(($"  In Use     : {(img.InUse ? "yes" : "no ⚠")}", img.InUse ? ConsoleColor.White : ConsoleColor.Yellow));
        lines.Add(("", ConsoleColor.Gray));
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;
            if (el.TryGetProperty("Config", out var cfg))
            {
                if (cfg.TryGetProperty("Entrypoint", out var ep) && ep.ValueKind == JsonValueKind.Array)
                { var parts = ep.EnumerateArray().Select(x => x.GetString() ?? "").ToList(); if (parts.Count > 0) lines.Add(($"  Entrypoint : {string.Join(" ", parts)}", ConsoleColor.White)); }
                if (cfg.TryGetProperty("Cmd", out var cmd) && cmd.ValueKind == JsonValueKind.Array)
                { var parts = cmd.EnumerateArray().Select(x => x.GetString() ?? "").ToList(); if (parts.Count > 0) lines.Add(($"  Cmd        : {string.Join(" ", parts)}", ConsoleColor.White)); }
                if (cfg.TryGetProperty("WorkingDir", out var wd) && !string.IsNullOrEmpty(wd.GetString()))
                    lines.Add(($"  WorkingDir : {wd.GetString()}", ConsoleColor.White));
                if (cfg.TryGetProperty("ExposedPorts", out var ports) && ports.ValueKind == JsonValueKind.Object && ports.EnumerateObject().Any())
                {
                    lines.Add(("", ConsoleColor.Gray)); lines.Add(("  ── Exposed Ports ──────────────────────", ConsoleColor.Cyan));
                    foreach (var p in ports.EnumerateObject()) lines.Add(($"    {p.Name}", ConsoleColor.White));
                }
                if (cfg.TryGetProperty("Env", out var env) && env.ValueKind == JsonValueKind.Array)
                {
                    lines.Add(("", ConsoleColor.Gray)); lines.Add(("  ── Environment ────────────────────────", ConsoleColor.Cyan));
                    foreach (var e in env.EnumerateArray()) lines.Add(($"    {e.GetString()}", ConsoleColor.Gray));
                }
            }
            if (el.TryGetProperty("RootFS", out var rootfs) && rootfs.TryGetProperty("Layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
            {
                lines.Add(("", ConsoleColor.Gray)); lines.Add(($"  ── Layers ({layers.GetArrayLength()}) ────────────────────────", ConsoleColor.Cyan));
                foreach (var layer in layers.EnumerateArray())
                { var s = layer.GetString() ?? ""; lines.Add(($"    {(s.StartsWith("sha256:") ? s[7..19] : s[..Math.Min(12, s.Length)])}", ConsoleColor.DarkGray)); }
            }
        }
        catch { }
        return lines;
    }

    private void Render(List<ImageInfo>? images, int selectedIndex, HashSet<string> markedIds,
                        string? error, string dockerSummary, int filterMode)
    {
        var width  = Console.WindowWidth;
        var height = Console.WindowHeight;

        IList<(string text, ConsoleColor color)>? titleFlags =
            filterMode == 1 ? new[] { ("  [IN USE]",   ConsoleColor.Green) } :
            filterMode == 2 ? new[] { ("  [UNUSED]",   ConsoleColor.Yellow) } :
            filterMode == 3 ? new[] { ("  [DANGLING]", ConsoleColor.Red) } : null;
        AppNav.RenderTitleBar(dockerSummary, width, titleFlags);
        AppNav.RenderGlobalNav(AppPage.Images, width);

        // Row 2: page-specific
        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"↑↓:Navigate     │  SPACE:Mark  ENTER:Actions  │  P:Delete Marked  X:Prune  T:Toggle Status".PadRight(width));
        Console.ResetColor();

        // Rows 3-5: headers
        Console.SetCursorPosition(0, 3);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('-', 184));
        Console.SetCursorPosition(0, 4);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(string.Format("{0,-3} {1,-14} {2,-64} {3,-16} {4,-10} {5,-10} {6}",
            " M", "ID", "REPOSITORY", "TAG", "SIZE", "STATUS", "CREATED").PadRight(184));
        Console.SetCursorPosition(0, 5);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('-', 184));
        Console.ResetColor();

        if (error != null)
        { Console.SetCursorPosition(0, 6); Console.ForegroundColor = ConsoleColor.Red; Console.Write($"  Error: {error}".PadRight(width)); return; }
        if (images == null)
        { Console.SetCursorPosition(0, 6); Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("  Loading...".PadRight(width)); return; }

        var dataRow     = 6;
        var contentRows = height - 2 - dataRow;
        for (int i = 0; i < images.Count && i < contentRows; i++, dataRow++)
        {
            var img      = images[i];
            var dangling = img.Repository == "<none>";
            var mIcon    = markedIds.Contains(img.Id) ? "[x]" : "[ ]";
            var uIcon    = " ";
            var status   = dangling ? "dangling" : img.InUse ? "in use" : "unused";
            var fg       = dangling    ? ConsoleColor.Red
                         : img.InUse  ? ConsoleColor.Green
                         :              ConsoleColor.Yellow;

            Console.SetCursorPosition(0, dataRow);
            if (i == selectedIndex) { Console.BackgroundColor = ConsoleColor.DarkCyan; Console.ForegroundColor = ConsoleColor.White; }
            else { Console.ResetColor(); Console.ForegroundColor = markedIds.Contains(img.Id) ? ConsoleColor.Cyan : fg; }

            Console.Write(string.Format("{0,-3} {1,-14} {2,-64} {3,-16} {4,-10} {5,-10} {6}",
                mIcon, img.Id,
                Truncate(img.Repository, 64), Truncate(img.Tag, 16), img.Size, status, img.Created).PadRight(width));
        }

        Console.ResetColor();
        for (int row = dataRow; row < height - 1; row++)
        { Console.SetCursorPosition(0, row); Console.Write(new string(' ', width)); }

        Console.SetCursorPosition(0, height - 1);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        var unusedCount   = images.Count(i => !i.InUse && i.Repository != "<none>");
        var danglingCount = images.Count(i => i.Repository == "<none>");
        var footer        = $"  {images.Count} image(s)";
        if (markedIds.Count > 0) footer += $"  │  {markedIds.Count} marked";
        if (danglingCount > 0) footer += $"  │  {danglingCount} dangling";
        if (unusedCount   > 0) footer += $"  │  {unusedCount} unused";
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
