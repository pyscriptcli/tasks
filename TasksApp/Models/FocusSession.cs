using System;
using System.Text.Json.Serialization;

namespace TasksApp.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FocusSessionStatus
{
    Active,
    Paused,
    Completed
}

public sealed class FocusSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("taskDate")]
    public DateTime TaskDate { get; set; }

    [JsonPropertyName("taskTitle")]
    public string TaskTitle { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public FocusSessionStatus Status { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTimeOffset? FinishedAt { get; set; }

    [JsonPropertyName("focusedDurationMs")]
    public long FocusedDurationMs { get; set; }

    [JsonPropertyName("currentRunStartedAt")]
    public DateTimeOffset? CurrentRunStartedAt { get; set; }
}
