using METERP.Application.Services;

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

    public IReadOnlyList<ReadyToInvoiceJobRow> ReadyToInvoiceQueue { get; init; } = Array.Empty<ReadyToInvoiceJobRow>();

    public int DepositDueJobs { get; init; }

    public decimal DepositDueValue { get; init; }

    public IReadOnlyList<ReadyToInvoiceJobRow> DepositDueQueue { get; init; } = Array.Empty<ReadyToInvoiceJobRow>();

    public IReadOnlyList<ConvertibleDocumentRow> ConvertToJobQueue { get; init; } = Array.Empty<ConvertibleDocumentRow>();

    public int AwaitingSignOffJobs { get; init; }

    public decimal AwaitingSignOffValue { get; init; }

    public IReadOnlyList<ReadyToInvoiceJobRow> AwaitingSignOffQueue { get; init; } = Array.Empty<ReadyToInvoiceJobRow>();

    public int OverduePurchaseOrders { get; init; }

    public decimal OverduePurchaseOrderValue { get; init; }

    public IReadOnlyList<ConvertibleDocumentRow> OverduePurchaseOrderQueue { get; init; } = Array.Empty<ConvertibleDocumentRow>();

    public IReadOnlyList<ConvertibleDocumentRow> UnsentPurchaseOrderQueue { get; init; } = Array.Empty<ConvertibleDocumentRow>();

    public IReadOnlyList<ConvertibleDocumentRow> UnsentQuoteQueue { get; init; } = Array.Empty<ConvertibleDocumentRow>();

    public IReadOnlyList<ConvertibleDocumentRow> UnsentInvoiceQueue { get; init; } = Array.Empty<ConvertibleDocumentRow>();

    public IReadOnlyList<ConvertibleDocumentRow> UnconfirmedSalesOrderQueue { get; init; } = Array.Empty<ConvertibleDocumentRow>();

    public IReadOnlyList<PpeOutstandingRow> OutstandingPpeQueue { get; init; } = Array.Empty<PpeOutstandingRow>();

    public IReadOnlyList<CertificationExpiryRow> ExpiringCertificationQueue { get; init; } = Array.Empty<CertificationExpiryRow>();

    public IReadOnlyList<ApprovalQueueRow> ApprovalQueue { get; init; } = Array.Empty<ApprovalQueueRow>();

    public IReadOnlyList<AgedDebtorRow> OverdueInvoiceQueue { get; init; } = Array.Empty<AgedDebtorRow>();
}