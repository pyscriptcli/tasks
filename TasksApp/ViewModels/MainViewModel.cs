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

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? RequestCenterAndActivate;
        public event Action? RequestStopForcingFront;
        public event Action<TaskItem>? RequestEditTask;
        public event Action? RequestAddTaskModal;
        public event Action? RequestOpenSettings;

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
                }
            }
        }

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
        public ICommand DismissReminderCommand { get; }
        public ICommand SnoozeReminderCommand { get; }
        public ICommand OpenSettingsCommand { get; }

        public MainViewModel(StorageService storageService, ReminderService reminderService, AppSettings settings)
        {
            _storageService = storageService;
            _reminderService = reminderService;
            _settings = settings;

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
            DismissReminderCommand = new RelayCommand(ExecuteDismissReminder);
            SnoozeReminderCommand = new RelayCommand(p => ExecuteSnoozeReminder(p));
            OpenSettingsCommand = new RelayCommand(() => RequestOpenSettings?.Invoke());
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

            _dailyNotes = _storageService.LoadNotesForDate(_settings.DataDirectory, _currentDate);
            OnPropertyChanged(nameof(DailyNotes));
            NotesSaveStatus = string.IsNullOrWhiteSpace(_dailyNotes) ? string.Empty : "Saved";

            UpdateCounts();
        }

        private void OnTaskItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TaskItem.IsCompleted))
            {
                UpdateCounts();
                SaveCurrentTasks();
            }
        }

        public void AddNewTask(TaskItem newTask)
        {
            if (newTask == null || string.IsNullOrWhiteSpace(newTask.Title)) return;

            newTask.OrderIndex = Tasks.Count + 1;
            newTask.PropertyChanged += OnTaskItemPropertyChanged;
            Tasks.Add(newTask);

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
            task.PropertyChanged -= OnTaskItemPropertyChanged;
            Tasks.Remove(task);
            UpdateCounts();
            SaveCurrentTasks();
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

        private void ExecuteDismissReminder()
        {
            IsReminderOverlayVisible = false;
            _reminderService.DismissReminder();
            RequestStopForcingFront?.Invoke(); // UR-028: Stops forcing to front
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

            _settings = newSettings;
            _storageService.SaveSettings(_settings);

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
                LoadDayData();
            }

            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(RemindersEnabled));
            OnPropertyChanged(nameof(AlwaysOnTop));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
