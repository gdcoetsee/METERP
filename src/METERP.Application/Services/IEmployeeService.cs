using METERP.Domain;

namespace METERP.Application.Services;

public interface IEmployeeService
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <param name="includeInactive">When true, lists inactive (not soft-deleted) employees too.</param>
    Task<IReadOnlyList<Employee>> GetAllAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        bool includeInactive = false,
        CancellationToken ct = default);

    Task<Guid> CreateAsync(Employee emp, CancellationToken ct = default);

    /// <summary>Load-then-patch update so unset UI fields do not wipe HR data.</summary>
    Task UpdateAsync(Employee emp, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
