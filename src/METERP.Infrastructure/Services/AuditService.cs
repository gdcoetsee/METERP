using System.Text;
using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService? _currentUser;

    public AuditService(AppDbContext dbContext, ICurrentUserService? currentUser = null)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        string entityReference,
        string details,
        CancellationToken ct = default)
    {
        var entry = new AuditLogEntry
        {
            UserEmail = _currentUser?.UserName ?? _currentUser?.UserId?.ToString() ?? "system",
            Action = action,
            EntityType = entityType,
            EntityReference = entityReference,
            Details = details,
            OccurredAtUtc = DateTime.UtcNow
        };

        _dbContext.Set<AuditLogEntry>().Add(entry);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLogRow>> GetRecentAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        return await SearchAsync(null, null, null, page, pageSize, ct);
    }

    public async Task<IReadOnlyList<AuditLogRow>> SearchAsync(
        string? entityType = null,
        string? entityReference = null,
        string? userEmail = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = _dbContext.Set<AuditLogEntry>().AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var type = entityType.Trim();
            query = query.Where(e => e.EntityType == type);
        }

        if (!string.IsNullOrWhiteSpace(entityReference))
        {
            var reference = entityReference.Trim();
            query = query.Where(e => e.EntityReference.Contains(reference));
        }

        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            var email = userEmail.Trim();
            query = query.Where(e => e.UserEmail.Contains(email));
        }

        return await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditLogRow(
                e.OccurredAtUtc,
                e.UserEmail,
                e.Action,
                e.EntityType,
                e.EntityReference,
                e.Details))
            .ToListAsync(ct);
    }

    public async Task<string> ExportCsvAsync(CancellationToken ct = default)
    {
        var rows = await GetRecentAsync(1, 5000, ct);
        var sb = new StringBuilder();
        sb.AppendLine("OccurredAtUtc,UserEmail,Action,EntityType,EntityReference,Details");
        foreach (var r in rows)
        {
            sb.AppendLine($"{r.OccurredAtUtc:O},\"{Escape(r.UserEmail)}\",\"{Escape(r.Action)}\",\"{Escape(r.EntityType)}\",\"{Escape(r.EntityReference)}\",\"{Escape(r.Details)}\"");
        }

        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("\"", "\"\"", StringComparison.Ordinal);
}