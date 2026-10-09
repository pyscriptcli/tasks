using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TasksApp.Models;

namespace TasksApp.Services
{
    public class StorageService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
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

        public static bool TryParseTaskFileDate(string filePath, out DateTime date)
        {
            date = default;
            string fileName = Path.GetFileName(filePath);
            if (!fileName.StartsWith("Task_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string datePart = fileName.Substring(5); // remove "Task_"
            if (datePart.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                datePart = datePart.Substring(0, datePart.Length - 5);
            }
            else if (datePart.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return DateTime.TryParseExact(datePart, "MMMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
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
                var files = Directory.GetFiles(dataDirectory, "Task_*");
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
                    var tasks = LoadTasksForDate(dataDirectory, pastDate);
                    var pending = tasks.Where(t => !t.IsCompleted).ToList();
                    var completed = tasks.Where(t => t.IsCompleted).ToList();

                    if (pending.Count > 0)
                    {
                        foreach (var p in pending)
                        {
                            if (string.IsNullOrEmpty(p.CarriedOverFrom))
                            {
                                p.CarriedOverFrom = pastDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                            }
                            carriedOver.Add(p);
                        }

                        if (completed.Count > 0)
                        {
                            SaveTasksForDate(dataDirectory, pastDate, completed);
                        }
                        else
                        {
                            // No tasks remain on this past day; clean up file
                            try { File.Delete(pastFilePath); } catch { }
                            string jsonName = Path.Combine(dataDirectory, GetTaskFileName(pastDate));
                            try { if (File.Exists(jsonName)) File.Delete(jsonName); } catch { }
                        }
                    }
                }

                if (carriedOver.Count > 0)
                {
                    var targetTasks = LoadTasksForDate(dataDirectory, targetDate);
                    foreach (var item in carriedOver)
                    {
                        if (!targetTasks.Any(t => t.Id == item.Id))
                        {
                            targetTasks.Add(item);
                        }
                    }

                    for (int i = 0; i < targetTasks.Count; i++)
                    {
                        targetTasks[i].OrderIndex = i + 1;
                    }

                    SaveTasksForDate(dataDirectory, targetDate, targetTasks);
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
