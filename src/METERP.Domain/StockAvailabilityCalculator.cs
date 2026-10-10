namespace METERP.Domain;

/// <summary>
/// Pure stock availability math (on hand minus reserved).
/// </summary>
public static class StockAvailabilityCalculator
{
    public static decimal GetAvailableQuantity(decimal quantityOnHand, decimal quantityReserved) =>
        Math.Max(0m, quantityOnHand - quantityReserved);

    public static decimal CalculateReservation(decimal requested, decimal available) =>
        Math.Min(requested, Math.Max(0m, available));

    /// <summary>
    /// Units still needed to reach the reorder level. Zero when on hand is at or above reorder.
    /// </summary>
    public static decimal CalculateReorderShortfall(decimal quantityOnHand, decimal reorderLevel) =>
        Math.Max(0m, reorderLevel - quantityOnHand);
}