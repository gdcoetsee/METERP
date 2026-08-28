using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Seeding;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class E2EReceiveDemoPoSeederTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private static (AppDbContext Db, PurchaseOrderService Pos, SupplierService Suppliers, InventoryService Inventory, Mock<ITenantProvider> Tenant) CreateServices(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var suppliers = new SupplierService(db);
        var pos = new PurchaseOrderService(db, inventory);
        return (db, pos, suppliers, inventory, tenantProvider);
    }

    [Fact]
    public async Task EnsureSentReceiveDemoPoAsync_DoesNotThrow_WhenExistingSentDemoPo()
    {
        var tenantId = Guid.NewGuid();
        var (db, pos, suppliers, inventory, tenant) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            var itemId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier
            {
                Id = supplierId,
                TenantId = tenantId,
                Name = "Panel Supplies CC",
                IsActive = true
            });
            db.Set<InventoryItem>().Add(new InventoryItem
            {
                Id = itemId,
                TenantId = tenantId,
                Sku = "LED-HB-150",
                Name = "LED High Bay 150W",
                UnitCost = 420m,
                Unit = "ea",
                QuantityOnHand = 10,
                ReorderLevel = 2
            });
            var poId = Guid.NewGuid();
            db.Set<PurchaseOrder>().Add(new PurchaseOrder
            {
                Id = poId,
                TenantId = tenantId,
                SupplierId = supplierId,
                PoNumber = "PO-E2E-1",
                Status = PurchaseOrderStatus.Sent,
                Notes = "E2E receive demo PO",
                TaxRate = 0.15m,
                Lines =
                {
                    new PurchaseOrderLine
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PurchaseOrderId = poId,
                        InventoryItemId = itemId,
                        Description = "LED High Bay 150W",
                        Quantity = 3,
                        UnitPrice = 420m,
                        Unit = "ea"
                    }
                }
            });
            await db.SaveChangesAsync();

            var ex = await Record.ExceptionAsync(() =>
                E2EReceiveDemoPoSeeder.EnsureSentReceiveDemoPoAsync(
                    pos, suppliers, inventory, tenant.Object, tenantId));

            Assert.Null(ex);
            var remaining = await pos.GetAllAsync(pageSize: 50);
            Assert.Single(remaining, p =>
                p.Notes != null && p.Notes.Contains("E2E receive demo", StringComparison.OrdinalIgnoreCase)
                && p.Status == PurchaseOrderStatus.Sent);
        }
    }

    [Fact]
    public async Task EnsureSentReceiveDemoPoAsync_CreatesDraftThenSends_WhenMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, pos, suppliers, inventory, tenant) = CreateServices(tenantId);
        using (db)
        {
            db.Set<Supplier>().Add(new Supplier
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Panel Supplies CC",
                IsActive = true
            });
            db.Set<InventoryItem>().Add(new InventoryItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Sku = "LED-HB-150",
                Name = "LED High Bay 150W",
                UnitCost = 420m,
                Unit = "ea",
                QuantityOnHand = 10,
                ReorderLevel = 2
            });
            await db.SaveChangesAsync();

            await E2EReceiveDemoPoSeeder.EnsureSentReceiveDemoPoAsync(
                pos, suppliers, inventory, tenant.Object, tenantId);

            var listed = (await pos.GetAllAsync(pageSize: 50))
                .Single(p => p.Notes != null && p.Notes.Contains("E2E receive demo", StringComparison.OrdinalIgnoreCase));
            var created = await pos.GetByIdAsync(listed.Id);
            Assert.NotNull(created);
            Assert.Equal(PurchaseOrderStatus.Sent, created!.Status);
            Assert.NotEmpty(created.Lines);
        }
    }
}
