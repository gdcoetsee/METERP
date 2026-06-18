using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Tags the seeded demo job (Hospital + travel quote lines) for job→invoice E2E.
/// </summary>
public static class E2EDemoInvoiceJobSeeder
{
    public const string DemoNotesMarker = "E2E demo invoice job";

    public static async Task EnsureDemoInvoiceJobTaggedAsync(IJobService jobService, CancellationToken ct = default)
    {
        var jobs = await jobService.GetAllAsync(pageSize: 200, ct: ct);
        var demoJob = jobs.FirstOrDefault(j =>
            (j.Customer?.Name?.Contains("Hospital", StringComparison.OrdinalIgnoreCase) == true
             || (j.Title?.Contains("Hospital", StringComparison.OrdinalIgnoreCase) == true))
            && j.QuoteId != null);

        if (demoJob == null)
            return;

        if (demoJob.Notes != null && demoJob.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase))
            return;

        demoJob.Notes = DemoNotesMarker;
        await jobService.UpdateAsync(demoJob, ct);
    }
}