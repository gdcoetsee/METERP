namespace METERP.Domain;

/// <summary>
/// Customer (client) in the contracting business.
/// Can be a company or individual.
/// </summary>
public class Customer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? CompanyRegistrationNumber { get; set; }
    public string? VatNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; } = "South Africa";

    public string? Notes { get; set; }

    /// <summary>
    /// Days after the invoice date until payment is due.
    /// Null or 0 means <see cref="CustomerPaymentTerms.DefaultDays"/>. Allowed range is 0–180.
    /// </summary>
    public int? PaymentTermsDays { get; set; }

    // Navigation
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
}
