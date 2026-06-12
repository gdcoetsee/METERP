namespace METERP.Domain;

/// <summary>
/// Links a job to one or more crew members (scheduling / field execution).
/// The lead technician may also be stored on <see cref="Job.AssignedEmployeeId"/>.
/// </summary>
public class JobCrewAssignment : BaseEntity
{
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
}