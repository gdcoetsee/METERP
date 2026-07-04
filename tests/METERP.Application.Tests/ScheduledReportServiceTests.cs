using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class ScheduledReportServiceTests
{
    private static (AppDbContext Db, Mock<ITenantProvider> TenantProvider) CreateDb(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        return (db, tenantProvider);
    }

    [Fact]
    public async Task SendExecutiveSummaryEmailAsync_ReturnsFalse_WhenSmtpNotConfigured()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(false);

            var service = new ScheduledReportService(
                Mock.Of<IExecutiveDashboardService>(),
                Mock.Of<IAccountabilityReportService>(),
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions()),
                tenantProvider.Object,
                db);

            var sent = await service.SendExecutiveSummaryEmailAsync("exec@test.com");
            Assert.False(sent);
            email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task SendExecutiveSummaryEmailAsync_SendsWhenConfigured()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                BrandDisplayName = "Acme Electrical"
            });
            await db.SaveChangesAsync();

            var dashboard = new Mock<IExecutiveDashboardService>();
            dashboard.Setup(d => d.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutiveDashboardSummary { PendingApprovals = 2, ReadyToInvoiceJobs = 1, ReadyToInvoiceValue = 1000m });

            var accountability = new Mock<IAccountabilityReportService>();
            accountability.Setup(a => a.GetDivisionScorecardsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DivisionScorecardRow>());
            accountability.Setup(a => a.GetUserActivityAsync(30, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserActivityRow>());
            accountability.Setup(a => a.GetOverdueApprovalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OverdueApprovalRow>());

            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(true);
            email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = new ScheduledReportService(
                dashboard.Object,
                accountability.Object,
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "mailpit", FromName = "METERP" }),
                tenantProvider.Object,
                db);

            var sent = await service.SendExecutiveSummaryEmailAsync("exec@test.com");

            Assert.True(sent);
            email.Verify(e => e.SendEmailAsync("exec@test.com", It.Is<string>(s => s.Contains("Executive")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task SendExecutiveSummaryEmailAsync_IncludesBrandingAndOverdueSection()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                BrandDisplayName = "Acme Electrical"
            });
            await db.SaveChangesAsync();

            var dashboard = new Mock<IExecutiveDashboardService>();
            dashboard.Setup(d => d.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutiveDashboardSummary { PendingApprovals = 3, ReadyToInvoiceJobs = 2, ReadyToInvoiceValue = 2500m });

            var accountability = new Mock<IAccountabilityReportService>();
            accountability.Setup(a => a.GetDivisionScorecardsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DivisionScorecardRow>());
            accountability.Setup(a => a.GetUserActivityAsync(30, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserActivityRow>());
            accountability.Setup(a => a.GetOverdueApprovalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OverdueApprovalRow>
                {
                    new() { ItemType = "Quote", Reference = "Q-1001", HoursInQueue = 52, SlaHours = 24 }
                });

            string? capturedHtml = null;
            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(true);
            email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, CancellationToken>((_, _, html, _) => capturedHtml = html)
                .Returns(Task.CompletedTask);

            var service = new ScheduledReportService(
                dashboard.Object,
                accountability.Object,
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "mailpit", FromName = "METERP" }),
                tenantProvider.Object,
                db);

            var sent = await service.SendExecutiveSummaryEmailAsync("exec@test.com");

            Assert.True(sent);
            Assert.NotNull(capturedHtml);
            Assert.Contains("Acme Electrical", capturedHtml);
            Assert.Contains("Overdue approval queue", capturedHtml);
            Assert.Contains("Q-1001", capturedHtml);
            Assert.Contains("Pending approvals", capturedHtml);
        }
    }

    [Fact]
    public async Task SendExecutiveSummaryEmailAsync_ShowsNone_WhenNoOverdueApprovals()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme"
            });
            await db.SaveChangesAsync();

            var dashboard = new Mock<IExecutiveDashboardService>();
            dashboard.Setup(d => d.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutiveDashboardSummary());

            var accountability = new Mock<IAccountabilityReportService>();
            accountability.Setup(a => a.GetDivisionScorecardsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DivisionScorecardRow>());
            accountability.Setup(a => a.GetUserActivityAsync(30, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserActivityRow>());
            accountability.Setup(a => a.GetOverdueApprovalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OverdueApprovalRow>());

            string? capturedHtml = null;
            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(true);
            email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, CancellationToken>((_, _, html, _) => capturedHtml = html)
                .Returns(Task.CompletedTask);

            var service = new ScheduledReportService(
                dashboard.Object,
                accountability.Object,
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "mailpit" }),
                tenantProvider.Object,
                db);

            await service.SendExecutiveSummaryEmailAsync("exec@test.com");

            Assert.NotNull(capturedHtml);
            Assert.Contains("None — all queues within SLA.", capturedHtml);
        }
    }

    [Fact]
    public async Task SendScheduledExecutiveReportsAsync_SendsToTenantsWithNotificationEmail()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                IsActive = true,
                NotificationEmail = "ops@acme.demo"
            });
            await db.SaveChangesAsync();

            var dashboard = new Mock<IExecutiveDashboardService>();
            dashboard.Setup(d => d.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutiveDashboardSummary());

            var accountability = new Mock<IAccountabilityReportService>();
            accountability.Setup(a => a.GetDivisionScorecardsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DivisionScorecardRow>());
            accountability.Setup(a => a.GetUserActivityAsync(30, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserActivityRow>());
            accountability.Setup(a => a.GetOverdueApprovalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OverdueApprovalRow>());

            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(true);
            email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = new ScheduledReportService(
                dashboard.Object,
                accountability.Object,
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "mailpit", FromName = "METERP" }),
                tenantProvider.Object,
                db);

            var sent = await service.SendScheduledExecutiveReportsAsync();

            Assert.Equal(1, sent);
            email.Verify(e => e.SendEmailAsync("ops@acme.demo", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task SendExecutiveSummaryEmailAsync_ReturnsFalse_WhenRecipientMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(true);

            var service = new ScheduledReportService(
                Mock.Of<IExecutiveDashboardService>(),
                Mock.Of<IAccountabilityReportService>(),
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "mailpit" }),
                tenantProvider.Object,
                db);

            var sent = await service.SendExecutiveSummaryEmailAsync("   ");

            Assert.False(sent);
            email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task SendScheduledExecutiveReportsAsync_ReturnsZero_WhenSmtpNotConfigured()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                IsActive = true,
                NotificationEmail = "ops@acme.demo"
            });
            await db.SaveChangesAsync();

            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(false);

            var service = new ScheduledReportService(
                Mock.Of<IExecutiveDashboardService>(),
                Mock.Of<IAccountabilityReportService>(),
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions()),
                tenantProvider.Object,
                db);

            var sent = await service.SendScheduledExecutiveReportsAsync();

            Assert.Equal(0, sent);
            email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task SendScheduledExecutiveReportsAsync_SkipsInactiveTenantsAndTenantsWithoutNotificationEmail()
    {
        var activeTenantId = Guid.NewGuid();
        var inactiveTenantId = Guid.NewGuid();
        var missingEmailTenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(activeTenantId);
        using (db)
        {
            db.Tenants.AddRange(
                new Tenant
                {
                    Id = activeTenantId,
                    TenantId = activeTenantId,
                    Name = "Active",
                    Subdomain = "active",
                    IsActive = true,
                    NotificationEmail = "ops@active.demo"
                },
                new Tenant
                {
                    Id = inactiveTenantId,
                    TenantId = inactiveTenantId,
                    Name = "Inactive",
                    Subdomain = "inactive",
                    IsActive = false,
                    NotificationEmail = "ops@inactive.demo"
                },
                new Tenant
                {
                    Id = missingEmailTenantId,
                    TenantId = missingEmailTenantId,
                    Name = "No Email",
                    Subdomain = "noemail",
                    IsActive = true,
                    NotificationEmail = null
                });
            await db.SaveChangesAsync();

            var dashboard = new Mock<IExecutiveDashboardService>();
            dashboard.Setup(d => d.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutiveDashboardSummary());

            var accountability = new Mock<IAccountabilityReportService>();
            accountability.Setup(a => a.GetDivisionScorecardsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DivisionScorecardRow>());
            accountability.Setup(a => a.GetUserActivityAsync(30, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserActivityRow>());
            accountability.Setup(a => a.GetOverdueApprovalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OverdueApprovalRow>());

            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(true);
            email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = new ScheduledReportService(
                dashboard.Object,
                accountability.Object,
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "mailpit", FromName = "METERP" }),
                tenantProvider.Object,
                db);

            var sent = await service.SendScheduledExecutiveReportsAsync();

            Assert.Equal(1, sent);
            email.Verify(e => e.SendEmailAsync("ops@active.demo", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            email.Verify(e => e.SendEmailAsync(It.Is<string>(r => r != "ops@active.demo"), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task SendExecutiveSummaryEmailAsync_UsesCurrentUserEmail_WhenRecipientOmitted()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme"
            });
            await db.SaveChangesAsync();

            var userManager = CreateUserManager(db);
            await userManager.CreateAsync(new ApplicationUser
            {
                Id = userId,
                UserName = "exec@acme.demo",
                Email = "exec@acme.demo",
                EmailConfirmed = true,
                TenantId = tenantId
            });

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(userId);

            var dashboard = new Mock<IExecutiveDashboardService>();
            dashboard.Setup(d => d.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutiveDashboardSummary());

            var accountability = new Mock<IAccountabilityReportService>();
            accountability.Setup(a => a.GetDivisionScorecardsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DivisionScorecardRow>());
            accountability.Setup(a => a.GetUserActivityAsync(30, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserActivityRow>());
            accountability.Setup(a => a.GetOverdueApprovalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OverdueApprovalRow>());

            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(true);
            email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = new ScheduledReportService(
                dashboard.Object,
                accountability.Object,
                email.Object,
                currentUser.Object,
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "mailpit", FromName = "METERP" }),
                tenantProvider.Object,
                db,
                userManager);

            var sent = await service.SendExecutiveSummaryEmailAsync();

            Assert.True(sent);
            email.Verify(e => e.SendEmailAsync("exec@acme.demo", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task SendExecutiveSummaryEmailAsync_IncludesDivisionScorecardsAndActivity()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantProvider) = CreateDb(tenantId);
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme"
            });
            await db.SaveChangesAsync();

            var dashboard = new Mock<IExecutiveDashboardService>();
            dashboard.Setup(d => d.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExecutiveDashboardSummary { PendingApprovals = 1, ReadyToInvoiceJobs = 2, ReadyToInvoiceValue = 5000m });

            var accountability = new Mock<IAccountabilityReportService>();
            accountability.Setup(a => a.GetDivisionScorecardsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DivisionScorecardRow>
                {
                    new() { DivisionName = "Johannesburg Operations", ActiveJobs = 3, ReadyToInvoiceCount = 1, ReadyToInvoiceValue = 2500m }
                });
            accountability.Setup(a => a.GetUserActivityAsync(30, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserActivityRow>
                {
                    new() { UserEmail = "admin@acme.demo", TotalActions = 42, ApprovalActions = 5 }
                });
            accountability.Setup(a => a.GetOverdueApprovalsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<OverdueApprovalRow>());

            string? capturedHtml = null;
            var email = new Mock<IEmailSender>();
            email.Setup(e => e.IsConfigured).Returns(true);
            email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, string, CancellationToken>((_, _, html, _) => capturedHtml = html)
                .Returns(Task.CompletedTask);

            var service = new ScheduledReportService(
                dashboard.Object,
                accountability.Object,
                email.Object,
                Mock.Of<ICurrentUserService>(),
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "mailpit", FromName = "METERP" }),
                tenantProvider.Object,
                db);

            await service.SendExecutiveSummaryEmailAsync("exec@test.com");

            Assert.NotNull(capturedHtml);
            Assert.Contains("Division scorecards", capturedHtml);
            Assert.Contains("Johannesburg Operations", capturedHtml);
            Assert.Contains("User activity (30 days)", capturedHtml);
            Assert.Contains("admin@acme.demo", capturedHtml);
            Assert.Contains("42 actions", capturedHtml);
        }
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