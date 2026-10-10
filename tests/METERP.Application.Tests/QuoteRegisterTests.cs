using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Quote book: inclusive dates, status matches QuoteStatus, VAT is stored tax, jobs stay in-tenant.
/// </summary>
public class QuoteRegisterTests
{
    private static AppDbContext CreateContext(string dbName, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.UserName).Returns("register-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    private static Quote QuoteOn(
        Guid tenantId,
        Guid customerId,
        string number,
        DateTime quoteDate,
        QuoteStatus status,
        decimal subtotal = 100m,
        decimal tax = 15m)
    {
        return new Quote
        {
            TenantId = tenantId,
            CustomerId = customerId,
            QuoteNumber = number,
            QuoteDate = quoteDate,
            Status = status,
            Subtotal = subtotal,
            TaxRate = 0.15m,
            Tax = tax,
            Total = subtotal + tax
        };
    }

    [Fact]
    public async Task GetRegisterAsync_DateFilterIsInclusive_AndDropsQuotesOutsideTheRange()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(dbName, tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Register Customer" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var onFrom = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var lateOnTo = new DateTime(2026, 4, 3, 23, 59, 0, DateTimeKind.Utc);
        var before = new DateTime(2026, 3, 31, 23, 59, 0, DateTimeKind.Utc);
        var after = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc);

        var deleted = QuoteOn(tenantId, customer.Id, "Q-DELETED", onFrom, QuoteStatus.Sent);
        db.Set<Quote>().AddRange(
            QuoteOn(tenantId, customer.Id, "Q-FROM", onFrom, QuoteStatus.Draft),
            QuoteOn(tenantId, customer.Id, "Q-TO", lateOnTo, QuoteStatus.Sent),
            QuoteOn(tenantId, customer.Id, "Q-BEFORE", before, QuoteStatus.Accepted),
            QuoteOn(tenantId, customer.Id, "Q-AFTER", after, QuoteStatus.Draft),
            deleted);
        await db.SaveChangesAsync();
        deleted.IsDeleted = true;
        await db.SaveChangesAsync();

