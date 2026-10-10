using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class PayrollServiceTests
{
    private (AppDbContext Db, PayrollService Service, Guid TenantId) CreateHarness(string? databaseName = null, Guid? tenantId = null)
    {
        var id = tenantId ?? Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(id);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.TenantId).Returns(id);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        return (db, new PayrollService(db), id);
    }

    [Fact]
    public async Task GetMonthlySummariesAsync_AggregatesJobLaborByEmployee()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            var jobId = Guid.NewGuid();
            var thaboId = Guid.NewGuid();
            var johanId = Guid.NewGuid();
            var month = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

            db.Set<Employee>().AddRange(
                new Employee { Id = thaboId, TenantId = tenantId, FirstName = "Thabo", LastName = "Mokoena", JobTitle = "Electrician", DefaultHourlyRate = 195m, IsActive = true },
                new Employee { Id = johanId, TenantId = tenantId, FirstName = "Johan", LastName = "Berg", JobTitle = "Technician", DefaultHourlyRate = 210m, IsActive = true });
            db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "Job", QuotedTotal = 1000m });
            db.Set<JobLabor>().AddRange(
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = thaboId, WorkDate = month, Hours = 8, HourlyRate = 195m },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = thaboId, WorkDate = month.AddDays(1), Hours = 4, HourlyRate = 195m },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = johanId, WorkDate = month, Hours = 6, HourlyRate = 210m },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = null, WorkDate = month, Hours = 99, HourlyRate = 50m },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = thaboId, WorkDate = month.AddMonths(-1), Hours = 40, HourlyRate = 195m, IsDeleted = false },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = thaboId, WorkDate = month, Hours = 2, HourlyRate = 195m, IsDeleted = true });
            await db.SaveChangesAsync();

            var summaries = await service.GetMonthlySummariesAsync(month);

            Assert.Equal(2, summaries.Count);

            var thabo = summaries.First(s => s.EmployeeId == thaboId);
            Assert.Equal(12m, thabo.Hours);
            Assert.Equal(2340m, thabo.GrossPay);
            Assert.Equal(2, thabo.LaborEntryCount);
            // Default 1% deduction
            Assert.Equal(23.40m, thabo.Deductions);
            Assert.Equal(2316.60m, thabo.NetPay);

            var johan = summaries.First(s => s.EmployeeId == johanId);
            Assert.Equal(6m, johan.Hours);
            Assert.Equal(1260m, johan.GrossPay);
            Assert.Equal(1, johan.LaborEntryCount);

            var single = await service.GetEmployeeSummaryAsync(thaboId, month);
            Assert.NotNull(single);
            Assert.Equal(thabo.Hours, single!.Hours);
            Assert.Equal(thabo.NetPay, single.NetPay);

            var csv = await service.ExportMonthlyCsvAsync(month);
            Assert.Contains("EmployeeNumber", csv);
            Assert.Contains("Thabo Mokoena", csv);
            Assert.Contains("2026-06", csv);
        }
    }

    [Fact]
    public async Task GetMonthlySummariesAsync_AppliesCustomDeductions()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            var empId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var month = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
            db.Set<Employee>().Add(new Employee
            {
                Id = empId,
                TenantId = tenantId,
                EmployeeNumber = "E1",
                FirstName = "Pay",
                LastName = "Test",
                DefaultHourlyRate = 100m,
                IsActive = true
            });
            db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "J", QuotedTotal = 1 });
            db.Set<JobLabor>().Add(new JobLabor
            {
                TenantId = tenantId,
                JobId = jobId,
                EmployeeId = empId,
                WorkDate = month,
                Hours = 10,
                HourlyRate = 100m
            });
            await db.SaveChangesAsync();

            var summaries = await service.GetMonthlySummariesAsync(month, deductionPercent: 10m, fixedDeductions: 50m);
            var row = Assert.Single(summaries);
            Assert.Equal(1000m, row.GrossPay);
            Assert.Equal(150m, row.Deductions); // 10% + 50
            Assert.Equal(850m, row.NetPay);
        }
    }

    [Fact]
    public async Task GetMonthlySummariesAsync_IncludesEmployeesWithZeroLabor()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            db.Set<Employee>().Add(new Employee
            {
                TenantId = tenantId,
                FirstName = "Idle",
                LastName = "Tech",
                DefaultHourlyRate = 180m,
                IsActive = true
            });
            await db.SaveChangesAsync();

            var summaries = await service.GetMonthlySummariesAsync(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Single(summaries);
            Assert.Equal(0m, summaries[0].Hours);
            Assert.Equal(0m, summaries[0].GrossPay);
            Assert.Equal(0, summaries[0].LaborEntryCount);
        }
    }

    [Fact]
    public async Task GetWeeklyTimesheetAsync_PlacesHoursOnTheRightDay_AndKeepsClosedJobHistory()
    {
        var dbName = Guid.NewGuid().ToString();
        var (db, service, tenantId) = CreateHarness(dbName);
        using (db)
        {
            var employeeId = Guid.NewGuid();
            var otherEmployeeId = Guid.NewGuid();
            var openJobId = Guid.NewGuid();
            var closedJobId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            // Monday 15 Jun 2026 through Sunday 21 Jun 2026.
            var monday = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

            db.Set<Employee>().AddRange(
                new Employee
                {
                    Id = employeeId,
                    TenantId = tenantId,
                    EmployeeNumber = "E-17",
                    FirstName = "Thabo",
                    LastName = "Mokoena",
                    IsActive = true
                },
                new Employee
                {
                    Id = otherEmployeeId,
                    TenantId = tenantId,
                    EmployeeNumber = "E-18",
                    FirstName = "Johan",
                    LastName = "Berg",
                    IsActive = true
                });
            db.Set<Job>().AddRange(
                new Job
                {
                    Id = openJobId,
                    TenantId = tenantId,
                    CustomerId = customerId,
                    JobNumber = "J-OPEN",
                    Title = "Live panel",
                    Status = JobStatus.InProgress,
                    QuotedTotal = 1000m
                },
                new Job
                {
                    Id = closedJobId,
                    TenantId = tenantId,
                    CustomerId = customerId,
                    JobNumber = "J-CLOSED",
                    Title = "Finished panel",
                    Status = JobStatus.Closed,
                    QuotedTotal = 500m
                });
            db.Set<JobLabor>().AddRange(
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = openJobId,
                    EmployeeId = employeeId,
                    WorkDate = monday.AddHours(16),
                    Hours = 1.25m,
                    HourlyRate = 200m
                },
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = closedJobId,
                    EmployeeId = employeeId,
                    WorkDate = monday.AddDays(2),
                    Hours = 3m,
                    HourlyRate = 200m
                },
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = closedJobId,
                    EmployeeId = employeeId,
                    WorkDate = monday.AddDays(2).AddHours(9),
                    Hours = 1.5m,
                    HourlyRate = 200m
                },
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = closedJobId,
                    EmployeeId = employeeId,
                    WorkDate = monday.AddDays(6).AddHours(23).AddMinutes(30),
                    Hours = 0.5m,
                    HourlyRate = 200m
                },
                // Sunday before the week, and Monday after it, stay off this grid.
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = closedJobId,
                    EmployeeId = employeeId,
                    WorkDate = monday.AddMinutes(-1),
                    Hours = 7m,
                    HourlyRate = 200m
                },
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = closedJobId,
                    EmployeeId = employeeId,
                    WorkDate = monday.AddDays(7),
                    Hours = 9m,
                    HourlyRate = 200m
                },
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = closedJobId,
                    EmployeeId = employeeId,
                    WorkDate = monday.AddDays(3),
                    Hours = 6m,
                    HourlyRate = 200m,
                    IsDeleted = true
                },
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = openJobId,
                    EmployeeId = otherEmployeeId,
                    WorkDate = monday,
                    Hours = 8m,
                    HourlyRate = 180m
                },
                new JobLabor
                {
                    TenantId = tenantId,
                    JobId = openJobId,
                    EmployeeId = null,
                    WorkDate = monday,
                    Hours = 4m,
                    HourlyRate = 100m
                });
            await db.SaveChangesAsync();

            var wednesday = new DateTime(2026, 6, 17, 18, 0, 0, DateTimeKind.Utc);
            var sheet = await service.GetWeeklyTimesheetAsync(employeeId, wednesday);

            Assert.NotNull(sheet);
            Assert.Equal(employeeId, sheet!.EmployeeId);
            Assert.Equal("E-17", sheet.EmployeeNumber);
            Assert.Equal("Thabo Mokoena", sheet.Name);
            Assert.Equal(monday, sheet.WeekStartUtc);
            Assert.Equal(7, sheet.Days.Count);
            Assert.Equal(
                new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" },
                sheet.Days.Select(d => d.Label));
            Assert.Equal(DayOfWeek.Monday, sheet.Days[0].Date.DayOfWeek);
            Assert.Equal(DayOfWeek.Sunday, sheet.Days[6].Date.DayOfWeek);
            Assert.Equal(monday.AddDays(6), sheet.Days[6].Date);

            Assert.Equal(new decimal[] { 1.25m, 0m, 4.5m, 0m, 0m, 0m, 0.5m }, sheet.Days.Select(d => d.Hours));

            Assert.Equal(2, sheet.Rows.Count);
            var closed = Assert.Single(sheet.Rows, r => r.JobId == closedJobId);
            Assert.True(closed.JobClosed);
            Assert.Equal("J-CLOSED", closed.JobNumber);
            Assert.Equal(new decimal[] { 0m, 0m, 4.5m, 0m, 0m, 0m, 0.5m }, closed.HoursByDay);
            Assert.Equal(5m, closed.TotalHours);

            var open = Assert.Single(sheet.Rows, r => r.JobId == openJobId);
            Assert.False(open.JobClosed);
            Assert.Equal(1.25m, open.HoursByDay[0]);
            Assert.Equal(1.25m, open.TotalHours);

            Assert.Equal(6.25m, sheet.TotalHours);
            Assert.Equal(sheet.TotalHours, sheet.Days.Sum(d => d.Hours));
            Assert.Equal(sheet.TotalHours, sheet.Rows.Sum(r => r.TotalHours));
            foreach (var row in sheet.Rows)
                Assert.Equal(row.TotalHours, row.HoursByDay.Sum());
            for (var i = 0; i < 7; i++)
                Assert.Equal(sheet.Days[i].Hours, sheet.Rows.Sum(r => r.HoursByDay[i]));

            var sameWeekFromSunday = await service.GetWeeklyTimesheetAsync(employeeId, monday.AddDays(6));
            Assert.Equal(monday, sameWeekFromSunday!.WeekStartUtc);
            Assert.Equal(6.25m, sameWeekFromSunday.TotalHours);

            var nextWeek = await service.GetWeeklyTimesheetAsync(employeeId, monday.AddDays(7));
            Assert.Equal(monday.AddDays(7), nextWeek!.WeekStartUtc);
            Assert.Equal(new decimal[] { 9m, 0m, 0m, 0m, 0m, 0m, 0m }, nextWeek.Days.Select(d => d.Hours));
            Assert.Equal(9m, nextWeek.TotalHours);
            Assert.Equal(nextWeek.TotalHours, nextWeek.Rows.Sum(r => r.HoursByDay.Sum()));
            var nextClosed = Assert.Single(nextWeek.Rows);
            Assert.True(nextClosed.JobClosed);

            var emptyWeek = await service.GetWeeklyTimesheetAsync(employeeId, new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc));
            Assert.NotNull(emptyWeek);
            Assert.Empty(emptyWeek!.Rows);
            Assert.Equal(0m, emptyWeek.TotalHours);
            Assert.Equal(0m, emptyWeek.Days.Sum(d => d.Hours));
            Assert.All(emptyWeek.Days, d => Assert.Equal(0m, d.Hours));

            Assert.Null(await service.GetWeeklyTimesheetAsync(Guid.NewGuid(), monday));

            var otherEmployeeIdOnOtherTenant = Guid.NewGuid();
            var (otherDb, otherService, _) = CreateHarness(dbName);
            using (otherDb)
            {
                Assert.Null(await otherService.GetWeeklyTimesheetAsync(employeeId, monday));

                var otherJobId = Guid.NewGuid();
                otherDb.Set<Employee>().Add(new Employee
                {
                    Id = otherEmployeeIdOnOtherTenant,
                    EmployeeNumber = "E-X",
                    FirstName = "Other",
                    LastName = "Tenant",
                    IsActive = true
                });
                otherDb.Set<Job>().Add(new Job
                {
                    Id = otherJobId,
                    CustomerId = Guid.NewGuid(),
                    JobNumber = "J-OTHER",
                    Title = "Other tenant",
                    Status = JobStatus.Closed,
                    QuotedTotal = 1m
                });
                otherDb.Set<JobLabor>().Add(new JobLabor
                {
                    JobId = otherJobId,
                    EmployeeId = otherEmployeeIdOnOtherTenant,
                    WorkDate = monday,
                    Hours = 40m,
                    HourlyRate = 100m
                });
                await otherDb.SaveChangesAsync();

                var otherSheet = await otherService.GetWeeklyTimesheetAsync(otherEmployeeIdOnOtherTenant, monday);
                Assert.NotNull(otherSheet);
                Assert.Equal(40m, otherSheet!.TotalHours);
                Assert.True(Assert.Single(otherSheet.Rows).JobClosed);
            }

            var unchanged = await service.GetWeeklyTimesheetAsync(employeeId, monday);
            Assert.Equal(6.25m, unchanged!.TotalHours);
            Assert.Null(await service.GetWeeklyTimesheetAsync(otherEmployeeIdOnOtherTenant, monday));
        }
    }
}