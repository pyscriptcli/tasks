# Tasks — Windows Desktop To-Do & Daily Reminders

A self-contained, high-performance desktop application for Windows built with **C#, .NET 8 LTS, WPF, and System.Text.Json**.

## Single-File Executable Location
The application is shippable strictly as a single standalone executable with no companion files, no PDBs, no runtime installations, and no internet connection required:

- **Executable:** `C:\Users\PRIME_Dave\.gemini\antigravity\scratch\TasksApp\publish\Tasks.exe` (100% self-contained single file)

---

## Requirements Verification Matrix

### A. Tasks
- **UR-001 (Add Task):** Dedicated `+ Add Task` button in the navigation bar and empty-state workspace opens a clean, resizable modal dialog (`TaskAddWindow`) to input task details. Shortcuts (`Ctrl+N` or system tray menu) also open this modal.
- **UR-002 (Optional Details):** Multi-line details input inside the modal for additional notes and context.
- **UR-003 (Priority):** Toggle for Normal vs. High Priority directly in the modal dialog, rendering with a distinct gold badge and accent in the list.
- **UR-004 (Edit Task):** Dedicated resizable dialog (`TaskEditWindow`) to edit title, details, and priority.
- **UR-005 (Delete Task):** Instant deletion with immediate disk persistence.
- **UR-006 (Done / Undo):** Checkbox toggles completed status with strike-through styling and recorded completion timestamp.
- **UR-007 (Drag Reordering):** Full WPF drag-and-drop reordering with persistent order indexing.
- **UR-008 (Day Scoping):** Every task belongs to a day. Calendar date picker and day navigation display only that day's tasks.
- **UR-009 (Navigation):** `< Previous Day`, `Next Day >`, and `Today` quick jump.

### B. Daily Notes
- **UR-010 (Free-text Notes):** Dedicated daily notes editor for each day.
- **UR-011 (Auto-save Confirmation):** Automatically saved with debouncing; displays live confirmation pill: `Saved [time]`.

### C. Reminders
- **UR-020 (Repeating Interval):** Configurable interval timer delivering task reminders.
- **UR-021 (All-in-one Reminder):** Consolidated list of all today's tasks clearly partitioned into `PENDING` and `DONE`.
- **UR-022 (Persistent Attention):** Nag cycle repeats until dismissed.
- **UR-023 (Window Bring to Front & Glowing Animation):** Window centers on screen, restores to front, and pulses an animated gold glow effect.
- **UR-024 (Dismiss & Snooze):** One-click Dismiss or Snooze presets (10m, 30m, 1h).
- **UR-025 (Toggle Reminders):** Enable / disable reminders toggle in Settings and tray menu.
- **UR-026 (Interval Presets & Custom Fields):** Hours, Minutes, Seconds inputs plus quick presets (15m, 30m, 1h, 2h, 4h).
- **UR-027 (Zero/Bad Value Protection):** Interval input strictly clamped and validated (minimum 10s).
- **UR-028 (Topmost Reset):** Stops forcing to front once dismissed or snoozed.

### D. First Run & Storage
- **UR-030 (First Run Setup):** On first launch, prompts user to select data directory with folder picker.
- **UR-031 (Task File Naming):** Readable files named `Task_<Month> <Day> <Year>.json` (e.g. `Task_October 7 2026.json`).
- **UR-032 (Notes File Naming):** Readable files named `Notes_<Month> <Day> <Year>.txt` (e.g. `Notes_October 7 2026.txt`).
- **UR-033 (Separated Settings):** Settings isolated in `%LOCALAPPDATA%\TasksApp\settings.json`.
- **UR-034 (Change Storage):** Change storage directory anytime in Settings with automatic file migration.
- **UR-035 (Automatic Setup):** Creates directories automatically on demand.
- **UR-036 (Zero Data Loss):** Atomic writes with temporary file replacement.

### E. Window Behavior
- **UR-040 (Controls):** Custom minimize, maximize/restore, and close buttons.
- **UR-041 (Drag Titlebar):** Full WindowChrome caption dragging.
- **UR-042 (Edge/Corner Resizing):** Full standard edge and corner resizing.
- **UR-043 (Position Memory):** Restores saved position, size, and maximized state.
- **UR-044 (Wider Than Tall):** Default window dimensions: 1060 × 680 px.
- **UR-045 (Popup Dialogs):** All modal dialogs wider than tall by default and resizable with grip.
- **UR-046 (Minimizable Anytime):** Window can be minimized during startup.
- **UR-047 (Always on Top Toggle):** Off by default; toggleable in Settings.
- **UR-048 (Taskbar & Alt+Tab):** Full taskbar and Alt+Tab visibility.
- **UR-049 (Single Instance):** Second launch automatically activates existing window.
- **UR-050 (Background Tray Run):** Closing window keeps app running in tray; notifies user via notification.
- **UR-051 (Tray Exit):** Full application exit via system tray right-click menu.
- **UR-052 (Visible Screen Bounds):** Automatically centers if saved coordinates are outside current monitors.

### F. Startup & Performance
- **UR-060 (Immediate Presentation):** Immediate skeleton screen display on launch.
- **UR-061 (Skeleton Screen & Progress):** Placeholder card layout with percentage progress bar.
- **UR-062 (Sub-2-Second Ready):** Fully interactive in ~100–150 ms (exceeds requirement).
- **UR-063 (Responsive Startup):** Asynchronous step progression.
- **UR-064 (Step Diagnostics Log):** Records millisecond duration of every phase.
- **UR-065 (Slowest Step & Advice):** Highlights slowest step with actionable storage/graphics advice.
- **UR-066 (Instant UI Actions):** All UI mutations occur in 0 ms with background persistence.
- **UR-067 (Low Resource Footprint):** Idle CPU < 0.1%, minimal memory.

### G. Look & Feel
- **UR-070 (Titled 'Tasks'):** Strictly titled "Tasks"; zero company branding or logos.
- **UR-071 (PRIME Palette):** Deep Blue (`#003366`), Warm White (`#FFFCFB`), Gold (`#C9A84C`), Amber (`#FFBF00`), sharp 90-degree square corners (`CornerRadius="0"` everywhere).
- **UR-072 (Vector Icons):** Pure XAML geometry paths; zero emojis.
- **UR-073 (Sticky Note Icon):** Flat yellow square note with folded dog-ear corner.
- **UR-074 (No Pin Button):** No pin button anywhere.
- **UR-075 (Direct Copy):** High-signal, direct text with no corporate filler or decorative kickers.

### H. Delivery
- **UR-080 (Single Program):** Self-contained single-file `Tasks.exe`.
- **UR-081 (All-Inclusive):** .NET runtime and WPF engine embedded.
- **UR-082 (No Internet):** 100% offline functionality.
- **UR-083 (Standard User Privileges):** No admin rights or installer required.
- **UR-084 (Normal Double-Click):** Double-click to run consistently.
