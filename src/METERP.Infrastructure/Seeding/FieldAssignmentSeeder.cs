using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Idempotent field-portal assignments for the demo technician.
/// Links <c>tech@acme.demo</c> (or <c>tech@met.demo</c>) to one employee, then assigns
/// a handful of unassigned InProgress FT/SD TRFid jobs. Does not mass-assign and does not
/// take a job that already has a lead or crew.
/// </summary>
public static class FieldAssignmentSeeder
{
    public const string TechEmail = "tech@acme.demo";
    public const string MetTechEmail = "tech@met.demo";
    public const string DemoEmployeeNumber = "TECH-DEMO";
    public const int TargetAssignments = 5;
    public const int StopWhenAtLeast = 3;
    public const int MaximumAssignments = 8;

    public readonly record struct Result(
        int Assigned,
        int AlreadyAssigned,
        bool EmployeeReady,
        string? EmployeeNumber,
        IReadOnlyList<string> JobNumbers)
    {
        public string Summary =>
            $"assigned={Assigned} already={AlreadyAssigned} employee={(EmployeeReady ? EmployeeNumber ?? "?" : "none")} jobs={string.Join(",", JobNumbers)}";
    }

    public static async Task<Result> RunAsync(
        AppDbContext db,
        ITenantProvider tenantProvider,
        CancellationToken ct = default)
    {
        var user = await FindDemoTechAsync(db, ct);
        if (user == null || user.TenantId == Guid.Empty)
            return new Result(0, 0, false, null, Array.Empty<string>());

        tenantProvider.SetTenantId(user.TenantId);
        var tenantId = user.TenantId;

        var employee = await EnsureEmployeeAsync(db, tenantId, user.Id, user.Email ?? TechEmail, ct);
        if (employee == null)
            return new Result(0, 0, false, null, Array.Empty<string>());

        var already = await CountOpenAssignmentsAsync(db, tenantId, employee.Id, ct);
        if (already >= StopWhenAtLeast)
            return new Result(0, already, true, employee.EmployeeNumber, Array.Empty<string>());

        var need = Math.Min(TargetAssignments, MaximumAssignments) - already;
        if (need <= 0)
            return new Result(0, already, true, employee.EmployeeNumber, Array.Empty<string>());

        var jobIds = await PickJobIdsAsync(db, tenantId, need, ct);
        if (jobIds.Count == 0)
            return new Result(0, already, true, employee.EmployeeNumber, Array.Empty<string>());

        var numbers = new List<string>();
        foreach (var jobId in jobIds)
        {
            var job = await db.Set<Job>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.Id == jobId && j.TenantId == tenantId && !j.IsDeleted, ct);
            if (job == null || job.AssignedEmployeeId != null)
                continue;

            var hasCrew = await db.Set<JobCrewAssignment>()
                .IgnoreQueryFilters()
                .AnyAsync(a => a.JobId == job.Id && a.TenantId == tenantId && !a.IsDeleted, ct);
            if (hasCrew)
                continue;

            job.AssignedEmployeeId = employee.Id;

            var crew = await db.Set<JobCrewAssignment>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.JobId == job.Id && a.TenantId == tenantId && a.EmployeeId == employee.Id, ct);
            if (crew != null)
                crew.IsDeleted = false;
            else
            {
                db.Set<JobCrewAssignment>().Add(new JobCrewAssignment
                {
                    JobId = job.Id,
                    EmployeeId = employee.Id,
                    TenantId = tenantId
                });
            }

