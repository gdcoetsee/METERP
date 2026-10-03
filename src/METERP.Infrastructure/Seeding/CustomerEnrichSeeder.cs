using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// One-way fill-blank enrich from <c>customers_enrich.csv</c>.
/// Matches existing customers by <see cref="AccessImportSeeder.NormalizeName"/> and never inserts a customer.
/// Non-blank customer fields are left as they are. Does not open Access and does not require
/// <c>METERP_ACCESS_IMPORT</c>.
/// </summary>
public static class CustomerEnrichSeeder
{
    public const string FileName = "customers_enrich.csv";

    public const string ContactMarker = "Contact:";

    public const string SkipOffMessage = "skipped (METERP_CUSTOMER_ENRICH is off)";

    public const string SkipMissingFileMessage = "skipped (customers_enrich.csv not found)";

    public enum Mode
    {
        Off = 0,
        Auto = 1,
        On = 2
    }

    public readonly record struct Options(Mode Mode, string CsvDirectory);

    /// <summary>
    /// Blank means leave the customer field unchanged. A value is the text to write into a blank field.
    /// </summary>
    public readonly record struct FillPlan(
        string? AddressLine1,
        string? AddressLine2,
        string? City,
        string? PostalCode,
        string? Phone,
        string? Email,
        string? VatNumber,
        string? Notes)
    {
        public bool Any =>
            AddressLine1 != null
            || AddressLine2 != null
            || City != null
            || PostalCode != null
            || Phone != null
            || Email != null
            || VatNumber != null
            || Notes != null;
    }

    public readonly record struct Snapshot(
        string? AddressLine1,
        string? AddressLine2,
        string? City,
        string? PostalCode,
        string? Phone,
        string? Email,
        string? VatNumber,
        string? Notes)
    {
        public static Snapshot From(Customer customer) => new(
            customer.AddressLine1,
            customer.AddressLine2,
            customer.City,
            customer.PostalCode,
            customer.Phone,
            customer.Email,
            customer.VatNumber,
            customer.Notes);
    }

    public readonly record struct Incoming(
        string Address,
        string AddressLine2,
        string City,
        string PostalCode,
        string Phone,
        string Cell,
        string Email,
        string ContactPerson,
        string VatNumber)
    {
        public static Incoming From(IReadOnlyDictionary<string, string> row) => new(
            AccessCsvReader.Field(row, "Address", "AddressLine1"),
            AccessCsvReader.Field(row, "AddressLine2"),
            AccessCsvReader.Field(row, "City"),
            AccessCsvReader.Field(row, "PostalCode"),
            AccessCsvReader.Field(row, "Phone", "TelNo", "Tel"),
            AccessCsvReader.Field(row, "Cell", "Mobile"),
            AccessCsvReader.Field(row, "Email", "eMail"),
            AccessCsvReader.Field(row, "ContactPerson", "Contact"),
            AccessCsvReader.Field(row, "VATNumber", "VatNumber"));
    }

    public static Options OptionsFrom(IConfiguration config, string? contentRoot)
    {
        var envDir = Environment.GetEnvironmentVariable("METERP_ACCESS_CSV_DIR");
        var configuredDir = config["Seed:AccessCsvDir"];
        var root = string.IsNullOrWhiteSpace(contentRoot) ? AppContext.BaseDirectory : contentRoot;
        return new Options(
            ResolveMode(Environment.GetEnvironmentVariable("METERP_CUSTOMER_ENRICH"), config["Seed:CustomerEnrich"]),
            AccessImportSeeder.ResolveCsvDirectory(envDir, configuredDir, root));
    }

    /// <summary>
    /// Environment flag wins. Unset or blank means <see cref="Mode.Auto"/> (run when the file exists).
    /// Explicit false/0/no/off skips. Anything unrecognised also skips.
    /// </summary>
    public static Mode ResolveMode(string? environmentFlag, string? configuredFlag)
    {
        var flag = string.IsNullOrWhiteSpace(environmentFlag) ? configuredFlag : environmentFlag;
        return ParseFlag(flag);
    }

    public static Mode ParseFlag(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return Mode.Auto;

        return flag.Trim().ToLowerInvariant() switch
        {
            "false" or "0" or "no" or "off" => Mode.Off,
            "true" or "1" or "yes" or "on" or "write" => Mode.On,
            _ => Mode.Off
        };
    }

