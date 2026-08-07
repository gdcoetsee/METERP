using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class RecurringJobService : IRecurringJobService
{
    private readonly AppDbContext _dbContext;
    private readonly IJobService _jobs;
    private readonly IAuditService? _audit;

    public RecurringJobService(AppDbContext dbContext, IJobService jobs, IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _jobs = jobs;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RecurringJobSchedule>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default)
    {
        var query = _dbContext.Set<RecurringJobSchedule>()
            .AsNoTracking()
            .Include(s => s.Customer)
            .AsQueryable();

        if (activeOnly)
            query = query.Where(s => s.IsActive);

        return await query.OrderBy(s => s.NextRunDate).ToListAsync(ct);
    }

    public async Task<RecurringJobSchedule?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<RecurringJobSchedule>()
            .AsNoTracking()
            .Include(s => s.Customer)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Guid> CreateAsync(RecurringJobSchedule schedule, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(schedule.Title))
            throw new InvalidOperationException("Recurring job title is required.");

        if (schedule.IntervalDays < 1)
            throw new InvalidOperationException("Interval must be at least 1 day.");
        if (schedule.IntervalDays > 3650)
            throw new InvalidOperationException("Interval cannot exceed 10 years (3650 days).");

        if (schedule.DefaultQuotedTotal < 0)
            throw new InvalidOperationException("Default quoted total cannot be negative.");
        if (schedule.DefaultQuotedTotal > 100_000_000m)
            throw new InvalidOperationException("Default quoted total cannot exceed 100,000,000.");

        await EnsureCustomerAndDivisionForScheduleAsync(schedule, ct);

        schedule.Title = schedule.Title.Trim();
        if (schedule.Title.Length > 200)
            throw new InvalidOperationException("Recurring job title cannot exceed 200 characters.");
        schedule.NextRunDate = schedule.NextRunDate == default
            ? DateTime.UtcNow.Date
            : schedule.NextRunDate.Date;
        if (schedule.NextRunDate > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Next run date cannot be more than 2 years in the future.");
        if (schedule.NextRunDate < DateTime.UtcNow.Date.AddYears(-1))
            throw new InvalidOperationException("Next run date cannot be more than 1 year in the past.");

        _dbContext.Set<RecurringJobSchedule>().Add(schedule);
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "CREATE",
                "RecurringJobSchedule",
                schedule.Title,
                $"Every {schedule.IntervalDays} day(s), next {schedule.NextRunDate:yyyy-MM-dd}",
                ct);
        }

        return schedule.Id;
    }

    public async Task UpdateAsync(RecurringJobSchedule schedule, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(schedule.Title))
            throw new InvalidOperationException("Recurring job title is required.");
        if (schedule.IntervalDays < 1)
            throw new InvalidOperationException("Interval must be at least 1 day.");
        if (schedule.IntervalDays > 3650)
            throw new InvalidOperationException("Interval cannot exceed 10 years (3650 days).");
        if (schedule.DefaultQuotedTotal < 0)
            throw new InvalidOperationException("Default quoted total cannot be negative.");
        if (schedule.DefaultQuotedTotal > 100_000_000m)
            throw new InvalidOperationException("Default quoted total cannot exceed 100,000,000.");

        var existing = await _dbContext.Set<RecurringJobSchedule>()
            .FirstOrDefaultAsync(s => s.Id == schedule.Id, ct)
            ?? throw new InvalidOperationException("Recurring schedule not found.");

        await EnsureCustomerAndDivisionForScheduleAsync(schedule, ct);

        existing.Title = schedule.Title.Trim();
        if (existing.Title.Length > 200)
            throw new InvalidOperationException("Recurring job title cannot exceed 200 characters.");
        existing.CustomerId = schedule.CustomerId;
        existing.DivisionId = schedule.DivisionId;
        existing.IntervalDays = schedule.IntervalDays;
        existing.NextRunDate = schedule.NextRunDate == default
            ? existing.NextRunDate
            : schedule.NextRunDate.Date;
        if (existing.NextRunDate > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Next run date cannot be more than 2 years in the future.");
        if (existing.NextRunDate < DateTime.UtcNow.Date.AddYears(-1))
            throw new InvalidOperationException("Next run date cannot be more than 1 year in the past.");
        existing.DefaultQuotedTotal = schedule.DefaultQuotedTotal;
        existing.IsActive = schedule.IsActive;

        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "UPDATE",
                "RecurringJobSchedule",
                existing.Title,
                $"Every {existing.IntervalDays} day(s), next {existing.NextRunDate:yyyy-MM-dd}",
                ct);
        }
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var schedule = await _dbContext.Set<RecurringJobSchedule>()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException("Recurring schedule not found.");

        schedule.IsActive = isActive;
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                isActive ? "ACTIVATE" : "DEACTIVATE",
                "RecurringJobSchedule",
                schedule.Title,
                isActive ? "Schedule activated" : "Schedule deactivated",
                ct);
        }
    }

    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var due = await _dbContext.Set<RecurringJobSchedule>()
            .Where(s => s.IsActive && s.NextRunDate <= today)
            .ToListAsync(ct);

        var spawned = 0;
        foreach (var schedule in due)
        {
            try
            {
                var customerOk = await _dbContext.Set<Customer>()
                    .AnyAsync(c => c.Id == schedule.CustomerId, ct);
                if (!customerOk)
                {
                    // Avoid infinite daily failure loops for soft-deleted customers.
                    schedule.IsActive = false;
                    throw new InvalidOperationException(
                        $"Customer not found for schedule '{schedule.Title}' — schedule deactivated.");
                }

                if (schedule.DivisionId is { } divId && divId != Guid.Empty)
                {
                    var divisionOk = await _dbContext.Set<Division>()
                        .AnyAsync(d => d.Id == divId && d.IsActive, ct);
                    if (!divisionOk)
                    {
                        schedule.IsActive = false;
                        throw new InvalidOperationException(
                            $"Division not found or inactive for schedule '{schedule.Title}' — schedule deactivated.");
                    }
                }

                await _jobs.CreateAsync(new Job
                {
                    CustomerId = schedule.CustomerId,
                    DivisionId = schedule.DivisionId,
                    Title = schedule.Title,
                    QuotedTotal = schedule.DefaultQuotedTotal,
                    ScheduledStart = schedule.NextRunDate,
                    Status = JobStatus.Scheduled,
                    Notes = "Spawned from recurring schedule"
                }, ct);

                schedule.NextRunDate = schedule.NextRunDate.AddDays(Math.Max(1, schedule.IntervalDays));
                spawned++;
            }
            catch (Exception ex)
            {
                // Keep processing remaining schedules (quota, closed customer data, etc.).
                if (_audit != null)
                {
                    await _audit.LogAsync(
                        "RECURRING_FAIL",
                        "RecurringJobSchedule",
                        schedule.Title,
                        ex.Message,
                        ct);
                }
            }
        }

        if (spawned > 0 || due.Count > 0)
            await _dbContext.SaveChangesAsync(ct);

        if (spawned > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "RECURRING_PROCESS",
                "RecurringJobSchedule",
                "batch",
                $"Spawned {spawned} of {due.Count} due schedule(s)",
                ct);
        }

        return spawned;
    }

    private async Task EnsureCustomerAndDivisionForScheduleAsync(RecurringJobSchedule schedule, CancellationToken ct)
    {
        var customer = await _dbContext.Set<Customer>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == schedule.CustomerId, ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException("Customer not found for recurring schedule.");

        if (schedule.DivisionId is { } divisionId && divisionId != Guid.Empty)
        {
            var divisionOk = await _dbContext.Set<Division>()
                .AnyAsync(d => d.Id == divisionId && d.IsActive, ct);
            if (!divisionOk)
                throw new InvalidOperationException("Division not found or inactive.");
        }
    }
}
