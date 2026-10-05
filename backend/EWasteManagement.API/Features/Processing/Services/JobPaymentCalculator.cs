using EWasteManagement.API.Features.Processing.DTOs;
using EWasteManagement.API.Features.Processing.Entities;

namespace EWasteManagement.API.Features.Processing.Services;

public class JobPaymentCalculator : IPaymentCalculator
{
    // Named constants rather than another DB-driven policy table — two numbers don't earn a
    // table of their own yet. Worth one sentence in the ADR if asked why at viva.
    private const decimal BaseCollectionFee = 200m;
    private const decimal PerKmRate = 15m;

    /// <summary>
    /// The rate-policy key that prices the weight part of a job payment. It is not a real item
    /// type, so it must never be accepted as an extra-waste item.
    /// </summary>
    public const string GeneralCollectionItemType = "GeneralCollection";

    private readonly IRatePolicyLookupService _rates;
    public JobPaymentCalculator(IRatePolicyLookupService rates) => _rates = rates;

    public PaymentSourceType Handles => PaymentSourceType.Job;

    public async Task<decimal> CalculateAsync(PaymentContext context, CancellationToken cancellationToken = default)
        => (await CalculateWithBreakdownAsync(context, cancellationToken)).Amount;

    public async Task<PaymentCalculationResult> CalculateWithBreakdownAsync(PaymentContext context, CancellationToken cancellationToken = default)
    {
        var rate = await _rates.GetActiveRateAsync(GeneralCollectionItemType, cancellationToken)
            ?? throw new KeyNotFoundException($"No active rate policy for '{GeneralCollectionItemType}'.");

        // The formula is unchanged: 200 + (weight × rate) + (distance × 15), rounded once to 2 dp.
        // The components are only split out so they can be stored alongside the amount.
        var distanceUsed = context.DistanceKm ?? 0m;
        var weightAmount = context.TotalWeightKg * rate.RatePerKg;
        var distanceAmount = distanceUsed * PerKmRate;
        var amount = Math.Round(BaseCollectionFee + weightAmount + distanceAmount, 2);

        var snapshot = new PaymentCalculationSnapshot
        {
            SourceType = PaymentSourceType.Job.ToString(),
            TotalAmount = amount,
            Job = new JobCalculationSnapshot
            {
                VerifiedWeightKg = context.TotalWeightKg,
                ReportedWeightKg = context.ReportedWeightKg,
                DiscrepancyKg = context.ReportedWeightKg.HasValue ? context.TotalWeightKg - context.ReportedWeightKg.Value : null,
                WeightRateItemType = rate.ItemType,
                RatePerKg = rate.RatePerKg,
                WeightAmount = weightAmount,
                BaseFee = BaseCollectionFee,
                DistanceKm = context.DistanceKm,
                DistanceUsedKm = distanceUsed,
                DistanceRatePerKm = PerKmRate,
                DistanceAmount = distanceAmount,
                ExpectedQuantity = context.ExpectedQuantity,
                ReceivedQuantity = context.ReceivedQuantity,
                NotReceived = context.NotReceived.ToList()
            }
        };

        return new PaymentCalculationResult(amount, snapshot);
    }
}
