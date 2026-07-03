namespace METERP.Application.Models;

public sealed class ExecutiveDashboardSummary
{
    public int PendingApprovals { get; init; }

    public int PendingQuotes { get; init; }

    public int PendingRequisitions { get; init; }

    public int PendingLeave { get; init; }

    public int PendingFieldReports { get; init; }

    public int UnreadNotifications { get; init; }

    public int ReadyToInvoiceJobs { get; init; }

    public decimal ReadyToInvoiceValue { get; init; }

    public decimal AgedDebtorsTotal { get; init; }

    public int LowStockItems { get; init; }
}