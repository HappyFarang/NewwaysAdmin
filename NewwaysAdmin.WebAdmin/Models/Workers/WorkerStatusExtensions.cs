// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerStatusExtensions.cs
// Purpose: Extension methods to apply adjustment data to WorkerStatus objects
// UPDATED: Enhanced to handle all adjustment types and copy all necessary properties

using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public static class WorkerStatusExtensions
    {
        /// <summary>
        /// Apply current cycle adjustments to worker status display
        /// CRITICAL: Returns a NEW WorkerStatus object with adjusted values applied
        /// </summary>
        public static WorkerStatus ApplyTodaysAdjustments(this WorkerStatus worker, DailyWorkRecord? cycleAdjustments)
        {
            if (cycleAdjustments?.HasAdjustments != true || cycleAdjustments.AppliedAdjustment == null)
                return worker; // No adjustments, return as-is

            var adjustment = cycleAdjustments.AppliedAdjustment;

            // Create a copy of the worker with adjusted display values
            var adjustedWorker = new WorkerStatus
            {
                // Copy ALL original properties
                WorkerId = worker.WorkerId,
                WorkerName = worker.WorkerName,
                IsActive = worker.IsActive,
                CurrentCycle = worker.CurrentCycle,
                LastActivity = worker.LastActivity,
                CycleDate = worker.CycleDate,
                ShowDate = worker.ShowDate,
                HasOT = worker.HasOT,
                CurrentDuration = worker.CurrentDuration,

                // Apply adjusted sign-in/sign-out times (these affect display)
                NormalSignIn = adjustment.AdjustedSignIn ?? worker.NormalSignIn,
                NormalSignOut = adjustment.AdjustedSignOut ?? worker.NormalSignOut,

                // OT times (not usually adjusted, but copy for completeness)
                OTSignIn = worker.OTSignIn,
                OTSignOut = worker.OTSignOut,
                OTHoursWorked = worker.OTHoursWorked,

                // CRITICAL: Set adjustment indicators for UI display
                HasAdjustments = true,
                AdjustmentTooltip = cycleAdjustments.AdjustmentTooltip
            };

            // Recalculate normal hours based on adjusted times
            if (adjustedWorker.NormalSignIn.HasValue && adjustedWorker.NormalSignOut.HasValue)
            {
                var duration = adjustedWorker.NormalSignOut.Value - adjustedWorker.NormalSignIn.Value;
                adjustedWorker.NormalHoursWorked = duration;
            }
            else if (adjustedWorker.NormalSignIn.HasValue && worker.IsActive)
            {
                // For active workers without sign-out, calculate current duration
                adjustedWorker.CurrentDuration = DateTime.Now - adjustedWorker.NormalSignIn.Value;
            }

            return adjustedWorker;
        }

        /// <summary>
        /// Check if a worker has any display-affecting adjustments
        /// </summary>
        public static bool HasDisplayAdjustments(this WorkerStatus worker)
        {
            return worker.HasAdjustments;
        }

        /// <summary>
        /// Get a summary of what adjustments were applied for display purposes
        /// </summary>
        public static string GetAdjustmentSummary(this WorkerStatus worker)
        {
            if (!worker.HasAdjustments || string.IsNullOrEmpty(worker.AdjustmentTooltip))
                return string.Empty;

            // Extract just the description from the tooltip
            var lines = worker.AdjustmentTooltip.Split('\n');
            return lines.Length > 0 ? lines[0].Replace("Adjusted: ", "") : "Adjusted";
        }
    }
}