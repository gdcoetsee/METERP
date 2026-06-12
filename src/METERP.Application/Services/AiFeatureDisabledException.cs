namespace METERP.Application.Services;

/// <summary>
/// Thrown when AI Copilot apply or suggestions are blocked by tenant feature flags.
/// </summary>
public class AiFeatureDisabledException : Exception
{
    public AiFeatureDisabledException()
        : base("AI Copilot is not enabled for this tenant. Upgrade your plan or enable the 'ai' feature flag.")
    {
    }
}