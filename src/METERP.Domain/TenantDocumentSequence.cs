namespace METERP.Domain;

/// <summary>
/// Per-tenant sequential document numbers (quote, job, invoice, PO, etc.) reset yearly.
/// </summary>
public class TenantDocumentSequence : BaseEntity
{
    /// <summary>Document family key, e.g. Quote, Job, Invoice, PurchaseOrder.</summary>
    public string DocumentType { get; set; } = string.Empty;

    public int Year { get; set; }

    public int NextNumber { get; set; } = 1;
}