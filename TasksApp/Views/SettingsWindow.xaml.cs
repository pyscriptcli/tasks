using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TasksApp.Models;
using TasksApp.Services;

namespace TasksApp.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly StorageService _storageService;
        private readonly UpdateService _updateService = new UpdateService();
        private UpdateInfo? _latestUpdateInfo;

        public AppSettings UpdatedSettings { get; private set; }
        public bool StoragePathChanged { get; private set; }
        public bool MigrateFilesRequested { get; private set; }
        public string OriginalStoragePath { get; private set; }

        public SettingsWindow(AppSettings currentSettings, StorageService storageService)
        {
            InitializeComponent();
            _storageService = storageService;
            OriginalStoragePath = currentSettings.DataDirectory;

            TxtCurrentVersion.Text = $"v{UpdateService.CurrentVersion}";

            // Clone settings
            UpdatedSettings = new AppSettings
            {
                DataDirectory = currentSettings.DataDirectory,
                RemindersEnabled = currentSettings.RemindersEnabled,
                ReminderIntervalSeconds = currentSettings.ReminderIntervalSeconds,
                AlwaysOnTop = currentSettings.AlwaysOnTop,
                HasCompletedFirstRun = currentSettings.HasCompletedFirstRun,
                HasShownBackgroundCloseNotification = currentSettings.HasShownBackgroundCloseNotification,
                WindowLeft = currentSettings.WindowLeft,
                WindowTop = currentSettings.WindowTop,
                WindowWidth = currentSettings.WindowWidth,
                WindowHeight = currentSettings.WindowHeight,
                IsMaximized = currentSettings.IsMaximized
            };

            TxtStoragePath.Text = UpdatedSettings.DataDirectory;
            ChkRemindersEnabled.IsChecked = UpdatedSettings.RemindersEnabled;
            ChkAlwaysOnTop.IsChecked = UpdatedSettings.AlwaysOnTop;

            PopulateIntervalFields(UpdatedSettings.ReminderIntervalSeconds);
        }

        private void PopulateIntervalFields(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;

            TxtHours.Text = hours.ToString();
            TxtMinutes.Text = minutes.ToString();
            TxtSeconds.Text = seconds.ToString();
        }

        private void OnBrowseStorageClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Select New Storage Location",
                    InitialDirectory = Directory.Exists(TxtStoragePath.Text) ? TxtStoragePath.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (dialog.ShowDialog() == true)
                {
                    TxtStoragePath.Text = dialog.FolderName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open folder picker: {ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnToggleRemindersLabelClick(object sender, MouseButtonEventArgs e)
        {
            ChkRemindersEnabled.IsChecked = !(ChkRemindersEnabled.IsChecked ?? false);
        }

        private void OnToggleAlwaysOnTopLabelClick(object sender, MouseButtonEventArgs e)
        {
            ChkAlwaysOnTop.IsChecked = !(ChkAlwaysOnTop.IsChecked ?? false);
        }

        private void OnPreset15MinClick(object sender, RoutedEventArgs e) => PopulateIntervalFields(15 * 60);
        private void OnPreset30MinClick(object sender, RoutedEventArgs e) => PopulateIntervalFields(30 * 60);
        private void OnPreset1HourClick(object sender, RoutedEventArgs e) => PopulateIntervalFields(60 * 60);
        private void OnPreset2HoursClick(object sender, RoutedEventArgs e) => PopulateIntervalFields(120 * 60);
        private void OnPreset4HoursClick(object sender, RoutedEventArgs e) => PopulateIntervalFields(240 * 60);

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            TxtIntervalError.Visibility = Visibility.Collapsed;

            // Validate interval (UR-027: Protected against bad interval values such as zero)
            if (!int.TryParse(TxtHours.Text.Trim(), out int h) || h < 0)
            {
                ShowIntervalError("Hours must be a valid non-negative number.");
                return;
            }
            if (!int.TryParse(TxtMinutes.Text.Trim(), out int m) || m < 0)
            {
                ShowIntervalError("Minutes must be a valid non-negative number.");
                return;
            }
            if (!int.TryParse(TxtSeconds.Text.Trim(), out int s) || s < 0)
            {
                ShowIntervalError("Seconds must be a valid non-negative number.");
                return;
            }

            int totalSeconds = (h * 3600) + (m * 60) + s;
            if (totalSeconds <= 0)
            {
                ShowIntervalError("Interval cannot be zero. Please specify an interval of at least 10 seconds.");
                return;
            }
            if (totalSeconds < 10)
            {
                ShowIntervalError("Minimum interval is 10 seconds.");
                return;
            }

            string newPath = TxtStoragePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(newPath))
            {
                MessageBox.Show("Please enter a valid storage path.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!Directory.Exists(newPath))
                {
                    Directory.CreateDirectory(newPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not access target storage directory: {ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool pathChanged = !string.Equals(OriginalStoragePath, newPath, StringComparison.OrdinalIgnoreCase);
            if (pathChanged && (ChkMigrateFiles.IsChecked ?? false))
            {
                MigrateFilesRequested = true;
                _storageService.MigrateData(OriginalStoragePath, newPath);
            }

            StoragePathChanged = pathChanged;
            UpdatedSettings.DataDirectory = newPath;
            UpdatedSettings.RemindersEnabled = ChkRemindersEnabled.IsChecked ?? true;
            UpdatedSettings.ReminderIntervalSeconds = totalSeconds;
            UpdatedSettings.AlwaysOnTop = ChkAlwaysOnTop.IsChecked ?? false;

            DialogResult = true;
            Close();
        }

        private void ShowIntervalError(string message)
        {
            TxtIntervalError.Text = message;
            TxtIntervalError.Visibility = Visibility.Visible;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            BtnCheckUpdates.Content = "CHECKING...";
            TxtUpdateStatus.Visibility = Visibility.Visible;
            TxtUpdateStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x33, 0x66));
            TxtUpdateStatus.Text = "Connecting to GitHub (pyscriptcli/tasks)...";
            PnlUpdateAvailable.Visibility = Visibility.Collapsed;
            PbUpdateProgress.Visibility = Visibility.Collapsed;

            try
            {
                _latestUpdateInfo = await _updateService.CheckForUpdatesAsync();

                if (_latestUpdateInfo.UpdateAvailable)
                {
                    TxtUpdateStatus.Text = _latestUpdateInfo.StatusMessage;
                    TxtNewVersionTag.Text = string.IsNullOrWhiteSpace(_latestUpdateInfo.LatestVersion) ? "Latest Version" : _latestUpdateInfo.LatestVersion;
                    TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(_latestUpdateInfo.ReleaseNotes) 
                        ? (_latestUpdateInfo.ReleaseTitle ?? "New release available.") 
                        : _latestUpdateInfo.ReleaseNotes;
                    PnlUpdateAvailable.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtUpdateStatus.Text = _latestUpdateInfo.StatusMessage;
                    PnlUpdateAvailable.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                TxtUpdateStatus.Text = $"Error checking for updates: {ex.Message}";
            }
            finally
            {
                BtnCheckUpdates.IsEnabled = true;
                BtnCheckUpdates.Content = "CHECK FOR UPDATES";
            }
        }

        private async void OnInstallUpdateClick(object sender, RoutedEventArgs e)
        {
            if (_latestUpdateInfo == null || !_latestUpdateInfo.UpdateAvailable)
                return;

            if (string.IsNullOrWhiteSpace(_latestUpdateInfo.DownloadUrl))
            {
                // Fallback: open release page if direct asset isn't attached
                var res = MessageBox.Show(
                    "Direct executable asset download is not found in this release. Would you like to visit the release page on GitHub?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes)
                {
                    OnViewReleasePageClick(sender, e);
                }
                return;
            }

            BtnInstallUpdate.IsEnabled = false;
            BtnInstallUpdate.Content = "DOWNLOADING...";
            PbUpdateProgress.Value = 0;
            PbUpdateProgress.Visibility = Visibility.Visible;
            TxtUpdateStatus.Text = "Downloading update from GitHub...";

            try
            {
                var progressReporter = new Progress<double>(percent =>
                {
                    PbUpdateProgress.Value = percent * 100;
                    TxtUpdateStatus.Text = $"Downloading update... {Math.Round(percent * 100):0}%";
                });

                string tempExe = await _updateService.DownloadUpdateAsync(_latestUpdateInfo.DownloadUrl, progressReporter);

                TxtUpdateStatus.Text = "Update downloaded successfully. Restarting app...";

                var confirm = MessageBox.Show(
                    $"Update {_latestUpdateInfo.LatestVersion} has been downloaded.\n\nThe application will now close, update, and restart. Continue?",
                    "Install Update",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);

                if (confirm == MessageBoxResult.OK)
                {
                    _updateService.ApplyUpdateAndRestart(tempExe);
                }
                else
                {
                    TxtUpdateStatus.Text = $"Update saved to: {tempExe}";
                    BtnInstallUpdate.IsEnabled = true;
                    BtnInstallUpdate.Content = "INSTALL UPDATE";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtUpdateStatus.Text = $"Update error: {ex.Message}";
                BtnInstallUpdate.IsEnabled = true;
                BtnInstallUpdate.Content = "RETRY DOWNLOAD";
            }
        }

        private void OnViewReleasePageClick(object sender, RoutedEventArgs e)
        {
            string url = _latestUpdateInfo?.ReleasePageUrl ?? $"https://github.com/{UpdateService.GitHubRepo}/releases";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
