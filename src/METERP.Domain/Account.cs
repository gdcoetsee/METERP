namespace METERP.Domain;

/// <summary>
/// Chart of Accounts entry (minimal GL for contracting ERP).
/// Supports job costing roll-up and basic financial statements.
/// </summary>
public class Account : BaseEntity
{
    public string AccountCode { get; set; } = string.Empty; // e.g. 1000, 4000

    public string Name { get; set; } = string.Empty;

    public AccountType Type { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Description { get; set; }
}
