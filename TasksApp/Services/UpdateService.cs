using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TasksApp.Models;

namespace TasksApp.Services
{
    public class UpdateService
    {
        public const string CurrentVersion = "1.0.0";
        public const string GitHubRepo = "pyscriptcli/tasks";
        private const string LatestReleaseApiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";

        private readonly HttpClient _httpClient;

        public UpdateService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "TasksApp-Updater");
            }
        }

        /// <summary>
        /// Checks GitHub Releases API for a newer version than CurrentVersion.
        /// </summary>
        public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            var result = new UpdateInfo
            {
                CurrentVersion = CurrentVersion,
                ReleasePageUrl = $"https://github.com/{GitHubRepo}/releases"
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    result.UpdateAvailable = false;
                    result.StatusMessage = "You are up to date (no newer releases found on GitHub).";
                    return result;
                }

                if (!response.IsSuccessStatusCode)
                {
                    result.UpdateAvailable = false;
                    result.StatusMessage = $"GitHub returned status {(int)response.StatusCode} ({response.ReasonPhrase}).";
                    return result;
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                string releaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : tagName;
                string body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                string htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? "" : result.ReleasePageUrl;

                result.LatestVersion = tagName;
                result.ReleaseTitle = releaseName;
                result.ReleaseNotes = body;
                result.ReleasePageUrl = htmlUrl;

                // Find Tasks.exe asset
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                        if (assetName.Equals("Tasks.exe", StringComparison.OrdinalIgnoreCase) ||
                            assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (asset.TryGetProperty("browser_download_url", out var dlProp))
                            {
                                result.DownloadUrl = dlProp.GetString() ?? "";
                            }
                            if (asset.TryGetProperty("size", out var sizeProp))
                            {
                                result.AssetSizeBytes = sizeProp.GetInt64();
                            }
                            break;
                        }
                    }
                }

                bool isNewer = IsVersionNewer(tagName, CurrentVersion);
                result.UpdateAvailable = isNewer;

                if (isNewer)
                {
                    result.StatusMessage = $"Update {tagName} is available!";
                }
                else
                {
                    result.StatusMessage = $"You are using the latest version (v{CurrentVersion}).";
                }

                return result;
            }
            catch (HttpRequestException ex)
            {
                result.UpdateAvailable = false;
                result.StatusMessage = $"Could not connect to GitHub: {ex.Message}";
                return result;
            }
            catch (Exception ex)
            {
                result.UpdateAvailable = false;
                result.StatusMessage = $"Update check failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Downloads the updated executable to a temporary file.
        /// </summary>
        public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new ArgumentException("Download URL cannot be empty.", nameof(downloadUrl));

            string tempFilePath = Path.Combine(Path.GetTempPath(), $"Tasks_update_{Guid.NewGuid():N}.exe");

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    double percent = (double)totalRead / totalBytes.Value;
                    progress?.Report(percent);
                }
            }

            return tempFilePath;
        }

        /// <summary>
        /// Applies the update on Windows by replacing the running executable and restarting.
        /// </summary>
        public void ApplyUpdateAndRestart(string downloadedExePath)
        {
            if (!File.Exists(downloadedExePath))
                throw new FileNotFoundException("Downloaded update file not found.", downloadedExePath);

            string? currentExePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExePath))
            {
                currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(currentExePath))
            {
                throw new InvalidOperationException("Could not determine current executable path.");
            }

            if (currentExePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot replace running runtime host in development mode ('dotnet.exe').\nUpdate downloaded to:\n{downloadedExePath}");
            }

            int pid = Environment.ProcessId;
            string escapedCurrent = currentExePath.Replace("'", "''");
            string escapedNew = downloadedExePath.Replace("'", "''");

            // Silent Windows PowerShell script:
            // 1. Waits for current process PID to exit cleanly (up to 15 seconds)
            // 2. Overwrites current executable with new version
            // 3. Starts the new executable
            string script = $"-NoProfile -WindowStyle Hidden -Command \"try {{ Wait-Process -Id {pid} -Timeout 15 -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 400; Move-Item -LiteralPath '{escapedNew}' -Destination '{escapedCurrent}' -Force; Start-Process -FilePath '{escapedCurrent}' }} catch {{ }}\"";

            var startInfo = new ProcessStartInfo("powershell.exe", script)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Process.Start(startInfo);
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Compares two version strings (e.g., "v1.1.0" > "1.0.0").
        /// </summary>
        public static bool IsVersionNewer(string remoteTag, string currentVer)
        {
            if (string.IsNullOrWhiteSpace(remoteTag)) return false;

            string cleanRemote = remoteTag.TrimStart('v', 'V').Trim();
            string cleanCurrent = currentVer.TrimStart('v', 'V').Trim();

            if (Version.TryParse(cleanRemote, out var vRemote) && Version.TryParse(cleanCurrent, out var vCurrent))
            {
                return vRemote > vCurrent;
            }

            return string.Compare(cleanRemote, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}
