using System;
using System.Text.Json.Serialization;

namespace TasksApp.Models
{
    public class AppSettings
    {
        [JsonPropertyName("dataDirectory")]
        public string DataDirectory { get; set; } = string.Empty;

        [JsonPropertyName("windowLeft")]
        public double WindowLeft { get; set; } = double.NaN;

        [JsonPropertyName("windowTop")]
        public double WindowTop { get; set; } = double.NaN;

        [JsonPropertyName("windowWidth")]
        public double WindowWidth { get; set; } = 1040;

        [JsonPropertyName("windowHeight")]
        public double WindowHeight { get; set; } = 680;

        [JsonPropertyName("isMaximized")]
        public bool IsMaximized { get; set; } = false;

        [JsonPropertyName("alwaysOnTop")]
        public bool AlwaysOnTop { get; set; } = false;

        [JsonPropertyName("remindersEnabled")]
        public bool RemindersEnabled { get; set; } = true;

        [JsonPropertyName("reminderIntervalSeconds")]
        public int ReminderIntervalSeconds { get; set; } = 1800; // 30 minutes default

        [JsonPropertyName("hasCompletedFirstRun")]
        public bool HasCompletedFirstRun { get; set; } = false;

        [JsonPropertyName("hasShownBackgroundCloseNotification")]
        public bool HasShownBackgroundCloseNotification { get; set; } = false;

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "Default";

        [JsonPropertyName("focusTimerFreeDrag")]
        public bool FocusTimerFreeDrag { get; set; } = true;

        [JsonPropertyName("focusTimerLeft")]
        public double FocusTimerLeft { get; set; } = double.NaN;

        [JsonPropertyName("focusTimerTop")]
        public double FocusTimerTop { get; set; } = double.NaN;

        [JsonPropertyName("focusTimerOpacity")]
        public double FocusTimerOpacity { get; set; } = 100;

        [JsonPropertyName("focusTimerClockFontSize")]
        public int FocusTimerClockFontSize { get; set; } = 34;

        [JsonPropertyName("focusTimerTaskFontSize")]
        public int FocusTimerTaskFontSize { get; set; } = 12;
    }
}
