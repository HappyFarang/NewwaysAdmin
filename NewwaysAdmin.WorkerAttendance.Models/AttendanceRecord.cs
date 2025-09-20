// NewwaysAdmin.WorkerAttendance.Models/AttendanceRecord.cs
public class AttendanceRecord
{
    public int Id { get; set; }
    public int WorkerId { get; set; }
    public DateTime Timestamp { get; set; }
    public AttendanceType Type { get; set; } // CheckIn/CheckOut
    public bool IsSynced { get; set; } = false; // For future API sync
}

public enum AttendanceType
{
    CheckIn,
    CheckOut
}