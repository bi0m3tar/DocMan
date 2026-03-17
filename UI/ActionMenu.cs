using DocMan.Models;
using DocMan.Services;

namespace DocMan.UI;

public class ActionMenu
{
    private readonly DockerService _dockerService;
    private readonly ComposeService _composeService;

    public ActionMenu(DockerService dockerService, ComposeService composeService)
    {
        _dockerService = dockerService;
        _composeService = composeService;
    }

    public async Task<ContainerInfo?> ShowAsync(List<DisplayRow> selectedRows)
    {
        // Check if any project rows are selected (determines if we use compose)
        bool isProjectSelected = selectedRows.Any(r => r.IsProjectRow);
        
        // Extract all containers from selected rows (projects expand to their containers)
        var selectedContainers = new List<ContainerInfo>();
        foreach (var row in selectedRows)
        {
            if (row.IsProjectRow)
            {
                // For project rows, add all containers in the project
                selectedContainers.AddRange(row.ProjectContainers);
            }
            else if (row.Container != null)
            {
                // For individual container rows, add only the specific container
                selectedContainers.Add(row.Container);
            }
        }

        // Deduplicate by container ID
        selectedContainers = selectedContainers
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();

        if (selectedContainers.Count == 0) return null;

        var overlay = new Overlay(6, 80, 22);
        
        bool canRecreate = selectedContainers.Any(c => !c.IsStandalone);
        bool canTerminal = !isProjectSelected && selectedContainers.Count == 1 && selectedContainers[0].IsRunning;

        var menuItems = new List<(string Label, int Action)>
        {
            ("Start",                        0),
            ("Stop",                         1),
            ("Restart",                      2),
        };
        if (canRecreate)
            menuItems.Add(("Recreate (up --force-recreate)", 3));
        menuItems.Add(("Kill",               6));
        menuItems.Add(("Delete",             5));
        if (!isProjectSelected)
        {
            menuItems.Add(("Live Logs",      4));
        }
        menuItems.Add(("Detailed Info",      8));
        if (canTerminal)
            menuItems.Add(("Terminal",       7));

        var selectedMenuItem = 0;

        while (true)
        {
            var menuLines = new List<string>
            {
                "",
                $"Selected: {selectedContainers.Count} container(s)",
                ""
            };

            foreach (var container in selectedContainers.Take(6))
            {
                menuLines.Add($"  - {container.Name}");
            }
            if (selectedContainers.Count > 6)
            {
                menuLines.Add($"  ... and {selectedContainers.Count - 6} more");
            }

            menuLines.Add("");
            
            // Add menu items with arrow indicator
            for (int i = 0; i < menuItems.Count; i++)
            {
                var prefix = i == selectedMenuItem ? " ► " : "   ";
                menuLines.Add($"{prefix}{i + 1}. {menuItems[i].Label}");
            }

            menuLines.Add("");
            menuLines.Add("Use ↑↓ to select, Enter to confirm, C/ESC to cancel");

            overlay.Show("Container Actions", menuLines);

            var key = Console.ReadKey(true);

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selectedMenuItem = selectedMenuItem > 0 ? selectedMenuItem - 1 : menuItems.Count - 1;
                    break;
                case ConsoleKey.DownArrow:
                    selectedMenuItem = selectedMenuItem < menuItems.Count - 1 ? selectedMenuItem + 1 : 0;
                    break;
                case ConsoleKey.Enter:
                    overlay.Hide();
                    if (menuItems[selectedMenuItem].Action == 7)
                        return selectedContainers[0];
                    await ExecuteMenuActionAsync(menuItems[selectedMenuItem].Action, selectedContainers, isProjectSelected);
                    return null;
                case ConsoleKey.C:
                case ConsoleKey.Escape:
                    overlay.Hide();
                    return null;
                default:
                    // Number keys 1–9 map directly to menu item index
                    int numIdx = key.Key switch
                    {
                        ConsoleKey.D1 or ConsoleKey.NumPad1 => 0,
                        ConsoleKey.D2 or ConsoleKey.NumPad2 => 1,
                        ConsoleKey.D3 or ConsoleKey.NumPad3 => 2,
                        ConsoleKey.D4 or ConsoleKey.NumPad4 => 3,
                        ConsoleKey.D5 or ConsoleKey.NumPad5 => 4,
                        ConsoleKey.D6 or ConsoleKey.NumPad6 => 5,
                        ConsoleKey.D7 or ConsoleKey.NumPad7 => 6,
                        _ => -1
                    };
                    if (numIdx >= 0 && numIdx < menuItems.Count)
                    {
                        overlay.Hide();
                        if (menuItems[numIdx].Action == 7)
                            return selectedContainers[0];
                        await ExecuteMenuActionAsync(menuItems[numIdx].Action, selectedContainers, isProjectSelected);
                        return null;
                    }
                    break;
            }
        }
    }

    private async Task ExecuteMenuActionAsync(int menuIndex, List<ContainerInfo> selectedContainers, bool isProjectSelected)
    {
        switch (menuIndex)
        {
            case 0: // Start
                // Use docker-compose ONLY if a project row was selected
                if (isProjectSelected)
                {
                    var projects = selectedContainers.Select(c => c.Project).Distinct().ToList();
                    if (projects.Count == 1 && !string.IsNullOrEmpty(selectedContainers[0].ComposeFile))
                    {
                        // Use docker-compose up for the whole project
                        await _composeService.StartProjectAsync(
                            projects[0], 
                            selectedContainers[0].ComposeFile,
                            selectedContainers[0].WorkingDir);
                    }
                    else
                    {
                        // Fall back to individual starts
                        await ExecuteActionAsync(selectedContainers, "Starting", c => _dockerService.StartContainerAsync(c.Id));
                    }
                }
                else
                {
                    // Individual container rows selected - start them individually
                    await ExecuteActionAsync(selectedContainers, "Starting", c => _dockerService.StartContainerAsync(c.Id));
                }
                break;
            case 1: // Stop
                await ExecuteActionAsync(selectedContainers, "Stopping", c => _dockerService.StopContainerAsync(c.Id));
                break;
            case 2: // Restart
                await ExecuteActionAsync(selectedContainers, "Restarting", c => _dockerService.RestartContainerAsync(c.Id));
                break;
            case 3: // Recreate
                if (isProjectSelected)
                {
                    var projects = selectedContainers.Select(c => c.Project).Distinct().ToList();
                    if (projects.Count == 1 && !string.IsNullOrEmpty(selectedContainers[0].ComposeFile))
                    {
                        await _composeService.StartProjectAsync(
                            projects[0],
                            selectedContainers[0].ComposeFile,
                            selectedContainers[0].WorkingDir,
                            forceRecreate: true);
                    }
                    else
                    {
                        await ExecuteActionAsync(selectedContainers, "Recreating",
                            c => _dockerService.RecreateContainerAsync(c));
                    }
                }
                else
                {
                    await ExecuteActionAsync(selectedContainers, "Recreating",
                        c => _dockerService.RecreateContainerAsync(c));
                }
                break;
            case 4: // Inspect
                if (selectedContainers.Count == 1)
                {
                    var logViewer = new LogViewer(_dockerService);
                    await logViewer.ShowAsync(selectedContainers[0]);
                }
                break;
            case 8: // Info
                if (isProjectSelected)
                {
                    // Show project-level info with compose file content
                    var projectName = selectedContainers.FirstOrDefault()?.Project ?? "";
                    var projectViewer = new ProjectInfoViewer(_dockerService);
                    await projectViewer.ShowAsync(projectName, selectedContainers);
                }
                else if (selectedContainers.Count == 1)
                {
                    var infoViewer = new InfoViewer(_dockerService);
                    await infoViewer.ShowAsync(selectedContainers[0]);
                }
                break;
            case 5: // Delete
                await DeleteContainersAsync(selectedContainers);
                break;
            case 6: // Kill
                await ExecuteActionAsync(selectedContainers, "Killing", c => _dockerService.KillContainerAsync(c.Id));
                break;
        }
    }

    public async Task<ContainerInfo?> ExecuteDirectAsync(int action, List<DisplayRow> selectedRows)
    {
        bool isProjectSelected = selectedRows.Any(r => r.IsProjectRow);
        var selectedContainers = new List<ContainerInfo>();
        foreach (var row in selectedRows)
        {
            if (row.IsProjectRow)
                selectedContainers.AddRange(row.ProjectContainers);
            else if (row.Container != null)
                selectedContainers.Add(row.Container);
        }
        selectedContainers = selectedContainers.GroupBy(c => c.Id).Select(g => g.First()).ToList();
        if (selectedContainers.Count == 0) return null;
        if (action == 7) // Terminal — caller handles it
            return selectedContainers.Count == 1 && selectedContainers[0].IsRunning ? selectedContainers[0] : null;
        await ExecuteMenuActionAsync(action, selectedContainers, isProjectSelected);
        return null;
    }

    private async Task DeleteContainersAsync(List<ContainerInfo> containers)
    {
        var runningContainers = containers.Where(c => c.IsRunning).ToList();
        var overlay = new Overlay(6, 80, 20);
        var statusLines = new List<string>
        {
            "",
            runningContainers.Count > 0
                ? $"Stopping and deleting {containers.Count} container(s)..."
                : $"Deleting {containers.Count} container(s)...",
            "",
            "ESC/Enter to close (operations continue in background)"
        };

        overlay.Show("Delete", statusLines);

        int completed = 0;
        var operationsTask = Task.Run(async () =>
        {
            foreach (var container in runningContainers)
            {
                try
                {
                    statusLines[1] = $"Stopping {runningContainers.Count} container(s)... ({completed}/{runningContainers.Count})";
                    await _dockerService.StopContainerAsync(container.Id);
                    statusLines.Add($"⏹ {container.Service}: stopped");
                    completed++;
                }
                catch (Exception ex)
                {
                    statusLines.Add($"✗ {container.Service}: stop failed - {ex.Message}");
                    completed++;
                }
            }

            completed = 0;
            statusLines[1] = $"Deleting {containers.Count} container(s)...";
            foreach (var container in containers)
            {
                try
                {
                    statusLines[1] = $"Deleting {containers.Count} container(s)... ({completed}/{containers.Count})";
                    await _dockerService.DeleteContainerAsync(container.Id);
                    statusLines.Add($"✓ {container.Service}: deleted");
                    completed++;
                }
                catch (Exception ex)
                {
                    statusLines.Add($"✗ {container.Service}: delete failed - {ex.Message}");
                    completed++;
                }
            }
        });

        while (!operationsTask.IsCompleted)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                {
                    overlay.Hide();
                    _ = operationsTask;
                    return;
                }
            }
            overlay.Update(statusLines.ToList());
            await Task.Delay(100);
        }

        await operationsTask;

        statusLines[1] = $"Completed: {completed}/{containers.Count}";
        statusLines.Add("");
        statusLines.Add("Press any key to close...");
        overlay.Update(statusLines);

        Console.ReadKey(true);
        overlay.Hide();
    }

    private async Task ExecuteActionAsync(List<ContainerInfo> containers, string action, Func<ContainerInfo, Task> operation)
    {
        var overlay = new Overlay(6, 80, 20);
        var statusLines = new List<string>
        {
            "",
            $"{action} {containers.Count} container(s)...",
            "",
            "ESC/Enter to close (operations continue in background)"
        };

        overlay.Show(action, statusLines);

        int completed = 0;
        var operationsTask = Task.Run(async () =>
        {
            foreach (var container in containers)
            {
                try
                {
                    statusLines[1] = $"{action} {containers.Count} container(s)... ({completed}/{containers.Count})";
                    await operation(container);
                    statusLines.Add($"✓ {container.Service}");
                    completed++;
                }
                catch (Exception ex)
                {
                    statusLines.Add($"✗ {container.Service}: {ex.Message}");
                    completed++;
                }
            }
        });

        // Update overlay every 100ms; allow ESC/Enter to close early
        while (!operationsTask.IsCompleted)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Enter)
                {
                    overlay.Hide();
                    _ = operationsTask; // continue in background
                    return;
                }
            }
            overlay.Update(statusLines.ToList());
            await Task.Delay(100);
        }

        await operationsTask; // observe any exceptions

        statusLines[1] = $"Completed: {completed}/{containers.Count}";
        statusLines.Add("");
        statusLines.Add("Press any key to close...");
        overlay.Update(statusLines);

        Console.ReadKey(true);
        overlay.Hide();
    }
}
