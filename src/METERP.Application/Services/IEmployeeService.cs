using METERP.Domain;

namespace METERP.Application.Services;

public interface IEmployeeService
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Employee>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(Employee emp, CancellationToken ct = default);
    Task UpdateAsync(Employee emp, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
