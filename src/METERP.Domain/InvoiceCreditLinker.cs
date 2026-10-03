namespace METERP.Domain;

/// <summary>
/// Matches an unlinked credit note to a parent invoice when the notes, TRFid, or job card identify exactly one.
/// Ambiguous matches stay unlinked.
/// </summary>
public static class InvoiceCreditLinker
{
    public readonly record struct CreditSnap(
        Guid Id,
        Guid CustomerId,
        string? Notes,
        decimal Total,
        Guid? JobId);

    public readonly record struct ParentSnap(
        Guid Id,
        string InvoiceNumber,
        Guid CustomerId,
        string? Notes,
        decimal Total,
        Guid? JobId);

    public static Guid? MatchParent(CreditSnap credit, IReadOnlyList<ParentSnap> parents)
    {
        var mentioned = parents.Where(p =>
            p.Id != credit.Id
            && p.CustomerId == credit.CustomerId
            && !string.IsNullOrWhiteSpace(p.InvoiceNumber)
            && ContainsToken(credit.Notes, p.InvoiceNumber)).ToList();
        if (mentioned.Count == 1)
            return mentioned[0].Id;
        if (mentioned.Count > 1)
            return null;

        var trf = InvoiceLegacyRef.TryReadTrfId(credit.Notes);
        if (!string.IsNullOrWhiteSpace(trf))
        {
            var hits = parents.Where(p =>
                p.Id != credit.Id
                && p.CustomerId == credit.CustomerId
                && AmountsMatch(credit.Total, p.Total)
                && string.Equals(InvoiceLegacyRef.TryReadTrfId(p.Notes), trf, StringComparison.OrdinalIgnoreCase)).ToList();
            var chosen = ChooseOne(credit, hits);
            if (chosen != null || hits.Count > 0)
                return chosen;
        }

        if (credit.JobId is Guid jobId && jobId != Guid.Empty)
        {
            var hits = parents.Where(p =>
                p.Id != credit.Id
                && p.CustomerId == credit.CustomerId
                && p.JobId == jobId
                && AmountsMatch(credit.Total, p.Total)).ToList();
            var chosen = ChooseOne(credit, hits);
            if (chosen != null)
                return chosen;
        }

        var cards = InvoiceLegacyRef.ReadJobCardNumbers(credit.Notes);
        if (cards.Count > 0)
        {
            var hits = parents.Where(p =>
                p.Id != credit.Id
                && p.CustomerId == credit.CustomerId
                && AmountsMatch(credit.Total, p.Total)
                && SharesJobCard(cards, p.Notes)).ToList();
            return ChooseOne(credit, hits);
        }

        return null;
    }

    private static Guid? ChooseOne(CreditSnap credit, List<ParentSnap> hits)
    {
        if (hits.Count == 0)
            return null;
        if (hits.Count == 1)
            return hits[0].Id;

        var cards = InvoiceLegacyRef.ReadJobCardNumbers(credit.Notes);
        if (cards.Count == 0)
            return null;

        var narrowed = hits.Where(p => SharesJobCard(cards, p.Notes)).ToList();
        return narrowed.Count == 1 ? narrowed[0].Id : null;
    }

    private static bool SharesJobCard(IReadOnlyList<string> cards, string? notes)
    {
        var other = InvoiceLegacyRef.ReadJobCardNumbers(notes);
        return other.Any(card => cards.Contains(card, StringComparer.OrdinalIgnoreCase));
    }

    private static bool AmountsMatch(decimal left, decimal right) =>
        Math.Abs(Math.Abs(left) - Math.Abs(right)) < 0.01m;

    internal static bool ContainsToken(string? notes, string token)
    {
        if (string.IsNullOrWhiteSpace(notes) || string.IsNullOrWhiteSpace(token))
            return false;

        var index = 0;
        while ((index = notes.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(notes[index - 1]);
            var afterIndex = index + token.Length;
            var after = afterIndex >= notes.Length || !char.IsLetterOrDigit(notes[afterIndex]);
            if (before && after)
                return true;
            index = afterIndex;
        }

        return false;
    }
}
