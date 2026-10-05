namespace EWasteManagement.Api.Dtos
{
    // UserId and UserType are not accepted from the client — the controller
    // takes them from the caller's JWT (sub claim and role).
    public class CreateSubmissionDto
    {
        // "Manual" (default) or "Csv". Csv is only accepted from corporate accounts.
        public string Source { get; set; } = SubmissionSources.Manual;

        // Manual only: a CSV submission's category and total weight are worked out from its rows.
        public string Category { get; set; } = string.Empty;
        public decimal EstimatedWeight { get; set; }
        public string PickupAddress { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public List<CreateSubmissionItemDto> Items { get; set; } = new();
    }

    public class CreateSubmissionItemDto
    {
        public string ItemName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;

        // CSV rows only.
        public int Quantity { get; set; } = 1;
        public decimal? EstimatedWeightKg { get; set; }   // per unit
        public string? Category { get; set; }              // hint
    }
}
