// File: NewwaysAdmin.Mobile/Platforms/Android/Services/BankSlipMonitorControl.cs
// Android implementation of bank slip monitor control

using Android.Content;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.BankSlip;

namespace NewwaysAdmin.Mobile.Platforms.Android.Services
{
    public class BankSlipMonitorControl : IBankSlipMonitorControl
    {
        private readonly ILogger<BankSlipMonitorControl> _logger;
        private readonly BankSlipObserverManager _observerManager;
        private readonly BankSlipSettingsService _settingsService;
        private bool _isMonitoring;

        public BankSlipMonitorControl(
            ILogger<BankSlipMonitorControl> logger,
            BankSlipObserverManager observerManager,
            BankSlipSettingsService settingsService)
        {
            _logger = logger;
            _observerManager = observerManager;
            _settingsService = settingsService;
        }

        public bool IsMonitoring => _isMonitoring;

        public void StartMonitoring()
        {
            if (_isMonitoring)
            {
                _logger.LogDebug("[MonitorControl] Already monitoring");
                return;
            }

            _logger.LogInformation("[MonitorControl] Starting monitoring service...");

            try
            {
                var context = global::Android.App.Application.Context;
                BankSlipMonitorService.Start(context);
                _isMonitoring = true;

                _logger.LogInformation("[MonitorControl] ✅ Monitoring service started");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MonitorControl] Failed to start monitoring service");
            }
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring)
            {
                _logger.LogDebug("[MonitorControl] Not monitoring");
                return;
            }

            _logger.LogInformation("[MonitorControl] Stopping monitoring service...");

            try
            {
                var context = global::Android.App.Application.Context;
                BankSlipMonitorService.Stop(context);
                _isMonitoring = false;

                _logger.LogInformation("[MonitorControl] ✅ Monitoring service stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MonitorControl] Failed to stop monitoring service");
            }
        }

        public async void RefreshWatchedFolders()
        {
            if (!_isMonitoring)
            {
                _logger.LogDebug("[MonitorControl] Not monitoring, nothing to refresh");
                return;
            }

            _logger.LogInformation("[MonitorControl] Refreshing watched folders...");

            // Stop all current watchers
            _observerManager.StopAll();

            // Restart with current settings
            var settings = await _settingsService.LoadSettingsAsync();
            var watchCount = 0;

            foreach (var folder in settings.MonitoredFolders)
            {
                if (!string.IsNullOrEmpty(folder.DeviceFolderPath) &&
                    Directory.Exists(folder.DeviceFolderPath))
                {
                    if (_observerManager.StartWatching(folder.PatternIdentifier, folder.DeviceFolderPath))
                    {
                        watchCount++;
                    }
                }
            }

            _logger.LogInformation(
                "[MonitorControl] Refreshed - now watching {Count} folders", watchCount);
        }
    }
}