using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TasksApp.Models;
using TasksApp.Services;

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

        var elements = window.Descendants().ToList();
        var taskTitle = elements.Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TaskTitleText");
        var elapsed = elements.Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ElapsedText");
        Assert.True(elements.IndexOf(elapsed) < elements.IndexOf(taskTitle));
        Assert.Equal("1", (string?)taskTitle.Attribute("Grid.Row"));
        Assert.Equal("Center", (string?)taskTitle.Attribute("HorizontalAlignment"));
        Assert.Equal("CharacterEllipsis", (string?)taskTitle.Attribute("TextTrimming"));
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

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true);
    }
}
