using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class EmployeeCertificationService : IEmployeeCertificationService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService? _audit;

    public EmployeeCertificationService(AppDbContext dbContext, IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EmployeeCertification>> GetForEmployeeAsync(
        Guid employeeId,
        CancellationToken ct = default)
    {
        return await _dbContext.Set<EmployeeCertification>()
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId)
            .OrderBy(c => c.ExpiryDate == null)
            .ThenBy(c => c.ExpiryDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeCertification>> GetExpiringAsync(
        int withinDays = 30,
        CancellationToken ct = default)
    {
        var until = DateTime.UtcNow.Date.AddDays(withinDays);
        return await _dbContext.Set<EmployeeCertification>()
            .AsNoTracking()
            .Include(c => c.Employee)
            .Where(c => !c.NoExpiry && c.ExpiryDate != null && c.ExpiryDate <= until)
            .OrderBy(c => c.ExpiryDate)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(EmployeeCertification cert, CancellationToken ct = default)
    {
        Validate(cert);
        if (string.IsNullOrWhiteSpace(cert.StorageKey))
            cert.StorageKey = $"cert-meta/{cert.EmployeeId:N}/{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(cert.FileName))
            cert.FileName = "record.txt";

        _dbContext.Set<EmployeeCertification>().Add(cert);
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "CREATE",
                "EmployeeCertification",
                cert.CertificationType,
                $"Employee {cert.EmployeeId:N}",
                ct);
        }

        return cert.Id;
    }

    public async Task UpdateAsync(EmployeeCertification cert, CancellationToken ct = default)
    {
        var existing = await _dbContext.Set<EmployeeCertification>()
            .FirstOrDefaultAsync(c => c.Id == cert.Id, ct)
            ?? throw new InvalidOperationException("Certification not found.");

        Validate(cert);
        existing.CertificationType = cert.CertificationType.Trim();
        existing.CertificateNumber = cert.CertificateNumber;
        existing.NoExpiry = cert.NoExpiry;
        existing.ExpiryDate = cert.NoExpiry ? null : cert.ExpiryDate;
        existing.FileName = string.IsNullOrWhiteSpace(cert.FileName) ? existing.FileName : cert.FileName;
        existing.ContentType = cert.ContentType;
        existing.SizeBytes = cert.SizeBytes;

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _dbContext.Set<EmployeeCertification>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (existing == null) return;
        existing.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
    }

    private static void Validate(EmployeeCertification cert)
    {
        if (cert.EmployeeId == Guid.Empty)
            throw new InvalidOperationException("Employee is required.");
        if (string.IsNullOrWhiteSpace(cert.CertificationType))
            throw new InvalidOperationException("Certification type is required.");
        if (!cert.NoExpiry && cert.ExpiryDate == null)
            throw new InvalidOperationException("Expiry date is required unless marked no expiry.");
    }
}
