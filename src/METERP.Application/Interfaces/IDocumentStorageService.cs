namespace METERP.Application.Interfaces;

/// <summary>
/// Tenant-scoped file storage for company docs, attachments, POP, etc.
/// </summary>
public interface IDocumentStorageService
{
    Task<DocumentStorageResult> SaveAsync(
        Guid tenantId,
        string category,
        string fileName,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(Guid tenantId, string storageKey, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid tenantId, string storageKey, CancellationToken ct = default);
}

public sealed record DocumentStorageResult(
    string StorageKey,
    string FileName,
    long SizeBytes,
    string ContentType);