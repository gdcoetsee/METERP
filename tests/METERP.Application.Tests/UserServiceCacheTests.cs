using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Common;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class UserServiceCacheTests
{
    private sealed class TestHarness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public RoleManager<ApplicationRole> RoleManager { get; }
        public TenantDistributedCacheService Cache { get; }
        public UserService Service { get; }

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
            UserManager = CreateUserManager(Db);
            RoleManager = CreateRoleManager(Db);

            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
            var provider = services.BuildServiceProvider();
            Cache = new TenantDistributedCacheService(
                provider.GetRequiredService<IDistributedCache>(),
                tenantProvider.Object,
                provider.GetRequiredService<IOptions<CacheOptions>>());

            Service = new UserService(UserManager, RoleManager, tenantProvider.Object, Db, Cache);
        }

        public void Dispose() => Db.Dispose();

        private static UserManager<ApplicationUser> CreateUserManager(AppDbContext db)
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var store = new UserStore<ApplicationUser, ApplicationRole, AppDbContext, Guid>(db);
            return new UserManager<ApplicationUser>(
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

        private static RoleManager<ApplicationRole> CreateRoleManager(AppDbContext db)
        {
            var store = new RoleStore<ApplicationRole, AppDbContext, Guid>(db);
            return new RoleManager<ApplicationRole>(
                store,
                new IRoleValidator<ApplicationRole>[] { new RoleValidator<ApplicationRole>() },
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new LoggerFactory().CreateLogger<RoleManager<ApplicationRole>>());
        }
    }

    private static async Task SeedRoleAsync(RoleManager<ApplicationRole> roleManager, Guid tenantId, string name)
    {
        var role = new ApplicationRole { Name = name, TenantId = tenantId };
        var result = await roleManager.CreateAsync(role);
        Assert.True(result.Succeeded);
        await roleManager.AddClaimAsync(role, new Claim("Permission", Permissions.QuotesView));
    }

    private static async Task SeedUserAsync(UserManager<ApplicationUser> userManager, Guid tenantId, string email)
    {
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            TenantId = tenantId
        };
        var result = await userManager.CreateAsync(user, "TestPass1!");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);
        await SeedUserAsync(harness.UserManager, tenantId, "original@acme.demo");

        Assert.Equal("original@acme.demo", (await harness.Service.GetAllAsync())[0].Email);
        (await harness.Db.Users.FirstAsync()).Email = "mutated@acme.demo";
        (await harness.Db.Users.FirstAsync()).UserName = "mutated@acme.demo";
        await harness.Db.SaveChangesAsync();
        Assert.Equal("original@acme.demo", (await harness.Service.GetAllAsync())[0].Email);

        harness.Cache.InvalidateCategory("users");
        Assert.Equal("mutated@acme.demo", (await harness.Service.GetAllAsync())[0].Email);
    }

    [Fact]
    public async Task CreateUserAsync_InvalidatesUserListCache()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);
        await SeedRoleAsync(harness.RoleManager, tenantId, "Viewer");
        await SeedUserAsync(harness.UserManager, tenantId, "first@acme.demo");

        Assert.Single(await harness.Service.GetAllAsync());
        var (ok, errors) = await harness.Service.CreateUserAsync("second@acme.demo", "SecurePass1!", "Viewer");
        Assert.True(ok, string.Join("; ", errors));
        Assert.Equal(2, (await harness.Service.GetAllAsync()).Count);
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);
        await SeedUserAsync(harness.UserManager, tenantId, "alpha@acme.demo");
        await SeedUserAsync(harness.UserManager, tenantId, "beta@acme.demo");
        await harness.Service.GetAllAsync(pageSize: 50);

        var beta = await harness.Db.Users.FirstAsync(u => u.Email == "beta@acme.demo");
        beta.Email = "beta-mutated@acme.demo";
        beta.UserName = "beta-mutated@acme.demo";
        await harness.Db.SaveChangesAsync();

        Assert.Equal("beta-mutated@acme.demo", (await harness.Service.GetAllAsync(search: "beta", pageSize: 50))[0].Email);
    }

    [Fact]
    public async Task GetAllAsync_WithWhitespaceSearch_UsesCache()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);
        await SeedUserAsync(harness.UserManager, tenantId, "cached@acme.demo");
        await harness.Service.GetAllAsync(pageSize: 50);

        var user = await harness.Db.Users.FirstAsync();
        user.Email = "db-mutated@acme.demo";
        user.UserName = "db-mutated@acme.demo";
        await harness.Db.SaveChangesAsync();

        Assert.Equal("cached@acme.demo", (await harness.Service.GetAllAsync(search: "   ", pageSize: 50))[0].Email);
    }

    [Fact]
    public async Task GetAvailableRolesAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);
        await SeedRoleAsync(harness.RoleManager, tenantId, "Alpha");
        await SeedRoleAsync(harness.RoleManager, tenantId, "Zulu");

        var first = await harness.Service.GetAvailableRolesAsync();
        Assert.Equal(2, first.Count);
        Assert.Equal("Alpha", first[0]);

        var role = await harness.Db.Roles.FirstAsync(r => r.Name == "Alpha");
        role.Name = "Mutated";
        role.NormalizedName = "MUTATED";
        await harness.Db.SaveChangesAsync();

        Assert.Equal("Alpha", (await harness.Service.GetAvailableRolesAsync())[0]);

        harness.Cache.InvalidateCategory("roles");
        var refreshed = await harness.Service.GetAvailableRolesAsync();
        Assert.Contains("Mutated", refreshed);
    }

    [Fact]
    public async Task CreateUserAsync_InvalidatesRolesCache()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);
        await SeedRoleAsync(harness.RoleManager, tenantId, "Viewer");
        await harness.Service.GetAvailableRolesAsync();

        var role = await harness.Db.Roles.FirstAsync();
        role.Name = "ChangedRole";
        role.NormalizedName = "CHANGEDROLE";
        await harness.Db.SaveChangesAsync();

        Assert.Equal("Viewer", (await harness.Service.GetAvailableRolesAsync())[0]);

        var (ok, errors) = await harness.Service.CreateUserAsync("roles@acme.demo", "SecurePass1!", "ChangedRole");
        Assert.True(ok, string.Join("; ", errors));
        Assert.Equal("ChangedRole", (await harness.Service.GetAvailableRolesAsync())[0]);
    }
}