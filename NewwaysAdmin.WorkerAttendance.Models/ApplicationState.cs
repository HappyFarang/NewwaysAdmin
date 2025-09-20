// File: NewwaysAdmin.WorkerAttendance.Models/ApplicationState.cs
// Purpose: Enum defining the main application states for the attendance system

namespace NewwaysAdmin.WorkerAttendance.Models
{
    public enum ApplicationState
    {
        Ready,
        Scanning,
        WaitingForConfirmation,
        Processing
    }
}