using System;
using System.Windows.Threading;

namespace TasksApp.Services
{
    public class ReminderService
    {
        private readonly DispatcherTimer _intervalTimer;
        private readonly DispatcherTimer _snoozeTimer;
        private readonly DispatcherTimer _nagTimer; // For UR-022 repeating reminder if left unaddressed

        private int _intervalSeconds = 1800; // 30 minutes
        private bool _isEnabled = true;
        private bool _isReminderActive = false;

        public event EventHandler? ReminderDue;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (!_isEnabled)
                {
                    StopAll();
                }
                else
                {
                    StartIntervalTimer();
                }
            }
        }

        public int IntervalSeconds
        {
            get => _intervalSeconds;
            set
            {
                // Protected against zero or bad values (UR-027)
                int safeValue = Math.Max(10, value);
                _intervalSeconds = safeValue;
                if (_isEnabled)
                {
                    StartIntervalTimer();
                }
            }
        }

        public bool IsReminderActive => _isReminderActive;

        public ReminderService()
        {
            _intervalTimer = new DispatcherTimer(DispatcherPriority.Background);
            _intervalTimer.Tick += (s, e) => OnIntervalTick();

            _snoozeTimer = new DispatcherTimer(DispatcherPriority.Background);
            _snoozeTimer.Tick += (s, e) => OnSnoozeTick();

            _nagTimer = new DispatcherTimer(DispatcherPriority.Background);
            _nagTimer.Interval = TimeSpan.FromMinutes(2); // If reminder is open and untouched, repeat notification attention
            _nagTimer.Tick += (s, e) => OnNagTick();
        }

        public void Initialize(bool enabled, int intervalSeconds)
        {
            _isEnabled = enabled;
            // Clamped against invalid/zero values (UR-027)
            _intervalSeconds = Math.Max(10, intervalSeconds);

            if (_isEnabled)
            {
                StartIntervalTimer();
            }
        }

        private void StartIntervalTimer()
        {
            _intervalTimer.Stop();
            _intervalTimer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
            _intervalTimer.Start();
        }

        private void OnIntervalTick()
        {
            TriggerReminder();
        }

        private void OnSnoozeTick()
        {
            _snoozeTimer.Stop();
            TriggerReminder();
        }

        private void OnNagTick()
        {
            if (_isReminderActive)
            {
                // Repeat trigger to bring attention back (UR-022)
                ReminderDue?.Invoke(this, EventArgs.Empty);
            }
        }

        private void TriggerReminder()
        {
            _isReminderActive = true;
            _nagTimer.Start();
            ReminderDue?.Invoke(this, EventArgs.Empty);
        }

        public void DismissReminder()
        {
            _isReminderActive = false;
            _nagTimer.Stop();
            _snoozeTimer.Stop();
            if (_isEnabled)
            {
                StartIntervalTimer();
            }
        }

        public void SnoozeReminder(TimeSpan duration)
        {
            _isReminderActive = false;
            _nagTimer.Stop();
            _intervalTimer.Stop();

            _snoozeTimer.Stop();
            _snoozeTimer.Interval = duration > TimeSpan.Zero ? duration : TimeSpan.FromMinutes(5);
            _snoozeTimer.Start();
        }

        public void StopAll()
        {
            _intervalTimer.Stop();
            _snoozeTimer.Stop();
            _nagTimer.Stop();
            _isReminderActive = false;
        }
    }
}
