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
    public void TimerWindow_IsAlwaysOnTopAndResizable_AndMainWindowCanReopenIt()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Tasks.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var window = XDocument.Load(Path.Combine(root!.FullName, "TasksApp", "Views", "FocusTimerWindow.xaml")).Root!;
        Assert.Equal("True", (string?)window.Attribute("Topmost"));
        Assert.Equal("CanResizeWithGrip", (string?)window.Attribute("ResizeMode"));
        Assert.Equal("False", (string?)window.Attribute("ShowInTaskbar"));
        Assert.Contains(window.Descendants(), element =>
            element.Name.LocalName == "Button" && (string?)element.Attribute("Content") == "HIDE");

        var mainWindow = XDocument.Load(Path.Combine(root.FullName, "TasksApp", "MainWindow.xaml"));
        Assert.Contains(mainWindow.Descendants(), element =>
            element.Name.LocalName == "Button" && ((string?)element.Attribute("Command"))?.Contains("OpenFocusTimerCommand") == true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true);
    }
}
