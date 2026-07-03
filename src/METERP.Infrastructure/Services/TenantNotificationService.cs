using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace METERP.Infrastructure.Services;

public sealed class TenantNotificationService : ITenantNotificationService
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUser;

    public TenantNotificationService(
        AppDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<TenantNotification>> GetForCurrentUserAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var roles = await GetCurrentUserRolesAsync(ct);
        var all = await _dbContext.Set<TenantNotification>()
            .AsNoTracking()
            .OrderByDescending(n => n.CreatedDate)
            .ToListAsync(ct);

        return all
            .Where(n => IsVisibleToRoles(n.TargetRoles, roles))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        var roles = await GetCurrentUserRolesAsync(ct);
        var all = await _dbContext.Set<TenantNotification>().AsNoTracking().ToListAsync(ct);
        return all.Count(n => !n.IsRead && IsVisibleToRoles(n.TargetRoles, roles));
    }

    public async Task CreateAsync(TenantNotification notification, CancellationToken ct = default)
    {
        _dbContext.Set<TenantNotification>().Add(notification);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _dbContext.Set<TenantNotification>().FirstOrDefaultAsync(n => n.Id == id, ct);
        if (item == null) return;
        item.IsRead = true;
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var roles = await GetCurrentUserRolesAsync(ct);
        var items = await _dbContext.Set<TenantNotification>().ToListAsync(ct);
        foreach (var item in items.Where(n => !n.IsRead && IsVisibleToRoles(n.TargetRoles, roles)))
            item.IsRead = true;

        await _dbContext.SaveChangesAsync(ct);
    }

    private static bool IsVisibleToRoles(string targetRoles, IReadOnlyList<string> userRoles)
    {
        if (targetRoles == "*")
            return true;

        return userRoles.Any(r => targetRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(r, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<string>> GetCurrentUserRolesAsync(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Array.Empty<string>();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return Array.Empty<string>();

        var roles = await _userManager.GetRolesAsync(user);
        return roles.ToList();
    }
}