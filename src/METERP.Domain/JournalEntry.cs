namespace METERP.Domain;

/// <summary>
/// Journal entry (double-entry GL) for posting costs, invoices, receipts, etc.
/// Lines must balance (Debits = Credits).
/// </summary>
public class JournalEntry : BaseEntity
{
    public string EntryNumber { get; set; } = string.Empty;

    public DateTime EntryDate { get; set; } = DateTime.UtcNow;

    public string? Description { get; set; }

    public string? Reference { get; set; } // e.g. Job #, Invoice #, PO #

    public Guid? JobId { get; set; }

    public ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
}
