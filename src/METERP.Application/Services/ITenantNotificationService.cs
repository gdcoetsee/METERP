using METERP.Domain;

namespace METERP.Application.Services;

public interface ITenantNotificationService
{
    Task<IReadOnlyList<TenantNotification>> GetForCurrentUserAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(CancellationToken ct = default);

    Task CreateAsync(TenantNotification notification, CancellationToken ct = default);

    Task MarkReadAsync(Guid id, CancellationToken ct = default);

    Task MarkAllReadAsync(CancellationToken ct = default);
}