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

    public const string JobsView = "Jobs.View";
    public const string JobsManage = "Jobs.Manage";

    // Add more as modules are implemented
}
