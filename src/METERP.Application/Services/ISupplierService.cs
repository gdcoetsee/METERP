using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Supplier / Vendor management (purchasing side).
/// </summary>
public interface ISupplierService
{
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Supplier>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(Supplier supplier, CancellationToken ct = default);
    Task UpdateAsync(Supplier supplier, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
