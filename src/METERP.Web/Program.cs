using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Services;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// === Multi-tenancy + Current User ===
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, CurrentTenantProvider>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();

// === Database (PostgreSQL chosen for cost and scalability in a sellable multi-tenant ERP) ===
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Host=localhost;Database=METERP;Username=postgres;Password=postgres;Port=5432";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// === ASP.NET Core Identity + Multi-tenancy ===
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false; // Foundation: simplify for now
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Cookie authentication (suitable for Blazor Server)
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/access-denied";
});

// Authorization policies based on permissions (claim-based)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Tenants.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.TenantsManage));

    options.AddPolicy("Tenants.View", policy =>
        policy.RequireClaim("Permission", Permissions.TenantsView, Permissions.TenantsManage));

    // Customer module policies
    options.AddPolicy("Customers.View", policy =>
        policy.RequireClaim("Permission", Permissions.CustomersView, Permissions.CustomersManage));

    options.AddPolicy("Customers.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.CustomersManage));

    // Add more policies as modules are built
});

// For development/demo only: ensure DB + seed data
builder.Services.AddHostedService<DatabaseSeeder>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Seeds the database with a default tenant, roles with permissions, and a demo user.
/// This is for foundation/demo purposes only.
/// </summary>
public class DatabaseSeeder : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseSeeder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var tenantService = scope.ServiceProvider.GetRequiredService<ITenantService>();
        var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        // Ensure database exists (replace with migrations in real use)
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // 1. Create a default tenant if none exists
        var existingTenants = await tenantService.GetAllAsync(cancellationToken);
        Guid defaultTenantId;

        if (!existingTenants.Any())
        {
            // Temporarily set no tenant for creation
            tenantProvider.SetTenantId(Guid.Empty);
            defaultTenantId = await tenantService.CreateAsync("Acme Electrical (Demo)", "acme", cancellationToken);
        }
        else
        {
            defaultTenantId = existingTenants.First().Id;
        }

        // 2. Ensure roles with permissions exist for the tenant
        tenantProvider.SetTenantId(defaultTenantId);

        await EnsureRoleWithPermissions(roleManager, "Admin", new[]
        {
            Permissions.TenantsView,
            Permissions.TenantsManage,
            Permissions.CustomersView,
            Permissions.CustomersManage,
            Permissions.JobsView,
            Permissions.JobsManage
        }, defaultTenantId, cancellationToken);

        await EnsureRoleWithPermissions(roleManager, "Manager", new[]
        {
            Permissions.TenantsView,
            Permissions.CustomersView,
            Permissions.CustomersManage,
            Permissions.JobsView,
            Permissions.JobsManage
        }, defaultTenantId, cancellationToken);

        await EnsureRoleWithPermissions(roleManager, "Technician", new[]
        {
            Permissions.CustomersView,
            Permissions.JobsView
        }, defaultTenantId, cancellationToken);

        // 3. Create a demo admin user if none exists for this tenant
        var adminEmail = "admin@acme.demo";
        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin == null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                TenantId = defaultTenantId
            };

            var result = await userManager.CreateAsync(adminUser, "Demo123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                // Add explicit permission claims as well (defense in depth)
                await userManager.AddClaimAsync(adminUser, new System.Security.Claims.Claim("Permission", Permissions.TenantsManage));
                await userManager.AddClaimAsync(adminUser, new System.Security.Claims.Claim("Permission", Permissions.CustomersManage));
                // Add TenantId claim so CurrentTenantProvider can read it automatically after login
                await userManager.AddClaimAsync(adminUser, new System.Security.Claims.Claim("TenantId", defaultTenantId.ToString()));
            }
        }

        // 4. Seed some demo customers for the tenant (if none exist)
        var existingCustomers = await customerService.GetAllAsync(ct: cancellationToken);
        if (!existingCustomers.Any())
        {
            var cust1 = new Customer
            {
                Name = "Johannesburg General Hospital",
                Phone = "011 555 1234",
                Email = "procurement@jhgh.co.za",
                City = "Johannesburg",
                Province = "Gauteng",
                PostalCode = "2001"
            };
            await customerService.CreateAsync(cust1, cancellationToken);

            await customerService.AddContactAsync(new Contact
            {
                CustomerId = cust1.Id,
                FirstName = "Thabo",
                LastName = "Mokoena",
                JobTitle = "Procurement Manager",
                Phone = "011 555 1235",
                Email = "thabo.mokoena@jhgh.co.za",
                IsPrimary = true
            }, cancellationToken);

            var cust2 = new Customer
            {
                Name = "Cape Town Mining Ltd",
                Phone = "021 444 9876",
                Email = "ops@ctmining.co.za",
                City = "Cape Town",
                Province = "Western Cape"
            };
            await customerService.CreateAsync(cust2, cancellationToken);
        }
    }

    private async Task EnsureRoleWithPermissions(RoleManager<ApplicationRole> roleManager, string roleName, string[] permissions, Guid tenantId, CancellationToken ct)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null)
        {
            role = new ApplicationRole
            {
                Name = roleName,
                TenantId = tenantId,
                NormalizedName = roleName.ToUpper()
            };
            await roleManager.CreateAsync(role);
        }

        // Add permission claims to the role
        var existingClaims = await roleManager.GetClaimsAsync(role);
        foreach (var perm in permissions)
        {
            if (!existingClaims.Any(c => c.Type == "Permission" && c.Value == perm))
            {
                await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim("Permission", perm));
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
