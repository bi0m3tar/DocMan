# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [1.0.8] - 2026-02-25

### Fixed

- Removed Info and Inspect from Container Action menu when a project is highlighted.

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
- `-Interval` flag to configure the container list refresh interval (default 5 s)
