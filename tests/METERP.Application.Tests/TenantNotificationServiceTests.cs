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
    public async Task MarkReadAsync_MarksSingleNotification()
    {
        using var harness = new Harness("Executive");
        await using (harness.Db)
        {
            var notification = new TenantNotification
            {
                TenantId = harness.TenantId,
                Title = "Unread exec",
                Message = "Needs review",
                TargetRoles = "Executive",
                IsRead = false
            };
            harness.Db.Set<TenantNotification>().Add(notification);
            await harness.Db.SaveChangesAsync();

            await harness.Service.MarkReadAsync(notification.Id);

            var saved = await harness.Db.Set<TenantNotification>().FirstAsync(n => n.Id == notification.Id);
            Assert.True(saved.IsRead);
        }
    }

    [Fact]
    public async Task MarkReadAsync_NoOp_WhenNotificationMissing()
    {
        using var harness = new Harness("Executive");
        await using (harness.Db)
        {
            await harness.Service.MarkReadAsync(Guid.NewGuid());
        }
    }

    [Fact]
    public async Task CreateAsync_PersistsNotification()
    {
        using var harness = new Harness("Executive");
        await using (harness.Db)
        {
            await harness.Service.CreateAsync(new TenantNotification
            {
                TenantId = harness.TenantId,
                Title = "Low Stock",
                Message = "Transformer oil below reorder.",
                TargetRoles = "StoresClerk",
                Category = "inventory"
            });

            var saved = await harness.Db.Set<TenantNotification>().ToListAsync();
            Assert.Single(saved);
            Assert.Equal("Low Stock", saved[0].Title);
            Assert.Equal("inventory", saved[0].Category);
        }
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsZero_AfterMarkAllRead()
    {
        using var harness = new Harness("Executive");
        await using (harness.Db)
        {
            await harness.Service.CreateAsync(new TenantNotification
            {
                TenantId = harness.TenantId,
                Title = "Unread one",
                Message = "Needs attention",
                TargetRoles = "Executive",
                IsRead = false
            });
            await harness.Service.CreateAsync(new TenantNotification
            {
                TenantId = harness.TenantId,
                Title = "Unread two",
                Message = "Also needs attention",
                TargetRoles = "Executive",
                IsRead = false
            });

            Assert.Equal(2, await harness.Service.GetUnreadCountAsync());

            await harness.Service.MarkAllReadAsync();

            Assert.Equal(0, await harness.Service.GetUnreadCountAsync());
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

    [Fact]
    public async Task MarkAllReadAsync_DoesNotAffectOtherTenantUnread()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid betaNotificationId;

        await using (var seedB = CreateSharedContext(dbName, tenantB))
        {
            var notification = new TenantNotification
            {
                TenantId = tenantB,
                Title = "Beta stays unread",
                Message = "Other tenant",
                TargetRoles = "*",
                IsRead = false
            };
            seedB.Set<TenantNotification>().Add(notification);
            await seedB.SaveChangesAsync();
            betaNotificationId = notification.Id;
        }

        var (service, dbA) = CreateSharedService(dbName, tenantA, Guid.NewGuid(), "Admin");
        await using (dbA)
        {
            dbA.Set<TenantNotification>().Add(new TenantNotification
            {
                TenantId = tenantA,
                Title = "Acme unread",
                Message = "Should be marked read",
                TargetRoles = "*",
                IsRead = false
            });
            await dbA.SaveChangesAsync();

            await service.MarkAllReadAsync();
            Assert.Equal(0, await service.GetUnreadCountAsync());
        }

        await using var verifyB = CreateSharedContext(dbName, tenantB);
        var betaSaved = await verifyB.Set<TenantNotification>().FirstAsync(n => n.Id == betaNotificationId);
        Assert.False(betaSaved.IsRead);
    }

    [Fact]
    public async Task MarkReadAsync_NoOp_WhenNotificationBelongsToOtherTenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid betaNotificationId;

        await using (var seedB = CreateSharedContext(dbName, tenantB))
        {
            var notification = new TenantNotification
            {
                TenantId = tenantB,
                Title = "Beta unread",
                Message = "Should stay unread",
                TargetRoles = "*",
                IsRead = false
            };
            seedB.Set<TenantNotification>().Add(notification);
            await seedB.SaveChangesAsync();
            betaNotificationId = notification.Id;
        }

        var (service, dbA) = CreateSharedService(dbName, tenantA, Guid.NewGuid(), "Admin");
        await using (dbA)
        {
            await service.MarkReadAsync(betaNotificationId);

            await using var verifyB = CreateSharedContext(dbName, tenantB);
            var saved = await verifyB.Set<TenantNotification>().FirstAsync(n => n.Id == betaNotificationId);
            Assert.False(saved.IsRead);
        }
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsOnlyCurrentTenantUnread()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userIdA = Guid.NewGuid();

        await using (var seedB = CreateSharedContext(dbName, tenantB))
        {
            seedB.Set<TenantNotification>().Add(new TenantNotification
            {
                TenantId = tenantB,
                Title = "Beta unread",
                Message = "Should not count for Acme",
                TargetRoles = "*",
                IsRead = false
            });
            await seedB.SaveChangesAsync();
        }

        var (service, dbA) = CreateSharedService(dbName, tenantA, userIdA, "Admin");
        await using (dbA)
        {
            dbA.Set<TenantNotification>().AddRange(
                new TenantNotification
                {
                    TenantId = tenantA,
                    Title = "Acme unread",
                    Message = "Visible",
                    TargetRoles = "*",
                    IsRead = false
                },
                new TenantNotification
                {
                    TenantId = tenantA,
                    Title = "Acme read",
                    Message = "Already read",
                    TargetRoles = "*",
                    IsRead = true
                });
            await dbA.SaveChangesAsync();

            Assert.Equal(1, await service.GetUnreadCountAsync());
        }
    }

    [Fact]
    public async Task GetForCurrentUserAsync_ReturnsOnlyCurrentTenantNotifications()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userIdA = Guid.NewGuid();

        await using (var seedB = CreateSharedContext(dbName, tenantB))
        {
            seedB.Set<TenantNotification>().Add(new TenantNotification
            {
                TenantId = tenantB,
                Title = "Beta tenant alert",
                Message = "Should not leak",
                TargetRoles = "*"
            });
            await seedB.SaveChangesAsync();
        }

        var (service, dbA) = CreateSharedService(dbName, tenantA, userIdA, "Admin");
        await using (dbA)
        {
            dbA.Set<TenantNotification>().Add(new TenantNotification
            {
                TenantId = tenantA,
                Title = "Acme tenant alert",
                Message = "Visible to Acme",
                TargetRoles = "*"
            });
            await dbA.SaveChangesAsync();

            var visible = await service.GetForCurrentUserAsync();
            Assert.Single(visible);
            Assert.Equal("Acme tenant alert", visible[0].Title);
        }
    }

    private static AppDbContext CreateSharedContext(string dbName, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    private static (TenantNotificationService Service, AppDbContext Db) CreateSharedService(
        string dbName, Guid tenantId, Guid userId, string roleName)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(userId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var userManager = CreateUserManager(db);

        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant(),
            TenantId = tenantId
        };
        db.Set<ApplicationRole>().Add(role);

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"user-{tenantId:N}@test.com",
            NormalizedUserName = $"USER-{tenantId:N}@TEST.COM",
            Email = $"user-{tenantId:N}@test.com",
            NormalizedEmail = $"USER-{tenantId:N}@TEST.COM",
            TenantId = tenantId
        };
        userManager.CreateAsync(user).GetAwaiter().GetResult();
        userManager.AddToRoleAsync(user, roleName).GetAwaiter().GetResult();
        db.SaveChanges();

        return (new TenantNotificationService(db, userManager, currentUser.Object), db);
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