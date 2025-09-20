// NewwaysAdmin.WorkerAttendance.Models/Worker.cs
public class Worker
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public List<byte[]> FaceEncodings { get; set; } = new(); // Multiple face angles
}

