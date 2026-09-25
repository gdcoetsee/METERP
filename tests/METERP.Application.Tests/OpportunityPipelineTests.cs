using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class OpportunityPipelineTests
{
    [Theory]
    [InlineData(OpportunityStage.Lead, 10)]
    [InlineData(OpportunityStage.Qualified, 25)]
    [InlineData(OpportunityStage.Proposal, 50)]
    [InlineData(OpportunityStage.Negotiation, 75)]
    [InlineData(OpportunityStage.ClosedWon, 100)]
    [InlineData(OpportunityStage.ClosedLost, 0)]
    public void DefaultProbability_MatchesStage(OpportunityStage stage, int expected)
    {
        Assert.Equal(expected, OpportunityPipeline.DefaultProbability(stage));
    }

    [Fact]
    public void WeightedValue_ScalesByProbability()
    {
        Assert.Equal(25000m, OpportunityPipeline.WeightedValue(100000m, 25));
        Assert.Equal(0m, OpportunityPipeline.WeightedValue(100000m, 0));
        Assert.Equal(100000m, OpportunityPipeline.WeightedValue(100000m, 100));
    }

    [Fact]
    public void AlignProbabilityWithStage_LocksClosedAndInheritsOpenDefault()
    {
        var opp = new Opportunity
        {
            Stage = OpportunityStage.Proposal,
            ProbabilityPercent = 10,
            Value = 80000m
        };

        OpportunityPipeline.AlignProbabilityWithStage(opp, OpportunityStage.Lead);
        Assert.Equal(50, opp.ProbabilityPercent);

        opp.Stage = OpportunityStage.ClosedWon;
        OpportunityPipeline.AlignProbabilityWithStage(opp, OpportunityStage.Proposal);
        Assert.Equal(100, opp.ProbabilityPercent);

        opp.Stage = OpportunityStage.ClosedLost;
        OpportunityPipeline.AlignProbabilityWithStage(opp, OpportunityStage.ClosedWon);
        Assert.Equal(0, opp.ProbabilityPercent);
    }

    [Fact]
    public void AlignProbabilityWithStage_PreservesManualOverride()
    {
        var opp = new Opportunity
        {
            Stage = OpportunityStage.Proposal,
            ProbabilityPercent = 40
        };

        OpportunityPipeline.AlignProbabilityWithStage(opp, OpportunityStage.Qualified);
        Assert.Equal(40, opp.ProbabilityPercent);
    }
}
