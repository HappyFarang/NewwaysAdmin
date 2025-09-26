// NewwaysAdmin.WorkerAttendance.Models/AttendanceEnums.cs
// Purpose: Shared enums for attendance system

namespace NewwaysAdmin.WorkerAttendance.Models
{
    public enum AttendanceType
    {
        CheckIn,
        CheckOut
    }

    public enum WorkCycle
    {
        Normal,    // Regular work hours
        OT         // Overtime work
    }
}