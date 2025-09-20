// File: NewwaysAdmin.WorkerAttendance.Models/FacePosition.cs
// Purpose: Represents the position and dimensions of a detected face

namespace NewwaysAdmin.WorkerAttendance.Models
{
    public class FacePosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}