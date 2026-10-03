namespace METERP.Application.Models;

/// <summary>
/// Home cash table shows eight rows. Deposits come first, then ready-to-invoice,
/// then sign-off. When deposits and sign-off are both waiting and the deposit-first
/// window would hide every sign-off row, two slots stay open for sign-off.
/// </summary>
public static class CashDeskQueue
{
    public const int Window = 8;
    public const int ReservedSignOffSlots = 2;

    public static IReadOnlyList<ReadyToInvoiceJobRow> Mix(
        IReadOnlyList<ReadyToInvoiceJobRow>? deposits,
        IReadOnlyList<ReadyToInvoiceJobRow>? readyToInvoice,
        IReadOnlyList<ReadyToInvoiceJobRow>? awaitingSignOff,
        int take = Window,
        int reservedSignOff = ReservedSignOffSlots)
    {
        deposits ??= [];
        readyToInvoice ??= [];
        awaitingSignOff ??= [];
        if (take < 1)
            return [];

        var seen = new HashSet<Guid>(deposits.Count + readyToInvoice.Count);
        var earlier = new List<ReadyToInvoiceJobRow>(deposits.Count + readyToInvoice.Count);
        foreach (var row in deposits)
        {
            if (seen.Add(row.JobId))
                earlier.Add(row);
        }

        foreach (var row in readyToInvoice)
        {
            if (seen.Add(row.JobId))
                earlier.Add(row);
        }

        var signOff = new List<ReadyToInvoiceJobRow>();
        foreach (var row in awaitingSignOff)
        {
            if (!seen.Contains(row.JobId))
                signOff.Add(row);
        }

        var reserve = deposits.Count > 0 && signOff.Count > 0
            ? Math.Min(Math.Max(0, reservedSignOff), Math.Min(signOff.Count, take))
            : 0;

        if (reserve > 0 && earlier.Count > take - reserve)
        {
            var mixed = new List<ReadyToInvoiceJobRow>(take);
            mixed.AddRange(earlier.Take(take - reserve));
            mixed.AddRange(signOff.Take(reserve));
            return mixed;
        }

        return earlier.Concat(signOff).Take(take).ToList();
    }
}
