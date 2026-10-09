using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TasksApp.Models;
using TasksApp.Services;
using TasksApp.ViewModels;
using Xunit;

namespace TasksApp.Tests
{
    public class RequirementsTests
    {
        private readonly string _testDir;

        public RequirementsTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "TasksTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        [Fact]
        public void UR001_UR002_UR003_AddTask_SetsTitleDetailsPriority()
        {
            var task = new TaskItem
            {
                Title = "Board Review",
                Details = "Prepare financial deck",
                IsHighPriority = true
            };

            Assert.Equal("Board Review", task.Title);
            Assert.Equal("Prepare financial deck", task.Details);
            Assert.True(task.IsHighPriority);
            Assert.False(task.IsCompleted);
        }

        [Fact]
        public void UR004_EditTask_UpdatesTitleDetailsPriority()
        {
            var task = new TaskItem
            {
                Title = "Original Title",
                Details = "Original Details",
                IsHighPriority = false
            };

            var clone = task.Clone();
            clone.Title = "Updated Title";
            clone.Details = "Updated Details";
            clone.IsHighPriority = true;

            Assert.Equal("Updated Title", clone.Title);
            Assert.Equal("Updated Details", clone.Details);
            Assert.True(clone.IsHighPriority);
        }

        [Fact]
        public void UR006_MarkTaskDoneAndUndo_TogglesCompletionStatus()
        {
            var task = new TaskItem { Title = "Deliver shipment" };
            Assert.False(task.IsCompleted);
            Assert.Null(task.CompletedAt);

            task.IsCompleted = true;
            Assert.True(task.IsCompleted);
            Assert.NotNull(task.CompletedAt);

            task.IsCompleted = false;
            Assert.False(task.IsCompleted);
            Assert.Null(task.CompletedAt);
        }

        [Fact]
        public void UR007_DragReorder_PreservesExactOrder()
        {
            var storage = new StorageService();
            var reminder = new ReminderService();
            var settings = new AppSettings { DataDirectory = _testDir };
            var vm = new MainViewModel(storage, reminder, settings);

            vm.Tasks.Add(new TaskItem { Title = "First", OrderIndex = 1 });
            vm.Tasks.Add(new TaskItem { Title = "Second", OrderIndex = 2 });
            vm.Tasks.Add(new TaskItem { Title = "Third", OrderIndex = 3 });

            // Move "First" from index 0 to index 2 (bottom)
            vm.ReorderTasks(0, 2);

            Assert.Equal("Second", vm.Tasks[0].Title);
            Assert.Equal(1, vm.Tasks[0].OrderIndex);
            Assert.Equal("Third", vm.Tasks[1].Title);
            Assert.Equal(2, vm.Tasks[1].OrderIndex);
            Assert.Equal("First", vm.Tasks[2].Title);
            Assert.Equal(3, vm.Tasks[2].OrderIndex);
        }

        [Fact]
        public void UR008_UR009_DateNavigation_ScopesTasksToDay()
        {
            var storage = new StorageService();
            var reminder = new ReminderService();
            var settings = new AppSettings { DataDirectory = _testDir };
            var vm = new MainViewModel(storage, reminder, settings);

            DateTime today = DateTime.Today;
            DateTime yesterday = today.AddDays(-1);

            // Save tasks for yesterday
            storage.SaveTasksForDate(_testDir, yesterday, new[]
            {
                new TaskItem { Title = "Yesterday Task", IsCompleted = true }
            });

            // Save tasks for today
            storage.SaveTasksForDate(_testDir, today, new[]
            {
                new TaskItem { Title = "Today Task 1" },
                new TaskItem { Title = "Today Task 2" }
            });

            vm.LoadDayData();
            Assert.Equal(2, vm.Tasks.Count);
            Assert.True(vm.IsToday);

            // Go to previous day (yesterday)
            vm.PreviousDayCommand.Execute(null);
            Assert.Equal(yesterday.Date, vm.CurrentDate.Date);
            Assert.Single(vm.Tasks);
            Assert.Equal("Yesterday Task", vm.Tasks[0].Title);
            Assert.False(vm.IsToday);

            // Jump back to today
            vm.JumpToTodayCommand.Execute(null);
            Assert.Equal(today.Date, vm.CurrentDate.Date);
            Assert.Equal(2, vm.Tasks.Count);
            Assert.True(vm.IsToday);
        }

        [Fact]
        public void UR010_UR011_DailyNotes_SavesAndLoadsCorrectly()
        {
            var storage = new StorageService();
            DateTime testDate = new DateTime(2026, 10, 7);
            string noteContent = "Client called regarding Prime Park warehouse acquisition.\nNext action on Monday.";

            storage.SaveNotesForDate(_testDir, testDate, noteContent);
            string loaded = storage.LoadNotesForDate(_testDir, testDate);

            Assert.Equal(noteContent, loaded);
        }

