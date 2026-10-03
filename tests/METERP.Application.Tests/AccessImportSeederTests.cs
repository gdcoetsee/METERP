using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Seeding;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AccessImportSeederTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "true", false)]
    [InlineData("", "1", false)]
    [InlineData("Acme", null, false)]
    [InlineData("MET", "true", true)]
    [InlineData("met", null, true)]
    [InlineData(null, "false", true)]
    [InlineData(null, "0", true)]
    [InlineData("Acme", "false", true)]
    public void IsMet_MatchesProfileOrExplicitE2EOff(string? profile, string? seedE2E, bool expected) =>
        Assert.Equal(expected, SeedProfileGates.IsMet(profile, seedE2E));

    [Theory]
    [InlineData(null, AccessImportMode.Off)]
    [InlineData("", AccessImportMode.Off)]
    [InlineData("false", AccessImportMode.Off)]
    [InlineData("0", AccessImportMode.Off)]
    [InlineData("true", AccessImportMode.Write)]
    [InlineData("YES", AccessImportMode.Write)]
    [InlineData("dry", AccessImportMode.DryRun)]
    [InlineData("dry-run", AccessImportMode.DryRun)]
    public void ParseAccessImportFlag_DefaultsOff(string? flag, AccessImportMode expected) =>
        Assert.Equal(expected, SeedProfileGates.ParseAccessImportFlag(flag));

    [Fact]
    public void ResolveCsvDirectory_PrefersEnvironmentThenConfigThenRepoDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "meterp-content");
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "custom")),
            AccessImportSeeder.ResolveCsvDirectory("custom", "other", root));
        Assert.Equal(
            Path.GetFullPath(@"D:\extracts"),
            AccessImportSeeder.ResolveCsvDirectory(null, @"D:\extracts", root));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "..", "..", "_helm_walk", "seed_fy2026")),
            AccessImportSeeder.ResolveCsvDirectory(null, null, root));
    }

    [Fact]
    public void CsvReader_KeepsQuotedCommas()
    {
        var dir = Directory.CreateTempSubdirectory("meterp-csv-");
        try
        {
            var path = Path.Combine(dir.FullName, "customers.csv");
            File.WriteAllText(path, "CustomerName,Address,VATNumber\n\"Mines, Ltd\",\"1 Main, Road\",400\n");
            var rows = AccessCsvReader.Read(path);
            Assert.Single(rows);
            Assert.Equal("Mines, Ltd", rows[0]["CustomerName"]);
            Assert.Equal("1 Main, Road", rows[0]["Address"]);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void MapJobStatus_UsesJobCard_NotInvoicePayment()
    {
        Assert.Equal(JobStatus.InProgress, AccessImportSeeder.MapJobStatus("WIP"));
        Assert.Equal(JobStatus.Completed, AccessImportSeeder.MapJobStatus("Completed AIP"));
        Assert.Equal(JobStatus.Closed, AccessImportSeeder.MapJobStatus("Completed AO"));
        var (invoiceStatus, paid) = AccessImportSeeder.MapInvoiceStatus("Sold", 230m, 230m);
        Assert.Equal(InvoiceStatus.Paid, invoiceStatus);
        Assert.Equal(230m, paid);
        var (subtotal, tax) = AccessImportSeeder.SplitVatInclusive(230m);
        Assert.Equal(200m, subtotal);
        Assert.Equal(30m, tax);
    }

    [Fact]
    public void JobModel_EnforcesUniqueTenantJobNumberForLiveRows()
    {
        using var harness = new Harness();
        var index = harness.Db.Model.FindEntityType(typeof(Job))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "JobNumber" }));
        Assert.True(index.IsUnique);
        Assert.Equal("\"IsDeleted\" = false", index.FindAnnotation("Relational:Filter")?.Value as string);
        Assert.Equal("IX_Jobs_TenantId_JobNumber", index.FindAnnotation("Relational:Name")?.Value as string);
    }

    [Fact]
    public async Task Run_WhenDisabled_WritesNothingAndLogsSkipPhrase()
    {
        using var harness = new Harness();
        var logger = new CollectingLogger();
        var result = await AccessImportSeeder.RunAsync(
            harness.Db,
            harness.TenantProvider,
            harness.TenantId,
            new AccessImportSeeder.AccessImportOptions(AccessImportMode.Off, @"Z:\missing-csv", null, null),
            logger);

        Assert.True(result.Skipped);
        Assert.Contains(AccessImportSeeder.SkipMessage, result.Reason, StringComparison.Ordinal);
        Assert.Contains(logger.Lines, line => line.Contains(AccessImportSeeder.SkipMessage, StringComparison.Ordinal));
        Assert.Equal(0, await harness.Db.Set<Job>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await harness.Db.Set<Customer>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Run_WhenDirectoryMissing_DoesNotThrow()
    {
        using var harness = new Harness();
        var missing = Path.Combine(Path.GetTempPath(), "meterp-missing-" + Guid.NewGuid().ToString("N"));
        var result = await AccessImportSeeder.RunAsync(
            harness.Db,
            harness.TenantProvider,
            harness.TenantId,
            new AccessImportSeeder.AccessImportOptions(AccessImportMode.Write, missing, null, null),
            new CollectingLogger());

        Assert.True(result.Skipped);
        Assert.Contains("not found", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await harness.Db.Set<Invoice>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Run_DryRun_ReportsRowsWithoutWriting()
    {
        using var pack = CsvPack.Create();
        using var harness = new Harness();
        var result = await AccessImportSeeder.RunAsync(
            harness.Db,
            harness.TenantProvider,
            harness.TenantId,
            new AccessImportSeeder.AccessImportOptions(AccessImportMode.DryRun, pack.Directory, null, pack.ReportPath),
            new CollectingLogger());

        Assert.False(result.Skipped);
        Assert.True(result.DryRun);
        Assert.True(result.Step("jobs").Created + result.Step("stub-jobs").Created >= 1);
        Assert.Equal(0, await harness.Db.Set<Job>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await harness.Db.Set<Invoice>().IgnoreQueryFilters().CountAsync());
        var report = await File.ReadAllTextAsync(pack.ReportPath);
        Assert.Contains("dry-run", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_UpsertsByLegacyKey_LinksInvoice_DoesNotCloseJob_SecondRunIsIdempotent()
    {
        using var pack = CsvPack.Create();
        using var harness = new Harness();
        harness.TenantProvider.SetTenantId(harness.TenantId);

        var customer = new Customer
        {
            Name = "Kruger Park Lodge",
            VatNumber = "4000123456",
            AddressLine1 = "Skukuza",
            Country = "South Africa"
        };
        harness.Db.Set<Customer>().Add(customer);
        harness.Db.Set<Job>().Add(new Job
        {
            CustomerId = customer.Id,
            JobNumber = "FT16010",
            Title = "Existing lodge job",
            Status = JobStatus.Closed,
            DepositPercent = 30m,
            RetentionPercent = 10m,
            Notes = "Already on the MET tenant."
        });
        await harness.Db.SaveChangesAsync();
        var existingJobId = await harness.Db.Set<Job>().IgnoreQueryFilters()
            .Where(j => j.JobNumber == "FT16010")
            .Select(j => j.Id)
            .SingleAsync();

        var options = new AccessImportSeeder.AccessImportOptions(
            AccessImportMode.Write, pack.Directory, null, pack.ReportPath);

        var first = await AccessImportSeeder.RunAsync(
            harness.Db, harness.TenantProvider, harness.TenantId, options, new CollectingLogger());

        Assert.False(first.Skipped);
        Assert.Equal(0, first.UnlinkedInvoices);
        Assert.Equal(1, first.Step("stub-jobs").Created);
        Assert.Equal(0, first.Step("jobs").Created);
        Assert.True(first.Step("jobs").Skipped >= 1);
        Assert.Equal(3, first.Step("invoices").Created);
        Assert.Equal(1, first.Step("opportunities").Created);
        Assert.Equal(1, first.Step("quotes").Created);
        Assert.Equal(1, first.Step("employees").Created);

        var jobs = await harness.Db.Set<Job>().IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.TenantId == harness.TenantId && !j.IsDeleted)
            .ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Equal(2, jobs.Select(j => j.JobNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var existing = jobs.Single(j => j.JobNumber == "FT16010");
        Assert.Equal(existingJobId, existing.Id);
        Assert.Equal(JobStatus.Closed, existing.Status);
        Assert.Equal(30m, existing.DepositPercent);
        Assert.Contains("https://trello.com/c/abc", existing.Notes);
        Assert.DoesNotContain(AccessImportSeeder.StubNoteMarker, existing.Notes);

        var stub = jobs.Single(j => j.JobNumber == "FT99999");
        Assert.Equal(JobStatus.Scheduled, stub.Status);
        Assert.NotEqual(JobStatus.Closed, stub.Status);
        Assert.NotEqual(JobStatus.Invoiced, stub.Status);
        Assert.Equal(0m, stub.DepositPercent);
        Assert.Contains(AccessImportSeeder.StubNoteMarker, stub.Notes);

        var invoices = await harness.Db.Set<Invoice>().IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == harness.TenantId && !i.IsDeleted)
            .ToListAsync();
        Assert.Equal(3, invoices.Count);
        Assert.Equal(existing.Id, invoices.Single(i => i.InvoiceNumber == "INV1").JobId);
        Assert.Equal(InvoiceStatus.Sent, invoices.Single(i => i.InvoiceNumber == "INV1").Status);
        var paid = invoices.Single(i => i.InvoiceNumber == "INV2");
        Assert.Equal(stub.Id, paid.JobId);
        Assert.Equal(InvoiceStatus.Paid, paid.Status);
        Assert.Equal(InvoiceDocumentType.CreditNote, (await harness.Db.Set<Invoice>().IgnoreQueryFilters()
            .SingleAsync(i => i.InvoiceNumber == "INV3")).DocumentType);
        Assert.Equal(1, await harness.Db.Set<Customer>().IgnoreQueryFilters().CountAsync(c => c.TenantId == harness.TenantId && !c.IsDeleted));
        Assert.Equal(4, await harness.Db.Set<Division>().IgnoreQueryFilters().CountAsync(d => d.TenantId == harness.TenantId && !d.IsDeleted));
        Assert.Equal(QuoteStatus.Draft, (await harness.Db.Set<Quote>().IgnoreQueryFilters().SingleAsync()).Status);
        Assert.Equal(0, await harness.Db.Set<Job>().IgnoreQueryFilters().CountAsync(j => j.QuoteId != null));

        var second = await AccessImportSeeder.RunAsync(
            harness.Db, harness.TenantProvider, harness.TenantId, options, new CollectingLogger());

        Assert.Equal(0, second.Step("jobs").Created);
        Assert.Equal(0, second.Step("stub-jobs").Created);
        Assert.Equal(0, second.Step("invoices").Created);
        Assert.Equal(0, second.Step("opportunities").Created);
        Assert.Equal(2, await harness.Db.Set<Job>().IgnoreQueryFilters().CountAsync(j => j.TenantId == harness.TenantId && !j.IsDeleted));
        var notes = await harness.Db.Set<Job>().IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.Id == existingJobId)
            .Select(j => j.Notes)
            .SingleAsync();
        Assert.Equal(1, CountOf(notes, "https://trello.com/c/abc"));
        Assert.Equal(JobStatus.Closed, (await harness.Db.Set<Job>().IgnoreQueryFilters().SingleAsync(j => j.Id == existingJobId)).Status);
        Assert.Contains("created=", await File.ReadAllTextAsync(pack.ReportPath));
    }

    private static int CountOf(string? text, string token)
    {
        if (string.IsNullOrEmpty(text) || token.Length == 0)
            return 0;
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private sealed class CsvPack : IDisposable
    {
        public string Directory { get; }
        public string ReportPath { get; }

        private CsvPack(string directory)
        {
            Directory = directory;
            ReportPath = Path.Combine(directory, "ACCESS_IMPORT_REPORT.md");
        }

        public static CsvPack Create()
        {
            var dir = System.IO.Directory.CreateTempSubdirectory("meterp-access-").FullName;
            File.WriteAllText(Path.Combine(dir, "customers.csv"),
                """
                CustomerName,Address,VATNumber,Sources
                Kruger Park Lodge,Skukuza,4000123456,jobs
                Kruger   Park Lodge,,,dup
                """);
            File.WriteAllText(Path.Combine(dir, "jobs.csv"),
                """
                JobCardNo,TRFid,Status,Team,Customer,QuoteNo,QuoteTotal,JobCardCategory,JobCardType,CustomerOrderNo,DateReceived,DateJobStart,ExpectedCompletionDate,DateJobComplete,DateDelivered,PercComplete,LastInvoiceNo,LastInvoiceDate,InvoiceCount,TotalInvoiceAmount,StillToInvoice,Supervisor,Region,Depot,Bay,HasNotes,OnFrontendPDF,SourceSheet,SourcePack
                30558,FT16010,WIP,Field Team,Kruger Park Lodge,FT16010,1000,Fuses,STANDARD,,2026-04-08,,,,,,,,,,,,,,,,,
                """);
            File.WriteAllText(Path.Combine(dir, "invoices.csv"),
                """
                InvoiceDate,Month,Type,InvoiceNo,InvoiceAmount,SignedAmount,TRFid,Customer,JobCardNo,Status,JobCardCategory,JobCardType,Team,Region
                2026-04-09,2026-04,INV,INV1,115,0,FT16010,Kruger Park Lodge,30558,WIP,Fuses,STANDARD,Field Team,
                2026-04-10,2026-04,INV,INV2,230,230,FT99999,Kruger Park Lodge,,Sold,Callout,STANDARD,Field Team,
                2026-04-11,2026-04,CRN,INV3,50,0,FT99999,Kruger Park Lodge,,Sent,Callout,STANDARD,Field Team,
                """);
            File.WriteAllText(Path.Combine(dir, "opportunities.csv"),
                """
                Title,CustomerName,Value,Stage,ExpectedClose,Notes,QuoteNumber,Division,PrioritySource
                FT16010NB LODGE,Kruger Park Lodge,5000,Quote submitted,,Note,FT16010NB,Field Teams,HubSpot
                """);
            File.WriteAllText(Path.Combine(dir, "hubspot_opportunities_20260930.csv"),
                """
                Title,CustomerName,Value,Stage,ExpectedClose,Notes,QuoteNumber,Division,Priority/Source
                FT16010NB LODGE,Kruger Park Lodge,5000,Quote submitted,,Note,FT16010NB,Field Teams,HubSpot MET Sales
                """);
            File.WriteAllText(Path.Combine(dir, "trello_status.csv"),
                """
                TRFid,CardName,List,Board,Url,DateCreated,DateLastActivity,DescSnippet,CardId,SourceSnapshot
                FT16010,FT16010 card,WIP,MET Board,https://trello.com/c/abc,2026-04-01,2026-04-02,snippet,card1,snap
                """);
            File.WriteAllText(Path.Combine(dir, "quotes.csv"),
                """
                QuoteNumber,Customer,Total,Title
                Q-FT16010,Kruger Park Lodge,1150,Lodge quote
                """);
            File.WriteAllText(Path.Combine(dir, "employees.csv"),
                """
                EmployeeNumber,FirstName,LastName,JobTitle
                EMP-FT1,Thabo,Mokoena,Electrician
                """);
            return new CsvPack(dir);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, true); } catch { /* temp cleanup */ }
        }
    }

    private sealed class Harness : IDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public CurrentTenantProvider TenantProvider { get; }
        public AppDbContext Db { get; }

        public Harness()
        {
            TenantProvider = new CurrentTenantProvider();
            TenantProvider.SetTenantId(TenantId);
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(s => s.UserName).Returns("access-import-test");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new AppDbContext(options, TenantProvider, currentUser.Object);
        }

        public void Dispose() => Db.Dispose();
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Noop.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));

        private sealed class Noop : IDisposable
        {
            public static readonly Noop Instance = new();
            public void Dispose() { }
        }
    }
}
