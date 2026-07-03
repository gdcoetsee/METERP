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
        var tenantId = _tenantProvider.GetCurrentTenantId();
        var year = DateTime.UtcNow.Year;
        var typeKey = documentType.Trim();

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