        [Fact]
        public void UR027_ReminderService_ProtectsAgainstZeroOrBadInterval()
        {
            var reminder = new ReminderService();
            reminder.IntervalSeconds = 0; // Attempt bad value 0
            Assert.True(reminder.IntervalSeconds >= 10, "Interval must be clamped to safe minimum.");

            reminder.IntervalSeconds = -50; // Attempt negative
            Assert.True(reminder.IntervalSeconds >= 10, "Interval must be clamped to safe minimum.");
        }

        [Fact]
        public void UR031_UR032_StorageFileNaming_ConformsToRequiredPattern()
        {
            var storage = new StorageService();
            DateTime date = new DateTime(2026, 10, 7);

            storage.SaveTasksForDate(_testDir, date, new[] { new TaskItem { Title = "Test Task" } });
            storage.SaveNotesForDate(_testDir, date, "Test Note");

            string expectedTaskFileName = $"Task_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}.json";
            string expectedNoteFileName = $"Notes_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}.txt";

            Assert.True(File.Exists(Path.Combine(_testDir, expectedTaskFileName)), $"Tasks file should exist: {expectedTaskFileName}");
            Assert.True(File.Exists(Path.Combine(_testDir, expectedNoteFileName)), $"Notes file should exist: {expectedNoteFileName}");

            // Verify content is human-readable
            string taskContent = File.ReadAllText(Path.Combine(_testDir, expectedTaskFileName));
            Assert.Contains("Test Task", taskContent);
            Assert.Contains("\"title\"", taskContent);
        }

        [Fact]
        public void EmptyNotes_NeverSavesEmptyTxtFile_AndDeletesExistingIfCleared()
        {
            var storage = new StorageService();
            DateTime testDate = new DateTime(2026, 10, 8);
            string noteFile = Path.Combine(_testDir, $"Notes_{testDate.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}.txt");

            // Attempt saving empty notes
            storage.SaveNotesForDate(_testDir, testDate, "");
            Assert.False(File.Exists(noteFile), "Empty note should NOT create a .txt file on disk.");

            storage.SaveNotesForDate(_testDir, testDate, "   ");
            Assert.False(File.Exists(noteFile), "Whitespace-only note should NOT create a .txt file on disk.");

            // Save real note
            storage.SaveNotesForDate(_testDir, testDate, "Some actual note");
            Assert.True(File.Exists(noteFile));

            // Clear the note
            storage.SaveNotesForDate(_testDir, testDate, "");
            Assert.False(File.Exists(noteFile), "Clearing a note must remove the file from disk.");
        }

        [Fact]
        public void AddTaskToOtherDate_SavesToTargetDate_AndNavigatesToDate()
        {
            var storage = new StorageService();
            var reminder = new ReminderService();
            var settings = new AppSettings { DataDirectory = _testDir };
            var vm = new MainViewModel(storage, reminder, settings);

            DateTime today = DateTime.Today;
            DateTime nextWeek = today.AddDays(7);

            vm.AddTaskToDate(new TaskItem { Title = "Contract Signing" }, nextWeek);

            Assert.Equal(nextWeek.Date, vm.CurrentDate.Date);
            Assert.Single(vm.Tasks);
            Assert.Equal("Contract Signing", vm.Tasks[0].Title);

            // Verify persisted in storage
            var savedTasks = storage.LoadTasksForDate(_testDir, nextWeek);
            Assert.Single(savedTasks);
            Assert.Equal("Contract Signing", savedTasks[0].Title);
        }

        [Fact]
        public void NotificationBell_ShowReminderCommand_TogglesReminderOverlayAndLoadsTodayTasks()
        {
            var storage = new StorageService();
            var reminder = new ReminderService();
            var settings = new AppSettings { DataDirectory = _testDir };
            var vm = new MainViewModel(storage, reminder, settings);

            DateTime today = DateTime.Today;
            storage.SaveTasksForDate(_testDir, today, new[]
            {
                new TaskItem { Title = "Pending Task 1", IsCompleted = false },
                new TaskItem { Title = "Done Task 2", IsCompleted = true }
            });
            vm.LoadDayData();

            Assert.False(vm.IsReminderOverlayVisible);

            // User clicks notification bell
            vm.ShowReminderCommand.Execute(null);

            Assert.True(vm.IsReminderOverlayVisible);
            Assert.Equal(2, vm.ReminderTasks.Count);
            Assert.Equal(1, vm.ReminderPendingCount);
            Assert.Equal(1, vm.ReminderCompletedCount);
            Assert.True(vm.HasPendingTasksToday);

            // User clicks bell again to toggle closed
            vm.ShowReminderCommand.Execute(null);
            Assert.False(vm.IsReminderOverlayVisible);
        }

        [Fact]
        public void PendingTasks_AutomaticallyCarryOverToNextDayOrToday()
        {
            var storage = new StorageService();
            DateTime yesterday = DateTime.Today.AddDays(-1);
            DateTime today = DateTime.Today;

            // Save tasks on yesterday: 1 pending, 1 completed
            var yesterdayTasks = new List<TaskItem>
            {
                new TaskItem { Title = "Unfinished Client Review", IsCompleted = false },
                new TaskItem { Title = "Finished Morning Standup", IsCompleted = true }
            };
            storage.SaveTasksForDate(_testDir, yesterday, yesterdayTasks);

            // Execute carry over to today
            var carried = storage.CarryOverPendingTasks(_testDir, today);

            Assert.Single(carried);
            Assert.Equal("Unfinished Client Review", carried[0].Title);
            Assert.True(carried[0].IsCarriedOver);
            Assert.Equal(yesterday.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture), carried[0].CarriedOverFrom);

