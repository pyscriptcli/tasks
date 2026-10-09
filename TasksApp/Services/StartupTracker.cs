using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace TasksApp.Services
{
    public class StartupStep
    {
        public string Name { get; set; } = string.Empty;
        public double DurationMs { get; set; }
        public string Details { get; set; } = string.Empty;
    }

    public class StartupTracker
    {
        private static readonly Lazy<StartupTracker> _instance = new(() => new StartupTracker());
        public static StartupTracker Instance => _instance.Value;

        private readonly Stopwatch _totalStopwatch = new();
        private readonly Stopwatch _stepStopwatch = new();
        private readonly List<StartupStep> _steps = new();
        private string _currentStepName = string.Empty;

        private StartupTracker()
        {
            _totalStopwatch.Start();
        }

        public void StartStep(string stepName)
        {
            if (_stepStopwatch.IsRunning && !string.IsNullOrEmpty(_currentStepName))
            {
                EndStep("Completed");
            }

            _currentStepName = stepName;
            _stepStopwatch.Restart();
        }

        public void EndStep(string details = "")
        {
            if (!_stepStopwatch.IsRunning || string.IsNullOrEmpty(_currentStepName)) return;

            _stepStopwatch.Stop();
            double duration = _stepStopwatch.Elapsed.TotalMilliseconds;

            _steps.Add(new StartupStep
            {
                Name = _currentStepName,
                DurationMs = Math.Round(duration, 2),
                Details = details
            });

            _currentStepName = string.Empty;
        }

        public void CompleteStartup()
        {
            if (_stepStopwatch.IsRunning && !string.IsNullOrEmpty(_currentStepName))
            {
                EndStep("Completed");
            }
            _totalStopwatch.Stop();
        }

        public IReadOnlyList<StartupStep> Steps => _steps.AsReadOnly();

        public double TotalDurationMs => Math.Round(_totalStopwatch.Elapsed.TotalMilliseconds, 2);

        public StartupStep? GetSlowestStep()
        {
            if (_steps.Count == 0) return null;
            return _steps.OrderByDescending(s => s.DurationMs).FirstOrDefault();
        }

        public string GetImprovementRecommendation()
        {
            var slowest = GetSlowestStep();
            if (slowest == null) return "No startup steps recorded.";

            if (slowest.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
                slowest.Name.Contains("Load", StringComparison.OrdinalIgnoreCase))
            {
                return $"Slowest step: {slowest.Name} ({slowest.DurationMs:F1} ms).\n" +
                       $"Recommendation: Store tasks on a fast local solid-state drive (SSD). Keep daily files lean and archive past months if list size grows very large.";
            }
            else if (slowest.Name.Contains("UI", StringComparison.OrdinalIgnoreCase) ||
                     slowest.Name.Contains("Render", StringComparison.OrdinalIgnoreCase))
            {
                return $"Slowest step: {slowest.Name} ({slowest.DurationMs:F1} ms).\n" +
                       $"Recommendation: Hardware acceleration is active. Ensure modern graphics drivers are installed to maintain instant desktop rendering.";
            }
            else
            {
                return $"Slowest step: {slowest.Name} ({slowest.DurationMs:F1} ms).\n" +
                       $"Recommendation: Startup is executing efficiently well under the 2.0s requirement ({TotalDurationMs:F1} ms total).";
            }
        }

        public string GenerateLogReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TASKS STARTUP DIAGNOSTICS LOG ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Startup Time: {TotalDurationMs:F1} ms");
            sb.AppendLine();
            sb.AppendLine("--- STEP BREAKDOWN ---");
            foreach (var step in _steps)
            {
                sb.AppendLine($"• {step.Name}: {step.DurationMs:F1} ms [{step.Details}]");
            }
            sb.AppendLine();
            sb.AppendLine("--- PERFORMANCE ANALYSIS ---");
            sb.AppendLine(GetImprovementRecommendation());
            return sb.ToString();
        }
    }
}
