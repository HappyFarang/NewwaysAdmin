// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerStatusExtensions.cs
// Purpose: Extension methods to apply adjustment data to WorkerStatus objects

using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public static class WorkerStatusExtensions
    {
        public static WorkerStatus ApplyTodaysAdjustments(this WorkerStatus worker, DailyWorkRecord? todaysAdjustments)
        {
            if (todaysAdjustments?.HasAdjustments != true)
                return worker; // No adjustments, return as-is

            // Create a copy of the worker with adjusted display values
            var adjustedWorker = new WorkerStatus
            {
                // Copy all original properties
                WorkerId = worker.WorkerId,
                WorkerName = worker.WorkerName,
                IsActive = worker.IsActive,
                CurrentCycle = worker.CurrentCycle,
                LastActivity = worker.LastActivity,

                // Apply adjusted times if available, otherwise use originals
                NormalSignIn = todaysAdjustments.AppliedAdjustment?.AdjustedSignIn ?? worker.NormalSignIn,
                NormalSignOut = todaysAdjustments.AppliedAdjustment?.AdjustedSignOut ?? worker.NormalSignOut,
                OTSignIn = worker.OTSignIn, // OT adjustments not implemented yet
                OTSignOut = worker.OTSignOut,
                                
                // Add adjustment indicator
                HasAdjustments = true,
                AdjustmentTooltip = todaysAdjustments.AdjustmentTooltip
            };

            // Recalculate hours based on adjusted times
            if (adjustedWorker.NormalSignIn.HasValue && adjustedWorker.NormalSignOut.HasValue)
            {
                var duration = adjustedWorker.NormalSignOut.Value - adjustedWorker.NormalSignIn.Value;
                adjustedWorker.NormalHoursWorked = duration;
            }

            return adjustedWorker;
        }
    }
}