using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class LeaveServiceTests
{
    private static (LeaveService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"leave-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new TestCurrentUser());
        return (new LeaveService(db), db, tenantId);
    }

    [Fact]
    public async Task SubmitRequestAsync_AdvancesThroughApprovalChain()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E1",
                FirstName = "Test",
                LastName = "User",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(14),
                EndDate = DateTime.UtcNow.AddDays(16),
                IsPaid = true,
                Reason = "Family"
            });

            var managerId = Guid.NewGuid();
            Assert.True(await service.ApproveManagerAsync(requestId, managerId));

            var executiveId = Guid.NewGuid();
            Assert.True(await service.ApproveExecutiveAsync(requestId, executiveId));

            var hrId = Guid.NewGuid();
            Assert.True(await service.ApproveHrAsync(requestId, hrId));

            var saved = await db.Set<LeaveRequest>().FirstAsync(r => r.Id == requestId);
            Assert.Equal(LeaveRequestStatus.Approved, saved.Status);
            Assert.True(saved.DaysRequested > 0);
        }
    }

    [Fact]
    public async Task GetPendingApprovalsAsync_ReturnsNonFinalRequests()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E3",
                FirstName = "Pending",
                LastName = "Leave",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            db.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(7),
                EndDate = DateTime.UtcNow.AddDays(8),
                DaysRequested = 2,
                Status = LeaveRequestStatus.PendingManager
            });
            db.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(-30),
                EndDate = DateTime.UtcNow.AddDays(-28),
                DaysRequested = 3,
                Status = LeaveRequestStatus.Approved
            });
            await db.SaveChangesAsync();

            var pending = await service.GetPendingApprovalsAsync();
            Assert.Single(pending);
            Assert.Equal(LeaveRequestStatus.PendingManager, pending[0].Status);
        }
    }

    [Fact]
    public async Task SubmitRequestAsync_RejectsWhenInsufficientBalance()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E2",
                FirstName = "New",
                LastName = "Hire",
                HireDate = DateTime.UtcNow,
                AnnualLeaveEntitlementDays = 15
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(1),
                EndDate = DateTime.UtcNow.AddDays(10),
                IsPaid = true
            }));
        }
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid TenantId => Guid.Empty;
        public string? UserName => "test";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Permissions => Array.Empty<string>();
    }
}