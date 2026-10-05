using FluentValidation;

namespace EWasteManagement.API.Features.Processing.DTOs;

// POST /api/v1/inventory/job-collection/receive-delivery
public class ReceiveDeliveryRequest
{
    public Guid CollectorId { get; set; }
    public Guid WarehouseLocationId { get; set; }
    public string? Notes { get; set; }
    public List<DeliveryJobLine> Jobs { get; set; } = new();
}

public class DeliveryJobLine
{
    public Guid JobId { get; set; }

    /// <summary>
    /// One line per submission item (manual item or CSV row). Items not listed count as not brought.
    /// Required whenever the job's submission has items.
    /// </summary>
    public List<DeliveryItemLine> Items { get; set; } = new();

    /// <summary>Whole-job receiving, only for a job whose submission has no items.</summary>
    public decimal VerifiedWeightKg { get; set; }

    /// <summary>Whole-job receiving only: from the item-type list; falls back to the submission category.</summary>
    public string? ItemType { get; set; }
}

public class DeliveryItemLine
{
    public Guid SubmissionItemId { get; set; }

    /// <summary>Units actually brought, 0 to the expected quantity. 0 = not brought: nothing is created.</summary>
    public int ReceivedQuantity { get; set; }

    /// <summary>Required when ReceivedQuantity > 0.</summary>
    public string? ItemType { get; set; }

    /// <summary>The whole row's weight on the scale (all its units together). Required when ReceivedQuantity > 0.</summary>
    public decimal VerifiedWeightKg { get; set; }
}

public class ReceiveDeliveryRequestValidator : AbstractValidator<ReceiveDeliveryRequest>
{
    public ReceiveDeliveryRequestValidator()
    {
        RuleFor(x => x.CollectorId).NotEmpty();
        RuleFor(x => x.WarehouseLocationId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Jobs).NotEmpty().WithMessage("Select at least one job.");
        RuleFor(x => x.Jobs.Count).LessThanOrEqualTo(50);
        RuleFor(x => x.Jobs)
            .Must(jobs => jobs.Select(j => j.JobId).Distinct().Count() == jobs.Count)
            .WithMessage("The same job is listed more than once.");
        RuleForEach(x => x.Jobs).ChildRules(job =>
        {
            job.RuleFor(j => j.JobId).NotEmpty();
            job.RuleFor(j => j.ItemType).MaximumLength(50);
            job.RuleFor(j => j.VerifiedWeightKg).GreaterThan(0).When(j => j.Items.Count == 0)
                .WithMessage("Enter the verified weight.");

            job.RuleFor(j => j.Items)
                .Must(items => items.Select(i => i.SubmissionItemId).Distinct().Count() == items.Count)
                .WithMessage("The same item is listed more than once.")
                .Must(items => items.Count == 0 || items.Any(i => i.ReceivedQuantity > 0))
                .WithMessage("Nothing from this job was received — untick it instead.");

            job.RuleForEach(j => j.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.SubmissionItemId).NotEmpty();
                item.RuleFor(i => i.ReceivedQuantity).GreaterThanOrEqualTo(0);
                item.RuleFor(i => i.ItemType)
                    .NotEmpty().WithMessage("Choose the item type.")
                    .MaximumLength(50)
                    .When(i => i.ReceivedQuantity > 0);
                item.RuleFor(i => i.VerifiedWeightKg)
                    .GreaterThan(0).WithMessage("Enter the verified weight.")
                    .When(i => i.ReceivedQuantity > 0);
            });
        });
    }
}

public class DeliveryJobResult
{
    public Guid JobId { get; set; }

    /// <summary>Sum of the received rows — what the payment is calculated from.</summary>
    public decimal VerifiedWeightKg { get; set; }
    public decimal? ReportedWeightKg { get; set; }
    public decimal? DiscrepancyKg { get; set; }

    /// <summary>Units expected vs. brought, across all of the job's items.</summary>
    public int ExpectedQuantity { get; set; }
    public int ReceivedQuantity { get; set; }

    public List<DeliveryItemResult> Items { get; set; } = new();
    public Guid PaymentId { get; set; }
    public decimal PaymentAmount { get; set; }
}

public class DeliveryItemResult
{
    /// <summary>Null for a job received as a whole.</summary>
    public Guid? SubmissionItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int ExpectedQuantity { get; set; }
    public int ReceivedQuantity { get; set; }

    /// <summary>Null when nothing was brought (no inventory item created).</summary>
    public Guid? InventoryItemId { get; set; }
    public string? ItemType { get; set; }
    public decimal VerifiedWeightKg { get; set; }
}

public class ReceiveDeliveryResponse
{
    public Guid DeliveryId { get; set; }
    public Guid CollectorId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public List<DeliveryJobResult> Jobs { get; set; } = new();
    public decimal TotalPendingAmount { get; set; }
}

// GET /api/v1/payments/deliveries/{id}
public class DeliveryPaymentLine
{
    public Guid PaymentId { get; set; }
    public Guid JobId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
}

public class DeliverySummaryResponse
{
    public Guid DeliveryId { get; set; }
    public Guid CollectorId { get; set; }
    public string? CollectorName { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ReceivedByName { get; set; }
    public string? Notes { get; set; }
    public List<DeliveryPaymentLine> Payments { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public decimal PendingAmount { get; set; }
    public decimal PaidAmount { get; set; }
}
