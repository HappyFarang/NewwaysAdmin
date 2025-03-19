namespace NewwaysAdmin.WebAdmin.Components.Features.Sales.Daily
{
    public class CourierTrackingData
    {
        public int TodayCount { get; set; }
        public int CarryoverCount { get; set; }
        public int TotalCount => TodayCount + CarryoverCount;
        public DateTime LastReset { get; set; } = DateTime.Now;
    }
}