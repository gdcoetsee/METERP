using Microsoft.EntityFrameworkCore;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Locks Identity users that still belong to a soft-deleted tenant.
/// The row stays (claims and roles are foreign-keyed). Office and E2E demo logins are never locked.
/// </summary>
public static class SoftDeletedTenantUserLock
{
    /// <summary>Demo logins that must keep working even if their tenant row is soft-deleted.</summary>
    public static readonly string[] ProtectedEmails =
    [
        "admin@met.demo",
        "gregory@met.co.za",
        "admin@acme.demo",
        "manager@acme.demo",
        "tech@acme.demo",
        "tech@met.demo",
        "portal@met.demo"
    ];

    public readonly record struct Result(int Locked, int AlreadyLocked, IReadOnlyList<string> Emails)
    {
        public string Summary =>
            $"locked={Locked} already={AlreadyLocked} emails={string.Join(",", Emails)}";
    }

    public static async Task<Result> RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        var deletedTenantIds = await db.Set<Tenant>().IgnoreQueryFilters()
            .Where(t => t.IsDeleted)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (deletedTenantIds.Count == 0)
            return new Result(0, 0, Array.Empty<string>());

        var users = await db.Users.IgnoreQueryFilters()
            .Where(u => deletedTenantIds.Contains(u.TenantId))
            .ToListAsync(ct);

        var lockUntil = DateTimeOffset.UtcNow.AddYears(100);
        var alreadyFar = DateTimeOffset.UtcNow.AddYears(50);
        var newlyLocked = new List<string>();
        var already = 0;

        foreach (var user in users)
        {
            if (IsProtected(user.Email))
                continue;

            if (user.LockoutEnabled && user.LockoutEnd is { } end && end >= alreadyFar)
            {
                already++;
                continue;
            }

            user.LockoutEnabled = true;
            user.LockoutEnd = lockUntil;
            newlyLocked.Add(user.Email ?? user.UserName ?? user.Id.ToString());
        }

        if (newlyLocked.Count > 0)
            await db.SaveChangesAsync(ct);

        return new Result(newlyLocked.Count, already, newlyLocked);
    }

    public static bool IsProtected(string? email) =>
        email != null && ProtectedEmails.Contains(email, StringComparer.OrdinalIgnoreCase);
}
