// Create: NewwaysAdmin.WebAdmin/Models/Workers/WeeklyTableColumnVisibility.cs

public class WeeklyTableColumnVisibility
{
    // All columns visible by default as you requested
    public bool NormalSignIn { get; set; } = true;
    public bool NormalSignOut { get; set; } = true;
    public bool OTSignIn { get; set; } = true;
    public bool OTSignOut { get; set; } = true;
    public bool WorkHours { get; set; } = true;
    public bool OTHours { get; set; } = true;
    public bool Variance { get; set; } = true;
    public bool OnTime { get; set; } = true;
    public bool DailyPay { get; set; } = true;
    public bool NormalPay { get; set; } = false;
    public bool OTPay { get; set; } = false;
}