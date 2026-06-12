using METERP.Domain;

namespace METERP.Infrastructure.Caching;

/// <summary>
/// Breaks EF navigation back-references before list payloads are JSON-cached.
/// IgnoreCycles alone is insufficient when EF materializes duplicate parent instances.
/// </summary>
internal static class ListCacheGraphHelper
{
    public static void PrepareQuotesForCache(IEnumerable<Quote> quotes)
    {
        foreach (var quote in quotes)
        {
            foreach (var line in quote.Lines)
                line.Quote = null!;
        }
    }

    public static void PrepareJobsForCache(IEnumerable<Job> jobs)
    {
        foreach (var job in jobs)
        {
            if (job.Quote != null)
                PrepareQuotesForCache(new[] { job.Quote });

            foreach (var cost in job.ActualCosts)
                cost.Job = null!;

            foreach (var crew in job.CrewAssignments)
            {
                crew.Job = null!;
                if (crew.Employee != null)
                    crew.Employee = null!;
            }
        }
    }
}