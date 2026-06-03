using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Identity;

namespace METERP.Infrastructure.Persistence;

/// <summary>
/// The main EF Core DbContext for METERP.
/// Combines domain entities with ASP.NET Core Identity, all under multi-tenancy.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantProvider _tenantProvider;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantProvider tenantProvider) : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    // Platform / cross-cutting (non-Identity)
    public DbSet<Tenant> Tenants { get; set; } = null!;

    // Future module sets will be added here or via conventions
    // public DbSet<Customer> Customers { get; set; } = null!;

    public Guid CurrentTenantId => _tenantProvider.GetCurrentTenantId();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply any IEntityTypeConfiguration from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // === Multi-tenancy configuration ===

        // 1. BaseEntity descendants (business entities)
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(BaseEntity).IsAssignableFrom(e.ClrType) && e.ClrType != typeof(Tenant)))
        {
            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var tenantIdProperty = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseEntity.TenantId));
            var isDeletedProperty = System.Linq.Expressions.Expression.Property(parameter, nameof(BaseEntity.IsDeleted));

            var currentTenantId = System.Linq.Expressions.Expression.Property(
                System.Linq.Expressions.Expression.Constant(this),
                nameof(CurrentTenantId));

            var tenantFilter = System.Linq.Expressions.Expression.Equal(tenantIdProperty, currentTenantId);
            var notDeleted = System.Linq.Expressions.Expression.Equal(isDeletedProperty, System.Linq.Expressions.Expression.Constant(false));

            var combined = System.Linq.Expressions.Expression.AndAlso(tenantFilter, notDeleted);
            var lambda = System.Linq.Expressions.Expression.Lambda(combined, parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }

        // 2. Tenant entity: soft delete only (cross-tenant management allowed via IgnoreQueryFilters)
        modelBuilder.Entity<Tenant>()
            .HasQueryFilter(t => !t.IsDeleted);

        // 3. Identity users and roles - also tenant-isolated
        modelBuilder.Entity<ApplicationUser>()
            .HasQueryFilter(u => u.TenantId == CurrentTenantId);

        modelBuilder.Entity<ApplicationRole>()
            .HasQueryFilter(r => r.TenantId == CurrentTenantId);

        // Indexes
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(t => t.Subdomain).IsUnique();
        });

        // Configure Identity tables to include TenantId in keys where useful (optional but good for multi-tenant)
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => new { u.TenantId, u.UserName }).IsUnique();
        });
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenant();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenant();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditAndTenant()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        var currentTenant = _tenantProvider.GetCurrentTenantId();
        var now = DateTime.UtcNow;
        // TODO: Replace with real current user from ICurrentUserService / HttpContext
        const string currentUser = "system";

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.TenantId = currentTenant;
                entry.Entity.CreatedDate = now;
                entry.Entity.CreatedBy = currentUser;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.LastModifiedDate = now;
                entry.Entity.LastModifiedBy = currentUser;
            }
        }

        // Also stamp TenantId on new Identity users/roles if not set
        var userEntries = ChangeTracker.Entries<ApplicationUser>();
        foreach (var entry in userEntries.Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty))
        {
            entry.Entity.TenantId = currentTenant;
        }

        var roleEntries = ChangeTracker.Entries<ApplicationRole>();
        foreach (var entry in roleEntries.Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty))
        {
            entry.Entity.TenantId = currentTenant;
        }
    }
}
