using Microsoft.EntityFrameworkCore;
using METERP.Application.Accounting;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class AccountingExportService : IAccountingExportService
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenantProvider;

    public AccountingExportService(AppDbContext db, ITenantProvider tenantProvider)
    {
        _db = db;
        _tenantProvider = tenantProvider;
    }

    public async Task<AccountingExportResult> ExportOutstandingSalesAsync(
        AccountingProvider provider,
        string? salesAccountCode = null,
        CancellationToken ct = default)
    {
        if (provider is AccountingProvider.None)
            throw new InvalidOperationException("Select Sage or Xero in Finance before exporting.");

        var invoices = await _db.Set<Invoice>()
            .AsNoTracking()
            .Include(i => i.Customer)
            .Include(i => i.Job)
            .Include(i => i.Lines)
            .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
            .OrderBy(i => i.InvoiceDate)
            .ToListAsync(ct);

        var code = string.IsNullOrWhiteSpace(salesAccountCode) ? "200" : salesAccountCode.Trim();

        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId != Guid.Empty)
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (tenant != null)
            {
                tenant.AccountingProvider = provider;
                tenant.AccountingSalesAccountCode = code;
                await _db.SaveChangesAsync(ct);
            }
        }

        var csv = AccountingExportFormatter.BuildCsv(provider, invoices, code);
        return new AccountingExportResult(AccountingExportFormatter.FileName(provider, DateTime.UtcNow), csv, invoices.Count);
    }
}
