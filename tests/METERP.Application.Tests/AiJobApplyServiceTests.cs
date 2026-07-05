using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AiJobApplyServiceTests
{
    private readonly Mock<IJobService> _jobService = new();
    private readonly Mock<ITenantProvider> _tenantProvider = new();
    private readonly Mock<ITenantService> _tenantService = new();

    private AiJobApplyService CreateService() =>
        new(_jobService.Object, _tenantProvider.Object, _tenantService.Object);

    [Fact]
    public async Task CreateJobFromAiTextAsync_CreatesJob_WithExplicitTravelCost()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        _tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        _tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "ai,usage-tracking" });
        _jobService.Setup(s => s.CreateAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);
        _jobService.Setup(s => s.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Job { Id = jobId, JobNumber = "J-2026-TEST", CustomerId = customerId });

        var result = await CreateService().CreateJobFromAiTextAsync("Install panel with travel to site", customerId);

        Assert.Equal(jobId, result.Id);
        _jobService.Verify(s => s.AddCostAsync(It.Is<JobCost>(c =>
            c.JobId == jobId &&
            c.CostType == "Travel" &&
            c.Amount == 650m &&
            c.Description.Contains("Travel", StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateJobFromAiTextAsync_PersistsAiNotes_OnCreatedJob()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        const string aiText = "Replace 11kV breaker with travel to remote site";

        _tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        _tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "ai,usage-tracking" });
        _jobService.Setup(s => s.CreateAsync(It.Is<Job>(j =>
            j.Title == "AI Generated Job" &&
            (j.Notes ?? "").Contains(aiText)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);
        _jobService.Setup(s => s.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Job { Id = jobId, JobNumber = "J-2026-NOTES", Notes = "AI Generated: " + aiText });

        var result = await CreateService().CreateJobFromAiTextAsync(aiText, Guid.NewGuid());

        Assert.Contains(aiText, result.Notes ?? string.Empty);
        _jobService.Verify(s => s.AddCostAsync(It.IsAny<JobCost>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateJobFromAiTextAsync_CreatesJob_WhenCustomerIdNull()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        _tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        _tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "ai,usage-tracking" });
        _jobService.Setup(s => s.CreateAsync(It.Is<Job>(j => j.CustomerId == Guid.Empty), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobId);
        _jobService.Setup(s => s.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Job { Id = jobId, JobNumber = "J-2026-NO-CUST", CustomerId = Guid.Empty });

        var result = await CreateService().CreateJobFromAiTextAsync("Travel to site for breaker swap", null);

        Assert.Equal(jobId, result.Id);
        _jobService.Verify(s => s.AddCostAsync(It.Is<JobCost>(c => c.CostType == "Travel"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateJobFromAiTextAsync_Throws_When_AiFeatureDisabled()
    {
        var tenantId = Guid.NewGuid();
        _tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        _tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "usage-tracking" });

        await Assert.ThrowsAsync<AiFeatureDisabledException>(() =>
            CreateService().CreateJobFromAiTextAsync("scope", Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateJobFromAiTextAsync_Throws_When_TextEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().CreateJobFromAiTextAsync("  ", null));
    }
}