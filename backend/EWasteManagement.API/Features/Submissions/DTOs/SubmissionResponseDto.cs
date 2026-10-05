using EWasteManagement.API.Features.Workflow.DTOs;

namespace EWasteManagement.Api.Dtos
{
    // What the submissions API returns. Status is never read from the
    // Submission row — it's derived from the submission's Job (once one
    // exists) or its CollectionWorkflow; see SubmissionStatusResolver.
    public class SubmissionResponseDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserType { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal EstimatedWeight { get; set; }
        public string PickupAddress { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Source { get; set; } = SubmissionSources.Manual;
        public List<SubmissionItemResponseDto> Items { get; set; } = new();

        /// <summary>Machine-readable status code, e.g. "Analyzing", "CollectorAssigned".</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Human-readable label for Status, e.g. "Collector assigned".</summary>
        public string StatusLabel { get; set; } = string.Empty;

        /// <summary>Why the submission is Failed / Rejected / Closed; null otherwise.</summary>
        public string? StatusReason { get; set; }

        public SubmissionWorkflowDto? Workflow { get; set; }

        public Guid? JobId { get; set; }
        public string? JobStatus { get; set; }
    }

    public class SubmissionItemResponseDto
    {
        public Guid Id { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public decimal? EstimatedWeightKg { get; set; }
        public string? CategoryHint { get; set; }
    }

    public class SubmissionWorkflowDto
    {
        public Guid WorkflowId { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool ApprovalRequired { get; set; }

        /// <summary>Null until the Analyzer agent has run.</summary>
        public SubmissionAnalysisDto? Analysis { get; set; }
    }

    public class SubmissionAnalysisDto
    {
        public string WasteCategory { get; set; } = string.Empty;
        public string HazardLevel { get; set; } = string.Empty;
        public decimal EstimatedVolumeKg { get; set; }
        public decimal EstimatedValueLkr { get; set; }
        public double ConfidenceScore { get; set; }

        // Each item's own classification; empty for submissions analysed before per-item analysis.
        public List<AnalyzedItem> Items { get; set; } = new();
    }
}
