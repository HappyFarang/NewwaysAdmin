// NewwaysAdmin.WorkerAttendance.UI/Controls/ActiveWorkersControl.xaml.cs
// Purpose: Code-behind for Active Workers display component
// FIXED: Uses cycle-based logic, not date-based, so workers stay active after midnight

using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WorkerAttendance.Services;
using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class ActiveWorkersControl : UserControl
    {
        private EnhancedStorageFactory? _storageFactory;
        private ILoggerFactory? _loggerFactory;
        private System.Windows.Threading.DispatcherTimer? _refreshTimer;

        public ActiveWorkersControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize the control with dependencies
        /// </summary>
        public void Initialize(EnhancedStorageFactory factory, ILoggerFactory loggerFactory)
        {
            _storageFactory = factory;
            _loggerFactory = loggerFactory;

            // Set up auto-refresh every 30 seconds
            _refreshTimer = new System.Windows.Threading.DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(30);
            _refreshTimer.Tick += async (s, e) => await RefreshActiveWorkers();
            _refreshTimer.Start();

            // Initial load
            _ = RefreshActiveWorkers();
        }

        /// <summary>
        /// Refresh the list of active workers using direct storage access
        /// </summary>
        public async Task RefreshActiveWorkers()
        {
            if (_storageFactory == null || _loggerFactory == null) return;

            try
            {
                var activeWorkers = await GetActiveWorkersDirectlyAsync();

                Dispatcher.Invoke(() =>
                {
                    if (activeWorkers.Any())
                    {
                        WorkersList.ItemsSource = activeWorkers;
                        NoWorkersMessage.Visibility = Visibility.Collapsed;
                        WorkersList.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        WorkersList.ItemsSource = null;
                        WorkersList.Visibility = Visibility.Collapsed;
                        NoWorkersMessage.Visibility = Visibility.Visible;
                    }
                });
            }
            catch (Exception ex)
            {
                // Log error but don't crash UI
                System.Diagnostics.Debug.WriteLine($"Error refreshing active workers: {ex.Message}");
            }
        }

        /// <summary>
        /// Get active workers using CYCLE-BASED logic (not date-based)
        /// Workers stay active until they sign out, even after midnight
        /// </summary>
        private async Task<List<ActiveWorkerDisplay>> GetActiveWorkersDirectlyAsync()
        {
            var activeWorkers = new List<ActiveWorkerDisplay>();
            var cycleStorage = _storageFactory!.GetStorage<DailyWorkCycle>("AttendanceRecords");

            try
            {
                var identifiers = await cycleStorage.ListIdentifiersAsync();

                // Get all date-prefixed cycle files (YYYY-MM-DD_WorkerX.json)
                // We need to check recent files, not just today's
                var cycleDateFiles = identifiers
                    .Where(id => id.Contains("_Worker") && id.EndsWith(".json"))
                    .OrderByDescending(id => id) // Newest first
                    .ToList();

                // Track which workers we've already processed
                var processedWorkers = new HashSet<int>();

                foreach (var fileName in cycleDateFiles)
                {
                    try
                    {
                        var cycle = await cycleStorage.LoadAsync(fileName);

                        if (cycle == null || cycle.Records.Count == 0)
                            continue;

                        // Skip if we already found this worker's active status
                        if (processedWorkers.Contains(cycle.WorkerId))
                            continue;

                        // Check if currently checked in (last record is check-in)
                        if (cycle.IsCurrentlyCheckedIn)
                        {
                            var lastCheckIn = cycle.Records
                                .Where(r => r.Type == AttendanceType.CheckIn)
                                .LastOrDefault();

                            if (lastCheckIn != null)
                            {
                                activeWorkers.Add(new ActiveWorkerDisplay
                                {
                                    WorkerName = cycle.WorkerName,
                                    CheckInTime = lastCheckIn.Timestamp,
                                    CurrentCycle = lastCheckIn.WorkCycle
                                });

                                processedWorkers.Add(cycle.WorkerId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading cycle file {fileName}: {ex.Message}");
                        continue;
                    }
                }

                return activeWorkers.OrderBy(w => w.CheckInTime).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting active workers: {ex.Message}");
                return new List<ActiveWorkerDisplay>();
            }
        }

        /// <summary>
        /// Manual refresh - call this when someone signs in/out
        /// </summary>
        public void TriggerRefresh()
        {
            _ = RefreshActiveWorkers();
        }

        /// <summary>
        /// Clean up timer when control is disposed
        /// </summary>
        public void Cleanup()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
        }
    }

    // Simple data class for display
    public class ActiveWorkerDisplay
    {
        public string WorkerName { get; set; } = string.Empty;
        public DateTime CheckInTime { get; set; }
        public WorkCycle CurrentCycle { get; set; }
        public string Status => $"{CurrentCycle} - {WorkedTime}";
        public string WorkedTime =>
            $"{(DateTime.Now - CheckInTime).Hours:D2}:{(DateTime.Now - CheckInTime).Minutes:D2}";
    }
}