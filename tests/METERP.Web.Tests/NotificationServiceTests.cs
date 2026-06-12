using System.Text.Json;
using Microsoft.JSInterop;
using METERP.Web.Services;
using Xunit;

namespace METERP.Web.Tests;

public class NotificationServiceTests
{
    [Fact]
    public async Task AddAsync_PersistsAndReturnsOrderedByDate()
    {
        var js = new InMemoryJsRuntime();
        var service = new NotificationService(js);

        await service.AddAsync("Low Stock Alert", "Transformer oil below reorder.");
        await service.AddAsync("Job Overdue", "Job past scheduled date.", isRead: true);

        var items = await service.GetAllAsync();

        Assert.Equal(2, items.Count);
        Assert.Equal("Job Overdue", items[0].Title);
        Assert.True(items[1].IsRead == false || items[0].Date >= items[1].Date);
        Assert.Contains("Low Stock Alert", items.Select(i => i.Title));
    }

    [Fact]
    public async Task MarkReadAsync_MarksSingleItemRead()
    {
        var js = new InMemoryJsRuntime();
        var service = new NotificationService(js);
        await service.AddAsync("Unread alert", "Needs attention");
        await service.AddAsync("Already read", "Done", isRead: true);

        var unread = (await service.GetAllAsync()).First(i => i.Title == "Unread alert");
        await service.MarkReadAsync(unread.Id);

        var items = await service.GetAllAsync();
        Assert.True(items.First(i => i.Id == unread.Id).IsRead);
        Assert.True(items.First(i => i.Title == "Already read").IsRead);
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksEveryItemRead()
    {
        var js = new InMemoryJsRuntime();
        var service = new NotificationService(js);
        await service.AddAsync("A", "one");
        await service.AddAsync("B", "two", isRead: false);

        await service.MarkAllReadAsync();
        var items = await service.GetAllAsync();

        Assert.All(items, i => Assert.True(i.IsRead));
    }

    private sealed class InMemoryJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> _storage = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "localStorage.getItem" && args?.Length == 1)
            {
                var key = args[0]?.ToString() ?? "";
                _storage.TryGetValue(key, out var value);
                return ValueTask.FromResult((TValue)(object)(value ?? "")!);
            }

            if (identifier == "localStorage.setItem" && args?.Length == 2)
            {
                _storage[args[0]?.ToString() ?? ""] = args[1]?.ToString() ?? "";
                return ValueTask.FromResult(default(TValue)!);
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}