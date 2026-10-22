using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TasksApp.Models
{
    public class TaskItem : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _title = string.Empty;
        private string _details = string.Empty;
        private bool _isHighPriority;
        private bool _isCompleted;
        private DateTime? _completedAt;
        private int _orderIndex;
        private DateTime _createdAt = DateTime.Now;
        private DateTime _displayDate = DateTime.Today;
        private bool _hasStoredCreatedAt;

        [JsonPropertyName("id")]
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("order")]
        public int OrderIndex
        {
            get => _orderIndex;
            set { _orderIndex = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("title")]
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("details")]
        public string Details
        {
            get => _details;
            set { _details = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("isHighPriority")]
        public bool IsHighPriority
        {
            get => _isHighPriority;
            set { _isHighPriority = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("isCompleted")]
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    _completedAt = value ? DateTime.Now : null;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CompletedAt));
                }
            }
        }

        private string? _carriedOverFrom;
        private string? _carriedForwardTo;

        [JsonPropertyName("completedAt")]
        public DateTime? CompletedAt
        {
            get => _completedAt;
            set { _completedAt = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt
        {
            get => _createdAt;
            set
            {
                _createdAt = value;
                _hasStoredCreatedAt = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CarryOverDays));
                OnPropertyChanged(nameof(CarryOverLabel));
                OnPropertyChanged(nameof(CarryOverTooltip));
            }
        }

        [JsonIgnore]
        public bool HasStoredCreatedAt => _hasStoredCreatedAt;

        [JsonIgnore]
        public DateTime DisplayDate
        {
            get => _displayDate;
            set
            {
                _displayDate = value.Date;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CarryOverDays));
                OnPropertyChanged(nameof(CarryOverLabel));
                OnPropertyChanged(nameof(CarryOverTooltip));
            }
        }

        [JsonIgnore]
        public int CarryOverDays => IsCarriedOver ? GetCarryOverDaysAsOf(DisplayDate) : 0;

        [JsonIgnore]
        public string CarryOverLabel => CarryOverDays == 1
            ? "CARRIED OVER · 1 DAY"
            : $"CARRIED OVER · {CarryOverDays} DAYS";

        [JsonIgnore]
        public string CarryOverTooltip => $"Created {CreatedAt.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)}";

        public int GetCarryOverDaysAsOf(DateTime taskDate) =>
            Math.Max(0, (taskDate.Date - CreatedAt.Date).Days);

        [JsonPropertyName("carriedOverFrom")]
        public string? CarriedOverFrom
        {
            get => _carriedOverFrom;
            set
            {
                _carriedOverFrom = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCarriedOver));
                OnPropertyChanged(nameof(CarryOverDays));
                OnPropertyChanged(nameof(CarryOverLabel));
                OnPropertyChanged(nameof(CarryOverTooltip));
            }
        }

        [JsonPropertyName("carriedForwardTo")]
        public string? CarriedForwardTo
        {
            get => _carriedForwardTo;
            set
            {
                _carriedForwardTo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCarriedForward));
            }
        }

        // Backward compatibility fallbacks for capturing tasks from other/older versions
        [JsonPropertyName("isDone")]
        public bool? IsDoneLegacy
        {
            get => null;
            set
            {
                if (value.HasValue && !_isCompleted)
                {
                    IsCompleted = value.Value;
                }
            }
        }

        [JsonPropertyName("orderIndex")]
        public int? OrderIndexLegacy
        {
            get => null;
            set
            {
                if (value.HasValue && _orderIndex == 0)
                {
                    OrderIndex = value.Value;
                }
            }
        }

        [JsonPropertyName("priorityGroup")]
        public System.Text.Json.JsonElement? PriorityGroupLegacy
        {
            get => null;
            set
            {
                if (!value.HasValue) return;
                var el = value.Value;
                if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    string str = el.GetString() ?? "";
                    if (str.Contains("HIGH", StringComparison.OrdinalIgnoreCase))
                    {
                        IsHighPriority = true;
                    }
                }
                else if (el.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    if (el.TryGetInt32(out int val) && val == 1)
                    {
                        IsHighPriority = true;
                    }
                }
            }
        }

        [JsonIgnore]
        public bool IsCarriedOver => !string.IsNullOrEmpty(_carriedOverFrom);

        [JsonIgnore]
        public bool IsCarriedForward => !string.IsNullOrEmpty(_carriedForwardTo);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public TaskItem Clone()
        {
            return new TaskItem
            {
                Id = this.Id,
                OrderIndex = this.OrderIndex,
                Title = this.Title,
                Details = this.Details,
                IsHighPriority = this.IsHighPriority,
                IsCompleted = this.IsCompleted,
                CompletedAt = this.CompletedAt,
                CreatedAt = this.CreatedAt,
                DisplayDate = this.DisplayDate,
                CarriedOverFrom = this.CarriedOverFrom,
                CarriedForwardTo = this.CarriedForwardTo
            };
        }
    }
}
