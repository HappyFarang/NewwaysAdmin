// File: NewwaysAdmin.WorkerAttendance.UI/Windows/WorkerManagementWindow.xaml.cs
// Purpose: Simple worker management window - list and delete registered workers

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NewwaysAdmin.WorkerAttendance.Services;
using NewwaysAdmin.WorkerAttendance.Models;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WorkerAttendance.UI.Windows
{
    public partial class WorkerManagementWindow : Window
    {
        private readonly WorkerStorageService _storageService;
        private readonly ILogger<WorkerManagementWindow> _logger;

        public WorkerManagementWindow(WorkerStorageService storageService, ILogger<WorkerManagementWindow> logger)
        {
            InitializeComponent();
            _storageService = storageService;
            _logger = logger;

            // Load workers when window opens
            Loaded += WorkerManagementWindow_Loaded;
        }

        private async void WorkerManagementWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadWorkersAsync();
        }

        private async Task LoadWorkersAsync()
        {
            try
            {
                StatusMessage.Text = "Loading registered workers...";
                WorkerListPanel.Children.Clear();

                var workers = await _storageService.GetAllWorkersAsync();

                if (workers.Count == 0)
                {
                    StatusMessage.Text = "No registered workers found.";

                    // Show empty message
                    var emptyMessage = new TextBlock
                    {
                        Text = "No workers registered yet.",
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(0, 20, 0, 0)
                    };
                    WorkerListPanel.Children.Add(emptyMessage);
                    return;
                }

                // Sort workers by ID
                workers = workers.OrderBy(w => w.Id).ToList();

                foreach (var worker in workers)
                {
                    var workerPanel = CreateWorkerPanel(worker);
                    WorkerListPanel.Children.Add(workerPanel);
                }

                StatusMessage.Text = $"Found {workers.Count} registered worker{(workers.Count == 1 ? "" : "s")}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading workers");
                StatusMessage.Text = $"Error loading workers: {ex.Message}";
                StatusMessage.Foreground = Brushes.Red;
            }
        }

        private Border CreateWorkerPanel(Worker worker)
        {
            // Main container
            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(15, 10, 15, 10)
            };

            // Horizontal layout for worker info and delete button
            var panel = new DockPanel();

            // Delete button (right side)
            var deleteButton = new Button
            {
                Content = "Delete",
                Width = 60,
                Height = 25,
                FontSize = 10,
                Background = Brushes.LightCoral,
                Foreground = Brushes.White,
                Margin = new Thickness(10, 0, 0, 0)
            };
            deleteButton.Click += async (s, e) => await DeleteWorker_Click(worker, border);
            DockPanel.SetDock(deleteButton, Dock.Right);

            // Worker information (left side)
            var infoPanel = new StackPanel();

            // Worker name
            var nameText = new TextBlock
            {
                Text = worker.Name,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkBlue
            };

            // Worker details
            var detailsText = new TextBlock
            {
                FontSize = 10,
                Foreground = Brushes.Gray
            };

            // Build details text
            var details = new List<string>
            {
                $"ID: {worker.Id}",
                $"Created: {worker.CreatedDate:yyyy-MM-dd HH:mm}",
                $"Face Encodings: {worker.FaceEncodings?.Count ?? 0}",
                worker.IsActive ? "Active" : "Inactive"
            };
            detailsText.Text = string.Join(" | ", details);

            infoPanel.Children.Add(nameText);
            infoPanel.Children.Add(detailsText);

            // Add to panel
            panel.Children.Add(deleteButton);
            panel.Children.Add(infoPanel);
            border.Child = panel;

            return border;
        }

        private async Task DeleteWorker_Click(Worker worker, Border workerPanel)
        {
            try
            {
                // Confirm deletion
                var result = MessageBox.Show(
                    $"Are you sure you want to delete worker '{worker.Name}'?\n\nThis action cannot be undone.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                StatusMessage.Text = $"Deleting worker '{worker.Name}'...";
                StatusMessage.Foreground = Brushes.Blue;

                // Delete from storage
                await _storageService.DeleteWorkerAsync(worker.Id);

                // Remove from UI
                WorkerListPanel.Children.Remove(workerPanel);

                // Update status
                StatusMessage.Text = $"Worker '{worker.Name}' deleted successfully.";
                StatusMessage.Foreground = Brushes.Green;

                _logger.LogInformation("Deleted worker: {WorkerName} (ID: {WorkerId})", worker.Name, worker.Id);

                // Check if no workers left
                if (WorkerListPanel.Children.Count == 0)
                {
                    var emptyMessage = new TextBlock
                    {
                        Text = "No workers registered.",
                        FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(0, 20, 0, 0)
                    };
                    WorkerListPanel.Children.Add(emptyMessage);
                    StatusMessage.Text = "All workers deleted.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting worker {WorkerName} (ID: {WorkerId})", worker.Name, worker.Id);
                StatusMessage.Text = $"Error deleting worker: {ex.Message}";
                StatusMessage.Foreground = Brushes.Red;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}