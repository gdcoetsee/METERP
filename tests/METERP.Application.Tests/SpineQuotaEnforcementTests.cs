using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Verifies core spine services enforce monthly quotas via real QuotaService (sellable billing foundation).
/// </summary>
public class SpineQuotaEnforcementTests
{
    private sealed class QuotaHarness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public QuotaService QuotaService { get; }
        public Mock<ITenantProvider> TenantProvider { get; }

        public QuotaHarness(Guid tenantId)
        {
            TenantId = tenantId;
            var dbName = Guid.NewGuid().ToString();

            TenantProvider = new Mock<ITenantProvider>();
            TenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
            currentUser.Setup(u => u.UserName).Returns("quota-test");

            var services = new ServiceCollection();
            services.AddScoped(_ => TenantProvider.Object);
            services.AddScoped(_ => currentUser.Object);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateScope();
            Db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            QuotaService = new QuotaService(provider.GetRequiredService<IServiceScopeFactory>());
        }

        public async Task SeedTenantAsync(
            int periodQuotes = 0,
            int periodJobs = 0,
            int periodInvoices = 0,
            SubscriptionTier tier = SubscriptionTier.Starter)
        {
            Db.Tenants.Add(new Tenant
            {
                Id = TenantId,
                TenantId = TenantId,
                Name = "Quota Tenant",
                Subdomain = $"q-{TenantId:N}".Substring(0, 12),
                Tier = tier,
                UsagePeriodStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodQuotesCreated = periodQuotes,
                PeriodJobsCreated = periodJobs,
                PeriodInvoicesIssued = periodInvoices
            });
            await Db.SaveChangesAsync();
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task QuoteService_CreateAsync_ThrowsQuotaExceeded_WhenAtMonthlyLimit()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaHarness(tenantId);
        await harness.SeedTenantAsync(periodQuotes: 20);

        var service = new QuoteService(
            harness.Db,
            tenantProvider: harness.TenantProvider.Object,
            quotaService: harness.QuotaService);

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0.15m
        };

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => service.CreateAsync(quote));
        Assert.Equal(QuotaType.Quote, ex.QuotaType);
    }

    [Fact]
    public async Task JobService_CreateAsync_ThrowsQuotaExceeded_WhenAtMonthlyLimit()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaHarness(tenantId);
        await harness.SeedTenantAsync(periodJobs: 10);

        var service = new JobService(
            harness.Db,
            tenantProvider: harness.TenantProvider.Object,
            quotaService: harness.QuotaService);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            Title = "Blocked job"
        };

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => service.CreateAsync(job));
        Assert.Equal(QuotaType.Job, ex.QuotaType);
    }

    [Fact]
    public async Task InvoiceService_CreateAsync_ThrowsQuotaExceeded_WhenAtMonthlyLimit()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaHarness(tenantId);
        await harness.SeedTenantAsync(periodInvoices: 10);

        var service = new InvoiceService(
            harness.Db,
            tenantProvider: harness.TenantProvider.Object,
            quotaService: harness.QuotaService);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0.15m
        };

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => service.CreateAsync(invoice));
        Assert.Equal(QuotaType.Invoice, ex.QuotaType);
    }

    [Fact]
    public async Task QuoteService_ConvertToJobAsync_ThrowsQuotaExceeded_WhenJobQuotaFull()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaHarness(tenantId);
        await harness.SeedTenantAsync(periodJobs: 10);

        var customer = new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Acme" };
        harness.Db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-QUOTA-001",
            Status = QuoteStatus.Draft,
            TaxRate = 0.15m,
            Lines = new List<QuoteLine>
            {
                new() { Quantity = 1, UnitPrice = 1000, Description = "Work", IsDeleted = false }
            }
        };
        quote.RecalculateTotals();
        harness.Db.Set<Quote>().Add(quote);
        await harness.Db.SaveChangesAsync();

        var service = new QuoteService(
            harness.Db,
            tenantProvider: harness.TenantProvider.Object,
            quotaService: harness.QuotaService);

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => service.ConvertToJobAsync(quote.Id));
        Assert.Equal(QuotaType.Job, ex.QuotaType);
    }

    [Fact]
    public async Task InvoiceService_CreateBillingDocumentAsync_AllowsProforma_WhenInvoiceQuotaFull()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaHarness(tenantId);
        await harness.SeedTenantAsync(periodInvoices: 10);

        var customer = new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Acme" };
        harness.Db.Set<Customer>().Add(customer);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "J-PROFORMA-001",
            Title = "Quoted work",
            Status = JobStatus.Completed,
            SignOffStatus = JobSignOffStatus.SignedOff,
            SignedOffAt = DateTime.UtcNow,
            QuotedTotal = 8000m
        };
        harness.Db.Set<Job>().Add(job);
        await harness.Db.SaveChangesAsync();

        var service = new InvoiceService(
            harness.Db,
            tenantProvider: harness.TenantProvider.Object,
            quotaService: harness.QuotaService);

        var proforma = await service.CreateBillingDocumentAsync(job.Id, InvoiceDocumentType.Proforma);
        Assert.Equal(InvoiceDocumentType.Proforma, proforma.DocumentType);

        await Assert.ThrowsAsync<QuotaExceededException>(
            () => service.CreateBillingDocumentAsync(job.Id, InvoiceDocumentType.Final));
    }

    [Fact]
    public async Task InvoiceService_CreateFromJobAsync_ThrowsQuotaExceeded_WhenInvoiceQuotaFull()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaHarness(tenantId);
        await harness.SeedTenantAsync(periodInvoices: 10);

        var customer = new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Acme" };
        harness.Db.Set<Customer>().Add(customer);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "J-QUOTA-001",
            Title = "Signed-off job",
            Status = JobStatus.Completed,
            SignOffStatus = JobSignOffStatus.SignedOff,
            SignedOffAt = DateTime.UtcNow,
            QuotedTotal = 5000m
        };
        harness.Db.Set<Job>().Add(job);
        await harness.Db.SaveChangesAsync();

        var service = new InvoiceService(
            harness.Db,
            tenantProvider: harness.TenantProvider.Object,
            quotaService: harness.QuotaService);

        var ex = await Assert.ThrowsAsync<QuotaExceededException>(() => service.CreateFromJobAsync(job.Id));
        Assert.Equal(QuotaType.Invoice, ex.QuotaType);
    }
}