namespace METERP.Application.Services;

public interface IAuditService
{
    Task LogAsync(
        string action,
        string entityType,
        string entityReference,
        string details,
        CancellationToken ct = default);

    Task<IReadOnlyList<AuditLogRow>> GetRecentAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);

    Task<string> ExportCsvAsync(CancellationToken ct = default);
}

public record AuditLogRow(
    DateTime OccurredAtUtc,
    string UserEmail,
    string Action,
    string EntityType,
    string EntityReference,
    string Details);