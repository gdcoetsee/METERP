namespace METERP.Domain;

public enum StockTransactionType
{
    Receipt = 0,        // Stock in (purchase, return)
    Issue = 1,          // Stock out to job
    Adjustment = 2,     // Manual count adjustment (+ or -)
    Return = 3          // Return from job / customer
}
