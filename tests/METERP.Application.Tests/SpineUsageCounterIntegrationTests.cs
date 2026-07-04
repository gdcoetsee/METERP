using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Verifies spine services persist commercial counters through real <see cref="TenantService"/>
/// (isolated DbContext scope), not just mock verify calls.
/// </summary>
public class SpineUsageCounterIntegrationTests
{
    private sealed class TestHarness : IDisposable
    {
        private readonly ServiceProvider _provider;

        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public TenantService TenantService { get; }
        public Mock<ITenantProvider> TenantProvider { get; } = new();
        public Mock<ICurrentUserService> CurrentUser { get; } = new();

        public TestHarness()
        {
            TenantId = Guid.NewGuid();
            var dbName = Guid.NewGuid().ToString();

            TenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(TenantId);
            CurrentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
            CurrentUser.Setup(u => u.UserName).Returns("counter-integration");

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
            services.AddSingleton(TenantProvider.Object);
            services.AddSingleton(CurrentUser.Object);

            _provider = services.BuildServiceProvider();
            var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

            Db = _provider.GetRequiredService<AppDbContext>();
            TenantService = new TenantService(Db, scopeFactory);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId,
                Name = "Counter Co",
                Subdomain = "counter-co",
                Tier = SubscriptionTier.Starter,
                EnabledFeatures = "ai,usage-tracking"
            });
            Db.SaveChanges();
        }

        public async Task<Tenant> ReloadTenantAsync()
        {
            // Counters persist in an isolated scope; clear tracking so reads see committed values.
            Db.ChangeTracker.Clear();
            var tenant = await TenantService.GetByIdAsync(TenantId);
            return tenant!;
        }

        public void Dispose() => _provider.Dispose();
    }

    [Fact]
    public async Task QuoteService_CreateAsync_PersistsQuoteCounter_InDatabase()
    {
        using var harness = new TestHarness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Client" };
        harness.Db.Set<Customer>().Add(customer);
        await harness.Db.SaveChangesAsync();

        var quoteService = new QuoteService(harness.Db, harness.TenantService, harness.TenantProvider.Object);

        await quoteService.CreateAsync(new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            TaxRate = 0.15m,
            Lines = { new QuoteLine { Quantity = 1, UnitPrice = 1000m } }
        });

        var tenant = await harness.ReloadTenantAsync();
        Assert.Equal(1, tenant.TotalQuotesCreated);
        Assert.Equal(1, tenant.PeriodQuotesCreated);
    }

    [Fact]
    public async Task JobService_CreateAsync_PersistsJobCounter_InDatabase()
    {
        using var harness = new TestHarness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Client" };
        harness.Db.Set<Customer>().Add(customer);
        await harness.Db.SaveChangesAsync();

        var jobService = new JobService(harness.Db, harness.TenantService);

        await jobService.CreateAsync(new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            Title = "Field job",
            QuotedTotal = 5000m
        });

        var tenant = await harness.ReloadTenantAsync();
        Assert.Equal(1, tenant.TotalJobsCreated);
        Assert.Equal(1, tenant.PeriodJobsCreated);
    }

    [Fact]
    public async Task QuoteService_ConvertToJobAsync_PersistsJobCounter_InDatabase()
    {
        using var harness = new TestHarness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Client" };
        harness.Db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-COUNTER-001",
            Status = QuoteStatus.Draft,
            TaxRate = 0.15m,
            Lines = { new QuoteLine { Quantity = 1, UnitPrice = 2000m } }
        };
        quote.RecalculateTotals();
        harness.Db.Set<Quote>().Add(quote);
        await harness.Db.SaveChangesAsync();

        var quoteService = new QuoteService(harness.Db, harness.TenantService, harness.TenantProvider.Object);
        await quoteService.ConvertToJobAsync(quote.Id);

        var tenant = await harness.ReloadTenantAsync();
        Assert.Equal(0, tenant.TotalQuotesCreated);
        Assert.Equal(1, tenant.TotalJobsCreated);
        Assert.Equal(1, tenant.PeriodJobsCreated);
    }

    [Fact]
    public async Task InvoiceService_CreateFromJobAsync_PersistsInvoiceCounter_AndRevenue()
    {
        using var harness = new TestHarness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Client" };
        harness.Db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            TaxRate = 0.15m,
            Lines =
            {
                new QuoteLine { Description = "Work", Quantity = 1, UnitPrice = 1000m },
                new QuoteLine { Description = "Travel", Quantity = 1, UnitPrice = 200m, LineType = "Travel" }
            }
        };
        quote.RecalculateTotals();
        harness.Db.Set<Quote>().Add(quote);

        var job = new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            QuoteId = quote.Id,
            QuotedTotal = quote.Total,
            Title = "Signed-off job",
            SignOffStatus = JobSignOffStatus.SignedOff
        };
        harness.Db.Set<Job>().Add(job);
        await harness.Db.SaveChangesAsync();

        var invoiceService = new InvoiceService(harness.Db, harness.TenantService);
        var invoice = await invoiceService.CreateFromJobAsync(job.Id);

        var tenant = await harness.ReloadTenantAsync();
        Assert.Equal(1, tenant.TotalInvoicesIssued);
        Assert.Equal(1, tenant.PeriodInvoicesIssued);
        Assert.Equal(invoice.Total, tenant.TotalRevenueBilled);
    }

    [Fact]
    public async Task InvoiceService_CreateFromJobAsync_IncrementsInvoiceCounter_ExactlyOnce()
    {
        using var harness = new TestHarness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Client" };
        harness.Db.Set<Customer>().Add(customer);

        var job = new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            QuotedTotal = 3000m,
            Title = "Single-increment job",
            SignOffStatus = JobSignOffStatus.SignedOff
        };
        harness.Db.Set<Job>().Add(job);
        await harness.Db.SaveChangesAsync();

        var invoiceService = new InvoiceService(harness.Db, harness.TenantService);
        await invoiceService.CreateFromJobAsync(job.Id);

        var tenant = await harness.ReloadTenantAsync();
        Assert.Equal(1, tenant.PeriodInvoicesIssued);
    }

    [Fact]
    public async Task InvoiceService_ProformaBillingDocument_SkipsInvoiceCounter()
    {
        using var harness = new TestHarness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Client" };
        harness.Db.Set<Customer>().Add(customer);

        var job = new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            QuotedTotal = 4000m,
            Title = "Proforma job"
        };
        harness.Db.Set<Job>().Add(job);
        await harness.Db.SaveChangesAsync();

        var invoiceService = new InvoiceService(harness.Db, harness.TenantService);
        await invoiceService.CreateBillingDocumentAsync(job.Id, InvoiceDocumentType.Proforma);

        var tenant = await harness.ReloadTenantAsync();
        Assert.Equal(0, tenant.TotalInvoicesIssued);
        Assert.Equal(0, tenant.PeriodInvoicesIssued);
        Assert.Equal(0m, tenant.TotalRevenueBilled);
    }

    [Fact]
    public async Task SpineFlow_QuoteCreateConvertInvoice_CountersAccumulateCorrectly()
    {
        using var harness = new TestHarness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Spine Client" };
        harness.Db.Set<Customer>().Add(customer);
        await harness.Db.SaveChangesAsync();

        var quoteService = new QuoteService(harness.Db, harness.TenantService, harness.TenantProvider.Object);
        var quoteId = await quoteService.CreateAsync(new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            TaxRate = 0.15m,
            Lines = { new QuoteLine { Quantity = 1, UnitPrice = 5000m } }
        });

        var job = await quoteService.ConvertToJobAsync(quoteId);
        var jobService = new JobService(harness.Db, harness.TenantService);
        await jobService.SignOffAsync(job.Id, Guid.NewGuid());

        var invoiceService = new InvoiceService(harness.Db, harness.TenantService);
        await invoiceService.CreateFromJobAsync(job.Id);

        var tenant = await harness.ReloadTenantAsync();
        Assert.Equal(1, tenant.PeriodQuotesCreated);
        Assert.Equal(1, tenant.PeriodJobsCreated);
        Assert.Equal(1, tenant.PeriodInvoicesIssued);
    }

    [Fact]
    public async Task AiAssistantService_SuccessfulLlmCall_PersistsAiCounter_InDatabase()
    {
        AiAssistantService.ClearThrottleStateForTesting();

        using var harness = new TestHarness();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:ApiKey"] = "test-key",
                ["Ai:BaseUrl"] = "https://llm.test/v1",
                ["Ai:Model"] = "gpt-test",
                ["Ai:Enabled"] = "true"
            })
            .Build();

        var envelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = "Top risk: travel costs on remote jobs." } } }
        });
        using var http = new HttpClient(new StubLlmHandler(envelope))
        {
            BaseAddress = new Uri("https://llm.test/v1/")
        };

        var service = new AiAssistantService(
            new AiConfigurationResolver(config, harness.TenantProvider.Object, harness.TenantService),
            Mock.Of<ILogger<AiAssistantService>>(),
            harness.TenantService,
            harness.TenantProvider.Object,
            quotaService: null,
            http);

        var reply = await service.AskCopilotAsync("What are my cost risks?");

        Assert.Equal("Top risk: travel costs on remote jobs.", reply);
        var tenant = await harness.ReloadTenantAsync();
        Assert.Equal(1, tenant.TotalAiCalls);
        Assert.Equal(1, tenant.PeriodAiCalls);
    }

    private sealed class StubLlmHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubLlmHandler(string responseBody) => _responseBody = responseBody;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}