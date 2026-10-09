using System;

namespace TasksApp.Models
{
    public class UpdateInfo
    {
        public bool UpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "1.0.0";
        public string LatestVersion { get; set; } = "";
        public string ReleaseTitle { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleasePageUrl { get; set; } = "";
        public string StatusMessage { get; set; } = "";
        public long AssetSizeBytes { get; set; }
    }
}
