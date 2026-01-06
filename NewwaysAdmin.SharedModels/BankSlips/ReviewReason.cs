// NewwaysAdmin.SharedModels/BankSlips/ReviewReason.cs

namespace NewwaysAdmin.SharedModels.BankSlips;

/// <summary>
/// Reason why a bank slip project needs review
/// </summary>
public enum ReviewReason
{
    /// <summary>
    /// Standard review - all data extracted, just needs verification
    /// </summary>
    NormalReview = 0,

    /// <summary>
    /// Note field was empty or couldn't be parsed into structured format
    /// (Location/Person/Category/VAT format not found)
    /// </summary>
    MissingStructuralNote = 1,

    /// <summary>
    /// Critical OCR fields missing (To or Total not extracted)
    /// </summary>
    OcrFailed = 2
}