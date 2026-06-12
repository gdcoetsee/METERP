namespace METERP.Application.Options;

/// <summary>
/// Database seeding controls (see DatabaseSeeder in Program.cs).
/// </summary>
public class SeedOptions
{
    public const string SectionName = "Seed";

    public bool ForceResetOnStart { get; set; }

    /// <summary>When true, seeds a larger Acme dataset for performance demos (opt-in).</summary>
    public bool LargeDataset { get; set; }

    public int LargeDatasetCustomers { get; set; } = 50;
    public int LargeDatasetQuotes { get; set; } = 200;
    public int LargeDatasetJobs { get; set; } = 120;
    public int LargeDatasetInvoices { get; set; } = 80;
}