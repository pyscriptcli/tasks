using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using TasksApp.Models;
using TasksApp.Services;
using TasksApp.Views;

namespace TasksApp.Tests;

public sealed class FocusTimerTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "FocusTimer_" + Guid.NewGuid().ToString("N"));

    public FocusTimerTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void Session_AccumulatesOnlyFocusedTime_AndPersistsWhenFinished()
    {
        var now = new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero);
        var storage = new StorageService();
        var timer = new FocusTimerService(storage, _testDir, () => now);
        var task = new TaskItem { Id = "task-1", Title = "Prepare proposal" };

        var session = timer.Start(task, new DateTime(2026, 10, 19));
        now = now.AddMinutes(5);
        timer.Pause();
        now = now.AddMinutes(12);
        timer.Resume();
        now = now.AddMinutes(7);
        var completed = timer.Finish();

        Assert.Equal("task-1", session.TaskId);
        Assert.Equal(new DateTime(2026, 10, 19), session.TaskDate);
        Assert.Equal("Prepare proposal", session.TaskTitle);
        Assert.Equal((long)TimeSpan.FromMinutes(12).TotalMilliseconds, completed.FocusedDurationMs);
        Assert.Equal(FocusSessionStatus.Completed, completed.Status);
        Assert.Null(timer.CurrentSession);
        Assert.Equal(completed.FocusedDurationMs, storage.LoadFocusSessions(_testDir)[0].FocusedDurationMs);
    }

    [Fact]
    public void ActiveSession_RestoresAndIncludesElapsedTimeAcrossReload()
    {
        var now = new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero);
        var storage = new StorageService();
        var timer = new FocusTimerService(storage, _testDir, () => now);
        timer.Start(new TaskItem { Id = "task-1", Title = "Prepare proposal" }, new DateTime(2026, 10, 19));
        now = now.AddMinutes(4);

        var restored = new FocusTimerService(storage, _testDir, () => now);

        Assert.NotNull(restored.CurrentSession);
        Assert.Equal(TimeSpan.FromMinutes(4), restored.Elapsed);
    }

    [Fact]
    public void StartingAnotherSessionWhileOneIsUnfinished_IsRejected()
    {
        var now = new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero);
        var timer = new FocusTimerService(new StorageService(), _testDir, () => now);
        timer.Start(new TaskItem { Id = "task-1", Title = "First" }, new DateTime(2026, 10, 19));

        Assert.Throws<InvalidOperationException>(() =>
            timer.Start(new TaskItem { Id = "task-2", Title = "Second" }, new DateTime(2026, 10, 19)));
    }

    [Fact]
    public void DataMigration_CopiesFocusSessionHistory()
    {
        var source = Path.Combine(_testDir, "source");
        var target = Path.Combine(_testDir, "target");
        Directory.CreateDirectory(source);
        var now = new DateTimeOffset(2026, 10, 19, 1, 0, 0, TimeSpan.Zero);
        var timer = new FocusTimerService(new StorageService(), source, () => now);
        timer.Start(new TaskItem { Id = "task-1", Title = "Prepare proposal" }, new DateTime(2026, 10, 19));

        new StorageService().MigrateData(source, target);

        Assert.Single(new StorageService().LoadFocusSessions(target));
    }

    [Fact]
    public void FocusTimerSettings_DefaultAndRoundTripThroughJson()
    {
        var defaults = new AppSettings();

        Assert.True(defaults.FocusTimerFreeDrag);
        Assert.Equal(100, defaults.FocusTimerOpacity);
        Assert.Equal(34, defaults.FocusTimerClockFontSize);
        Assert.Equal(12, defaults.FocusTimerTaskFontSize);

        var path = Path.Combine(_testDir, "settings.json");
        var storage = new StorageService(path);
        storage.SaveSettings(new AppSettings
        {
            DataDirectory = _testDir,
            FocusTimerFreeDrag = false,
            FocusTimerLeft = 325.5,
            FocusTimerTop = 188.25,
            FocusTimerOpacity = 72,
            FocusTimerClockFontSize = 42,
            FocusTimerTaskFontSize = 14
        });

        var loaded = storage.LoadSettings();

        Assert.False(loaded.FocusTimerFreeDrag);
        Assert.Equal(325.5, loaded.FocusTimerLeft);
        Assert.Equal(188.25, loaded.FocusTimerTop);
        Assert.Equal(72, loaded.FocusTimerOpacity);
        Assert.Equal(42, loaded.FocusTimerClockFontSize);
        Assert.Equal(14, loaded.FocusTimerTaskFontSize);
    }

    [Fact]
    public void FocusTimerSettings_OlderSettingsFileUsesDefaults()
    {
        var path = Path.Combine(_testDir, "legacy-settings.json");
        File.WriteAllText(path, "{\"dataDirectory\":\"legacy-data\",\"theme\":\"Default\"}");

        var loaded = new StorageService(path).LoadSettings();

        Assert.True(loaded.FocusTimerFreeDrag);
        Assert.True(double.IsNaN(loaded.FocusTimerLeft));
        Assert.True(double.IsNaN(loaded.FocusTimerTop));
        Assert.Equal(100, loaded.FocusTimerOpacity);
        Assert.Equal(34, loaded.FocusTimerClockFontSize);
        Assert.Equal(12, loaded.FocusTimerTaskFontSize);
    }

    [Fact]
    public void TimerWindow_IsCompactReadableAndUsesAccessibleIconControls()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Tasks.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var window = XDocument.Load(Path.Combine(root!.FullName, "TasksApp", "Views", "FocusTimerWindow.xaml")).Root!;
        Assert.Equal("True", (string?)window.Attribute("Topmost"));
        Assert.Equal("None", (string?)window.Attribute("WindowStyle"));
        Assert.Equal("CanResizeWithGrip", (string?)window.Attribute("ResizeMode"));
        Assert.Equal("False", (string?)window.Attribute("ShowInTaskbar"));
        Assert.Equal("True", (string?)window.Attribute("AllowsTransparency"));
        Assert.Equal("Transparent", (string?)window.Attribute("Background"));
        Assert.Equal("240", (string?)window.Attribute("Width"));
        Assert.Equal("140", (string?)window.Attribute("Height"));
        Assert.Equal("220", (string?)window.Attribute("MinWidth"));
        Assert.Equal("132", (string?)window.Attribute("MinHeight"));

        var elements = window.Descendants().ToList();
        var taskTitle = elements.Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TaskTitleText");
        var elapsed = elements.Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ElapsedText");
        Assert.True(elements.IndexOf(elapsed) < elements.IndexOf(taskTitle));
        Assert.Equal("1", (string?)taskTitle.Attribute("Grid.Row"));
        Assert.Equal("Center", (string?)taskTitle.Attribute("HorizontalAlignment"));
        Assert.Equal("CharacterEllipsis", (string?)taskTitle.Attribute("TextTrimming"));
        Assert.Contains(elements, element =>
            element.Name.LocalName == "Border" &&
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TimerContent");
        Assert.True(int.Parse(taskTitle.Attribute("FontSize")!.Value) < int.Parse(elapsed.Attribute("FontSize")!.Value));
        Assert.Equal("{DynamicResource AppToolbarForeground}", (string?)taskTitle.Attribute("Foreground"));
        Assert.Equal("{DynamicResource AppToolbarForeground}", (string?)elapsed.Attribute("Foreground"));

        Assert.Contains(elements, element =>
            element.Name.LocalName == "WindowChrome" &&
            element.Attribute("ResizeBorderThickness") != null &&
            element.Attribute("GlassFrameThickness")?.Value == "0" &&
            element.Attribute("CaptionHeight")?.Value == "0");

        Assert.Contains(elements, element =>
            element.Name.LocalName == "Border" && element.Attribute("MouseLeftButtonDown")?.Value == "OnWindowMouseLeftButtonDown");

        var settingsWindow = XDocument.Load(Path.Combine(root.FullName, "TasksApp", "Views", "SettingsWindow.xaml"));
        foreach (var name in new[] { "CmbFocusTimerPosition", "SldFocusTimerOpacity", "CmbFocusTimerClockSize", "CmbFocusTimerTaskSize" })
        {
            Assert.Contains(settingsWindow.Descendants(), element =>
                (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);
        }

        foreach (var name in new[] { "PauseResumeButton", "FinishButton", "HideButton" })
        {
            var button = Assert.Single(elements.Where(element =>
                element.Name.LocalName == "Button" &&
                (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name));
            Assert.Null(button.Attribute("Content"));
            Assert.NotNull(button.Attribute("ToolTip"));
            Assert.Contains(button.Attributes(), attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name");
            Assert.Contains(button.Descendants(), child =>
                child.Name.LocalName == "TextBlock" &&
                child.Attribute("Text") != null &&
                (string?)child.Attribute("FontFamily") == "Segoe MDL2 Assets");
        }

        var mainWindow = XDocument.Load(Path.Combine(root.FullName, "TasksApp", "MainWindow.xaml"));
        Assert.Contains(mainWindow.Descendants(), element =>
            element.Name.LocalName == "Button" && ((string?)element.Attribute("Command"))?.Contains("OpenFocusTimerCommand") == true);
    }

    [Fact]
    public void TimerWindow_RendersAtCompactSizeWithLargePresetAndOpacity()
    {
        RunOnSta(() =>
        {
            var settings = new AppSettings
            {
                FocusTimerOpacity = 50,
                FocusTimerClockFontSize = 42,
                FocusTimerTaskFontSize = 14
            };
            var timer = new FocusTimerService(new StorageService(), _testDir);
            var window = new FocusTimerWindow(timer, settings, (_, _) => { });
            try
            {
                window.Show();
                window.Width = 240;
                window.Height = 140;
                window.UpdateLayout();

                var timerContent = (Border)window.FindName("TimerContent")!;
                var bitmap = new RenderTargetBitmap(480, 280, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);

                Assert.Equal(1, window.Opacity);
                Assert.Equal(0.5, timerContent.Opacity);
                Assert.True(bitmap.PixelWidth > 0);
                Assert.Equal(42, window.FindName("ElapsedText") is System.Windows.Controls.TextBlock elapsed ? elapsed.FontSize : 0);
                Assert.Equal(14, window.FindName("TaskTitleText") is System.Windows.Controls.TextBlock title ? title.FontSize : 0);
            }
            finally
            {
                window.CloseForAppExit();
            }
        });
    }

    [Fact]
    public void FocusTimerSettings_PreviewsAppearanceAndCancelRestoresPriorValues()
    {
        RunOnSta(() =>
        {
            var original = new AppSettings { DataDirectory = _testDir, FocusTimerOpacity = 100, FocusTimerClockFontSize = 34, FocusTimerTaskFontSize = 12 };
            AppSettings? previewed = null;
            AppSettings? restored = null;
            var window = new SettingsWindow(original, new StorageService(Path.Combine(_testDir, "settings.json")),
                settings => previewed = new AppSettings { FocusTimerOpacity = settings.FocusTimerOpacity, FocusTimerClockFontSize = settings.FocusTimerClockFontSize, FocusTimerTaskFontSize = settings.FocusTimerTaskFontSize },
                settings => restored = settings);
            var driveControls = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            driveControls.Tick += (_, _) =>
            {
                driveControls.Stop();
                ((Slider)window.FindName("SldFocusTimerOpacity")!).Value = 70;
                ((ComboBox)window.FindName("CmbFocusTimerClockSize")!).SelectedIndex = 2;
                ((ComboBox)window.FindName("CmbFocusTimerTaskSize")!).SelectedIndex = 0;
                ((Button)window.FindName("BtnCancelSettings")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            driveControls.Start();

            Assert.False(window.ShowDialog() ?? true);
            Assert.NotNull(previewed);
            Assert.Equal(70, previewed!.FocusTimerOpacity);
            Assert.Equal(42, previewed.FocusTimerClockFontSize);
            Assert.Equal(10, previewed.FocusTimerTaskFontSize);
            Assert.Same(original, restored);
        });
    }

    [Fact]
    public void FocusTimerSettings_SavePersistsPositionAndAppearance()
    {
        RunOnSta(() =>
        {
            var path = Path.Combine(_testDir, "saved-settings.json");
            var current = new AppSettings { DataDirectory = _testDir };
            var window = new SettingsWindow(current, new StorageService(path));
            var driveControls = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            driveControls.Tick += (_, _) =>
            {
                driveControls.Stop();
                ((ComboBox)window.FindName("CmbFocusTimerPosition")!).SelectedIndex = 1;
                ((Slider)window.FindName("SldFocusTimerOpacity")!).Value = 60;
                ((ComboBox)window.FindName("CmbFocusTimerClockSize")!).SelectedIndex = 0;
                ((ComboBox)window.FindName("CmbFocusTimerTaskSize")!).SelectedIndex = 2;
                ((Button)window.FindName("BtnSaveSettings")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            driveControls.Start();

            Assert.True(window.ShowDialog() ?? false);
            Assert.False(window.UpdatedSettings.FocusTimerFreeDrag);
            Assert.Equal(60, window.UpdatedSettings.FocusTimerOpacity);
            Assert.Equal(26, window.UpdatedSettings.FocusTimerClockFontSize);
            Assert.Equal(14, window.UpdatedSettings.FocusTimerTaskFontSize);

            var saved = new StorageService(path).LoadSettings();
            Assert.False(saved.FocusTimerFreeDrag);
            Assert.Equal(60, saved.FocusTimerOpacity);
            Assert.Equal(26, saved.FocusTimerClockFontSize);
            Assert.Equal(14, saved.FocusTimerTaskFontSize);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true);
    }
}
