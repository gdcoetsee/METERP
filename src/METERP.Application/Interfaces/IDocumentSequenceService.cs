namespace METERP.Application.Interfaces;

/// <summary>
/// Tenant-scoped sequential document numbers (e.g. Q-2026-00042).
/// </summary>
public interface IDocumentSequenceService
{
    Task<string> GetNextNumberAsync(string documentType, string prefix, CancellationToken ct = default);
}