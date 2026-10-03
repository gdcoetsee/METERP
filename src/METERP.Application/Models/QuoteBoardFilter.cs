namespace METERP.Application.Models;

/// <summary>
/// Quotes register view. Live hides expired history so a large import is not the landing list.
/// </summary>
public enum QuoteBoardFilter
{
    All = 0,
    Live = 1,
    Draft = 2,
    Accepted = 3,
    Expired = 4
}
