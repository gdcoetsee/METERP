using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class DocumentSequenceService : IDocumentSequenceService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantProvider _tenantProvider;

    public DocumentSequenceService(AppDbContext dbContext, ITenantProvider tenantProvider)
    {
        _dbContext = dbContext;
        _tenantProvider = tenantProvider;
    }

    public async Task<string> GetNextNumberAsync(string documentType, string prefix, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            throw new ArgumentException("Document type is required.", nameof(documentType));
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Document number prefix is required.", nameof(prefix));

        documentType = documentType.Trim();
        prefix = prefix.Trim();
        if (documentType.Length > 50)
            throw new ArgumentException("Document type cannot exceed 50 characters.", nameof(documentType));
        if (prefix.Length > 20)
            throw new ArgumentException("Document number prefix cannot exceed 20 characters.", nameof(prefix));

        var tenantId = _tenantProvider.GetCurrentTenantId();
        var year = DateTime.UtcNow.Year;
        var typeKey = documentType;

        var sequence = await _dbContext.Set<TenantDocumentSequence>()
            .FirstOrDefaultAsync(s => s.DocumentType == typeKey && s.Year == year, ct);

        if (sequence == null)
        {
            sequence = new TenantDocumentSequence
            {
                TenantId = tenantId,
                DocumentType = typeKey,
                Year = year,
                NextNumber = 1
            };
            _dbContext.Set<TenantDocumentSequence>().Add(sequence);
            try
            {
                await _dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(sequence).State = EntityState.Detached;
                sequence = await _dbContext.Set<TenantDocumentSequence>()
                    .FirstAsync(s => s.DocumentType == typeKey && s.Year == year, ct);
            }
        }

        var number = sequence.NextNumber;
        sequence.NextNumber++;
        sequence.LastModifiedDate = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        return $"{prefix}-{year}-{number:D5}";
    }
}