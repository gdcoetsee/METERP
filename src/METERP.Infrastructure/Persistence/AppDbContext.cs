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
    private readonly ICurrentUserService _currentUserService;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantProvider tenantProvider,
        ICurrentUserService currentUserService) : base(options)
    {
        _tenantProvider = tenantProvider;
        _currentUserService = currentUserService;
    }

    // Platform / cross-cutting (non-Identity)
    public DbSet<Tenant> Tenants { get; set; } = null!;

    // Customer module (Module 1)
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Contact> Contacts { get; set; } = null!;

    // Quote -> Job workflow (Module 2)
    public DbSet<Quote> Quotes { get; set; } = null!;
    public DbSet<QuoteLine> QuoteLines { get; set; } = null!;
    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<JobCost> JobCosts { get; set; } = null!;
    public DbSet<JobCrewAssignment> JobCrewAssignments { get; set; } = null!;

    // Invoicing (completes sales flow)
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceLine> InvoiceLines { get; set; } = null!;

    // Inventory & Stock Transactions (Module 3)
    public DbSet<InventoryItem> InventoryItems { get; set; } = null!;
    public DbSet<StockTransaction> StockTransactions { get; set; } = null!;

    // Assets / Transformers (electrical contracting specific)
    public DbSet<Asset> Assets { get; set; } = null!;

    // Job Labor / Timesheets (deep job costing)
    public DbSet<JobLabor> JobLabors { get; set; } = null!;

    // Purchasing / Supply Chain (Phase 2 - closes inventory replenishment loop)
    public DbSet<Supplier> Suppliers { get; set; } = null!;
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;

    // Finance / Accounting (Phase 3 - minimal GL to support real costing + invoicing)
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<JournalEntry> JournalEntries { get; set; } = null!;
    public DbSet<JournalEntryLine> JournalEntryLines { get; set; } = null!;

    // HR / Payroll (Phase 4 - links labor to people)
    public DbSet<Employee> Employees { get; set; } = null!;

    // Sales Order (intermediate Quote -> SO -> Job per original roadmap)
    public DbSet<SalesOrder> SalesOrders { get; set; } = null!;
    public DbSet<SalesOrderLine> SalesOrderLines { get; set; } = null!;

    // CRM Opportunities + compliance audit trail
    public DbSet<Opportunity> Opportunities { get; set; } = null!;
    public DbSet<AuditLogEntry> AuditLogEntries { get; set; } = null!;
    public DbSet<ProcessedStripeWebhookEvent> ProcessedStripeWebhookEvents { get; set; } = null!;

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

        // 3. Identity users and roles - also tenant-isolated.
        // Allow CurrentTenantId == Guid.Empty (pre-login / anonymous) to bypass tenant filter
        // so that PasswordSignInAsync can find the user by email across tenants during login.
        // After successful sign-in the TenantId claim will set the real tenant for subsequent requests.
        modelBuilder.Entity<ApplicationUser>()
            .HasQueryFilter(u => CurrentTenantId == Guid.Empty || u.TenantId == CurrentTenantId);

        modelBuilder.Entity<ApplicationRole>()
            .HasQueryFilter(r => CurrentTenantId == Guid.Empty || r.TenantId == CurrentTenantId);

        // Indexes
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(t => t.Subdomain).IsUnique();
        });

        modelBuilder.Entity<ProcessedStripeWebhookEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId).HasMaxLength(128);
            entity.Property(e => e.EventType).HasMaxLength(128);
        });

        // Configure Identity tables to include TenantId in keys where useful (optional but good for multi-tenant)
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => new { u.TenantId, u.UserName }).IsUnique();
        });

        // Enable RowVersion as a true concurrency token for optimistic concurrency where BaseEntity is used
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(BaseEntity).IsAssignableFrom(e.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(BaseEntity.RowVersion))
                .IsRowVersion();
        }
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
        var currentUser = _currentUserService?.UserName
            ?? _currentUserService?.UserId?.ToString()
            ?? "system";

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
