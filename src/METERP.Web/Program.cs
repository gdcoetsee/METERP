using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Serilog;
using Serilog.Events;
using METERP.Application.Integrations;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Integrations;
using METERP.Infrastructure.Seeding;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Services;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using METERP.Web.Components;
using METERP.Web.HealthChecks;
using METERP.Web.Middleware;
using METERP.Web.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Load repo-root .env in Development so `dotnet run` matches docker-compose Ai__* keys.
if (builder.Environment.IsDevelopment())
{
    var envPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".env"));
    if (File.Exists(envPath))
    {
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim().Trim('"');
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("METERP"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());

// === Structured logging (Serilog) — tenant id enriched via TenantLoggingMiddleware ===
const string logTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [Tenant:{TenantId}] {Message:lj}{NewLine}{Exception}";
var loggerConfiguration = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "METERP")
    .WriteTo.Console(outputTemplate: logTemplate)
    .WriteTo.File("logs/meterp-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: logTemplate);

var seqServerUrl = builder.Configuration["Seq:ServerUrl"];
if (!string.IsNullOrWhiteSpace(seqServerUrl))
{
    loggerConfiguration = loggerConfiguration.WriteTo.Seq(seqServerUrl);
}

Log.Logger = loggerConfiguration.CreateLogger();
builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMemoryCache();

// === Distributed cache (Redis when configured, else in-process memory for demo/local) ===
builder.Services.Configure<METERP.Application.Options.CacheOptions>(
    builder.Configuration.GetSection(METERP.Application.Options.CacheOptions.SectionName));
var redisConnection = builder.Configuration.GetSection(METERP.Application.Options.CacheOptions.SectionName)["RedisConnection"]
    ?? builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "meterp:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// === Multi-tenancy + Current User ===
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantCacheService, TenantDistributedCacheService>();
builder.Services.AddScoped<ITenantProvider, CurrentTenantProvider>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IQuotaService, QuotaService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IQuoteService, QuoteService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IPayrollService, PayrollService>();
builder.Services.AddScoped<IWorkforceReportService, WorkforceReportService>();
builder.Services.AddScoped<IJobReportService, JobReportService>();
builder.Services.AddScoped<ICashflowReportService, CashflowReportService>();
builder.Services.AddScoped<ITenantBillingViewService, TenantBillingViewService>();
builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
builder.Services.AddScoped<IOpportunityService, OpportunityService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITwoFactorAuthService, TwoFactorAuthService>();
builder.Services.AddSingleton<IPendingTwoFactorChallengeStore, PendingTwoFactorChallengeStore>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.SectionName));
builder.Services.AddHttpClient("integrations", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("stripe", client =>
{
    client.BaseAddress = new Uri("https://api.stripe.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IInvoiceIntegrationService, InvoiceIntegrationService>();
builder.Services.AddScoped<IBillingWebhookService, BillingWebhookService>();
builder.Services.AddScoped<IBillingWebhookMaintenanceService, BillingWebhookMaintenanceService>();
builder.Services.AddScoped<IStripeCustomerPortalClient, StripeCustomerPortalClient>();
builder.Services.AddScoped<IBillingPortalService, BillingPortalService>();
builder.Services.AddScoped<ISchedulingService, SchedulingService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<NotificationService>();

// === Health checks (production readiness) ===
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
    .AddCheck<AiConfigurationHealthCheck>("ai", tags: ["ready"]);

// === AI Assistant (optional - powers smart quoting & post-job learning) ===
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "dataprotection-keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
builder.Services.AddScoped<IAiConfigurationResolver, AiConfigurationResolver>();
builder.Services.AddScoped<ITenantAiSettingsService, TenantAiSettingsService>();
builder.Services.AddScoped<IAiAssistantService, AiAssistantService>();
builder.Services.AddScoped<IAiQuoteApplyService, AiQuoteApplyService>();
builder.Services.AddScoped<IAiJobApplyService, AiJobApplyService>();
builder.Services.AddScoped<IAiProjectPlanApplyService, AiProjectPlanApplyService>();

// === HTTP rate limiting (complements in-service AI throttle) ===
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please retry later.", token);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        if (httpContext.Request.Path.StartsWithSegments("/health")
            || httpContext.Request.Path.StartsWithSegments("/webhooks"))
            return RateLimitPartition.GetNoLimiter("health");

        // Tighter limit on AI Copilot page loads (complements in-service AI throttle).
        if (httpContext.Request.Path.StartsWithSegments("/ai-copilot"))
        {
            var aiKey = httpContext.User.Identity?.IsAuthenticated == true
                ? $"ai-user:{httpContext.User.Identity.Name}"
                : $"ai-ip:{httpContext.Connection.RemoteIpAddress}";

            return RateLimitPartition.GetFixedWindowLimiter(aiKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity.Name ?? "authenticated"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// === Database (PostgreSQL chosen for cost and scalability in a sellable multi-tenant ERP) ===
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Host=localhost;Database=METERP;Username=postgres;Password=CHANGE_ME;Port=5432";

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseInMemoryDatabase("meterp-integration-tests"));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString)
               .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));  // Dev: log instead of throw on pending migrations (common during feature dev). For prod, always add migration first.
}

