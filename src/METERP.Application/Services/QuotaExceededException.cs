using METERP.Domain;

namespace METERP.Application.Services;

public class QuotaExceededException : InvalidOperationException
{
    public QuotaType QuotaType { get; }
    public int Limit { get; }
    public int Used { get; }

    public QuotaExceededException(QuotaType quotaType, int limit, int used)
        : base($"Monthly {quotaType} quota exceeded ({used}/{limit}). Upgrade your subscription tier or contact your administrator.")
    {
        QuotaType = quotaType;
        Limit = limit;
        Used = used;
    }
}