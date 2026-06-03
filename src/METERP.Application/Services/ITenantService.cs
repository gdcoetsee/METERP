using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Application service contract for basic tenant management (Phase 1 foundation).
/// </summary>
public interface ITenantService
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken ct = default);
    Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct = default);
    Task<Guid> CreateAsync(string name, string subdomain, CancellationToken ct = default);
    Task UpdateAsync(Guid id, string name, bool isActive, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
