using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Domain;
using METERP.Infrastructure.Integrations;
using METERP.Infrastructure.Persistence;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class InvoiceIntegrationTests
{
    [Fact]
    public async Task NotifyInvoiceCreatedAsync_SendsEmailWhenConfigured()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Acme",
            Subdomain = "acme",
            NotificationEmail = "billing@acme.demo"
        });

        var customer = new Customer { TenantId = tenantId, Name = "Client Co" };
        db.Set<Customer>().Add(customer);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            InvoiceNumber = "INV-EM-001",
            Total = 1500m,
            Subtotal = 1304.35m,
            Tax = 195.65m
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailSender>();
        emailMock.Setup(e => e.IsConfigured).Returns(true);

        var service = new InvoiceIntegrationService(
            db,
            emailMock.Object,
            new StubHttpClientFactory(new HttpClient(new RecordingHandler(HttpStatusCode.OK))),
            Microsoft.Extensions.Options.Options.Create(new EmailOptions()),
            NullLogger<InvoiceIntegrationService>.Instance);

        await service.NotifyInvoiceCreatedAsync(invoice.Id);

        emailMock.Verify(e => e.SendEmailAsync(
            "billing@acme.demo",
            It.Is<string>(s => s.Contains("INV-EM-001")),
            It.Is<string>(b => b.Contains("Client Co")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyInvoiceCreatedAsync_PostsWebhookWhenTenantUrlConfigured()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Acme",
            Subdomain = "acme",
            InvoiceWebhookUrl = "https://hooks.test/invoice"
        });

        var customer = new Customer { TenantId = tenantId, Name = "Client Co" };
        db.Set<Customer>().Add(customer);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            InvoiceNumber = "INV-WH-001",
            Total = 2500m,
            Subtotal = 2173.91m,
            Tax = 326.09m
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();

        var handler = new RecordingHandler(HttpStatusCode.OK);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var emailMock = new Mock<IEmailSender>();
        emailMock.Setup(e => e.IsConfigured).Returns(false);

        var service = new InvoiceIntegrationService(
            db,
            emailMock.Object,
            factory,
            Microsoft.Extensions.Options.Options.Create(new EmailOptions()),
            NullLogger<InvoiceIntegrationService>.Instance);

        await service.NotifyInvoiceCreatedAsync(invoice.Id);

        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("invoice.created", handler.LastBody);
        Assert.Contains("INV-WH-001", handler.LastBody);
    }

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var tenantProvider = new Mock<METERP.Application.Interfaces.ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var currentUser = new Mock<METERP.Application.Interfaces.ICurrentUserService>();
        currentUser.Setup(s => s.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public RecordingHandler(HttpStatusCode status) => _status = status;

        public int RequestCount { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status);
        }
    }
}