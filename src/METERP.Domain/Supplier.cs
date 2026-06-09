namespace METERP.Domain;

/// <summary>
/// Supplier / Vendor for purchasing (to replenish Inventory).
/// Completes the supply chain side of the contracting ERP (purchase -> stock -> quote/job use).
/// </summary>
public class Supplier : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? ContactPerson { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? Province { get; set; }

    public string? PostalCode { get; set; }

    public string? Country { get; set; } = "South Africa";

    public string? TaxNumber { get; set; } // VAT / Tax ID for supplier invoices

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
