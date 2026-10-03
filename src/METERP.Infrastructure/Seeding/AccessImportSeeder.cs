using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// One-way CSV import for the MET FY Access extracts under <c>_helm_walk/seed_fy2026</c>.
/// Python loaders stay the reference. This seeder never opens Access and never writes back.
/// Heavy import is off unless <c>METERP_ACCESS_IMPORT=true</c> (or <c>dry</c> for a read-only report).
/// Invoice linking does not close or reopen jobs.
/// </summary>
public static class AccessImportSeeder
{
    public const string SkipMessage = "skipped (set METERP_ACCESS_IMPORT=true to run)";

    public const string StubNoteMarker =
        "Access import stub — invoice-only TRFid. Billing does not close this job.";

    public const decimal VatRate = 0.15m;

    private const int BatchSize = 100;

    public readonly record struct AccessImportOptions(
        AccessImportMode Mode,
        string CsvDirectory,
        int? MaxRowsPerFile,
        string? ReportPath)
    {
        public bool Writes => Mode == AccessImportMode.Write;
    }

    public static AccessImportOptions OptionsFrom(IConfiguration config, string? contentRoot)
    {
        var flag = Environment.GetEnvironmentVariable("METERP_ACCESS_IMPORT");
        if (string.IsNullOrWhiteSpace(flag))
            flag = config["Seed:AccessImport"];

        var envDir = Environment.GetEnvironmentVariable("METERP_ACCESS_CSV_DIR");
        var configuredDir = config["Seed:AccessCsvDir"];
        var root = string.IsNullOrWhiteSpace(contentRoot) ? AppContext.BaseDirectory : contentRoot;
        int? maxRows = null;
        var maxRaw = Environment.GetEnvironmentVariable("METERP_ACCESS_IMPORT_MAX_ROWS");
        if (int.TryParse(maxRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            maxRows = parsed;

        return new AccessImportOptions(
            SeedProfileGates.ParseAccessImportFlag(flag),
            ResolveCsvDirectory(envDir, configuredDir, root),
            maxRows,
            ReportPath: null);
    }

    public static string ResolveCsvDirectory(string? environmentDir, string? configuredDir, string contentRoot)
    {
        var chosen = FirstNonWhite(environmentDir, configuredDir);
        if (chosen == null)
            return Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "_helm_walk", "seed_fy2026"));

        return Path.IsPathRooted(chosen)
            ? Path.GetFullPath(chosen)
            : Path.GetFullPath(Path.Combine(contentRoot, chosen));
    }

    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var parts = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    public static decimal ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0m;

        var s = raw.Trim().Replace(" ", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }

    public static (decimal Subtotal, decimal Tax) SplitVatInclusive(decimal total, decimal rate = VatRate)
    {
        if (total == 0 || rate <= 0)
            return (total, 0m);

        var subtotal = Math.Round(total / (1 + rate), 2, MidpointRounding.AwayFromZero);
        var tax = Math.Round(total - subtotal, 2, MidpointRounding.AwayFromZero);
        return (subtotal, tax);
    }

    public static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var s = raw.Trim();
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            return dto.UtcDateTime;

