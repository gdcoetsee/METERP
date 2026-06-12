namespace METERP.Common;

/// <summary>
/// Central list of permission constants used across the application.
/// These are assigned as claims to roles/users.
/// </summary>
public static class Permissions
{
    // Tenant management
    public const string TenantsView = "Tenants.View";
    public const string TenantsManage = "Tenants.Manage";

    // Customer & Contact Management (Module 1)
    public const string CustomersView = "Customers.View";
    public const string CustomersManage = "Customers.Manage";
    public const string ContactsView = "Contacts.View";
    public const string ContactsManage = "Contacts.Manage";

    // Quote -> Job workflow (Module 2)
    public const string QuotesView = "Quotes.View";
    public const string QuotesManage = "Quotes.Manage";

    public const string JobsView = "Jobs.View";
    public const string JobsManage = "Jobs.Manage";

    // User management (foundation - tenant-scoped user admin)
    public const string UsersView = "Users.View";
    public const string UsersManage = "Users.Manage";

    // Invoicing (completes Quote -> Job -> Invoice flow)
    public const string InvoicesView = "Invoices.View";
    public const string InvoicesManage = "Invoices.Manage";

    // Inventory & Stock (materials tracking for quotes/jobs)
    public const string InventoryView = "Inventory.View";
    public const string InventoryManage = "Inventory.Manage";

    // Assets / Transformers management
    public const string AssetsView = "Assets.View";
    public const string AssetsManage = "Assets.Manage";

    // Purchasing / Supply Chain (replenish inventory, AP)
    public const string SuppliersView = "Suppliers.View";
    public const string SuppliersManage = "Suppliers.Manage";

    public const string PurchaseOrdersView = "PurchaseOrders.View";
    public const string PurchaseOrdersManage = "PurchaseOrders.Manage";

    // Finance / Accounting (GL, journals, basic financials)
    public const string FinanceView = "Finance.View";
    public const string FinanceManage = "Finance.Manage";

    // HR / Employees (links to JobLabor for costing)
    public const string EmployeesView = "Employees.View";
    public const string EmployeesManage = "Employees.Manage";

    // Sales Orders (Quote -> SO -> Job flow)
    public const string SalesOrdersView = "SalesOrders.View";
    public const string SalesOrdersManage = "SalesOrders.Manage";

    // CRM Opportunities
    public const string OpportunitiesView = "Opportunities.View";
    public const string OpportunitiesManage = "Opportunities.Manage";

    // Audit / compliance
    public const string AuditView = "Audit.View";

    // Add more as modules are implemented
}
