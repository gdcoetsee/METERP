using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using OtpNet;
using Xunit;

namespace METERP.Application.Tests;

public class TwoFactorAuthServiceTests
{
    private sealed class Harness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public TwoFactorAuthService Service { get; }
        public ApplicationUser User { get; }

        public Harness()
        {
            TenantId = Guid.NewGuid();
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(TenantId);
            var currentUser = new Mock<ICurrentUserService>();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
            UserManager = CreateUserManager(Db);
            Service = new TwoFactorAuthService(UserManager, UrlEncoder.Default);

            User = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                Email = "2fa@test.demo",
                UserName = "2fa@test.demo",
                EmailConfirmed = true
            };
            UserManager.CreateAsync(User, "TestPass123!").GetAwaiter().GetResult();
        }

        public void Dispose() => Db.Dispose();

        private static UserManager<ApplicationUser> CreateUserManager(AppDbContext db)
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var store = new UserStore<ApplicationUser, ApplicationRole, AppDbContext, Guid>(db);
            var manager = new UserManager<ApplicationUser>(
                store,
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                new IUserValidator<ApplicationUser>[] { new UserValidator<ApplicationUser>() },
                new IPasswordValidator<ApplicationUser>[] { new PasswordValidator<ApplicationUser>() },
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services,
                new LoggerFactory().CreateLogger<UserManager<ApplicationUser>>());

            manager.RegisterTokenProvider(
                TokenOptions.DefaultAuthenticatorProvider,
                new AuthenticatorTokenProvider<ApplicationUser>());

            return manager;
        }
    }

    private static string ComputeTotp(string rawBase32Key) =>
        new Totp(Base32Encoding.ToBytes(rawBase32Key)).ComputeTotp();

    [Fact]
    public async Task IsEnabledAsync_ReturnsFalse_BeforeSetup()
    {
        using var h = new Harness();
        Assert.False(await h.Service.IsEnabledAsync(h.User.Id));
    }

    [Fact]
    public async Task BeginSetup_ReturnsFormattedSharedKey()
    {
        using var h = new Harness();
        var info = await h.Service.BeginSetupAsync(h.User.Id);

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info!.SharedKey));
        Assert.Contains("otpauth://totp/", info.AuthenticatorUri);
    }

    [Fact]
    public async Task ConfirmSetup_EnablesTwoFactor_WhenCodeValid()
    {
        using var h = new Harness();
        var info = await h.Service.BeginSetupAsync(h.User.Id);
        Assert.NotNull(info);

        var rawKey = await h.UserManager.GetAuthenticatorKeyAsync(h.User);
        Assert.False(string.IsNullOrWhiteSpace(rawKey));

        var code = ComputeTotp(rawKey!);
        var (ok, errors) = await h.Service.ConfirmSetupAsync(h.User.Id, code);

        Assert.True(ok, string.Join("; ", errors));
        Assert.True(await h.Service.IsEnabledAsync(h.User.Id));
    }

    [Fact]
    public async Task VerifyCode_ReturnsTrue_WhenTwoFactorEnabled()
    {
        using var h = new Harness();
        await h.Service.BeginSetupAsync(h.User.Id);
        var rawKey = (await h.UserManager.GetAuthenticatorKeyAsync(h.User))!;
        await h.Service.ConfirmSetupAsync(h.User.Id, ComputeTotp(rawKey));

        Assert.True(await h.Service.VerifyCodeAsync(h.User.Id, ComputeTotp(rawKey)));
        Assert.False(await h.Service.VerifyCodeAsync(h.User.Id, "000000"));
    }

    [Fact]
    public async Task DisableAsync_TurnsOffTwoFactor()
    {
        using var h = new Harness();
        await h.Service.BeginSetupAsync(h.User.Id);
        var rawKey = (await h.UserManager.GetAuthenticatorKeyAsync(h.User))!;
        await h.Service.ConfirmSetupAsync(h.User.Id, ComputeTotp(rawKey));

        var (ok, _) = await h.Service.DisableAsync(h.User.Id);

        Assert.True(ok);
        Assert.False(await h.Service.IsEnabledAsync(h.User.Id));
    }
}