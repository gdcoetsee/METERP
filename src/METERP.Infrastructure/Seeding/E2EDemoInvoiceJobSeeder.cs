using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Ensures a demo job with travel costs is ready for job→invoice E2E (resets invoices between runs).
/// </summary>
public static class E2EDemoInvoiceJobSeeder
{
    public const string DemoNotesMarker = "E2E demo invoice job";

    public static async Task<string?> EnsureDemoInvoiceJobTaggedAsync(IJobService jobService, CancellationToken ct = default)
    {
        var jobs = await jobService.GetAllAsync(pageSize: 200, ct: ct);
        var demoJob = jobs.FirstOrDefault(j =>
            j.Notes != null && j.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase));

        if (demoJob == null)
        {
            demoJob = jobs.FirstOrDefault(j =>
                (j.Customer?.Name?.Contains("Hospital", StringComparison.OrdinalIgnoreCase) == true
                 || (j.Title?.Contains("Hospital", StringComparison.OrdinalIgnoreCase) == true))
                && j.QuoteId != null);

            if (demoJob == null)
                return null;

            demoJob.Notes = DemoNotesMarker;
            await jobService.UpdateAsync(demoJob, ct);
        }

        return demoJob.JobNumber;
    }

    public static async Task<string?> EnsureInvoiceReadyDemoJobAsync(
        IJobService jobService,
        IInvoiceService invoiceService,
        ICustomerService customerService,
        IQuoteService quoteService,
        ITenantProvider tenantProvider,
        Guid tenantId,
        CancellationToken ct = default)
    {
        tenantProvider.SetTenantId(tenantId);

        var jobs = await jobService.GetAllAsync(pageSize: 500, ct: ct);
        var demoJob = jobs.FirstOrDefault(j =>
            j.Notes != null && j.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase));

        if (demoJob == null)
        {
            demoJob = jobs.FirstOrDefault(j =>
                (j.Customer?.Name?.Contains("Hospital", StringComparison.OrdinalIgnoreCase) == true
                 || j.Title?.Contains("Hospital", StringComparison.OrdinalIgnoreCase) == true)
                && j.QuoteId != null);

            if (demoJob == null)
            {
                demoJob = await CreateFreshDemoJobAsync(jobService, customerService, quoteService, ct);
                if (demoJob == null)
                    return null;
            }
            else
            {
                demoJob.Notes = DemoNotesMarker;
                await jobService.UpdateAsync(demoJob, ct);
            }
        }

        var invoices = await invoiceService.GetAllAsync(pageSize: 500, ct: ct);
        foreach (var invoice in invoices.Where(i => i.JobId == demoJob.Id))
            await invoiceService.DeleteAsync(invoice.Id, ct);

        if (demoJob.Status == JobStatus.Invoiced || demoJob.Status == JobStatus.Completed)
            await jobService.UpdateStatusAsync(demoJob.Id, JobStatus.InProgress, ct);

        var loaded = await jobService.GetByIdAsync(demoJob.Id, ct);
        if (loaded == null)
            return null;

        var hasTravelCost = (loaded.ActualCosts ?? [])
            .Any(c => !c.IsDeleted && c.CostType.Equals("Travel", StringComparison.OrdinalIgnoreCase));
        if (!hasTravelCost)
        {
            await jobService.AddCostAsync(new JobCost
            {
                JobId = loaded.Id,
                Description = "Travel & transport (van + fuel for crew)",
                Amount = 620m,
                CostType = "Travel",
                CostDate = DateTime.UtcNow.AddDays(-1)
            }, ct);
        }

        var hasLabor = (loaded.Labors ?? []).Any(l => !l.IsDeleted);
        if (!hasLabor)
        {
            await jobService.AddLaborAsync(new JobLabor
            {
                JobId = loaded.Id,
                WorkDate = DateTime.UtcNow.AddDays(-1),
                Hours = 4,
                HourlyRate = 195m,
                Description = "Installation and testing",
                Technician = "Thabo Mokoena"
            }, ct);
        }

        loaded.CreatedDate = DateTime.UtcNow;
        loaded.Notes = DemoNotesMarker;
        await jobService.UpdateAsync(loaded, ct);

        return loaded.JobNumber;
    }

    private static async Task<Job?> CreateFreshDemoJobAsync(
        IJobService jobService,
        ICustomerService customerService,
        IQuoteService quoteService,
        CancellationToken ct)
    {
        var customer = (await customerService.GetAllAsync(ct: ct))
            .FirstOrDefault(c => c.Name.Contains("Hospital", StringComparison.OrdinalIgnoreCase));
        if (customer == null)
            return null;

        var quoteId = await quoteService.CreateAsync(new Quote
        {
            CustomerId = customer.Id,
            QuoteDate = DateTime.UtcNow,
            ValidUntil = DateTime.UtcNow.AddDays(30),
            Status = QuoteStatus.Accepted,
            TaxRate = 0.15m,
            Notes = DemoNotesMarker
        }, ct);

        await quoteService.AddLineAsync(new QuoteLine
        {
            QuoteId = quoteId,
            Description = "Panel upgrade labour (8 hours)",
            Quantity = 8,
            UnitPrice = 195m,
            LineType = "Labour",
            Unit = "hr"
        }, ct);

        await quoteService.AddLineAsync(new QuoteLine
        {
            QuoteId = quoteId,
            Description = "Travel & site transport (explicit contractor cost)",
            Quantity = 1,
            UnitPrice = 620m,
            LineType = "Travel",
            Unit = "lot"
        }, ct);

        var quote = await quoteService.GetByIdAsync(quoteId, ct);
        if (quote == null)
            return null;

        var jobId = await jobService.CreateAsync(new Job
        {
            QuoteId = quoteId,
            CustomerId = customer.Id,
            Title = $"{customer.Name} - E2E invoice demo",
            QuotedTotal = quote.Total,
            Status = JobStatus.InProgress,
            Notes = DemoNotesMarker
        }, ct);

        return await jobService.GetByIdAsync(jobId, ct);
    }
}