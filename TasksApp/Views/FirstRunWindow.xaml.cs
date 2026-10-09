using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TasksApp.Views
{
    public partial class FirstRunWindow : Window
    {
        public string SelectedDirectory { get; private set; } = string.Empty;

        public FirstRunWindow(string defaultPath)
        {
            InitializeComponent();
            TxtFolderPath.Text = defaultPath;
            SelectedDirectory = defaultPath;
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Select Folder for Tasks and Notes",
                    InitialDirectory = Directory.Exists(TxtFolderPath.Text) ? TxtFolderPath.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (dialog.ShowDialog() == true)
                {
                    TxtFolderPath.Text = dialog.FolderName;
                    TxtError.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                TxtError.Text = $"Could not open folder picker: {ex.Message}";
                TxtError.Visibility = Visibility.Visible;
            }
        }

        private void OnGetStartedClick(object sender, RoutedEventArgs e)
        {
            string path = TxtFolderPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                TxtError.Text = "Please enter or select a valid folder path.";
                TxtError.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                SelectedDirectory = path;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TxtError.Text = $"Cannot create or access folder: {ex.Message}";
                TxtError.Visibility = Visibility.Visible;
            }
        }
    }
}
