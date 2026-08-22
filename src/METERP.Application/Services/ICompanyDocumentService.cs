using METERP.Application.Models;
using METERP.Domain;

namespace METERP.Application.Services;

public interface ICompanyDocumentService
{
    Task<IReadOnlyList<CompanyDocument>> GetAllAsync(string? documentType = null, CancellationToken ct = default);

    Task<CompanyDocument?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Guid> UploadAsync(
        string documentType,
        string title,
        string fileName,
        Stream content,
        string contentType,
        bool noExpiry,
        DateTime? expiryDate,
        string? notes,
        CancellationToken ct = default);

    Task UpdateMetadataAsync(CompanyDocument document, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<CompanyDocument>> GetExpiringAsync(int withinDays = 30, CancellationToken ct = default);

    /// <summary>Company compliance documents expiring within 30 days (or already expired).</summary>
    Task<IReadOnlyList<CompanyDocumentExpiryRow>> GetExpiringQueueAsync(int take = 20, CancellationToken ct = default);
}