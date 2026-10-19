using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TasksApp.Models;
using TasksApp.Services;

namespace TasksApp.Views;

public partial class FocusTimerWindow : Window
{
    private readonly FocusTimerService _focusTimer;
    private readonly AppSettings _settings;
    private readonly Action<double, double> _savePosition;
    private readonly DispatcherTimer _displayTimer;
    private bool _allowClose;

    public FocusTimerWindow(FocusTimerService focusTimer, AppSettings settings, Action<double, double> savePosition)
    {
        InitializeComponent();
        _focusTimer = focusTimer;
        _settings = settings;
        _savePosition = savePosition;
        if (double.IsFinite(settings.FocusTimerLeft) && double.IsFinite(settings.FocusTimerTop))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.FocusTimerLeft;
            Top = settings.FocusTimerTop;
        }
        ApplyAppearance(settings);
        _focusTimer.Changed += OnFocusTimerChanged;
        _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _displayTimer.Tick += (_, _) => RefreshDisplay();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshDisplay();
                _displayTimer.Start();
            }
            else
            {
                _displayTimer.Stop();
            }
        };
        Loaded += (_, _) =>
        {
            ClampToVirtualScreen();
            SavePosition();
        };
        LocationChanged += (_, _) => ClampToVirtualScreen();
        SizeChanged += (_, _) => ClampToVirtualScreen();
        Closed += (_, _) =>
        {
            _displayTimer.Stop();
            _focusTimer.Changed -= OnFocusTimerChanged;
        };
        RefreshDisplay();
    }

    public void CloseForAppExit()
    {
        _allowClose = true;
        Close();
    }

    private void OnFocusTimerChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(RefreshDisplay);
    }

    private void RefreshDisplay()
    {
        var session = _focusTimer.CurrentSession;
        if (session == null)
        {
            TaskTitleText.Text = string.Empty;
            ElapsedText.Text = "00:00:00";
            return;
        }

        TaskTitleText.Text = session.TaskTitle;
        var elapsed = _focusTimer.Elapsed;
        ElapsedText.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        var paused = session.Status == FocusSessionStatus.Paused;
        PauseResumeIcon.Text = paused ? "\uE768" : "\uE769";
        var action = paused ? "Resume" : "Pause";
        PauseResumeButton.ToolTip = action;
        AutomationProperties.SetName(PauseResumeButton, action);
    }

    private void OnPauseResumeClick(object sender, RoutedEventArgs e)
    {
        if (_focusTimer.CurrentSession?.Status == FocusSessionStatus.Paused)
        {
            _focusTimer.Resume();
        }
        else
        {
            _focusTimer.Pause();
        }
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        if (_focusTimer.CurrentSession != null)
        {
            _focusTimer.Finish();
            Hide();
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnWindowMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_settings.FocusTimerFreeDrag && e.OriginalSource is DependencyObject source && !IsInsideButton(source))
        {
            DragMove();
            ClampToVirtualScreen();
            SavePosition();
        }
    }

    public void ApplyAppearance(AppSettings settings)
    {
        Opacity = Math.Clamp(double.IsFinite(settings.FocusTimerOpacity) ? settings.FocusTimerOpacity : 100, 50, 100) / 100d;
        ElapsedText.FontSize = settings.FocusTimerClockFontSize is 26 or 34 or 42 ? settings.FocusTimerClockFontSize : 34;
        TaskTitleText.FontSize = settings.FocusTimerTaskFontSize is 10 or 12 or 14 ? settings.FocusTimerTaskFontSize : 12;
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings.FocusTimerFreeDrag = settings.FocusTimerFreeDrag;
        _settings.FocusTimerLeft = settings.FocusTimerLeft;
        _settings.FocusTimerTop = settings.FocusTimerTop;
        _settings.FocusTimerOpacity = settings.FocusTimerOpacity;
        _settings.FocusTimerClockFontSize = settings.FocusTimerClockFontSize;
        _settings.FocusTimerTaskFontSize = settings.FocusTimerTaskFontSize;
        ApplyAppearance(settings);
    }

    private void SavePosition()
    {
        if (!IsLoaded || !double.IsFinite(Left) || !double.IsFinite(Top)) return;
        _settings.FocusTimerLeft = Left;
        _settings.FocusTimerTop = Top;
        _savePosition(Left, Top);
    }

    private static bool IsInsideButton(DependencyObject element)
    {
        while (element != null)
        {
            if (element is ButtonBase) return true;
            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void ClampToVirtualScreen()
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;

        double minLeft = SystemParameters.VirtualScreenLeft;
        double minTop = SystemParameters.VirtualScreenTop;
        double maxLeft = minLeft + SystemParameters.VirtualScreenWidth - ActualWidth;
        double maxTop = minTop + SystemParameters.VirtualScreenHeight - ActualHeight;
        Left = Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft));
        Top = Math.Clamp(Top, minTop, Math.Max(minTop, maxTop));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
