using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Seeding;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class SoftDeletedTenantUserLockTests
{
    private sealed class TestHarness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public UserManager<ApplicationUser> UserManager { get; }

        public TestHarness(Guid tenantId)
        {
            TenantId = tenantId;
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
            var services = new ServiceCollection().BuildServiceProvider();
            var store = new UserStore<ApplicationUser, ApplicationRole, AppDbContext, Guid>(Db);
            UserManager = new UserManager<ApplicationUser>(
                store,
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                new IUserValidator<ApplicationUser>[] { new UserValidator<ApplicationUser>() },
                new IPasswordValidator<ApplicationUser>[] { new PasswordValidator<ApplicationUser>() },
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services,
                new LoggerFactory().CreateLogger<UserManager<ApplicationUser>>());
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<ApplicationUser> SeedUserAsync(
        UserManager<ApplicationUser> userManager,
        Guid tenantId,
        string email)
    {
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            TenantId = tenantId
        };
        var result = await userManager.CreateAsync(user, "TestPass1!");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    [Fact]
    public async Task RunAsync_LocksUsersOnSoftDeletedTenant_AndKeepsProtectedDemos()
    {
        var metId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        using var harness = new TestHarness(metId);

        harness.Db.Tenants.Add(new Tenant
        {
            Id = betaId,
            Name = "Beta Corp (Demo)",
            Subdomain = "beta-lock",
            IsDeleted = true
        });
        await harness.Db.SaveChangesAsync();

        await SeedUserAsync(harness.UserManager, betaId, "admin@beta.demo");
        await SeedUserAsync(harness.UserManager, betaId, "admin@acme.demo");
        await SeedUserAsync(harness.UserManager, metId, "admin@met.demo");
        await SeedUserAsync(harness.UserManager, metId, "gregory@met.co.za");
        await SeedUserAsync(harness.UserManager, metId, "manager@acme.demo");
        await SeedUserAsync(harness.UserManager, metId, "tech@acme.demo");

        var result = await SoftDeletedTenantUserLock.RunAsync(harness.Db);

        Assert.Equal(1, result.Locked);
        Assert.Equal("admin@beta.demo", Assert.Single(result.Emails));

        var beta = await harness.Db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "admin@beta.demo");
        Assert.True(beta.LockoutEnabled);
        Assert.NotNull(beta.LockoutEnd);
        Assert.True(beta.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50));

        var protectedEmails = new[]
        {
            "admin@acme.demo",
            "admin@met.demo",
            "gregory@met.co.za",
            "manager@acme.demo",
            "tech@acme.demo"
        };
        foreach (var email in protectedEmails)
        {
            var user = await harness.Db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            Assert.Null(user.LockoutEnd);
        }

        Assert.Equal(6, await harness.Db.Users.IgnoreQueryFilters().CountAsync());

        var again = await SoftDeletedTenantUserLock.RunAsync(harness.Db);
        Assert.Equal(0, again.Locked);
        Assert.Equal(1, again.AlreadyLocked);
    }
}
