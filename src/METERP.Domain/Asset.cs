namespace METERP.Domain;

/// <summary>
/// Physical asset (e.g. Transformer, Switchgear, Motor, etc.) owned by a customer or the company.
/// Key for electrical contracting companies.
/// </summary>
public class Asset : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string AssetNumber { get; set; } = string.Empty; // e.g. TRF-2026-0042

    public string Name { get; set; } = string.Empty; // "Main 11kV/400V Transformer"

    public string? SerialNumber { get; set; }

    public string AssetType { get; set; } = "Transformer"; // Transformer, Switchgear, Motor, Panel, Other

    public string? Location { get; set; } // Site / substation name

    public decimal? RatedKVA { get; set; } // For transformers

    public string? Voltage { get; set; } // e.g. "11kV / 400V"

    public DateTime? CommissionedDate { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Operational;

    public string? Notes { get; set; }

    // Future: maintenance history could be separate entity
}
