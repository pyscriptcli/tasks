using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TasksApp.Models;

namespace TasksApp.Services
{
    public class StorageService
    {
        private sealed class DeletedTaskMarker
        {
            public string Id { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
        }

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

        private string GetDeletedTasksFileName(DateTime date)
        {
            return $"Task_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}.deleted.json";
        }

        private List<DeletedTaskMarker> LoadDeletedTaskMarkers(string dataDirectory, DateTime date)
        {
            string path = Path.Combine(dataDirectory, GetDeletedTasksFileName(date));
            if (!File.Exists(path)) return new List<DeletedTaskMarker>();

            return JsonSerializer.Deserialize<List<DeletedTaskMarker>>(File.ReadAllText(path), JsonOptions)
                ?? new List<DeletedTaskMarker>();
        }

        private void SaveDeletedTaskMarkers(string dataDirectory, DateTime date, List<DeletedTaskMarker> markers)
        {
            string path = Path.Combine(dataDirectory, GetDeletedTasksFileName(date));
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(markers, JsonOptions));
            File.Move(temporaryPath, path, true);
        }

        private static string GetTaskSignature(TaskItem task)
        {
            string identity = string.Join("\n", task.Title.Trim(), task.Details.Trim(), task.IsHighPriority ? "1" : "0");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }

        private static string CreateStableLegacyTaskId(string sourceFilePath, DateTime date, int index, TaskItem task)
        {
            string identity = string.Join("\n",
                Path.GetFullPath(sourceFilePath).ToUpperInvariant(),
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                index.ToString(CultureInfo.InvariantCulture),
                GetTaskSignature(task));
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            return "legacy-" + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
        }

        private static bool HasExplicitTaskId(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return false;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("id", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTaskMarkedDeleted(IEnumerable<DeletedTaskMarker> markers, TaskItem task, bool includeSignature)
        {
            string signature = includeSignature ? GetTaskSignature(task) : string.Empty;
            return markers.Any(marker =>
                string.Equals(marker.Id, task.Id, StringComparison.OrdinalIgnoreCase) ||
                (includeSignature && string.Equals(marker.Signature, signature, StringComparison.Ordinal)));
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
                        using var document = JsonDocument.Parse(json);
                        if (document.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            int itemIndex = 0;
                            foreach (var element in document.RootElement.EnumerateArray())
                            {
                                if (itemIndex >= items.Count) break;
                                if (!HasExplicitTaskId(element))
                                {
                                    items[itemIndex].Id = CreateStableLegacyTaskId(targetPath, date, itemIndex, items[itemIndex]);
                                }
                                itemIndex++;
                            }
                        }

                        var deletedMarkers = LoadDeletedTaskMarkers(dataDirectory, date);
                        result = items
                            .Where(task => !IsTaskMarkedDeleted(deletedMarkers, task, includeSignature: false))
                            .OrderBy(t => t.OrderIndex)
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read tasks file: {ex.Message}");
                }
            }

            return result;
        }

