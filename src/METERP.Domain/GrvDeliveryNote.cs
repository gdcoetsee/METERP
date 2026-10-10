namespace METERP.Domain;

/// <summary>
/// Supplier delivery note as shown on the GRV register.
/// A missing or blank note is a dash so stores can scan the column.
/// </summary>
public static class GrvDeliveryNote
{
    public const string Blank = "—";

    public static string Show(string? note) =>
        string.IsNullOrWhiteSpace(note) ? Blank : note.Trim();
}