    /// <summary>
    /// Phone uses the Phone column when it has text, otherwise Cell. VAT is filled only when the CSV has a number.
    /// Contact person is written into Notes when Notes is blank or does not already contain <see cref="ContactMarker"/>.
    /// </summary>
    public static FillPlan PlanFill(Snapshot current, Incoming incoming)
    {
        var phoneSource = !string.IsNullOrWhiteSpace(incoming.Phone) ? incoming.Phone : incoming.Cell;
        return new FillPlan(
            Take(current.AddressLine1, incoming.Address),
            Take(current.AddressLine2, incoming.AddressLine2),
            Take(current.City, incoming.City),
            Take(current.PostalCode, incoming.PostalCode),
            Take(current.Phone, phoneSource),
            Take(current.Email, incoming.Email),
            Take(current.VatNumber, incoming.VatNumber),
            PlanNotes(current.Notes, incoming.ContactPerson));
    }

    public static void Apply(Customer customer, FillPlan plan)
    {
        if (plan.AddressLine1 != null)
            customer.AddressLine1 = plan.AddressLine1;
        if (plan.AddressLine2 != null)
            customer.AddressLine2 = plan.AddressLine2;
        if (plan.City != null)
            customer.City = plan.City;
        if (plan.PostalCode != null)
            customer.PostalCode = plan.PostalCode;
        if (plan.Phone != null)
            customer.Phone = plan.Phone;
        if (plan.Email != null)
            customer.Email = plan.Email;
        if (plan.VatNumber != null)
            customer.VatNumber = plan.VatNumber;
        if (plan.Notes != null)
            customer.Notes = plan.Notes;
    }

    public static async Task<CustomerEnrichResult> RunAsync(
        AppDbContext db,
        ITenantProvider tenantProvider,
        Guid tenantId,
        Options options,
        ILogger logger,
        CancellationToken ct = default)
    {
        var result = new CustomerEnrichResult { Mode = options.Mode };
        if (options.Mode == Mode.Off)
        {
            result.Skipped = true;
            result.Reason = SkipOffMessage;
            logger.LogInformation("Customer enrich {Reason}.", SkipOffMessage);
            return result;
        }

        if (tenantId == Guid.Empty)
        {
            result.Skipped = true;
            result.Reason = "tenant id is empty";
            logger.LogWarning("Customer enrich skipped — tenant id is empty.");
            return result;
        }

        var path = Path.Combine(options.CsvDirectory, FileName);
        if (!File.Exists(path))
        {
            result.Skipped = true;
            result.Reason = $"{SkipMissingFileMessage}: {path}";
            logger.LogInformation(
                "Customer enrich {Reason} at {Path}. Existing customers unchanged.",
                SkipMissingFileMessage,
                path);
            return result;
        }

        var previousTenant = tenantProvider.GetCurrentTenantId();
        tenantProvider.SetTenantId(tenantId);
        try
        {
            logger.LogInformation(
                "Customer enrich starting ({Mode}) from {Path} for tenant {TenantId}. Fill-blank only; no new customers; Access is not opened. METERP_ACCESS_IMPORT is not required.",
                options.Mode,
                path,
                tenantId);

            var rows = AccessCsvReader.Read(path);
            var customers = await db.Set<Customer>().IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && !c.IsDeleted)
                .ToListAsync(ct);

            var byName = new Dictionary<string, Customer>(StringComparer.Ordinal);
            foreach (var customer in customers)
            {
                var key = AccessImportSeeder.NormalizeName(customer.Name);
                if (key.Length == 0)
                    continue;
                if (!byName.TryAdd(key, customer))
                    result.DuplicateNameKeys++;
            }

            result.CustomersBefore = customers.Count;
            var pending = 0;
            foreach (var row in rows)
            {
                var name = AccessCsvReader.Field(row, "CustomerName", "Name", "Customer");
                var key = AccessImportSeeder.NormalizeName(name);
                if (key.Length == 0)
                {
                    result.SkippedBlankName++;
                    continue;
                }

                if (!byName.TryGetValue(key, out var customer))
                {
                    result.Unmatched++;
                    if (result.UnmatchedSample.Count < 8)
                        result.UnmatchedSample.Add(name.Trim());
                    continue;
                }

                result.Matched++;
                var plan = PlanFill(Snapshot.From(customer), Incoming.From(row));
                if (!plan.Any)
                {
                    result.SkippedNoBlank++;
                    continue;
                }

                Apply(customer, plan);
                result.Updated++;
                if (plan.Phone != null)
                    result.FilledPhone++;
                if (plan.Email != null)
                    result.FilledEmail++;
                if (plan.AddressLine1 != null || plan.AddressLine2 != null || plan.City != null)
                    result.FilledAddress++;
                if (plan.PostalCode != null)
                    result.FilledPostal++;
                if (plan.VatNumber != null)
                    result.FilledVat++;
                if (plan.Notes != null)
                    result.FilledContact++;
                pending++;
            }

            var illegal = db.ChangeTracker.Entries<Customer>()
                .Count(entry => entry.State is EntityState.Added or EntityState.Deleted);
            if (illegal > 0)
                throw new InvalidOperationException(
                    $"Customer enrich must not insert or delete customers (tracker had {illegal}).");

            if (pending > 0)
                await db.SaveChangesAsync(ct);

            result.CustomersAfter = await db.Set<Customer>().IgnoreQueryFilters()
                .CountAsync(c => c.TenantId == tenantId && !c.IsDeleted, ct);
            result.Reason = "wrote";

            logger.LogInformation(
                "Customer enrich done. matched={Matched} updated={Updated} unmatched={Unmatched} skipped-no-blank={SkippedNoBlank} skipped-blank-name={SkippedBlankName} created=0 phones={Phones} emails={Emails} addresses={Addresses} postal={Postal} vat={Vat} contacts={Contacts} customers {Before}->{After} duplicate-name-keys={DuplicateKeys}.",
                result.Matched,
                result.Updated,
                result.Unmatched,
                result.SkippedNoBlank,
                result.SkippedBlankName,
                result.FilledPhone,
                result.FilledEmail,
                result.FilledAddress,
                result.FilledPostal,
                result.FilledVat,
                result.FilledContact,
                result.CustomersBefore,
                result.CustomersAfter,
                result.DuplicateNameKeys);

            if (result.UnmatchedSample.Count > 0)
            {
                logger.LogInformation(
                    "Customer enrich unmatched sample ({Count} of {Unmatched}): {Sample}",
                    result.UnmatchedSample.Count,
                    result.Unmatched,
                    string.Join(" | ", result.UnmatchedSample));
            }

            if (result.CustomersAfter != result.CustomersBefore)
            {
                logger.LogWarning(
                    "Customer enrich changed customer count from {Before} to {After}.",
                    result.CustomersBefore,
                    result.CustomersAfter);
            }
        }
        finally
        {
            tenantProvider.SetTenantId(previousTenant);
        }

