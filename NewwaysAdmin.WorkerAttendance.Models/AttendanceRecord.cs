// NewwaysAdmin.WorkerAttendance.Models/AttendanceRecord.cs
// Updated to include WorkCycle and RecognitionConfidence

namespace NewwaysAdmin.WorkerAttendance.Models
{
    public class AttendanceRecord
    {
        public int Id { get; set; }
        public int WorkerId { get; set; }
        public DateTime Timestamp { get; set; }
        public AttendanceType Type { get; set; } // CheckIn/CheckOut
        public WorkCycle WorkCycle { get; set; } // Normal/OT
        public double RecognitionConfidence { get; set; } // Face recognition accuracy
        public bool IsSynced { get; set; } = false; // For future API sync
    }
}