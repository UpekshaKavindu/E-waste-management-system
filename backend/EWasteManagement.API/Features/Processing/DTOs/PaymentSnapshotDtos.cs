using System.Text.Json;

namespace EWasteManagement.API.Features.Processing.DTOs;

/// <summary>Single place that writes/reads the snapshot JSON, so the stored format stays consistent.</summary>
public static class PaymentSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(PaymentCalculationSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    /// <summary>Null when there is no snapshot or it cannot be read — the caller then reports "no saved breakdown".</summary>
    public static PaymentCalculationSnapshot? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<PaymentCalculationSnapshot>(json, Options); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// The exact calculation used when a collector payment was created, stored as JSON on the payment
/// and shown as-is afterwards. It is never recomputed from today's rates.
/// </summary>
public class PaymentCalculationSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>"Job" or "ExtraWaste".</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>The final amount, after the existing single rounding to 2 decimals.</summary>
    public decimal TotalAmount { get; set; }

    public JobCalculationSnapshot? Job { get; set; }
    public ExtraWasteCalculationSnapshot? ExtraWaste { get; set; }
}

public class JobCalculationSnapshot
{
    public decimal VerifiedWeightKg { get; set; }
    public decimal? ReportedWeightKg { get; set; }
    public decimal? DiscrepancyKg { get; set; }

    /// <summary>The rate-policy key the weight component was priced with (always "GeneralCollection").</summary>
    public string WeightRateItemType { get; set; } = string.Empty;
    public decimal RatePerKg { get; set; }
    /// <summary>VerifiedWeightKg × RatePerKg.</summary>
    public decimal WeightAmount { get; set; }

    public decimal BaseFee { get; set; }

    /// <summary>The job's estimated distance, or null when none was known.</summary>
    public decimal? DistanceKm { get; set; }
    /// <summary>The distance actually used in the calculation (0 when DistanceKm is null).</summary>
    public decimal DistanceUsedKm { get; set; }
    public decimal DistanceRatePerKm { get; set; }
    /// <summary>DistanceUsedKm × DistanceRatePerKm.</summary>
    public decimal DistanceAmount { get; set; }

    /// <summary>Units the submission listed vs. units the collector brought (null before per-item receiving).</summary>
    public int? ExpectedQuantity { get; set; }
    public int? ReceivedQuantity { get; set; }

    /// <summary>Items short, e.g. "Router (0 of 1)". Not priced: pay already follows the weight brought.</summary>
    public List<string> NotReceived { get; set; } = new();
}

public class ExtraWasteCalculationSnapshot
{
    /// <summary>Accepted lines first, then rejected lines (which always contribute Rs. 0).</summary>
    public List<ExtraWasteLineSnapshot> Lines { get; set; } = new();
}

public class ExtraWasteLineSnapshot
{
    /// <summary>The receipt line this row came from, so it can be matched back to the receipt.</summary>
    public Guid? ReceiptItemId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public bool Accepted { get; set; }

    /// <summary>The rate applied to this line; null for rejected lines (no rate is applied).</summary>
    public decimal? RatePerKg { get; set; }
    /// <summary>WeightKg × RatePerKg for accepted lines (unrounded); always 0 for rejected lines.</summary>
    public decimal Amount { get; set; }
    /// <summary>False for rejected lines: they are kept for traceability but excluded from the total.</summary>
    public bool IncludedInTotal { get; set; }
    public string? RejectionReason { get; set; }
}
