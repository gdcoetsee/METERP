using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class EmployeeCertificationServiceTests
{
    private static (EmployeeCertificationService Service, AppDbContext Db, Guid TenantId, Employee Employee) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"certs-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var employee = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "CERT-1",
            FirstName = "Cert",
            LastName = "Holder",
            HireDate = DateTime.UtcNow.AddYears(-2),
            IsActive = true
        };
        db.Set<Employee>().Add(employee);
        db.SaveChanges();

        return (new EmployeeCertificationService(db), db, tenantId, employee);
    }

    [Fact]
    public async Task CreateAsync_RequiresTypeAndEmployee()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = Guid.Empty,
                    CertificationType = "First Aid"
                }));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = employee.Id,
                    CertificationType = "  "
                }));
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsCertificateNumberTooLong()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = employee.Id,
                    CertificationType = "Red Card",
                    CertificateNumber = new string('N', 101),
                    NoExpiry = true
                }));
            Assert.Contains("100 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsFileNameTooLong()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = employee.Id,
                    CertificationType = "Red Card",
                    FileName = new string('F', 256),
                    NoExpiry = true
                }));
            Assert.Contains("255 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_AcceptsFileNameAt255Characters()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new EmployeeCertification
            {
                EmployeeId = employee.Id,
                CertificationType = "Medical",
                FileName = new string('F', 255),
                NoExpiry = true
            });
            Assert.NotEqual(Guid.Empty, id);
        }
    }

    [Fact]
    public async Task CreateAsync_AcceptsTypeAt100Characters()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new EmployeeCertification
            {
                EmployeeId = employee.Id,
                CertificationType = new string('T', 100),
                NoExpiry = true
            });
            var saved = await db.Set<EmployeeCertification>().FirstAsync(c => c.Id == id);
            Assert.Equal(100, saved.CertificationType.Length);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsInactiveEmployee()
    {
        var (service, db, tenantId, employee) = Create();
        await using (db)
        {
            employee.IsActive = false;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = employee.Id,
                    CertificationType = "First Aid",
                    NoExpiry = true
                }));
            Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsTypeTooLong()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = employee.Id,
                    CertificationType = new string('C', 101),
                    NoExpiry = true
                }));
            Assert.Contains("100 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateTypeForSameEmployee()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            await service.CreateAsync(new EmployeeCertification
            {
                EmployeeId = employee.Id,
                CertificationType = "Red Card",
                NoExpiry = true
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = employee.Id,
                    CertificationType = "Red Card",
                    ExpiryDate = DateTime.UtcNow.Date.AddYears(1)
                }));
            Assert.Contains("already has", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CreateAsync_AndGetExpiring_ReturnsWithinWindow()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            await service.CreateAsync(new EmployeeCertification
            {
                EmployeeId = employee.Id,
                CertificationType = "First Aid",
                CertificateNumber = "FA-1",
                ExpiryDate = DateTime.UtcNow.Date.AddDays(10)
            });

            await service.CreateAsync(new EmployeeCertification
            {
                EmployeeId = employee.Id,
                CertificationType = "Far future",
                ExpiryDate = DateTime.UtcNow.Date.AddYears(5)
            });

            await service.CreateAsync(new EmployeeCertification
            {
                EmployeeId = employee.Id,
                CertificationType = "No expiry ticket",
                NoExpiry = true
            });

            var expiring = await service.GetExpiringAsync(30);
            Assert.Single(expiring);
            Assert.Equal("First Aid", expiring[0].CertificationType);

            var queue = await service.GetExpiringQueueAsync();
            Assert.Single(queue);
            Assert.Equal("First Aid", queue[0].CertificationType);
            Assert.True(queue[0].DaysRemaining <= 10);

            var forEmp = await service.GetForEmployeeAsync(employee.Id);
            Assert.Equal(3, forEmp.Count);
        }
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new EmployeeCertification
            {
                EmployeeId = employee.Id,
                CertificationType = "Electrical",
                NoExpiry = true
            });

            await service.DeleteAsync(id);

            var remaining = await service.GetForEmployeeAsync(employee.Id);
            Assert.Empty(remaining);

            var deleted = await db.Set<EmployeeCertification>()
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == id);
            Assert.True(deleted.IsDeleted);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmployeeInactive()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var inactive = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "CERT-OFF",
                FirstName = "Off",
                LastName = "Duty",
                IsActive = false
            };
            db.Set<Employee>().Add(inactive);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = inactive.Id,
                    CertificationType = "First Aid",
                    NoExpiry = true
                }));
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmployeeSoftDeleted()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var emp = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "CERT-DEL",
                FirstName = "Gone",
                LastName = "Tech",
                IsActive = true
            };
            db.Set<Employee>().Add(emp);
            await db.SaveChangesAsync();
            emp.IsDeleted = true;
            emp.IsActive = false;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new EmployeeCertification
                {
                    EmployeeId = emp.Id,
                    CertificationType = "Medical",
                    NoExpiry = true
                }));
            Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UpdateAsync_NormalizesExpiryDate()
    {
        var (service, db, _, employee) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new EmployeeCertification
            {
                EmployeeId = employee.Id,
                CertificationType = "Working at heights",
                ExpiryDate = DateTime.UtcNow.Date.AddMonths(6)
            });

            var cert = await db.Set<EmployeeCertification>().FirstAsync(c => c.Id == id);
            cert.ExpiryDate = new DateTime(2027, 3, 15, 18, 45, 0, DateTimeKind.Utc);
            await service.UpdateAsync(cert);

            var reloaded = await db.Set<EmployeeCertification>().AsNoTracking().FirstAsync(c => c.Id == id);
            Assert.Equal(new DateTime(2027, 3, 15), reloaded.ExpiryDate);
        }
    }
}
