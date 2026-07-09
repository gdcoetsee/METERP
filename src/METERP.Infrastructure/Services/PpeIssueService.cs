using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class PpeIssueService : IPpeIssueService
{
    private readonly AppDbContext _dbContext;
    private readonly IInventoryService _inventoryService;
    private readonly IAuditService? _audit;

    public PpeIssueService(
        AppDbContext dbContext,
        IInventoryService inventoryService,
        IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EmployeePpeIssue>> GetHistoryAsync(
        Guid? employeeId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = _dbContext.Set<EmployeePpeIssue>()
            .AsNoTracking()
            .Include(p => p.InventoryItem)
            .Include(p => p.Job)
            .Include(p => p.Employee)
            .Include(p => p.StockRequisition)
            .AsQueryable();

        if (employeeId.HasValue && employeeId.Value != Guid.Empty)
            query = query.Where(p => p.EmployeeId == employeeId.Value);

        return await query
            .OrderByDescending(p => p.IssuedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> IssueToEmployeeAsync(
        Guid employeeId,
        Guid inventoryItemId,
        decimal quantity,
        Guid issuedByUserId,
        Guid? jobId = null,
        string? notes = null,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            throw new InvalidOperationException("Quantity must be positive.");

        var employee = await _dbContext.Set<Employee>().FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive, ct);
        if (employee == null)
            throw new InvalidOperationException("Employee not found or inactive.");

        var item = await _dbContext.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == inventoryItemId && i.IsActive, ct);
        if (item == null)
            throw new InvalidOperationException("Inventory item not found or inactive.");

        if (item.QuantityOnHand < quantity)
            throw new InvalidOperationException(
                $"Insufficient stock for {item.Name}. On hand: {item.QuantityOnHand:N2}, requested: {quantity:N2}.");

        if (jobId is { } jid && jid != Guid.Empty)
        {
            var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jid, ct);
            if (job == null)
                throw new InvalidOperationException("Job not found.");
            if (!job.IsOpenForOperations())
                throw JobClosedException.ForJob(job.JobNumber);
        }
        else
        {
            jobId = null;
        }

        var reference = $"PPE-{DateTime.UtcNow:yyyyMMddHHmmss}";
        await _inventoryService.RecordStockTransactionAsync(
            inventoryItemId,
            -quantity,
            StockTransactionType.Issue,
            reference,
            jobId,
            $"PPE issue to {employee.FirstName} {employee.LastName}",
            ct);

        var issue = new EmployeePpeIssue
        {
            EmployeeId = employeeId,
            RequestedByUserId = issuedByUserId,
            JobId = jobId,
            InventoryItemId = inventoryItemId,
            Quantity = quantity,
            IssuedAt = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes)
                ? $"Issued to {employee.FirstName} {employee.LastName}"
                : notes.Trim()
        };

        _dbContext.Set<EmployeePpeIssue>().Add(issue);
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "ISSUE",
                "EmployeePpeIssue",
                item.Sku,
                $"Qty {quantity:N2} to {employee.EmployeeNumber} {employee.FirstName} {employee.LastName}" +
                (jobId.HasValue ? " (job-linked)" : " (register)"),
                ct);
        }

        return issue.Id;
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
                JobId = requisition.JobId == Guid.Empty ? null : requisition.JobId,
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
