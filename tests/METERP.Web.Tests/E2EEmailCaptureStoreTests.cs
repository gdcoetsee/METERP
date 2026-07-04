using METERP.Web.Services;
using Xunit;

namespace METERP.Web.Tests;

public class E2EEmailCaptureStoreTests
{
    [Fact]
    public void BeginCapture_RecordsMessages_WhileActive()
    {
        var store = new E2EEmailCaptureStore();
        store.Record("a@test.demo", "Ignored", "<p>x</p>");
        Assert.Empty(store.GetAll());

        store.BeginCapture();
        store.Record("beta@test.demo", "Two-factor authentication enabled", "<p>enabled</p>");
        store.Record("beta@test.demo", "Two-factor authentication disabled", "<p>disabled</p>");

        var messages = store.GetAll();
        Assert.Equal(2, messages.Count);
        Assert.Equal("Two-factor authentication enabled", messages[0].Subject);
    }

    [Fact]
    public void BeginCapture_SetsIsCapturingTrue()
    {
        var store = new E2EEmailCaptureStore();
        Assert.False(store.IsCapturing);

        store.BeginCapture();

        Assert.True(store.IsCapturing);
    }

    [Fact]
    public void GetAll_ReturnsSnapshot_NotLiveBackingList()
    {
        var store = new E2EEmailCaptureStore();
        store.BeginCapture();
        store.Record("user@test.demo", "Subject", "<p>body</p>");

        var first = store.GetAll();
        store.Record("other@test.demo", "Second", "<p>two</p>");

        Assert.Single(first);
        Assert.Equal(2, store.GetAll().Count);
    }

    [Fact]
    public void Clear_StopsCapture_AndEmptiesMessages()
    {
        var store = new E2EEmailCaptureStore();
        store.BeginCapture();
        store.Record("user@test.demo", "Subject", "<p>body</p>");
        store.Clear();

        Assert.False(store.IsCapturing);
        Assert.Empty(store.GetAll());
    }
}