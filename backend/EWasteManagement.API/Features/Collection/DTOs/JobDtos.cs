using EWasteManagement.API.Features.Collection.Entities;

namespace EWasteManagement.API.Features.Collection.DTOs;

// PickupAddress is passed in explicitly by the caller (the staff dashboard,
// or whatever finalizes approval) rather than read from Submission directly —
// keeps this component decoupled from Submission's schema, as discussed.
public class CreateJobDto
{
    public Guid SubmissionId { get; set; }
    public string PickupAddress { get; set; } = string.Empty;
    public decimal? RequiredCapacityKg { get; set; }
    public DateTime? ScheduledWindowStart { get; set; }
    public DateTime? ScheduledWindowEnd { get; set; }

    // The Matcher agent's recommended collector. Used if they're still
    // eligible when the job is created; otherwise normal matching picks
    // someone and the history records that the recommendation was replaced.
    public Guid? PreferredCollectorId { get; set; }

    // The Matcher chose not to auto-assign: create the job unassigned as
    // AwaitingStaffAssignment so staff pick the collector.
    public bool SkipAutoAssign { get; set; }
    public string? MatcherReasoning { get; set; }
}

public class RejectJobDto
{
    public string? Reason { get; set; }
}

public class CompleteJobDto
{
    public string PhotoUrl { get; set; } = string.Empty;
    public decimal MeasuredWeightKg { get; set; }
    public string? Notes { get; set; }
}

public class JobResponseDto
{
    public Guid JobId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid? CollectorId { get; set; }
    public string? CollectorName { get; set; }
    public string Status { get; set; } = string.Empty;

    public string PickupAddress { get; set; } = string.Empty;
    public decimal? PickupLatitude { get; set; }
    public decimal? PickupLongitude { get; set; }

    public decimal? RequiredCapacityKg { get; set; }
    public DateTime? ScheduledWindowStart { get; set; }
    public DateTime? ScheduledWindowEnd { get; set; }
    public int? EstimatedEtaMinutes { get; set; }
    public decimal? EstimatedDistanceKm { get; set; }

    public string? PhotoUrl { get; set; }
    public decimal? MeasuredWeightKg { get; set; }
    public string? Notes { get; set; }
    public string? RejectionReason { get; set; }
    public string? MatcherReasoning { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Completed and weighed into inventory. A completed job that is not yet received is still in the vehicle.
    public bool ReceivedAtWarehouse { get; set; }
}


// --- Staff actions -------------------------------------------------------

// PUT /api/v1/jobs/{id}/address — fix an address that couldn't be geocoded.
public class UpdateJobAddressDto
{
    public string PickupAddress { get; set; } = string.Empty;
}

// PUT /api/v1/jobs/{id}/reassign
// CollectorId set   -> staff hand-pick that collector.
// CollectorId null  -> re-run automatic matching (e.g. new collectors came online).
public class ReassignJobDto
{
    public Guid? CollectorId { get; set; }
}

public class JobAssignmentHistoryDto
{
    public Guid HistoryId { get; set; }
    public Guid CollectorId { get; set; }
    public string CollectorName { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; }
}

// GET /api/v1/jobs/{id}/route — what the collector app draws on the job's map.
public class JobRouteDto
{
    public decimal? OriginLatitude { get; set; }
    public decimal? OriginLongitude { get; set; }
    public decimal? PickupLatitude { get; set; }
    public decimal? PickupLongitude { get; set; }

    public decimal? DistanceKm { get; set; }
    public int? DurationMinutes { get; set; }

    // The road path as [lat, lng] pairs, origin first. Empty when no route could be found
    // (no origin yet, unresolved pickup, or the routing service failed) — the app still shows the pins.
    public List<double[]> Points { get; set; } = new();
}

// GET /api/v1/jobs/{id}/collector-info — for the assigned collector only.
public class CollectorJobInfoDto
{
    // Customer contact: filled only while the job is Accepted or InProgress.
    public bool ContactAvailable { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }

    // The real payment once the warehouse has received the job, otherwise an estimate with the same
    // formula (200 + weight × rate + distance × 15). Null when it can't be worked out yet.
    public decimal? PaymentAmount { get; set; }
    public bool PaymentIsEstimate { get; set; }
    public string? PaymentStatus { get; set; }   // "Pending" | "Paid" once a real payment exists

    // Estimate breakdown (estimates only).
    public decimal? EstimateWeightKg { get; set; }
    public decimal? BaseFee { get; set; }
    public decimal? RatePerKg { get; set; }
    public decimal? WeightAmount { get; set; }
    public decimal? DistanceKm { get; set; }
    public decimal? DistanceAmount { get; set; }
}
