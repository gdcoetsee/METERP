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

    public RecurringJobService(AppDbContext dbContext, IJobService jobs)
    {
        _dbContext = dbContext;
        _jobs = jobs;
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

    public async Task<Guid> CreateAsync(RecurringJobSchedule schedule, CancellationToken ct = default)
    {
        _dbContext.Set<RecurringJobSchedule>().Add(schedule);
        await _dbContext.SaveChangesAsync(ct);
        return schedule.Id;
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

        if (spawned > 0)
            await _dbContext.SaveChangesAsync(ct);

        return spawned;
    }
}