            // Verify today contains the carried over task
            var todayTasks = storage.LoadTasksForDate(_testDir, today);
            Assert.Single(todayTasks);
            Assert.Equal("Unfinished Client Review", todayTasks[0].Title);

            // Verify yesterday only keeps the completed task
            var remainingYesterdayTasks = storage.LoadTasksForDate(_testDir, yesterday);
            Assert.Single(remainingYesterdayTasks);
            Assert.Equal("Finished Morning Standup", remainingYesterdayTasks[0].Title);
            Assert.True(remainingYesterdayTasks[0].IsCompleted);
        }

        [Fact]
        public void EditTask_WithDatePicker_MovesTaskToNewTargetDate()
        {
            var storage = new StorageService();
            var reminder = new ReminderService();
            var settings = new AppSettings { DataDirectory = _testDir };
            var vm = new MainViewModel(storage, reminder, settings);

            DateTime dateA = DateTime.Today;
            DateTime dateB = DateTime.Today.AddDays(3);

            var task = new TaskItem { Title = "Draft Strategy Doc", Details = "Initial version" };
            vm.AddNewTask(task);

            Assert.Single(vm.Tasks);
            Assert.Equal(dateA.Date, vm.CurrentDate.Date);

            // User edits the task and picks dateB
            var clone = task.Clone();
            clone.Title = "Draft Strategy Doc - Rescheduled";
            vm.SaveEditedTask(clone, dateB);

            // View should now be at dateB with the updated task
            Assert.Equal(dateB.Date, vm.CurrentDate.Date);
            Assert.Single(vm.Tasks);
            Assert.Equal("Draft Strategy Doc - Rescheduled", vm.Tasks[0].Title);

            // Verify dateA in storage no longer has the task
            var tasksOnDateA = storage.LoadTasksForDate(_testDir, dateA);
            Assert.Empty(tasksOnDateA);

            // Verify dateB in storage has the task
            var tasksOnDateB = storage.LoadTasksForDate(_testDir, dateB);
            Assert.Single(tasksOnDateB);
            Assert.Equal("Draft Strategy Doc - Rescheduled", tasksOnDateB[0].Title);
        }

        [Fact]
        public void UpdateService_VersionComparison_DetectsNewerVersions()
        {
            Assert.True(UpdateService.IsVersionNewer("v1.1.0", "1.0.0"));
            Assert.True(UpdateService.IsVersionNewer("1.0.1", "1.0.0"));
            Assert.True(UpdateService.IsVersionNewer("v2.0.0", "1.9.9"));
            Assert.False(UpdateService.IsVersionNewer("v1.0.0", "1.0.0"));
            Assert.False(UpdateService.IsVersionNewer("v0.9.9", "1.0.0"));
            Assert.False(UpdateService.IsVersionNewer("", "1.0.0"));
        }

        [Fact]
        public async Task UpdateService_CheckForUpdates_HandlesNetworkOrNoReleaseGracefully()
        {
            var updater = new UpdateService();
            var result = await updater.CheckForUpdatesAsync();
            Assert.NotNull(result);
            Assert.Equal(UpdateService.CurrentVersion, result.CurrentVersion);
            Assert.False(string.IsNullOrWhiteSpace(result.StatusMessage));
        }

        [Fact]
        public void ThemeManager_SwitchesThemesCorrectly()
        {
            ThemeManager.ApplyTheme(ThemeManager.Win95Theme);
            Assert.True(ThemeManager.IsWin95);
            Assert.Equal(ThemeManager.Win95Theme, ThemeManager.CurrentTheme);

            ThemeManager.ApplyTheme(ThemeManager.DefaultTheme);
            Assert.False(ThemeManager.IsWin95);
            Assert.Equal(ThemeManager.DefaultTheme, ThemeManager.CurrentTheme);
        }

        [Fact]
        public void AppSettings_StoresAndRetrievesThemeProperly()
        {
            string customSettingsPath = Path.Combine(_testDir, "test_settings.json");
            var storage = new StorageService(customSettingsPath);
            var settings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.Win95Theme
            };

            storage.SaveSettings(settings);
            Assert.True(File.Exists(customSettingsPath), $"Settings file should exist at {customSettingsPath}");
            string rawJson = File.ReadAllText(customSettingsPath);
            Assert.Contains("Win95", rawJson);

            var loaded = storage.LoadSettings();

            Assert.Equal(ThemeManager.Win95Theme, loaded.Theme);

            // Clean up
            settings.Theme = ThemeManager.DefaultTheme;
            storage.SaveSettings(settings);
        }
    }
}
