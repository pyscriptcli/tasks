# PROJECT HANDOFF SPECIFICATION: TASKS APP (v8.0.0)

**Date**: 2026-10-16  
**Target Environment**: Windows 10/11 x64  
**Target Agent / Developer**: Codex / Future AI Assistant  
**Repository**: [pyscriptcli/tasks](https://github.com/pyscriptcli/tasks) (Branch: `main`, Tag: `v8`)  

---

## 1. Quick Access & Essential Paths

| Resource | Path |
|---|---|
| **Git Repository Root** | `C:\Users\PRIME_Dave\.gemini\antigravity\scratch\tasks_git` |
| **Secondary Workspace Mirror** | `C:\Users\PRIME_Dave\.gemini\antigravity\scratch\TasksApp` |
| **.NET 8 SDK CLI** | `C:\Users\PRIME_Dave\.dotnet\dotnet.exe` |
| **Active Shipped Binary** | `C:\Users\PRIME_Dave\Downloads\Tasks.exe` |
| **Local App Settings** | `C:\Users\PRIME_Dave\AppData\Local\TasksApp\settings.json` |
| **User Data Storage** | `C:\Users\PRIME_Dave\Documents\TasksData` (`tasks.json`, `notes.txt`) |
| **GitHub Remote** | `https://github.com/pyscriptcli/tasks.git` |

---

## 2. Core Build & Verification Commands

All commands can be run directly using the local .NET 8 CLI from `C:\Users\PRIME_Dave\.gemini\antigravity\scratch\tasks_git`:

```powershell
# 1. Run full automated test suite (30/30 tests must pass)
& "C:\Users\PRIME_Dave\.dotnet\dotnet.exe" test

# 2. Build debug version
& "C:\Users\PRIME_Dave\.dotnet\dotnet.exe" build TasksApp\TasksApp.csproj

# 3. Publish single-file self-contained win-x64 binary
& "C:\Users\PRIME_Dave\.dotnet\dotnet.exe" publish TasksApp\TasksApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o TasksApp\publish

# 4. Deploy published binary to user's Downloads directory
Copy-Item "TasksApp\publish\Tasks.exe" "C:\Users\PRIME_Dave\Downloads\Tasks.exe" -Force
Copy-Item "TasksApp\publish\Tasks.exe" "C:\Users\PRIME_Dave\Downloads\Tasks (2).exe" -Force

# 5. Git commit & push automated release tag
git add -A
git commit -m "feat/fix: your commit message"
git tag -f v8  # or bump to v9
git push origin main
git push origin v8 --force
```

---

## 3. Project Architecture & Codebase Map

### Core Project: `TasksApp` (`.NET 8.0-windows`, WPF, Single-File Win-x64)

```
TasksApp/
├── App.xaml / App.xaml.cs            # Single-instance lifecycle, named mutex, IPC wake-up event
├── MainWindow.xaml / .cs             # Main 2-column workspace, title bar, animations, reminder overlay
├── TasksApp.csproj                   # Project configuration, single-file publish settings, versioning
├── app.ico                           # Application icon
├── Models/
│   ├── TaskItem.cs                   # Task model (Id, Title, Details, DueDate, DueTime, IsCompleted, etc.)
│   └── AppSettings.cs                # Settings model (Geometry, DataDir, Theme, Reminders, Interval)
├── ViewModels/
│   ├── MainViewModel.cs              # MVVM ViewModel for task CRUD, daily notes auto-save, reminder states
│   └── ViewModelBase.cs              # INotifyPropertyChanged helper
├── Services/
│   ├── StorageService.cs             # Atomic JSON storage, multi-version migration (v1->v8 capture)
│   ├── ReminderService.cs            # Timer loop checking due tasks, triggering notification overlay
│   ├── TrayIconManager.cs            # Windows system tray integration, right-click context menu
│   └── UpdateService.cs              # GitHub release API poller, self-updater download & replace
├── Themes/
│   ├── ThemeManager.cs               # DynamicResource theme switcher
│   ├── ChristmasTheme.xaml           # Santa's Workshop theme, gold/white/cream high-contrast dialog brushes
│   ├── HalloweenTheme.xaml           # Jack-o'-Lantern theme, orange/black styling, cobwebs, bats
│   ├── DefaultTheme.xaml             # Clean modern navy/white theme
│   ├── RetroTheme.xaml               # Warm retro vintage palette
│   ├── TerminalTheme.xaml            # Monospace hacker green CRT theme
│   └── Win95Theme.xaml               # Classic Windows 95 teal/grey theme
└── Views/
    ├── TaskAddWindow.xaml / .cs      # Modal to add new task with validation
    ├── TaskEditWindow.xaml / .cs     # Modal to edit existing task
    ├── SettingsWindow.xaml / .cs     # Settings dialog (theme picker, data directory, reminder interval)
    └── FirstRunWindow.xaml / .cs     # Initial onboarding wizard to select storage directory
```

---

## 4. Key Systems & Critical Design Rules

### A. High-Contrast Dialog Theming (WCAG AA)
Every theme dictionary in `TasksApp/Themes/*.xaml` defines three critical dialog foreground brushes used by reminder cards and modals:
- `AppDialogTitleForeground`: High-contrast title color (Gold `#FFD700` in Christmas, `#FFA000` in Halloween).
- `AppDialogTextForeground`: High-contrast body color (Pure White `#FFFFFF` in Christmas, `#FFFDF8` in Halloween).
- `AppDialogMutedForeground`: High-contrast secondary/snooze color (Warm Cream `#FFE9B0` in Christmas).
**Rule**: Never use low-contrast red-on-dark-red or dark-gray in dialog text.

### B. Christmas Reminder Spectacle & Animation Lifecycle
- **Gift Box Reminder Card**: Built using vector geometries in `MainWindow.xaml` (`GiftLid`, `GiftLidLift`, `GiftLidRotate`, `ReminderCardScale`).
- **Santa Sky Flight**: `SetupSantaFlight(Canvas canvas)` builds a flying Santa sleigh team with Rudolph (red glowing nose), second reindeer, gold harness reins, Santa Claus, and stardust sparkles undulating across the sky.
- **Slow Snowfall**: `SetupSlowSnow(Canvas canvas, ...)` instantiates staggered snow characters (`❄`, `❅`, `✻`, `•`) with negative `BeginTime` on `DoubleAnimation` for zero-flicker startup.
- **Hit-Testing**: All animation canvases (`ChristmasReminderSky`, `TasksSnowCanvas`, `NotesSnowCanvas`, `OlafStage`) **MUST** have `IsHitTestVisible="False"`. Typing and task dragging must never be blocked.

### C. Olaf Companion Pet System
- `OlafPet` starts in `DailyNotes` grid in XAML, but dynamically transfers to `OlafStage` (spanning columns 0 to 3) in `StartOlafRunning()` so it can roam freely across the entire application window.
- **Direction Flip**: `OlafFacingScale.ScaleX = (targetX >= curX) ? 1.0 : -1.0;`
- **Timer Safety**: `_olafRunTimer` must initialize with `Interval >= 3.5s`. `PerformOlafRunStep()` uses `_isOlafMoving` guard to prevent animation queue overflow and UI thread starvation.

### D. Single-Instance Mutex & Process Recovery
- `App.xaml.cs` uses `TasksApp_SingleInstance_Mutex_9988`.
- If `!isFirstInstance`, it inspects `Process.GetProcesses()` to confirm whether another instance of `Tasks` or `Tasks (2)` is actually running. If no real process exists (orphaned mutex), it proceeds instead of shutting down.

---

## 5. Completed in v8.0.0

- [x] Fixed low-contrast text in Christmas reminder dialog to high-contrast WCAG AA standards.
- [x] Animated gift-box pop-open reminder card with lid lift and tilt.
- [x] Full-screen Santa sleigh flight with Rudolph across `ChristmasReminderSky`.
- [x] Screen-wide and pane-level slow falling snowflakes on Tasks and Daily Notes.
- [x] Dynamic running Olaf companion pet roaming the workspace with interactive animations.
- [x] Hardened single-instance mutex and Olaf timer lifecycle.
- [x] Passed 30/30 unit tests and published to `Downloads\Tasks.exe`.
- [x] Pushed commit and `v8` tag to GitHub (`pyscriptcli/tasks`).

---

## 6. What Is Left / Recommended Next Tasks for Codex

1. **Companion Pets for Other Themes**:
   - Consider adding a friendly roaming cartoon Bat or Ghostling pet for the Halloween theme following the same `OlafStage` pattern.
2. **Audio Cues (Optional)**:
   - Provide an optional chime / holiday bell sound effect when the reminder opens (toggleable in Settings).
3. **Theme Preview Badges in Settings**:
   - In `SettingsWindow.xaml`, render a mini visual preview swatch next to each theme option.
4. **Window Geometry Edge Cases**:
   - On ultra-wide or multi-monitor setups, verify that dragging across monitors preserves optimal aspect ratio when maximizing/restoring.