        public bool DeleteTaskForDate(string dataDirectory, DateTime date, TaskItem task)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory) || task == null) return false;

            try
            {
                EnsureDirectoryExists(dataDirectory);
                var markers = LoadDeletedTaskMarkers(dataDirectory, date);
                if (!markers.Any(marker => string.Equals(marker.Id, task.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    markers.Add(new DeletedTaskMarker
                    {
                        Id = task.Id,
                        Signature = GetTaskSignature(task)
                    });
                    SaveDeletedTaskMarkers(dataDirectory, date, markers);
                }

                // The marker is authoritative. If removing the old task file fails,
                // LoadTasksForDate still filters the deleted identity.
                var remainingTasks = LoadTasksForDate(dataDirectory, date);
                SaveTasksForDate(dataDirectory, date, remainingTasks);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to record task deletion: {ex.Message}");
                return false;
            }
        }

        public void SaveTasksForDate(string dataDirectory, DateTime date, IEnumerable<TaskItem> tasks)
        {
            TrySaveTasksForDate(dataDirectory, date, tasks);
        }

        private bool TrySaveTasksForDate(string dataDirectory, DateTime date, IEnumerable<TaskItem> tasks)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) return false;

            try
            {
                EnsureDirectoryExists(dataDirectory);
                string filePath = Path.Combine(dataDirectory, GetTaskFileName(date));
                string rawName = $"Task_{date.ToString("MMMM d yyyy", CultureInfo.InvariantCulture)}";
                string fallbackPath = Path.Combine(dataDirectory, rawName);

                var taskList = tasks.ToList();
                if (taskList.Count == 0)
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                    if (File.Exists(fallbackPath)) File.Delete(fallbackPath);
                    return true;
                }

                for (int i = 0; i < taskList.Count; i++)
                {
                    taskList[i].OrderIndex = i + 1;
                }

                string json = JsonSerializer.Serialize(taskList, JsonOptions);
                string tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, true);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write tasks file: {ex.Message}");
                return false;
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
                if (TryParseDeletedTasksFileDate(file, out var deletedDate))
                {
                    var sourceMarkers = LoadDeletedTaskMarkers(sourceDir, deletedDate);
                    var targetMarkers = LoadDeletedTaskMarkers(targetDir, deletedDate);
                    foreach (var marker in sourceMarkers)
                    {
                        var existing = targetMarkers.FirstOrDefault(item =>
                            string.Equals(item.Id, marker.Id, StringComparison.OrdinalIgnoreCase));
                        if (existing == null)
                        {
                            targetMarkers.Add(marker);
                        }
                        else if (string.IsNullOrWhiteSpace(existing.Signature))
                        {
                            existing.Signature = marker.Signature;
                        }
                    }
                    SaveDeletedTaskMarkers(targetDir, deletedDate, targetMarkers);
                    continue;
                }

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

            var deletedMarkers = LoadDeletedTaskMarkers(activeDataDirectory, date);
            if (IsTaskMarkedDeleted(deletedMarkers, task, includeSignature: true)) return;

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

            if (TryParseTaskFileDate(filePath, out var taskDate))
            {
                for (int i = 0; i < result.Count; i++)
                {
                    if (!result[i].Id.StartsWith("legacy-txt-", StringComparison.OrdinalIgnoreCase))
                    {
                        result[i].Id = CreateStableLegacyTaskId(filePath, taskDate, i, result[i]);
                    }
                }
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
                    int rowIndex = 0;
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
                        int currentRowIndex = rowIndex++;
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
                            Id = string.Empty,
                            Title = title.Trim(),
                            Details = details.Trim(),
                            IsCompleted = status.Equals("completed", StringComparison.OrdinalIgnoreCase),
                            IsHighPriority = priority.Contains("high", StringComparison.OrdinalIgnoreCase) || priority.Equals("1")
                        };

                        task.Id = CreateStableLegacyTaskId(dbPath, taskDate, currentRowIndex, task);

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

        private void ImportLegacyTasksJson(string filePath, string activeDataDirectory)
        {
            if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(activeDataDirectory)) return;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(filePath));
                var root = document.RootElement;
                var fallbackDate = DateTime.Today;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    ImportTaskArray(root, activeDataDirectory, fallbackDate);
                    return;
                }

                if (root.ValueKind != JsonValueKind.Object) return;

                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name.Equals("tasks", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("items", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("taskList", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            var date = ReadDateProperty(root) ?? fallbackDate;
                            ImportTaskArray(property.Value, activeDataDirectory, date);
                            return;
                        }
                    }
                }

                // Some older exports group task arrays under date-named properties.
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array && TryParseLegacyDate(property.Name, out var date))
                    {
                        ImportTaskArray(property.Value, activeDataDirectory, date);
                    }
                }

                if (TryReadTask(root, out var singleTask))
                {
                    MergeTaskIntoDate(activeDataDirectory, ReadDateProperty(root) ?? fallbackDate, singleTask);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to import legacy tasks file {filePath}: {ex.Message}");
            }
        }

        private void ImportTaskArray(JsonElement tasks, string activeDataDirectory, DateTime fallbackDate)
        {
            foreach (var item in tasks.EnumerateArray())
            {
                if (!TryReadTask(item, out var task)) continue;
                var taskDate = ReadDateProperty(item) ?? fallbackDate;
                MergeTaskIntoDate(activeDataDirectory, taskDate, task);
            }
        }

        private static bool TryReadTask(JsonElement element, out TaskItem task)
        {
            task = new TaskItem();
            if (element.ValueKind != JsonValueKind.Object || !TryGetProperty(element, "title", out _)) return false;
            task = JsonSerializer.Deserialize<TaskItem>(element.GetRawText(), JsonOptions) ?? new TaskItem();
            return !string.IsNullOrWhiteSpace(task.Title);
        }

        private static DateTime? ReadDateProperty(JsonElement element)
        {
            foreach (var name in new[] { "taskDate", "dueDate", "date" })
            {
                if (TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return date.Date;
                }
            }
            return null;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        private static bool TryParseLegacyDate(string value, out DateTime date)
        {
            string[] formats = { "MMMM d yyyy", "MMMM d, yyyy", "yyyy-MM-dd", "yyyyMMdd" };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>
        /// Automatically discovers and captures tasks and notes from older versions or alternative locations
        /// (e.g. Documents\Tasks, Documents\TasksStorage, Documents\PrimeTasks, Documents\TasksApp, portable EXEs, %LOCALAPPDATA%),
        /// merging them into the active data directory.
        /// </summary>
        public void CaptureTasksFromOtherVersions(string activeDataDirectory, IEnumerable<string>? additionalLegacyDirs = null)
        {
            if (string.IsNullOrWhiteSpace(activeDataDirectory)) return;
            EnsureDirectoryExists(activeDataDirectory);

            // v1-v4 stored all tasks together in tasks.json instead of one file per date.
            ImportLegacyTasksJson(Path.Combine(activeDataDirectory, "tasks.json"), activeDataDirectory);
            ImportLegacyTextTaskFiles(activeDataDirectory, activeDataDirectory);

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
                    ImportLegacyTasksJson(Path.Combine(legacyDir, "tasks.json"), activeDataDirectory);

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
                    ImportLegacyTextTaskFiles(legacyDir, activeDataDirectory);

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

        private static bool TryParseDeletedTasksFileDate(string filePath, out DateTime date)
        {
            date = default;
            string fileName = Path.GetFileName(filePath);
            const string prefix = "Task_";
            const string suffix = ".deleted.json";
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string datePart = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
            string[] formats = { "yyyy-MM-dd", "MMMM d yyyy" };
            return DateTime.TryParseExact(datePart, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private void ImportLegacyTextTaskFiles(string sourceDirectory, string activeDataDirectory)
        {
            foreach (var taskFile in Directory.GetFiles(sourceDirectory, "Task_*.txt"))
            {
                if (!TryParseTaskFileDate(taskFile, out var taskDate)) continue;
                foreach (var task in ParseTextTaskFile(taskFile))
                {
                    MergeTaskIntoDate(activeDataDirectory, taskDate, task);
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
                var sourceUpdates = new List<(DateTime Date, List<TaskItem> Tasks)>();
                var deletedMarkers = LoadDeletedTaskMarkers(dataDirectory, targetDate);

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
                            // A source task is forwarded only once. The copy on the target
                            // date has no CarriedForwardTo value, so it can roll forward on
                            // a later day without resurrecting a deleted earlier copy.
                            if (!string.IsNullOrEmpty(p.CarriedForwardTo))
                            {
                                continue;
                            }

                            p.CarriedForwardTo = targetDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                            updatedPast = true;

                            var targetItem = p.Clone();
                            targetItem.CarriedOverFrom = pastDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                            targetItem.CarriedForwardTo = null;

                            if (!IsTaskMarkedDeleted(deletedMarkers, targetItem, includeSignature: true))
                            {
                                carriedOver.Add(targetItem);
                            }
                        }

                        if (updatedPast)
                        {
                            sourceUpdates.Add((pastDate, pastTasks));
                        }
                    }
                }

                bool targetSaved = true;
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

                        targetSaved = TrySaveTasksForDate(dataDirectory, targetDate, targetTasks);
                    }
                }

                // Persist source markers only after the destination is safely stored.
                // If a source write fails, the next carry-over pass sees the existing
                // destination item and can retry without creating a duplicate.
                if (targetSaved)
                {
                    foreach (var (sourceDate, sourceTasks) in sourceUpdates)
                    {
                        TrySaveTasksForDate(dataDirectory, sourceDate, sourceTasks);
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
