using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class CompanyDocumentServiceTests
{
    private static (CompanyDocumentService Service, AppDbContext Db, Guid TenantId, Mock<IDocumentStorageService> Storage) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var storage = new Mock<IDocumentStorageService>();
        storage.Setup(s => s.SaveAsync(
                tenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentStorageResult("key/coid.pdf", "coid.pdf", 12, "application/pdf"));
        storage.Setup(s => s.DeleteAsync(tenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"company-doc-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        var service = new CompanyDocumentService(db, storage.Object, tenantProvider.Object, audit.Object);
        return (service, db, tenantId, storage);
    }

    [Fact]
    public async Task UploadAsync_PersistsMetadataAndCallsStorage()
    {
        var (service, db, tenantId, storage) = Create();
        await using (db)
        {
            await using var content = new MemoryStream("pdf"u8.ToArray());
            var expiry = DateTime.UtcNow.AddMonths(6).Date;

            var id = await service.UploadAsync("COID", "COID Certificate", "coid.pdf", content, "application/pdf", false, expiry, "Annual");

            var saved = await db.Set<CompanyDocument>().FirstAsync(d => d.Id == id);
            Assert.Equal(tenantId, saved.TenantId);
            Assert.Equal("COID Certificate", saved.Title);
            Assert.Equal(expiry, saved.ExpiryDate);
            Assert.False(saved.NoExpiry);
            storage.Verify(s => s.SaveAsync(tenantId, "company-docs", "coid.pdf", content, "application/pdf", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task UploadAsync_RequiresExpiryUnlessNoExpiry()
    {
        var (service, db, _, _) = Create();
        await using (db)
        {
            await using var content = new MemoryStream("x"u8.ToArray());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UploadAsync("Insurance", "Policy", "policy.pdf", content, "application/pdf", false, null, null));
        }
    }

    [Fact]
    public async Task GetExpiringAsync_ReturnsDocumentsWithinWindow()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            db.Set<CompanyDocument>().AddRange(
                new CompanyDocument { TenantId = tenantId, DocumentType = "COID", Title = "Soon", ExpiryDate = DateTime.UtcNow.AddDays(10) },
                new CompanyDocument { TenantId = tenantId, DocumentType = "Tax", Title = "Later", ExpiryDate = DateTime.UtcNow.AddDays(60) },
                new CompanyDocument { TenantId = tenantId, DocumentType = "Policy", Title = "No expiry", NoExpiry = true });
            await db.SaveChangesAsync();

            var expiring = await service.GetExpiringAsync(withinDays: 30);

            Assert.Single(expiring);
            Assert.Equal("Soon", expiring[0].Title);
        }
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesAndRemovesStorage()
    {
        var (service, db, tenantId, storage) = Create();
        await using (db)
        {
            var doc = new CompanyDocument
            {
                TenantId = tenantId,
                DocumentType = "COID",
                Title = "Remove me",
                StorageKey = "key/remove.pdf",
                FileName = "remove.pdf"
            };
            db.Set<CompanyDocument>().Add(doc);
            await db.SaveChangesAsync();

            await service.DeleteAsync(doc.Id);

            var saved = await db.Set<CompanyDocument>().IgnoreQueryFilters().FirstAsync(d => d.Id == doc.Id);
            Assert.True(saved.IsDeleted);
            storage.Verify(s => s.DeleteAsync(tenantId, "key/remove.pdf", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}