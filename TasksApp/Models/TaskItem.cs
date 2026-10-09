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
            set { _createdAt = value; OnPropertyChanged(); }
        }

        [JsonPropertyName("carriedOverFrom")]
        public string? CarriedOverFrom
        {
            get => _carriedOverFrom;
            set
            {
                _carriedOverFrom = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCarriedOver));
            }
        }

        [JsonIgnore]
        public bool IsCarriedOver => !string.IsNullOrEmpty(_carriedOverFrom);

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
                CarriedOverFrom = this.CarriedOverFrom
            };
        }
    }
}
