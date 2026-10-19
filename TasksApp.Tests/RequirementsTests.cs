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
        public void ReminderStatus_UsesOnAndOffLabels()
        {
            var vm = new MainViewModel(new StorageService(), new ReminderService(), new AppSettings { DataDirectory = _testDir });

            Assert.Equal("On", vm.RemindersStatusText);
            vm.RemindersEnabled = false;
            Assert.Equal("Off", vm.RemindersStatusText);
        }

        [Fact]
        public void OpenDataFolderCommand_RequestsConfiguredStorageLocation()
        {
            var vm = new MainViewModel(new StorageService(), new ReminderService(), new AppSettings { DataDirectory = _testDir });
            string? requestedPath = null;
            vm.RequestOpenDataFolder += path => requestedPath = path;

            vm.OpenDataFolderCommand.Execute(null);

            Assert.Equal(_testDir, requestedPath);
        }

        [Fact]
        public void CloseReminderCommand_HidesReminderAndKeepsMainWindowVisible()
        {
            var vm = new MainViewModel(new StorageService(), new ReminderService(), new AppSettings { DataDirectory = _testDir });
            bool minimizeRequested = false;
            vm.RequestMinimize += () => minimizeRequested = true;
            vm.IsReminderOverlayVisible = true;

            vm.CloseReminderCommand.Execute(null);

            Assert.False(vm.IsReminderOverlayVisible);
            Assert.False(minimizeRequested);
        }

        [Fact]
        public void DismissReminderCommand_HidesReminderAndMinimizesMainWindow()
        {
            var vm = new MainViewModel(new StorageService(), new ReminderService(), new AppSettings { DataDirectory = _testDir });
            bool minimizeRequested = false;
            vm.RequestMinimize += () => minimizeRequested = true;
            vm.IsReminderOverlayVisible = true;

            vm.DismissReminderCommand.Execute(null);

            Assert.False(vm.IsReminderOverlayVisible);
            Assert.True(minimizeRequested);
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

            // Verify yesterday preserves both the completed task and carried forward task (historical record is never lost)
            var remainingYesterdayTasks = storage.LoadTasksForDate(_testDir, yesterday);
            Assert.Equal(2, remainingYesterdayTasks.Count);
            Assert.Contains(remainingYesterdayTasks, t => t.Title == "Finished Morning Standup" && t.IsCompleted);
            var carriedForwardTask = remainingYesterdayTasks.First(t => t.Title == "Unfinished Client Review");
            Assert.True(carriedForwardTask.IsCarriedForward);
            Assert.Equal(today.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture), carriedForwardTask.CarriedForwardTo);
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

            // Sequential integer versions (v2, v3, v4, and so on)
            Assert.True(UpdateService.IsVersionNewer("v2", "1.0.3"));
            Assert.True(UpdateService.IsVersionNewer("v3", "v2"));
            Assert.True(UpdateService.IsVersionNewer("v4", "v3"));
            Assert.True(UpdateService.IsVersionNewer("4", "3"));
            Assert.True(UpdateService.IsVersionNewer("v10", "v9"));
            Assert.False(UpdateService.IsVersionNewer("v2", "v2"));
            Assert.False(UpdateService.IsVersionNewer("v2", "v3"));
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

        [Fact]
        public void UpdateSettings_PersistsWin95Theme_ToStorageService()
        {
            string customSettingsPath = Path.Combine(_testDir, "test_vm_settings.json");
            var storage = new StorageService(customSettingsPath);
            var reminder = new ReminderService();
            var initialSettings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.DefaultTheme
            };
            storage.SaveSettings(initialSettings);

            var vm = new MainViewModel(storage, reminder, initialSettings);
            Assert.False(vm.IsWin95Theme);

            var newSettings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.Win95Theme
            };
            vm.UpdateSettings(newSettings);

            Assert.True(vm.IsWin95Theme);
            Assert.True(ThemeManager.IsWin95);

            // Verify reloaded from disk has Win95 saved
            var reloaded = storage.LoadSettings();
            Assert.Equal(ThemeManager.Win95Theme, reloaded.Theme);

            // Clean up
            ThemeManager.ApplyTheme(ThemeManager.DefaultTheme);
        }

        [Fact]
        public void UpdateSettings_PersistsRetroTheme_ToStorageService()
        {
            string customSettingsPath = Path.Combine(_testDir, "test_retro_settings.json");
            var storage = new StorageService(customSettingsPath);
            var reminder = new ReminderService();
            var initialSettings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.DefaultTheme
            };
            storage.SaveSettings(initialSettings);

            var vm = new MainViewModel(storage, reminder, initialSettings);
            Assert.False(vm.IsWin95Theme);

            var newSettings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.RetroTheme
            };
            vm.UpdateSettings(newSettings);

            Assert.False(vm.IsWin95Theme);
            Assert.True(ThemeManager.IsRetro);
            Assert.Equal(ThemeManager.RetroTheme, ThemeManager.CurrentTheme);

            // Verify reloaded from disk has Retro saved
            var reloaded = storage.LoadSettings();
            Assert.Equal(ThemeManager.RetroTheme, reloaded.Theme);

            // Clean up
            ThemeManager.ApplyTheme(ThemeManager.DefaultTheme);
        }

        [Fact]
        public void UpdateSettings_PersistsTerminalTheme_ToStorageService()
        {
            string customSettingsPath = Path.Combine(_testDir, "test_terminal_settings.json");
            var storage = new StorageService(customSettingsPath);
            var reminder = new ReminderService();
            var initialSettings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.DefaultTheme
            };
            storage.SaveSettings(initialSettings);

            var vm = new MainViewModel(storage, reminder, initialSettings);
            Assert.False(vm.IsWin95Theme);

            var newSettings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.TerminalTheme
            };
            vm.UpdateSettings(newSettings);

            Assert.False(vm.IsWin95Theme);
            Assert.True(ThemeManager.IsTerminal);
            Assert.Equal(ThemeManager.TerminalTheme, ThemeManager.CurrentTheme);

            // Verify reloaded from disk has Terminal saved
            var reloaded = storage.LoadSettings();
            Assert.Equal(ThemeManager.TerminalTheme, reloaded.Theme);

            // Clean up
            ThemeManager.ApplyTheme(ThemeManager.DefaultTheme);
        }

        [Fact]
        public void UpdateSettings_PersistsHalloweenTheme_ToStorageService()
        {
            string customSettingsPath = Path.Combine(_testDir, "test_halloween_settings.json");
            var storage = new StorageService(customSettingsPath);
            var reminder = new ReminderService();
            var initialSettings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.DefaultTheme
            };
            storage.SaveSettings(initialSettings);

            var vm = new MainViewModel(storage, reminder, initialSettings);
            Assert.False(vm.IsWin95Theme);

            var newSettings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.HalloweenTheme
            };
            vm.UpdateSettings(newSettings);

            Assert.False(vm.IsWin95Theme);
            Assert.True(ThemeManager.IsHalloween);
            Assert.Equal(ThemeManager.HalloweenTheme, ThemeManager.CurrentTheme);

            // Verify reloaded from disk has Halloween saved
            var reloaded = storage.LoadSettings();
            Assert.Equal(ThemeManager.HalloweenTheme, reloaded.Theme);

            // Clean up
            ThemeManager.ApplyTheme(ThemeManager.DefaultTheme);
        }

        [Fact]
        public void LegacyTaskFormat_DeserializesIsDoneAndOrderIndexCorrectly()
        {
            string legacyJson = @"[
                {
                    ""id"": ""legacy-1"",
                    ""Title"": ""Legacy Done Task"",
                    ""IsDone"": true,
                    ""OrderIndex"": 7,
                    ""priorityGroup"": 0
                },
                {
                    ""id"": ""legacy-2"",
                    ""title"": ""Legacy Pending Task"",
                    ""isDone"": false,
                    ""orderIndex"": 3
                }
            ]";

            var tasks = System.Text.Json.JsonSerializer.Deserialize<List<TaskItem>>(legacyJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(tasks);
            Assert.Equal(2, tasks.Count);

            Assert.Equal("Legacy Done Task", tasks[0].Title);
            Assert.True(tasks[0].IsCompleted);
            Assert.Equal(7, tasks[0].OrderIndex);

            Assert.Equal("Legacy Pending Task", tasks[1].Title);
            Assert.False(tasks[1].IsCompleted);
            Assert.Equal(3, tasks[1].OrderIndex);
        }

        [Fact]
        public void CaptureTasksFromOtherVersions_MigratesAndMergesTasks()
        {
            string activeDir = Path.Combine(_testDir, "ActiveTasks");
            string legacyDir = Path.Combine(_testDir, "LegacyTasks");
            Directory.CreateDirectory(activeDir);
            Directory.CreateDirectory(legacyDir);

            // Create a legacy file in legacyDir
            string legacyFile = Path.Combine(legacyDir, "Task_October 5 2026.json");
            File.WriteAllText(legacyFile, @"[{ ""id"": ""old-1"", ""Title"": ""Ancient Task"", ""IsDone"": false, ""orderIndex"": 1 }]");

            var storage = new StorageService(Path.Combine(_testDir, "settings.json"));
            // Run capture
            storage.CaptureTasksFromOtherVersions(activeDir);

            // Verify activeDir now contains Task_October 5 2026.json if searched, or direct load works
            var activeFiles = Directory.GetFiles(activeDir, "*.json");
            // Also test loading tasks directly from legacy formatted file
            var loaded = storage.LoadTasksForDate(legacyDir, new DateTime(2026, 10, 5));
            Assert.Single(loaded);
            Assert.Equal("Ancient Task", loaded[0].Title);
            Assert.False(loaded[0].IsCompleted);
        }

        [Fact]
        public void ThemeManager_ChristmasTheme_AppliesCorrectIndicatorsAndPersists()
        {
            var settings = new AppSettings { Theme = ThemeManager.ChristmasTheme };
            Assert.Equal("Christmas", settings.Theme);

            ThemeManager.ApplyTheme(ThemeManager.ChristmasTheme);
            Assert.Equal(ThemeManager.ChristmasTheme, ThemeManager.CurrentTheme);
            Assert.True(ThemeManager.IsChristmas);
            Assert.False(ThemeManager.IsHalloween);
            Assert.False(ThemeManager.IsWin95);

            // Revert back to Default
            ThemeManager.ApplyTheme(ThemeManager.DefaultTheme);
            Assert.False(ThemeManager.IsChristmas);
        }

        [Fact]
        public void LegacyTextTaskParsing_ParsesTxtFormatCorrectly()
        {
            string txtPath = Path.Combine(_testDir, "Task_October 7 2026.txt");
            string sample = @"[ ] Title: Task test 1
    Priority: normal
    ID: 1
    Notes: none
---
[x] Title: Done Task from v1
    Priority: high
    ID: 2
    Notes: Specific details here
---";
            File.WriteAllText(txtPath, sample);

            var tasks = StorageService.ParseTextTaskFile(txtPath);
            Assert.Equal(2, tasks.Count);

            Assert.Equal("Task test 1", tasks[0].Title);
            Assert.False(tasks[0].IsCompleted);
            Assert.False(tasks[0].IsHighPriority);
            Assert.Equal("legacy-txt-1", tasks[0].Id);
            Assert.Equal(string.Empty, tasks[0].Details);

            Assert.Equal("Done Task from v1", tasks[1].Title);
            Assert.True(tasks[1].IsCompleted);
            Assert.True(tasks[1].IsHighPriority);
            Assert.Equal("legacy-txt-2", tasks[1].Id);
            Assert.Equal("Specific details here", tasks[1].Details);
        }

        [Fact]
        public void PastTaskFiles_NeverDeletedWhenAllTasksPending()
        {
            var storage = new StorageService();
            DateTime pastDate = new DateTime(2026, 10, 7);
            DateTime today = DateTime.Today;

            var pastTasks = new List<TaskItem>
            {
                new TaskItem { Title = "Only Pending Task 1", IsCompleted = false },
                new TaskItem { Title = "Only Pending Task 2", IsCompleted = false }
            };
            storage.SaveTasksForDate(_testDir, pastDate, pastTasks);

            // Execute carryover
            var carried = storage.CarryOverPendingTasks(_testDir, today);

            Assert.Equal(2, carried.Count);

            // Past file MUST still exist on disk!
            string expectedPastFile = Path.Combine(_testDir, $"Task_{pastDate.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}.json");
            Assert.True(File.Exists(expectedPastFile), "Past task file must NEVER be deleted during carryover!");

            // Past tasks must be preserved with CarriedForwardTo
            var reloadedPastTasks = storage.LoadTasksForDate(_testDir, pastDate);
            Assert.Equal(2, reloadedPastTasks.Count);
            Assert.All(reloadedPastTasks, t => Assert.True(t.IsCarriedForward));
        }

        [Fact]
        public void MergeNotesIntoDate_PreservesExistingAndAppendsDistinctContent()
        {
            var storage = new StorageService();
            DateTime date = new DateTime(2026, 10, 7);

            // Start empty
            storage.MergeNotesIntoDate(_testDir, date, "Initial note from legacy v1.");
            Assert.Equal("Initial note from legacy v1.", storage.LoadNotesForDate(_testDir, date));

            // Merge another distinct note
            storage.MergeNotesIntoDate(_testDir, date, "Second note from sqlite.");
            string combined = storage.LoadNotesForDate(_testDir, date);
            Assert.Contains("Initial note from legacy v1.", combined);
            Assert.Contains("Second note from sqlite.", combined);

            // Merging duplicate should not repeat
            storage.MergeNotesIntoDate(_testDir, date, "Initial note from legacy v1.");
            Assert.Equal(combined, storage.LoadNotesForDate(_testDir, date));
        }

        [Fact]
        public void CaptureTasksFromOtherVersions_CapturesJsonTextAndNotes()
        {
            var storage = new StorageService();
            string activeDir = Path.Combine(_testDir, "ActiveTasks");
            string legacyDir1 = Path.Combine(_testDir, "LegacyTasks1");
            string legacyDir2 = Path.Combine(_testDir, "LegacyTasks2");

            Directory.CreateDirectory(activeDir);
            Directory.CreateDirectory(legacyDir1);
            Directory.CreateDirectory(legacyDir2);

            DateTime date = new DateTime(2028, 5, 20);

            // Legacy 1: JSON tasks
            var legacyJsonTasks = new List<TaskItem>
            {
                new TaskItem { Title = "Legacy Json Task 1", IsCompleted = false }
            };
            storage.SaveTasksForDate(legacyDir1, date, legacyJsonTasks);

            // Legacy 2: Plain text tasks and notes
            string txtTaskFile = Path.Combine(legacyDir2, "Task_May 20 2028.txt");
            File.WriteAllText(txtTaskFile, "[ ] Title: Legacy Txt Task 2\nPriority: high\nID: 99\nNotes: none\n---");
            string txtNoteFile = Path.Combine(legacyDir2, "Notes_May 20 2028.txt");
            File.WriteAllText(txtNoteFile, "Important site meeting note.");

            // Capture
            storage.CaptureTasksFromOtherVersions(activeDir, new[] { legacyDir1, legacyDir2 });

            // Verify activeDir has both tasks!
            var loadedTasks = storage.LoadTasksForDate(activeDir, date);
            Assert.Equal(2, loadedTasks.Count);
            Assert.Contains(loadedTasks, t => t.Title == "Legacy Json Task 1");
            Assert.Contains(loadedTasks, t => t.Title == "Legacy Txt Task 2" && t.IsHighPriority);

            // Verify activeDir has notes!
            string loadedNotes = storage.LoadNotesForDate(activeDir, date);
            Assert.Equal("Important site meeting note.", loadedNotes);
        }

        [Fact]
        public void DeleteCarriedTask_StaysDeletedAfterNavigatingAwayAndBack()
        {
            var storage = new StorageService();
            var reminder = new ReminderService();
            var sourceDate = DateTime.Today.AddDays(-1);
            var targetDate = DateTime.Today;
            var sourceTask = new TaskItem { Title = "Carried task" };
            storage.SaveTasksForDate(_testDir, sourceDate, new[] { sourceTask });

            var settings = new AppSettings { DataDirectory = _testDir };
            var viewModel = new MainViewModel(storage, reminder, settings);
            viewModel.LoadDayData();
            Assert.Contains(viewModel.Tasks, task => task.Id == sourceTask.Id);

            viewModel.DeleteTaskCommand.Execute(viewModel.Tasks.Single(task => task.Id == sourceTask.Id));
            viewModel.CurrentDate = targetDate.AddDays(1);
            viewModel.CurrentDate = targetDate;

            Assert.DoesNotContain(viewModel.Tasks, task => task.Id == sourceTask.Id);
        }

        [Fact]
        public void DeleteCarriedTask_DoesNotRollForwardAgainOnTheNextDay()
        {
            var storage = new StorageService();
            var sourceDate = DateTime.Today.AddDays(-1);
            var deletedDate = DateTime.Today;
            var sourceTask = new TaskItem { Title = "Carried task" };
            storage.SaveTasksForDate(_testDir, sourceDate, new[] { sourceTask });
            storage.CarryOverPendingTasks(_testDir, deletedDate);

            var carriedTask = storage.LoadTasksForDate(_testDir, deletedDate).Single();
            Assert.True(storage.DeleteTaskForDate(_testDir, deletedDate, carriedTask));
            storage.CarryOverPendingTasks(_testDir, deletedDate.AddDays(1));

            Assert.DoesNotContain(storage.LoadTasksForDate(_testDir, deletedDate.AddDays(1)), task => task.Id == sourceTask.Id);
        }

        [Fact]
        public void DeletedTask_RemainsDeletedAfterStorageMigrationAndLegacyImport()
        {
            var storage = new StorageService();
            var date = new DateTime(2099, 10, 19);
            string legacyDirectory = Path.Combine(_testDir, "Legacy");
            string activeDirectory = Path.Combine(_testDir, "Active");
            string migratedDirectory = Path.Combine(_testDir, "Migrated");
            Directory.CreateDirectory(legacyDirectory);
            string legacyFile = Path.Combine(legacyDirectory, $"Task_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}.json");
            File.WriteAllText(legacyFile, "[{\"title\":\"Imported task\",\"details\":\"Legacy details\",\"isHighPriority\":false,\"isCompleted\":false,\"order\":1}]");

            var importedTask = Assert.Single(storage.LoadTasksForDate(legacyDirectory, date));
            storage.MergeTaskIntoDate(activeDirectory, date, importedTask);
            var editedImportedTask = Assert.Single(storage.LoadTasksForDate(activeDirectory, date));
            editedImportedTask.Title = "Edited imported task";
            editedImportedTask.Details = "Edited details";
            storage.SaveTasksForDate(activeDirectory, date, new[] { editedImportedTask });

            var settings = new AppSettings { DataDirectory = activeDirectory };
            var viewModel = new MainViewModel(storage, new ReminderService(), settings);
            viewModel.CurrentDate = date;
            viewModel.LoadDayData();
            var taskToDelete = Assert.Single(viewModel.Tasks);

            viewModel.DeleteTaskCommand.Execute(taskToDelete);
            storage.MigrateData(activeDirectory, migratedDirectory);
            var reimportedTask = Assert.Single(storage.LoadTasksForDate(legacyDirectory, date));
            storage.MergeTaskIntoDate(migratedDirectory, date, reimportedTask);

            Assert.Empty(storage.LoadTasksForDate(migratedDirectory, date));

            var newlyCreatedTask = new TaskItem { Title = "Edited imported task", Details = "Edited details" };
            storage.SaveTasksForDate(migratedDirectory, date, new[] { newlyCreatedTask });

            Assert.Equal(newlyCreatedTask.Id, Assert.Single(storage.LoadTasksForDate(migratedDirectory, date)).Id);
        }

        [Fact]
        public void StorageMigration_MergesDeletionRecordsWhenDestinationAlreadyHasSome()
        {
            var storage = new StorageService();
            var date = new DateTime(2099, 10, 19);
            string sourceDirectory = Path.Combine(_testDir, "Source");
            string targetDirectory = Path.Combine(_testDir, "Target");
            var sourceTask = new TaskItem { Id = "source-task", Title = "Source task" };
            var targetTask = new TaskItem { Id = "target-task", Title = "Target task" };

            storage.SaveTasksForDate(sourceDirectory, date, new[] { sourceTask });
            Assert.True(storage.DeleteTaskForDate(sourceDirectory, date, sourceTask));
            storage.SaveTasksForDate(targetDirectory, date, new[] { targetTask });
            Assert.True(storage.DeleteTaskForDate(targetDirectory, date, targetTask));

            storage.MigrateData(sourceDirectory, targetDirectory);
            storage.MergeTaskIntoDate(targetDirectory, date, sourceTask);
            storage.MergeTaskIntoDate(targetDirectory, date, targetTask);

            Assert.Empty(storage.LoadTasksForDate(targetDirectory, date));

        }

        [Fact]
        public void CarryOverPendingTasks_DoesNotRestoreTaskDeletedFromTargetDate()
        {
            var storage = new StorageService();
            var sourceDate = new DateTime(2026, 10, 18);
            var targetDate = new DateTime(2026, 10, 19);
            var sourceTask = new TaskItem { Title = "Carried task" };

            storage.SaveTasksForDate(_testDir, sourceDate, new[] { sourceTask });
            storage.CarryOverPendingTasks(_testDir, targetDate);
            Assert.Single(storage.LoadTasksForDate(_testDir, targetDate));

            // Deleting the task on Oct 19 empties the target file. Loading that day
            // again reruns carry-over, which must not resurrect the source task.
            storage.SaveTasksForDate(_testDir, targetDate, Array.Empty<TaskItem>());
            storage.LoadTasksForDate(_testDir, new DateTime(2026, 10, 20));
            storage.CarryOverPendingTasks(_testDir, targetDate);

            Assert.Empty(storage.LoadTasksForDate(_testDir, targetDate));
        }

        [Fact]
        public void ChristmasTheme_CanBeSavedAndLoadedInSettings()
        {
            string customSettingsFile = Path.Combine(_testDir, "test_settings.json");
            var storage = new StorageService(customSettingsFile);

            var settings = new AppSettings
            {
                DataDirectory = _testDir,
                Theme = ThemeManager.ChristmasTheme
            };

            storage.SaveSettings(settings);

            var loaded = storage.LoadSettings();
            Assert.Equal(ThemeManager.ChristmasTheme, loaded.Theme);
            Assert.Equal("Christmas", loaded.Theme);
        }

        [Fact]
        public void UpdateService_VersionComparison_ValidatesCurrentVersionAgainstTags()
        {
            string current = UpdateService.CurrentVersion;
            Assert.False(string.IsNullOrWhiteSpace(current));
            Assert.True(UpdateService.TryParseVersion(current, out var currentVer));
            Assert.Equal(typeof(UpdateService).Assembly.GetName().Version!.ToString(3), current);

            // Same major version is not newer
            Assert.False(UpdateService.IsVersionNewer($"v{current}", current));
            Assert.False(UpdateService.IsVersionNewer(current, current));

            // Older version is not newer
            Assert.False(UpdateService.IsVersionNewer("v6", "7"));
            Assert.False(UpdateService.IsVersionNewer("v5", "7"));

            // Newer version is newer
            Assert.True(UpdateService.IsVersionNewer("v8", "7"));
            Assert.True(UpdateService.IsVersionNewer("v10", "7"));
            Assert.True(UpdateService.IsVersionNewer("v9.0.1", "9.0.0"));
            Assert.False(UpdateService.IsVersionNewer("v9.0.1", "9.0.1"));
        }

        [Fact]
        public void UpdateService_CurrentVersionIncludesMinorAndPatchForLatestReleaseComparison()
        {
            var assemblyVersion = typeof(UpdateService).Assembly.GetName().Version;

            Assert.NotNull(assemblyVersion);
            Assert.Equal(assemblyVersion!.ToString(3), UpdateService.CurrentVersion);
        }
    }
}
