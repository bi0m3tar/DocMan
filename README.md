# DocMan — DOcker Container MANager

A lightweight, keyboard-driven terminal UI for managing Docker containers running in WSL. No Docker Desktop required.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4) ![Platform](https://img.shields.io/badge/platform-Windows-0078D4) ![License](https://img.shields.io/badge/license-MIT-green)

---

## Requirements

- Windows 10 / 11
- [WSL](https://aka.ms/wsl) with a Linux distro (Ubuntu recommended)
- Docker Engine installed **inside WSL** (not Docker Desktop)

```bash
# Install Docker Engine in WSL
curl -fsSL https://get.docker.com | sudo sh
```

DocMan will check for WSL and Docker at startup and print a clear error message if either is missing.

---

## Installation

### Run from source

```powershell
git clone https://github.com/youruser/docman.git
cd docman
dotnet run
```

### Build a self-contained single-file executable

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\DocMan.exe
```

### Optional: adjust the refresh interval (default 5 seconds)

```powershell
.\DocMan.exe -Interval 3
```

---

## Features

- **Container list** grouped by Docker Compose project, with standalone containers listed separately
- **Multi-select** — mark individual containers or whole projects with `Space`
- **Batch operations** — start, stop, or delete all marked items at once
- **Global actions** — start all / stop all / delete all without selecting anything
- **Live log tail** — real-time log panel pinned to the bottom of the screen (`I`)
- **Container Info** — detailed view with image, volumes, network settings, CPU, memory, net/disk I/O, and PID count (auto-refreshes every 2 s)
- **Log inspector** — last 20 lines of container logs
- **Terminal access** — open an interactive shell inside any running container
- **Docker Engine update** — upgrade docker-ce in WSL without leaving the app (`U`)
- **WSL restart** — shut down and restart WSL (`W`)
- **Version bar** — shows installed docker-ce version vs latest available, highlighted in red when an update is ready
- **Resource bar** — total CPU %, memory usage, and core count across all running containers
- **Startup checks** — validates WSL is installed, can start, and that Docker is available before the UI loads

---

## Keyboard Controls

### Main screen

| Key | Action |
|-----|--------|
| `↑` / `↓` | Navigate the container list |
| `Space` | Mark / unmark container or project (marking a project marks all its services) |
| `Enter` | Open action menu for selected / marked items |
| `P` | Start all stopped containers |
| `S` | Stop all running containers |
| `D` | Delete all containers (with confirmation) |
| `I` | Toggle live log tail panel |
| `R` | Toggle running-only filter |
| `U` | Update Docker Engine in WSL (apt upgrade) |
| `W` | Restart WSL |
| `Q` | Quit |

### Action menu (Enter)

| Option | Description |
|--------|-------------|
| Start | Start selected container(s) or compose project |
| Stop | Stop selected container(s) |
| Restart | Restart selected container(s) |
| Recreate | `docker compose up --force-recreate` (compose containers only) |
| Delete | Remove selected container(s) |
| Inspect | View last 20 lines of logs |
| Info | Detailed container info + live resource stats |
| Terminal | Open interactive shell (running containers only) |

Press `C` or `Esc` to dismiss the menu without taking action.

---

## Display

```
DocMan - DOcker Container MANager  v1.0.7
↑↓:Navigate │ SPACE:Mark │ ENTER:Container Actions
P:Start All │ S:Stop All │ D:Delete All │ I:Inspect │ R:Toggle Running │ U:Update Docker │ W:Restart WSL/Docker │ Q:Quit
──────────────────────────────────────────────────────────────────────────────────────────────────────
 M   NAME                                      ID             IMAGE            PORTS          STATUS
──────────────────────────────────────────────────────────────────────────────────────────────────────
[ ]  myapi                                                                                            
[ ]    db                                      a1b2c3d4e5f6   postgres         5432→5432      Up 2 hours
[ ]    web                                     f6e5d4c3b2a1   nginx            8080→80        Up 2 hours
[ ]  standalone-redis                          1122334455aa   redis            6379→6379      Exited 5 min ago
```

- **Project rows** (cyan) represent a Docker Compose project; selecting one applies actions to all its services
- **Service rows** are indented under their project
- **Standalone containers** (not part of a compose project) appear at the top in yellow
- **Status colours**: green = running, yellow = restarting, red = stopped/exited

---

## Project Structure

```
DocMan/
├── Models/
│   ├── ContainerInfo.cs        # Lightweight container model used in the main list
│   ├── ContainerDetail.cs      # Detailed model used by the Info viewer
│   ├── ContainerGroup.cs
│   └── DisplayRow.cs
├── Services/
│   ├── DockerService.cs        # All docker / WSL process calls
│   └── ComposeService.cs       # docker-compose operations and WSL restart
├── UI/
│   ├── Screen.cs               # Console helpers + VT processing + scroll region
│   ├── ContainerListView.cs    # Main list renderer
│   ├── ActionMenu.cs           # Per-container action overlay
│   ├── InfoViewer.cs           # Detailed container info + live stats
│   ├── LogViewer.cs            # Log inspector
│   ├── Overlay.cs              # Generic overlay box
│   └── (terminal in Program.cs)
├── Utilities/
│   └── ContainerNameParser.cs
├── Program.cs                  # Main loop, input handling, terminal access
└── run.ps1                     # Quick launcher
```

---

## Why not Docker Desktop?

Docker Desktop is a 700 MB+ GUI application. DocMan is a ~3 MB self-contained executable that talks directly to Docker Engine in WSL. It starts instantly, uses ~18 MB RAM, and stays out of the way.

---

## License

MIT

