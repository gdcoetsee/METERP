using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class QuoteApprovalServiceTests
{
    private static (QuoteService Service, AppDbContext Db, Guid TenantId, Customer Customer) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"quote-approval-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        var customer = new Customer { TenantId = tenantId, Name = "Test Co" };
        db.Set<Customer>().Add(customer);
        db.SaveChanges();

        var service = new QuoteService(db, auditService: audit.Object, tenantProvider: tenantProvider.Object);
        return (service, db, tenantId, customer);
    }

    [Fact]
    public async Task SubmitForExecutiveApproval_SetsPendingStatus()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-001",
                Status = QuoteStatus.Draft,
                Lines = [new QuoteLine { TenantId = tenantId, Description = "Line", Quantity = 1, UnitPrice = 100 }]
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            var userId = Guid.NewGuid();
            await service.SubmitForExecutiveApprovalAsync(quote.Id, userId);

            var saved = await db.Set<Quote>().FirstAsync(q => q.Id == quote.Id);
            Assert.Equal(QuoteApprovalStatus.PendingExecutive, saved.ApprovalStatus);
            Assert.Equal(userId, saved.SubmittedForApprovalByUserId);
        }
    }

    [Fact]
    public async Task SubmitForExecutiveApproval_ThrowsWhenNoLines()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-EMPTY",
                Status = QuoteStatus.Draft
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SubmitForExecutiveApprovalAsync(quote.Id, Guid.NewGuid()));
            Assert.Contains("line", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UpdateAsync_BlocksSentWithoutExecutiveApproval()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-002",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.None
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            quote.Status = QuoteStatus.Sent;

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(quote));
        }
    }

    [Fact]
    public async Task ExecutiveRejectAsync_SetsRejectedStatus()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-REJ",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            await service.ExecutiveRejectAsync(quote.Id, Guid.NewGuid(), "Margin too low");

            var saved = await db.Set<Quote>().FirstAsync(q => q.Id == quote.Id);
            Assert.Equal(QuoteApprovalStatus.Rejected, saved.ApprovalStatus);
            Assert.Equal("Margin too low", saved.ExecutiveRejectionReason);
        }
    }

    [Fact]
    public async Task ExecutiveRejectAsync_RejectsReasonTooLong()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-REJ-LONG",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExecutiveRejectAsync(quote.Id, Guid.NewGuid(), new string('R', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task ExecutiveRejectAsync_AcceptsReasonAt500Characters()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-REJ-OK",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            await service.ExecutiveRejectAsync(quote.Id, Guid.NewGuid(), new string('R', 500));
            var saved = await db.Set<Quote>().FirstAsync(q => q.Id == quote.Id);
            Assert.Equal(QuoteApprovalStatus.Rejected, saved.ApprovalStatus);
            Assert.Equal(500, saved.ExecutiveRejectionReason!.Length);
        }
    }

    [Fact]
    public async Task ExecutiveRejectAsync_RequiresReason()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-REJ2",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExecutiveRejectAsync(quote.Id, Guid.NewGuid(), "  "));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExecutiveRejectAsync(quote.Id, Guid.NewGuid(), "ab"));
        }
    }

    [Fact]
    public async Task WithdrawFromApprovalAsync_ReturnsToNoneAndAllowsLineEdits()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-WD",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive,
                SubmittedForApprovalAt = DateTime.UtcNow,
                SubmittedForApprovalByUserId = Guid.NewGuid()
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            await service.WithdrawFromApprovalAsync(quote.Id, Guid.NewGuid(), "Need to fix travel line");

            var saved = await db.Set<Quote>().FirstAsync(q => q.Id == quote.Id);
            Assert.Equal(QuoteApprovalStatus.None, saved.ApprovalStatus);
            Assert.Null(saved.SubmittedForApprovalAt);
            Assert.Contains("fix travel", saved.ExecutiveRejectionReason);

            // Lines editable again once withdrawn.
            await service.AddLineAsync(new QuoteLine
            {
                QuoteId = quote.Id,
                Description = "Travel",
                Quantity = 1,
                UnitPrice = 500m
            });
            var withLines = await service.GetByIdAsync(quote.Id);
            Assert.Single(withLines!.Lines, l => !l.IsDeleted);
        }
    }

    [Fact]
    public async Task WithdrawFromApprovalAsync_ThrowsWhenNotPending()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-WD2",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.None
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.WithdrawFromApprovalAsync(quote.Id, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task ExecutiveApprove_ReturnsFalse_WhenNotPending()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-004",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            var execId = Guid.NewGuid();
            await service.ExecutiveApproveAsync(quote.Id, execId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ExecutiveApproveAsync(quote.Id, execId));
        }
    }

    [Fact]
    public async Task ExecutiveApprove_AllowsSentStatus()
    {
        var (service, db, tenantId, customer) = Create();
        await using (db)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-TEST-003",
                Status = QuoteStatus.Draft,
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive,
                Lines =
                {
                    new QuoteLine { Description = "Scope", Quantity = 1, UnitPrice = 1000m }
                }
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            var execId = Guid.NewGuid();
            await service.ExecutiveApproveAsync(quote.Id, execId);

            quote = await db.Set<Quote>().Include(q => q.Lines).FirstAsync(q => q.Id == quote.Id);
            quote.Status = QuoteStatus.Sent;
            await service.UpdateAsync(quote);

            var saved = await db.Set<Quote>().FirstAsync(q => q.Id == quote.Id);
            Assert.Equal(QuoteStatus.Sent, saved.Status);
        }
    }
}