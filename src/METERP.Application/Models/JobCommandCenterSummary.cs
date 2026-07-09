using METERP.Domain;

namespace METERP.Application.Models;

public sealed class JobCommandCenterSummary
{
    public Guid JobId { get; init; }

    public string JobNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public JobStatus Status { get; init; }

    public bool IsClosed { get; init; }

    public decimal QuotedTotal { get; init; }

    public decimal ActualTotal { get; init; }

    public decimal BilledToDate { get; init; }

    public decimal UnbilledResidual { get; init; }

    public decimal MaterialCost { get; init; }

    public decimal TravelCost { get; init; }

    public decimal LaborCost { get; init; }

    public decimal OtherCost { get; init; }

    public decimal MarginPercent { get; init; }

    public bool IsReadyToInvoice { get; init; }

    public int ProgressPercent { get; init; }

    public IReadOnlyList<JobRequisitionSummary> Requisitions { get; init; } = Array.Empty<JobRequisitionSummary>();

    public IReadOnlyList<JobInvoiceSummary> Invoices { get; init; } = Array.Empty<JobInvoiceSummary>();
}

public sealed class JobInvoiceSummary
{
    public Guid InvoiceId { get; init; }

    public string InvoiceNumber { get; init; } = string.Empty;

    public InvoiceDocumentType DocumentType { get; init; }

    public InvoiceStatus Status { get; init; }

    public decimal Total { get; init; }

    public DateTime InvoiceDate { get; init; }
}

public sealed class JobRequisitionSummary
{
    public string RequisitionNumber { get; init; } = string.Empty;

    public RequisitionStatus Status { get; init; }

    public string? PurchaseOrderNumber { get; init; }

    public string? GrvNumber { get; init; }

    public int LineCount { get; init; }
}