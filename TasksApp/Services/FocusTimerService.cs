using System;
using System.Collections.Generic;
using System.Linq;
using TasksApp.Models;

namespace TasksApp.Services;

public sealed class FocusTimerService
{
    private readonly StorageService _storage;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<FocusSession> _sessions;
    private string _dataDirectory;

    public event EventHandler? Changed;

    public FocusSession? CurrentSession => _sessions.LastOrDefault(session =>
        session.Status is FocusSessionStatus.Active or FocusSessionStatus.Paused);

    public TimeSpan Elapsed
    {
        get
        {
            var session = CurrentSession;
            if (session == null) return TimeSpan.Zero;

            long elapsedMs = session.FocusedDurationMs;
            if (session.Status == FocusSessionStatus.Active && session.CurrentRunStartedAt is { } startedAt)
            {
                elapsedMs += Math.Max(0, (long)(_clock().ToUniversalTime() - startedAt).TotalMilliseconds);
            }

            return TimeSpan.FromMilliseconds(elapsedMs);
        }
    }

    public FocusTimerService(StorageService storage, string dataDirectory, Func<DateTimeOffset>? clock = null)
    {
        _storage = storage;
        _dataDirectory = dataDirectory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _sessions = _storage.LoadFocusSessions(_dataDirectory);
    }

    public FocusSession Start(TaskItem task, DateTime taskDate)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (CurrentSession != null)
        {
            throw new InvalidOperationException("Finish the current focus session before starting another.");
        }

        var now = _clock().ToUniversalTime();
        var session = new FocusSession
        {
            TaskId = task.Id,
            TaskDate = taskDate.Date,
            TaskTitle = task.Title,
            Status = FocusSessionStatus.Active,
            StartedAt = now,
            CurrentRunStartedAt = now
        };
        _sessions.Add(session);
        SaveAndNotify();
        return session;
    }

    public void Pause()
    {
        var session = RequireSession(FocusSessionStatus.Active);
        session.FocusedDurationMs += Math.Max(0, (long)(_clock().ToUniversalTime() - session.CurrentRunStartedAt!.Value).TotalMilliseconds);
        session.CurrentRunStartedAt = null;
        session.Status = FocusSessionStatus.Paused;
        SaveAndNotify();
    }

    public void Resume()
    {
        var session = RequireSession(FocusSessionStatus.Paused);
        session.CurrentRunStartedAt = _clock().ToUniversalTime();
        session.Status = FocusSessionStatus.Active;
        SaveAndNotify();
    }

    public FocusSession Finish()
    {
        var session = CurrentSession ?? throw new InvalidOperationException("There is no focus session to finish.");
        if (session.Status == FocusSessionStatus.Active && session.CurrentRunStartedAt is { } startedAt)
        {
            session.FocusedDurationMs += Math.Max(0, (long)(_clock().ToUniversalTime() - startedAt).TotalMilliseconds);
        }

        session.CurrentRunStartedAt = null;
        session.FinishedAt = _clock().ToUniversalTime();
        session.Status = FocusSessionStatus.Completed;
        SaveAndNotify();
        return session;
    }

    public void ChangeDataDirectory(string dataDirectory)
    {
        if (string.Equals(_dataDirectory, dataDirectory, StringComparison.OrdinalIgnoreCase)) return;

        var unfinished = CurrentSession;
        _dataDirectory = dataDirectory;
        _sessions.Clear();
        _sessions.AddRange(_storage.LoadFocusSessions(_dataDirectory));
        if (unfinished != null && !_sessions.Any(session => session.Id == unfinished.Id))
        {
            _sessions.Add(unfinished);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private FocusSession RequireSession(FocusSessionStatus status)
    {
        var session = CurrentSession;
        if (session == null || session.Status != status)
        {
            throw new InvalidOperationException($"There is no {status.ToString().ToLowerInvariant()} focus session.");
        }
        return session;
    }

    private void SaveAndNotify()
    {
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Persist() => _storage.SaveFocusSessions(_dataDirectory, _sessions);
}
