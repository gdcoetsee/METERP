using METERP.Domain;

namespace METERP.Application.Services;

public interface IQuotaService
{
    /// <summary>
    /// Throws <see cref="QuotaExceededException"/> when the tenant has reached its monthly limit.
    /// Resets period counters automatically when the calendar month changes.
    /// </summary>
    Task EnsureAllowedAsync(Guid tenantId, QuotaType type, CancellationToken ct = default);
}