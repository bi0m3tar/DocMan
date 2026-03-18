# DocMan — DOcker Container MANager

A lightweight, keyboard-driven terminal UI (TUI) for managing Docker containers, networks, images and volumes. Runs on Windows (via WSL) and Linux — no Docker Desktop required.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4) ![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-0078D4) ![License](https://img.shields.io/badge/license-MIT-green)

---

## Dependencies

DocMan has **zero external (NuGet) dependencies**. It is built entirely on the .NET 10.0 base class library:

- `System.Console` — terminal rendering and keyboard input
- `System.Diagnostics.Process` — spawning `docker` / `wsl` subprocesses
- `System.Threading.Tasks` — async background refresh tasks

No third-party packages are required.

## Requirements

### Windows
- Windows 10 / 11
- [WSL](https://aka.ms/wsl) with a Linux distro (Ubuntu recommended)
- Docker Engine installed **inside WSL** (not Docker Desktop)

```bash
# Install Docker Engine in WSL (if not already installed)
curl -fsSL https://get.docker.com | sudo sh
```

### Linux
- Any Linux distro with Docker Engine installed
- `sudo` access (required for daemon auto-start and docker updates)

```bash
# Install Docker Engine
curl -fsSL https://get.docker.com | sudo sh
```

DocMan checks for Docker at startup and prints a clear error if it is missing.

---

## Installation

### Download a release

Download the pre-built binary for your platform from the [GitHub Releases](https://github.com/bi0m3tar/DocMan/releases) page:

- **Windows** — `docman.exe` (no .NET install required)
- **Linux** — `docman` (no .NET install required)

```bash
# Linux: make it executable and run
chmod +x docman
./docman
```

### Run from source

```powershell
git clone https://github.com/bi0m3tar/DocMan.git
cd DocMan
dotnet run
```

### Build a self-contained single-file executable

```powershell
# Windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o publish\release

# Linux
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o publish/linux
```

---

## Features

- **Multi-page UI** — Containers, Networks, Images and Volumes pages, switchable with `←`/`→` or `N`/`I`/`V`/`C`
- **Container list** grouped by Docker Compose project, with standalone containers listed separately
- **Multi-select** — mark individual items or whole projects with `Space`
- **Batch operations** — start, stop, or delete all marked items at once
- **Live log tail** — real-time log panel pinned to the bottom of the screen (`L`)
- **Fullscreen live logs** — fullscreen streaming log view (`Shift+L` or via action menu)
- **Container / Project Info** — scrollable detailed view with image, volumes, network settings, resource usage, environment variables; project rows show compose file content with YAML highlighting
- **Networks viewer** — list, filter (in use / unused), inspect, delete, and prune Docker networks
- **Images viewer** — list, filter (All / In Use / Unused / Dangling), inspect, delete, and prune Docker images
- **Volumes viewer** — list, filter (in use / unused), inspect, delete, and prune Docker volumes
- **Color coding** — green = in use / running, yellow = unused images, red = unused / stopped, gray = system
- **Status filters** — `T:Toggle Status` cycles filter states on every page; active filter shown in title bar
- **Terminal access** — open an interactive shell inside any running container
- **Kill** — send SIGKILL to a container without a graceful stop
- **Docker Engine update** — upgrade docker-ce without leaving the app (`U`); notified automatically at startup when an update is available
- **WSL restart / Docker restart** — on Windows: shut down and restart WSL (`W`); on Linux: restart the Docker service (`W`)
- **Version bar** — shows installed docker-ce version vs latest available, highlighted in red when an update is ready
- **Resource bar** — total CPU %, memory usage, and core count across all running containers
- **Help page** — full keyboard shortcut reference (`H`)
- **Startup checks** — validates WSL (Windows) or Docker (Linux) is available before the UI loads

---

## Keyboard Controls

### Global (all pages)

| Key | Action |
|-----|--------|
| `←` / `→` | Cycle to previous / next page |
| `C` | Go to Containers page |
| `N` | Go to Networks page |
| `I` | Go to Images page |
| `V` | Go to Volumes page |
| `T` | Toggle Status filter (cycles states per page) |
| `U` | Update Docker Engine |
| `W` | Restart WSL (Windows) / Restart Docker service (Linux) |
| `H` | Show keyboard shortcut help |
| `Q` | Quit |

### Containers page

| Key | Action |
|-----|--------|
| `↑` / `↓` | Navigate the container list |
| `Space` | Mark / unmark container or project |
| `Enter` | Open action menu for selected / marked items |
| `L` | Toggle live log tail panel for highlighted container |
| `P` | Start all stopped containers |
| `S` | Stop all running containers (with confirmation) |
| `D` | Delete marked containers |
| `T` | Toggle Status filter: All → Running Only → Not Running |

### Networks / Images / Volumes pages

| Key | Action |
|-----|--------|
| `↑` / `↓` | Navigate the list |
| `Space` | Mark / unmark item |
| `Enter` | Open action menu for selected item |
| `D` | Delete marked items (Networks / Volumes) |
| `D` | Delete marked images |
| `X` | Prune unused items |
| `T` | Toggle Status filter |
| `Shift+I` | Open Detailed Info for selected item |
| `Shift+D` | Delete selected item (with confirmation) |

### Shift hotkeys — act on highlighted container / project (Containers page)

| Key | Action |
|-----|--------|
| `Shift+L` | Fullscreen live logs |
| `Shift+I` | Fullscreen info |
| `Shift+T` | Open terminal (running containers only) |
| `Shift+P` | Start |
| `Shift+S` | Stop |
| `Shift+K` | Kill |
| `Shift+R` | Restart |
| `Shift+U` | Recreate |
| `Shift+D` | Delete (with confirmation) |

### Action menu (`Enter`)

| Option | Description |
|--------|-------------|
| Start | Start selected container(s) or compose project |
| Stop | Stop selected container(s) |
| Restart | Restart selected container(s) |
| Recreate | `docker compose up --force-recreate` (compose containers only) |
| Kill | Send SIGKILL to selected container(s) |
| Delete | Remove selected container(s) |
| Live Logs | Fullscreen streaming log view (individual containers only) |
| Detailed Info | Scrollable container / project / network / image / volume info |
| Terminal | Open interactive shell (running containers only) |

Press `C` or `Esc` to dismiss the menu without taking action.

### Info / detail views

| Key | Action |
|-----|--------|
| `↑` / `↓` | Scroll one line |
| `PgUp` / `PgDn` | Scroll one screen |
| `Home` / `End` | Jump to top / bottom |
| `Enter` / `Esc` | Close |

---

## Display

```
DocMan - DOcker Container MANager  v1.1.4                          docker: 29.0.0  │  3/4 running
←→:Switch Page  │  [C:Containers]  N:Networks  I:Images  V:Volumes  │  U:Update Docker  W:Restart WSL  H:Help  Q:Quit
↑↓:Navigate     │  SPACE:Mark  ENTER:Actions  │  P:Start All  S:Stop All  D:Delete Marked  L:Live Logs  T:Toggle Status
────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
 M   NAME                                      ID             IMAGE                PORTS                  STATUS
────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
[ ]  myapi
[ ]    db                                      a1b2c3d4e5f6   postgres             5432→5432              Up 2 hours
[ ]    web                                     f6e5d4c3b2a1   nginx                8080→80                Up 2 hours
[ ]  standalone-redis                          1122334455aa   redis                6379→6379              Exited 5 min ago
```

- **Project rows** (cyan) represent a Docker Compose project; selecting one applies actions to all its services
- **Service rows** are indented under their project
- **Standalone containers** (not part of a compose project) appear at the top in yellow
- **Active page** is shown in white `[brackets]` in the navigation bar
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
│   ├── DockerService.cs        # All docker process calls (platform-aware)
│   ├── ComposeService.cs       # docker-compose operations and WSL/Docker restart
│   └── Platform.cs             # Platform abstraction (Windows: wsl, Linux: sh -c)
├── UI/
│   ├── Screen.cs               # Console helpers + VT processing + scroll region
│   ├── AppNav.cs               # Global navigation bar + page routing helpers
│   ├── ContainerListView.cs    # Main container list renderer
│   ├── ActionMenu.cs           # Per-container action overlay
│   ├── NetworksViewer.cs       # Networks page
│   ├── ImagesViewer.cs         # Images page
│   ├── VolumesViewer.cs        # Volumes page
│   ├── InfoViewer.cs           # Scrollable container info + live stats
│   ├── ProjectInfoViewer.cs    # Scrollable project info + compose file viewer
│   ├── LogViewer.cs            # Fullscreen log inspector
│   ├── HelpViewer.cs           # Keyboard shortcut reference
│   ├── Overlay.cs              # Generic overlay box
│   └── (terminal in Program.cs)
├── Utilities/
│   └── ContainerNameParser.cs
├── Program.cs                  # Main loop, input handling, terminal access
└── run.ps1                     # Quick launcher
```

---

## License

MIT

