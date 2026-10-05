namespace EWasteManagement.API.Features.Processing.DTOs;

/// <summary>
/// A completed job that has an assigned collector and has NOT yet been received into inventory.
/// Filtered on the server, so a page refresh never brings received jobs back.
/// </summary>
public class ReceivableJobResponse
{
    public Guid JobId { get; set; }
    public Guid CollectorId { get; set; }
    public string? CollectorName { get; set; }
    public string? CollectorVehicleType { get; set; }
    public string PickupAddress { get; set; } = string.Empty;
    public decimal? ReportedWeightKg { get; set; }
    public decimal? EstimatedDistanceKm { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>The category the customer chose on the submission, as they wrote it (null if unknown).</summary>
    public string? SubmissionCategory { get; set; }

    /// <summary>
    /// <see cref="SubmissionCategory"/> matched to the item-type list, pre-selected on the receive form.
    /// Null when the category is not on the list — staff must choose the type.
    /// </summary>
    public string? SuggestedItemType { get; set; }

    /// <summary>
    /// What the customer submitted, in order — one row per item or CSV row. The warehouse receives
    /// each one separately. Empty only for a job whose submission has no items (received as a whole).
    /// </summary>
    public List<ReceivableJobItem> Items { get; set; } = new();
}

public class ReceivableJobItem
{
    public Guid SubmissionItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Units expected (CSV quantity; 1 for manual items).</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Best guess at the whole row's weight, for splitting a total across rows: the customer's CSV
    /// per-unit weight × quantity, else the Analyzer's estimate for the item. Null when neither exists.
    /// </summary>
    public decimal? ExpectedWeightKg { get; set; }

    /// <summary>A type from the item-type list, pre-selected on the form. Null when nothing matched.</summary>
    public string? SuggestedItemType { get; set; }

    /// <summary>What the suggestion came from: "name", "category", "ai" or "submission".</summary>
    public string? SuggestionSource { get; set; }
}
