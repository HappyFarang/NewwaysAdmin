// File: NewwaysAdmin.WorkerAttendance.Models/DetectedFace.cs
// Purpose: Represents a single detected face with confidence and position data

namespace NewwaysAdmin.WorkerAttendance.Models
{
    public class DetectedFace
    {
        public string Id { get; set; } = "";
        public double Confidence { get; set; }
        public FacePosition? Position { get; set; }
    }
}