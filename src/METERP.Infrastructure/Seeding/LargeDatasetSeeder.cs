using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Optional bulk seed for performance demos (pagination, reports, list views).
/// Safe: skips when disabled or when tenant already has a large quote count.
/// </summary>
public static class LargeDatasetSeeder
{
    private const int AlreadySeededQuoteThreshold = 40;

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>($"{SeedOptions.SectionName}:LargeDataset")
        || string.Equals(Environment.GetEnvironmentVariable("METERP_SEED_LARGE"), "true", StringComparison.OrdinalIgnoreCase);

    public static async Task SeedAsync(
        IServiceProvider serviceProvider,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var config = serviceProvider.GetRequiredService<IConfiguration>();
        if (!IsEnabled(config))
            return;

        var options = config.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("LargeDatasetSeeder");

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        if (tenantProvider is CurrentTenantProvider mutable)
            mutable.SetTenantId(tenantId);

        var existingQuotes = await db.Set<Quote>()
            .IgnoreQueryFilters()
            .CountAsync(q => q.TenantId == tenantId && !q.IsDeleted, ct);

        if (existingQuotes >= AlreadySeededQuoteThreshold)
        {
            logger.LogInformation("Large dataset seed skipped — tenant already has {Count} quotes.", existingQuotes);
            return;
        }

        var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var quoteService = scope.ServiceProvider.GetRequiredService<IQuoteService>();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

        logger.LogInformation(
            "Seeding large demo dataset for tenant {TenantId} ({Customers} customers, {Quotes} quotes, {Jobs} jobs, {Invoices} invoices)...",
            tenantId, options.LargeDatasetCustomers, options.LargeDatasetQuotes, options.LargeDatasetJobs, options.LargeDatasetInvoices);

        var customers = new List<Customer>();
        for (var i = 1; i <= options.LargeDatasetCustomers; i++)
        {
            customers.Add(new Customer
            {
                Name = $"Demo Customer {i:D3}",
                Email = $"customer{i:D3}@large.demo",
                City = i % 2 == 0 ? "Johannesburg" : "Cape Town",
                Province = i % 2 == 0 ? "Gauteng" : "Western Cape"
            });
        }

        foreach (var batch in customers.Chunk(25))
        {
            foreach (var c in batch)
                await customerService.CreateAsync(c, ct);
        }

        var customerIds = await db.Set<Customer>()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (!customerIds.Any())
            return;

        var rng = new Random(42);
        var quotesCreated = 0;
        var jobsCreated = 0;
        var invoicesCreated = 0;

        for (var q = 1; q <= options.LargeDatasetQuotes; q++)
        {
            var customerId = customerIds[rng.Next(customerIds.Count)];
            var hasTravel = q % 5 == 0;
            var quote = new Quote
            {
                CustomerId = customerId,
                QuoteDate = DateTime.UtcNow.AddDays(-rng.Next(1, 180)),
                Status = (QuoteStatus)(q % 5),
                TaxRate = 0.15m,
                Lines = new List<QuoteLine>
                {
                    new()
                    {
                        Description = $"Contract work package {q}",
                        Quantity = 1 + rng.Next(3),
                        UnitPrice = 500m + rng.Next(500, 5000),
                        LineType = "Labour"
                    }
                }
            };

            if (hasTravel)
            {
                quote.Lines.Add(new QuoteLine
                {
                    Description = "Travel & transport",
                    Quantity = 1,
                    UnitPrice = 400m + rng.Next(100, 800),
                    LineType = "Travel"
                });
            }

            quote.RecalculateTotals();
            var quoteId = await quoteService.CreateAsync(quote, ct);
            quotesCreated++;

            if (jobsCreated < options.LargeDatasetJobs && q % 2 == 0)
            {
                try
                {
                    var job = await quoteService.ConvertToJobAsync(quoteId, ct);
                    jobsCreated++;

                    if (invoicesCreated < options.LargeDatasetInvoices && q % 4 == 0)
                    {
                        try
                        {
                            await jobService.SignOffAsync(job.Id, Guid.Empty, ct);
                            await invoiceService.CreateFromJobAsync(job.Id, ct);
                            invoicesCreated++;
                        }
                        catch
                        {
                            // Quota or integration edge — continue seeding.
                        }
                    }
                }
                catch
                {
                    // Continue on conversion failures.
                }
            }

            if (q % 50 == 0)
                logger.LogInformation("Large seed progress: {Quotes} quotes, {Jobs} jobs, {Invoices} invoices...", quotesCreated, jobsCreated, invoicesCreated);
        }

        logger.LogInformation(
            "Large dataset seed complete: {Quotes} quotes, {Jobs} jobs, {Invoices} invoices.",
            quotesCreated, jobsCreated, invoicesCreated);
    }
}