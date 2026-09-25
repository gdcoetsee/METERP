namespace METERP.Domain;

/// <summary>
/// Pure CRM pipeline helpers — default win probability by stage and weighted forecast.
/// </summary>
public static class OpportunityPipeline
{
    public static int DefaultProbability(OpportunityStage stage) => stage switch
    {
        OpportunityStage.Lead => 10,
        OpportunityStage.Qualified => 25,
        OpportunityStage.Proposal => 50,
        OpportunityStage.Negotiation => 75,
        OpportunityStage.ClosedWon => 100,
        OpportunityStage.ClosedLost => 0,
        _ => 10
    };

    public static decimal WeightedValue(decimal value, int probabilityPercent)
    {
        var p = Math.Clamp(probabilityPercent, 0, 100);
        return Math.Round(value * p / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Closed stages lock probability. Open stages inherit the new default when the user
    /// has not overridden the previous stage default.
    /// </summary>
    public static void AlignProbabilityWithStage(Opportunity opportunity, OpportunityStage previousStage)
    {
        var next = opportunity.Stage;
        if (next == OpportunityStage.ClosedWon)
        {
            opportunity.ProbabilityPercent = 100;
            return;
        }

        if (next == OpportunityStage.ClosedLost)
        {
            opportunity.ProbabilityPercent = 0;
            return;
        }

        var previousDefault = DefaultProbability(previousStage);
        if (opportunity.ProbabilityPercent == 0 || opportunity.ProbabilityPercent == previousDefault)
            opportunity.ProbabilityPercent = DefaultProbability(next);
    }
}
