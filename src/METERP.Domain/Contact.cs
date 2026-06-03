namespace METERP.Domain;

/// <summary>
/// Individual contact person associated with a Customer.
/// </summary>
public class Contact : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; } // e.g. Mr, Mrs, Dr, Site Manager
    public string? JobTitle { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }

    public bool IsPrimary { get; set; } = false;
}
