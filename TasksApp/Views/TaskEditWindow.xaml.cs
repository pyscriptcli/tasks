using System;
using System.Windows;
using System.Windows.Input;
using TasksApp.Models;

namespace TasksApp.Views
{
    public partial class TaskEditWindow : Window
    {
        public TaskItem UpdatedTask { get; private set; }
        public DateTime TargetDate => DpTaskDate.SelectedDate ?? DateTime.Today;

        public TaskEditWindow(TaskItem task, DateTime? currentDate = null)
        {
            InitializeComponent();
            UpdatedTask = task.Clone();

            TxtTitle.Text = UpdatedTask.Title;
            TxtDetails.Text = UpdatedTask.Details;
            ChkHighPriority.IsChecked = UpdatedTask.IsHighPriority;
            DpTaskDate.SelectedDate = currentDate ?? DateTime.Today;

            Loaded += (s, e) =>
            {
                TxtTitle.Focus();
                TxtTitle.SelectAll();
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

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtTitle.Text))
            {
                MessageBox.Show("Please enter a title for the task.", "Tasks", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTitle.Focus();
                return;
            }

            UpdatedTask.Title = TxtTitle.Text.Trim();
            UpdatedTask.Details = TxtDetails.Text.Trim();
            UpdatedTask.IsHighPriority = ChkHighPriority.IsChecked ?? false;

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
