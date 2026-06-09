namespace METERP.Domain;

/// <summary>
/// Employee / crew member for HR and labor costing.
/// Links JobLabor to real people for payroll / utilization.
/// </summary>
public class Employee : BaseEntity
{
    public string EmployeeNumber { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? JobTitle { get; set; }

    public decimal DefaultHourlyRate { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
