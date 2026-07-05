using System.Net;
using System.Security.Claims;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace METERP.Web.Tests;

public class ForbiddenAccessEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly MeterpWebApplicationFactory _factory;

    public ForbiddenAccessEndpointTests(MeterpWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Tenants_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-tenants@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/tenants");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenants-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiSettings_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-aisettings@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/settings/ai");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ai-settings-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Jobs_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-jobs@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/jobs");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scheduling_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-scheduling@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/scheduling");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Requisitions_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-requisitions@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/requisitions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoices_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-invoices@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/invoices");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PurchaseOrders_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-po@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/purchase-orders");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quotes_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-quotes@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/quotes");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tenants_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-tenants@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/tenants");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Audit_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-audit@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/audit");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approvals_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-approvals@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/approvals");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiSettings_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-aisettings@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/settings/ai");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finance_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-finance@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/finance");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Users_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-users@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/users");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quotes_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-quotes@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/quotes");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quotes-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Jobs_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-jobs@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/jobs");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jobs-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiCopilot_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-aicopilot@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/ai-copilot");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ai-copilot-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiCopilot_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-aicopilot@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/ai-copilot");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ai-copilot-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoices_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-invoices@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/invoices");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invoices-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesOrders_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-salesorders@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/sales-orders");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sales-orders-ready", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sales-orders-empty", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PurchaseOrders_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-purchaseorders@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/purchase-orders");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("purchase-orders-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scheduling_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-scheduling@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/scheduling");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scheduling-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Requisitions_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-requisitions@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/requisitions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requisitions-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finance_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-finance@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/finance");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("finance-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Users_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-users@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/users");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("users-edit-permissions", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesOrders_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-salesorders@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/sales-orders");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Customers_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-customers@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/customers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customers-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Assets_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-assets@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/assets");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assets-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inventory_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-inventory@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/inventory");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Suppliers_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-suppliers@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/suppliers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("suppliers-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Divisions_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-divisions@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/divisions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("divisions-table", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompanyDocuments_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-companydocs@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/company-documents");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("company-docs-table", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StockTake_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-stocktake@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/stock-take");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stock-take-start", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PpeHistory_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-ppehistory@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/ppe-history");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ppe-history-table", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Employees_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-employees@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/employees");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("employees-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Opportunities_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-opportunities@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/opportunities");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opportunities-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Customers_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-customers@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/customers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customers-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Suppliers_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-suppliers@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/suppliers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("suppliers-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inventory_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-inventory@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/inventory");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Employees_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-employees@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/employees");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("employees-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Opportunities_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-opportunities@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/opportunities");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opportunities-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Assets_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-assets@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/assets");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assets-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Divisions_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-divisions@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/divisions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("divisions-table", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompanyDocuments_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-companydocs@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/company-documents");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("company-docs-table", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StockTake_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-stocktake@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/stock-take");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stock-take-start", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PpeHistory_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-ppehistory@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/ppe-history");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ppe-history-table", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Audit_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-audit@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/audit");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureTenantOnlyUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(email) != null)
            return;

        var tenantId = db.Tenants.Select(t => t.Id).FirstOrDefault();
        if (tenantId == Guid.Empty)
        {
            tenantId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Forbidden Test Tenant",
                Subdomain = "forbidtest",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            TenantId = tenantId
        };
        await userManager.CreateAsync(user, "TestPass123!");
        await userManager.AddClaimAsync(user, new Claim("TenantId", tenantId.ToString()));
    }

    private async Task EnsureFieldOnlyUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(email) != null)
            return;

        var tenantId = db.Tenants.Select(t => t.Id).FirstOrDefault();
        if (tenantId == Guid.Empty)
        {
            tenantId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Field Forbidden Test Tenant",
                Subdomain = "fieldforbid",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            TenantId = tenantId
        };
        await userManager.CreateAsync(user, "TestPass123!");
        await userManager.AddClaimAsync(user, new Claim("TenantId", tenantId.ToString()));
        await userManager.AddClaimAsync(user, new Claim("Permission", Permissions.FieldView));
    }
}