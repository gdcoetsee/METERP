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
        if (quantity > 1000m)
            throw new InvalidOperationException("PPE issue quantity cannot exceed 1000 in a single transaction.");

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
            var job = await _dbContext.Set<Job>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.Id == jid, ct);
            if (job == null || job.IsDeleted)
                throw new InvalidOperationException("Job not found or deleted.");
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

        var issueNotes = string.IsNullOrWhiteSpace(notes)
            ? $"Issued to {employee.FirstName} {employee.LastName}"
            : notes.Trim();
        if (issueNotes.Length > 500)
            throw new InvalidOperationException("PPE issue notes cannot exceed 500 characters.");

        var issue = new EmployeePpeIssue
        {
            EmployeeId = employeeId,
            RequestedByUserId = issuedByUserId,
            JobId = jobId,
            InventoryItemId = inventoryItemId,
            Quantity = quantity,
            IssuedAt = DateTime.UtcNow,
            Notes = issueNotes
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

        foreach (var line in requisition.Lines.Where(l => !l.IsDeleted && l.QuantityIssued > 0 && !l.IsNonCatalog && l.InventoryItemId.HasValue))
        {
            _dbContext.Set<EmployeePpeIssue>().Add(new EmployeePpeIssue
            {
                EmployeeId = employeeId,
                RequestedByUserId = requisition.RequestedByUserId,
                JobId = requisition.JobId == Guid.Empty ? null : requisition.JobId,
                InventoryItemId = line.InventoryItemId!.Value,
                StockRequisitionId = requisition.Id,
                Quantity = line.QuantityIssued,
                IssuedAt = DateTime.UtcNow,
                Notes = $"PPE from {requisition.RequisitionNumber}"
            });
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> ReturnFromEmployeeAsync(
        Guid issueId,
        decimal quantity,
        Guid returnedByUserId,
        string? notes = null,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            throw new InvalidOperationException("Return quantity must be positive.");
        if (quantity > 1000m)
            throw new InvalidOperationException("PPE return quantity cannot exceed 1000 in a single transaction.");

        // IgnoreQueryFilters: soft-deleted inventory would hide the issue via required Include filters.
        var issue = await _dbContext.Set<EmployeePpeIssue>()
            .IgnoreQueryFilters()
            .Include(i => i.InventoryItem)
            .Include(i => i.Employee)
            .FirstOrDefaultAsync(i => i.Id == issueId && !i.IsDeleted, ct);
        if (issue == null)
            return false;

        var outstanding = issue.QuantityOutstanding;
        if (outstanding <= 0)
            throw new InvalidOperationException("This PPE issue is already fully returned.");

        if (quantity > outstanding)
            throw new InvalidOperationException(
                $"Cannot return {quantity:N2} — only {outstanding:N2} outstanding on this issue.");

        var item = issue.InventoryItem
            ?? await _dbContext.Set<InventoryItem>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.Id == issue.InventoryItemId, ct);
        if (item == null || item.IsDeleted)
            throw new InvalidOperationException(
                "Cannot return PPE — inventory item is missing or deleted.");

        await _inventoryService.RecordStockTransactionAsync(
            issue.InventoryItemId,
            quantity,
            StockTransactionType.Return,
            $"PPE-RET-{DateTime.UtcNow:yyyyMMddHHmmss}",
            issue.JobId,
            $"PPE return from {(issue.Employee != null ? $"{issue.Employee.FirstName} {issue.Employee.LastName}" : "employee")}",
            ct);

        issue.QuantityReturned += quantity;
        issue.ReturnedAt = DateTime.UtcNow;
        issue.ReturnedByUserId = returnedByUserId;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            var note = notes.Trim();
            if (note.Length > 500)
                throw new InvalidOperationException("PPE return notes cannot exceed 500 characters.");
            issue.Notes = string.IsNullOrWhiteSpace(issue.Notes)
                ? $"Return: {note}"
                : $"{issue.Notes} | Return: {note}";
            if (issue.Notes.Length > 1000)
                issue.Notes = issue.Notes[..1000];
        }

        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            var sku = issue.InventoryItem?.Sku ?? issue.InventoryItemId.ToString("N")[..8];
            await _audit.LogAsync(
                "RETURN",
                "EmployeePpeIssue",
                sku,
                $"Qty {quantity:N2} returned (outstanding {issue.QuantityOutstanding:N2})",
                ct);
        }

        return true;
    }
}
