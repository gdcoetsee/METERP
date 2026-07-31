using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class CompanyDocumentService : ICompanyDocumentService
{
    private readonly AppDbContext _dbContext;
    private readonly IDocumentStorageService _storage;
    private readonly ITenantProvider _tenantProvider;
    private readonly IAuditService? _audit;

    public CompanyDocumentService(
        AppDbContext dbContext,
        IDocumentStorageService storage,
        ITenantProvider tenantProvider,
        IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _storage = storage;
        _tenantProvider = tenantProvider;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CompanyDocument>> GetAllAsync(string? documentType = null, CancellationToken ct = default)
    {
        var query = _dbContext.Set<CompanyDocument>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(documentType))
            query = query.Where(d => d.DocumentType == documentType);

        return await query.OrderBy(d => d.Title).ToListAsync(ct);
    }

    public async Task<CompanyDocument?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<CompanyDocument>().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<Guid> UploadAsync(
        string documentType,
        string title,
        string fileName,
        Stream content,
        string contentType,
        bool noExpiry,
        DateTime? expiryDate,
        string? notes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Document type and title are required.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("File name is required.");
        fileName = fileName.Trim();
        if (fileName.Length > 255)
            throw new InvalidOperationException("File name cannot exceed 255 characters.");
        if (content is null || !content.CanRead)
            throw new InvalidOperationException("Document content stream is required.");

        if (!noExpiry && expiryDate is null)
            throw new InvalidOperationException("Expiry date is required unless marked as no expiry.");
        if (!noExpiry && expiryDate.HasValue && expiryDate.Value.Date < DateTime.UtcNow.Date)
            throw new InvalidOperationException("Expiry date cannot be in the past for new uploads.");

        var type = documentType.Trim();
        var docTitle = title.Trim();
        if (type.Length > 100)
            throw new InvalidOperationException("Document type cannot exceed 100 characters.");
        if (docTitle.Length > 200)
            throw new InvalidOperationException("Document title cannot exceed 200 characters.");
        string? docNotes = null;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            docNotes = notes.Trim();
            if (docNotes.Length > 500)
                throw new InvalidOperationException("Document notes cannot exceed 500 characters.");
        }
        var titleTaken = await _dbContext.Set<CompanyDocument>()
            .AnyAsync(d => d.DocumentType == type && d.Title == docTitle, ct);
        if (titleTaken)
            throw new InvalidOperationException(
                $"A company document titled '{docTitle}' already exists for type '{type}'.");

        var tenantId = _tenantProvider.GetCurrentTenantId();
        var stored = await _storage.SaveAsync(tenantId, "company-docs", fileName, content, contentType, ct);

        var doc = new CompanyDocument
        {
            TenantId = tenantId,
            DocumentType = type,
            Title = docTitle,
            StorageKey = stored.StorageKey,
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            NoExpiry = noExpiry,
            ExpiryDate = noExpiry ? null : expiryDate?.Date,
            Notes = docNotes
        };

        _dbContext.Set<CompanyDocument>().Add(doc);
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "UPLOAD",
                "CompanyDocument",
                doc.Title,
                $"Type {doc.DocumentType}, expiry {(doc.NoExpiry ? "none" : doc.ExpiryDate?.ToString("yyyy-MM-dd"))}",
                ct);
        }

        return doc.Id;
    }

    public async Task UpdateMetadataAsync(CompanyDocument document, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(document.Title))
            throw new InvalidOperationException("Document title is required.");
        if (string.IsNullOrWhiteSpace(document.DocumentType))
            throw new InvalidOperationException("Document type is required.");
        if (!document.NoExpiry && document.ExpiryDate is null)
            throw new InvalidOperationException("Expiry date is required unless marked as no expiry.");

        document.Title = document.Title.Trim();
        document.DocumentType = document.DocumentType.Trim();
        if (document.DocumentType.Length > 100)
            throw new InvalidOperationException("Document type cannot exceed 100 characters.");
        if (document.Title.Length > 200)
            throw new InvalidOperationException("Document title cannot exceed 200 characters.");
        if (!string.IsNullOrWhiteSpace(document.Notes))
        {
            document.Notes = document.Notes.Trim();
            if (document.Notes.Length > 500)
                throw new InvalidOperationException("Document notes cannot exceed 500 characters.");
        }
        if (!document.NoExpiry && document.ExpiryDate.HasValue)
            document.ExpiryDate = document.ExpiryDate.Value.Date;

        var titleTaken = await _dbContext.Set<CompanyDocument>()
            .AnyAsync(d => d.DocumentType == document.DocumentType
                && d.Title == document.Title
                && d.Id != document.Id, ct);
        if (titleTaken)
            throw new InvalidOperationException(
                $"A company document titled '{document.Title}' already exists for type '{document.DocumentType}'.");

        _dbContext.Set<CompanyDocument>().Update(document);
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "UPDATE",
                "CompanyDocument",
                document.Title,
                $"Type {document.DocumentType}",
                ct);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await _dbContext.Set<CompanyDocument>().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc == null) return;

        var tenantId = _tenantProvider.GetCurrentTenantId();
        await _storage.DeleteAsync(tenantId, doc.StorageKey, ct);

        doc.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
            await _audit.LogAsync("DELETE", "CompanyDocument", doc.Title, doc.DocumentType, ct);
    }

    public async Task<IReadOnlyList<CompanyDocument>> GetExpiringAsync(int withinDays = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(withinDays);
        return await _dbContext.Set<CompanyDocument>()
            .AsNoTracking()
            .Where(d => !d.NoExpiry && d.ExpiryDate != null && d.ExpiryDate <= cutoff)
            .OrderBy(d => d.ExpiryDate)
            .ToListAsync(ct);
    }
}