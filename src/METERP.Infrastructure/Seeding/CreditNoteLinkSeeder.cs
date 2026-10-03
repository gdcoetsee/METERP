using Microsoft.EntityFrameworkCore;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Idempotent credit-note repair. Does not require <c>METERP_ACCESS_IMPORT</c> and does not delete rows.
/// Normalizes legacy negative totals onto the positive-total convention, settles Access credits whose notes
/// say Sold / Completed AIP, and sets <see cref="Invoice.CreditNoteForInvoiceId"/> when exactly one parent matches.
/// </summary>
public static class CreditNoteLinkSeeder
{
    public readonly record struct Result(int Examined, int Linked, int Normalized, int Settled)
    {
        public string Summary =>
            $"examined={Examined} linked={Linked} normalized={Normalized} settled={Settled}";
    }

    public static async Task<Result> RunAsync(AppDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return new Result(0, 0, 0, 0);

        var credits = await db.Set<Invoice>()
            .IgnoreQueryFilters()
            .Include(i => i.Lines)
            .Where(i => i.TenantId == tenantId && !i.IsDeleted && i.DocumentType == InvoiceDocumentType.CreditNote)
            .ToListAsync(ct);

        if (credits.Count == 0)
            return new Result(0, 0, 0, 0);

        var parents = await db.Set<Invoice>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId
                && !i.IsDeleted
                && i.DocumentType != InvoiceDocumentType.CreditNote
                && i.DocumentType != InvoiceDocumentType.Proforma)
            .Select(i => new InvoiceCreditLinker.ParentSnap(
                i.Id,
                i.InvoiceNumber,
                i.CustomerId,
                i.Notes,
                i.Total,
                i.JobId))
            .ToListAsync(ct);

        var linked = 0;
        var normalized = 0;
        var settled = 0;
        foreach (var credit in credits)
        {
            if (InvoiceCreditConvention.NormalizeStoredPositive(credit))
                normalized++;

            if (InvoiceCreditConvention.SettleImportedCredit(credit))
                settled++;

            if (credit.CreditNoteForInvoiceId != null)
                continue;

            var match = InvoiceCreditLinker.MatchParent(
                new InvoiceCreditLinker.CreditSnap(credit.Id, credit.CustomerId, credit.Notes, credit.Total, credit.JobId),
                parents);
            if (match is not Guid parentId)
                continue;

            credit.CreditNoteForInvoiceId = parentId;
            linked++;
        }

        if (linked + normalized + settled > 0)
            await db.SaveChangesAsync(ct);

        return new Result(credits.Count, linked, normalized, settled);
    }
}
