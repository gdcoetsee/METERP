using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Common;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class UserServiceTests
{
    private sealed class TestHarness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public RoleManager<ApplicationRole> RoleManager { get; }
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
            Service = new UserService(UserManager, RoleManager, tenantProvider.Object, Db);
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

    private static async Task<ApplicationRole> SeedRoleAsync(
        RoleManager<ApplicationRole> roleManager,
        Guid tenantId,
        string name,
        params string[] permissions)
    {
        var role = new ApplicationRole { Name = name, TenantId = tenantId };
        var result = await roleManager.CreateAsync(role);
        Assert.True(result.Succeeded);

        foreach (var permission in permissions)
        {
            await roleManager.AddClaimAsync(role, new Claim("Permission", permission));
        }

        return role;
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
        Assert.True(result.Succeeded);
        return user;
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyCurrentTenantUsers()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var harnessA = new TestHarness(tenantA);
        using var harnessB = new TestHarness(tenantB);

        await SeedUserAsync(harnessA.UserManager, tenantA, "alice@acme.demo");
        await SeedUserAsync(harnessA.UserManager, tenantA, "bob@acme.demo");
        await SeedUserAsync(harnessB.UserManager, tenantB, "carol@beta.demo");

        var results = await harnessA.Service.GetAllAsync();

        Assert.Equal(2, results.Count);
        Assert.All(results, u => Assert.Contains("@acme.demo", u.Email));
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearchTerm()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);

        await SeedUserAsync(harness.UserManager, tenantId, "alice@acme.demo");
        await SeedUserAsync(harness.UserManager, tenantId, "bob@acme.demo");

        var results = await harness.Service.GetAllAsync("alice");

        Assert.Single(results);
        Assert.Equal("alice@acme.demo", results[0].Email);
    }

    [Fact]
    public async Task CreateUserAsync_AssignsRoleTenantClaimAndPermissionClaims()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);
        await SeedRoleAsync(harness.RoleManager, tenantId, "Manager", Permissions.QuotesView, Permissions.JobsView);

        var (ok, errors) = await harness.Service.CreateUserAsync("newuser@acme.demo", "SecurePass1!", "Manager");

        Assert.True(ok, string.Join("; ", errors));
        var users = await harness.Service.GetAllAsync();
        Assert.Contains(users, u => u.Email == "newuser@acme.demo");

        var created = await harness.UserManager.FindByEmailAsync("newuser@acme.demo");
        Assert.NotNull(created);
        Assert.Equal(tenantId, created!.TenantId);

        var claims = await harness.UserManager.GetClaimsAsync(created);
        Assert.Contains(claims, c => c.Type == "TenantId" && c.Value == tenantId.ToString());
        Assert.Contains(claims, c => c.Type == "Permission" && c.Value == Permissions.QuotesView);
        Assert.Contains(claims, c => c.Type == "Permission" && c.Value == Permissions.JobsView);

        var roles = await harness.Service.GetUserRolesAsync(created.Id);
        Assert.Single(roles);
        Assert.Equal("Manager", roles[0]);
    }

    [Fact]
    public async Task CreateUserAsync_FailsWhenRoleDoesNotExist()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);

        var (ok, errors) = await harness.Service.CreateUserAsync("orphan@acme.demo", "SecurePass1!", "NonExistent");

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ChangeUserRoleAsync_ReplacesRoleAndSyncsPermissionClaims()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new TestHarness(tenantId);
        await SeedRoleAsync(harness.RoleManager, tenantId, "Viewer", Permissions.QuotesView);
        await SeedRoleAsync(harness.RoleManager, tenantId, "Manager", Permissions.QuotesManage, Permissions.JobsManage);

        var (createOk, createErrors) = await harness.Service.CreateUserAsync("rolechange@acme.demo", "SecurePass1!", "Viewer");
        Assert.True(createOk, string.Join("; ", createErrors));
        var user = await harness.UserManager.FindByEmailAsync("rolechange@acme.demo");
        Assert.NotNull(user);

        var (ok, errors) = await harness.Service.ChangeUserRoleAsync(user!.Id, "Manager");
        Assert.True(ok, string.Join("; ", errors));

        var roles = await harness.Service.GetUserRolesAsync(user.Id);
        Assert.Single(roles);
        Assert.Equal("Manager", roles[0]);

        var claims = await harness.UserManager.GetClaimsAsync(user);
        Assert.DoesNotContain(claims, c => c.Type == "Permission" && c.Value == Permissions.QuotesView);
        Assert.Contains(claims, c => c.Type == "Permission" && c.Value == Permissions.QuotesManage);
        Assert.Contains(claims, c => c.Type == "Permission" && c.Value == Permissions.JobsManage);
    }

    [Fact]
    public async Task GetAvailableRolesAsync_ReturnsOnlyCurrentTenantRoles()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var harnessA = new TestHarness(tenantA);
        using var harnessB = new TestHarness(tenantB);

        await SeedRoleAsync(harnessA.RoleManager, tenantA, "Admin");
        await SeedRoleAsync(harnessA.RoleManager, tenantA, "Manager");
        await SeedRoleAsync(harnessB.RoleManager, tenantB, "BetaAdmin");

        var roles = await harnessA.Service.GetAvailableRolesAsync();

        Assert.Equal(2, roles.Count);
        Assert.Contains("Admin", roles);
        Assert.Contains("Manager", roles);
        Assert.DoesNotContain("BetaAdmin", roles);
    }
}