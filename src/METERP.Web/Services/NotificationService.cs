using System.Text.Json;
using Microsoft.JSInterop;
using METERP.Web.Components.Pages; // for the internal Notification if needed, but we'll duplicate model for simplicity

namespace METERP.Web.Services;

/// <summary>
/// Simple notification service for demo (in-app alerts + persistence).
/// In production: use SignalR, database, email/SMS providers (SendGrid, Twilio), and tenant isolation.
/// </summary>
public class NotificationService
{
    private readonly IJSRuntime _js;
    private List<NotificationItem> _cache = new();

    public NotificationService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<List<NotificationItem>> GetAllAsync()
    {
        if (_cache.Count == 0)
        {
            try
            {
                var saved = await _js.InvokeAsync<string>("localStorage.getItem", "notifications");
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    _cache = JsonSerializer.Deserialize<List<NotificationItem>>(saved) ?? new();
                }
            }
            catch { }
        }
        return _cache.OrderByDescending(n => n.Date).ToList();
    }

    public async Task AddAsync(string title, string message, bool isRead = false)
    {
        var items = await GetAllAsync();
        var maxId = items.Any() ? items.Max(n => n.Id) : 0;
        var item = new NotificationItem
        {
            Id = maxId + 1,
            Title = title,
            Message = message,
            Date = DateTime.Now,
            IsRead = isRead
        };
        items.Insert(0, item);
        _cache = items;
        await SaveAsync();
    }

    public async Task MarkReadAsync(int id)
    {
        var items = await GetAllAsync();
        var item = items.FirstOrDefault(n => n.Id == id);
        if (item != null)
        {
            item.IsRead = true;
            await SaveAsync();
        }
    }

    public async Task MarkAllReadAsync()
    {
        var items = await GetAllAsync();
        foreach (var n in items) n.IsRead = true;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cache);
            await _js.InvokeVoidAsync("localStorage.setItem", "notifications", json);
        }
        catch { }
    }

    public class NotificationItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime Date { get; set; }
        public bool IsRead { get; set; }
    }
}
