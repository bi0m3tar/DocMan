# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [1.1.5] - 2026-03-18

### Changed

- Restructured all menus.
- **Help page** revised.

### Fixed

- **Images page**: Delete Marked key corrected from `P` to `D` — now consistent with Networks and Volumes pages

---

## [1.1.4] - 2026-03-17

### Added

- **Networks page** (`N`) — lists all Docker networks with NAME, STATUS, DRIVER, SUBNET, CONTAINERS and CREATED columns; color-coded (green = in use, red = unused, gray = system); supports multi-select, filter, prune, and action menu with Detailed Info / Delete
- **Images page** (`I`) — lists all Docker images with ID, REPOSITORY, TAG, SIZE, STATUS and CREATED columns; color-coded (green = in use, yellow = unused, red = dangling); supports multi-select, 4-state status filter (All / In Use / Unused / Dangling), prune, and action menu
- **Volumes page** (`V`) — lists all Docker volumes with NAME, STATUS, DRIVER, SCOPE and CREATED columns; color-coded (green = in use, red = unused); supports multi-select, filter, prune, and action menu
- **Global navigation bar** (row 1) — `←→:Switch Page │ C:Containers N:Networks I:Images V:Volumes │ U:Update Docker W:Restart WSL/Docker H:Help Q:Quit`; active page highlighted in white
- **Page cycling** — `←` / `→` arrow keys cycle through Containers → Networks → Images → Volumes and back
- **T:Toggle Status** filter on all pages — cycles All → Running Only → Not Running (Containers), In Use → Unused → All (Networks / Volumes), All → In Use → Unused → Dangling (Images); replaces the old `R:Toggle Running` on the container page
- **Shift+I** — open Detailed Info for selected item on Networks / Images / Volumes pages
- **Shift+D** — delete selected item directly on Networks / Images / Volumes pages
- **Stop All confirmation** — pressing `S` on the container page now shows a confirmation overlay before stopping all running containers

### Changed

- Action menu label "Info" / "Details" unified to **"Detailed Info"** across all pages
- **Container page**: `D:Delete All` renamed to **`D:Delete Marked`** — only deletes marked containers; no-op if nothing is marked
- **Container page**: `R:Toggle Running` moved to **`T:Toggle Status`** with a 3-state cycle (All / Running Only / Not Running); `R` now only handles `Shift+R` (Restart)
- Separator lines standardised to **184 characters** on all pages
- Footer on Networks / Images / Volumes pages shows unused/dangling counts without warning icons
- Filter status badges (e.g. `[IN USE]`, `[NOT RUNNING]`) appear in the title bar rather than the controls row

---

## [1.1.3] - 2026-03-13

### Added

- **Linux support** — DocMan now runs natively on Linux, calling `docker` directly without a WSL wrapper
- **Cross-platform `Platform` abstraction** (`Services/Platform.cs`) — routes all shell commands through `wsl` on Windows and `sh -c` on Linux; the rest of the codebase is identical on both platforms
- Lowercase executable name: `docman` (Windows: `docman.exe`, Linux: `docman`)

### Changed

- On Linux the `W` key restarts the Docker service (`sudo systemctl restart docker`) instead of restarting WSL; the controls bar label updates accordingly
- Prerequisite check is now platform-aware: on Linux it verifies `docker` is in PATH directly, skipping the WSL checks
- Docker daemon auto-start uses `sudo -n service docker start` on Linux instead of `wsl -u root -- service docker start`
- Docker update uses `sudo apt-get install` on Linux instead of the WSL root flag
- `RestartDockerAsync` works on both platforms: PowerShell service control on Windows, `sudo systemctl restart docker` on Linux
- `OpenTerminalAsync` calls `docker exec` directly on Linux instead of routing through `wsl`

---

## [1.1.2] - 2026-03-12

### Added

- **Shift hotkeys** for acting on the highlighted container or project without opening the action menu:
  - `Shift+L` — fullscreen live logs
  - `Shift+I` — fullscreen info (container) or project info (project row)
  - `Shift+T` — open terminal (running containers only)
  - `Shift+P` — start container / project
  - `Shift+S` — stop container / project
  - `Shift+K` — kill container / project
  - `Shift+R` — restart container / project
  - `Shift+U` — recreate container / project
  - `Shift+D` — delete container (with confirmation)
- **Kill** action added to the Container Action menu.
- **Project Info view** — selecting Info on a project row now opens a dedicated view showing the project name, working directory, compose file path, a services summary (with live status), and the full compose file content with YAML syntax highlighting.
- **Scrollable Info views** — both the container Info page and the new Project Info page support `↑`/`↓`, `PgUp`/`PgDn`, `Home`/`End` scrolling. A position indicator `[n–m/total]` is shown in the header when content exceeds the screen.
- **Docker update prompt** — a non-blocking overlay appears ~2 s after launch (when the background version check completes) if a newer `docker-ce` is available. Press `U` to upgrade or any other key to dismiss. Shown at most once per session.
- **H:Help** — a full-screen keyboard shortcut reference, accessible via `H`.
- `L:Live Logs` replaces `I:Inspect` as the live log panel toggle key.

### Changed

- Info page: **Resource Usage** section moved above **Environment** section
- Action menu: "Inspect" renamed to "Live Logs"
- `U` key update flow refactored into a shared `RunDockerUpdateAsync()` function (used by both the key handler and the new startup prompt)

---

## [1.1.1] - 2026-03-09

### Fixed

- The **Delete** function was not working properly from the Container Actions menu because the container(s) was not stopped before deletion

---

## [1.1.0] - 2026-02-27

### Removed

- `-Interval` command-line flag. The refresh interval is now fixed at 3 s

---

## [1.0.9] - 2026-02-27

### Changed

- Default container list refresh interval reduced from 5 s to 3 s
- Container list, stats, and Docker version checks now run as three independent background tasks. Slow operations (`docker stats`, `apt policy docker-ce`) no longer delay the container list from appearing — startup is fast regardless of apt cache state

### Fixed

- **Live log panel** (inspect mode) now automatically re-attaches to the replacement container when a build script disposes and recreates a container, instead of going silent until the user moves the selection. The tracked service is matched by project/service name rather than container ID, so index drift during the recreation window no longer causes the wrong container to be streamed

---

## [1.0.8] - 2026-02-25

### Fixed

- Removed **Info** and **Inspect** from the **Container Action** menu when a project is highlighted

---

## [1.0.7] - 2026-02-25

### Fixed

- Corrected version number in project file

---

## [1.0.0] - 2026-02-25

### Added

- Container list grouped by Docker Compose project, with standalone containers listed separately
- Multi-select — mark individual containers or whole projects with `Space`
- Batch operations — start, stop, or delete all marked items at once
- Global actions — start all / stop all / delete all without selecting anything
- Live log tail panel pinned to the bottom of the screen (`I`)
- Container Info view with image, volumes, network settings, CPU, memory, net/disk I/O, and PID count (auto-refreshes every 2 s)
- Log inspector — last 20 lines of container logs
- Terminal access — open an interactive shell inside any running container
- Docker Engine update — upgrade docker-ce in WSL without leaving the app (`U`)
- WSL restart — shut down and restart WSL (`W`)
- Version bar — shows installed docker-ce version vs latest available, highlighted red when an update is ready
- Resource bar — total CPU %, memory usage, and core count across all running containers
- Startup checks — validates WSL is installed, can start, and that Docker is available before UI loads
- `-Interval` flag to configure the container list refresh interval (default 5 s) — removed in 1.0.9
