# DocMan - Docker Container Manager

A modern .NET console application for managing Docker containers with an interactive terminal UI.

## Features

- **Interactive TUI**: Navigate containers with arrow keys
- **Container Grouping**: Automatically groups containers by Docker Compose project with dedicated project rows
- **Project-Level Actions**: Select a project row to perform actions on all containers in that project
- **Multi-Select**: Mark multiple containers/projects with spacebar for batch operations
- **Checkbox Indicators**: Clear `[ ]` and `[x]` markers for selected items
- **Overlay Menus**: Clean, dismissible menus for actions with proper background rendering
- **Live Log Inspection**: View last 20 lines of container logs with live updates every second
- **Color-Coded Status**: Visual indication of container health and status (separate columns)
- **Compose Integration**: Quick restart action for docker-compose
- **WSL Management**: Built-in WSL restart capability

## Requirements

- .NET 8.0 Runtime
- Docker Desktop for Windows (running)
- Windows Terminal (recommended for best experience)

## Usage

Run the application from the DocMan directory:

```powershell
dotnet run
```

Or build and run the executable:

```powershell
dotnet build
.\bin\Debug\net8.0\DocMan.exe
```

### Keyboard Controls

| Key | Action |
|-----|--------|
| ↑/↓ | Navigate containers and projects |
| Space | Mark/unmark container or project (marking project marks all its containers) |
| Enter | Show action menu for selected/marked items |
| R | Toggle between all containers and running-only view |
| D | Restart Docker daemon |
| W | Restart WSL |
| Q | Quit application |

### Action Menu

When you press Enter, you'll see an overlay menu with these options:

1. **Stop** - Stop selected container(s)
2. **Start** - Start selected container(s)
3. **Restart** - Restart selected container(s)
4. **Inspect** - View last 20 lines of logs (live updating)
5. **Delete** - Remove selected container(s)

### Optional Arguments

```powershell
# Custom refresh interval (default: 5 seconds)
dotnet run -- -Interval 3
```

## Display Format

```
M    NAME                                     ID            IMAGE                     STATUS                      HEALTH     PORTS
-----------------------------------------------------------------------------------------------------------------------------------
[ ]  datamart                                                                                                                    
[x]    db-1                                   01999231de34  postgres                  Exited (255) 9 months       none       5432→5432
[ ]    dips-1                                 7f343b71fd33  dips/integration          Exited (255) 9 months       none       
[ ]  mors-messageintegration                                                                                                     
[ ]    causecodeupdater                       2555e3ecce7c  mors/updater              Up 6 minutes                healthy    
[x]  worktasks-service-dev                                                                                                       
[x]    db-1                                   cb64bb934ad6  postgres                  Exited (137) 23 hours       none       5433→5432
[x]    rabbitmq-1                             bada3cc73c82  rabbitmq                  Exited (0) 16 minutes       none       5672→5672, 15672...
```

- **M**: Mark indicator (`[ ]` = unmarked, `[x]` = marked for batch operation)
- **NAME**: Container name (project rows in cyan, services indented with 2 spaces)
- **ID**: Short container ID (12 characters)
- **IMAGE**: Docker image name (without tag/digest)
- **STATUS**: Current container status
- **HEALTH**: Health check status (healthy/unhealthy/none)
- **PORTS**: Published ports in format `publicPort→privatePort` (shows first 2, then `...`)

### Project Row Actions

When you select/mark a project row:
- All containers in that project are automatically included
- Marking a project with Space automatically marks all its containers with `[x]`
- Actions apply to all containers in the project

### Running Filter

Press `R` to toggle the running-only filter:
- **OFF** (default): Shows all containers (running and stopped)
- **ON**: Shows only running containers
- Status displayed in header: `[RUNNING ONLY]`
- **SERVICE**: Service name within the project
- **ID**: Short container ID
- **STATUS**: Current container status
- **HEALTH**: Health check status (if available)

## Project Structure

```
DocMan/
├── Models/              # Data models
│   ├── ContainerInfo.cs
│   └── ContainerGroup.cs
├── Services/            # Docker and Compose services
│   ├── DockerService.cs
│   └── ComposeService.cs
├── UI/                  # Console UI components
│   ├── Screen.cs
│   ├── ContainerListView.cs
│   ├── Overlay.cs
│   ├── ActionMenu.cs
│   └── LogViewer.cs
├── Utilities/           # Helper classes
│   └── ContainerNameParser.cs
└── Program.cs           # Main application loop
```

## Building

```powershell
cd DocMan
dotnet build -c Release
```

The compiled executable will be in `bin/Release/net8.0/DocMan.exe`

## License

This project is provided as-is for Docker container management.