            numbers.Add(job.JobNumber);
        }

        if (numbers.Count > 0)
            await db.SaveChangesAsync(ct);

        return new Result(numbers.Count, already, true, employee.EmployeeNumber, numbers);
    }

    internal static bool IsTrfIdJobNumber(string? jobNumber)
    {
        if (string.IsNullOrWhiteSpace(jobNumber))
            return false;

        var number = jobNumber.Trim();
        if (number.Length < 3)
            return false;

        if (!number.StartsWith("FT", StringComparison.OrdinalIgnoreCase)
            && !number.StartsWith("SD", StringComparison.OrdinalIgnoreCase))
            return false;

        for (var i = 2; i < number.Length; i++)
        {
            if (!char.IsDigit(number[i]))
                return false;
        }

        return true;
    }

    private static async Task<ApplicationUser?> FindDemoTechAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var email in new[] { TechEmail, MetTechEmail })
        {
            var normalized = email.ToUpperInvariant();
            var user = await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
            if (user != null)
                return user;
        }

        return null;
    }

    private static async Task<Employee?> EnsureEmployeeAsync(
        AppDbContext db,
        Guid tenantId,
        Guid userId,
        string email,
        CancellationToken ct)
    {
        var linked = await db.Set<Employee>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && !e.IsDeleted && e.LinkedUserId == userId, ct);
        if (linked != null)
            return linked.IsActive ? linked : null;

        var demo = await db.Set<Employee>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && !e.IsDeleted && e.EmployeeNumber == DemoEmployeeNumber, ct);
        if (demo != null)
        {
            if (demo.LinkedUserId is { } other && other != Guid.Empty && other != userId)
                return null;

            demo.LinkedUserId = userId;
            demo.IsActive = true;
            if (string.IsNullOrWhiteSpace(demo.Email))
                demo.Email = email;
            await db.SaveChangesAsync(ct);
            return demo;
        }

        demo = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = DemoEmployeeNumber,
            FirstName = "Field",
            LastName = "Technician",
            JobTitle = "Field Team",
            Email = email,
            LinkedUserId = userId,
            IsActive = true,
            DefaultHourlyRate = 210m,
            HireDate = DateTime.UtcNow.AddYears(-1),
            AnnualLeaveEntitlementDays = 15m,
            MandatoryHoursPerMonth = 160m,
            Notes = "Demo field login for tech@acme.demo. Not an Access payroll row."
        };
        db.Set<Employee>().Add(demo);
        await db.SaveChangesAsync(ct);
        return demo;
    }

    private static async Task<int> CountOpenAssignmentsAsync(
        AppDbContext db,
        Guid tenantId,
        Guid employeeId,
        CancellationToken ct)
    {
        return await db.Set<Job>()
            .IgnoreQueryFilters()
            .Where(j => j.TenantId == tenantId && !j.IsDeleted)
            .Where(j => j.Status == JobStatus.Scheduled
                || j.Status == JobStatus.InProgress
                || j.Status == JobStatus.OnHold)
            .Where(j => j.AssignedEmployeeId == employeeId
                || db.Set<JobCrewAssignment>().IgnoreQueryFilters().Any(a =>
                    a.JobId == j.Id && a.TenantId == tenantId && !a.IsDeleted && a.EmployeeId == employeeId))
            .CountAsync(ct);
    }

    private static async Task<List<Guid>> PickJobIdsAsync(
        AppDbContext db,
        Guid tenantId,
        int need,
        CancellationToken ct)
    {
        var pool = await db.Set<Job>()
            .IgnoreQueryFilters()
            .Where(j => j.TenantId == tenantId && !j.IsDeleted && j.Status == JobStatus.InProgress)
            .Where(j => j.AssignedEmployeeId == null)
            .Where(j => !db.Set<JobCrewAssignment>().IgnoreQueryFilters().Any(a =>
                a.JobId == j.Id && a.TenantId == tenantId && !a.IsDeleted))
            .Where(j => j.JobNumber.StartsWith("FT") || j.JobNumber.StartsWith("SD"))
            .OrderByDescending(j => j.JobNumber.Length)
            .ThenByDescending(j => j.JobNumber)
            .Select(j => new { j.Id, j.JobNumber })
            .Take(120)
            .ToListAsync(ct);

        var trf = pool.Where(j => IsTrfIdJobNumber(j.JobNumber)).ToList();
        var picked = new List<Guid>();
        foreach (var row in trf.Where(j => j.JobNumber.StartsWith("FT", StringComparison.OrdinalIgnoreCase)).Take(3))
            picked.Add(row.Id);
        foreach (var row in trf.Where(j => j.JobNumber.StartsWith("SD", StringComparison.OrdinalIgnoreCase)).Take(2))
            picked.Add(row.Id);

        if (picked.Count < need)
        {
            foreach (var row in trf)
            {
                if (picked.Count >= need)
                    break;
                if (!picked.Contains(row.Id))
                    picked.Add(row.Id);
            }
        }

        if (picked.Count >= need)
            return picked.Take(need).ToList();

        var more = await db.Set<Job>()
            .IgnoreQueryFilters()
            .Where(j => j.TenantId == tenantId && !j.IsDeleted && j.Status == JobStatus.InProgress)
            .Where(j => j.AssignedEmployeeId == null)
            .Where(j => !db.Set<JobCrewAssignment>().IgnoreQueryFilters().Any(a =>
                a.JobId == j.Id && a.TenantId == tenantId && !a.IsDeleted))
            .Where(j => !j.JobNumber.StartsWith("FT") && !j.JobNumber.StartsWith("SD"))
            .OrderByDescending(j => j.JobNumber)
            .Select(j => j.Id)
            .Take(need - picked.Count)
            .ToListAsync(ct);

        picked.AddRange(more);
        return picked.Take(need).ToList();
    }
}
