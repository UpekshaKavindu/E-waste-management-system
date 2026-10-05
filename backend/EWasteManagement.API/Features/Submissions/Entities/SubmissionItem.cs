namespace EWasteManagement.Api.Entities
{
    public class SubmissionItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SubmissionId { get; set; }

        // 0-based order as entered (CSV row order). Rows are numbered from this, so it must be stable.
        public int Position { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ImageUrl { get; set; } = string.Empty;

        // CSV rows describe several identical units ("40 × Dell monitor"); manual items are always 1.
        public int Quantity { get; set; } = 1;

        // Per unit, as given in the CSV. Preferred over the Analyzer's own estimate when present.
        public decimal? EstimatedWeightKg { get; set; }

        // The CSV's category column — only a hint for the Analyzer, never trusted as the classification.
        public string? CategoryHint { get; set; }
    }
}