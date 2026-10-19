using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using TasksApp.Helpers;
using TasksApp.Models;
using TasksApp.Services;

namespace TasksApp.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly StorageService _storageService;
        private readonly ReminderService _reminderService;
        private readonly FocusTimerService _focusTimerService;
        private AppSettings _settings;

        private DateTime _currentDate = DateTime.Today;
        private string _dailyNotes = string.Empty;
        private string _notesSaveStatus = "Saved";
        private readonly DispatcherTimer _notesSaveDebounceTimer;
        private readonly DispatcherTimer _midnightTimer;
        private DateTime _lastCheckedDate = DateTime.Today;

        private string _newTaskTitle = string.Empty;
        private string _newTaskDetails = string.Empty;
        private bool _newTaskIsHighPriority = false;
        private bool _isDetailsInputVisible = false;

        private bool _isLoading = true;
        private int _loadingProgress = 0;
        private string _loadingStatusText = "Preparing workspace...";

        private bool _isReminderOverlayVisible = false;
        private ObservableCollection<TaskItem> _reminderTasks = new();
        private bool _isUpdateAvailable = false;
        private string _availableUpdateVersion = string.Empty;

        public ObservableCollection<TaskItem> Tasks { get; } = new();

        public FocusTimerService FocusTimer => _focusTimerService;
        public FocusSession? CurrentFocusSession => _focusTimerService.CurrentSession;
        public bool HasFocusSession => CurrentFocusSession != null;
        public string FocusTimerButtonText => CurrentFocusSession?.Status == FocusSessionStatus.Paused ? "FOCUS PAUSED" : "FOCUS TIMER";

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RequestCenterAndActivate;
        public event Action? RequestStopForcingFront;
        public event Action<TaskItem>? RequestEditTask;
        public event Action? RequestAddTaskModal;
        public event Action? RequestOpenSettings;
        public event Action<string>? RequestStorageError;
        public event Action<string>? RequestOpenDataFolder;
        public event Action? RequestMinimize;
        public event Action<TaskItem>? RequestStartFocus;
        public event Action? RequestOpenFocusTimer;

        public AppSettings Settings => _settings;

        public DateTime CurrentDate
        {
            get => _currentDate;
            set
            {
                if (_currentDate.Date != value.Date)
                {
                    _notesSaveDebounceTimer.Stop();
                    if (!string.IsNullOrWhiteSpace(_dailyNotes))
                    {
                        FlushNotesSave();
                    }
                    _currentDate = value.Date;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayDateString));
                    OnPropertyChanged(nameof(TasksPaneTitle));
                    OnPropertyChanged(nameof(IsToday));
                    LoadDayData();
                }
            }
        }

        public string DisplayDateString => _currentDate.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

        public bool IsToday => _currentDate.Date == DateTime.Today;

        public string TasksPaneTitle => IsToday
            ? "TODAY'S TASKS"
            : $"TASKS FOR {_currentDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()}";

        public string DailyNotes
        {
            get => _dailyNotes;
            set
            {
                if (_dailyNotes != value)
                {
                    _dailyNotes = value;
                    OnPropertyChanged();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        NotesSaveStatus = string.Empty;
                        _notesSaveDebounceTimer.Stop();
                        _storageService.SaveNotesForDate(_settings.DataDirectory, _currentDate, string.Empty);
                    }
                    else
                    {
                        NotesSaveStatus = "Saving...";
                        _notesSaveDebounceTimer.Stop();
                        _notesSaveDebounceTimer.Start();
                    }
                }
            }
        }

        public string NotesSaveStatus
        {
            get => _notesSaveStatus;
            set { _notesSaveStatus = value; OnPropertyChanged(); }
        }

        public string NewTaskTitle
        {
            get => _newTaskTitle;
            set { _newTaskTitle = value; OnPropertyChanged(); }
        }

        public string NewTaskDetails
        {
            get => _newTaskDetails;
            set { _newTaskDetails = value; OnPropertyChanged(); }
        }

        public bool NewTaskIsHighPriority
        {
            get => _newTaskIsHighPriority;
            set { _newTaskIsHighPriority = value; OnPropertyChanged(); }
        }

        public bool IsDetailsInputVisible
        {
            get => _isDetailsInputVisible;
            set { _isDetailsInputVisible = value; OnPropertyChanged(); }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public int LoadingProgress
        {
            get => _loadingProgress;
            set { _loadingProgress = value; OnPropertyChanged(); }
        }

        public string LoadingStatusText
        {
            get => _loadingStatusText;
            set { _loadingStatusText = value; OnPropertyChanged(); }
        }

        public bool IsReminderOverlayVisible
        {
            get => _isReminderOverlayVisible;
            set { _isReminderOverlayVisible = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TaskItem> ReminderTasks
        {
            get => _reminderTasks;
            set { _reminderTasks = value; OnPropertyChanged(); }
        }

        public int PendingCount => Tasks.Count(t => !t.IsCompleted);
        public int CompletedCount => Tasks.Count(t => t.IsCompleted);
        public int TotalCount => Tasks.Count;
        public bool HasTasks => Tasks.Count > 0;
        public bool HasNoTasks => Tasks.Count == 0;

        public int ReminderPendingCount => ReminderTasks.Count(t => !t.IsCompleted);
        public int ReminderCompletedCount => ReminderTasks.Count(t => t.IsCompleted);

        public bool HasPendingTasksToday
        {
            get
            {
                if (IsToday && Tasks.Count > 0)
                {
                    return PendingCount > 0;
                }
                var todayTasks = _storageService.LoadTasksForDate(_settings.DataDirectory, DateTime.Today);
                return todayTasks.Any(t => !t.IsCompleted);
            }
        }

        public bool AlwaysOnTop
        {
            get => _settings.AlwaysOnTop;
            set
            {
                if (_settings.AlwaysOnTop != value)
                {
                    _settings.AlwaysOnTop = value;
                    _storageService.SaveSettings(_settings);
                    OnPropertyChanged();
                }
            }
        }

        public bool IsWin95Theme => ThemeManager.IsWin95;

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set { _isUpdateAvailable = value; OnPropertyChanged(); }
        }

        public string AvailableUpdateVersion
        {
            get => _availableUpdateVersion;
            set { _availableUpdateVersion = value; OnPropertyChanged(); }
        }

        public string AppVersionDisplay => $"v{UpdateService.CurrentVersion.TrimStart('v', 'V')}";

        public bool RemindersEnabled
        {
            get => _settings.RemindersEnabled;
            set
            {
                if (_settings.RemindersEnabled != value)
                {
                    _settings.RemindersEnabled = value;
                    _reminderService.IsEnabled = value;
                    _storageService.SaveSettings(_settings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RemindersStatusText));
                }
            }
        }

        public string RemindersStatusText => RemindersEnabled ? "On" : "Off";

        // Commands
        public ICommand PreviousDayCommand { get; }
        public ICommand NextDayCommand { get; }
        public ICommand JumpToTodayCommand { get; }
        public ICommand AddTaskCommand { get; }
        public ICommand DeleteTaskCommand { get; }
        public ICommand ToggleTaskStatusCommand { get; }
        public ICommand EditTaskCommand { get; }
        public ICommand ToggleDetailsVisibilityCommand { get; }
        public ICommand ShowReminderCommand { get; }
        public ICommand CloseReminderCommand { get; }
        public ICommand DismissReminderCommand { get; }
        public ICommand SnoozeReminderCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenDataFolderCommand { get; }
        public ICommand StartFocusCommand { get; }
        public ICommand OpenFocusTimerCommand { get; }

        public MainViewModel(StorageService storageService, ReminderService reminderService, AppSettings settings)
        {
            _storageService = storageService;
            _reminderService = reminderService;
            _settings = settings;
            _focusTimerService = new FocusTimerService(_storageService, _settings.DataDirectory);
            _focusTimerService.Changed += (_, _) =>
            {
                OnPropertyChanged(nameof(CurrentFocusSession));
                OnPropertyChanged(nameof(HasFocusSession));
                OnPropertyChanged(nameof(FocusTimerButtonText));
                CommandManager.InvalidateRequerySuggested();
            };

            _notesSaveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _notesSaveDebounceTimer.Tick += (s, e) =>
            {
                _notesSaveDebounceTimer.Stop();
                FlushNotesSave();
            };

            _midnightTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _midnightTimer.Tick += (s, e) =>
            {
                if (DateTime.Today != _lastCheckedDate)
                {
                    _lastCheckedDate = DateTime.Today;
                    _storageService.CarryOverPendingTasks(_settings.DataDirectory, DateTime.Today);
                    if (_currentDate.Date < DateTime.Today)
                    {
                        CurrentDate = DateTime.Today;
                    }
                    else if (_currentDate.Date == DateTime.Today)
                    {
                        LoadDayData();
                    }
                }
            };
            _midnightTimer.Start();

            _reminderService.ReminderDue += OnReminderDue;

            PreviousDayCommand = new RelayCommand(() => CurrentDate = CurrentDate.AddDays(-1));
            NextDayCommand = new RelayCommand(() => CurrentDate = CurrentDate.AddDays(1));
            JumpToTodayCommand = new RelayCommand(() => CurrentDate = DateTime.Today);
            AddTaskCommand = new RelayCommand(p => ExecuteAddTask(p));
            DeleteTaskCommand = new RelayCommand(p => ExecuteDeleteTask(p as TaskItem));
            ToggleTaskStatusCommand = new RelayCommand(p => ExecuteToggleTaskStatus(p as TaskItem));
            EditTaskCommand = new RelayCommand(p => ExecuteEditTask(p as TaskItem));
            ToggleDetailsVisibilityCommand = new RelayCommand(() => IsDetailsInputVisible = !IsDetailsInputVisible);
            ShowReminderCommand = new RelayCommand(ExecuteShowReminder);
            CloseReminderCommand = new RelayCommand(ExecuteCloseReminder);
            DismissReminderCommand = new RelayCommand(ExecuteDismissReminder);
            SnoozeReminderCommand = new RelayCommand(p => ExecuteSnoozeReminder(p));
            OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke());
            OpenDataFolderCommand = new RelayCommand(() => RequestOpenDataFolder?.Invoke(_settings.DataDirectory));
            StartFocusCommand = new RelayCommand(p =>
            {
                if (p is TaskItem task) RequestStartFocus?.Invoke(task);
            });
            OpenFocusTimerCommand = new RelayCommand(() => RequestOpenFocusTimer?.Invoke(), () => HasFocusSession);

            ThemeManager.ThemeChanged += _ => OnPropertyChanged(nameof(IsWin95Theme));
        }

        public async Task InitializeStartupAsync()
        {
            LoadingStatusText = "Loading configuration and settings...";
            LoadingProgress = 25;
            await Task.Delay(20);

            LoadingStatusText = "Verifying storage and folder structure...";
            LoadingProgress = 50;
            _storageService.EnsureDirectoryExists(_settings.DataDirectory);
            await Task.Delay(20);

            LoadingStatusText = "Loading tasks and daily notes...";
            LoadingProgress = 75;
            _storageService.CaptureTasksFromOtherVersions(_settings.DataDirectory);
            _storageService.CarryOverPendingTasks(_settings.DataDirectory, DateTime.Today);
            LoadDayData();
            await Task.Delay(20);

            LoadingStatusText = "Initializing task reminder scheduler...";
            LoadingProgress = 95;
            _reminderService.Initialize(_settings.RemindersEnabled, _settings.ReminderIntervalSeconds);
            await Task.Delay(20);

            LoadingStatusText = "Ready.";
            LoadingProgress = 100;
            await Task.Delay(15);

            IsLoading = false;

            // Non-blocking background check for updates from GitHub (pyscriptcli/tasks)
            _ = Task.Run(async () =>
            {
                try
                {
                    var updater = new UpdateService();
                    var update = await updater.CheckForUpdatesAsync();
                    if (update.UpdateAvailable)
                    {
                        App.Current?.Dispatcher.Invoke(() =>
                        {
                            IsUpdateAvailable = true;
                            AvailableUpdateVersion = update.LatestVersion;
                        });
                    }
                }
                catch { }
            });
        }

        public void LoadDayData()
        {
            if (_currentDate.Date == DateTime.Today)
            {
                _storageService.CarryOverPendingTasks(_settings.DataDirectory, DateTime.Today);
            }

            Tasks.Clear();
            var items = _storageService.LoadTasksForDate(_settings.DataDirectory, _currentDate);
            foreach (var item in items)
            {
                item.PropertyChanged += OnTaskItemPropertyChanged;
                Tasks.Add(item);
            }

            if (SortTasksByPriority())
            {
                SaveCurrentTasks();
            }
            _storageService.EnsureDailyFiles(_settings.DataDirectory, _currentDate);

            _dailyNotes = _storageService.LoadNotesForDate(_settings.DataDirectory, _currentDate);
            OnPropertyChanged(nameof(DailyNotes));
            NotesSaveStatus = string.IsNullOrWhiteSpace(_dailyNotes) ? string.Empty : "Saved";

            UpdateCounts();
        }

        private void OnTaskItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TaskItem.IsCompleted) || e.PropertyName == nameof(TaskItem.IsHighPriority))
            {
                if (e.PropertyName == nameof(TaskItem.IsCompleted))
                {
                    UpdateCounts();
                }

                SortTasksByPriority();
                SaveCurrentTasks();
            }
        }

        public void StartFocusSession(TaskItem task) => _focusTimerService.Start(task, _currentDate);

        public FocusSession FinishFocusSession() => _focusTimerService.Finish();

        private bool SortTasksByPriority()
        {
            var orderedTasks = Tasks.OrderBy(task => task.IsCompleted)
                .ThenByDescending(task => task.IsHighPriority)
                .ThenBy(task => task.OrderIndex)
                .ToList();
            var changed = false;
            for (var targetIndex = 0; targetIndex < orderedTasks.Count; targetIndex++)
            {
                var currentIndex = Tasks.IndexOf(orderedTasks[targetIndex]);
                if (currentIndex != targetIndex)
                {
                    Tasks.Move(currentIndex, targetIndex);
                    changed = true;
                }
            }

            for (var i = 0; i < Tasks.Count; i++)
            {
                if (Tasks[i].OrderIndex != i + 1)
                {
                    Tasks[i].OrderIndex = i + 1;
                    changed = true;
                }
            }

            return changed;
        }

        public void AddNewTask(TaskItem newTask)
        {
            if (newTask == null || string.IsNullOrWhiteSpace(newTask.Title)) return;

            newTask.OrderIndex = Tasks.Count + 1;
            newTask.PropertyChanged += OnTaskItemPropertyChanged;
            Tasks.Add(newTask);

            SortTasksByPriority();
            UpdateCounts();
            SaveCurrentTasks();
        }

        public void AddTaskToDate(TaskItem newTask, DateTime targetDate)
        {
            if (newTask == null || string.IsNullOrWhiteSpace(newTask.Title)) return;

            if (targetDate.Date == _currentDate.Date)
            {
                AddNewTask(newTask);
            }
            else
            {
                CurrentDate = targetDate.Date;
                AddNewTask(newTask);
            }
        }

        private void ExecuteAddTask(object? parameter)
        {
            if (parameter is TaskItem taskItem)
            {
                AddNewTask(taskItem);
                return;
            }

            if (!string.IsNullOrWhiteSpace(NewTaskTitle))
            {
                AddNewTask(new TaskItem
                {
                    Title = NewTaskTitle.Trim(),
                    Details = string.IsNullOrWhiteSpace(NewTaskDetails) ? string.Empty : NewTaskDetails.Trim(),
                    IsHighPriority = NewTaskIsHighPriority,
                    CreatedAt = DateTime.Now
                });

                NewTaskTitle = string.Empty;
                NewTaskDetails = string.Empty;
                NewTaskIsHighPriority = false;
                IsDetailsInputVisible = false;
                return;
            }

            RequestAddTaskModal?.Invoke();
        }

        private void ExecuteDeleteTask(TaskItem? task)
        {
            if (task == null) return;
            if (!_storageService.DeleteTaskForDate(_settings.DataDirectory, _currentDate, task))
            {
                RequestStorageError?.Invoke("The task could not be deleted because its deletion record could not be saved.");
                return;
            }

            task.PropertyChanged -= OnTaskItemPropertyChanged;
            Tasks.Remove(task);
            UpdateCounts();
        }

        private void ExecuteToggleTaskStatus(TaskItem? task)
        {
            if (task == null) return;
            task.IsCompleted = !task.IsCompleted;
        }

        private void ExecuteEditTask(TaskItem? task)
        {
            if (task == null) return;
            RequestEditTask?.Invoke(task);
        }

        public void SaveEditedTask(TaskItem editedTask)
        {
            SaveEditedTask(editedTask, _currentDate);
        }

        public void SaveEditedTask(TaskItem editedTask, DateTime targetDate)
        {
            if (editedTask == null) return;

            if (targetDate.Date == _currentDate.Date)
            {
                var existing = Tasks.FirstOrDefault(t => t.Id == editedTask.Id);
                if (existing != null)
                {
                    existing.Title = editedTask.Title.Trim();
                    existing.Details = editedTask.Details?.Trim() ?? string.Empty;
                    existing.IsHighPriority = editedTask.IsHighPriority;
                    SaveCurrentTasks();
                    UpdateCounts();
                }
            }
            else
            {
                // Remove task from currently active day
                var existing = Tasks.FirstOrDefault(t => t.Id == editedTask.Id);
                if (existing != null)
                {
                    existing.PropertyChanged -= OnTaskItemPropertyChanged;
                    Tasks.Remove(existing);
                    SaveCurrentTasks();
                    UpdateCounts();
                }

                // Add or update task in target day storage
                var targetTasks = _storageService.LoadTasksForDate(_settings.DataDirectory, targetDate);
                targetTasks.RemoveAll(t => t.Id == editedTask.Id);
                targetTasks.Add(editedTask);
                _storageService.SaveTasksForDate(_settings.DataDirectory, targetDate, targetTasks);

                // Switch view to target date so user immediately sees their relocated task
                CurrentDate = targetDate.Date;
            }
        }

        public void ReorderTasks(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= Tasks.Count || newIndex < 0 || newIndex >= Tasks.Count || oldIndex == newIndex)
                return;

            var item = Tasks[oldIndex];
            Tasks.RemoveAt(oldIndex);
            Tasks.Insert(newIndex, item);

            for (int i = 0; i < Tasks.Count; i++)
            {
                Tasks[i].OrderIndex = i + 1;
            }

            SortTasksByPriority();
            SaveCurrentTasks();
        }

        public void SaveCurrentTasks()
        {
            _storageService.SaveTasksForDate(_settings.DataDirectory, _currentDate, Tasks);
        }

        public void FlushNotesSave()
        {
            _notesSaveDebounceTimer.Stop();
            if (string.IsNullOrWhiteSpace(_dailyNotes))
            {
                _storageService.SaveNotesForDate(_settings.DataDirectory, _currentDate, string.Empty);
                NotesSaveStatus = string.Empty;
                return;
            }

            _storageService.SaveNotesForDate(_settings.DataDirectory, _currentDate, _dailyNotes);
            NotesSaveStatus = $"Saved {DateTime.Now:h:mm tt}";
        }

        private void UpdateCounts()
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(HasTasks));
            OnPropertyChanged(nameof(HasNoTasks));
            OnPropertyChanged(nameof(HasPendingTasksToday));
        }

        public void ExecuteShowReminder()
        {
            var todayTasks = _storageService.LoadTasksForDate(_settings.DataDirectory, DateTime.Today);
            ReminderTasks.Clear();
            foreach (var t in todayTasks)
            {
                ReminderTasks.Add(t);
            }

            OnPropertyChanged(nameof(ReminderPendingCount));
            OnPropertyChanged(nameof(ReminderCompletedCount));
            OnPropertyChanged(nameof(HasPendingTasksToday));

            IsReminderOverlayVisible = !IsReminderOverlayVisible;
        }

        private void OnReminderDue(object? sender, EventArgs e)
        {
            // Reminder lists ALL of today's tasks (UR-021)
            var todayTasks = _storageService.LoadTasksForDate(_settings.DataDirectory, DateTime.Today);
            ReminderTasks.Clear();
            foreach (var t in todayTasks)
            {
                ReminderTasks.Add(t);
            }

            OnPropertyChanged(nameof(ReminderPendingCount));
            OnPropertyChanged(nameof(ReminderCompletedCount));
            OnPropertyChanged(nameof(HasPendingTasksToday));

            IsReminderOverlayVisible = true;
            RequestCenterAndActivate?.Invoke(); // UR-023: Comes to front, centered, with glowing animation
        }

        private void ExecuteCloseReminder()
        {
            IsReminderOverlayVisible = false;
            RequestStopForcingFront?.Invoke();
        }

        private void ExecuteDismissReminder()
        {
            IsReminderOverlayVisible = false;
            _reminderService.DismissReminder();
            RequestStopForcingFront?.Invoke(); // UR-028: Stops forcing to front
            RequestMinimize?.Invoke();
        }

        private void ExecuteSnoozeReminder(object? parameter)
        {
            int minutes = 15;
            if (parameter is string str && int.TryParse(str, out int parsed))
            {
                minutes = parsed;
            }
            else if (parameter is int val)
            {
                minutes = val;
            }

            IsReminderOverlayVisible = false;
            _reminderService.SnoozeReminder(TimeSpan.FromMinutes(minutes));
            RequestStopForcingFront?.Invoke(); // UR-028
        }

        public void UpdateSettings(AppSettings newSettings)
        {
            bool pathChanged = !string.Equals(_settings.DataDirectory, newSettings.DataDirectory, StringComparison.OrdinalIgnoreCase);
            bool intervalChanged = _settings.ReminderIntervalSeconds != newSettings.ReminderIntervalSeconds;
            bool enabledChanged = _settings.RemindersEnabled != newSettings.RemindersEnabled;
            bool themeChanged = !string.Equals(_settings.Theme, newSettings.Theme, StringComparison.OrdinalIgnoreCase);

            if (pathChanged)
            {
                _notesSaveDebounceTimer.Stop();
                if (!string.IsNullOrWhiteSpace(_dailyNotes))
                {
                    FlushNotesSave();
                }
            }

            _settings = newSettings;
            _storageService.SaveSettings(_settings);

            if (themeChanged)
            {
                ThemeManager.ApplyTheme(_settings.Theme);
            }

            if (intervalChanged)
            {
                _reminderService.IntervalSeconds = _settings.ReminderIntervalSeconds;
            }

            if (enabledChanged)
            {
                _reminderService.IsEnabled = _settings.RemindersEnabled;
            }

            if (pathChanged)
            {
                _focusTimerService.ChangeDataDirectory(_settings.DataDirectory);
                _storageService.EnsureDirectoryExists(_settings.DataDirectory);
                _storageService.CaptureTasksFromOtherVersions(_settings.DataDirectory);
                _storageService.CarryOverPendingTasks(_settings.DataDirectory, DateTime.Today);
                LoadDayData();
            }

            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(RemindersEnabled));
            OnPropertyChanged(nameof(RemindersStatusText));
            OnPropertyChanged(nameof(AlwaysOnTop));
            OnPropertyChanged(nameof(IsWin95Theme));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
