namespace METERP.Application.Models;

public sealed class DivisionScorecardRow
{
    public Guid DivisionId { get; init; }

    public string DivisionCode { get; init; } = string.Empty;

    public string DivisionName { get; init; } = string.Empty;

    public int ActiveJobs { get; init; }

    public decimal AvgProgressPercent { get; init; }

    public int ReadyToInvoiceCount { get; init; }

    public decimal ReadyToInvoiceValue { get; init; }

    public decimal InvoicedRevenue { get; init; }
}