        var service = new QuoteService(db);
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 3);
        var rows = await service.GetRegisterAsync(from, to);

        Assert.Equal(new[] { "Q-TO", "Q-FROM" }, rows.Select(r => r.QuoteNumber).ToArray());
        Assert.DoesNotContain(rows, r => r.QuoteNumber is "Q-BEFORE" or "Q-AFTER" or "Q-DELETED");
        Assert.All(rows, r => Assert.Equal("Register Customer", r.CustomerName));

        var listed = await service.GetAllAsync(filter: QuoteBoardFilter.All, from: from, to: to);
        Assert.Equal(2, listed.Count);
        Assert.Equal(2, await service.CountAsync(filter: QuoteBoardFilter.All, from: from, to: to));
        Assert.DoesNotContain(listed, q => q.QuoteNumber is "Q-BEFORE" or "Q-AFTER" or "Q-DELETED");
    }

    [Fact]
    public async Task GetRegisterAsync_StatusFilterMatchesDraftSentAndAccepted()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(dbName, tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Status Customer" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var day = new DateTime(2026, 5, 12, 8, 0, 0, DateTimeKind.Utc);
        db.Set<Quote>().AddRange(
            QuoteOn(tenantId, customer.Id, "Q-DRAFT", day, QuoteStatus.Draft),
            QuoteOn(tenantId, customer.Id, "Q-SENT", day, QuoteStatus.Sent),
            QuoteOn(tenantId, customer.Id, "Q-ACCEPTED", day, QuoteStatus.Accepted),
            QuoteOn(tenantId, customer.Id, "Q-REJECTED", day, QuoteStatus.Rejected),
            QuoteOn(tenantId, customer.Id, "Q-EXPIRED", day, QuoteStatus.Expired));
        await db.SaveChangesAsync();

        var service = new QuoteService(db);

        var drafts = await service.GetRegisterAsync(status: QuoteStatus.Draft);
        var sent = await service.GetRegisterAsync(status: QuoteStatus.Sent);
        var accepted = await service.GetRegisterAsync(status: QuoteStatus.Accepted);

        Assert.Equal("Q-DRAFT", Assert.Single(drafts).QuoteNumber);
        Assert.Equal(QuoteStatus.Draft, drafts[0].Status);
        Assert.Equal("Q-SENT", Assert.Single(sent).QuoteNumber);
        Assert.Equal(QuoteStatus.Sent, sent[0].Status);
        Assert.Equal("Q-ACCEPTED", Assert.Single(accepted).QuoteNumber);
        Assert.Equal(QuoteStatus.Accepted, accepted[0].Status);

        var sentOnList = await service.GetAllAsync(filter: QuoteBoardFilter.All, status: QuoteStatus.Sent);
        Assert.Equal("Q-SENT", Assert.Single(sentOnList).QuoteNumber);
        Assert.Equal(1, await service.CountAsync(filter: QuoteBoardFilter.All, status: QuoteStatus.Sent));
    }

    [Fact]
    public async Task GetRegisterAsync_VatIsStoredTax_AndJobFlagIgnoresDeletedAndOtherTenants()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid quoteWithOtherTenantJob;

        await using (var db = CreateContext(dbName, tenantA))
        {
            var customer = new Customer { TenantId = tenantA, Name = "Vat Customer" };
            db.Set<Customer>().Add(customer);
            await db.SaveChangesAsync();

            var day = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);
            // Stored tax is 7.50, not Subtotal × 0.15 (which would be 15.00).
            var stored = QuoteOn(tenantA, customer.Id, "Q-STORED", day, QuoteStatus.Sent, subtotal: 100m, tax: 7.50m);
            var withJob = QuoteOn(tenantA, customer.Id, "Q-JOB", day, QuoteStatus.Accepted, subtotal: 200m, tax: 30m);
            var cancelledJobQuote = QuoteOn(tenantA, customer.Id, "Q-CANCELLED-JOB", day, QuoteStatus.Accepted, subtotal: 50m, tax: 7.50m);
            var deletedJobQuote = QuoteOn(tenantA, customer.Id, "Q-DELETED-JOB", day, QuoteStatus.Draft, subtotal: 80m, tax: 12m);
            var noJob = QuoteOn(tenantA, customer.Id, "Q-NONE", day, QuoteStatus.Draft, subtotal: 40m, tax: 6m);
            var otherJobQuote = QuoteOn(tenantA, customer.Id, "Q-OTHER-JOB", day, QuoteStatus.Sent, subtotal: 10m, tax: 1.50m);
            db.Set<Quote>().AddRange(stored, withJob, cancelledJobQuote, deletedJobQuote, noJob, otherJobQuote);
            await db.SaveChangesAsync();
            quoteWithOtherTenantJob = otherJobQuote.Id;

            db.Set<Job>().AddRange(
                new Job
                {
                    TenantId = tenantA,
                    CustomerId = customer.Id,
                    QuoteId = withJob.Id,
                    JobNumber = "J-LIVE",
                    Title = "Live job",
                    Status = JobStatus.InProgress
                },
                new Job
                {
                    TenantId = tenantA,
                    CustomerId = customer.Id,
                    QuoteId = cancelledJobQuote.Id,
                    JobNumber = "J-CANCELLED",
                    Title = "Cancelled job",
                    Status = JobStatus.Cancelled
                },
                new Job
                {
                    TenantId = tenantA,
                    CustomerId = customer.Id,
                    QuoteId = deletedJobQuote.Id,
                    JobNumber = "J-DELETED",
                    Title = "Deleted job",
                    Status = JobStatus.Scheduled,
                    IsDeleted = true
                });
            await db.SaveChangesAsync();

            var service = new QuoteService(db);
            var rows = await service.GetRegisterAsync(from: day, to: day);

            var storedRow = Assert.Single(rows, r => r.QuoteNumber == "Q-STORED");
            Assert.Equal(100m, storedRow.ExVat);
            Assert.Equal(7.50m, storedRow.Vat);
            Assert.NotEqual(Math.Round(storedRow.ExVat * 0.15m, 2), storedRow.Vat);
            Assert.Equal(107.50m, storedRow.Total);
            Assert.False(storedRow.HasJob);

            Assert.True(Assert.Single(rows, r => r.QuoteNumber == "Q-JOB").HasJob);
            Assert.True(Assert.Single(rows, r => r.QuoteNumber == "Q-CANCELLED-JOB").HasJob);
            Assert.False(Assert.Single(rows, r => r.QuoteNumber == "Q-DELETED-JOB").HasJob);
            Assert.False(Assert.Single(rows, r => r.QuoteNumber == "Q-NONE").HasJob);
            Assert.False(Assert.Single(rows, r => r.QuoteNumber == "Q-OTHER-JOB").HasJob);

            var flags = await service.GetQuoteIdsWithJobsAsync(rows.Select(r => r.Id).ToList());
            Assert.Contains(withJob.Id, flags);
            Assert.Contains(cancelledJobQuote.Id, flags);
            Assert.DoesNotContain(deletedJobQuote.Id, flags);
            Assert.Empty(await service.GetQuoteIdsWithJobsAsync(Array.Empty<Guid>()));
        }

        await using (var other = CreateContext(dbName, tenantB))
        {
            var customer = new Customer { TenantId = tenantB, Name = "Other Tenant" };
            other.Set<Customer>().Add(customer);
            await other.SaveChangesAsync();
            var day = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);
            other.Set<Quote>().Add(QuoteOn(tenantB, customer.Id, "Q-TENANT-B", day, QuoteStatus.Sent, subtotal: 100m, tax: 15m));
            other.Set<Job>().Add(new Job
            {
                TenantId = tenantB,
                CustomerId = customer.Id,
                QuoteId = quoteWithOtherTenantJob,
                JobNumber = "J-OTHER",
                Title = "Other tenant job",
                Status = JobStatus.Scheduled
            });
            await other.SaveChangesAsync();
        }

        await using var read = CreateContext(dbName, tenantA);
        var register = new QuoteService(read);
        var visible = await register.GetRegisterAsync(from: new DateTime(2026, 6, 2), to: new DateTime(2026, 6, 2));
        Assert.DoesNotContain(visible, r => r.QuoteNumber == "Q-TENANT-B");
        Assert.False(Assert.Single(visible, r => r.QuoteNumber == "Q-OTHER-JOB").HasJob);
        Assert.DoesNotContain(
            quoteWithOtherTenantJob,
            await register.GetQuoteIdsWithJobsAsync(new[] { quoteWithOtherTenantJob }));
    }
}
