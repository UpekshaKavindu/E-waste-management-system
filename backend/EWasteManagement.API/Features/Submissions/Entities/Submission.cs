namespace EWasteManagement.Api.Entities
{
    public class Submission
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public string UserType { get; set; } = "Household";
        public string Category { get; set; } = string.Empty;
        public decimal EstimatedWeight { get; set; }
        public string PickupAddress { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // "Manual" (item form) or "Csv" (corporate spreadsheet upload) — see SubmissionSources.
        public string Source { get; set; } = "Manual";

        public ICollection<SubmissionItem> Items { get; set; } = new List<SubmissionItem>();
    }
}
