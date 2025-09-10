// NewwaysAdmin.Shared/IO/FileIndexing/Models/ProcessingStage.cs
namespace NewwaysAdmin.Shared.IO.FileIndexing.Models
{
    /// <summary>
    /// Represents the processing stage of an indexed file
    /// Generic enough to work for OCR, PDF processing, or any future document processing
    /// </summary>
    public enum ProcessingStage
    {
        /// <summary>
        /// File has been detected and indexed but processing hasn't started
        /// </summary>
        Detected = 0,

        /// <summary>
        /// Processing has begun (OCR started, PDF parsing started, etc.)
        /// </summary>
        ProcessingStarted = 1,

        /// <summary>
        /// Currently being processed (OCR in progress, PDF parsing in progress, etc.)
        /// </summary>
        Processing = 2,

        /// <summary>
        /// Processing completed successfully
        /// </summary>
        ProcessingCompleted = 3,

        /// <summary>
        /// Processing failed - may need retry or manual intervention
        /// </summary>
        ProcessingFailed = 4
    }
}