using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TasksApp.Helpers;
using TasksApp.Models;
using TasksApp.Services;
using TasksApp.ViewModels;
using TasksApp.Views;

namespace TasksApp
{
    public partial class MainWindow : Window
    {
        private readonly StorageService _storageService;
        private readonly ReminderService _reminderService;
        private AppSettings _settings;
        private readonly MainViewModel _viewModel;
        private readonly TrayIconManager _trayManager;

        private bool _isExplicitExit = false;
        private Point _dragStartPoint;
        private TaskItem? _draggedItem;
        private Storyboard? _glowStoryboard;

        public MainWindow()
        {
            InitializeComponent();

            _storageService = new StorageService();
            _reminderService = new ReminderService();
            _settings = _storageService.LoadSettings();
            ThemeManager.ApplyTheme(_settings.Theme);

            // First Run Storage Prompt (UR-030)
            if (!_settings.HasCompletedFirstRun)
            {
                var firstRunWin = new FirstRunWindow(_settings.DataDirectory);
                if (firstRunWin.ShowDialog() == true)
                {
                    _settings.DataDirectory = firstRunWin.SelectedDirectory;
                    _settings.HasCompletedFirstRun = true;
                    _storageService.SaveSettings(_settings);
                }
            }

            _viewModel = new MainViewModel(_storageService, _reminderService, _settings);
            DataContext = _viewModel;

            _trayManager = new TrayIconManager(this);
            _trayManager.OpenRequested += RestoreAndBringToFront;
            _trayManager.ExitRequested += ExitApplication;
            _trayManager.NewTaskRequested += FocusNewTaskInput;
            _trayManager.ToggleRemindersRequested += ToggleReminders;
            _trayManager.Initialize();

            // Wire VM Events
            _viewModel.RequestCenterAndActivate += OnReminderTriggeredUI;
            _viewModel.RequestStopForcingFront += OnReminderFinishedUI;
            _viewModel.RequestEditTask += OpenEditTaskDialog;
            _viewModel.RequestAddTaskModal += OpenAddTaskDialog;
            _viewModel.RequestOpenSettings += OpenSettingsDialog;

            RestoreWindowBounds();
            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;

            ThemeManager.ThemeChanged += OnThemeChanged;
            OnThemeChanged(_settings.Theme);
        }

        private void OnThemeChanged(string newTheme)
        {
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome != null)
            {
                chrome.CaptionHeight = ThemeManager.IsWin95 ? 26 : 48;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _glowStoryboard = TryFindResource("ReminderGlowAnimation") as Storyboard;
            await _viewModel.InitializeStartupAsync();
        }

        private void RestoreWindowBounds()
        {
            // Set Default Window Geometry (UR-044: wider than tall)
            double width = !double.IsNaN(_settings.WindowWidth) && _settings.WindowWidth >= 800 ? _settings.WindowWidth : 1060;
            double height = !double.IsNaN(_settings.WindowHeight) && _settings.WindowHeight >= 520 ? _settings.WindowHeight : 680;
            Width = width;
            Height = height;

            // Check if saved position is valid and on-screen (UR-052)
            if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop) &&
                IsPositionOnScreen(_settings.WindowLeft, _settings.WindowTop, width, height))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _settings.WindowLeft;
                Top = _settings.WindowTop;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (_settings.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }

