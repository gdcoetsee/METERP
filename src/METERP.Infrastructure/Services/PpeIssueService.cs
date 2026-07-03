using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class PpeIssueService : IPpeIssueService
{
    private readonly AppDbContext _dbContext;

    public PpeIssueService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<EmployeePpeIssue>> GetHistoryAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        return await _dbContext.Set<EmployeePpeIssue>()
            .AsNoTracking()
            .Include(p => p.InventoryItem)
            .Include(p => p.Job)
            .Include(p => p.Employee)
            .Include(p => p.StockRequisition)
            .OrderByDescending(p => p.IssuedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task RecordFromRequisitionIssueAsync(StockRequisition requisition, CancellationToken ct = default)
    {
        if (!requisition.IsPpe) return;

        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == requisition.JobId, ct);
        Guid? employeeId = job?.AssignedEmployeeId;

        foreach (var line in requisition.Lines.Where(l => !l.IsDeleted && l.QuantityIssued > 0))
        {
            _dbContext.Set<EmployeePpeIssue>().Add(new EmployeePpeIssue
            {
                EmployeeId = employeeId,
                RequestedByUserId = requisition.RequestedByUserId,
                JobId = requisition.JobId,
                InventoryItemId = line.InventoryItemId,
                StockRequisitionId = requisition.Id,
                Quantity = line.QuantityIssued,
                IssuedAt = DateTime.UtcNow,
                Notes = $"PPE from {requisition.RequisitionNumber}"
            });
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}