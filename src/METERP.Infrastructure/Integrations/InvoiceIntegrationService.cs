using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Integrations;

public class InvoiceIntegrationService : IInvoiceIntegrationService
{
    private readonly AppDbContext _dbContext;
    private readonly IEmailSender _emailSender;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<InvoiceIntegrationService> _logger;

    public InvoiceIntegrationService(
        AppDbContext dbContext,
        IEmailSender emailSender,
        IHttpClientFactory httpClientFactory,
        IOptions<EmailOptions> emailOptions,
        ILogger<InvoiceIntegrationService> logger)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
        _httpClientFactory = httpClientFactory;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task NotifyInvoiceCreatedAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _dbContext.Set<Domain.Invoice>()
            .Include(i => i.Customer)
            .Include(i => i.Job)
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && !i.IsDeleted, ct);

        if (invoice == null)
            return;

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == invoice.TenantId && !t.IsDeleted, ct);

        await TrySendWebhookAsync(invoice, tenant, ct);
        await TrySendEmailAsync(invoice, tenant, ct);
    }

    private async Task TrySendWebhookAsync(Domain.Invoice invoice, Domain.Tenant? tenant, CancellationToken ct)
    {
        var webhookUrl = tenant?.InvoiceWebhookUrl;
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            _logger.LogWarning("Invalid invoice webhook URL for tenant {TenantId}", invoice.TenantId);
            return;
        }

        var payload = new
        {
            @event = "invoice.created",
            occurredAtUtc = DateTime.UtcNow,
            tenantId = invoice.TenantId,
            invoice = new
            {
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.DueDate,
                invoice.Status,
                invoice.Subtotal,
                invoice.Tax,
                invoice.Total,
                customerId = invoice.CustomerId,
                customerName = invoice.Customer?.Name,
                jobId = invoice.JobId,
                jobNumber = invoice.Job?.JobNumber,
                lineCount = invoice.Lines.Count(l => !l.IsDeleted)
            }
        };

        try
        {
            var client = _httpClientFactory.CreateClient("integrations");
            using var response = await client.PostAsJsonAsync(uri, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Invoice webhook returned {StatusCode} for invoice {InvoiceNumber}: {Body}",
                    (int)response.StatusCode, invoice.InvoiceNumber, body);
            }
            else
            {
                _logger.LogInformation("Invoice webhook delivered for {InvoiceNumber}", invoice.InvoiceNumber);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invoice webhook failed for {InvoiceNumber}", invoice.InvoiceNumber);
        }
    }

    private async Task TrySendEmailAsync(Domain.Invoice invoice, Domain.Tenant? tenant, CancellationToken ct)
    {
        if (!_emailSender.IsConfigured)
            return;

        var recipient = tenant?.NotificationEmail ?? _emailOptions.DefaultNotificationTo;
        if (string.IsNullOrWhiteSpace(recipient))
            return;

        var customerName = invoice.Customer?.Name ?? "Customer";
        var subject = $"Invoice {invoice.InvoiceNumber} created — R {invoice.Total:N2}";
        var body = $"""
            <p>A new invoice was created in METERP.</p>
            <ul>
              <li><strong>Invoice:</strong> {invoice.InvoiceNumber}</li>
              <li><strong>Customer:</strong> {customerName}</li>
              <li><strong>Total:</strong> R {invoice.Total:N2}</li>
              <li><strong>Status:</strong> {invoice.Status}</li>
            </ul>
            """;

        try
        {
            await _emailSender.SendEmailAsync(recipient, subject, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invoice notification email failed for {InvoiceNumber}", invoice.InvoiceNumber);
        }
    }
}