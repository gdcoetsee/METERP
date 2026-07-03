using METERP.Domain;

namespace METERP.Application.Services;

public interface IDivisionService
{
    Task<IReadOnlyList<Division>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default);
    Task<Division?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAsync(Division division, CancellationToken ct = default);
    Task UpdateAsync(Division division, CancellationToken ct = default);
}