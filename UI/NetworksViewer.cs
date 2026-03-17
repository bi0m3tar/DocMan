using System.Text.Json;
using DocMan.Models;
using DocMan.Services;

namespace DocMan.UI;

public class NetworksViewer
{
    private static readonly HashSet<string> SystemNetworks = new(StringComparer.OrdinalIgnoreCase) { "bridge", "host", "none" };
    private readonly DockerService _dockerService;

    public NetworksViewer(DockerService dockerService) => _dockerService = dockerService;

    public async Task<AppPage> ShowAsync(string dockerSummary = "")
    {
        List<DockerNetworkInfo>? networks  = null;
        string?                  loadError = null;
        var alive         = true;
        var needsRender   = true;
        var selectedIndex = 0;
        var markedIds     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filterMode = 0; // 0=all, 1=in use, 2=unused

        _ = Task.Run(async () =>
        {
            while (alive)
            {
                try   { networks = await _dockerService.GetNetworksAsync(); loadError = null; }
                catch (Exception ex) { loadError = ex.Message; }
                await Task.Delay(5000);
            }
        });

        List<DockerNetworkInfo>? lastNetworks = null;
        string?                  lastError    = null;

        while (true)
        {
            if (!ReferenceEquals(networks, lastNetworks) || loadError != lastError)
            {
                lastNetworks = networks;
                lastError    = loadError;
                needsRender  = true;
            }

            if (needsRender)
            {
                var visible = GetVisible(networks, filterMode);
                selectedIndex = visible == null ? 0 : Math.Clamp(selectedIndex, 0, Math.Max(0, visible.Count - 1));
                Render(visible, selectedIndex, markedIds, loadError, dockerSummary, filterMode);
                needsRender = false;
            }

            if (Console.KeyAvailable)
            {
                var key     = Console.ReadKey(true);
                var visible = GetVisible(networks, filterMode);
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

                    case ConsoleKey.N:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Networks; // re-enter same page (caller handles)

                    case ConsoleKey.RightArrow:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Images;

                    case ConsoleKey.LeftArrow:
                        alive = false; Console.ResetColor(); Console.Clear();
                        return AppPage.Containers;

                    case ConsoleKey.I:
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        {
                            if (visible != null && count > 0)
                            { await ShowDetail(visible[selectedIndex]); needsRender = true; }
                        }
                        else { alive = false; Console.ResetColor(); Console.Clear(); return AppPage.Images; }
                        break;

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
                        filterMode    = (filterMode + 1) % 3; // cycle: in use(1) → unused(2) → all(0)
                        selectedIndex = 0;
                        needsRender   = true;
                        break;

                    case ConsoleKey.D:
                        if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                        {
                            if (visible != null && count > 0)
                                await ConfirmDeleteOne(visible[selectedIndex]);
                            networks    = null;
                            needsRender = true;
                        }
                        else if (markedIds.Count > 0 && networks != null)
                        {
                            await DeleteMarked(networks, markedIds);
                            networks    = null;
                            needsRender = true;
                        }
                        else if (visible != null && count > 0)
                        {
                            await ShowActionMenu(visible[selectedIndex]);
                            networks    = null;
                            needsRender = true;
                        }
                        break;

                    case ConsoleKey.X:
                        await PruneUnused();
                        networks    = null;
                        needsRender = true;
                        break;

                    case ConsoleKey.Enter:
                        if (visible != null && count > 0)
                        {
                            await ShowActionMenu(visible[selectedIndex]);
                            networks    = null;
                            needsRender = true;
                        }
                        break;

                    case ConsoleKey.U: case ConsoleKey.W:
                        // Global actions — handled by Program.cs when back on container page; ignore here
                        break;
                }
            }

            await Task.Delay(50);
        }
    }

    private static List<DockerNetworkInfo>? GetVisible(List<DockerNetworkInfo>? all, int mode) =>
        all == null ? null :
        mode == 1 ? all.Where(n => n.ContainerCount > 0).OrderBy(n => n.Name).ToList() :
        mode == 2 ? all.Where(n => !SystemNetworks.Contains(n.Name) && n.ContainerCount == 0).OrderBy(n => n.Name).ToList() :
        all.OrderBy(n => n.Name).ToList();

    private async Task ShowActionMenu(DockerNetworkInfo net)
    {
        var inUse   = net.ContainerCount > 0;
        var options = new[] { "Detailed Info", inUse ? "Delete  ⚠ in use!" : "Delete" };
        var ov      = new Overlay(5, 54, 11);
        var sel     = 0;

        void Draw()
        {
            var lines = new List<string> { "", $"  {Truncate(net.Name, 46)}", "" };
            for (int i = 0; i < options.Length; i++)
                lines.Add(i == sel ? $"  > {i + 1}. {options[i]}" : $"    {i + 1}. {options[i]}");
            lines.Add(""); lines.Add("  ↑↓/1/2:Select  ENTER:Confirm  ESC:Cancel");
            ov.Update(lines);
        }

        ov.Show("Network Actions", new List<string>());
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
            if (sel == 0) await ShowDetail(net);
            else          await ConfirmDeleteOne(net);
            return;
        }
    }

    private async Task DeleteMarked(List<DockerNetworkInfo> all, HashSet<string> ids)
    {
        var targets = all.Where(n => ids.Contains(n.Id)).ToList();
        var ov      = new Overlay(5, 60, Math.Min(targets.Count + 8, 24));
        var lines   = new List<string> { "", $"  Deleting {targets.Count} network(s)...", "" };
        ov.Show("Delete Networks", lines);
        foreach (var n in targets)
        {
            var (ok, err) = await _dockerService.DeleteNetworkAsync(n.Id);
            lines.Add(ok ? $"  ✓ {n.Name}" : $"  ✗ {n.Name}: {Truncate(err, 46)}");
            ov.Update(lines);
        }
        ids.Clear();
        lines.Add(""); lines.Add("  Press any key...");
        ov.Update(lines); Console.ReadKey(true); ov.Hide();
        Console.ResetColor(); Console.Clear();
    }

    private async Task ShowDetail(DockerNetworkInfo net)
    {
        var json  = await _dockerService.GetNetworkDetailJsonAsync(net.Id);
        var lines = BuildDetailLines(net, json);
        await ShowScrollable($"NETWORK: {net.Name}", lines);
    }

    private async Task ConfirmDeleteOne(DockerNetworkInfo net)
    {
        var ov = new Overlay(6, 60, 7);
        ov.Show("Delete Network", new List<string>
        { "", $"  Delete: {Truncate(net.Name, 46)}", "", "  Y to confirm, any other key to cancel" });
        var k = Console.ReadKey(true); ov.Hide();
        if (k.Key != ConsoleKey.Y) { Console.ResetColor(); Console.Clear(); return; }

        var (ok, err) = await _dockerService.DeleteNetworkAsync(net.Id);
        var res = new Overlay(6, 62, 7);
        res.Show("Delete Network", new List<string>
        { "", ok ? $"  ✓ Deleted {net.Name}" : $"  ✗ Failed: {Truncate(err, 50)}", "", "  Press any key..." });
        Console.ReadKey(true); res.Hide();
        Console.ResetColor(); Console.Clear();
    }

    private static List<(string text, ConsoleColor color)> BuildDetailLines(DockerNetworkInfo net, string json)
    {
        var lines = new List<(string text, ConsoleColor color)>();
        lines.Add(("", ConsoleColor.Gray));
        lines.Add(($"  Name       : {net.Name}",   ConsoleColor.White));
        lines.Add(($"  ID         : {net.Id}",     ConsoleColor.Gray));
        lines.Add(($"  Driver     : {net.Driver}", ConsoleColor.White));
        lines.Add(($"  Scope      : {net.Scope}",  ConsoleColor.White));
        lines.Add(($"  Internal   : {(net.Internal ? "yes" : "no")}", ConsoleColor.White));
        lines.Add(($"  Subnet     : {(net.Subnet  == "" ? "-" : net.Subnet)}",  ConsoleColor.White));
        lines.Add(($"  Gateway    : {(net.Gateway == "" ? "-" : net.Gateway)}", ConsoleColor.White));
        lines.Add(($"  Containers : {net.ContainerCount}", net.ContainerCount == 0 ? ConsoleColor.DarkGray : ConsoleColor.White));
        lines.Add(($"  Created    : {net.Created}", ConsoleColor.Gray));
        lines.Add(("", ConsoleColor.Gray));
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;
            if (el.TryGetProperty("Containers", out var ctrs) && ctrs.ValueKind == JsonValueKind.Object && ctrs.EnumerateObject().Any())
            {
                lines.Add(("  ── Attached Containers ────────────────────", ConsoleColor.Cyan));
                foreach (var c in ctrs.EnumerateObject())
                {
                    var cname = c.Value.TryGetProperty("Name",        out var cn)  ? cn.GetString()  ?? c.Name[..12] : c.Name[..12];
                    var ip    = c.Value.TryGetProperty("IPv4Address", out var ip4) ? ip4.GetString() ?? "" : "";
                    lines.Add(($"    {cname,-32} {ip}", ConsoleColor.White));
                }
                lines.Add(("", ConsoleColor.Gray));
            }
            if (el.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object && labels.EnumerateObject().Any())
            {
                lines.Add(("  ── Labels ─────────────────────────────────", ConsoleColor.Cyan));
                foreach (var lbl in labels.EnumerateObject())
                    lines.Add(($"    {lbl.Name} = {lbl.Value.GetString()}", ConsoleColor.Gray));
            }
        }
        catch { }
        return lines;
    }

    private async Task PruneUnused()
    {
        var ov    = new Overlay(6, 70, 10);
        var lines = new List<string> { "", "  Pruning all unused Docker networks...", "", "  ESC/Enter to close (continues in background)" };
        ov.Show("Prune Networks", lines);
        var task = _dockerService.PruneNetworksAsync();
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

    private void Render(List<DockerNetworkInfo>? networks, int selectedIndex, HashSet<string> markedIds,
                        string? error, string dockerSummary, int filterMode)
    {
        var width  = Console.WindowWidth;
        var height = Console.WindowHeight;

        IList<(string text, ConsoleColor color)>? titleFlags =
            filterMode == 1 ? new[] { ("  [IN USE]",  ConsoleColor.Green) } :
            filterMode == 2 ? new[] { ("  [UNUSED]",  ConsoleColor.Yellow) } : null;
        AppNav.RenderTitleBar(dockerSummary, width, titleFlags);
        AppNav.RenderGlobalNav(AppPage.Networks, width);

        Console.SetCursorPosition(0, 2);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"↑↓:Navigate     │  SPACE:Mark  ENTER:Actions  │  D:Delete Marked  X:Prune  T:Toggle Status".PadRight(width));
        Console.ResetColor();

        // Row 3: column headers
        Console.SetCursorPosition(0, 3);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('-', 184));
        Console.SetCursorPosition(0, 4);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(string.Format("{0,-3} {1,-60} {2,-10} {3,-10} {4,-20} {5,-5} {6}",
            " M", "NAME", "STATUS", "DRIVER", "SUBNET", "CTRS", "CREATED").PadRight(184));
        Console.SetCursorPosition(0, 5);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(new string('-', 184));
        Console.ResetColor();

        if (error != null)
        { Console.SetCursorPosition(0, 6); Console.ForegroundColor = ConsoleColor.Red; Console.Write($"  Error: {error}".PadRight(width)); return; }

        if (networks == null)
        { Console.SetCursorPosition(0, 6); Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("  Loading...".PadRight(width)); return; }

        var dataRow     = 6;
        var contentRows = height - 2 - dataRow;
        for (int i = 0; i < networks.Count && i < contentRows; i++, dataRow++)
        {
            var n        = networks[i];
            var isSystem = SystemNetworks.Contains(n.Name);
            var unused   = !isSystem && n.ContainerCount == 0;
            var marked   = markedIds.Contains(n.Id);
            var mIcon    = marked ? "[x]" : "[ ]";
            var uIcon    = " ";
            var status   = isSystem ? "system" : n.ContainerCount > 0 ? "in use" : "unused";
            var fg       = n.ContainerCount > 0 ? ConsoleColor.Green
                         : isSystem             ? ConsoleColor.DarkGray
                         :                        ConsoleColor.Red;

            Console.SetCursorPosition(0, dataRow);
            if (i == selectedIndex) { Console.BackgroundColor = ConsoleColor.DarkCyan; Console.ForegroundColor = ConsoleColor.White; }
            else                    { Console.ResetColor(); Console.ForegroundColor = marked ? ConsoleColor.Cyan : fg; }

            Console.Write(string.Format("{0,-3} {1,-60} {2,-10} {3,-10} {4,-20} {5,-5} {6}",
                mIcon,
                Truncate(n.Name, 60), status, Truncate(n.Driver, 10),
                Truncate(n.Subnet == "" ? "-" : n.Subnet, 20),
                n.ContainerCount, n.Created).PadRight(width));
        }

        Console.ResetColor();
        for (int row = dataRow; row < height - 1; row++)
        { Console.SetCursorPosition(0, row); Console.Write(new string(' ', width)); }

        Console.SetCursorPosition(0, height - 1);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        var unusedCount = networks.Count(n => !SystemNetworks.Contains(n.Name) && n.ContainerCount == 0);
        var footer = $"  {networks.Count} network(s)";
        if (markedIds.Count > 0) footer += $"  │  {markedIds.Count} marked";
        if (unusedCount > 0) footer += $"  │  {unusedCount} unused";
        footer += "  │  auto-refreshes every 5s";
        Console.Write(footer.PadRight(width));
        Console.ResetColor();
    }

    private static async Task ShowScrollable(string title, List<(string text, ConsoleColor color)> lines)
    {
        Console.Clear();
        var scrollOffset = 0;
        var appVersion   = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");

        void Render()
        {
            var w = Console.WindowWidth; var h = Console.WindowHeight; var ch = h - 3;
            Console.SetCursorPosition(0, 0); Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(("DocMan - DOcker Container MANager  " + appVersion).PadRight(w));
            Console.SetCursorPosition(0, 1); Console.ForegroundColor = ConsoleColor.White;
            var si = lines.Count > ch ? $"  [{scrollOffset + 1}-{Math.Min(scrollOffset + ch, lines.Count)}/{lines.Count}]" : "";
            Console.Write($"--- {title} --- ↑↓/PgUp/PgDn/Home/End  │  ESC/Enter to close ---{si}".PadRight(w));
            Console.SetCursorPosition(0, 2); Console.Write(new string('-', w)); Console.ResetColor();
            for (int i = 0; i < ch; i++)
            {
                Console.SetCursorPosition(0, i + 3);
                var idx = scrollOffset + i;
                if (idx < lines.Count) { var (text, color) = lines[idx]; Console.ForegroundColor = color; var vis = Screen.StripAnsi(text).Length; Console.Write(vis > w ? text[..w] : text); Console.Write(new string(' ', Math.Max(0, w - vis))); }
                else Console.Write(new string(' ', w));
            }
            Console.ResetColor();
        }
        Render();
        while (true)
        {
            if (!Console.KeyAvailable) { await Task.Delay(50); continue; }
            var k = Console.ReadKey(true);
            if (k.Key is ConsoleKey.Escape or ConsoleKey.Enter) break;
            var ch = Console.WindowHeight - 3;
            switch (k.Key)
            {
                case ConsoleKey.UpArrow:   if (scrollOffset > 0) scrollOffset--; break;
                case ConsoleKey.DownArrow: if (scrollOffset < lines.Count - ch) scrollOffset++; break;
                case ConsoleKey.PageUp:    scrollOffset = Math.Max(0, scrollOffset - ch); break;
                case ConsoleKey.PageDown:  scrollOffset = Math.Max(0, Math.Min(scrollOffset + ch, lines.Count - ch)); break;
                case ConsoleKey.Home:      scrollOffset = 0; break;
                case ConsoleKey.End:       scrollOffset = Math.Max(0, lines.Count - ch); break;
            }
            Render();
        }
        Console.ResetColor(); Console.Clear();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
