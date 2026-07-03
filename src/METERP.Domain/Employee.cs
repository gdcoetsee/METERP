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

    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }

    /// <summary>Linked login user for Field Portal / self-service leave.</summary>
    public Guid? LinkedUserId { get; set; }

    public Guid? ManagerEmployeeId { get; set; }
    public Employee? Manager { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }

    public DateTime? HireDate { get; set; }

    public decimal AnnualLeaveEntitlementDays { get; set; } = 15m;

    /// <summary>Running balance after approved leave (accrual minus taken).</summary>
    public decimal LeaveBalanceDays { get; set; }
}
