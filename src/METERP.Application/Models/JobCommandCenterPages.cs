namespace METERP.Application.Models;

/// <summary>
/// One page of job cost lines. Totals live on <see cref="JobCommandCenterSummary"/>, not on this page.
/// </summary>
public sealed class JobCostPage
{
    public IReadOnlyList<JobCostLine> Items { get; init; } = Array.Empty<JobCostLine>();

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public int TotalCount { get; init; }

    public int PageCount =>
        TotalCount <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize));
}

public sealed class JobCostLine
{
    public Guid Id { get; init; }

    public DateTime CostDate { get; init; }

    public string CostType { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal Amount { get; init; }
}

public sealed class JobLaborPage
{
    public IReadOnlyList<JobLaborLine> Items { get; init; } = Array.Empty<JobLaborLine>();

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public int TotalCount { get; init; }

    public int PageCount =>
        TotalCount <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize));
}

public sealed class JobLaborLine
{
    public Guid Id { get; init; }

    public DateTime WorkDate { get; init; }

    public string Technician { get; init; } = string.Empty;

    public decimal Hours { get; init; }

    public decimal HourlyRate { get; init; }

    public decimal TotalCost { get; init; }

    public string? Description { get; init; }
}

public sealed class JobInvoicePage
{
    public IReadOnlyList<JobInvoiceSummary> Items { get; init; } = Array.Empty<JobInvoiceSummary>();

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;

    public int TotalCount { get; init; }

    public int PageCount =>
        TotalCount <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize));
}