        return null;
    }

    /// <summary>
    /// Job-card status only. Invoice payment never maps onto this — billing does not close the job.
    /// </summary>
    public static JobStatus MapJobStatus(string? raw)
    {
        return (raw ?? "").Trim() switch
        {
            "Completed AIP" => JobStatus.Completed,
            "Completed AO" => JobStatus.Closed,
            "WIP" or "FT WIP - no package" or "Solar-WIP no package" => JobStatus.InProgress,
            _ => JobStatus.InProgress
        };
    }

    public static (InvoiceStatus Status, decimal AmountPaid) MapInvoiceStatus(string? statusCsv, decimal total, decimal signed)
    {
        var status = (statusCsv ?? "").Trim().ToLowerInvariant();
        // Totals are stored positive. Access SignedAmount is negative on a credit note, so compare magnitudes.
        var totalMagnitude = Math.Abs(total);
        var signedMagnitude = Math.Abs(signed);
        if (totalMagnitude != 0m && Math.Abs(signedMagnitude - totalMagnitude) < 0.01m
            && status is "sold" or "completed aip" or "trf stock completed")
            return (InvoiceStatus.Paid, totalMagnitude);

        if (status is "scrapped")
            return (InvoiceStatus.Cancelled, 0m);

        if (signedMagnitude > 0m && signedMagnitude < totalMagnitude - 0.01m)
            return (InvoiceStatus.PartiallyPaid, signedMagnitude);

        return (InvoiceStatus.Sent, 0m);
    }

    public static (OpportunityStage Stage, int Probability) MapOpportunityStage(string? raw)
    {
        var stageRaw = (raw ?? "").Trim().ToLowerInvariant();
        var stage = stageRaw switch
        {
            "quote submitted" => OpportunityStage.Proposal,
            "qualified" => OpportunityStage.Qualified,
            "lead" => OpportunityStage.Lead,
            "proposal" => OpportunityStage.Proposal,
            "negotiation" => OpportunityStage.Negotiation,
            "closed won" => OpportunityStage.ClosedWon,
            "closed lost" => OpportunityStage.ClosedLost,
            _ => stageRaw.Contains("quote", StringComparison.Ordinal)
                ? OpportunityStage.Proposal
                : OpportunityStage.Lead
        };

        var probability = stage switch
        {
            OpportunityStage.Lead => 10,
            OpportunityStage.Qualified => 25,
            OpportunityStage.Proposal => 50,
            OpportunityStage.Negotiation => 75,
            OpportunityStage.ClosedWon => 100,
            _ => 0
        };

        return (stage, probability);
    }

    public static string OpportunityLegacyKey(IReadOnlyDictionary<string, string> row)
    {
        var quote = AccessCsvReader.Field(row, "QuoteNumber", "QuoteNo");
        if (quote.Length > 0)
            return quote;

        var title = AccessCsvReader.Field(row, "Title");
        var token = title.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (token.Length >= 3 && token.Any(char.IsLetter) && token.Any(char.IsDigit))
            return token;

        var normalized = NormalizeName(title);
        return normalized.Length > 0 ? normalized : title;
    }

    public static async Task<AccessImportResult> RunAsync(
        AppDbContext db,
        ITenantProvider tenantProvider,
        Guid tenantId,
        AccessImportOptions options,
        ILogger logger,
        CancellationToken ct = default)
    {
        var result = new AccessImportResult { DryRun = options.Mode == AccessImportMode.DryRun };
        if (options.Mode == AccessImportMode.Off)
        {
            result.Skipped = true;
            result.Reason = SkipMessage;
            logger.LogInformation(
                "Access CSV import {Reason}. Configured directory: {Directory}.",
                SkipMessage,
                options.CsvDirectory);
            return result;
        }

        if (tenantId == Guid.Empty)
        {
            result.Skipped = true;
            result.Reason = "tenant id is empty";
            logger.LogWarning("Access CSV import skipped — tenant id is empty.");
            return result;
        }

        if (!Directory.Exists(options.CsvDirectory))
        {
            result.Skipped = true;
            result.Reason = $"CSV directory not found: {options.CsvDirectory}";
            logger.LogWarning(
                "Access CSV import skipped — directory not found: {Directory}. Acme startup is unaffected.",
                options.CsvDirectory);
            return result;
        }

        var previousTenant = tenantProvider.GetCurrentTenantId();
        tenantProvider.SetTenantId(tenantId);
        var session = new ImportSession(db, tenantId, options, result, ct);
        try
        {
            logger.LogInformation(
                "Access CSV import starting ({Mode}) from {Directory} for tenant {TenantId}. One-way CSV only — Access is not opened.",
                options.Mode,
                options.CsvDirectory,
                tenantId);

            await session.LoadExistingAsync();
            await session.ImportCustomersAsync();
            await session.ImportDivisionsAsync();
            await session.ImportOpportunitiesAsync("opportunities.csv");
            await session.ImportHubSpotOpportunitiesAsync();
            await session.ImportJobsAsync();
            await session.ImportInvoiceStubJobsAsync();
            await session.ImportInvoicesAsync();
            await session.OverlayTrelloAsync();
            await session.ImportOptionalQuotesAsync();
            await session.ImportOptionalEmployeesAsync();
            await session.FlushAsync(force: true);
            result.UnlinkedInvoices = session.UnlinkedInvoiceNumbers.Count;
            result.Reason = options.Mode == AccessImportMode.DryRun ? "dry-run" : "wrote";
        }
        finally
        {
            tenantProvider.SetTenantId(previousTenant);
            var reportPath = options.ReportPath
                ?? Path.Combine(options.CsvDirectory, "ACCESS_IMPORT_REPORT.md");
            try
            {
                WriteReport(reportPath, options, tenantId, result);
                result.ReportPath = reportPath;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Access CSV import report was not written to {Path}.", reportPath);
            }

            logger.LogInformation("Access CSV import result: {Summary}", result.Summary);
        }

        return result;
    }

    public static void WriteReport(string path, AccessImportOptions options, Guid tenantId, AccessImportResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Access CSV import report");
        sb.AppendLine();
        sb.AppendLine($"- Mode: {(result.DryRun ? "dry-run (no writes)" : options.Mode.ToString())}");
        sb.AppendLine($"- Directory: `{options.CsvDirectory}`");
        sb.AppendLine($"- Tenant: `{tenantId}`");
        sb.AppendLine($"- Unlinked invoices (no JobId): {result.UnlinkedInvoices}");
        sb.AppendLine("- One-way CSV only. Access was not opened and nothing was written back.");
        sb.AppendLine("- Invoice rows link JobId when the TRFid matches. Billing does not close or reopen the job.");
        sb.AppendLine();
        sb.AppendLine("| Step | Created | Updated | Skipped |");
        sb.AppendLine("|------|--------:|--------:|--------:|");
        foreach (var step in result.Steps)
            sb.AppendLine($"| {step.Key} | {step.Value.Created} | {step.Value.Updated} | {step.Value.Skipped} |");

        if (result.Notes.Count > 0)
        {
            sb.AppendLine();
            foreach (var note in result.Notes)
                sb.AppendLine($"- {note}");
        }

        sb.AppendLine();
        sb.AppendLine(result.Summary);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string? FirstNonWhite(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private sealed class ImportSession
    {
        private readonly AppDbContext _db;
        private readonly Guid _tenantId;
        private readonly AccessImportOptions _options;
        private readonly AccessImportResult _result;
        private readonly CancellationToken _ct;
        private int _pending;

        private readonly Dictionary<string, CustomerSnap> _customers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _divisions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JobSnap> _jobs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _jobCards = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _invoices = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _opportunities = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _quotes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid> _employees = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> UnlinkedInvoiceNumbers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ImportSession(AppDbContext db, Guid tenantId, AccessImportOptions options, AccessImportResult result, CancellationToken ct)
        {
            _db = db;
            _tenantId = tenantId;
            _options = options;
            _result = result;
            _ct = ct;
        }

        public async Task LoadExistingAsync()
        {
            var customers = await _db.Set<Customer>().IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.TenantId == _tenantId && !c.IsDeleted)
                .Select(c => new { c.Id, c.Name, c.VatNumber, c.AddressLine1 })
                .ToListAsync(_ct);
            foreach (var customer in customers)
            {
                var key = NormalizeName(customer.Name);
                if (key.Length > 0)
                    _customers.TryAdd(key, new CustomerSnap(customer.Id, customer.VatNumber, customer.AddressLine1));
            }

            var divisions = await _db.Set<Division>().IgnoreQueryFilters().AsNoTracking()
                .Where(d => d.TenantId == _tenantId && !d.IsDeleted)
                .Select(d => new { d.Id, d.Code })
                .ToListAsync(_ct);
            foreach (var division in divisions)
            {
                if (!string.IsNullOrWhiteSpace(division.Code))
                    _divisions.TryAdd(division.Code.Trim(), division.Id);
            }

            var jobs = await _db.Set<Job>().IgnoreQueryFilters().AsNoTracking()
                .Where(j => j.TenantId == _tenantId && !j.IsDeleted)
                .Select(j => new { j.Id, j.JobNumber, j.Notes, j.Status })
                .ToListAsync(_ct);
            foreach (var job in jobs)
            {
                var number = (job.JobNumber ?? "").Trim();
                if (number.Length == 0)
                    continue;
                var stub = job.Notes != null && job.Notes.Contains("Access import stub", StringComparison.OrdinalIgnoreCase);
                _jobs.TryAdd(number, new JobSnap(job.Id, stub, job.Status));
            }

            var opportunities = await _db.Set<Opportunity>().IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.TenantId == _tenantId && !o.IsDeleted)
                .Select(o => new { o.Id, o.Title, o.Notes })
                .ToListAsync(_ct);
            foreach (var opportunity in opportunities)
                RememberOpportunity(opportunity.Id, opportunity.Title, opportunity.Notes);

            var invoices = await _db.Set<Invoice>().IgnoreQueryFilters().AsNoTracking()
                .Where(i => i.TenantId == _tenantId && !i.IsDeleted)
                .Select(i => new { i.Id, i.InvoiceNumber, i.JobId })
                .ToListAsync(_ct);
            foreach (var invoice in invoices)
            {
                var number = (invoice.InvoiceNumber ?? "").Trim();
                if (number.Length == 0)
                    continue;
                if (_invoices.TryAdd(number, invoice.Id) && invoice.JobId == null)
                    UnlinkedInvoiceNumbers.Add(number);
            }

            var quotes = await _db.Set<Quote>().IgnoreQueryFilters().AsNoTracking()
                .Where(q => q.TenantId == _tenantId && !q.IsDeleted)
                .Select(q => new { q.Id, q.QuoteNumber })
                .ToListAsync(_ct);
            foreach (var quote in quotes)
            {
                var number = (quote.QuoteNumber ?? "").Trim();
                if (number.Length > 0)
                    _quotes.TryAdd(number, quote.Id);
            }

            var employees = await _db.Set<Employee>().IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.TenantId == _tenantId && !e.IsDeleted)
                .Select(e => new { e.Id, e.EmployeeNumber })
                .ToListAsync(_ct);
            foreach (var employee in employees)
            {
                var number = (employee.EmployeeNumber ?? "").Trim();
                if (number.Length > 0)
                    _employees.TryAdd(number, employee.Id);
            }
        }

        public async Task ImportCustomersAsync()
        {
            var counts = _result.Step("customers");
            var rows = ReadFile("customers.csv", counts);
            if (rows == null)
                return;

            foreach (var row in rows)
            {
                var name = AccessCsvReader.Field(row, "CustomerName", "Name", "Customer");
                var key = NormalizeName(name);
                if (key.Length == 0)
                {
                    counts.Skipped++;
                    continue;
                }

                var address = AccessCsvReader.Field(row, "Address", "AddressLine1");
                var vat = AccessCsvReader.Field(row, "VATNumber", "VatNumber");
                var sources = AccessCsvReader.Field(row, "Sources");
                if (_customers.TryGetValue(key, out var existing))
                {
                    if (await FillBlankCustomerAsync(existing, address, vat))
                        counts.Updated++;
                    else
                        counts.Skipped++;
                    continue;
                }

                var created = AddCustomer(name, address, vat, sources.Length > 0 ? $"Sources: {sources}" : null);
                _customers[key] = created;
                counts.Created++;
            }

            await FlushAsync();
        }

        public async Task ImportDivisionsAsync()
        {
            var counts = _result.Step("divisions");
            (string Code, string Name)[] standard =
            [
                ("FT", "Field Teams"),
                ("WS", "Workshop"),
                ("SD", "Solar Division"),
                ("JHB", "Johannesburg Operations")
            ];
            foreach (var item in standard)
                EnsureDivision(item.Code, item.Name, counts);

            var rows = ReadFile("divisions.csv", counts, missingIsQuiet: true);
            if (rows == null)
            {
                _result.Notes.Add("divisions.csv not present — standard FT/WS/SD/JHB codes ensured, Cape Town Operations is not created.");
            }
            else
            {
                foreach (var row in rows)
                {
                    var code = AccessCsvReader.Field(row, "Code");
                    var name = AccessCsvReader.Field(row, "Name");
                    if (code.Length == 0 || name.Length == 0)
                    {
                        counts.Skipped++;
                        continue;
                    }

                    EnsureDivision(code, name, counts);
                }
            }

            await FlushAsync();
        }

        public async Task ImportHubSpotOpportunitiesAsync()
        {
            var hubspot = Directory.GetFiles(_options.CsvDirectory, "hubspot_opportunities*.csv");
            if (hubspot.Length == 0)
            {
                _result.Notes.Add("hubspot_opportunities*.csv not present — HubSpot hook skipped.");
                return;
            }

            Array.Sort(hubspot, StringComparer.OrdinalIgnoreCase);
            foreach (var path in hubspot)
                await ImportOpportunityRowsAsync(AccessCsvReader.Read(path, _options.MaxRowsPerFile));
        }

        public Task ImportOpportunitiesAsync(string fileName) =>
            ImportOpportunityRowsAsync(ReadFile(fileName, _result.Step("opportunities")));

        public async Task ImportJobsAsync()
        {
            var counts = _result.Step("jobs");
            var rows = ReadFile("jobs.csv", counts);
            if (rows == null)
                return;

            foreach (var row in rows)
            {
                var trf = AccessCsvReader.Field(row, "TRFid", "JobNumber");
                var jobCard = AccessCsvReader.Field(row, "JobCardNo");
                var jobNumber = trf.Length > 0 ? trf : jobCard;
                if (jobNumber.Length == 0)
                {
                    counts.Skipped++;
                    continue;
                }

                var customerName = AccessCsvReader.Field(row, "Customer", "CustomerName");
                var customerId = ResolveCustomerId(customerName, "Auto-created during access CSV job import");
                if (customerId == null)
                {
                    counts.Skipped++;
                    continue;
                }

                var divisionId = DivisionForTeam(AccessCsvReader.Field(row, "Team"));
                var status = MapJobStatus(FirstNonWhite(AccessCsvReader.Field(row, "Status"), AccessCsvReader.Field(row, "SourceSheet")));
                var title = AccessCsvReader.Field(row, "JobCardCategory");
                if (title.Length == 0)
                    title = $"Job {jobNumber}";
                if (title.Length > 300)
                    title = title[..297] + "...";

                var description = JoinParts(
                    Label("Type", AccessCsvReader.Field(row, "JobCardType")),
                    Label("Team", AccessCsvReader.Field(row, "Team")),
                    Label("Region", AccessCsvReader.Field(row, "Region")),
                    Label("Depot", AccessCsvReader.Field(row, "Depot")),
                    jobCard.Length > 0 && !jobCard.Equals(jobNumber, StringComparison.OrdinalIgnoreCase) ? $"JobCardNo: {jobCard}" : null,
                    Label("CustomerOrderNo", AccessCsvReader.Field(row, "CustomerOrderNo")),
                    Label("QuoteNo", AccessCsvReader.Field(row, "QuoteNo")),
                    Label("SourceSheet", AccessCsvReader.Field(row, "SourceSheet")));
                var notes = JoinLines(
                    Label("Supervisor", AccessCsvReader.Field(row, "Supervisor")),
                    Label("StillToInvoice", AccessCsvReader.Field(row, "StillToInvoice")),
                    Label("LastInvoiceNo", AccessCsvReader.Field(row, "LastInvoiceNo")));
                var quoted = ParseDecimal(AccessCsvReader.Field(row, "QuoteTotal"));
                var scheduled = ParseDate(AccessCsvReader.Field(row, "DateJobStart"))
                    ?? ParseDate(AccessCsvReader.Field(row, "DateReceived"));
                var completed = ParseDate(AccessCsvReader.Field(row, "DateJobComplete"))
                    ?? ParseDate(AccessCsvReader.Field(row, "DateDelivered"));

                if (_jobs.TryGetValue(jobNumber, out var existing))
                {
                    if (existing.IsStub)
                    {
                        await PromoteStubAsync(existing.Id, title, description, notes, status, quoted, divisionId, scheduled, completed);
                        _jobs[jobNumber] = existing with { IsStub = false, Status = status };
                        counts.Updated++;
                    }
                    else
                    {
                        counts.Skipped++;
                    }
                }
                else
                {
                    var id = Guid.NewGuid();
                    Add(new Job
                    {
                        Id = id,
                        CustomerId = customerId.Value,
                        DivisionId = divisionId,
                        JobNumber = jobNumber,
                        Title = title,
                        Description = description,
                        Status = status,
                        ScheduledStart = scheduled,
                        CompletedDate = completed,
                        QuotedTotal = quoted,
                        ActualCost = 0m,
                        Notes = notes,
                        DepositPercent = 30m,
                        RetentionPercent = 10m,
                        DepositReceived = false,
                        SignOffStatus = JobSignOffStatus.None,
                        IsEmergency = false
                    });
                    _jobs[jobNumber] = new JobSnap(id, false, status);
                    counts.Created++;
                }

                if (jobCard.Length > 0 && _jobs.TryGetValue(jobNumber, out var linked))
                    _jobCards.TryAdd(jobCard, linked.Id);
            }

            await FlushAsync();
        }

        public async Task ImportInvoiceStubJobsAsync()
        {
            var counts = _result.Step("stub-jobs");
            var rows = ReadFile("invoices.csv", counts, missingIsQuiet: true);
            if (rows == null)
            {
                _result.Notes.Add("invoices.csv not present — no invoice-only stub jobs.");
                return;
            }

            foreach (var row in rows)
            {
                var trf = AccessCsvReader.Field(row, "TRFid");
                if (trf.Length == 0 || _jobs.ContainsKey(trf))
                    continue;

                var customerName = AccessCsvReader.Field(row, "Customer", "CustomerName");
                var customerId = ResolveCustomerId(customerName, "Auto-created during access CSV invoice stub");
                if (customerId == null)
                {
                    counts.Skipped++;
                    continue;
                }

                var id = Guid.NewGuid();
                var divisionId = DivisionForTeam(AccessCsvReader.Field(row, "Team"));
                Add(new Job
                {
                    Id = id,
                    CustomerId = customerId.Value,
                    DivisionId = divisionId,
                    JobNumber = trf,
                    Title = Truncate($"TRFid {trf} (invoice stub)", 300),
                    Description = "Created so an invoice-only TRFid can link. Not a weekly job-card row.",
                    Status = JobStatus.Scheduled,
                    QuotedTotal = 0m,
                    ActualCost = 0m,
                    Notes = StubNoteMarker,
                    DepositPercent = 0m,
                    RetentionPercent = 0m,
                    DepositReceived = false,
                    SignOffStatus = JobSignOffStatus.None,
                    IsEmergency = false
                });
                _jobs[trf] = new JobSnap(id, true, JobStatus.Scheduled);
                var jobCard = AccessCsvReader.Field(row, "JobCardNo");
                if (jobCard.Length > 0)
                    _jobCards.TryAdd(jobCard, id);
                counts.Created++;
            }

            await FlushAsync();
        }

        public async Task ImportInvoicesAsync()
        {
            var counts = _result.Step("invoices");
            var rows = ReadFile("invoices.csv", counts, missingIsQuiet: true);
            if (rows == null)
                return;

            foreach (var row in rows)
            {
                var invoiceNumber = AccessCsvReader.Field(row, "InvoiceNo", "InvoiceNumber");
                if (invoiceNumber.Length == 0)
                {
                    counts.Skipped++;
                    continue;
                }

                var customerName = AccessCsvReader.Field(row, "Customer", "CustomerName");
                var customerId = ResolveCustomerId(customerName, "Auto-created during access CSV invoice import");
                if (customerId == null)
                {
                    counts.Skipped++;
                    continue;
                }

                var trf = AccessCsvReader.Field(row, "TRFid");
                var jobCard = AccessCsvReader.Field(row, "JobCardNo");
                Guid? jobId = null;
                if (trf.Length > 0 && _jobs.TryGetValue(trf, out var byTrf))
                    jobId = byTrf.Id;
                else if (jobCard.Length > 0 && _jobCards.TryGetValue(jobCard, out var byCard))
                    jobId = byCard;

                if (_invoices.TryGetValue(invoiceNumber, out var existingId))
                {
                    if (jobId != null && UnlinkedInvoiceNumbers.Contains(invoiceNumber))
                    {
                        await LinkInvoiceAsync(existingId, jobId.Value);
                        UnlinkedInvoiceNumbers.Remove(invoiceNumber);
                        counts.Updated++;
                    }
                    else
                    {
                        counts.Skipped++;
                    }

                    continue;
                }

                var total = ParseDecimal(AccessCsvReader.Field(row, "InvoiceAmount", "Total"));
                var signed = ParseDecimal(AccessCsvReader.Field(row, "SignedAmount"));
                var (subtotal, tax) = SplitVatInclusive(total);
                var invoiceDate = ParseDate(AccessCsvReader.Field(row, "InvoiceDate")) ?? DateTime.UtcNow;
                var (status, amountPaid) = MapInvoiceStatus(AccessCsvReader.Field(row, "Status"), total, signed);
                var documentType = AccessCsvReader.Field(row, "Type").Equals("CRN", StringComparison.OrdinalIgnoreCase)
                    ? InvoiceDocumentType.CreditNote
                    : InvoiceDocumentType.Standard;
                var category = AccessCsvReader.Field(row, "JobCardCategory");
                var description = Truncate($"{(category.Length > 0 ? category : "Services")} ({(trf.Length > 0 ? trf : jobCard.Length > 0 ? jobCard : invoiceNumber)})", 500);
                var invoiceId = Guid.NewGuid();
                var invoice = new Invoice
                {
                    Id = invoiceId,
                    CustomerId = customerId.Value,
                    JobId = jobId,
                    InvoiceNumber = invoiceNumber,
                    InvoiceDate = invoiceDate,
                    DueDate = invoiceDate.AddDays(30),
                    Status = status,
                    DocumentType = documentType,
                    Notes = $"Access CSV | Team={AccessCsvReader.Field(row, "Team")} | Status={AccessCsvReader.Field(row, "Status")} | TRFid={trf} | JobCardNo={jobCard}",
                    Subtotal = subtotal,
                    TaxRate = VatRate,
                    Tax = tax,
                    Total = total,
                    AmountPaid = amountPaid,
                    RetentionAmount = 0m,
                    RetentionPercent = 0m
                };
                invoice.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoiceId,
                    Invoice = invoice,
                    Description = description,
                    Quantity = 1m,
                    UnitPrice = subtotal,
                    Unit = "ea",
                    LineType = "Service"
                });
                Add(invoice);
                _invoices[invoiceNumber] = invoiceId;
                if (jobId == null)
                    UnlinkedInvoiceNumbers.Add(invoiceNumber);
                counts.Created++;
            }

            await FlushAsync();
            if (_options.Writes)
            {
                var link = await CreditNoteLinkSeeder.RunAsync(_db, _tenantId, _ct);
                if (link.Linked + link.Normalized + link.Settled > 0)
                    _result.Notes.Add("Credit notes: " + link.Summary);
            }
        }

        public async Task OverlayTrelloAsync()
        {
            var counts = _result.Step("trello");
            var path = Path.Combine(_options.CsvDirectory, "trello_status.csv");
            if (!File.Exists(path))
            {
                _result.Notes.Add("trello_status.csv not present — overlay skipped.");
                counts.Skipped++;
                return;
            }

            var additions = new Dictionary<Guid, List<string>>();
            foreach (var row in AccessCsvReader.Read(path, _options.MaxRowsPerFile))
            {
                var trf = AccessCsvReader.Field(row, "TRFid");
                if (trf.Length == 0 || !_jobs.TryGetValue(trf, out var job))
                {
                    counts.Skipped++;
                    continue;
                }

                var url = AccessCsvReader.Field(row, "Url");
                var board = AccessCsvReader.Field(row, "Board");
                var list = AccessCsvReader.Field(row, "List");
                var card = AccessCsvReader.Field(row, "CardName");
                var snippet = AccessCsvReader.Field(row, "DescSnippet");
                var line = $"Trello[{board}/{list}]: {card} {url}".Trim();
                if (snippet.Length > 0)
                    line += " — " + Truncate(snippet, 120);
                additions.TryAdd(job.Id, []);
                additions[job.Id].Add(line);
            }

            if (additions.Count == 0)
                return;

            if (!_options.Writes)
            {
                counts.Updated += additions.Count;
                return;
            }

            foreach (var chunk in additions.Keys.Chunk(500))
            {
                var jobs = await _db.Set<Job>().IgnoreQueryFilters()
                    .Where(j => j.TenantId == _tenantId && chunk.Contains(j.Id))
                    .ToListAsync(_ct);
                foreach (var job in jobs)
                {
                    var lines = additions[job.Id];
                    var fresh = lines.Where(line => job.Notes == null || !job.Notes.Contains(MarkerToken(line), StringComparison.Ordinal)).Take(3).ToList();
                    if (fresh.Count == 0 || (job.Notes?.Length ?? 0) > 8000)
                    {
                        counts.Skipped += lines.Count;
                        continue;
                    }

                    var addition = string.Join('\n', fresh);
                    if (lines.Count > fresh.Count)
                        addition += $"\n(+{lines.Count - fresh.Count} more Trello cards)";
                    job.Notes = string.IsNullOrWhiteSpace(job.Notes) ? addition : job.Notes.TrimEnd() + "\n" + addition;
                    counts.Updated++;
                    counts.Skipped += lines.Count - fresh.Count;
                    _pending++;
                }
            }

            await FlushAsync();
        }

        public async Task ImportOptionalQuotesAsync()
        {
            var counts = _result.Step("quotes");
            var rows = ReadFile("quotes.csv", counts, missingIsQuiet: true);
            if (rows == null)
            {
                _result.Notes.Add("quotes.csv not present — quote hook skipped. No quote is converted to a job.");
                return;
            }

            if (rows.Count > 0 && !rows[0].Keys.Any(k => k.Equals("QuoteNumber", StringComparison.OrdinalIgnoreCase)))
            {
                counts.Skipped += rows.Count;
                _result.Notes.Add("quotes.csv has no QuoteNumber column — hook skipped.");
                return;
            }

            foreach (var row in rows)
            {
                var number = AccessCsvReader.Field(row, "QuoteNumber", "QuoteNo");
                var customerName = AccessCsvReader.Field(row, "Customer", "CustomerName");
                if (number.Length == 0 || customerName.Length == 0)
                {
                    counts.Skipped++;
                    continue;
                }

                if (_quotes.ContainsKey(number))
                {
                    counts.Skipped++;
                    continue;
                }

                var customerId = ResolveCustomerId(customerName, "Auto-created during access CSV quote hook");
                if (customerId == null)
                {
                    counts.Skipped++;
                    continue;
                }

                var total = ParseDecimal(AccessCsvReader.Field(row, "Total", "QuoteTotal", "Value"));
                var (subtotal, tax) = SplitVatInclusive(total);
                var title = AccessCsvReader.Field(row, "Title");
                var quoteDate = ParseDate(AccessCsvReader.Field(row, "QuoteDate")) ?? DateTime.UtcNow;
                var quoteId = Guid.NewGuid();
                var quote = new Quote
                {
                    Id = quoteId,
                    CustomerId = customerId.Value,
                    QuoteNumber = number,
                    QuoteDate = quoteDate,
                    ValidUntil = quoteDate.AddDays(30),
                    Status = QuoteStatus.Draft,
                    Notes = $"LegacyKey:{number}\nAccess CSV quote hook. Not converted to a job.",
                    Subtotal = subtotal,
                    TaxRate = VatRate,
                    Tax = tax,
                    Total = total
                };
                if (total != 0m)
                {
                    quote.Lines.Add(new QuoteLine
                    {
                        Id = Guid.NewGuid(),
                        QuoteId = quoteId,
                        Quote = quote,
                        Description = title.Length > 0 ? Truncate(title, 300) : $"Quoted work {number}",
                        Quantity = 1m,
                        UnitPrice = subtotal,
                        Unit = "lot",
                        LineType = "Service"
                    });
                }

                Add(quote);
                _quotes[number] = quoteId;
                counts.Created++;
            }

            await FlushAsync();
        }

        public async Task ImportOptionalEmployeesAsync()
        {
            var counts = _result.Step("employees");
            var rows = ReadFile("employees.csv", counts, missingIsQuiet: true);
            if (rows == null)
            {
                _result.Notes.Add("employees.csv not present — employee hook skipped.");
                return;
            }

            foreach (var row in rows)
            {
                var number = AccessCsvReader.Field(row, "EmployeeNumber", "EmployeeNo");
                var first = AccessCsvReader.Field(row, "FirstName");
                var last = AccessCsvReader.Field(row, "LastName");
                if (number.Length == 0 || first.Length == 0 || last.Length == 0)
                {
                    counts.Skipped++;
                    continue;
                }

                if (_employees.ContainsKey(number))
                {
                    counts.Skipped++;
                    continue;
                }

                var id = Guid.NewGuid();
                Add(new Employee
                {
                    Id = id,
                    EmployeeNumber = number,
                    FirstName = first,
                    LastName = last,
                    JobTitle = NullIfEmpty(AccessCsvReader.Field(row, "JobTitle")),
                    DefaultHourlyRate = ParseDecimal(AccessCsvReader.Field(row, "DefaultHourlyRate")),
                    IsActive = true,
                    Notes = "Access CSV employee hook.",
                    AnnualLeaveEntitlementDays = 15m
                });
                _employees[number] = id;
                counts.Created++;
            }

            await FlushAsync();
        }

        public async Task FlushAsync(bool force = false)
        {
            if (_pending == 0)
                return;
            if (!force && _pending < BatchSize)
                return;
            if (_options.Writes)
            {
                await _db.SaveChangesAsync(_ct);
                _db.ChangeTracker.Clear();
            }

            _pending = 0;
        }

        private async Task ImportOpportunityRowsAsync(IReadOnlyList<Dictionary<string, string>>? rows)
        {
            var counts = _result.Step("opportunities");
            if (rows == null)
                return;

            var order = _opportunities.Count;
            foreach (var row in rows)
            {
                var title = AccessCsvReader.Field(row, "Title");
                var key = OpportunityLegacyKey(row);
                if (title.Length == 0 || key.Length == 0)
                {
                    counts.Skipped++;
                    continue;
                }

                if (_opportunities.ContainsKey(key) || _opportunities.ContainsKey(NormalizeName(title)))
                {
                    counts.Skipped++;
                    continue;
                }

                var customerName = AccessCsvReader.Field(row, "CustomerName", "Customer");
                var customerId = customerName.Length > 0
                    ? ResolveCustomerId(customerName, "Auto-created during access CSV opportunity import")
                    : null;
                var (stage, probability) = MapOpportunityStage(AccessCsvReader.Field(row, "Stage"));
                var expected = ParseDate(AccessCsvReader.Field(row, "ExpectedClose")) ?? DateTime.UtcNow.AddDays(30);
                var notes = JoinLines(
                    $"LegacyKey:{key}",
                    NullIfEmpty(AccessCsvReader.Field(row, "Notes")),
                    Label("QuoteNumber", AccessCsvReader.Field(row, "QuoteNumber")),
                    Label("Division", AccessCsvReader.Field(row, "Division")),
                    Label("Source", AccessCsvReader.Field(row, "PrioritySource", "Priority/Source")));
                var id = Guid.NewGuid();
                Add(new Opportunity
                {
                    Id = id,
                    Title = Truncate(title, 500),
                    CustomerId = customerId,
                    CustomerName = NullIfEmpty(customerName),
                    Value = ParseDecimal(AccessCsvReader.Field(row, "Value")),
                    Stage = stage,
                    ExpectedClose = expected,
                    Notes = notes,
                    BoardOrder = order++,
                    DealType = OpportunityDealType.NewWork,
                    Priority = OpportunityPriority.Medium,
                    ProbabilityPercent = probability,
                    Source = OpportunitySource.Other
                });
                RememberOpportunity(id, title, notes);
                counts.Created++;
            }

            await FlushAsync();
        }

        private void RememberOpportunity(Guid id, string? title, string? notes)
        {
            var legacy = ReadMarkedValue(notes, "LegacyKey:");
            if (legacy != null)
                _opportunities.TryAdd(legacy, id);
            var quote = ReadMarkedValue(notes, "QuoteNumber:");
            if (quote != null)
                _opportunities.TryAdd(quote, id);
            var normalized = NormalizeName(title);
            if (normalized.Length > 0)
                _opportunities.TryAdd(normalized, id);
        }

        private async Task<bool> FillBlankCustomerAsync(CustomerSnap existing, string address, string vat)
        {
            var fillVat = vat.Length > 0 && string.IsNullOrWhiteSpace(existing.VatNumber);
            var fillAddress = address.Length > 0 && string.IsNullOrWhiteSpace(existing.AddressLine1);
            if (!fillVat && !fillAddress)
                return false;

            if (_options.Writes)
            {
                var entity = await _db.Set<Customer>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == existing.Id && c.TenantId == _tenantId, _ct);
                if (entity == null)
                    return false;
                if (fillVat)
                    entity.VatNumber = vat;
                if (fillAddress)
                    entity.AddressLine1 = address;
                _pending++;
            }

            var key = _customers.First(pair => pair.Value.Id == existing.Id).Key;
            _customers[key] = existing with
            {
                VatNumber = fillVat ? vat : existing.VatNumber,
                AddressLine1 = fillAddress ? address : existing.AddressLine1
            };
            return true;
        }

        private async Task PromoteStubAsync(
            Guid jobId,
            string title,
            string? description,
            string? notes,
            JobStatus status,
            decimal quoted,
            Guid? divisionId,
            DateTime? scheduled,
            DateTime? completed)
        {
            if (!_options.Writes)
                return;

            var job = await _db.Set<Job>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.Id == jobId && j.TenantId == _tenantId, _ct);
            if (job == null || job.Notes == null || !job.Notes.Contains("Access import stub", StringComparison.OrdinalIgnoreCase))
                return;

            job.Title = title;
            job.Description = description;
            job.Notes = string.IsNullOrWhiteSpace(notes) ? "Promoted from access import stub by jobs.csv." : notes;
            job.Status = status;
            job.QuotedTotal = quoted;
            job.DivisionId = divisionId ?? job.DivisionId;
            job.ScheduledStart = scheduled ?? job.ScheduledStart;
            job.CompletedDate = completed ?? job.CompletedDate;
            job.DepositPercent = 30m;
            job.RetentionPercent = 10m;
            _pending++;
        }

        private async Task LinkInvoiceAsync(Guid invoiceId, Guid jobId)
        {
            if (!_options.Writes)
                return;

            var invoice = await _db.Set<Invoice>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == _tenantId, _ct);
            if (invoice == null || invoice.JobId != null)
                return;

            invoice.JobId = jobId;
            _pending++;
        }

        private Guid? ResolveCustomerId(string name, string createdNote)
        {
            var key = NormalizeName(name);
            if (key.Length == 0)
                return null;
            if (_customers.TryGetValue(key, out var existing))
                return existing.Id;

            var created = AddCustomer(name.Trim(), null, null, createdNote);
            _customers[key] = created;
            _result.Step("customers").Created++;
            return created.Id;
        }

        private CustomerSnap AddCustomer(string name, string? address, string? vat, string? notes)
        {
            var id = Guid.NewGuid();
            Add(new Customer
            {
                Id = id,
                Name = name.Trim(),
                AddressLine1 = NullIfEmpty(address),
                VatNumber = NullIfEmpty(vat),
                Country = "South Africa",
                Notes = notes
            });
            return new CustomerSnap(id, vat, address);
        }

        private void EnsureDivision(string code, string name, AccessImportCounts counts)
        {
            var trimmed = code.Trim();
            if (_divisions.ContainsKey(trimmed))
            {
                counts.Skipped++;
                return;
            }

            var id = Guid.NewGuid();
            Add(new Division
            {
                Id = id,
                Code = trimmed,
                Name = name.Trim(),
                IsActive = true
            });
            _divisions[trimmed] = id;
            counts.Created++;
        }

        private Guid? DivisionForTeam(string team)
        {
            var value = team.Trim().ToLowerInvariant();
            var code = value.Contains("solar", StringComparison.Ordinal) ? "SD"
                : value.Contains("workshop", StringComparison.Ordinal) ? "WS"
                : "FT";
            return _divisions.TryGetValue(code, out var id) ? id : null;
        }

        private void Add(BaseEntity entity)
        {
            entity.TenantId = _tenantId;
            if (_options.Writes)
            {
                _db.Add(entity);
                _pending++;
            }
        }

        private IReadOnlyList<Dictionary<string, string>>? ReadFile(string fileName, AccessImportCounts counts, bool missingIsQuiet = false)
        {
            var path = Path.Combine(_options.CsvDirectory, fileName);
            if (!File.Exists(path))
            {
                if (!missingIsQuiet)
                {
                    counts.Skipped++;
                    _result.Notes.Add($"{fileName} not present.");
                }

                return null;
            }

            return AccessCsvReader.Read(path, _options.MaxRowsPerFile);
        }

    }

    private static string? ReadMarkedValue(string? notes, string marker)
    {
        if (string.IsNullOrEmpty(notes))
            return null;

        foreach (var rawLine in notes.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = line[marker.Length..].Trim().TrimStart(':').Trim();
            if (value.Length == 0)
                continue;
            var token = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
            return token;
        }

        return null;
    }

    private static string MarkerToken(string line)
    {
        var urlAt = line.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        return urlAt >= 0 ? line[urlAt..] : line;
    }

    private static string? Label(string name, string value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{name}: {value.Trim()}";

    private static string? JoinParts(params string?[] parts)
    {
        var values = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return values.Length == 0 ? null : string.Join(" | ", values);
    }

    private static string? JoinLines(params string?[] parts)
    {
        var values = parts.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return values.Length == 0 ? null : string.Join('\n', values);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 3)] + "...";

    private sealed record CustomerSnap(Guid Id, string? VatNumber, string? AddressLine1);

    private sealed record JobSnap(Guid Id, bool IsStub, JobStatus Status);
}

public sealed class AccessImportResult
{
    public bool Skipped { get; set; }
    public bool DryRun { get; set; }
    public string Reason { get; set; } = "";
    public int UnlinkedInvoices { get; set; }
    public string? ReportPath { get; set; }
    public List<string> Notes { get; } = [];
    public Dictionary<string, AccessImportCounts> Steps { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AccessImportCounts Step(string name)
    {
        if (!Steps.TryGetValue(name, out var counts))
        {
            counts = new AccessImportCounts();
            Steps[name] = counts;
        }

        return counts;
    }

    public string Summary
    {
        get
        {
            if (Skipped && Steps.Count == 0)
                return Reason;

            var steps = string.Join("; ", Steps.Select(step => $"{step.Key} {step.Value}"));
            var mode = DryRun ? "DRY RUN " : "";
            return $"{mode}{steps}; unlinked invoices={UnlinkedInvoices}";
        }
    }
}

public sealed class AccessImportCounts
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }

    public override string ToString() => $"created={Created} updated={Updated} skipped={Skipped}";
}
