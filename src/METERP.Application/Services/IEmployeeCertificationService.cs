using METERP.Domain;

namespace METERP.Application.Services;

public interface IEmployeeCertificationService
{
    Task<IReadOnlyList<EmployeeCertification>> GetForEmployeeAsync(Guid employeeId, CancellationToken ct = default);

    Task<IReadOnlyList<EmployeeCertification>> GetExpiringAsync(int withinDays = 30, CancellationToken ct = default);

    Task<Guid> CreateAsync(EmployeeCertification cert, CancellationToken ct = default);

    Task UpdateAsync(EmployeeCertification cert, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
