using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Thin South African chart for the MET Electrical tenant.
/// Idempotent: a tenant that already has any live account is left unchanged.
/// Opening balances stay zero — this seed does not post journals.
/// </summary>
public static class MetChartOfAccountsSeeder
{
    public const string StubDescription =
        "Stub / MET FY demo. Zero opening balance — journals are not posted by this seed.";

    public static readonly IReadOnlyList<StubAccount> StubAccounts =
    [
        new("1000", "Bank / Cash", AccountType.Asset),
        new("1100", "Accounts Receivable (Debtors)", AccountType.Asset),
        new("1200", "Inventory / Stock", AccountType.Asset),
        new("2000", "Accounts Payable (Creditors)", AccountType.Liability),
        new("2200", "VAT Control 15% (SARS VAT)", AccountType.Liability),
        new("3000", "Owner's Equity", AccountType.Equity),
        new("3100", "Retained Earnings", AccountType.Equity),
        new("4000", "Contracting Revenue", AccountType.Revenue),
        new("4100", "Travel Recoverable", AccountType.Revenue),
        new("5000", "Materials & Supplies", AccountType.Expense),
        new("5100", "Direct Labor", AccountType.Expense),
        new("5200", "Travel & Transport", AccountType.Expense),
        new("5300", "Cost of Sales", AccountType.Expense),
        new("6000", "Overhead", AccountType.Expense)
    ];

    public readonly record struct StubAccount(string Code, string Name, AccountType Type);

    /// <summary>
    /// Seeds every MET Electrical tenant whose chart is empty. Other tenants are untouched,
    /// so the Acme E2E chart in Program.cs still owns a true Acme demo.
    /// </summary>
    public static async Task<int> EnsureForMetOfficeTenantsAsync(
        AppDbContext db,
        IFinanceService finance,
        ITenantProvider tenantProvider,
        ILogger logger,
        CancellationToken ct = default)
    {
        var tenants = await db.Set<Tenant>().AsNoTracking()
            .Where(t => !t.IsDeleted)
            .ToListAsync(ct);

        var created = 0;
        foreach (var tenant in tenants.Where(TenantBranding.IsMetOfficeTenant))
            created += await EnsureForTenantIfEmptyAsync(db, finance, tenantProvider, tenant.Id, logger, ct);

        return created;
    }

    /// <summary>
    /// Inserts the stub chart when the tenant has no live accounts. Returns the number created (0 when skipped).
    /// </summary>
    public static async Task<int> EnsureForTenantIfEmptyAsync(
        AppDbContext db,
        IFinanceService finance,
        ITenantProvider tenantProvider,
        Guid tenantId,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return 0;

        var hasAccounts = await db.Set<Account>().IgnoreQueryFilters()
            .AnyAsync(a => a.TenantId == tenantId && !a.IsDeleted, ct);
        if (hasAccounts)
        {
            logger.LogInformation(
                "MET chart of accounts skipped — tenant {TenantId} already has accounts.",
                tenantId);
            return 0;
        }

        var previous = tenantProvider.GetCurrentTenantId();
        tenantProvider.SetTenantId(tenantId);
        try
        {
            var count = 0;
            foreach (var stub in StubAccounts)
            {
                await finance.CreateAccountAsync(new Account
                {
                    AccountCode = stub.Code,
                    Name = stub.Name,
                    Type = stub.Type,
                    IsActive = true,
                    Description = StubDescription
                }, ct);
                count++;
            }

            logger.LogInformation(
                "Seeded MET FY chart of accounts stub ({Count} accounts, zero balances, no journals) for tenant {TenantId}.",
                count,
                tenantId);
            return count;
        }
        finally
        {
            tenantProvider.SetTenantId(previous);
        }
    }
}
