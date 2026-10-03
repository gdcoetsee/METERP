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

public class CustomerEnrichSeederTests
{
    [Theory]
    [InlineData(null, null, CustomerEnrichSeeder.Mode.Auto)]
    [InlineData("", null, CustomerEnrichSeeder.Mode.Auto)]
    [InlineData(null, "true", CustomerEnrichSeeder.Mode.On)]
    [InlineData("  ", "yes", CustomerEnrichSeeder.Mode.On)]
    [InlineData("true", "false", CustomerEnrichSeeder.Mode.On)]
    [InlineData("false", "true", CustomerEnrichSeeder.Mode.Off)]
    [InlineData("0", null, CustomerEnrichSeeder.Mode.Off)]
    [InlineData("off", null, CustomerEnrichSeeder.Mode.Off)]
    [InlineData("no", null, CustomerEnrichSeeder.Mode.Off)]
    [InlineData("maybe", null, CustomerEnrichSeeder.Mode.Off)]
    public void ResolveMode_EnvWins_UnsetIsAuto(string? env, string? config, CustomerEnrichSeeder.Mode expected) =>
        Assert.Equal(expected, CustomerEnrichSeeder.ResolveMode(env, config));

    [Fact]
    public void PlanFill_FillsBlanks_AndNeverOverwrites()
    {
        var current = new CustomerEnrichSeeder.Snapshot(
            AddressLine1: "Keep Street",
            AddressLine2: null,
            City: " ",
            PostalCode: "0001",
            Phone: "011-KEEP",
            Email: null,
            VatNumber: "KEEP-VAT",
            Notes: "Existing account");

        var incoming = new CustomerEnrichSeeder.Incoming(
            Address: "New Mill",
            AddressLine2: "Unit 2",
            City: "Sabie",
            PostalCode: "1260",
            Phone: "0137649200",
            Cell: "0826548405",
            Email: "TashKiesling@york.co.za",
            ContactPerson: "TASCHIA KIESLING",
            VatNumber: "4999999999");

        var plan = CustomerEnrichSeeder.PlanFill(current, incoming);

        Assert.Null(plan.AddressLine1);
        Assert.Equal("Unit 2", plan.AddressLine2);
        Assert.Equal("Sabie", plan.City);
        Assert.Null(plan.PostalCode);
        Assert.Null(plan.Phone);
        Assert.Equal("TashKiesling@york.co.za", plan.Email);
        Assert.Null(plan.VatNumber);
        Assert.Equal("Existing account\nContact: TASCHIA KIESLING", plan.Notes);
    }

    [Fact]
    public void PlanFill_PrefersPhoneThenCell_AndSkipsBlankVat()
    {
        var blank = new CustomerEnrichSeeder.Snapshot(null, null, null, null, null, null, null, null);

        var phoneAndCell = CustomerEnrichSeeder.PlanFill(
            blank,
            new CustomerEnrichSeeder.Incoming("", "", "", "", "0137649200", "0826548405", "", "", ""));
        Assert.Equal("0137649200", phoneAndCell.Phone);
        Assert.Null(phoneAndCell.VatNumber);

        var cellOnly = CustomerEnrichSeeder.PlanFill(
            blank,
            new CustomerEnrichSeeder.Incoming("", "", "", "", "  ", "0821112222", "", "", ""));
        Assert.Equal("0821112222", cellOnly.Phone);

        var withMarker = CustomerEnrichSeeder.PlanFill(
            blank with { Notes = "Contact: ALREADY" },
            new CustomerEnrichSeeder.Incoming("", "", "", "", "", "", "", "SOMEONE ELSE", ""));
        Assert.Null(withMarker.Notes);
    }

    [Fact]
    public void NormalizeName_MatchesEnrichKey() =>
        Assert.Equal(
            "york timbers - plywood",
            AccessImportSeeder.NormalizeName("  YORK   TIMBERS - PLYWOOD "));

