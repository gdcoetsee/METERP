using METERP.Application.Services;
using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class NotificationLinksTests
{
    [Fact]
    public void ForEntity_OpensInvoiceWithQuery()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Assert.Equal($"/invoices?open={id:D}", NotificationLinks.ForEntity(nameof(Invoice), id));
    }

    [Fact]
    public void ForEntity_OpensJobCommandCenter()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"/jobs/{id:D}", NotificationLinks.ForEntity(nameof(Job), id));
    }

    [Fact]
    public void ForEntity_OpensQuoteEditorAndApprovalsForRequisition()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"/quotes?open={id:D}", NotificationLinks.ForEntity(nameof(Quote), id));
        Assert.Equal($"/opportunities?open={id:D}", NotificationLinks.ForEntity(nameof(Opportunity), id));
        Assert.Equal($"/purchase-orders?open={id:D}", NotificationLinks.ForEntity(nameof(PurchaseOrder), id));
        Assert.Equal("/approvals?tab=requisitions", NotificationLinks.ForEntity(nameof(StockRequisition), Guid.NewGuid()));
        Assert.Equal("/approvals?tab=field", NotificationLinks.ForEntity(nameof(FieldReport), Guid.NewGuid()));
        Assert.Equal("/approvals?tab=leave", NotificationLinks.ForEntity(nameof(LeaveRequest), Guid.NewGuid()));
    }

    [Fact]
    public void ForEntity_ReturnsNullWhenMissing()
    {
        Assert.Null(NotificationLinks.ForEntity(null, Guid.NewGuid()));
        Assert.Null(NotificationLinks.ForEntity(nameof(Invoice), Guid.Empty));
        Assert.Null(NotificationLinks.ForEntity("Unknown", Guid.NewGuid()));
    }
}
