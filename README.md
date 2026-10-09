# Tasks

A high-performance, standalone Windows desktop task and daily notes manager built with WPF, C#, and .NET 8. Delivered strictly as a single self-contained executable with zero required external runtimes or dependencies.

---

## Features

- **Daily Task Scoping**: Tasks belong to specific days. Browse backward/forward through dates or jump directly to today.
- **Automatic Rollover**: Any uncompleted pending tasks automatically carry over to the next day, marked with a `CARRIED OVER` audit badge.
- **Task Management**:
  - Add tasks via modal dialog with title, details, and normal/high priority.
  - Edit tasks with an integrated date picker to easily reschedule work to any date (with `[TODAY]` and `[TOMORROW]` shortcuts).
  - Check off tasks with instant undo and drag-and-drop reordering.
- **Daily Notes**: Free-text markdown/notes for each day that auto-save with instant status confirmation. Empty notes never generate blank files.
- **Configurable Reminders**:
  - Notification bell in the title bar showing pending task status.
  - Scheduled reminder overlays with glowing animation and quick snooze/dismiss options.
  - Granular interval controls (hours/minutes/seconds) plus 15m, 30m, 1h, 2h, and 4h presets.
- **Theme Switcher & Authentic Windows 95 Theme**:
  - **PRIME Sovereign (Default)**: Modern, refined aesthetic with deep blue `#003366`, warm ivory, and sharp editorial typography.
  - **Windows 95 Retro**: Complete retro transformation matching authentic 1995 styling — classic battleship silver `#C0C0C0`, navy `#000080` title gradient, 3D raised and sunken bevels, tactile button depression on click, square pixel caption buttons (`_`, `□`, `✕`), and `MS Sans Serif` system font.
  - Instant live preview directly inside the Settings dialog without restarting.
- **Lightweight Native Auto-Updater**:
  - Checks GitHub Releases (`pyscriptcli/tasks`) for new versions.
  - In-app notification badge when a new release is detected.
  - One-click in-place update: downloads the new release asset, silently swaps the executable, and relaunches the app.
- **System Tray Integration**: Minimizes to the background notification tray to ensure scheduled reminders keep firing.

---

## Tech Stack & Architecture

- **Framework**: .NET 8 LTS (`net8.0-windows`)
- **UI Platform**: Windows Presentation Foundation (WPF) with MVVM architecture
- **Design System**: PRIME Typography & Palette (Cormorant Garamond display, Bebas Neue metrics, Montserrat UI, deep blue `#003366`, warm white `#FFFCFB`, gold `#C9A84C`).
- **Icons**: 100% SVG vector paths (no bitmap fonts or emojis).
- **Storage**: Plain JSON (`Task_<Month> <Day> <Year>`) and plain text (`Notes_<Month> <Day> <Year>`).
- **Distribution**: Single-file self-contained `.exe` (`PublishSingleFile=true`, compressed).

---

## Building from Source

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or Visual Studio 2022 (v17.8+) on Windows.

### Build and Run Tests
```powershell
# Restore dependencies and build solution
dotnet build Tasks.sln

# Run automated tests
dotnet test TasksApp.Tests/TasksApp.Tests.csproj
```

### Publish Single-File Executable (`Tasks.exe`)
To create the standalone, single-file distribution binary:

```powershell
dotnet publish TasksApp/TasksApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish/
```

The resulting `publish/Tasks.exe` is completely portable and can be shared without requiring .NET to be pre-installed on the host machine.

---

## Release & Auto-Update Setup

1. Bump `<Version>` in `TasksApp/TasksApp.csproj` and `UpdateService.CurrentVersion`.
2. Build the release binary using the publish command above.
3. Create a new tag and release on GitHub:
   ```powershell
   git tag v1.0.0
   git push origin v1.0.0
   ```
4. Attach `Tasks.exe` as a release asset in the GitHub Release on `pyscriptcli/tasks`.
5. Running instances of `Tasks.exe` will automatically detect the new version and allow users to update with a single click.

---

## License
MIT License