            Topmost = _settings.AlwaysOnTop;
        }

        private static bool IsPositionOnScreen(double left, double top, double width, double height)
        {
            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vWidth = SystemParameters.VirtualScreenWidth;
            double vHeight = SystemParameters.VirtualScreenHeight;

            return (left + width > vLeft + 100) &&
                   (left < vLeft + vWidth - 100) &&
                   (top + height > vTop + 50) &&
                   (top < vTop + vHeight - 100);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                PathMaximize.Data = System.Windows.Media.Geometry.Parse("M 2,0 L 10,0 L 10,8 L 8,8 L 8,10 L 0,10 L 0,2 L 2,2 Z M 2,2 L 8,2 L 8,8 L 2,8 Z");
                TxtWin95Maximize.Text = "❐";
            }
            else
            {
                PathMaximize.Data = System.Windows.Media.Geometry.Parse("M 0,0 L 10,0 L 10,10 L 0,10 Z");
                TxtWin95Maximize.Text = "□";
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                // Background execution (UR-050)
                e.Cancel = true;
                _viewModel.FlushNotesSave();
                _viewModel.SaveCurrentTasks();
                SaveWindowBounds();

                Hide();

                if (!_settings.HasShownBackgroundCloseNotification)
                {
                    _settings.HasShownBackgroundCloseNotification = true;
                    _storageService.SaveSettings(_settings);
                    _trayManager.ShowNotification("Tasks Running in Background",
                        "Tasks remains active to notify you of daily reminders. Right-click the sticky note icon anytime to exit.");
                }
            }
            else
            {
                _viewModel.FlushNotesSave();
                _viewModel.SaveCurrentTasks();
                SaveWindowBounds();
                _trayManager.Dispose();
                ThemeManager.ThemeChanged -= OnThemeChanged;
                base.OnClosing(e);
            }
        }

        private void SaveWindowBounds()
        {
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                _settings.WindowWidth = ActualWidth;
                _settings.WindowHeight = ActualHeight;
                _settings.IsMaximized = false;
            }
            else if (WindowState == WindowState.Maximized)
            {
                _settings.IsMaximized = true;
            }

            _storageService.SaveSettings(_settings);
        }

        public void RestoreAndBringToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();
            Focus();

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            NativeMethods.SetForegroundWindow(helper.Handle);
        }

        public void ExitApplication()
        {
            _isExplicitExit = true;
            Close();
            Application.Current.Shutdown();
        }

        private void FocusNewTaskInput()
        {
            RestoreAndBringToFront();
            OpenAddTaskDialog();
        }

        private void ToggleReminders()
        {
            _viewModel.RemindersEnabled = !_viewModel.RemindersEnabled;
        }

        // ========================================================
        // REMINDER ANIMATION & FOREGROUND (UR-023, UR-028)
        // ========================================================
        private void OnReminderTriggeredUI()
        {
            Dispatcher.Invoke(() =>
            {
                // Restore & Center window (UR-023)
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Show();
                Left = (SystemParameters.WorkArea.Width - ActualWidth) / 2 + SystemParameters.WorkArea.Left;
                Top = (SystemParameters.WorkArea.Height - ActualHeight) / 2 + SystemParameters.WorkArea.Top;

                // Force to front for attention
                Topmost = true;
                Activate();
                Focus();

                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                NativeMethods.SetForegroundWindow(helper.Handle);

                // Start glowing border animation (UR-023)
                ReminderGlowBorder.Visibility = Visibility.Visible;
                _glowStoryboard?.Begin(this, true);
            });
        }

        private void OnReminderFinishedUI()
        {
            Dispatcher.Invoke(() =>
            {
                // Stop glowing animation and remove topmost forcing (UR-028)
                _glowStoryboard?.Stop(this);
                ReminderGlowBorder.Visibility = Visibility.Collapsed;
                Topmost = _viewModel.AlwaysOnTop; // Reset to user's setting
            });
        }

        // ========================================================
        // DIALOGS (UR-004, UR-034, UR-045, UR-064)
        // ========================================================
        private void OpenEditTaskDialog(TaskItem task)
        {
            var editWin = new TaskEditWindow(task, _viewModel.CurrentDate) { Owner = this };
            if (editWin.ShowDialog() == true)
            {
                _viewModel.SaveEditedTask(editWin.UpdatedTask, editWin.TargetDate);
            }
        }

        private void OpenAddTaskDialog()
        {
            var addWin = new TaskAddWindow(_viewModel.CurrentDate) { Owner = this };
            if (addWin.ShowDialog() == true && addWin.CreatedTask != null)
            {
                _viewModel.AddTaskToDate(addWin.CreatedTask, addWin.SelectedDate);
            }
        }

        private void OpenSettingsDialog()
        {
            var settingsWin = new SettingsWindow(_settings, _storageService) { Owner = this };
            if (settingsWin.ShowDialog() == true)
            {
                _settings = settingsWin.UpdatedSettings;
                _viewModel.UpdateSettings(_settings);
                Topmost = _settings.AlwaysOnTop;
                ThemeManager.ApplyTheme(_settings.Theme);
            }
            else
            {
                ThemeManager.ApplyTheme(_settings.Theme);
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                OpenAddTaskDialog();
                e.Handled = true;
            }
        }

        // ========================================================
        // DRAG & DROP REORDERING (UR-007)
        // ========================================================
        private void OnTaskListPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedItem = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as TaskItem;
        }

        private void OnTaskListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var listBox = sender as ListBox;
                    var item = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource);
                    if (item != null)
                    {
                        DragDrop.DoDragDrop(item, _draggedItem, DragDropEffects.Move);
                    }
                }
            }
        }

        private void OnTaskListDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnTaskListDrop(object sender, DragEventArgs e)
        {
            if (_draggedItem == null) return;

            var targetItem = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource)?.DataContext as TaskItem;
            if (targetItem != null && targetItem != _draggedItem)
            {
                int oldIndex = _viewModel.Tasks.IndexOf(_draggedItem);
                int newIndex = _viewModel.Tasks.IndexOf(targetItem);

                if (oldIndex >= 0 && newIndex >= 0)
                {
                    _viewModel.ReorderTasks(oldIndex, newIndex);
                }
            }
            _draggedItem = null;
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T parent)
                {
                    return parent;
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}