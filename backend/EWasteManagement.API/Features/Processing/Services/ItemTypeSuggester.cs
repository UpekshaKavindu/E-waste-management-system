using System.Text.RegularExpressions;

namespace EWasteManagement.API.Features.Processing.Services;

/// <summary>
/// Picks the item type to pre-select for a received job item, from what is already known about it.
/// Only a suggestion: staff confirm or change it on the receive form, and it is null when nothing
/// matches (staff then choose). Checked in order of how specific the clue is:
/// the item's own name, the CSV category column, the Analyzer's category for that item, and last
/// the category of the whole submission.
/// </summary>
public static class ItemTypeSuggester
{
    public const string FromName = "name";
    public const string FromCategory = "category";
    public const string FromAi = "ai";
    public const string FromSubmission = "submission";

    // Everyday words for the same thing. A type gets a word's synonyms when that word is in its name,
    // so this keeps working when admins add types such as "CRT Monitor" or "Printer".
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["battery"] = new[] { "battery", "batteries", "ups", "power bank", "powerbank", "accumulator" },
        ["laptop"] = new[] { "laptop", "laptops", "notebook", "macbook", "chromebook", "ultrabook" },
        ["phone"] = new[] { "phone", "phones", "mobile", "smartphone", "iphone", "android", "cellphone", "cell phone" },
        ["monitor"] = new[] { "monitor", "monitors", "display", "screen" },
        ["printer"] = new[] { "printer", "printers", "photocopier", "copier", "scanner" },
        ["television"] = new[] { "television", "tv", "tvs" },
        ["tablet"] = new[] { "tablet", "tablets", "ipad" },
        ["desktop"] = new[] { "desktop", "cpu", "tower", "pc" },
        ["cable"] = new[] { "cable", "cables", "wire", "wires", "charger", "chargers" },
    };

    // Too general to identify a type on their own.
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "general", "household", "electronics", "electronic", "equipment", "other", "item", "items", "assorted", "mixed",
    };

    // Customer / Analyzer categories that name a type differently.
    private static readonly Dictionary<string, string> CategoryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["batteries"] = "Battery",
        ["household electronics"] = "General Household Electronics",
        ["mobile phones"] = "Mobile Phone",
        ["phones"] = "Mobile Phone",
        ["laptops"] = "Laptop",
    };

    public static (string? Type, string? Source) Suggest(
        IReadOnlyList<string> types, string? itemName, string? categoryHint, string? aiCategory, string? submissionCategory)
    {
        if (types.Count == 0) return (null, null);

        if (MatchText(types, itemName) is { } byName) return (byName, FromName);
        if (MatchCategory(types, categoryHint) is { } byHint) return (byHint, FromCategory);
        if (MatchCategory(types, aiCategory) is { } byAi) return (byAi, FromAi);
        if (MatchCategory(types, submissionCategory) is { } bySubmission) return (bySubmission, FromSubmission);
        return (null, null);
    }

    /// <summary>A category: the type itself, a known alias, a plural, then its words like a name.</summary>
    public static string? MatchCategory(IReadOnlyList<string> types, string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        var wanted = category.Trim();

        var exact = types.FirstOrDefault(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        if (CategoryAliases.TryGetValue(wanted, out var alias))
        {
            var aliased = types.FirstOrDefault(t => string.Equals(t, alias, StringComparison.OrdinalIgnoreCase));
            if (aliased is not null) return aliased;
        }

        if (wanted.EndsWith('s'))
        {
            var singular = types.FirstOrDefault(t => string.Equals(t, wanted[..^1], StringComparison.OrdinalIgnoreCase));
            if (singular is not null) return singular;
        }

        return MatchText(types, wanted);
    }

    /// <summary>Free text: the type whose keyword appears as a whole word, longest keyword first.</summary>
    public static string? MatchText(IReadOnlyList<string> types, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string? best = null;
        var bestLength = 0;
        foreach (var type in types)
        {
            foreach (var keyword in KeywordsFor(type))
            {
                if (keyword.Length <= bestLength) continue;
                if (Regex.IsMatch(text, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
                {
                    best = type;
                    bestLength = keyword.Length;
                }
            }
        }
        return best;
    }

    private static IEnumerable<string> KeywordsFor(string type)
    {
        var words = Regex.Split(type.ToLowerInvariant(), @"[^a-z0-9]+").Where(w => w.Length >= 2).ToList();
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { type };

        foreach (var word in words)
        {
            if (!GenericWords.Contains(word) && word.Length >= 3)
            {
                keywords.Add(word);
                keywords.Add(word + "s");
            }
            if (Synonyms.TryGetValue(word, out var synonyms))
                keywords.UnionWith(synonyms);
        }
        return keywords;
    }
}