    [Fact]
    public async Task RunAsync_FillsBlanks_IsIdempotent_AndCreatesNoCustomers()
    {
        await using var harness = new Harness();
        var york = harness.Add("  YORK   TIMBERS - plywood ");
        var kruger = harness.Add("Kruger Park Lodge", phone: "013-KEEP");
        var cellOnly = harness.Add("Cell Only Co");
        var hasVat = harness.Add("Has Vat Co", vat: "KEEP-VAT");
        harness.Add("Gone Co", deleted: true);
        await harness.Db.SaveChangesAsync();

        var otherTenantId = Guid.NewGuid();
        harness.TenantProvider.SetTenantId(otherTenantId);
        var otherTenant = harness.Add("Other Tenant Co", tenantId: otherTenantId);
        await harness.Db.SaveChangesAsync();
        harness.TenantProvider.SetTenantId(harness.TenantId);
        var before = await harness.CountLiveAsync();

        var dir = Directory.CreateTempSubdirectory("meterp-enrich-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "customers_enrich.csv"),
                """
                CustomerName,Address,AddressLine2,City,PostalCode,Phone,Cell,Email,ContactPerson,VATNumber,Notes,Source
                York Timbers - PLYWOOD,"1 Mill Road, Sabie",Unit 2,Sabie,1260,0137649200,0826548405,TashKiesling@york.co.za,TASCHIA KIESLING,,,Access.Customers
                Kruger Park Lodge,"P O BOX 989, HAZYVIEW",,,,(013) 737 7386,,,"ARCHIE COOMBS",,,Access.Customers
                Cell Only Co,,,,,,0821112222,,,,,Access.Customers
                Has Vat Co,,,,,,,,,4123456789,,Access.Customers
                Gone Co,,,,,0110000000,,,,,,Access.Customers
                Other Tenant Co,,,,,099,,,,,,,Access.Customers
                Brand New Co,,,,,0111111111,,,,,,Access.Customers
                ,,,,,011000,,,,,,
                """);

            var logger = new CollectingLogger();
            var options = new CustomerEnrichSeeder.Options(CustomerEnrichSeeder.Mode.On, dir.FullName);
            var first = await CustomerEnrichSeeder.RunAsync(
                harness.Db, harness.TenantProvider, harness.TenantId, options, logger);

            Assert.False(first.Skipped);
            Assert.Equal(before, first.CustomersBefore);
            Assert.Equal(before, first.CustomersAfter);
            Assert.Equal(before, await harness.CountLiveAsync());
            Assert.Equal(4, first.Matched);
            Assert.Equal(3, first.Updated);
            Assert.Equal(3, first.Unmatched);
            Assert.Equal(1, first.SkippedNoBlank);
            Assert.Equal(1, first.SkippedBlankName);
            Assert.Equal(0, first.FilledVat);
            Assert.Contains("created=0", first.Summary, StringComparison.Ordinal);

            harness.Db.ChangeTracker.Clear();
            var yorkDb = await harness.Db.Set<Customer>().IgnoreQueryFilters().SingleAsync(c => c.Id == york.Id);
            Assert.Equal("1 Mill Road, Sabie", yorkDb.AddressLine1);
            Assert.Equal("Unit 2", yorkDb.AddressLine2);
            Assert.Equal("Sabie", yorkDb.City);
            Assert.Equal("1260", yorkDb.PostalCode);
            Assert.Equal("0137649200", yorkDb.Phone);
            Assert.Equal("TashKiesling@york.co.za", yorkDb.Email);
            Assert.True(string.IsNullOrWhiteSpace(yorkDb.VatNumber));
            Assert.Equal("Contact: TASCHIA KIESLING", yorkDb.Notes);

            var krugerDb = await harness.Db.Set<Customer>().IgnoreQueryFilters().SingleAsync(c => c.Id == kruger.Id);
            Assert.Equal("013-KEEP", krugerDb.Phone);
            Assert.Equal("P O BOX 989, HAZYVIEW", krugerDb.AddressLine1);
            Assert.Equal("Contact: ARCHIE COOMBS", krugerDb.Notes);

            var cellDb = await harness.Db.Set<Customer>().IgnoreQueryFilters().SingleAsync(c => c.Id == cellOnly.Id);
            Assert.Equal("0821112222", cellDb.Phone);

            var vatDb = await harness.Db.Set<Customer>().IgnoreQueryFilters().SingleAsync(c => c.Id == hasVat.Id);
            Assert.Equal("KEEP-VAT", vatDb.VatNumber);

            var deleted = await harness.Db.Set<Customer>().IgnoreQueryFilters().SingleAsync(c => c.Name == "Gone Co");
            Assert.True(deleted.IsDeleted);
            Assert.True(string.IsNullOrWhiteSpace(deleted.Phone));

            var foreign = await harness.Db.Set<Customer>().IgnoreQueryFilters().SingleAsync(c => c.Id == otherTenant.Id);
            Assert.True(string.IsNullOrWhiteSpace(foreign.Phone));

            var names = await harness.Db.Set<Customer>().IgnoreQueryFilters()
                .Select(c => c.Name)
                .ToListAsync();
            Assert.DoesNotContain(names, name => name.Contains("Brand New", StringComparison.OrdinalIgnoreCase));

            var second = await CustomerEnrichSeeder.RunAsync(
                harness.Db, harness.TenantProvider, harness.TenantId, options, logger);
            Assert.Equal(0, second.Updated);
            Assert.Equal(4, second.Matched);
            Assert.Equal(4, second.SkippedNoBlank);
            Assert.Equal(before, second.CustomersAfter);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public async Task RunAsync_OffOrMissingFile_LeavesCustomersAlone()
    {
        await using var harness = new Harness();
        var customer = harness.Add("York Timbers - PLYWOOD");
        await harness.Db.SaveChangesAsync();

        var dir = Directory.CreateTempSubdirectory("meterp-enrich-skip-");
        try
        {
            var csv = Path.Combine(dir.FullName, "customers_enrich.csv");
            File.WriteAllText(csv, "CustomerName,Phone\nYork Timbers - PLYWOOD,0137649200\n");
            var logger = new CollectingLogger();

            var off = await CustomerEnrichSeeder.RunAsync(
                harness.Db,
                harness.TenantProvider,
                harness.TenantId,
                new CustomerEnrichSeeder.Options(CustomerEnrichSeeder.Mode.Off, dir.FullName),
                logger);
            Assert.True(off.Skipped);
            Assert.Equal(CustomerEnrichSeeder.SkipOffMessage, off.Reason);
            Assert.Contains(logger.Lines, line => line.Contains(CustomerEnrichSeeder.SkipOffMessage, StringComparison.Ordinal));

            var missing = await CustomerEnrichSeeder.RunAsync(
                harness.Db,
                harness.TenantProvider,
                harness.TenantId,
                new CustomerEnrichSeeder.Options(CustomerEnrichSeeder.Mode.Auto, Path.Combine(dir.FullName, "absent")),
                logger);
            Assert.True(missing.Skipped);
            Assert.Contains(CustomerEnrichSeeder.SkipMissingFileMessage, missing.Reason, StringComparison.Ordinal);

            harness.Db.ChangeTracker.Clear();
            var stored = await harness.Db.Set<Customer>().SingleAsync(c => c.Id == customer.Id);
            Assert.True(string.IsNullOrWhiteSpace(stored.Phone));
            Assert.Equal(1, await harness.CountLiveAsync());
        }
        finally
        {
            dir.Delete(true);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public CurrentTenantProvider TenantProvider { get; }
        public AppDbContext Db { get; }

        public Harness()
        {
            TenantProvider = new CurrentTenantProvider();
            TenantProvider.SetTenantId(TenantId);
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(s => s.UserName).Returns("customer-enrich-test");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new AppDbContext(options, TenantProvider, currentUser.Object);
        }

        public Customer Add(string name, string? phone = null, string? vat = null, bool deleted = false, Guid? tenantId = null)
        {
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId ?? TenantId,
                Name = name,
                Phone = phone,
                VatNumber = vat,
                IsDeleted = deleted,
                Country = "South Africa"
            };
            Db.Set<Customer>().Add(customer);
            return customer;
        }

        public Task<int> CountLiveAsync() =>
            Db.Set<Customer>().IgnoreQueryFilters().CountAsync(c => c.TenantId == TenantId && !c.IsDeleted);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
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
