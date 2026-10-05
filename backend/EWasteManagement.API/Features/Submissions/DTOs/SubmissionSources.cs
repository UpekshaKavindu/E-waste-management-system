namespace EWasteManagement.Api.Dtos
{
    // How a submission's items were entered. Manual: the item form (up to 3 items, optional photos).
    // Csv: a corporate account's uploaded spreadsheet (up to 100 rows, no photos).
    public static class SubmissionSources
    {
        public const string Manual = "Manual";
        public const string Csv = "Csv";

        public static bool IsCsv(string? source) => string.Equals(source, Csv, StringComparison.OrdinalIgnoreCase);

        public static bool IsKnown(string? source) =>
            string.IsNullOrWhiteSpace(source)
            || string.Equals(source, Manual, StringComparison.OrdinalIgnoreCase)
            || IsCsv(source);
    }
}
