using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Sales Order service for Quote -> SO -> Job workflow.
/// </summary>
public interface ISalesOrderService
{
    Task<SalesOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SalesOrder>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(SalesOrder so, CancellationToken ct = default);
    Task UpdateAsync(SalesOrder so, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid soId, SalesOrderStatus newStatus, CancellationToken ct = default);

    // Line management
    Task<Guid> AddLineAsync(SalesOrderLine line, CancellationToken ct = default);
    Task UpdateLineAsync(SalesOrderLine line, CancellationToken ct = default);
    Task DeleteLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>
    /// Convert accepted/confirmed SO to Job (similar to Quote conversion).
    /// </summary>
    Task<Job> ConvertToJobAsync(Guid soId, CancellationToken ct = default);
}
