// File: NewwaysAdmin.WebAdmin/Models/Workers/DashboardData.cs
// Purpose: Container for dashboard state data

using NewwaysAdmin.WebAdmin.Services.Workers;

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public class DashboardData
    {
        public List<WorkerStatus> ActiveWorkers { get; set; } = new();
        public List<WorkerStatus> InactiveWorkers { get; set; } = new();
        public DateTime RefreshTime { get; set; }
    }
}