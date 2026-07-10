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
}
