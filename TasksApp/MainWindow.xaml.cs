using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
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
        private Storyboard? _halloweenAmbientStoryboard;
        private Storyboard? _halloweenReminderStoryboard;
        private Storyboard? _christmasAmbientStoryboard;
        private Storyboard? _christmasReminderStoryboard;
        private readonly System.Windows.Threading.DispatcherTimer _olafRunTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4.0) };
        private readonly Random _rng = new Random();
        private bool _olafMovedToStage = false;
        private bool _isOlafRunning = false;
        private bool _isOlafMoving = false;
        private bool _isChristmasInitialized = false;

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
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            RestoreWindowBounds();
            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;

            ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;
            OnThemeChanged(_settings.Theme);
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsReminderOverlayVisible))
            {
                if (_viewModel.IsReminderOverlayVisible)
                {
                    if (ThemeManager.IsHalloween) _halloweenReminderStoryboard?.Begin(this, true);
                    if (ThemeManager.IsChristmas)
                    {
                        _christmasReminderStoryboard?.Begin(this, true);
                        StartChristmasReminderAnimations();
                    }
                }
                else
                {
                    _halloweenReminderStoryboard?.Stop(this);
                    _christmasReminderStoryboard?.Stop(this);
                    StopChristmasReminderAnimations();
                }
            }
        }

        private void OnThemeChanged(string newTheme)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                try
                {
                    Dispatcher.Invoke(() => OnThemeChanged(newTheme));
                }
                catch { }
                return;
            }

            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome != null)
            {
                chrome.CaptionHeight = ThemeManager.IsWin95 ? 26 : 48;
            }

            if (!IsLoaded)
            {
                return;
            }

            if (ThemeManager.IsHalloween)
            {
                _halloweenAmbientStoryboard?.Begin(this, true);
                if (_viewModel?.IsReminderOverlayVisible == true)
                {
                    _halloweenReminderStoryboard?.Begin(this, true);
                }
                else
                {
                    _halloweenReminderStoryboard?.Stop(this);
                }
            }
            else
            {
                _halloweenAmbientStoryboard?.Stop(this);
                _halloweenReminderStoryboard?.Stop(this);
            }

            if (ThemeManager.IsChristmas)
            {
                _christmasAmbientStoryboard?.Begin(this, true);
                StartOlafRunning();
                SetupSlowSnow(TasksSnowCanvas, 14, 11.0, 18.0);
                SetupSlowSnow(NotesSnowCanvas, 12, 10.0, 17.0);

                if (_viewModel?.IsReminderOverlayVisible == true)
                {
                    _christmasReminderStoryboard?.Begin(this, true);
                    StartChristmasReminderAnimations();
                }
                else
                {
                    _christmasReminderStoryboard?.Stop(this);
                    StopChristmasReminderAnimations();
                }
            }
            else
            {
                _christmasAmbientStoryboard?.Stop(this);
                _christmasReminderStoryboard?.Stop(this);
                StopOlafRunning();
                StopChristmasReminderAnimations();
                TasksSnowCanvas.Children.Clear();
                NotesSnowCanvas.Children.Clear();
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _glowStoryboard = TryFindResource("ReminderGlowAnimation") as Storyboard;
            _halloweenAmbientStoryboard = TryFindResource("HalloweenAmbientStoryboard") as Storyboard;
            _halloweenReminderStoryboard = TryFindResource("HalloweenReminderStoryboard") as Storyboard;
            _christmasAmbientStoryboard = TryFindResource("ChristmasAmbientStoryboard") as Storyboard;
            _christmasReminderStoryboard = TryFindResource("ChristmasReminderStoryboard") as Storyboard;

            InitializeChristmasFeatures();

            if (ThemeManager.IsHalloween)
            {
                _halloweenAmbientStoryboard?.Begin(this, true);
            }
            else if (ThemeManager.IsChristmas)
            {
                _christmasAmbientStoryboard?.Begin(this, true);
                StartOlafRunning();
                SetupSlowSnow(TasksSnowCanvas, 14, 11.0, 18.0);
                SetupSlowSnow(NotesSnowCanvas, 12, 10.0, 17.0);
            }

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
                if (!double.IsNaN(Left) && !double.IsNaN(Top))
                {
                    _settings.WindowLeft = Left;
                    _settings.WindowTop = Top;
                }
                if (ActualWidth >= 400 && ActualHeight >= 300)
                {
                    _settings.WindowWidth = ActualWidth;
                    _settings.WindowHeight = ActualHeight;
                }
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

                if (ThemeManager.IsHalloween)
                {
                    _halloweenReminderStoryboard?.Begin(this, true);
                }
                else if (ThemeManager.IsChristmas)
                {
                    _christmasReminderStoryboard?.Begin(this, true);
                    StartChristmasReminderAnimations();
                }
            });
        }

        private void OnReminderFinishedUI()
        {
            Dispatcher.Invoke(() =>
            {
                // Stop glowing animation and remove topmost forcing (UR-028)
                _glowStoryboard?.Stop(this);
                _halloweenReminderStoryboard?.Stop(this);
                _christmasReminderStoryboard?.Stop(this);
                StopChristmasReminderAnimations();
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

        #region Christmas & Olaf Companion Pet System

        private void InitializeChristmasFeatures()
        {
            if (_isChristmasInitialized) return;
            _isChristmasInitialized = true;
            _olafRunTimer.Interval = TimeSpan.FromSeconds(4.0);
            _olafRunTimer.Tick += (s, e) => PerformOlafRunStep();
        }

        private void SetupSlowSnow(Canvas canvas, int count, double minDuration, double maxDuration)
        {
            if (canvas == null) return;
            canvas.Children.Clear();

            string[] snowChars = { "❄", "❅", "✻", "•", "❄", "❅" };
            string[] snowColors = { "#FFFFFF", "#E0F7FA", "#B3E5FC", "#E1F5FE", "#80D8FF", "#FFF8E1" };

            double canvasW = canvas.ActualWidth > 50 ? canvas.ActualWidth : 420;
            double canvasH = canvas.ActualHeight > 50 ? canvas.ActualHeight : 600;

            for (int i = 0; i < count; i++)
            {
                var tb = new TextBlock
                {
                    Text = snowChars[_rng.Next(snowChars.Length)],
                    FontSize = _rng.Next(12, 22),
                    Foreground = (Brush)new BrushConverter().ConvertFromString(snowColors[_rng.Next(snowColors.Length)])!,
                    Opacity = 0.35 + (_rng.NextDouble() * 0.5),
                    IsHitTestVisible = false
                };

                double startX = _rng.NextDouble() * Math.Max(20, canvasW - 24);
                double startY = _rng.NextDouble() * canvasH;

                Canvas.SetLeft(tb, startX);
                Canvas.SetTop(tb, startY);

                double duration = minDuration + (_rng.NextDouble() * (maxDuration - minDuration));

                var fallAnim = new DoubleAnimation
                {
                    From = -25,
                    To = canvasH + 25,
                    Duration = TimeSpan.FromSeconds(duration),
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = -TimeSpan.FromSeconds(_rng.NextDouble() * duration)
                };

                var swayGroup = new TransformGroup();
                var swayTranslate = new TranslateTransform();
                var swayRotate = new RotateTransform();
                swayGroup.Children.Add(swayTranslate);
                swayGroup.Children.Add(swayRotate);

                tb.RenderTransformOrigin = new Point(0.5, 0.5);
                tb.RenderTransform = swayGroup;

                var swayX = new DoubleAnimation(-6 - _rng.Next(0, 8), 6 + _rng.Next(0, 8), TimeSpan.FromSeconds(2.5 + (_rng.NextDouble() * 2.5)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = -TimeSpan.FromSeconds(_rng.NextDouble() * 3.0)
                };
                swayTranslate.BeginAnimation(TranslateTransform.XProperty, swayX);

                var rotateAnim = new DoubleAnimation(0, _rng.Next(0, 2) == 0 ? 360 : -360, TimeSpan.FromSeconds(duration * 0.8))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                swayRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

                canvas.Children.Add(tb);
                tb.BeginAnimation(Canvas.TopProperty, fallAnim);
            }
        }

        private void SetupSantaFlight(Canvas canvas)
        {
            if (canvas == null) return;

            // Container for Santa's Sleigh Team
            var flightGrid = new Grid
            {
                Width = 340,
                Height = 70,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            var flightViewbox = new Viewbox { Stretch = Stretch.Uniform };
            var sceneCanvas = new Canvas { Width = 340, Height = 70 };

            // 1. Reins Connecting Rudolph and Reindeer to Sleigh
            var reinsPath = new Path
            {
                Data = Geometry.Parse("M 30,36 C 80,48 140,48 200,34 M 140,36 C 180,44 210,40 220,34"),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")),
                StrokeThickness = 1.2
            };
            sceneCanvas.Children.Add(reinsPath);

            // 2. Lead Reindeer (Rudolph with glowing red nose)
            var rudolph = new Canvas { Width = 55, Height = 55 };
            Canvas.SetLeft(rudolph, 10);
            Canvas.SetTop(rudolph, 8);
            // Antlers
            var rudolphAntlers = new Path
            {
                Data = Geometry.Parse("M 38,16 L 36,4 L 32,2 M 36,8 L 42,4 M 40,16 L 44,6 L 48,4"),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8D6E63")),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            rudolph.Children.Add(rudolphAntlers);
            // Body & Head
            var rudolphBody = new Path
            {
                Data = Geometry.Parse("M 10,34 C 14,24 28,24 34,28 C 38,20 44,18 48,22 C 50,26 44,32 38,34 C 36,44 26,46 14,46 Z"),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6D4C41"))
            };
            rudolph.Children.Add(rudolphBody);
            // Running Legs
            var rudolphLegs = new Path
            {
                Data = Geometry.Parse("M 14,46 L 6,56 M 18,46 L 14,56 M 32,44 L 40,54 M 36,44 L 46,52"),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5D4037")),
                StrokeThickness = 2.0,
                StrokeStartLineCap = PenLineCap.Round
            };
            rudolph.Children.Add(rudolphLegs);
            // Rudolph Glowing Red Nose
            var rudolphNose = new Ellipse
            {
                Width = 6.5,
                Height = 6.5,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF1744")),
                Effect = new DropShadowEffect
                {
                    Color = (Color)ColorConverter.ConvertFromString("#FF1744"),
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 1.0
                }
            };
            Canvas.SetLeft(rudolphNose, 47);
            Canvas.SetTop(rudolphNose, 22);
            rudolph.Children.Add(rudolphNose);
            sceneCanvas.Children.Add(rudolph);

            // 3. Second Reindeer
            var reindeer2 = new Canvas { Width = 55, Height = 55 };
            Canvas.SetLeft(reindeer2, 85);
            Canvas.SetTop(reindeer2, 10);
            var r2Antlers = new Path
            {
                Data = Geometry.Parse("M 38,16 L 36,4 L 32,2 M 36,8 L 42,4 M 40,16 L 44,6 L 48,4"),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8D6E63")),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            reindeer2.Children.Add(r2Antlers);
            var r2Body = new Path
            {
                Data = Geometry.Parse("M 10,34 C 14,24 28,24 34,28 C 38,20 44,18 48,22 C 50,26 44,32 38,34 C 36,44 26,46 14,46 Z"),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#795548"))
            };
            reindeer2.Children.Add(r2Body);
            var r2Legs = new Path
            {
                Data = Geometry.Parse("M 14,46 L 8,56 M 18,46 L 12,54 M 32,44 L 38,54 M 36,44 L 44,52"),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5D4037")),
                StrokeThickness = 2.0,
                StrokeStartLineCap = PenLineCap.Round
            };
            reindeer2.Children.Add(r2Legs);
            sceneCanvas.Children.Add(reindeer2);

            // 4. Santa's Sleigh & Jolly Santa
            var sleighGroup = new Canvas { Width = 110, Height = 65 };
            Canvas.SetLeft(sleighGroup, 185);
            Canvas.SetTop(sleighGroup, 5);

            // Golden Runners / Skis
            var sleighRunners = new Path
            {
                Data = Geometry.Parse("M 10,58 L 90,58 C 102,58 106,48 98,44 M 24,58 L 30,48 M 76,58 L 72,48"),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")),
                StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round
            };
            sleighGroup.Children.Add(sleighRunners);

            // Toy Sack in the back
            var toySack = new Path
            {
                Data = Geometry.Parse("M 18,48 C 12,30 20,18 34,18 C 44,18 48,28 46,48 Z"),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32")),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")),
                StrokeThickness = 1.0
            };
            sleighGroup.Children.Add(toySack);

            // Sleigh Body (Crimson Red with gold edge)
            var sleighBody = new Path
            {
                Data = Geometry.Parse("M 20,48 C 30,50 78,50 86,46 C 96,44 94,32 82,32 C 60,32 50,40 38,36 C 26,32 20,40 20,48 Z"),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C62828")),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")),
                StrokeThickness = 1.5
            };
            sleighGroup.Children.Add(sleighBody);

            // Santa Claus
            // White fluffy beard
            var santaBeard = new Path
            {
                Data = Geometry.Parse("M 58,26 C 54,34 66,42 70,34 C 74,34 76,26 70,24 Z"),
                Fill = Brushes.White
            };
            sleighGroup.Children.Add(santaBeard);
            // Face
            var santaFace = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCCBC")) };
            Canvas.SetLeft(santaFace, 60);
            Canvas.SetTop(santaFace, 18);
            sleighGroup.Children.Add(santaFace);
            // Red Hat with white pom-pom
            var santaHat = new Path
            {
                Data = Geometry.Parse("M 58,19 C 62,10 72,12 70,19 Z"),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"))
            };
            sleighGroup.Children.Add(santaHat);
            var hatPom = new Ellipse { Width = 5, Height = 5, Fill = Brushes.White };
            Canvas.SetLeft(hatPom, 56);
            Canvas.SetTop(hatPom, 18);
            sleighGroup.Children.Add(hatPom);

            sceneCanvas.Children.Add(sleighGroup);

            // 5. Golden Stardust Sparkles trailing behind the sleigh
            var sparkles = new TextBlock
            {
                Text = "✨  ⭐  ✨",
                FontSize = 13,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")),
                Opacity = 0.85
            };
            Canvas.SetLeft(sparkles, 290);
            Canvas.SetTop(sparkles, 26);
            sceneCanvas.Children.Add(sparkles);

            flightViewbox.Child = sceneCanvas;
            flightGrid.Children.Add(flightViewbox);

            canvas.Children.Add(flightGrid);

            // Horizontal flight across the sky
            double skyWidth = canvas.ActualWidth > 200 ? canvas.ActualWidth : (ActualWidth > 200 ? ActualWidth : 1060);
            Canvas.SetTop(flightGrid, 55);

            var flyX = new DoubleAnimation(-360, skyWidth + 360, TimeSpan.FromSeconds(14.0))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            flightGrid.BeginAnimation(Canvas.LeftProperty, flyX);

            // Gentle wave / bobbing flight motion
            var flyY = new DoubleAnimation(40, 85, TimeSpan.FromSeconds(2.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            flightGrid.BeginAnimation(Canvas.TopProperty, flyY);
        }

        private void StartChristmasReminderAnimations()
        {
            if (!ThemeManager.IsChristmas) return;

            // 1. Gift Box Pop Opening Animation
            if (GiftLidLift != null)
            {
                var liftAnim = new DoubleAnimation(0, -90, TimeSpan.FromSeconds(0.65))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                GiftLidLift.BeginAnimation(TranslateTransform.YProperty, liftAnim);
            }

            if (GiftLidRotate != null)
            {
                var rotateAnim = new DoubleAnimation(0, -14, TimeSpan.FromSeconds(0.65))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                GiftLidRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
            }

            if (ReminderCardScale != null)
            {
                var scaleXAnim = new DoubleAnimation(0.88, 1.0, TimeSpan.FromSeconds(0.6))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                var scaleYAnim = new DoubleAnimation(0.88, 1.0, TimeSpan.FromSeconds(0.6))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
                };
                ReminderCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                ReminderCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            }

            // 2. Full-Screen Slow Snow in Sky Overlay
            SetupSlowSnow(ChristmasReminderSky, 30, 9.0, 18.0);

            // 3. Santa Sleigh & Reindeer Flight
            SetupSantaFlight(ChristmasReminderSky);
        }

        private void StopChristmasReminderAnimations()
        {
            if (GiftLidLift != null)
            {
                GiftLidLift.BeginAnimation(TranslateTransform.YProperty, null);
                GiftLidLift.Y = 0;
            }
            if (GiftLidRotate != null)
            {
                GiftLidRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                GiftLidRotate.Angle = 0;
            }
            if (ReminderCardScale != null)
            {
                ReminderCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ReminderCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                ReminderCardScale.ScaleX = 1.0;
                ReminderCardScale.ScaleY = 1.0;
            }

            ChristmasReminderSky?.Children.Clear();
        }

        private void StartOlafRunning()
        {
            if (!IsLoaded || !ThemeManager.IsChristmas || OlafPet == null || OlafStage == null) return;

            InitializeChristmasFeatures();

            if (!_olafMovedToStage)
            {
                if (OlafPet.Parent is Panel p)
                {
                    p.Children.Remove(OlafPet);
                }
                OlafStage.Children.Add(OlafPet);
                Canvas.SetLeft(OlafPet, 180);
                Canvas.SetTop(OlafPet, 140);
                _olafMovedToStage = true;
            }

            OlafPet.Visibility = Visibility.Visible;
            _isOlafRunning = true;
            _olafRunTimer.Start();
            PerformOlafRunStep();
        }

        private void StopOlafRunning()
        {
            _isOlafRunning = false;
            _isOlafMoving = false;
            _olafRunTimer.Stop();

            if (_olafMovedToStage && OlafPet != null)
            {
                OlafPet.Visibility = Visibility.Collapsed;
            }
        }

        private void PerformOlafRunStep()
        {
            if (!_isOlafRunning || _isOlafMoving || !ThemeManager.IsChristmas || OlafPet == null || OlafStage == null) return;
            _isOlafMoving = true;

            double stageW = OlafStage.ActualWidth > 200 ? OlafStage.ActualWidth : (ActualWidth > 200 ? ActualWidth - 50 : 900);
            double stageH = OlafStage.ActualHeight > 200 ? OlafStage.ActualHeight : (ActualHeight > 200 ? ActualHeight - 120 : 450);

            double minX = 20;
            double maxX = Math.Max(minX + 50, stageW - 140);
            double minY = 20;
            double maxY = Math.Max(minY + 50, stageH - 170);

            double targetX = _rng.Next((int)minX, (int)maxX);
            double targetY = _rng.Next((int)minY, (int)maxY);

            double curX = Canvas.GetLeft(OlafPet);
            if (double.IsNaN(curX)) curX = 100;
            double curY = Canvas.GetTop(OlafPet);
            if (double.IsNaN(curY)) curY = 100;

            // Turn Olaf to face the running direction
            if (OlafFacingScale != null)
            {
                OlafFacingScale.ScaleX = (targetX >= curX) ? 1.0 : -1.0;
            }

            double dist = Math.Sqrt(Math.Pow(targetX - curX, 2) + Math.Pow(targetY - curY, 2));
            double durationSec = Math.Clamp(dist / 160.0, 1.6, 3.6);

            var moveX = new DoubleAnimation(curX, targetX, TimeSpan.FromSeconds(durationSec))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            var moveY = new DoubleAnimation(curY, targetY, TimeSpan.FromSeconds(durationSec))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            moveX.Completed += (s, e) =>
            {
                _isOlafMoving = false;
                PlayOlafIdleAction();
            };

            OlafPet.BeginAnimation(Canvas.LeftProperty, moveX);
            OlafPet.BeginAnimation(Canvas.TopProperty, moveY);

            // Bouncing wobble while running
            if (OlafPetTranslate != null)
            {
                var bounceAnim = new DoubleAnimation(0, -9, TimeSpan.FromSeconds(0.2))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(Math.Max(2, (int)(durationSec / 0.4)))
                };
                OlafPetTranslate.BeginAnimation(TranslateTransform.YProperty, bounceAnim);
            }

            // Schedule next run
            _olafRunTimer.Interval = TimeSpan.FromSeconds(durationSec + _rng.Next(1, 4));
        }

        private void PlayOlafIdleAction()
        {
            if (!_isOlafRunning || !ThemeManager.IsChristmas) return;

            int action = _rng.Next(0, 3);
            switch (action)
            {
                case 0:
                    // Wave stick arms
                    if (OlafArmRightRotate != null)
                    {
                        var waveAnim = new DoubleAnimation(0, 36, TimeSpan.FromSeconds(0.25))
                        {
                            AutoReverse = true,
                            RepeatBehavior = new RepeatBehavior(3)
                        };
                        OlafArmRightRotate.BeginAnimation(RotateTransform.AngleProperty, waveAnim);
                    }
                    break;

                case 1:
                    // Belly wobble / squash & stretch
                    if (OlafBellyScaleX != null && OlafBellyScaleY != null)
                    {
                        var wobbleX = new DoubleAnimation(1.0, 1.15, TimeSpan.FromSeconds(0.3))
                        {
                            AutoReverse = true,
                            RepeatBehavior = new RepeatBehavior(2)
                        };
                        var wobbleY = new DoubleAnimation(1.0, 0.88, TimeSpan.FromSeconds(0.3))
                        {
                            AutoReverse = true,
                            RepeatBehavior = new RepeatBehavior(2)
                        };
                        OlafBellyScaleX.BeginAnimation(ScaleTransform.ScaleXProperty, wobbleX);
                        OlafBellyScaleY.BeginAnimation(ScaleTransform.ScaleYProperty, wobbleY);
                    }
                    break;

                case 2:
                    // Head tilt and snowball balance wobble
                    if (OlafHeadRotate != null)
                    {
                        var tiltAnim = new DoubleAnimation(0, _rng.Next(0, 2) == 0 ? 16 : -16, TimeSpan.FromSeconds(0.35))
                        {
                            AutoReverse = true,
                            RepeatBehavior = new RepeatBehavior(2)
                        };
                        OlafHeadRotate.BeginAnimation(RotateTransform.AngleProperty, tiltAnim);
                    }
                    if (OlafSnowballTranslate != null)
                    {
                        var ballAnim = new DoubleAnimation(0, 5, TimeSpan.FromSeconds(0.3))
                        {
                            AutoReverse = true,
                            RepeatBehavior = new RepeatBehavior(2)
                        };
                        OlafSnowballTranslate.BeginAnimation(TranslateTransform.XProperty, ballAnim);
                    }
                    break;
            }
        }

        #endregion
    }
}