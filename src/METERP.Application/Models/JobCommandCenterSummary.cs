using METERP.Domain;

namespace METERP.Application.Models;

public sealed class JobCommandCenterSummary
{
    public Guid JobId { get; init; }

    public decimal MaterialCost { get; init; }

    public decimal TravelCost { get; init; }

    public decimal LaborCost { get; init; }

    public decimal OtherCost { get; init; }

    public decimal MarginPercent { get; init; }

    public bool IsReadyToInvoice { get; init; }

    public int ProgressPercent { get; init; }

    public IReadOnlyList<JobRequisitionSummary> Requisitions { get; init; } = Array.Empty<JobRequisitionSummary>();
}

public sealed class JobRequisitionSummary
{
    public string RequisitionNumber { get; init; } = string.Empty;

    public RequisitionStatus Status { get; init; }

    public string? PurchaseOrderNumber { get; init; }

    public string? GrvNumber { get; init; }

    public int LineCount { get; init; }
}