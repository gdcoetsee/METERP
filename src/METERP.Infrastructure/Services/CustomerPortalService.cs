using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class CustomerPortalService : ICustomerPortalService
{
    private readonly AppDbContext _db;
    private readonly ITenantNotificationService? _notifications;
    private readonly IAuditService? _audit;

    public CustomerPortalService(
        AppDbContext db,
        ITenantNotificationService? notifications = null,
        IAuditService? audit = null)
    {
        _db = db;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<CustomerPortalDashboard> GetDashboardAsync(Guid customerId, CancellationToken ct = default)
    {
        if (customerId == Guid.Empty)
            throw new InvalidOperationException("Customer portal access requires a linked customer.");

        var customer = await _db.Set<Customer>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new InvalidOperationException("Customer not found for this portal login.");

        var quotes = await _db.Set<Quote>()
            .AsNoTracking()
            .Include(q => q.Lines)
            .Where(q => q.CustomerId == customerId && q.Status != QuoteStatus.Draft)
            .OrderByDescending(q => q.QuoteDate)
            .Take(50)
            .ToListAsync(ct);

        var invoices = await _db.Set<Invoice>()
            .AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .Where(i => i.CustomerId == customerId && i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
            .OrderByDescending(i => i.InvoiceDate)
            .Take(50)
            .ToListAsync(ct);

        var openQuotes = quotes.Count(q => q.Status == QuoteStatus.Sent);
        var openInvoices = invoices.Count(i => i.BalanceDue > 0);
        var balance = invoices.Sum(i => i.BalanceDue);

        return new CustomerPortalDashboard(customer.Name, openQuotes, openInvoices, balance, quotes, invoices);
    }

    public async Task AcceptQuoteAsync(Guid customerId, Guid quoteId, CancellationToken ct = default)
    {
        if (customerId == Guid.Empty)
            throw new InvalidOperationException("Customer portal access requires a linked customer.");

        var quote = await _db.Set<Quote>()
            .FirstOrDefaultAsync(q => q.Id == quoteId && q.CustomerId == customerId, ct)
            ?? throw new InvalidOperationException("Quote not found.");

        if (quote.Status == QuoteStatus.Accepted)
            return;

        if (quote.Status != QuoteStatus.Sent)
            throw new InvalidOperationException("Only sent quotes can be accepted from the portal.");

        quote.Status = QuoteStatus.Accepted;
        await _db.SaveChangesAsync(ct);

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = quote.TenantId,
                Title = $"Quote {quote.QuoteNumber} accepted — convert to job",
                Message = $"{quote.QuoteNumber} (R {quote.Total:N0}) was accepted in the customer portal. Convert it from Home so deposit and work can start.",
                Category = "sales",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = quote.Id,
                RelatedEntityType = nameof(Quote)
            }, ct);
        }

        if (_audit != null)
        {
            await _audit.LogAsync(
                "ACCEPT",
                "Quote",
                quote.QuoteNumber,
                "Accepted from customer portal",
                ct);
        }
    }

    public async Task ReportPaymentAsync(
        Guid customerId,
        Guid invoiceId,
        decimal amount,
        string? reference,
        CancellationToken ct = default)
    {
        if (customerId == Guid.Empty)
            throw new InvalidOperationException("Customer portal access requires a linked customer.");
        if (amount <= 0)
            throw new InvalidOperationException("Payment amount must be greater than zero.");
        if (amount > 100_000_000m)
            throw new InvalidOperationException("Payment amount is too large.");

        var invoice = await _db.Set<Invoice>()
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.CustomerId == customerId, ct)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
            throw new InvalidOperationException("This invoice cannot take a payment notice.");
        if (invoice.BalanceDue <= 0)
            throw new InvalidOperationException("This invoice is already paid.");
        if (amount > invoice.BalanceDue)
            throw new InvalidOperationException($"Amount cannot exceed the outstanding balance of R {invoice.BalanceDue:N2}.");

        reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (reference is { Length: > 100 })
            throw new InvalidOperationException("Payment reference cannot exceed 100 characters.");

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = invoice.TenantId,
                Title = $"Payment notice for {invoice.InvoiceNumber}",
                Message = $"Customer reported R {amount:N2} paid on {invoice.InvoiceNumber}"
                          + (reference == null ? "." : $" (ref {reference}).")
                          + " Record the receipt in Invoices when the money lands.",
                Category = "finance",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = invoice.Id,
                RelatedEntityType = nameof(Invoice)
            }, ct);
        }

        if (_audit != null)
        {
            await _audit.LogAsync(
                "PAYMENT_NOTICE",
                "Invoice",
                invoice.InvoiceNumber,
                $"Portal payment notice R {amount:N2}" + (reference == null ? "" : $" ref {reference}"),
                ct);
        }
    }
}