// === ASP.NET Core Identity + Multi-tenancy ===
// (Serilog already configured above for structured + file logging. Rate limiter comment remains for AI cost control.)
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

    // Quote -> Job workflow policies (Module 2)
    options.AddPolicy("Quotes.View", policy =>
        policy.RequireClaim("Permission", Permissions.QuotesView, Permissions.QuotesManage));

    options.AddPolicy("Quotes.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.QuotesManage));

    options.AddPolicy("Jobs.View", policy =>
        policy.RequireClaim("Permission", Permissions.JobsView, Permissions.JobsManage));

    options.AddPolicy("Jobs.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.JobsManage));

    // User management (tenant-scoped)
    options.AddPolicy("Users.View", policy =>
        policy.RequireClaim("Permission", Permissions.UsersView, Permissions.UsersManage));

    options.AddPolicy("Users.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.UsersManage));

    // Invoicing policies
    options.AddPolicy("Invoices.View", policy =>
        policy.RequireClaim("Permission", Permissions.InvoicesView, Permissions.InvoicesManage));

    options.AddPolicy("Invoices.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.InvoicesManage));

    // Inventory policies
    options.AddPolicy("Inventory.View", policy =>
        policy.RequireClaim("Permission", Permissions.InventoryView, Permissions.InventoryManage));

    options.AddPolicy("Inventory.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.InventoryManage));

    // Assets policies
    options.AddPolicy("Assets.View", policy =>
        policy.RequireClaim("Permission", Permissions.AssetsView, Permissions.AssetsManage));

    options.AddPolicy("Assets.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.AssetsManage));

    // Purchasing policies (Phase 2)
    options.AddPolicy("Suppliers.View", policy =>
        policy.RequireClaim("Permission", Permissions.SuppliersView, Permissions.SuppliersManage));

    options.AddPolicy("Suppliers.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.SuppliersManage));

    options.AddPolicy("PurchaseOrders.View", policy =>
        policy.RequireClaim("Permission", Permissions.PurchaseOrdersView, Permissions.PurchaseOrdersManage));

    options.AddPolicy("PurchaseOrders.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.PurchaseOrdersManage));

    // Finance policies (Phase 3)
    options.AddPolicy("Finance.View", policy =>
        policy.RequireClaim("Permission", Permissions.FinanceView, Permissions.FinanceManage));

    options.AddPolicy("Finance.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.FinanceManage));

    // HR policies
    options.AddPolicy("Employees.View", policy =>
        policy.RequireClaim("Permission", Permissions.EmployeesView, Permissions.EmployeesManage));

    options.AddPolicy("Employees.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.EmployeesManage));

    // Sales Orders policies
    options.AddPolicy("SalesOrders.View", policy =>
        policy.RequireClaim("Permission", Permissions.SalesOrdersView, Permissions.SalesOrdersManage));

    options.AddPolicy("SalesOrders.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.SalesOrdersManage));

    options.AddPolicy("Opportunities.View", policy =>
        policy.RequireClaim("Permission", Permissions.OpportunitiesView, Permissions.OpportunitiesManage));

    options.AddPolicy("Opportunities.Manage", policy =>
        policy.RequireClaim("Permission", Permissions.OpportunitiesManage));

    options.AddPolicy("Audit.View", policy =>
        policy.RequireClaim("Permission", Permissions.AuditView, Permissions.TenantsManage));
});

// For development/demo only: ensure DB + seed data (idempotent by default).
// Safe by default (no destructive drops). Use METERP_SEED_RESET=true or config "Seed:ForceResetOnStart" for full reset after schema work.
// In production, disable the seeder (comment out or gate behind environment).
if (!builder.Environment.IsEnvironment("Testing"))
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
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantLoggingMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var tenantId = httpContext.RequestServices.GetService<ITenantProvider>()?.GetCurrentTenantId() ?? Guid.Empty;
        diagnosticContext.Set("TenantId", tenantId == Guid.Empty ? "none" : tenantId.ToString());
    };
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms [Tenant:{TenantId}]";
});

// === Health checks: /health = liveness, /health/ready = readiness (DB + AI probe) ===
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse
}).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthChecks.UI.Client.UIResponseWriter.WriteHealthCheckUIResponse
}).DisableRateLimiting();

if (app.Environment.IsDevelopment())
{
    app.MapPost("/e2e/ensure-receive-demo-po", async (IServiceProvider sp, CancellationToken ct) =>
    {
        using var scope = sp.CreateScope();
        var tenantService = scope.ServiceProvider.GetRequiredService<ITenantService>();
        var tenant = await tenantService.GetBySubdomainAsync("acme", ct);
        if (tenant == null)
            return Results.NotFound(new { error = "Acme demo tenant not found." });

        await E2EReceiveDemoPoSeeder.EnsureSentReceiveDemoPoAsync(
            scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>(),
            scope.ServiceProvider.GetRequiredService<ISupplierService>(),
            scope.ServiceProvider.GetRequiredService<IInventoryService>(),
            scope.ServiceProvider.GetRequiredService<ITenantProvider>(),
            tenant.Id,
            ct);

        return Results.Ok(new { ok = true });
    }).DisableRateLimiting();

    app.MapPost("/e2e/ensure-convertible-quote", async (IServiceProvider sp, CancellationToken ct) =>
    {
        using var scope = sp.CreateScope();
        var tenantService = scope.ServiceProvider.GetRequiredService<ITenantService>();
        var tenant = await tenantService.GetBySubdomainAsync("acme", ct);
        if (tenant == null)
            return Results.NotFound(new { error = "Acme demo tenant not found." });

        var quoteNumber = await E2EConvertibleQuoteSeeder.EnsureSentConvertibleQuoteAsync(
            scope.ServiceProvider.GetRequiredService<IQuoteService>(),
            scope.ServiceProvider.GetRequiredService<ICustomerService>(),
            scope.ServiceProvider.GetRequiredService<ITenantProvider>(),
            tenant.Id,
            ct);

        return Results.Ok(new { ok = true, quoteNumber });
    }).DisableRateLimiting();
}

app.MapPost("/webhooks/stripe", async (
    HttpContext httpContext,
    IBillingWebhookService billing,
    IHostEnvironment environment,
    IConfiguration configuration) =>
{
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();
    var signature = httpContext.Request.Headers["Stripe-Signature"].FirstOrDefault();
    var secretConfigured = !string.IsNullOrWhiteSpace(configuration[$"{BillingOptions.SectionName}:WebhookSecret"]);
    var allowUnsigned = !secretConfigured && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

    var result = await billing.ProcessStripeEventAsync(body, signature, allowUnsigned);

    return result.Outcome switch
    {
        BillingWebhookOutcome.InvalidSignature => Results.Unauthorized(),
        BillingWebhookOutcome.InvalidPayload => Results.BadRequest(new { error = result.Message }),
        _ => Results.Ok(new
        {
            received = true,
            outcome = result.Outcome.ToString(),
            detail = result.Message,
            tier = result.UpdatedTier?.ToString()
        })
    };
}).DisableRateLimiting();

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
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(IServiceProvider serviceProvider, ILogger<DatabaseSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var tenantService = scope.ServiceProvider.GetRequiredService<ITenantService>();
        var customerService = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var quoteService = scope.ServiceProvider.GetRequiredService<IQuoteService>();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();
        var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var assetService = scope.ServiceProvider.GetRequiredService<IAssetService>();
        var supplierService = scope.ServiceProvider.GetRequiredService<ISupplierService>();
        var purchaseOrderService = scope.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        var financeService = scope.ServiceProvider.GetRequiredService<IFinanceService>();
        var employeeService = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
        var salesOrderService = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var opportunityService = scope.ServiceProvider.GetRequiredService<IOpportunityService>();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        var env = scope.ServiceProvider.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>();

        // === Seeding strategy (Runnable & Demo-Ready) ===
        // By default: safe mode — just Migrate + seed missing data only. Never destructive on normal starts.
        // For major schema changes after code edits: set METERP_SEED_RESET=true (env var) or "Seed:ForceResetOnStart": true in config.
        // This keeps everyday `dotnet run` or docker-compose starts fast and non-destructive while preserving the powerful reset option.
        bool forceReset = string.Equals(Environment.GetEnvironmentVariable("METERP_SEED_RESET"), "true", StringComparison.OrdinalIgnoreCase)
                       || config.GetValue<bool>("Seed:ForceResetOnStart");

        // Dev-only robust reset (only when explicitly requested)
        if (forceReset && (env?.IsDevelopment() ?? true))
        {
            try
            {
                var rawCs = db.Database.GetConnectionString()
                            ?? "Host=localhost;Database=METERP_Dev;Username=postgres;Password=CHANGE_ME;Port=5432";
                var csb = new NpgsqlConnectionStringBuilder(rawCs);
                var targetDb = csb.Database ?? "METERP_Dev";

                // Maintenance connection to the always-present 'postgres' database
                var maintCsb = new NpgsqlConnectionStringBuilder(rawCs) { Database = "postgres" };
                await using var maint = new NpgsqlConnection(maintCsb.ConnectionString);
                await maint.OpenAsync(cancellationToken);

                var existsCmd = maint.CreateCommand();
                existsCmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @db";
                existsCmd.Parameters.AddWithValue("db", targetDb);
                var dbExists = (await existsCmd.ExecuteScalarAsync(cancellationToken)) != null;

                if (!dbExists)
                {
                    var createCmd = maint.CreateCommand();
                    createCmd.CommandText = $"CREATE DATABASE \"{targetDb}\" OWNER postgres";
                    await createCmd.ExecuteNonQueryAsync(cancellationToken);
                }
                await maint.CloseAsync();

                // Wipe whatever was there (or no-op) so the exact current migrations + full model apply cleanly, then seed.
                await db.Database.EnsureDeletedAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Non-fatal: surface in console so user sees why, then let Migrate surface the real problem if any.
                _logger.LogWarning(ex, "Dev DB reset note (only when METERP_SEED_RESET=true)");
            }
        }

        // Production-ready: Use migrations (now against a guaranteed clean DB in dev)
        await db.Database.MigrateAsync(cancellationToken);

        // 1. Create a default tenant if none exists
        var existingTenants = await tenantService.GetAllAsync(ct: cancellationToken);
        Guid defaultTenantId;

        if (!existingTenants.Any())
        {
            // Temporarily set no tenant for creation
            tenantProvider.SetTenantId(Guid.Empty);
            defaultTenantId = await tenantService.CreateAsync("Acme Electrical (Demo)", "acme", cancellationToken);
            // Demo: give the main tenant rich commercial features enabled
            var demoTenant = await tenantService.GetByIdAsync(defaultTenantId, cancellationToken);
            if (demoTenant != null)
            {
                demoTenant.Tier = SubscriptionTier.Professional;
                demoTenant.MaxQuotesPerMonth = 10_000;
                demoTenant.MaxJobsPerMonth = 10_000;
                demoTenant.MaxInvoicesPerMonth = 10_000;
                demoTenant.MaxAiCallsPerMonth = 10_000;
                demoTenant.MaxJobsPerMonth = null;
                demoTenant.MaxInvoicesPerMonth = null;
                demoTenant.MaxAiCallsPerMonth = null;
                demoTenant.EnabledFeatures = TenantQuotaDefaults.GetDefaultFeatures(SubscriptionTier.Professional) + ",compliance";
                demoTenant.StripeCustomerId ??= "cus_demo_acme";
                demoTenant.SubscriptionStatus ??= "active";
                await tenantService.UpdateAsync(demoTenant, cancellationToken);
            }
        }
        else
        {
            defaultTenantId = existingTenants.First().Id;
            var acme = await tenantService.GetBySubdomainAsync("acme", cancellationToken);
            if (acme != null)
            {
                if (acme.Tier != SubscriptionTier.Professional || acme.MaxQuotesPerMonth is null or < 10_000)
                {
                    acme.Tier = SubscriptionTier.Professional;
                    acme.MaxQuotesPerMonth = 10_000;
                    acme.MaxJobsPerMonth = 10_000;
                    acme.MaxInvoicesPerMonth = 10_000;
                    acme.MaxAiCallsPerMonth = 10_000;
                }

                if (string.IsNullOrWhiteSpace(acme.EnabledFeatures) || !acme.HasFeature("ai"))
                    acme.EnabledFeatures = TenantQuotaDefaults.GetDefaultFeatures(SubscriptionTier.Professional) + ",compliance";

                acme.StripeCustomerId ??= "cus_demo_acme";
                acme.SubscriptionStatus ??= "active";
                await tenantService.UpdateAsync(acme, cancellationToken);
            }
        }

        // 2. Ensure roles with permissions exist for the tenant
        tenantProvider.SetTenantId(defaultTenantId);

        await EnsureRoleWithPermissions(roleManager, tenantProvider, "Admin", new[]
        {
            Permissions.TenantsView,
            Permissions.TenantsManage,
            Permissions.CustomersView,
            Permissions.CustomersManage,
            Permissions.QuotesView,
            Permissions.QuotesManage,
            Permissions.JobsView,
            Permissions.JobsManage,
            Permissions.UsersView,
            Permissions.UsersManage,
            Permissions.InvoicesView,
            Permissions.InvoicesManage,
            Permissions.InventoryView,
            Permissions.InventoryManage,
            Permissions.AssetsView,
            Permissions.AssetsManage,
            Permissions.SuppliersView,
            Permissions.SuppliersManage,
            Permissions.PurchaseOrdersView,
            Permissions.PurchaseOrdersManage,
            Permissions.FinanceView,
            Permissions.FinanceManage,
            Permissions.EmployeesView,
            Permissions.EmployeesManage,
            Permissions.SalesOrdersView,
            Permissions.SalesOrdersManage,
            Permissions.OpportunitiesView,
            Permissions.OpportunitiesManage,
            Permissions.AuditView
        }, defaultTenantId, cancellationToken);

        await EnsureRoleWithPermissions(roleManager, tenantProvider, "Manager", new[]
        {
            Permissions.TenantsView,
            Permissions.CustomersView,
            Permissions.CustomersManage,
            Permissions.QuotesView,
            Permissions.QuotesManage,
            Permissions.JobsView,
            Permissions.JobsManage,
            Permissions.UsersView,
            Permissions.UsersManage,
            Permissions.InvoicesView,
            Permissions.InvoicesManage,
            Permissions.InventoryView,
            Permissions.InventoryManage,
            Permissions.AssetsView,
            Permissions.AssetsManage,
            Permissions.SuppliersView,
            Permissions.SuppliersManage,
            Permissions.PurchaseOrdersView,
            Permissions.PurchaseOrdersManage,
            Permissions.FinanceView,
            Permissions.FinanceManage,
            Permissions.EmployeesView,
            Permissions.EmployeesManage,
            Permissions.SalesOrdersView,
            Permissions.SalesOrdersManage,
            Permissions.OpportunitiesView,
            Permissions.OpportunitiesManage,
            Permissions.AuditView
        }, defaultTenantId, cancellationToken);

        await EnsureRoleWithPermissions(roleManager, tenantProvider, "Technician", new[]
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
                await AddUserToGlobalRoleAsync(userManager, tenantProvider, adminUser, "Admin");
                // Add explicit permission claims as well (defense in depth)
                await userManager.AddClaimAsync(adminUser, new System.Security.Claims.Claim("Permission", Permissions.TenantsManage));
                await userManager.AddClaimAsync(adminUser, new System.Security.Claims.Claim("Permission", Permissions.CustomersManage));
                // Add TenantId claim so CurrentTenantProvider can read it automatically after login
                await userManager.AddClaimAsync(adminUser, new System.Security.Claims.Claim("TenantId", defaultTenantId.ToString()));
            }
        }
        else
        {
            await SyncUserPermissionClaimsFromRoleAsync(userManager, roleManager, existingAdmin, "Admin", cancellationToken);
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

        // 4.5 Seed CRM opportunities (tenant-isolated pipeline)
        var existingOpportunities = await opportunityService.GetAllAsync(ct: cancellationToken);
        if (!existingOpportunities.Any())
        {
            var customers = await customerService.GetAllAsync(ct: cancellationToken);
            var hospital = customers.FirstOrDefault(c => c.Name.Contains("Hospital", StringComparison.OrdinalIgnoreCase))
                ?? customers.FirstOrDefault();
            var mining = customers.FirstOrDefault(c => c.Name.Contains("Mining", StringComparison.OrdinalIgnoreCase))
                ?? customers.Skip(1).FirstOrDefault();

            if (hospital != null)
            {
                await opportunityService.CreateAsync(new Opportunity
                {
                    Title = "Hospital DB Upgrade Phase 2",
                    CustomerId = hospital.Id,
                    CustomerName = hospital.Name,
                    Value = 185000m,
                    Stage = OpportunityStage.Proposal,
                    ExpectedClose = DateTime.UtcNow.AddDays(21),
                    Notes = "Follow-on from successful ward retrofit. Include travel to Sandton campus."
                }, cancellationToken);
            }

            if (mining != null)
            {
                await opportunityService.CreateAsync(new Opportunity
                {
                    Title = "Mine Substation Maintenance Contract",
                    CustomerId = mining.Id,
                    CustomerName = mining.Name,
                    Value = 92000m,
                    Stage = OpportunityStage.Qualified,
                    ExpectedClose = DateTime.UtcNow.AddDays(45),
                    Notes = "Annual maintenance + emergency call-out SLA."
                }, cancellationToken);
            }

            await opportunityService.CreateAsync(new Opportunity
            {
                Title = "Gauteng Power 11kV Install",
                CustomerName = "Gauteng Power",
                Value = 210000m,
                Stage = OpportunityStage.Lead,
                ExpectedClose = DateTime.UtcNow.AddDays(60),
                Notes = "Greenfield install — AI quote recommended for travel + materials."
            }, cancellationToken);
        }

        // 5. Seed demo quotes + one converted job to demonstrate the full Quote -> Job workflow (Module 2)
        var existingQuotes = await quoteService.GetAllAsync(ct: cancellationToken);
        if (!existingQuotes.Any())
        {
            var customers = await customerService.GetAllAsync(ct: cancellationToken);
            var cust = customers.FirstOrDefault();
            if (cust != null)
            {
                // Create a realistic quote for the first demo customer
                var q1 = new Quote
                {
                    CustomerId = cust.Id,
                    QuoteDate = DateTime.UtcNow.AddDays(-4),
                    ValidUntil = DateTime.UtcNow.AddDays(26),
                    Status = QuoteStatus.Sent,
                    Notes = "DB board upgrade + warehouse lighting retrofit. Includes supply, install, test & commission.",
                    TaxRate = 0.15m
                };

                var quoteId = await quoteService.CreateAsync(q1, cancellationToken);

                await quoteService.AddLineAsync(new QuoteLine
                {
                    QuoteId = quoteId,
                    Description = "DB board 12-way + breakers (complete)",
                    Quantity = 1,
                    UnitPrice = 2680m,
                    LineType = "Material",
                    Unit = "ea"
                }, cancellationToken);

                await quoteService.AddLineAsync(new QuoteLine
                {
                    QuoteId = quoteId,
                    Description = "Labour - 2 electricians x 8 hours install & testing",
                    Quantity = 16,
                    UnitPrice = 195m,
                    LineType = "Labour",
                    Unit = "hr"
                }, cancellationToken);

                await quoteService.AddLineAsync(new QuoteLine
                {
                    QuoteId = quoteId,
                    Description = "4mm SWA cable 50m + glands & accessories",
                    Quantity = 1,
                    UnitPrice = 875m,
                    LineType = "Material",
                    Unit = "lot"
                }, cancellationToken);

                await quoteService.AddLineAsync(new QuoteLine
                {
                    QuoteId = quoteId,
                    Description = "LED high-bay lights 150W (x8)",
                    Quantity = 8,
                    UnitPrice = 420m,
                    LineType = "Material",
                    Unit = "ea"
                }, cancellationToken);

                await quoteService.AddLineAsync(new QuoteLine
                {
                    QuoteId = quoteId,
                    Description = "Travel & site transport (explicit contractor cost)",
                    Quantity = 1,
                    UnitPrice = 620m,
                    LineType = "Other",
                    Unit = "lot"
                }, cancellationToken);

                // Demo Sales Order (Quote -> SO -> Job per original vision)
                var so = new SalesOrder
                {
                    QuoteId = quoteId,
                    CustomerId = cust.Id,
                    SoDate = DateTime.UtcNow.AddDays(-3),
                    DeliveryDate = DateTime.UtcNow.AddDays(4),
                    Status = SalesOrderStatus.Confirmed,
                    TaxRate = 0.15m
                };
                var soId = await salesOrderService.CreateAsync(so, cancellationToken);

                // Copy lines for demo (simplified)
                foreach (var ql in (await quoteService.GetByIdAsync(quoteId, cancellationToken))?.Lines ?? new List<QuoteLine>())
                {
                    if (!ql.IsDeleted)
                    {
                        await salesOrderService.AddLineAsync(new SalesOrderLine
                        {
                            SalesOrderId = soId,
                            Description = ql.Description,
                            Quantity = ql.Quantity,
                            UnitPrice = ql.UnitPrice,
                            Unit = ql.Unit,
                            LineType = ql.LineType
                        }, cancellationToken);
                    }
                }

                // Convert to Job from SO (demonstrates Quote -> SO -> Job flow)
                var createdJob = await salesOrderService.ConvertToJobAsync(soId, cancellationToken);

                // Record some actual costs to show variance tracking
                await jobService.AddCostAsync(new JobCost
                {
                    JobId = createdJob.Id,
                    Description = "DB board + breakers (supplier invoice)",
                    Amount = 2745m,
                    CostType = "Material",
                    CostDate = DateTime.UtcNow.AddDays(-2)
                }, cancellationToken);

                await jobService.AddCostAsync(new JobCost
                {
                    JobId = createdJob.Id,
                    Description = "Electricians - actual hours logged",
                    Amount = 3120m,
                    CostType = "Labour",
                    CostDate = DateTime.UtcNow.AddDays(-1)
                }, cancellationToken);

                await jobService.AddCostAsync(new JobCost
                {
                    JobId = createdJob.Id,
                    Description = "Site consumables & testing equipment",
                    Amount = 285m,
                    CostType = "Other",
                    CostDate = DateTime.UtcNow
                }, cancellationToken);

                // Explicit Travel / Transport cost (common in contracting jobs)
                await jobService.AddCostAsync(new JobCost
                {
                    JobId = createdJob.Id,
                    Description = "Travel & transport (van + fuel for crew)",
                    Amount = 620m,
                    CostType = "Travel",
                    CostDate = DateTime.UtcNow.AddDays(-2)
                }, cancellationToken);

                // Put the job into progress for demo
                createdJob.Notes = E2EDemoInvoiceJobSeeder.DemoNotesMarker;
                await jobService.UpdateAsync(createdJob, cancellationToken);
                await jobService.UpdateStatusAsync(createdJob.Id, JobStatus.InProgress, cancellationToken);

                // Add sample labor entries
                await jobService.AddLaborAsync(new JobLabor
                {
                    JobId = createdJob.Id,
                    WorkDate = DateTime.UtcNow.AddDays(-2),
                    Hours = 8,
                    HourlyRate = 195,
                    Description = "Installation and testing",
                    Technician = "Thabo Mokoena"
                }, cancellationToken);

                await jobService.AddLaborAsync(new JobLabor
                {
                    JobId = createdJob.Id,
                    WorkDate = DateTime.UtcNow.AddDays(-1),
                    Hours = 4,
                    HourlyRate = 210,
                    Description = "Commissioning and handover",
                    Technician = "Johan van der Berg"
                }, cancellationToken);

                // Create a demo invoice from the job (completes Quote -> Job -> Invoice flow)
                try
                {
                    var demoInvoice = await invoiceService.CreateFromJobAsync(createdJob.Id, cancellationToken);
                    await invoiceService.UpdateStatusAsync(demoInvoice.Id, InvoiceStatus.Sent, cancellationToken);
                }
                catch { /* non-fatal for demo seeding */ }

                // Seed realistic commercial usage data for demo (ties into tracking + flags)
                try
                {
                    var demoTenant = await tenantService.GetByIdAsync(defaultTenantId, cancellationToken);
                    if (demoTenant != null)
                    {
                        demoTenant.TotalJobsCreated = Math.Max(demoTenant.TotalJobsCreated, 4);
                        demoTenant.TotalQuotesCreated = Math.Max(demoTenant.TotalQuotesCreated, 3);
                        demoTenant.TotalInvoicesIssued = Math.Max(demoTenant.TotalInvoicesIssued, 1);
                        demoTenant.TotalRevenueBilled = Math.Max(demoTenant.TotalRevenueBilled, 8500m);
                        demoTenant.TotalAiCalls = Math.Max(demoTenant.TotalAiCalls, 8);
                        demoTenant.LastActivityUtc = DateTime.UtcNow;
                        await tenantService.UpdateAsync(demoTenant, cancellationToken);
                    }
                }
                catch { /* non-fatal */ }

                // Seed some inventory items + a stock issue to the job for realism
                var existingItems = await inventoryService.GetAllItemsAsync(ct: cancellationToken);
                if (!existingItems.Any())
                {
                    var dbBoard = new InventoryItem
                    {
                        Sku = "DB-12W-001",
                        Name = "DB Board 12-Way with Breakers",
                        Description = "Complete distribution board",
                        Unit = "ea",
                        QuantityOnHand = 12,
                        ReorderLevel = 5,
                        UnitCost = 2450m,
                        Category = "Electrical"
                    };
                    await inventoryService.CreateItemAsync(dbBoard, cancellationToken);

                    var cable = new InventoryItem
                    {
                        Sku = "CABLE-4MM-50",
                        Name = "4mm SWA Cable 50m",
                        Unit = "roll",
                        QuantityOnHand = 8,
                        ReorderLevel = 3,
                        UnitCost = 875m,
                        Category = "Electrical"
                    };
                    await inventoryService.CreateItemAsync(cable, cancellationToken);

                    var ledLight = new InventoryItem
                    {
                        Sku = "LED-HB-150",
                        Name = "LED High-Bay 150W",
                        Unit = "ea",
                        QuantityOnHand = 25,
                        ReorderLevel = 10,
                        UnitCost = 420m,
                        Category = "Lighting"
                    };
                    await inventoryService.CreateItemAsync(ledLight, cancellationToken);

                    var transformerOil = new InventoryItem
                    {
                        Sku = "OIL-TR-5L",
                        Name = "Transformer Oil 5L",
                        Unit = "ea",
                        QuantityOnHand = 2,
                        ReorderLevel = 5,
                        UnitCost = 185m,
                        Category = "Consumables"
                    };
                    await inventoryService.CreateItemAsync(transformerOil, cancellationToken);

                    // Record some stock usage against the created job
                    await inventoryService.RecordStockTransactionAsync(dbBoard.Id, -1, StockTransactionType.Issue, createdJob.JobNumber, createdJob.Id, "Used on demo job", cancellationToken);
                    await inventoryService.RecordStockTransactionAsync(cable.Id, -1, StockTransactionType.Issue, createdJob.JobNumber, createdJob.Id, "Used on demo job", cancellationToken);

                    // Seed demo assets (Transformers etc.) for the customer
                    var existingAssets = await assetService.GetAllAsync(ct: cancellationToken);
                    if (!existingAssets.Any())
                    {
                        await assetService.CreateAsync(new Asset
                        {
                            CustomerId = cust.Id,
                            Name = "Main 11kV/400V Transformer - Hospital Substation",
                            SerialNumber = "TRF-88421-2019",
                            AssetType = "Transformer",
                            Location = "Johannesburg General Hospital - Main Sub",
                            RatedKVA = 1250,
                            Voltage = "11kV / 400V",
                            CommissionedDate = new DateTime(2019, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                            Status = AssetStatus.Operational,
                            Notes = "Annual oil test due Q3 2026"
                        }, cancellationToken);

                        await assetService.CreateAsync(new Asset
                        {
                            CustomerId = cust.Id,
                            Name = "Warehouse LV Distribution Board",
                            SerialNumber = "DB-LV-0032",
                            AssetType = "Switchgear",
                            Location = "Cape Town Mining Ltd - Warehouse A",
                            CommissionedDate = new DateTime(2022, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                            Status = AssetStatus.Operational
                        }, cancellationToken);

                        // Link first asset to the job for demo
                        var linkableAssets = await assetService.GetAllAsync(ct: cancellationToken);
                        if (linkableAssets.Any())
                        {
                            createdJob.AssetId = linkableAssets.First().Id;
                            await jobService.UpdateAsync(createdJob, cancellationToken);
                        }

                        // Seed suppliers + PO (Phase 2 Purchasing) to demonstrate replenishment -> inventory -> job use
                        var existingSuppliers = await supplierService.GetAllAsync(ct: cancellationToken);
                        if (!existingSuppliers.Any())
                        {
                            var supplier = new Supplier
                            {
                                Name = "ElectroSupply SA (Pty) Ltd",
                                ContactPerson = "Sipho Dlamini",
                                Phone = "011 555 9876",
                                Email = "orders@electrosupply.co.za",
                                City = "Johannesburg",
                                Province = "Gauteng",
                                TaxNumber = "9876543210"
                            };
                            await supplierService.CreateAsync(supplier, cancellationToken);

                            var po = new PurchaseOrder
                            {
                                SupplierId = supplier.Id,
                                PoDate = DateTime.UtcNow.AddDays(-10),
                                ExpectedDate = DateTime.UtcNow.AddDays(-3),
                                Status = PurchaseOrderStatus.Received,
                                TaxRate = 0.15m
                            };
                            var poId = await purchaseOrderService.CreateAsync(po, cancellationToken);

                            // PO line that will be "received" into inventory (links to existing seeded dbBoard item)
                            var dbBoardItem = (await inventoryService.GetAllItemsAsync(ct: cancellationToken))
                                .FirstOrDefault(i => i.Sku == "DB-12W-001");
                            if (dbBoardItem != null)
                            {
                                await purchaseOrderService.AddLineAsync(new PurchaseOrderLine
                                {
                                    PurchaseOrderId = poId,
                                    InventoryItemId = dbBoardItem.Id,
                                    Description = dbBoardItem.Name,
                                    Quantity = 5,
                                    UnitPrice = dbBoardItem.UnitCost,
                                    Unit = dbBoardItem.Unit
                                }, cancellationToken);

                                // Simulate receipt (updates stock + transaction)
                                await inventoryService.RecordStockTransactionAsync(
                                    dbBoardItem.Id,
                                    5,
                                    StockTransactionType.Receipt,
                                    po.PoNumber,
                                    null,
                                    "PO receipt from " + supplier.Name,
                                    cancellationToken);
                            }
                        }

                        // Seed basic CoA + sample journal (Phase 3 Finance) so costing has GL visibility
                        var existingAccounts = await financeService.GetAccountsAsync(ct: cancellationToken);
                        if (!existingAccounts.Any())
                        {
                            var revenue = new Account { AccountCode = "4000", Name = "Revenue - Contracting", Type = AccountType.Revenue };
                            await financeService.CreateAccountAsync(revenue, cancellationToken);

                            var ar = new Account { AccountCode = "1100", Name = "Accounts Receivable", Type = AccountType.Asset };
                            await financeService.CreateAccountAsync(ar, cancellationToken);

                            var expenseMat = new Account { AccountCode = "5000", Name = "Materials & Supplies", Type = AccountType.Expense };
                            await financeService.CreateAccountAsync(expenseMat, cancellationToken);

                            var expenseLabor = new Account { AccountCode = "5100", Name = "Direct Labor", Type = AccountType.Expense };
                            await financeService.CreateAccountAsync(expenseLabor, cancellationToken);

                            var expenseTravel = new Account { AccountCode = "5200", Name = "Travel & Transport", Type = AccountType.Expense };
                            await financeService.CreateAccountAsync(expenseTravel, cancellationToken);

                            // Sample journal: recognize revenue + AR from the demo invoice (balanced double-entry)
                            var demoInvoiceForJournal = (await invoiceService.GetAllAsync(ct: cancellationToken)).FirstOrDefault();
                            if (demoInvoiceForJournal != null)
                            {
                                var revenueAccount = await db.Set<Account>()
                                    .FirstAsync(a => a.AccountCode == "4000", cancellationToken);
                                var arAccount = await db.Set<Account>()
                                    .FirstAsync(a => a.AccountCode == "1100", cancellationToken);

                                var je = new JournalEntry
                                {
                                    EntryDate = DateTime.UtcNow,
                                    Description = "Demo invoice revenue recognition",
                                    Reference = demoInvoiceForJournal.InvoiceNumber,
                                    JobId = demoInvoiceForJournal.JobId,
                                    Lines = new List<JournalEntryLine>
                                    {
                                        new()
                                        {
                                            AccountId = arAccount.Id,
                                            Debit = demoInvoiceForJournal.Total,
                                            Memo = "AR from demo invoice"
                                        },
                                        new()
                                        {
                                            AccountId = revenueAccount.Id,
                                            Credit = demoInvoiceForJournal.Total,
                                            Memo = "Contracting revenue"
                                        }
                                    }
                                };
                                _ = await financeService.PostJournalAsync(je, cancellationToken);
                            }
                        }

                        // Seed employees (Phase 4 HR) and link sample labor rates
                        var existingEmps = await employeeService.GetAllAsync(ct: cancellationToken);
                        if (!existingEmps.Any())
                        {
                            await employeeService.CreateAsync(new Employee
                            {
                                EmployeeNumber = "EMP-001",
                                FirstName = "Thabo",
                                LastName = "Mokoena",
                                JobTitle = "Electrician",
                                DefaultHourlyRate = 195
                            }, cancellationToken);

                            await employeeService.CreateAsync(new Employee
                            {
                                EmployeeNumber = "EMP-002",
                                FirstName = "Johan",
                                LastName = "van der Berg",
                                JobTitle = "Technician",
                                DefaultHourlyRate = 210
                            }, cancellationToken);
                        }
                    }
                }
            }
        }

        // Idempotent second supplier for suppliers search filter E2E.
        tenantProvider.SetTenantId(defaultTenantId);
        if (!(await supplierService.GetAllAsync(ct: cancellationToken)).Any(s => s.Name.Contains("Panel Supplies", StringComparison.OrdinalIgnoreCase)))
        {
            await supplierService.CreateAsync(new Supplier
            {
                Name = "Panel Supplies CC",
                ContactPerson = "Thabo Mokoena",
                Phone = "011 555 1122",
                Email = "orders@panelsupplies.test",
                City = "Pretoria",
                Province = "Gauteng"
            }, cancellationToken);
        }

        tenantProvider.SetTenantId(defaultTenantId);
        await E2EReceiveDemoPoSeeder.EnsureSentReceiveDemoPoAsync(
            purchaseOrderService,
            supplierService,
            inventoryService,
            tenantProvider,
            defaultTenantId,
            cancellationToken);

        await E2EDemoInvoiceJobSeeder.EnsureDemoInvoiceJobTaggedAsync(jobService, cancellationToken);

        // Idempotent low-stock demo item (inventory filter E2E + low-stock alerts).
        tenantProvider.SetTenantId(defaultTenantId);
        if (!(await inventoryService.GetAllItemsAsync(ct: cancellationToken)).Any(i => i.Sku == "OIL-TR-5L"))
        {
            await inventoryService.CreateItemAsync(new InventoryItem
            {
                Sku = "OIL-TR-5L",
                Name = "Transformer Oil 5L",
                Unit = "ea",
                QuantityOnHand = 2,
                ReorderLevel = 5,
                UnitCost = 185m,
                Category = "Consumables"
            }, cancellationToken);
        }

        // Backfill demo GL journal when CoA exists from older seeds but no exportable lines (Finance export E2E).
        tenantProvider.SetTenantId(defaultTenantId);
        if (!await db.Set<JournalEntryLine>().AnyAsync(l => !l.IsDeleted, cancellationToken))
        {
            var seededAccounts = await financeService.GetAccountsAsync(ct: cancellationToken);
            if (seededAccounts.Any())
            {
                var demoInvoiceForJournal = (await invoiceService.GetAllAsync(ct: cancellationToken)).FirstOrDefault();
                if (demoInvoiceForJournal != null)
                {
                    var revenueAccount = await db.Set<Account>()
                        .FirstAsync(a => a.AccountCode == "4000", cancellationToken);
                    var arAccount = await db.Set<Account>()
                        .FirstAsync(a => a.AccountCode == "1100", cancellationToken);

                    var je = new JournalEntry
                    {
                        EntryDate = DateTime.UtcNow,
                        Description = "Demo invoice revenue recognition",
                        Reference = demoInvoiceForJournal.InvoiceNumber,
                        JobId = demoInvoiceForJournal.JobId,
                        Lines = new List<JournalEntryLine>
                        {
                            new()
                            {
                                AccountId = arAccount.Id,
                                Debit = demoInvoiceForJournal.Total,
                                Memo = "AR from demo invoice"
                            },
                            new()
                            {
                                AccountId = revenueAccount.Id,
                                Credit = demoInvoiceForJournal.Total,
                                Memo = "Contracting revenue"
                            }
                        }
                    };
                    _ = await financeService.PostJournalAsync(je, cancellationToken);
                }
            }
        }

        // Backfill JobLabor.EmployeeId from Technician + seeded employees (crew/payroll linkage).
        tenantProvider.SetTenantId(defaultTenantId);
        var seededEmployees = await employeeService.GetAllAsync(ct: cancellationToken);
        if (seededEmployees.Any())
        {
            var unlinkedLabor = await db.Set<JobLabor>()
                .IgnoreQueryFilters()
                .Where(l => l.TenantId == defaultTenantId && !l.IsDeleted && l.EmployeeId == null && l.Technician != null)
                .ToListAsync(cancellationToken);

            var laborLinked = false;
            foreach (var labor in unlinkedLabor)
            {
                var match = seededEmployees.FirstOrDefault(e =>
                    labor.Technician!.Contains(e.FirstName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    labor.EmployeeId = match.Id;
                    laborLinked = true;
                }
            }

            if (laborLinked)
                await db.SaveChangesAsync(cancellationToken);
        }

        // Optional large dataset for performance demos (Seed:LargeDataset or METERP_SEED_LARGE=true)
        await LargeDatasetSeeder.SeedAsync(_serviceProvider, defaultTenantId, cancellationToken);

        // 6. Create an additional demo user (Manager role) so the Users page has something interesting to show
        var managerEmail = "manager@acme.demo";
        var existingManager = await userManager.FindByEmailAsync(managerEmail);
        if (existingManager == null)
        {
            var managerUser = new ApplicationUser
            {
                UserName = managerEmail,
                Email = managerEmail,
                EmailConfirmed = true,
                TenantId = defaultTenantId
            };

            var mgrResult = await userManager.CreateAsync(managerUser, "Demo123!");
            if (mgrResult.Succeeded)
            {
                await AddUserToGlobalRoleAsync(userManager, tenantProvider, managerUser, "Manager");
                await userManager.AddClaimAsync(managerUser, new System.Security.Claims.Claim("TenantId", defaultTenantId.ToString()));
                // Manager permissions are already granted via the role claims in the role setup above
            }
        }

        // === Beta tenant for multi-tenant E2E isolation (safe: only creates when missing) ===
        tenantProvider.SetTenantId(Guid.Empty);
        var betaTenant = await tenantService.GetBySubdomainAsync("beta", cancellationToken);
        Guid betaTenantId;
        if (betaTenant == null)
        {
            betaTenantId = await tenantService.CreateAsync("Beta Corp (Demo)", "beta", cancellationToken);
            var beta = await tenantService.GetByIdAsync(betaTenantId, cancellationToken);
            if (beta != null)
            {
                beta.Tier = SubscriptionTier.Starter;
                beta.EnabledFeatures = "ai,usage-tracking";
                await tenantService.UpdateAsync(beta, cancellationToken);
            }
        }
        else
        {
            betaTenantId = betaTenant.Id;
            if (betaTenant.Tier != SubscriptionTier.Starter)
            {
                betaTenant.Tier = SubscriptionTier.Starter;
                betaTenant.EnabledFeatures = "ai,usage-tracking";
                await tenantService.UpdateAsync(betaTenant, cancellationToken);
            }
        }

        // Reuse global Admin role (Identity RoleNameIndex is unique; tenant isolation is via user.TenantId claim).
        tenantProvider.SetTenantId(betaTenantId);

        var betaAdminEmail = "admin@beta.demo";
        var existingBetaAdmin = await userManager.FindByEmailAsync(betaAdminEmail);
        ApplicationUser? betaAdminUser = existingBetaAdmin;
        if (existingBetaAdmin == null)
        {
            betaAdminUser = new ApplicationUser
            {
                UserName = betaAdminEmail,
                Email = betaAdminEmail,
                EmailConfirmed = true,
                TenantId = betaTenantId
            };
            var betaResult = await userManager.CreateAsync(betaAdminUser, "Demo123!");
            if (!betaResult.Succeeded)
                betaAdminUser = null;
        }

        if (betaAdminUser != null)
        {
            var betaClaims = await userManager.GetClaimsAsync(betaAdminUser);
            if (!betaClaims.Any(c => c.Type == "TenantId" && c.Value == betaTenantId.ToString()))
            {
                await userManager.AddClaimAsync(betaAdminUser, new System.Security.Claims.Claim("TenantId", betaTenantId.ToString()));
            }

            if (!await IsInGlobalRoleAsync(userManager, tenantProvider, betaAdminUser, "Admin"))
                await AddUserToGlobalRoleAsync(userManager, tenantProvider, betaAdminUser, "Admin");

            await SyncUserPermissionClaimsFromRoleAsync(userManager, roleManager, betaAdminUser, "Admin", cancellationToken);
            tenantProvider.SetTenantId(betaTenantId);
        }

        var betaCustomers = await customerService.GetAllAsync(ct: cancellationToken);
        if (!betaCustomers.Any())
        {
            await customerService.CreateAsync(new Customer
            {
                Name = "Beta Mining Services",
                Email = "ops@betamining.demo",
                City = "Pretoria",
                Province = "Gauteng"
            }, cancellationToken);
        }

        var betaQuotes = await quoteService.GetAllAsync(ct: cancellationToken);
        if (!betaQuotes.Any())
        {
            var betaCust = (await customerService.GetAllAsync(ct: cancellationToken)).First();
            var betaQuoteId = await quoteService.CreateAsync(new Quote
            {
                CustomerId = betaCust.Id,
                QuoteDate = DateTime.UtcNow.AddDays(-2),
                ValidUntil = DateTime.UtcNow.AddDays(28),
                Status = QuoteStatus.Sent,
                Notes = "Beta tenant isolated quote — panel upgrade.",
                TaxRate = 0.15m
            }, cancellationToken);
            await quoteService.AddLineAsync(new QuoteLine
            {
                QuoteId = betaQuoteId,
                Description = "Beta-only travel allowance",
                Quantity = 1,
                UnitPrice = 400m,
                LineType = "Other",
                Unit = "lot"
            }, cancellationToken);
        }

        // Ensure Acme quotes include explicit travel line (E2E + demo; safe patch for existing DBs).
        tenantProvider.SetTenantId(defaultTenantId);
        var acmeQuotes = await quoteService.GetAllAsync(ct: cancellationToken);
        foreach (var q in acmeQuotes)
        {
            var full = await quoteService.GetByIdAsync(q.Id, cancellationToken);
            if (full?.Lines.Any(l => !l.IsDeleted && l.Description.Contains("Travel", StringComparison.OrdinalIgnoreCase)) == true)
                break;

            if (full != null)
            {
                await quoteService.AddLineAsync(new QuoteLine
                {
                    QuoteId = full.Id,
                    Description = "Travel & site transport (explicit contractor cost)",
                    Quantity = 1,
                    UnitPrice = 620m,
                    LineType = "Other",
                    Unit = "lot"
                }, cancellationToken);
            }
        }

        tenantProvider.SetTenantId(defaultTenantId);

        await PurgeStaleBillingWebhookEventsAsync(scope.ServiceProvider, config, _logger, cancellationToken);
    }

    private static async Task PurgeStaleBillingWebhookEventsAsync(
        IServiceProvider scopedProvider,
        IConfiguration config,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken ct)
    {
        var retentionDays = config.GetSection(BillingOptions.SectionName).GetValue<int?>("WebhookEventRetentionDays") ?? 90;
        if (retentionDays <= 0)
            return;

        var maintenance = scopedProvider.GetRequiredService<IBillingWebhookMaintenanceService>();
        var removed = await maintenance.PurgeProcessedEventsOlderThanAsync(TimeSpan.FromDays(retentionDays), ct);
        if (removed > 0)
            logger.LogInformation("Purged {Count} processed Stripe webhook events older than {Days} days", removed, retentionDays);
    }

    /// <summary>
    /// Identity role names are globally unique (RoleNameIndex). Use Guid.Empty tenant so role lookup
    /// is not blocked by per-tenant query filters during seeding.
    /// </summary>
    private static async Task AddUserToGlobalRoleAsync(
        UserManager<ApplicationUser> userManager,
        ITenantProvider tenantProvider,
        ApplicationUser user,
        string roleName)
    {
        var previousTenant = tenantProvider.GetCurrentTenantId();
        tenantProvider.SetTenantId(Guid.Empty);
        try
        {
            if (!await userManager.IsInRoleAsync(user, roleName))
                await userManager.AddToRoleAsync(user, roleName);
        }
        finally
        {
            tenantProvider.SetTenantId(previousTenant);
        }
    }

    private static async Task<bool> IsInGlobalRoleAsync(
        UserManager<ApplicationUser> userManager,
        ITenantProvider tenantProvider,
        ApplicationUser user,
        string roleName)
    {
        var previousTenant = tenantProvider.GetCurrentTenantId();
        tenantProvider.SetTenantId(Guid.Empty);
        try
        {
            return await userManager.IsInRoleAsync(user, roleName);
        }
        finally
        {
            tenantProvider.SetTenantId(previousTenant);
        }
    }

    /// <summary>
    /// Copies missing Permission claims from a global role onto an existing user (safe on every startup).
    /// Keeps demo/E2E users current when new permissions are added to roles.
    /// </summary>
    private static async Task SyncUserPermissionClaimsFromRoleAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationUser user,
        string roleName,
        CancellationToken ct)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null) return;

        var roleClaims = await roleManager.GetClaimsAsync(role);
        var userClaims = await userManager.GetClaimsAsync(user);
        foreach (var claim in roleClaims.Where(c => c.Type == "Permission"))
        {
            if (!userClaims.Any(c => c.Type == claim.Type && c.Value == claim.Value))
                await userManager.AddClaimAsync(user, claim);
        }
    }

    private async Task EnsureRoleWithPermissions(
        RoleManager<ApplicationRole> roleManager,
        ITenantProvider tenantProvider,
        string roleName,
        string[] permissions,
        Guid tenantId,
        CancellationToken ct)
    {
        var previousTenant = tenantProvider.GetCurrentTenantId();
        tenantProvider.SetTenantId(Guid.Empty);

        ApplicationRole? role;
        try
        {
            role = await roleManager.FindByNameAsync(roleName);
            if (role == null)
            {
                role = new ApplicationRole
                {
                    Name = roleName,
                    TenantId = Guid.Empty,
                    NormalizedName = roleName.ToUpperInvariant()
                };
                await roleManager.CreateAsync(role);
            }
        }
        finally
        {
            tenantProvider.SetTenantId(previousTenant);
        }

        if (role == null) return;

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
