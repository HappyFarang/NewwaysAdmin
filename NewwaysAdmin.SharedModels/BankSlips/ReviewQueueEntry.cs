// NewwaysAdmin.SharedModels/BankSlips/ReviewQueueEntry.cs

namespace NewwaysAdmin.SharedModels.BankSlips;

/// <summary>
/// Entry in the review queue for bank slip projects needing attention.
/// Stored in BankSlipsJson/ReviewQueue.json - short-lived, removed when project is closed.
/// </summary>
public class ReviewQueueEntry
{
    /// <summary>
    /// Project ID to review (matches BankSlipProject.ProjectId)
    /// Example: "KBIZ_Amy_01_01_2026_19_13_27"
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Why this project needs review
    /// </summary>
    public ReviewReason Reason { get; set; } = ReviewReason.NormalReview;

    /// <summary>
    /// When this entry was added to the review queue
    /// </summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}