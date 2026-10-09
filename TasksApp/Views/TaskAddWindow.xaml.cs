using System;
using System.Windows;
using System.Windows.Input;
using TasksApp.Models;

namespace TasksApp.Views
{
    public partial class TaskAddWindow : Window
    {
        public TaskItem? CreatedTask { get; private set; }
        public DateTime SelectedDate => DpTaskDate.SelectedDate ?? DateTime.Today;

        public TaskAddWindow(DateTime? initialDate = null)
        {
            InitializeComponent();

            DpTaskDate.SelectedDate = initialDate ?? DateTime.Today;

            Loaded += (s, e) =>
            {
                TxtTitle.Focus();
            };
        }

        private void OnDateTodayClick(object sender, RoutedEventArgs e)
        {
            DpTaskDate.SelectedDate = DateTime.Today;
        }

        private void OnDateTomorrowClick(object sender, RoutedEventArgs e)
        {
            DpTaskDate.SelectedDate = DateTime.Today.AddDays(1);
        }

        private void OnPriorityLabelClick(object sender, MouseButtonEventArgs e)
        {
            ChkHighPriority.IsChecked = !(ChkHighPriority.IsChecked ?? false);
        }

        private void OnTitleKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnAddClick(sender, e);
                e.Handled = true;
            }
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            string title = TxtTitle.Text.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Please enter a title for the task.", "Tasks", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTitle.Focus();
                return;
            }

            CreatedTask = new TaskItem
            {
                Title = title,
                Details = string.IsNullOrWhiteSpace(TxtDetails.Text) ? string.Empty : TxtDetails.Text.Trim(),
                IsHighPriority = ChkHighPriority.IsChecked ?? false,
                CreatedAt = DateTime.Now
            };

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
