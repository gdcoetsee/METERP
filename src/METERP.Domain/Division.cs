namespace METERP.Domain;

/// <summary>
/// Business division / branch for accountability and performance reporting.
/// </summary>
public class Division : BaseEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public Guid? ManagerEmployeeId { get; set; }
    public Employee? Manager { get; set; }
}