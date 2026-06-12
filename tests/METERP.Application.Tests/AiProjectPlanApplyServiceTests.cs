using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AiProjectPlanApplyServiceTests
{
    private readonly Mock<IQuoteService> _quoteService = new();
    private readonly Mock<IJobService> _jobService = new();
    private readonly Mock<ITenantProvider> _tenantProvider = new();
    private readonly Mock<ITenantService> _tenantService = new();

    private AiProjectPlanApplyService CreateService() =>
        new(_quoteService.Object, _jobService.Object, _tenantProvider.Object, _tenantService.Object);

    private void SetupAiEnabledTenant(Guid tenantId)
    {
        _tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        _tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "ai,usage-tracking" });
    }

    [Fact]
    public async Task CreateProjectPlanFromAiTextAsync_CreatesQuoteAndJob()
    {
        var tenantId = Guid.NewGuid();
        SetupAiEnabledTenant(tenantId);
        var customerId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        _quoteService.Setup(s => s.CreateAsync(It.IsAny<Quote>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteId);
        _quoteService.Setup(s => s.GetByIdAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Quote { Id = quoteId, CustomerId = customerId, Notes = "AI Project Plan - Quote: scope" });

        _jobService.Setup(s => s.CreateAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);
        _jobService.Setup(s => s.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Job { Id = jobId, CustomerId = customerId, Title = "AI Project Plan - Job" });

        var result = await CreateService().CreateProjectPlanFromAiTextAsync("Full scope with travel", customerId);

        Assert.Equal(quoteId, result.Quote.Id);
        Assert.Equal(jobId, result.Job.Id);
        _quoteService.Verify(s => s.CreateAsync(It.Is<Quote>(q =>
            q.CustomerId == customerId && (q.Notes ?? "").Contains("AI Project Plan")), It.IsAny<CancellationToken>()), Times.Once);
        _jobService.Verify(s => s.CreateAsync(It.Is<Job>(j =>
            j.CustomerId == customerId && j.Title == "AI Project Plan - Job"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateProjectPlanFromAiTextAsync_Throws_When_AiFeatureDisabled()
    {
        var tenantId = Guid.NewGuid();
        _tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        _tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "usage-tracking" });

        await Assert.ThrowsAsync<AiFeatureDisabledException>(() =>
            CreateService().CreateProjectPlanFromAiTextAsync("scope", Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateProjectPlanFromAiTextAsync_Throws_WhenCustomerMissing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().CreateProjectPlanFromAiTextAsync("scope", Guid.Empty));
    }

    [Fact]
    public async Task CreateProjectPlanFromAiTextAsync_Throws_WhenTextEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().CreateProjectPlanFromAiTextAsync("  ", Guid.NewGuid()));
    }
}