// File: NewwaysAdmin.Mobile/Services/BankSlips/IBankSlipMonitorControl.cs
// Interface for controlling the bank slip monitor service

namespace NewwaysAdmin.Mobile.Services.BankSlip
{
    /// <summary>
    /// Controls the bank slip monitoring service (platform-specific implementation)
    /// </summary>
    public interface IBankSlipMonitorControl
    {
        /// <summary>
        /// Start the monitoring service
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// Stop the monitoring service
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// Check if the monitoring service is running
        /// </summary>
        bool IsMonitoring { get; }

        /// <summary>
        /// Refresh watched folders (call after folder config changes)
        /// </summary>
        void RefreshWatchedFolders();
    }
}