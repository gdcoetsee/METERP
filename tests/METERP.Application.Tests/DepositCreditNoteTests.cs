using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class DepositCreditNoteTests
{
    [Fact]
    public async Task IssueCreditNote_FullCreditOfOnlyDeposit_ClearsFlagAndHomeAsksAgain()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var job = await SeedOpenJobAsync(db, depositReceived: true);
            var deposit = DepositInvoice(job, "DEP-FULL", 3000m);
            db.Set<Invoice>().Add(deposit);
            await db.SaveChangesAsync();

            var draft = await service.CreateCreditNoteAsync(deposit.Id, "Deposit given back");
            Assert.Equal(InvoiceStatus.Draft, draft.Status);
            Assert.Equal(3000m, draft.Total);
            Assert.True((await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id)).DepositReceived);

            await service.IssueCreditNoteAsync(draft.Id);

            var saved = await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
            Assert.False(saved.DepositReceived);
            Assert.Equal(JobStatus.InProgress, saved.Status);

            var jobs = new JobService(db);
            var summary = await jobs.GetCommandCenterSummaryAsync(job.Id);
            Assert.NotNull(summary);
            Assert.False(summary!.DepositReceived);
            Assert.Equal(3000m, summary.BilledToDate);
            Assert.Equal(0m, summary.DepositBilledCover);
            Assert.True(InvoiceBillingCalculator.ShowDepositCollectionBanner(
                summary.Status, job.DepositPercent, summary.DepositReceived, summary.QuotedTotal, summary.DepositBilledCover));

            var queue = await jobs.GetDepositDueQueueAsync();
            var row = Assert.Single(queue, r => r.JobId == job.Id);
            Assert.Equal(3000m, row.UnbilledResidual);
        }
    }

    [Fact]
    public async Task IssueCreditNote_PartialCreditAboveThreshold_KeepsDepositReceived()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var job = await SeedOpenJobAsync(db, depositReceived: true);
            var deposit = DepositInvoice(job, "DEP-PART", 3000m);
            var progress = new Invoice
            {
                CustomerId = job.CustomerId,
                JobId = job.Id,
                InvoiceNumber = "INV-PROG",
                DocumentType = InvoiceDocumentType.Standard,
                Status = InvoiceStatus.Sent,
                TaxRate = 0m,
                Total = 5000m,
                DueDate = DateTime.UtcNow.Date.AddDays(14),
                Lines = { new InvoiceLine { Description = "Progress", Quantity = 1, UnitPrice = 5000m } }
            };
            db.Set<Invoice>().AddRange(deposit, progress);
            await db.SaveChangesAsync();

            var draft = await service.CreatePartialCreditNoteAsync(deposit.Id, "Small deposit adjustment", 1000m, null);
            await service.IssueCreditNoteAsync(draft.Id);

            var saved = await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
            Assert.True(saved.DepositReceived);
            Assert.Equal(JobStatus.InProgress, saved.Status);

            var summary = await new JobService(db).GetCommandCenterSummaryAsync(job.Id);
            Assert.NotNull(summary);
            Assert.True(summary!.DepositReceived);
            Assert.Equal(7000m, summary.DepositBilledCover);
        }
    }

    [Fact]
    public async Task IssueCreditNote_FullCreditOnClosedJob_LeavesDepositReceived()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var job = await SeedOpenJobAsync(db, depositReceived: true);
            job.Status = JobStatus.Closed;
            var deposit = DepositInvoice(job, "DEP-CLOSED", 3000m);
            db.Set<Invoice>().Add(deposit);
            await db.SaveChangesAsync();

            var draft = await service.CreateCreditNoteAsync(deposit.Id, "Closed job credit");
            await service.IssueCreditNoteAsync(draft.Id);

            var saved = await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
            Assert.True(saved.DepositReceived);
            Assert.Equal(JobStatus.Closed, saved.Status);
        }
    }

    [Fact]
    public async Task IssueCreditNote_PartialCreditBelowThreshold_ClearsDepositReceived()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var job = await SeedOpenJobAsync(db, depositReceived: true);
            var deposit = DepositInvoice(job, "DEP-SHORT", 3000m);
            db.Set<Invoice>().Add(deposit);
            await db.SaveChangesAsync();

            var draft = await service.CreatePartialCreditNoteAsync(deposit.Id, "Most of the deposit returned", 500m, null);
            Assert.True((await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id)).DepositReceived);

            await service.IssueCreditNoteAsync(draft.Id);

            var saved = await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
            Assert.False(saved.DepositReceived);
        }
    }

    [Fact]
    public async Task IssueCreditNote_OtherCountingDepositRemains_KeepsDepositReceived()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var job = await SeedOpenJobAsync(db, depositReceived: true);
            var first = DepositInvoice(job, "DEP-A", 2000m);
            var second = DepositInvoice(job, "DEP-B", 2000m);
            db.Set<Invoice>().AddRange(first, second);
            await db.SaveChangesAsync();

            var draft = await service.CreateCreditNoteAsync(first.Id, "First deposit returned");
            await service.IssueCreditNoteAsync(draft.Id);

            var saved = await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
            Assert.True(saved.DepositReceived);
        }
    }

    private static async Task<Job> SeedOpenJobAsync(AppDbContext db, bool depositReceived)
    {
        var customer = new Customer { Name = "Deposit Credit Co" };
        db.Set<Customer>().Add(customer);
        var job = new Job
        {
            CustomerId = customer.Id,
            JobNumber = "J-DEP-CN",
            Title = "Transformer deposit",
            QuotedTotal = 10000m,
            DepositPercent = 30m,
            DepositReceived = depositReceived,
            Status = JobStatus.InProgress
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static Invoice DepositInvoice(Job job, string number, decimal total) =>
        new()
        {
            CustomerId = job.CustomerId,
            JobId = job.Id,
            InvoiceNumber = number,
            DocumentType = InvoiceDocumentType.Deposit,
            Status = InvoiceStatus.Sent,
            TaxRate = 0m,
            Total = total,
            DueDate = DateTime.UtcNow.Date.AddDays(14),
            Lines =
            {
                new InvoiceLine { Description = "Deposit", Quantity = 1m, UnitPrice = total }
            }
        };

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var db = CreateDb($"dep-cn-{Guid.NewGuid():N}", tenantId);
        return (new InvoiceService(db), db, tenantId);
    }

    private static AppDbContext CreateDb(string database, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(database)
            .Options;
        return new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
    }
}
