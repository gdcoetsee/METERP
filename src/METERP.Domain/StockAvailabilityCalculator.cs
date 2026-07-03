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
}