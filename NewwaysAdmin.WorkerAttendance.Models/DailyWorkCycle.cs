// NewwaysAdmin.WorkerAttendance.Models/DailyWorkCycle.cs
// Purpose: Container for one worker's complete work cycle (normal + optional OT)

namespace NewwaysAdmin.WorkerAttendance.Models
{
    public class DailyWorkCycle
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = string.Empty;
        public DateTime CycleDate { get; set; } // Date of first sign-in
        public List<AttendanceRecord> Records { get; set; } = new();

        // Helper properties
        public bool HasOT => Records.Any(r => r.WorkCycle == WorkCycle.OT);
        public AttendanceRecord? LastRecord => Records.LastOrDefault();
        public bool IsCurrentlyCheckedIn => LastRecord?.Type == AttendanceType.CheckIn;

        // File naming helper
        public string GetFileName() => $"{CycleDate:yyyy-MM-dd}_Worker{WorkerId}.json";
    }
}