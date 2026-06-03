using System.Reflection;
using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;

namespace METERP.Infrastructure.Persistence;

/// <summary>
/// The main EF Core DbContext for METERP.
/// Includes multi-tenancy via global query filters on BaseEntity.
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantProvider tenantProvider) : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    // Platform / cross-cutting
    public DbSet<Tenant> Tenants { get; set; } = null!;

    // Example future module sets will be added here or via conventions
    // public DbSet<Customer> Customers { get; set; } = null!;

    public Guid CurrentTenantId => _tenantProvider.GetCurrentTenantId();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply any IEntityTypeConfiguration from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Multi-tenancy + soft delete global query filter
        // Applied to all entities inheriting from BaseEntity.
        // The filter uses the live value from CurrentTenantId on this scoped DbContext instance.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(BaseEntity).IsAssignableFrom(e.ClrType) && e.ClrType != typeof(Tenant)))
        {
            // Apply filter: TenantId matches current + not soft-deleted
            // Note: We exclude Tenant itself so we can query/manage tenants cross-tenant when needed.
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

        // Tenant entity: we still want soft delete but allow querying without strict tenant filter
        // (or apply a different rule). For foundation we leave it filter-free here.
        modelBuilder.Entity<Tenant>()
            .HasQueryFilter(t => !t.IsDeleted);

        // Additional configuration (indexes, etc.) can go here or in separate Configuration classes
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(t => t.Subdomain).IsUnique();
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
        // In real app, get current user from HttpContext or ClaimsPrincipal
        const string currentUser = "system"; // TODO: replace with real user resolution

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
    }
}
