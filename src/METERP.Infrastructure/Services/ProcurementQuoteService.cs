using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class ProcurementQuoteService : IProcurementQuoteService
{
    private readonly AppDbContext _dbContext;
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly IAuditService? _audit;

    public ProcurementQuoteService(
        AppDbContext dbContext,
        IPurchaseOrderService purchaseOrders,
        IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _purchaseOrders = purchaseOrders;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ProcurementSupplierQuote>> GetForRequisitionAsync(
        Guid requisitionId,
        CancellationToken ct = default)
    {
        return await _dbContext.Set<ProcurementSupplierQuote>()
            .AsNoTracking()
            .Include(q => q.Supplier)
            .Where(q => q.StockRequisitionId == requisitionId)
            .OrderBy(q => q.QuotedTotal)
            .ToListAsync(ct);
    }

    public async Task<Guid> AddQuoteAsync(
        Guid requisitionId,
        Guid supplierId,
        decimal quotedTotal,
        string? notes = null,
        CancellationToken ct = default)
    {
        if (quotedTotal < 0)
            throw new InvalidOperationException("Quoted total cannot be negative.");

        var req = await _dbContext.Set<StockRequisition>().FirstOrDefaultAsync(r => r.Id == requisitionId, ct)
            ?? throw new InvalidOperationException("Requisition not found.");

        if (req.Status is not (RequisitionStatus.AwaitingProcurement or RequisitionStatus.ProcurementOrdered))
            throw new InvalidOperationException("Quotes can only be added for requisitions awaiting procurement.");

        var supplier = await _dbContext.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == supplierId, ct)
            ?? throw new InvalidOperationException("Supplier not found.");

        var quote = new ProcurementSupplierQuote
        {
            StockRequisitionId = requisitionId,
            SupplierId = supplierId,
            QuotedTotal = quotedTotal,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            QuotedAt = DateTime.UtcNow
        };

        _dbContext.Set<ProcurementSupplierQuote>().Add(quote);
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "RFQ_QUOTE",
                "ProcurementSupplierQuote",
                req.RequisitionNumber,
                $"{supplier.Name}: R {quotedTotal:N2}",
                ct);
        }

        return quote.Id;
    }

    public async Task<bool> SelectQuoteAsync(Guid quoteId, Guid selectedByUserId, CancellationToken ct = default)
    {
        var quote = await _dbContext.Set<ProcurementSupplierQuote>()
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote == null) return false;

        var siblings = await _dbContext.Set<ProcurementSupplierQuote>()
            .Where(q => q.StockRequisitionId == quote.StockRequisitionId)
            .ToListAsync(ct);

        foreach (var s in siblings)
        {
            s.IsSelected = s.Id == quoteId;
            if (s.Id == quoteId)
            {
                s.SelectedByUserId = selectedByUserId;
                s.SelectedAt = DateTime.UtcNow;
            }
            else
            {
                s.SelectedByUserId = null;
                s.SelectedAt = null;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            var req = await _dbContext.Set<StockRequisition>().AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == quote.StockRequisitionId, ct);
            await _audit.LogAsync(
                "RFQ_SELECT",
                "ProcurementSupplierQuote",
                req?.RequisitionNumber ?? quoteId.ToString("N")[..8],
                $"Selected supplier quote R {quote.QuotedTotal:N2}",
                ct);
        }

        return true;
    }

    public async Task<Guid> CreatePoFromSelectedQuoteAsync(Guid requisitionId, CancellationToken ct = default)
    {
        var selected = await _dbContext.Set<ProcurementSupplierQuote>()
            .FirstOrDefaultAsync(q => q.StockRequisitionId == requisitionId && q.IsSelected, ct)
            ?? throw new InvalidOperationException("Select a supplier quote before creating a PO.");

        return await _purchaseOrders.CreateFromRequisitionAsync(requisitionId, selected.SupplierId, ct);
    }
}
