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
        await ValidateAsync(cert, ct);
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

        await ValidateAsync(cert, ct);
        existing.CertificationType = cert.CertificationType.Trim();
        existing.CertificateNumber = string.IsNullOrWhiteSpace(cert.CertificateNumber)
            ? null
            : cert.CertificateNumber.Trim();
        existing.NoExpiry = cert.NoExpiry;
        existing.ExpiryDate = cert.NoExpiry ? null : cert.ExpiryDate?.Date;
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

    private async Task ValidateAsync(EmployeeCertification cert, CancellationToken ct)
    {
        if (cert.EmployeeId == Guid.Empty)
            throw new InvalidOperationException("Employee is required.");
        if (string.IsNullOrWhiteSpace(cert.CertificationType))
            throw new InvalidOperationException("Certification type is required.");
        if (!cert.NoExpiry && cert.ExpiryDate is null)
            throw new InvalidOperationException("Expiry date is required unless marked no expiry.");

        cert.CertificationType = cert.CertificationType.Trim();
        if (cert.CertificationType.Length > 100)
            throw new InvalidOperationException("Certification type cannot exceed 100 characters.");
        if (!cert.NoExpiry && cert.ExpiryDate.HasValue)
            cert.ExpiryDate = cert.ExpiryDate.Value.Date;
        if (!cert.NoExpiry && cert.ExpiryDate.HasValue
            && cert.ExpiryDate.Value.Date > DateTime.UtcNow.Date.AddYears(20))
            throw new InvalidOperationException("Certification expiry cannot be more than 20 years in the future.");

        var empExists = await _dbContext.Set<Employee>()
            .AnyAsync(e => e.Id == cert.EmployeeId && e.IsActive, ct);
        if (!empExists)
            throw new InvalidOperationException("Employee not found or inactive.");

        // One open record per employee + type (avoid duplicate Red Cards / medicals).
        var typeDup = await _dbContext.Set<EmployeeCertification>()
            .AnyAsync(c => c.EmployeeId == cert.EmployeeId
                && c.CertificationType == cert.CertificationType
                && c.Id != cert.Id, ct);
        if (typeDup)
            throw new InvalidOperationException(
                $"Employee already has a '{cert.CertificationType}' certification. Update the existing record.");
    }
}
