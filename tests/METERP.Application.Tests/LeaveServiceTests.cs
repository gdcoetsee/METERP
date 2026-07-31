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
    public async Task GetEmployeeForUserAsync_ReturnsLinkedEmployee()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var userId = Guid.NewGuid();
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E4",
                FirstName = "Linked",
                LastName = "Tech",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 15,
                LinkedUserId = userId,
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var found = await service.GetEmployeeForUserAsync(userId);
            Assert.NotNull(found);
            Assert.Equal(employee.Id, found!.Id);
        }
    }

    [Fact]
    public async Task GetEmployeeForUserAsync_ReturnsNull_WhenNotLinked()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var found = await service.GetEmployeeForUserAsync(Guid.NewGuid());
            Assert.Null(found);
        }
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_UpdatesBalance_AndRequiresReason()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-ADJ",
                FirstName = "Adjust",
                LastName = "Me",
                HireDate = DateTime.UtcNow.AddYears(-1),
                LeaveBalanceDays = 5m,
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AdjustLeaveBalanceAsync(employee.Id, 12m, "  "));

            await service.AdjustLeaveBalanceAsync(employee.Id, 12m, "HR correction Q1");

            var reloaded = await db.Set<Employee>().AsNoTracking().FirstAsync(e => e.Id == employee.Id);
            Assert.Equal(12m, reloaded.LeaveBalanceDays);
            Assert.Contains("HR correction Q1", reloaded.Notes);
            Assert.Contains("was 5", reloaded.Notes);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AdjustLeaveBalanceAsync(employee.Id, -1m, "Bad balance"));
        }
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_ThrowsWhenEmployeeInactive()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-INA",
                FirstName = "Inactive",
                LastName = "Emp",
                HireDate = DateTime.UtcNow.AddYears(-1),
                LeaveBalanceDays = 5m,
                IsActive = false
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AdjustLeaveBalanceAsync(employee.Id, 8m, "Should fail"));
        }
    }

    [Fact]
    public async Task GetRecentRequestsAsync_IncludesEmployee()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-REC",
                FirstName = "Recent",
                LastName = "Leave",
                HireDate = DateTime.UtcNow.AddYears(-1),
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            db.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(3),
                EndDate = DateTime.UtcNow.AddDays(5),
                DaysRequested = 3,
                Status = LeaveRequestStatus.PendingManager,
                Reason = "Trip"
            });
            await db.SaveChangesAsync();

            var recent = await service.GetRecentRequestsAsync(50);
            Assert.Single(recent);
            Assert.NotNull(recent[0].Employee);
            Assert.Equal("Recent", recent[0].Employee!.FirstName);
        }
    }

    [Fact]
    public async Task GetAvailableLeaveDaysAsync_ReturnsAccruedMinusTaken()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E5",
                FirstName = "Accrued",
                LastName = "Worker",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 2
            };
            db.Set<Employee>().Add(employee);
            db.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(-60),
                EndDate = DateTime.UtcNow.AddDays(-58),
                DaysRequested = 3,
                Status = LeaveRequestStatus.Approved,
                IsPaid = true
            });
            await db.SaveChangesAsync();

            var available = await service.GetAvailableLeaveDaysAsync(employee.Id);
            var accrued = await service.GetAccruedLeaveDaysAsync(employee.Id);

            Assert.True(available > 0);
            Assert.True(available < accrued + 2);
        }
    }

    [Fact]
    public async Task GetRequestsForEmployeeAsync_ReturnsEmployeeRequestsOnly()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E6",
                FirstName = "Leave",
                LastName = "History",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 15
            };
            var other = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E7",
                FirstName = "Other",
                LastName = "Person",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 15
            };
            db.Set<Employee>().AddRange(employee, other);
            await db.SaveChangesAsync();

            db.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(10),
                EndDate = DateTime.UtcNow.AddDays(11),
                DaysRequested = 2,
                Status = LeaveRequestStatus.PendingManager
            });
            db.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = other.Id,
                StartDate = DateTime.UtcNow.AddDays(20),
                EndDate = DateTime.UtcNow.AddDays(21),
                DaysRequested = 2,
                Status = LeaveRequestStatus.PendingManager
            });
            await db.SaveChangesAsync();

            var requests = await service.GetRequestsForEmployeeAsync(employee.Id);
            Assert.Single(requests);
            Assert.Equal(employee.Id, requests[0].EmployeeId);
        }
    }

    [Fact]
    public async Task RejectAsync_ReturnsFalse_WhenAlreadyRejected()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E8",
                FirstName = "Reject",
                LastName = "Twice",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 15
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(30),
                EndDate = DateTime.UtcNow.AddDays(31),
                IsPaid = true,
                Reason = "Break"
            });

            Assert.True(await service.RejectAsync(requestId, Guid.NewGuid(), "Denied"));
            Assert.False(await service.RejectAsync(requestId, Guid.NewGuid(), "Again"));
        }
    }

    [Fact]
    public async Task ApproveExecutiveAsync_ReturnsFalse_WhenWrongStage()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E10",
                FirstName = "Exec",
                LastName = "Guard",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 15
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(50),
                EndDate = DateTime.UtcNow.AddDays(51),
                IsPaid = true
            });

            Assert.False(await service.ApproveExecutiveAsync(requestId, Guid.NewGuid()));
            Assert.True(await service.ApproveManagerAsync(requestId, Guid.NewGuid()));
            Assert.True(await service.ApproveExecutiveAsync(requestId, Guid.NewGuid()));
            Assert.False(await service.ApproveExecutiveAsync(requestId, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task ApproveHrAsync_ReturnsFalse_WhenWrongStage()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E11",
                FirstName = "Hr",
                LastName = "Guard",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 15
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(60),
                EndDate = DateTime.UtcNow.AddDays(61),
                IsPaid = true
            });

            await service.ApproveManagerAsync(requestId, Guid.NewGuid());
            await service.ApproveExecutiveAsync(requestId, Guid.NewGuid());

            Assert.True(await service.ApproveHrAsync(requestId, Guid.NewGuid()));
            Assert.False(await service.ApproveHrAsync(requestId, Guid.NewGuid()));

            var saved = await db.Set<LeaveRequest>().FirstAsync(r => r.Id == requestId);
            Assert.Equal(LeaveRequestStatus.Approved, saved.Status);
        }
    }

    [Fact]
    public async Task ApproveManagerAsync_ReturnsFalse_WhenWrongStage()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E9",
                FirstName = "Stage",
                LastName = "Guard",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 15
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.AddDays(40),
                EndDate = DateTime.UtcNow.AddDays(41),
                IsPaid = true
            });

            Assert.True(await service.ApproveManagerAsync(requestId, Guid.NewGuid()));
            Assert.False(await service.ApproveManagerAsync(requestId, Guid.NewGuid()));
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

    [Fact]
    public async Task SubmitRequestAsync_RejectsOverlappingPendingLeave()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-OVL",
                FirstName = "Over",
                LastName = "Lap",
                HireDate = DateTime.UtcNow.AddYears(-2),
                AnnualLeaveEntitlementDays = 25,
                LeaveBalanceDays = 10
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(10),
                EndDate = DateTime.UtcNow.Date.AddDays(14),
                IsPaid = true
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(12),
                EndDate = DateTime.UtcNow.Date.AddDays(16),
                IsPaid = true
            }));
            Assert.Contains("overlap", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CancelAsync_CancelsPendingRequest()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-CAN",
                FirstName = "Can",
                LastName = "Cel",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 5
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(20),
                EndDate = DateTime.UtcNow.Date.AddDays(22),
                IsPaid = true
            });

            Assert.True(await service.CancelAsync(requestId, Guid.NewGuid(), "Plans changed"));
            var saved = await db.Set<LeaveRequest>().FirstAsync(r => r.Id == requestId);
            Assert.Equal(LeaveRequestStatus.Cancelled, saved.Status);
            Assert.False(await service.CancelAsync(requestId, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task CancelAsync_CancelsApprovedLeave_WhenNotStarted()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-APPR-CAN",
                FirstName = "Future",
                LastName = "Leave",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 10
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(30),
                EndDate = DateTime.UtcNow.Date.AddDays(32),
                IsPaid = true
            });
            var userId = Guid.NewGuid();
            Assert.True(await service.ApproveManagerAsync(requestId, userId));
            Assert.True(await service.ApproveExecutiveAsync(requestId, userId));
            Assert.True(await service.ApproveHrAsync(requestId, userId));

            Assert.True(await service.CancelAsync(requestId, userId, "Travel cancelled"));
            var saved = await db.Set<LeaveRequest>().FirstAsync(r => r.Id == requestId);
            Assert.Equal(LeaveRequestStatus.Cancelled, saved.Status);
        }
    }

    [Fact]
    public async Task CancelAsync_DoesNotCancelApprovedLeave_OnceStarted()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-STARTED",
                FirstName = "On",
                LastName = "Leave",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 10
            };
            db.Set<Employee>().Add(employee);
            db.Set<LeaveRequest>().Add(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(-1),
                EndDate = DateTime.UtcNow.Date.AddDays(2),
                DaysRequested = 3,
                Status = LeaveRequestStatus.Approved,
                IsPaid = true
            });
            await db.SaveChangesAsync();

            var requestId = await db.Set<LeaveRequest>().Select(r => r.Id).FirstAsync();
            Assert.False(await service.CancelAsync(requestId, Guid.NewGuid()));
            Assert.Equal(LeaveRequestStatus.Approved,
                (await db.Set<LeaveRequest>().FirstAsync(r => r.Id == requestId)).Status);
        }
    }

    [Fact]
    public async Task SubmitRequestAsync_RejectsDaysRequestedAbove120()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-DAYS",
                FirstName = "Days",
                LastName = "Cap",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 200,
                LeaveBalanceDays = 200
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SubmitRequestAsync(new LeaveRequest
                {
                    TenantId = tenantId,
                    EmployeeId = employee.Id,
                    StartDate = DateTime.UtcNow.Date.AddDays(1),
                    EndDate = DateTime.UtcNow.Date.AddDays(5),
                    DaysRequested = 121m,
                    IsPaid = false
                }));
            Assert.Contains("120", ex.Message);
        }
    }

    [Fact]
    public async Task SubmitRequestAsync_RejectsRangeLongerThan120Days()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-LONG",
                FirstName = "Long",
                LastName = "Leave",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 200,
                LeaveBalanceDays = 200
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SubmitRequestAsync(new LeaveRequest
                {
                    TenantId = tenantId,
                    EmployeeId = employee.Id,
                    StartDate = DateTime.UtcNow.Date.AddDays(1),
                    EndDate = DateTime.UtcNow.Date.AddDays(150),
                    IsPaid = true
                }));
            Assert.Contains("120", ex.Message);
        }
    }

    [Fact]
    public async Task SubmitRequestAsync_RejectsEndBeforeStart()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-DATE",
                FirstName = "Date",
                LastName = "Guard",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 5
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(5),
                EndDate = DateTime.UtcNow.Date.AddDays(2),
                IsPaid = false
            }));
        }
    }

    [Fact]
    public async Task SubmitRequestAsync_RejectsReasonTooLong()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-RSN",
                FirstName = "Reason",
                LastName = "Long",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 10
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SubmitRequestAsync(new LeaveRequest
                {
                    TenantId = tenantId,
                    EmployeeId = employee.Id,
                    StartDate = DateTime.UtcNow.Date.AddDays(1),
                    EndDate = DateTime.UtcNow.Date.AddDays(2),
                    IsPaid = false,
                    Reason = new string('R', 501)
                }));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task RejectAsync_RejectsReasonTooLong()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-REJ",
                FirstName = "Reject",
                LastName = "Long",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 10
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(1),
                EndDate = DateTime.UtcNow.Date.AddDays(2),
                IsPaid = false,
                Reason = "Holiday"
            });

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                service.RejectAsync(requestId, Guid.NewGuid(), new string('X', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task RejectAsync_AcceptsReasonAt500Characters()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-REJ-OK",
                FirstName = "Reject",
                LastName = "Ok",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 10
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(1),
                EndDate = DateTime.UtcNow.Date.AddDays(2),
                IsPaid = false,
                Reason = "Holiday"
            });

            Assert.True(await service.RejectAsync(requestId, Guid.NewGuid(), new string('X', 500)));
            var saved = await db.Set<LeaveRequest>().FirstAsync(r => r.Id == requestId);
            Assert.Equal(LeaveRequestStatus.Rejected, saved.Status);
            Assert.Equal(500, saved.RejectionReason!.Length);
        }
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_RejectsReasonTooLong()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-ADJ",
                FirstName = "Adj",
                LastName = "Long",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 5,
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                service.AdjustLeaveBalanceAsync(employee.Id, 8m, new string('A', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_AcceptsReasonAt500Characters()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-ADJ-OK",
                FirstName = "Adj",
                LastName = "Ok",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 5,
                IsActive = true
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            await service.AdjustLeaveBalanceAsync(employee.Id, 8m, new string('A', 500));
            var updated = await db.Set<Employee>().FirstAsync(e => e.Id == employee.Id);
            Assert.Equal(8m, updated.LeaveBalanceDays);
        }
    }

    [Fact]
    public async Task CancelAsync_RejectsReasonTooLong()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-CAN",
                FirstName = "Can",
                LastName = "Long",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 10
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(1),
                EndDate = DateTime.UtcNow.Date.AddDays(2),
                IsPaid = false,
                Reason = "Holiday"
            });

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CancelAsync(requestId, Guid.NewGuid(), new string('C', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CancelAsync_AcceptsReasonAt500Characters()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-CAN-OK",
                FirstName = "Can",
                LastName = "Ok",
                HireDate = DateTime.UtcNow.AddYears(-1),
                AnnualLeaveEntitlementDays = 20,
                LeaveBalanceDays = 10
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var requestId = await service.SubmitRequestAsync(new LeaveRequest
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(1),
                EndDate = DateTime.UtcNow.Date.AddDays(2),
                IsPaid = false,
                Reason = "Holiday"
            });

            Assert.True(await service.CancelAsync(requestId, Guid.NewGuid(), new string('C', 500)));
            var saved = await db.Set<LeaveRequest>().FirstAsync(r => r.Id == requestId);
            Assert.Equal(LeaveRequestStatus.Cancelled, saved.Status);
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