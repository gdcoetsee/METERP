using METERP.Application.Models;
using Xunit;

namespace METERP.Application.Tests;

public class CashDeskQueueTests
{
    private static ReadyToInvoiceJobRow Row(string number, string reason) =>
        new(Guid.NewGuid(), number, number, "Cust", 1000m, 0m, 100m, reason);

    [Fact]
    public void Deposits_That_Fill_The_Window_Leave_Two_SignOff_Slots()
    {
        var deposits = Enumerable.Range(1, 10).Select(i => Row($"D{i}", "Deposit")).ToList();
        var signOff = Enumerable.Range(1, 4).Select(i => Row($"S{i}", "Sign-off")).ToList();

        var mixed = CashDeskQueue.Mix(deposits, [], signOff);

        Assert.Equal(8, mixed.Count);
        Assert.Equal(["D1", "D2", "D3", "D4", "D5", "D6", "S1", "S2"], mixed.Select(r => r.JobNumber).ToArray());
    }

    [Fact]
    public void One_SignOff_Row_Takes_Only_One_Reserved_Slot()
    {
        var deposits = Enumerable.Range(1, 10).Select(i => Row($"D{i}", "Deposit")).ToList();

        var mixed = CashDeskQueue.Mix(deposits, [], [Row("S1", "Sign-off")]);

        Assert.Equal(8, mixed.Count);
        Assert.Equal(7, mixed.Count(r => r.Reason == "Deposit"));
        Assert.Equal("S1", mixed[^1].JobNumber);
    }

    [Fact]
    public void Does_Not_Steal_Slots_When_SignOff_Already_Fits()
    {
        var deposits = Enumerable.Range(1, 3).Select(i => Row($"D{i}", "Deposit")).ToList();
        var signOff = Enumerable.Range(1, 10).Select(i => Row($"S{i}", "Sign-off")).ToList();

        var mixed = CashDeskQueue.Mix(deposits, [], signOff);

        Assert.Equal(["D1", "D2", "D3", "S1", "S2", "S3", "S4", "S5"], mixed.Select(r => r.JobNumber).ToArray());
    }

    [Fact]
    public void Empty_Deposit_Queue_Keeps_Ready_Then_SignOff()
    {
        var ready = Enumerable.Range(1, 8).Select(i => Row($"R{i}", "Unbilled")).ToList();
        var signOff = Enumerable.Range(1, 3).Select(i => Row($"S{i}", "Sign-off")).ToList();

        var mixed = CashDeskQueue.Mix([], ready, signOff);

        Assert.Equal(8, mixed.Count);
        Assert.All(mixed, r => Assert.Equal("Unbilled", r.Reason));
    }

    [Fact]
    public void Ready_Rows_Sit_Between_Deposits_And_Reserved_SignOff()
    {
        var deposits = Enumerable.Range(1, 5).Select(i => Row($"D{i}", "Deposit")).ToList();
        var ready = Enumerable.Range(1, 5).Select(i => Row($"R{i}", "Unbilled")).ToList();
        var signOff = Enumerable.Range(1, 3).Select(i => Row($"S{i}", "Sign-off")).ToList();

        var mixed = CashDeskQueue.Mix(deposits, ready, signOff);

        Assert.Equal(["D1", "D2", "D3", "D4", "D5", "R1", "S1", "S2"], mixed.Select(r => r.JobNumber).ToArray());
    }

    [Fact]
    public void Same_Job_Is_Not_Listed_As_Deposit_And_SignOff()
    {
        var jobId = Guid.NewGuid();
        var deposit = new ReadyToInvoiceJobRow(jobId, "J1", "T", "C", 1m, 0m, 1m, "Deposit");
        var duplicate = new ReadyToInvoiceJobRow(jobId, "J1", "T", "C", 1m, 0m, 1m, "Sign-off");

        var mixed = CashDeskQueue.Mix([deposit], [], [duplicate, Row("S2", "Sign-off")]);

        Assert.Equal(["J1", "S2"], mixed.Select(r => r.JobNumber).ToArray());
    }

    [Fact]
    public void SignOff_Only_Still_Fills_The_Window()
    {
        var signOff = Enumerable.Range(1, 10).Select(i => Row($"S{i}", "Sign-off")).ToList();

        var mixed = CashDeskQueue.Mix([], [], signOff);

        Assert.Equal(8, mixed.Count);
        Assert.All(mixed, r => Assert.Equal("Sign-off", r.Reason));
    }
}
