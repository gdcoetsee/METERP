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
}