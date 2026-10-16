using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TasksApp.Models;

namespace TasksApp.Services
{
    public class StorageService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _settingsFilePath;

        public StorageService(string? customSettingsFilePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customSettingsFilePath))
            {
                _settingsFilePath = customSettingsFilePath;
            }
            else
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appDir = Path.Combine(appData, "TasksApp");
                if (!Directory.Exists(appDir))
                {
                    Directory.CreateDirectory(appDir);
                }
                _settingsFilePath = Path.Combine(appDir, "settings.json");
            }
        }

        public string GetDefaultDataDirectory()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "TasksData");
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        if (string.IsNullOrWhiteSpace(settings.DataDirectory))
                        {
                            settings.DataDirectory = GetDefaultDataDirectory();
                        }
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            var defaultSettings = new AppSettings
            {
                DataDirectory = GetDefaultDataDirectory(),
                HasCompletedFirstRun = false
            };
            return defaultSettings;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                string dir = Path.GetDirectoryName(_settingsFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public void EnsureDirectoryExists(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private string GetTaskFileName(DateTime date)
        {
            // Format: Task_<Month> <Day> <Year> e.g. Task_October 7 2026.json
            return $"Task_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}.json";
        }

        private string GetNotesFileName(DateTime date)
        {
            // Format: Notes_<Month> <Day> <Year> e.g. Notes_October 7 2026.txt
            return $"Notes_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}.txt";
        }

        public List<TaskItem> LoadTasksForDate(string dataDirectory, DateTime date)
        {
            var result = new List<TaskItem>();
            if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
            {
                return result;
            }

            string primaryPath = Path.Combine(dataDirectory, GetTaskFileName(date));
            string rawName = $"Task_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}";
            string fallbackPath = Path.Combine(dataDirectory, rawName);

            string targetPath = File.Exists(primaryPath) ? primaryPath : (File.Exists(fallbackPath) ? fallbackPath : string.Empty);

            if (!string.IsNullOrEmpty(targetPath))
            {
                try
                {
                    string json = File.ReadAllText(targetPath);
                    var items = JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions);
                    if (items != null)
                    {
                        result = items.OrderBy(t => t.OrderIndex).ToList();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read tasks file: {ex.Message}");
                }
            }

            return result;
        }

        public void SaveTasksForDate(string dataDirectory, DateTime date, IEnumerable<TaskItem> tasks)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) return;

            try
            {
                EnsureDirectoryExists(dataDirectory);
                string filePath = Path.Combine(dataDirectory, GetTaskFileName(date));
                string rawName = $"Task_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}";
                string fallbackPath = Path.Combine(dataDirectory, rawName);

                var taskList = tasks.ToList();
                if (taskList.Count == 0)
                {
                    if (File.Exists(filePath)) try { File.Delete(filePath); } catch { }
                    if (File.Exists(fallbackPath)) try { File.Delete(fallbackPath); } catch { }
                    return;
                }

                for (int i = 0; i < taskList.Count; i++)
                {
                    taskList[i].OrderIndex = i + 1;
                }

                string json = JsonSerializer.Serialize(taskList, JsonOptions);
                string tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write tasks file: {ex.Message}");
            }
        }

        public string LoadNotesForDate(string dataDirectory, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
            {
                return string.Empty;
            }

            string primaryPath = Path.Combine(dataDirectory, GetNotesFileName(date));
            string rawName = $"Notes_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}";
            string fallbackPath = Path.Combine(dataDirectory, rawName);

            string targetPath = File.Exists(primaryPath) ? primaryPath : (File.Exists(fallbackPath) ? fallbackPath : string.Empty);

            if (!string.IsNullOrEmpty(targetPath))
            {
                try
                {
                    string content = File.ReadAllText(targetPath);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        // Remove empty notes file from disk
                        try { File.Delete(targetPath); } catch { }
                        return string.Empty;
                    }
                    return content;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read notes file: {ex.Message}");
                }
            }

            return string.Empty;
        }

        public void SaveNotesForDate(string dataDirectory, DateTime date, string content)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) return;

            try
            {
                EnsureDirectoryExists(dataDirectory);
                string filePath = Path.Combine(dataDirectory, GetNotesFileName(date));
                string rawName = $"Notes_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}";
                string fallbackPath = Path.Combine(dataDirectory, rawName);

                // Do not save an empty txt file (UR requirement)
                if (string.IsNullOrWhiteSpace(content))
                {
                    if (File.Exists(filePath))
                    {
                        try { File.Delete(filePath); } catch { }
                    }
                    if (File.Exists(fallbackPath))
                    {
                        try { File.Delete(fallbackPath); } catch { }
                    }
                    return;
                }

                string tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, content);
                File.Move(tempPath, filePath, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write notes file: {ex.Message}");
            }
        }

        public void MigrateData(string sourceDir, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return;
            if (string.IsNullOrWhiteSpace(targetDir)) return;

            EnsureDirectoryExists(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir, "Task_*"))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                {
                    File.Copy(file, dest);
                }
            }

            foreach (var file in Directory.GetFiles(sourceDir, "Notes_*"))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                {
                    File.Copy(file, dest);
                }
            }
        }

        public void MergeTaskIntoDate(string activeDataDirectory, DateTime date, TaskItem task)
        {
            if (string.IsNullOrWhiteSpace(activeDataDirectory)) return;
            EnsureDirectoryExists(activeDataDirectory);

            var activeTasks = LoadTasksForDate(activeDataDirectory, date);
            bool exists = activeTasks.Any(t =>
                string.Equals(t.Id, task.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Title.Trim(), task.Title.Trim(), StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                task.OrderIndex = activeTasks.Count + 1;
                activeTasks.Add(task);
                SaveTasksForDate(activeDataDirectory, date, activeTasks);
            }
        }

        public void MergeNotesIntoDate(string activeDataDirectory, DateTime date, string note)
        {
            if (string.IsNullOrWhiteSpace(activeDataDirectory) || string.IsNullOrWhiteSpace(note)) return;
            EnsureDirectoryExists(activeDataDirectory);

            string existing = LoadNotesForDate(activeDataDirectory, date);
            if (string.IsNullOrWhiteSpace(existing))
            {
                SaveNotesForDate(activeDataDirectory, date, note);
            }
            else if (!existing.Contains(note, StringComparison.OrdinalIgnoreCase))
            {
                SaveNotesForDate(activeDataDirectory, date, existing + Environment.NewLine + note);
            }
        }

        public static List<TaskItem> ParseTextTaskFile(string filePath)
        {
            var result = new List<TaskItem>();
            if (!File.Exists(filePath)) return result;

            var lines = File.ReadAllLines(filePath);
            TaskItem? current = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("---") || line.StartsWith("==="))
                {
                    if (current != null && !string.IsNullOrWhiteSpace(current.Title))
                    {
                        result.Add(current);
                        current = null;
                    }
                    continue;
                }

                if (line.StartsWith("[ ]") || line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase) || line.StartsWith("[X]"))
                {
                    if (current != null && !string.IsNullOrWhiteSpace(current.Title))
                    {
                        result.Add(current);
                    }
                    current = new TaskItem
                    {
                        IsCompleted = !line.StartsWith("[ ]")
                    };

                    string afterBracket = line.Substring(3).Trim();
                    if (afterBracket.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Title = afterBracket.Substring(6).Trim();
                    }
                    else
                    {
                        current.Title = afterBracket;
                    }
                    continue;
                }

                if (current == null)
                {
                    current = new TaskItem();
                }

                if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
                {
                    current.Title = line.Substring(6).Trim();
                }
                else if (line.StartsWith("Priority:", StringComparison.OrdinalIgnoreCase))
                {
                    string prio = line.Substring(9).Trim();
                    current.IsHighPriority = prio.Contains("high", StringComparison.OrdinalIgnoreCase) || prio.Equals("1");
                }
                else if (line.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
                {
                    string id = line.Substring(3).Trim();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        current.Id = "legacy-txt-" + id;
                    }
                }
                else if (line.StartsWith("Notes:", StringComparison.OrdinalIgnoreCase))
                {
                    string notes = line.Substring(6).Trim();
                    if (!notes.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Details = notes;
                    }
                }
                else if (line.StartsWith("Details:", StringComparison.OrdinalIgnoreCase))
                {
                    current.Details = line.Substring(8).Trim();
                }
            }

            if (current != null && !string.IsNullOrWhiteSpace(current.Title))
            {
                result.Add(current);
            }

            return result;
        }

        public void ImportSQLiteDatabase(string dbPath, string activeDataDirectory)
        {
            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(activeDataDirectory)) return;

            try
            {
                using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                conn.Open();

                // 1. Tasks table
                bool hasTasksTable = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='tasks';";
                    var val = cmd.ExecuteScalar();
                    hasTasksTable = val != null && Convert.ToInt32(val) > 0;
                }

                if (hasTasksTable)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT * FROM tasks;";
                    using var reader = cmd.ExecuteReader();
                    int dateCol = -1, titleCol = -1, detailsCol = -1, statusCol = -1, prioCol = -1;
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string name = reader.GetName(i).ToLowerInvariant();
                        if (name == "date") dateCol = i;
                        else if (name == "title") titleCol = i;
                        else if (name is "details" or "notes" or "description") detailsCol = i;
                        else if (name is "status" or "is_completed" or "isdone") statusCol = i;
                        else if (name is "priority" or "prio") prioCol = i;
                    }

                    while (reader.Read())
                    {
                        string dateStr = dateCol >= 0 && !reader.IsDBNull(dateCol) ? reader.GetValue(dateCol).ToString() ?? "" : "";
                        string title = titleCol >= 0 && !reader.IsDBNull(titleCol) ? reader.GetValue(titleCol).ToString() ?? "" : "";
                        string details = detailsCol >= 0 && !reader.IsDBNull(detailsCol) ? reader.GetValue(detailsCol).ToString() ?? "" : "";
                        string status = statusCol >= 0 && !reader.IsDBNull(statusCol) ? reader.GetValue(statusCol).ToString() ?? "" : "";
                        string priority = prioCol >= 0 && !reader.IsDBNull(prioCol) ? reader.GetValue(prioCol).ToString() ?? "" : "";

                        if (string.IsNullOrWhiteSpace(title)) continue;

                        DateTime taskDate = DateTime.Today;
                        string[] formats = { "yyyy-MM-dd", "yyyyMMdd", "MMMM d yyyy", "MMMM d, yyyy" };
                        if (!DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out taskDate))
                        {
                            DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out taskDate);
                        }

                        var task = new TaskItem
                        {
                            Id = "legacy-sqlite-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                            Title = title.Trim(),
                            Details = details.Trim(),
                            IsCompleted = status.Equals("completed", StringComparison.OrdinalIgnoreCase),
                            IsHighPriority = priority.Contains("high", StringComparison.OrdinalIgnoreCase) || priority.Equals("1")
                        };

                        MergeTaskIntoDate(activeDataDirectory, taskDate, task);
                    }
                }

                // 2. Daily Notes table
                bool hasNotesTable = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='daily_notes';";
                    var val = cmd.ExecuteScalar();
                    hasNotesTable = val != null && Convert.ToInt32(val) > 0;
                }

                if (hasNotesTable)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT * FROM daily_notes;";
                    using var reader = cmd.ExecuteReader();
                    int dateCol = -1, contentCol = -1;
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string name = reader.GetName(i).ToLowerInvariant();
                        if (name == "date") dateCol = i;
                        else if (name is "content" or "note" or "notes" or "text") contentCol = i;
                    }

                    while (reader.Read())
                    {
                        string dateStr = dateCol >= 0 && !reader.IsDBNull(dateCol) ? reader.GetValue(dateCol).ToString() ?? "" : "";
                        string note = contentCol >= 0 && !reader.IsDBNull(contentCol) ? reader.GetValue(contentCol).ToString() ?? "" : "";

                        if (string.IsNullOrWhiteSpace(note)) continue;

                        DateTime noteDate = DateTime.Today;
                        string[] formats = { "yyyy-MM-dd", "yyyyMMdd", "MMMM d yyyy", "MMMM d, yyyy" };
                        if (!DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out noteDate))
                        {
                            DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out noteDate);
                        }

                        MergeNotesIntoDate(activeDataDirectory, noteDate, note.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error importing SQLite DB {dbPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Automatically discovers and captures tasks and notes from older versions or alternative locations
        /// (e.g. Documents\Tasks, Documents\TasksStorage, Documents\PrimeTasks, Documents\TasksApp, portable EXEs, %LOCALAPPDATA%),
        /// merging them into the active data directory.
        /// </summary>
        public void CaptureTasksFromOtherVersions(string activeDataDirectory, IEnumerable<string>? additionalLegacyDirs = null)
        {
            if (string.IsNullOrWhiteSpace(activeDataDirectory)) return;

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var potentialLegacyDirs = new List<string>
            {
                Path.Combine(docs, "Tasks"),
                Path.Combine(docs, "TasksStorage"),
                Path.Combine(docs, "TasksApp"),
                Path.Combine(docs, "PrimeTasks"),
                Path.Combine(docs, "TasksData"),
                Path.Combine(baseDir, "Tasks"),
                Path.Combine(baseDir, "TasksStorage"),
                Path.Combine(baseDir, "TasksData"),
                Path.Combine(localAppData, "TasksApp"),
                Path.Combine(localAppData, "TasksApp", "TasksData"),
                Path.Combine(appData, "TasksApp"),
                Path.Combine(appData, "TasksApp", "TasksData")
            };

            if (additionalLegacyDirs != null)
            {
                potentialLegacyDirs.AddRange(additionalLegacyDirs);
            }

            EnsureDirectoryExists(activeDataDirectory);
            string activeNormalized = Path.GetFullPath(activeDataDirectory).TrimEnd('\\');

            foreach (var legacyDir in potentialLegacyDirs)
            {
                if (!Directory.Exists(legacyDir)) continue;
                string legacyNormalized = Path.GetFullPath(legacyDir).TrimEnd('\\');
                if (string.Equals(legacyNormalized, activeNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    // 1. Process JSON Task_* files
                    foreach (var legacyFile in Directory.GetFiles(legacyDir, "Task_*.json"))
                    {
                        if (legacyFile.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        if (TryParseTaskFileDate(legacyFile, out DateTime fileDate))
                        {
                            var legacyTasks = LoadTasksForDate(legacyDir, fileDate);
                            foreach (var lTask in legacyTasks)
                            {
                                MergeTaskIntoDate(activeDataDirectory, fileDate, lTask);
                            }
                        }
                    }

                    // 2. Process plain text Task_* files (Task_*.txt)
                    foreach (var legacyTxtFile in Directory.GetFiles(legacyDir, "Task_*.txt"))
                    {
                        if (TryParseTaskFileDate(legacyTxtFile, out DateTime fileDate))
                        {
                            var parsedTasks = ParseTextTaskFile(legacyTxtFile);
                            foreach (var pTask in parsedTasks)
                            {
                                MergeTaskIntoDate(activeDataDirectory, fileDate, pTask);
                            }
                        }
                    }

                    // 3. Process SQLite .db files (e.g. prime_tasks.db, tasks.db)
                    foreach (var dbFile in Directory.GetFiles(legacyDir, "*.db"))
                    {
                        ImportSQLiteDatabase(dbFile, activeDataDirectory);
                    }

                    // 4. Process Notes_* files (.txt and others)
                    foreach (var legacyNotesFile in Directory.GetFiles(legacyDir, "Notes_*"))
                    {
                        if (legacyNotesFile.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        if (TryParseTaskFileDate(legacyNotesFile, out DateTime fileDate))
                        {
                            string noteContent = File.ReadAllText(legacyNotesFile).Trim();
                            if (!string.IsNullOrWhiteSpace(noteContent))
                            {
                                MergeNotesIntoDate(activeDataDirectory, fileDate, noteContent);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error capturing legacy tasks from {legacyDir}: {ex.Message}");
                }
            }
        }

        public static bool TryParseTaskFileDate(string filePath, out DateTime date)
        {
            date = default;
            string fileName = Path.GetFileName(filePath);
            int underscoreIdx = fileName.IndexOf('_');
            if (underscoreIdx < 0)
            {
                return false;
            }

            string prefix = fileName.Substring(0, underscoreIdx);
            if (!prefix.Equals("Task", StringComparison.OrdinalIgnoreCase) &&
                !prefix.Equals("Notes", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string datePart = fileName.Substring(underscoreIdx + 1);
            if (datePart.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                datePart = datePart.Substring(0, datePart.Length - 5);
            }
            else if (datePart.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                datePart = datePart.Substring(0, datePart.Length - 4);
            }
            else if (datePart.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] formats = { "MMMM d yyyy", "MMMM d, yyyy", "yyyy-MM-dd", "yyyyMMdd" };
            return DateTime.TryParseExact(datePart, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        public List<TaskItem> CarryOverPendingTasks(string dataDirectory, DateTime targetDate)
        {
            var carriedOver = new List<TaskItem>();
            if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
            {
                return carriedOver;
            }

            try
            {
                var files = Directory.GetFiles(dataDirectory, "Task_*.json");
                var pastFiles = new List<(DateTime Date, string Path)>();

                foreach (var file in files)
                {
                    if (TryParseTaskFileDate(file, out DateTime fileDate))
                    {
                        if (fileDate.Date < targetDate.Date)
                        {
                            pastFiles.Add((fileDate.Date, file));
                        }
                    }
                }

                // Sort chronologically (oldest past days first)
                pastFiles.Sort((a, b) => a.Date.CompareTo(b.Date));

                foreach (var (pastDate, pastFilePath) in pastFiles)
                {
                    var pastTasks = LoadTasksForDate(dataDirectory, pastDate);
                    var pending = pastTasks.Where(t => !t.IsCompleted).ToList();

                    if (pending.Count > 0)
                    {
                        bool updatedPast = false;
                        foreach (var p in pending)
                        {
                            if (string.IsNullOrEmpty(p.CarriedForwardTo))
                            {
                                p.CarriedForwardTo = targetDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                                updatedPast = true;
                            }

                            var targetItem = p.Clone();
                            targetItem.CarriedOverFrom = pastDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                            targetItem.CarriedForwardTo = null;
                            carriedOver.Add(targetItem);
                        }

                        // Preserve historical day records so past days are NEVER deleted!
                        if (updatedPast)
                        {
                            SaveTasksForDate(dataDirectory, pastDate, pastTasks);
                        }
                    }
                }

                if (carriedOver.Count > 0)
                {
                    var targetTasks = LoadTasksForDate(dataDirectory, targetDate);
                    bool addedAny = false;

                    foreach (var item in carriedOver)
                    {
                        bool exists = targetTasks.Any(t =>
                            string.Equals(t.Id, item.Id, StringComparison.OrdinalIgnoreCase) ||
                            (string.Equals(t.Title.Trim(), item.Title.Trim(), StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(t.CarriedOverFrom, item.CarriedOverFrom, StringComparison.OrdinalIgnoreCase)));

                        if (!exists)
                        {
                            targetTasks.Add(item);
                            addedAny = true;
                        }
                    }

                    if (addedAny)
                    {
                        for (int i = 0; i < targetTasks.Count; i++)
                        {
                            targetTasks[i].OrderIndex = i + 1;
                        }

                        SaveTasksForDate(dataDirectory, targetDate, targetTasks);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to carry over pending tasks: {ex.Message}");
            }

            return carriedOver;
        }
    }
}