        return result;
    }

    private static string? Take(string? current, string incoming)
    {
        if (!string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(incoming))
            return null;

        return incoming.Trim();
    }

    private static string? PlanNotes(string? notes, string contactPerson)
    {
        if (string.IsNullOrWhiteSpace(contactPerson))
            return null;

        var line = ContactMarker + " " + contactPerson.Trim();
        if (string.IsNullOrWhiteSpace(notes))
            return line;

        if (notes.Contains(ContactMarker, StringComparison.OrdinalIgnoreCase))
            return null;

        return notes.TrimEnd() + "\n" + line;
    }
}

public sealed class CustomerEnrichResult
{
    public bool Skipped { get; set; }
    public string Reason { get; set; } = "";
    public CustomerEnrichSeeder.Mode Mode { get; set; }
    public int Matched { get; set; }
    public int Updated { get; set; }
    public int Unmatched { get; set; }
    public int SkippedNoBlank { get; set; }
    public int SkippedBlankName { get; set; }
    public int FilledPhone { get; set; }
    public int FilledEmail { get; set; }
    public int FilledAddress { get; set; }
    public int FilledPostal { get; set; }
    public int FilledVat { get; set; }
    public int FilledContact { get; set; }
    public int CustomersBefore { get; set; }
    public int CustomersAfter { get; set; }
    public int DuplicateNameKeys { get; set; }
    public List<string> UnmatchedSample { get; } = [];

    public string Summary =>
        Skipped
            ? Reason
            : $"matched={Matched} updated={Updated} unmatched={Unmatched} skipped-no-blank={SkippedNoBlank} skipped-blank-name={SkippedBlankName} created=0 phones={FilledPhone} emails={FilledEmail} addresses={FilledAddress} postal={FilledPostal} vat={FilledVat} contacts={FilledContact} customers {CustomersBefore}->{CustomersAfter} duplicate-name-keys={DuplicateNameKeys}";
}
