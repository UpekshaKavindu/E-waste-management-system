using EWasteManagement.API.Features.Processing.DTOs;
using EWasteManagement.API.Features.Processing.Entities;

namespace EWasteManagement.API.Features.Processing.Services;

/// <param name="SourceItemId">The receipt line this came from (extra waste only) — kept in the snapshot.</param>
public record PaymentLineItem(string ItemType, decimal WeightKg, Guid? SourceItemId = null);

/// <summary>A rejected extra-waste line. It never affects the amount; it is only recorded in the snapshot.</summary>
public record RejectedLineItem(string ItemType, decimal WeightKg, string? Reason, Guid? SourceItemId = null);

public class PaymentContext
{
    public decimal TotalWeightKg { get; init; }
    public decimal? DistanceKm { get; init; }
    /// <summary>What the collector reported for a job; recorded in the snapshot only, not used in the amount.</summary>
    public decimal? ReportedWeightKg { get; init; }
    /// <summary>Accepted lines only — these are the lines the amount is calculated from.</summary>
    public IReadOnlyList<PaymentLineItem> LineItems { get; init; } = Array.Empty<PaymentLineItem>();
    public IReadOnlyList<RejectedLineItem> RejectedLineItems { get; init; } = Array.Empty<RejectedLineItem>();

    /// <summary>Job only: units expected vs. brought, and what was short — recorded, never priced.</summary>
    public int? ExpectedQuantity { get; init; }
    public int? ReceivedQuantity { get; init; }
    public IReadOnlyList<string> NotReceived { get; init; } = Array.Empty<string>();
}

public record PaymentCalculationResult(decimal Amount, PaymentCalculationSnapshot Snapshot);

public interface IPaymentCalculator
{
    PaymentSourceType Handles { get; }
    Task<decimal> CalculateAsync(PaymentContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same calculation as <see cref="CalculateAsync"/>, plus the exact components used, so the
    /// payment can store them and never need to be recalculated.
    /// </summary>
    Task<PaymentCalculationResult> CalculateWithBreakdownAsync(PaymentContext context, CancellationToken cancellationToken = default);
}
