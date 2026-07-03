using METERP.Domain;

namespace METERP.Application.Services;

public interface IFieldReportService
{
    Task<IReadOnlyList<FieldReport>> GetPendingAsync(CancellationToken ct = default);

    Task<IReadOnlyList<FieldReport>> GetByJobIdAsync(Guid jobId, CancellationToken ct = default);

    Task<Guid> SubmitAsync(FieldReport report, CancellationToken ct = default);

    Task<bool> ApproveAsync(Guid reportId, Guid approverUserId, CancellationToken ct = default);

    Task<bool> RejectAsync(Guid reportId, Guid approverUserId, string reason, CancellationToken ct = default);
}