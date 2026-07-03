using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class TenantNotificationServiceTests
{
    private sealed class Harness : IDisposable
    {
        public Guid TenantId { get; }
        public Guid UserId { get; }
        public AppDbContext Db { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public TenantNotificationService Service { get; }

        public Harness(string roleName)
        {
            TenantId = Guid.NewGuid();
            UserId = Guid.NewGuid();

            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(TenantId);

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(UserId);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"notif-{Guid.NewGuid():N}")
                .Options;

            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
            UserManager = CreateUserManager(Db);
            Service = new TenantNotificationService(Db, UserManager, currentUser.Object);

            SeedUserWithRoleAsync(roleName).GetAwaiter().GetResult();
        }

        public void Dispose() => Db.Dispose();

        private async Task SeedUserWithRoleAsync(string roleName)
        {
            var role = new ApplicationRole { Id = Guid.NewGuid(), Name = roleName, NormalizedName = roleName.ToUpperInvariant(), TenantId = TenantId };
            Db.Set<ApplicationRole>().Add(role);

            var user = new ApplicationUser
            {
                Id = UserId,
                UserName = "notif@test.com",
                NormalizedUserName = "NOTIF@TEST.COM",
                Email = "notif@test.com",
                NormalizedEmail = "NOTIF@TEST.COM",
                TenantId = TenantId
            };
            await UserManager.CreateAsync(user);
            await UserManager.AddToRoleAsync(user, roleName);
            await Db.SaveChangesAsync();
        }

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
    }

    [Fact]
    public async Task GetForCurrentUserAsync_FiltersByRole()
    {
        using var harness = new Harness("Executive");
        await using (harness.Db)
        {
            var tenantId = harness.TenantId;
            harness.Db.Set<TenantNotification>().AddRange(
                new TenantNotification { TenantId = tenantId, Title = "All", TargetRoles = "*", Message = "Everyone" },
                new TenantNotification { TenantId = tenantId, Title = "Exec only", TargetRoles = "Executive", Message = "Exec" },
                new TenantNotification { TenantId = tenantId, Title = "HR only", TargetRoles = "HrManager", Message = "HR" });
            await harness.Db.SaveChangesAsync();

            var visible = await harness.Service.GetForCurrentUserAsync();

            Assert.Equal(2, visible.Count);
            Assert.Contains(visible, n => n.Title == "All");
            Assert.Contains(visible, n => n.Title == "Exec only");
            Assert.DoesNotContain(visible, n => n.Title == "HR only");
        }
    }

    [Fact]
    public async Task GetUnreadCountAsync_CountsOnlyVisibleUnread()
    {
        using var harness = new Harness("StoresClerk");
        await using (harness.Db)
        {
            var tenantId = harness.TenantId;
            harness.Db.Set<TenantNotification>().AddRange(
                new TenantNotification { TenantId = tenantId, Title = "Unread", TargetRoles = "StoresClerk", IsRead = false },
                new TenantNotification { TenantId = tenantId, Title = "Read", TargetRoles = "StoresClerk", IsRead = true },
                new TenantNotification { TenantId = tenantId, Title = "Other role", TargetRoles = "Finance", IsRead = false });
            await harness.Db.SaveChangesAsync();

            var count = await harness.Service.GetUnreadCountAsync();

            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksOnlyVisibleNotifications()
    {
        using var harness = new Harness("Executive");
        await using (harness.Db)
        {
            var tenantId = harness.TenantId;
            var execUnread = new TenantNotification { TenantId = tenantId, Title = "Exec unread", TargetRoles = "Executive", IsRead = false };
            var hrUnread = new TenantNotification { TenantId = tenantId, Title = "HR unread", TargetRoles = "HrManager", IsRead = false };
            harness.Db.Set<TenantNotification>().AddRange(execUnread, hrUnread);
            await harness.Db.SaveChangesAsync();

            await harness.Service.MarkAllReadAsync();

            var execSaved = await harness.Db.Set<TenantNotification>().FirstAsync(n => n.Id == execUnread.Id);
            var hrSaved = await harness.Db.Set<TenantNotification>().FirstAsync(n => n.Id == hrUnread.Id);
            Assert.True(execSaved.IsRead);
            Assert.False(hrSaved.IsRead);
        }
    }
}