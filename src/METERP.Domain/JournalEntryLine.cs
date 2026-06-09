namespace METERP.Domain;

/// <summary>
/// Line in a JournalEntry (Debit or Credit).
/// </summary>
public class JournalEntryLine : BaseEntity
{
    public Guid JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;

    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }

    public string? Memo { get; set; }
}
