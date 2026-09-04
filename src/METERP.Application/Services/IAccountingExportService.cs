using METERP.Domain;

namespace METERP.Application.Services;

public sealed record AccountingExportResult(string FileName, string Csv, int InvoiceCount);

public interface IAccountingExportService
{
    Task<AccountingExportResult> ExportOutstandingSalesAsync(
        AccountingProvider provider,
        string? salesAccountCode = null,
        CancellationToken ct = default);
}
