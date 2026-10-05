using EWasteManagement.Api.Dtos;
using FluentValidation;

namespace EWasteManagement.Api.Validators
{
    // Runs automatically before SubmissionsController.CreateSubmission
    // (FluentValidation auto-validation + [ApiController]), so an invalid
    // request gets a 400 ValidationProblemDetails with one entry per field,
    // e.g. "PhoneNumber" or "Items[0].ImageUrl".
    //
    // The rules depend on Source. Who may use CSV (corporate accounts only) depends on the caller,
    // which a validator can't see — SubmissionService enforces that.
    public class CreateSubmissionDtoValidator : AbstractValidator<CreateSubmissionDto>
    {
        public const int MaxPickupAddressLength = 300;
        public const decimal MaxEstimatedWeightKg = 1000m;

        // Manual items are classified one photo each by the Analyzer, so keep them few.
        public const int MaxItems = 3;

        // CSV rows are classified as text in batches.
        public const int MaxCsvRows = 100;
        public const int MaxCsvQuantity = 1000;
        public const decimal MaxCsvUnitWeightKg = 1000m;
        public const int MaxItemNameLength = 200;
        public const int MaxDescriptionLength = 1000;
        public const int MaxCategoryHintLength = 100;

        // Sri Lankan mobile numbers: 07XXXXXXXX or +947XXXXXXXX.
        public const string PhonePattern = @"^(07\d{8}|\+947\d{8})$";

        public CreateSubmissionDtoValidator()
        {
            RuleFor(x => x.Source)
                .Must(SubmissionSources.IsKnown)
                .WithMessage("Source must be Manual or Csv.");

            RuleFor(x => x.PickupAddress)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Pickup address is required.")
                .MaximumLength(MaxPickupAddressLength)
                .WithMessage($"Pickup address must be {MaxPickupAddressLength} characters or fewer.");

            RuleFor(x => x.PhoneNumber)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("Phone number is required.")
                .Matches(PhonePattern)
                .WithMessage("Phone number must be a Sri Lankan number in the format 07XXXXXXXX or +947XXXXXXXX.");

            RuleFor(x => x.Items)
                .NotEmpty().WithMessage("At least one item is required.");

            When(x => !SubmissionSources.IsCsv(x.Source), ManualRules);
            When(x => SubmissionSources.IsCsv(x.Source), CsvRules);
        }

        private void ManualRules()
        {
            RuleFor(x => x.Category)
                .Must(c => SubmissionCategories.All.Contains(c))
                .WithMessage($"Category must be one of: {string.Join(", ", SubmissionCategories.All)}.");

            RuleFor(x => x.EstimatedWeight)
                .GreaterThan(0).WithMessage("Estimated weight must be greater than 0 kg.")
                .LessThanOrEqualTo(MaxEstimatedWeightKg)
                .WithMessage($"Estimated weight must be at most {MaxEstimatedWeightKg:0} kg.");

            RuleFor(x => x.Items)
                .Must(items => items == null || items.Count <= MaxItems)
                .WithMessage($"A submission can have at most {MaxItems} items.");

            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ItemName)
                    .NotEmpty().WithMessage("Item name is required.");

                item.RuleFor(i => i.Description)
                    .NotEmpty().WithMessage("Item description is required.");

                // Optional: only checked when provided.
                item.RuleFor(i => i.ImageUrl)
                    .Must(BeAbsoluteHttpUrl)
                    .When(i => !string.IsNullOrWhiteSpace(i.ImageUrl))
                    .WithMessage("Image URL must be an absolute http or https URL.");

                item.RuleFor(i => i.Quantity)
                    .Equal(1).WithMessage("Manual items are entered one at a time (quantity 1).");
            });
        }

        private void CsvRules()
        {
            RuleFor(x => x.Items)
                .Must(items => items == null || items.Count <= MaxCsvRows)
                .WithMessage($"A CSV upload can have at most {MaxCsvRows} rows.");

            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ItemName)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage("Item name is required.")
                    .MaximumLength(MaxItemNameLength)
                    .WithMessage($"Item name must be {MaxItemNameLength} characters or fewer.");

                item.RuleFor(i => i.Description)
                    .MaximumLength(MaxDescriptionLength)
                    .WithMessage($"Description must be {MaxDescriptionLength} characters or fewer.");

                item.RuleFor(i => i.Quantity)
                    .InclusiveBetween(1, MaxCsvQuantity)
                    .WithMessage($"Quantity must be between 1 and {MaxCsvQuantity}.");

                item.RuleFor(i => i.EstimatedWeightKg)
                    .GreaterThan(0).WithMessage("Estimated weight per unit must be greater than 0 kg.")
                    .LessThanOrEqualTo(MaxCsvUnitWeightKg)
                    .WithMessage($"Estimated weight per unit must be at most {MaxCsvUnitWeightKg:0} kg.")
                    .When(i => i.EstimatedWeightKg.HasValue);

                item.RuleFor(i => i.Category)
                    .MaximumLength(MaxCategoryHintLength)
                    .WithMessage($"Category must be {MaxCategoryHintLength} characters or fewer.");

                item.RuleFor(i => i.ImageUrl)
                    .Empty().WithMessage("CSV rows can't have photos.");
            });
        }

        private static bool BeAbsoluteHttpUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
