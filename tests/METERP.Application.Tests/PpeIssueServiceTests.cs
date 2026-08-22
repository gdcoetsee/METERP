using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class PpeIssueServiceTests
{
    private static (PpeIssueService Service, AppDbContext Db, Guid TenantId, InventoryService Inventory) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ppe-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        var inventory = new InventoryService(db);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (new PpeIssueService(db, inventory, audit.Object), db, tenantId, inventory);
    }

    [Fact]
    public async Task RecordFromRequisitionIssueAsync_SkipsNonPpeRequisitions()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var requisition = new StockRequisition
            {
                TenantId = tenantId,
                JobId = Guid.NewGuid(),
                RequestedByUserId = Guid.NewGuid(),
                IsPpe = false,
                RequisitionNumber = "REQ-001"
            };
            db.Set<StockRequisition>().Add(requisition);
            await db.SaveChangesAsync();

            await service.RecordFromRequisitionIssueAsync(requisition);

            Assert.Empty(await db.Set<EmployeePpeIssue>().ToListAsync());
        }
    }

    [Fact]
    public async Task RecordFromRequisitionIssueAsync_CreatesIssuesForIssuedPpeLines()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Mine Co" };
            db.Set<Customer>().Add(customer);

            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E1",
                FirstName = "Field",
                LastName = "Tech",
                HireDate = DateTime.UtcNow.AddYears(-1)
            };
            db.Set<Employee>().Add(employee);

            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Site work",
                QuotedTotal = 1000m,
                AssignedEmployeeId = employee.Id
            };
            db.Set<Job>().Add(job);

            var helmet = new InventoryItem { TenantId = tenantId, Sku = "PPE-H", Name = "Helmet", IsActive = true };
            var gloves = new InventoryItem { TenantId = tenantId, Sku = "PPE-G", Name = "Gloves", IsActive = true };
            db.Set<InventoryItem>().AddRange(helmet, gloves);
            await db.SaveChangesAsync();

            var requesterId = Guid.NewGuid();
            var requisition = new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = requesterId,
                IsPpe = true,
                RequisitionNumber = "REQ-PPE-01",
                Lines =
                [
                    new StockRequisitionLine { InventoryItemId = helmet.Id, QuantityRequested = 1, QuantityIssued = 1 },
                    new StockRequisitionLine { InventoryItemId = gloves.Id, QuantityRequested = 2, QuantityIssued = 0 }
                ]
            };
            db.Set<StockRequisition>().Add(requisition);
            await db.SaveChangesAsync();

            await service.RecordFromRequisitionIssueAsync(requisition);

            var issues = await db.Set<EmployeePpeIssue>().ToListAsync();
            Assert.Single(issues);
            Assert.Equal(employee.Id, issues[0].EmployeeId);
            Assert.Equal(helmet.Id, issues[0].InventoryItemId);
            Assert.Equal(1m, issues[0].Quantity);
            Assert.Equal(job.Id, issues[0].JobId);
            Assert.Equal(requesterId, issues[0].RequestedByUserId);
            Assert.Contains("REQ-PPE-01", issues[0].Notes);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_WithoutJob_DecrementsStockAndRegisters()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "EMP-9",
                FirstName = "Sam",
                LastName = "Store",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var item = new InventoryItem
            {
                TenantId = tenantId,
                Sku = "PPE-BOOT",
                Name = "Safety boots",
                QuantityOnHand = 10,
                UnitCost = 500m,
                IsActive = true
            };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            var issuer = Guid.NewGuid();
            var issueId = await service.IssueToEmployeeAsync(employee.Id, item.Id, 2m, issuer, jobId: null, notes: "New hire kit");

            var issue = await db.Set<EmployeePpeIssue>().Include(p => p.Employee).FirstAsync(p => p.Id == issueId);
            Assert.Equal(employee.Id, issue.EmployeeId);
            Assert.Null(issue.JobId);
            Assert.Equal(2m, issue.Quantity);
            Assert.Equal(issuer, issue.RequestedByUserId);

            var stock = await db.Set<InventoryItem>().FirstAsync(i => i.Id == item.Id);
            Assert.Equal(8m, stock.QuantityOnHand);

            var tx = await db.Set<StockTransaction>().FirstAsync(t => t.InventoryItemId == item.Id);
            Assert.Equal(StockTransactionType.Issue, tx.Type);
            Assert.Equal(-2m, tx.Quantity);
            Assert.Null(tx.JobId);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_ThrowsWhenJobClosed()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-CLOSED",
                FirstName = "Closed",
                LastName = "Job",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var customer = new Customer { TenantId = tenantId, Name = "C" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Done",
                Status = JobStatus.Closed,
                JobNumber = "J-CLOSED"
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "PPE-CL",
                Name = "Gloves",
                QuantityOnHand = 10,
                UnitCost = 20m,
                IsActive = true
            });

            var ex = await Assert.ThrowsAsync<JobClosedException>(() =>
                service.IssueToEmployeeAsync(employee.Id, itemId, 1m, Guid.NewGuid(), jobId: job.Id));
            Assert.Contains("closed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_ThrowsWhenJobSoftDeleted()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-SOFT",
                FirstName = "Soft",
                LastName = "Del",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var customer = new Customer { TenantId = tenantId, Name = "C" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Gone",
                Status = JobStatus.InProgress,
                JobNumber = "J-SOFT"
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "PPE-SOFT",
                Name = "Vest",
                QuantityOnHand = 10,
                UnitCost = 20m,
                IsActive = true
            });

            job.IsDeleted = true;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueToEmployeeAsync(employee.Id, itemId, 1m, Guid.NewGuid(), jobId: job.Id));
            Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_ThrowsWhenEmployeeSoftDeleted()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-GONE",
                FirstName = "Gone",
                LastName = "Hand",
                IsActive = true,
                IsDeleted = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "PPE-GONE",
                Name = "Vest",
                QuantityOnHand = 10,
                UnitCost = 20m,
                IsActive = true
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueToEmployeeAsync(employee.Id, itemId, 1m, Guid.NewGuid()));
            Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_ThrowsWhenInventoryItemSoftDeleted()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-ITEM",
                FirstName = "Live",
                LastName = "Hand",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "PPE-DEL-SKU",
                Name = "Hard hat",
                QuantityOnHand = 10,
                UnitCost = 20m,
                IsActive = true
            });
            var item = await db.Set<InventoryItem>().FirstAsync(i => i.Id == itemId);
            item.IsDeleted = true;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueToEmployeeAsync(employee.Id, itemId, 1m, Guid.NewGuid()));
            Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_ThrowsWhenJobCancelled()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-CANCEL",
                FirstName = "Cancel",
                LastName = "Job",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var customer = new Customer { TenantId = tenantId, Name = "C" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Scrapped",
                Status = JobStatus.Cancelled,
                JobNumber = "J-CANCEL"
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "PPE-CN",
                Name = "Helmet",
                QuantityOnHand = 10,
                UnitCost = 20m,
                IsActive = true
            });

            var ex = await Assert.ThrowsAsync<JobClosedException>(() =>
                service.IssueToEmployeeAsync(employee.Id, itemId, 1m, Guid.NewGuid(), jobId: job.Id));
            Assert.Contains("closed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_InsufficientStock_Throws()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "EMP-1",
                FirstName = "A",
                LastName = "B",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var item = new InventoryItem
            {
                TenantId = tenantId,
                Sku = "PPE-X",
                Name = "Hard hat",
                QuantityOnHand = 1,
                IsActive = true
            };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueToEmployeeAsync(employee.Id, item.Id, 5m, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_ThrowsWhenQuantityExceedsCap()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "EMP-CAP",
                FirstName = "A",
                LastName = "B",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var item = new InventoryItem
            {
                TenantId = tenantId,
                Sku = "PPE-CAP",
                Name = "Gloves",
                QuantityOnHand = 5000,
                IsActive = true
            };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueToEmployeeAsync(employee.Id, item.Id, 1001m, Guid.NewGuid()));
            Assert.Contains("1000", ex.Message);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_RejectsNotesTooLong()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "EMP-NOTE",
                FirstName = "A",
                LastName = "B",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var item = new InventoryItem
            {
                TenantId = tenantId,
                Sku = "PPE-NOTE",
                Name = "Boots",
                QuantityOnHand = 10,
                IsActive = true
            };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IssueToEmployeeAsync(employee.Id, item.Id, 1m, Guid.NewGuid(), notes: new string('N', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task IssueToEmployeeAsync_AcceptsNotesAt500Characters()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "EMP-N500",
                FirstName = "A",
                LastName = "B",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            var item = new InventoryItem
            {
                TenantId = tenantId,
                Sku = "PPE-N500",
                Name = "Boots",
                QuantityOnHand = 10,
                IsActive = true
            };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            var id = await service.IssueToEmployeeAsync(
                employee.Id, item.Id, 1m, Guid.NewGuid(), notes: new string('N', 500));
            Assert.NotEqual(Guid.Empty, id);
        }
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByEmployee()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var emp1 = new Employee { TenantId = tenantId, EmployeeNumber = "1", FirstName = "One", LastName = "A", IsActive = true };
            var emp2 = new Employee { TenantId = tenantId, EmployeeNumber = "2", FirstName = "Two", LastName = "B", IsActive = true };
            db.Set<Employee>().AddRange(emp1, emp2);
            var item = new InventoryItem { TenantId = tenantId, Sku = "PPE-1", Name = "Vest", IsActive = true, QuantityOnHand = 50 };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            await service.IssueToEmployeeAsync(emp1.Id, item.Id, 1m, Guid.NewGuid());
            await service.IssueToEmployeeAsync(emp2.Id, item.Id, 1m, Guid.NewGuid());

            var forEmp1 = await service.GetHistoryAsync(emp1.Id);
            Assert.Single(forEmp1);
            Assert.Equal(emp1.Id, forEmp1[0].EmployeeId);
        }
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsMostRecentFirst()
    {
        var (service, db, tenantId, _) = Create();
        await using (db)
        {
            var item = new InventoryItem { TenantId = tenantId, Sku = "PPE-1", Name = "Vest", IsActive = true };
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            db.Set<EmployeePpeIssue>().AddRange(
                new EmployeePpeIssue
                {
                    TenantId = tenantId,
                    JobId = null,
                    RequestedByUserId = Guid.NewGuid(),
                    InventoryItemId = item.Id,
                    Quantity = 1,
                    IssuedAt = DateTime.UtcNow.AddDays(-2)
                },
                new EmployeePpeIssue
                {
                    TenantId = tenantId,
                    JobId = null,
                    RequestedByUserId = Guid.NewGuid(),
                    InventoryItemId = item.Id,
                    Quantity = 1,
                    IssuedAt = DateTime.UtcNow.AddHours(-1)
                });
            await db.SaveChangesAsync();

            var history = await service.GetHistoryAsync();

            Assert.Equal(2, history.Count);
            Assert.True(history[0].IssuedAt > history[1].IssuedAt);
        }
    }

    [Fact]
    public async Task ReturnFromEmployeeAsync_RestocksAndTracksOutstanding()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-RET",
                FirstName = "Return",
                LastName = "Tech",
                HireDate = DateTime.UtcNow.AddYears(-1),
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "HARDHAT",
                Name = "Hard hat",
                QuantityOnHand = 10,
                UnitCost = 50m,
                IsActive = true
            });

            var userId = Guid.NewGuid();
            var issueId = await service.IssueToEmployeeAsync(employee.Id, itemId, 4m, userId);

            var afterIssue = await inventory.GetItemByIdAsync(itemId);
            Assert.Equal(6m, afterIssue!.QuantityOnHand);

            Assert.True(await service.ReturnFromEmployeeAsync(issueId, 1.5m, userId, "Damaged box OK"));
            afterIssue = await inventory.GetItemByIdAsync(itemId);
            Assert.Equal(7.5m, afterIssue!.QuantityOnHand);

            var issue = await db.Set<EmployeePpeIssue>().FirstAsync(i => i.Id == issueId);
            Assert.Equal(1.5m, issue.QuantityReturned);
            Assert.Equal(2.5m, issue.QuantityOutstanding);
            Assert.False(issue.IsFullyReturned);

            Assert.True(await service.ReturnFromEmployeeAsync(issueId, 2.5m, userId));
            issue = await db.Set<EmployeePpeIssue>().FirstAsync(i => i.Id == issueId);
            Assert.True(issue.IsFullyReturned);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReturnFromEmployeeAsync(issueId, 1m, userId));
        }
    }

    [Fact]
    public async Task GetOutstandingQueueAsync_IncludesOpenIssues_ExcludesFullyReturned()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-OUT",
                FirstName = "Anele",
                LastName = "Hold",
                HireDate = DateTime.UtcNow.AddYears(-1),
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "GLOVE",
                Name = "Gloves",
                QuantityOnHand = 20,
                UnitCost = 10m,
                IsActive = true
            });
            var userId = Guid.NewGuid();
            var openId = await service.IssueToEmployeeAsync(employee.Id, itemId, 3m, userId);
            var closedId = await service.IssueToEmployeeAsync(employee.Id, itemId, 2m, userId);
            Assert.True(await service.ReturnFromEmployeeAsync(closedId, 2m, userId));

            var queue = await service.GetOutstandingQueueAsync();

            Assert.Contains(queue, r => r.Id == openId && r.EmployeeName.Contains("Anele") && r.Outstanding == 3m && r.ItemName.Contains("GLOVE"));
            Assert.DoesNotContain(queue, r => r.Id == closedId);
        }
    }

    [Fact]
    public async Task ReturnFromEmployeeAsync_ThrowsWhenInventoryItemSoftDeleted()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-RET-DEL",
                FirstName = "Ret",
                LastName = "Del",
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "PPE-RET-DEL",
                Name = "Boots",
                QuantityOnHand = 5,
                UnitCost = 50m,
                IsActive = true
            });
            var issueId = await service.IssueToEmployeeAsync(employee.Id, itemId, 2m, Guid.NewGuid());

            var item = await db.Set<InventoryItem>().FirstAsync(i => i.Id == itemId);
            item.IsDeleted = true;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReturnFromEmployeeAsync(issueId, 1m, Guid.NewGuid()));
            Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ReturnFromEmployeeAsync_RejectsOverReturn()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-OV",
                FirstName = "Over",
                LastName = "Return",
                HireDate = DateTime.UtcNow.AddYears(-1),
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "GLOVE",
                Name = "Gloves",
                QuantityOnHand = 5,
                UnitCost = 20m,
                IsActive = true
            });

            var userId = Guid.NewGuid();
            var issueId = await service.IssueToEmployeeAsync(employee.Id, itemId, 2m, userId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReturnFromEmployeeAsync(issueId, 3m, userId));
        }
    }

    [Fact]
    public async Task ReturnFromEmployeeAsync_AcceptsNotesAt500Characters()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-RET-OK",
                FirstName = "Return",
                LastName = "Ok",
                IsActive = true
            };
            var item = new InventoryItem
            {
                TenantId = tenantId,
                Sku = "PPE-RET-OK",
                Name = "Hard Hat",
                QuantityOnHand = 10m,
                UnitCost = 50m,
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            db.Set<InventoryItem>().Add(item);
            await db.SaveChangesAsync();

            var userId = Guid.NewGuid();
            var issueId = await service.IssueToEmployeeAsync(employee.Id, item.Id, 1m, userId);
            Assert.True(await service.ReturnFromEmployeeAsync(issueId, 1m, userId, new string('N', 500)));
            var issue = await db.Set<EmployeePpeIssue>().FirstAsync(i => i.Id == issueId);
            Assert.Contains("Return:", issue.Notes);
            Assert.True(issue.Notes!.Length <= 1000);
        }
    }

    [Fact]
    public async Task ReturnFromEmployeeAsync_RejectsNotesTooLong()
    {
        var (service, db, tenantId, inventory) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-RN",
                FirstName = "Return",
                LastName = "Notes",
                HireDate = DateTime.UtcNow.AddYears(-1),
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "HELMET",
                Name = "Helmet",
                QuantityOnHand = 5,
                UnitCost = 50m,
                IsActive = true
            });

            var userId = Guid.NewGuid();
            var issueId = await service.IssueToEmployeeAsync(employee.Id, itemId, 1m, userId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReturnFromEmployeeAsync(issueId, 1m, userId, new string('N', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }
}
