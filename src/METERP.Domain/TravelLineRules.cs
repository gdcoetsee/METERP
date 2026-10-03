namespace METERP.Domain;

/// <summary>
/// Travel is its own line. Convert paths must not file it as Material.
/// </summary>
public static class TravelLineRules
{
    public const string TravelType = "Travel";
    public const string MaterialType = "Material";

    public static bool MentionsTravel(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains("travel", StringComparison.OrdinalIgnoreCase);

    public static bool IsTravelLine(string? lineType, string? description) =>
        string.Equals(lineType?.Trim(), TravelType, StringComparison.OrdinalIgnoreCase)
        || MentionsTravel(description);

    /// <summary>
    /// A materials or untyped line that is about travel becomes Travel.
    /// Labour stays labour. Travel stays Travel.
    /// </summary>
    public static string EnsureExplicitType(string? lineType, string? description)
    {
        var type = string.IsNullOrWhiteSpace(lineType) ? MaterialType : lineType.Trim();
        if (string.Equals(type, TravelType, StringComparison.OrdinalIgnoreCase))
            return TravelType;
        if (!MentionsTravel(description))
            return type;
        if (string.Equals(type, MaterialType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "Other", StringComparison.OrdinalIgnoreCase))
            return TravelType;
        return type;
    }

    /// <summary>
    /// Job-cost type for a document line. Travel is never Material.
    /// </summary>
    public static string JobCostType(string? lineType, string? description)
    {
        if (IsTravelLine(lineType, description))
            return TravelType;
        var type = string.IsNullOrWhiteSpace(lineType) ? "Other" : lineType.Trim();
        return string.Equals(type, MaterialType, StringComparison.OrdinalIgnoreCase)
            ? MaterialType
            : type;
    }
}
