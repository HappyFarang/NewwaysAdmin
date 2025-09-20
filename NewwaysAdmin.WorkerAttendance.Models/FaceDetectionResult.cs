// File: NewwaysAdmin.WorkerAttendance.Models/FaceDetectionResult.cs
// Purpose: Result container for Python face detection responses

namespace NewwaysAdmin.WorkerAttendance.Models
{
    public class FaceDetectionResult
    {
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
        public List<DetectedFace>? Faces { get; set; }
    }
}