using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace BottomUpZipper
{
    public partial class MainWindow : Window
    {
        private string? selectedFolderPath;
        private readonly ZipService zipService;

        public MainWindow()
        {
            InitializeComponent();
            zipService = new ZipService();

            // Subscribe to events from ZipService
            zipService.ProgressChanged += OnProgressChanged;
            zipService.StatusChanged += OnStatusChanged;
            zipService.OperationChanged += OnOperationChanged;
            zipService.LogMessage += OnLogMessage;
        }

        /// <summary>
        /// Opens folder browser dialog to select a folder
        /// </summary>
        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a folder to zip";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    selectedFolderPath = dialog.SelectedPath;
                    SelectedFolderTextBox.Text = selectedFolderPath;
                    StartZipButton.IsEnabled = true;
                    UpdateStatus($"Folder selected: {selectedFolderPath}");
                    LogMessage($"Selected folder: {selectedFolderPath}");
                }
            }
        }

        /// <summary>
        /// Starts the bottom-up zipping process
        /// </summary>
        private async void StartZipButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedFolderPath) || !Directory.Exists(selectedFolderPath))
            {
                System.Windows.MessageBox.Show("Please select a valid folder first.", "Invalid Folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Confirm action
            var result = System.Windows.MessageBox.Show(
                "This will zip all subfolders from deepest to shallowest and DELETE the original folders after zipping.\n\n" +
                "Are you sure you want to continue?",
                "Confirm Operation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // Disable buttons during operation
            SelectFolderButton.IsEnabled = false;
            StartZipButton.IsEnabled = false;

            try
            {
                UpdateStatus("Starting bottom-up zipping process...");
                LogMessage("=== Starting zipping operation ===");

                bool zipRoot = ZipRootFolderCheckBox.IsChecked ?? false;

                await Task.Run(() => zipService.ZipFolderBottomUp(selectedFolderPath, zipRoot));

                UpdateStatus("✓ Zipping completed successfully!");
                LogMessage("=== Operation completed successfully ===");

                System.Windows.MessageBox.Show("Folder zipping completed successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException ex)
            {
                UpdateStatus($"✗ Permission denied: {ex.Message}");
                LogMessage($"ERROR: Permission denied - {ex.Message}");
                System.Windows.MessageBox.Show($"Access denied. Please ensure you have permission to modify the folder.\n\n{ex.Message}",
                    "Permission Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                UpdateStatus($"✗ Error: {ex.Message}");
                LogMessage($"ERROR: {ex.Message}");
                System.Windows.MessageBox.Show($"An error occurred:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable buttons
                SelectFolderButton.IsEnabled = true;
                StartZipButton.IsEnabled = true;
            }
        }

        #region Event Handlers

        private void OnProgressChanged(object? sender, ProgressEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = e.PercentComplete;
                ProgressTextBlock.Text = $"{e.PercentComplete:F1}% ({e.Current}/{e.Total})";
            });
        }

        private void OnStatusChanged(object? sender, string status)
        {
            UpdateStatus(status);
        }

        private void OnOperationChanged(object? sender, string operation)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentOperationTextBlock.Text = operation;
            });
        }

        private void OnLogMessage(object? sender, string message)
        {
            LogMessage(message);
        }

        #endregion

        #region Helper Methods

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = message;
            });
        }

        private void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogTextBlock.Text += $"[{timestamp}] {message}\n";
            });
        }

        #endregion
    }
}
