namespace METERP.Common;

/// <summary>
/// Preset role templates with default permission sets for contractor ERP accountability.
/// </summary>
public static class RoleTemplates
{
    public static IReadOnlyList<string> AllRoleNames { get; } =
    [
        "Admin",
        "Executive",
        "DivisionManager",
        "Manager",
        "Estimator",
        "Procurement",
        "Stores",
        "Finance",
        "HrManager",
        "Technician",
        "Auditor"
    ];

    public static string[] GetPermissions(string roleName) => roleName switch
    {
        "Admin" =>
        [
            Permissions.TenantsView, Permissions.TenantsManage,
            Permissions.CustomersView, Permissions.CustomersManage,
            Permissions.QuotesView, Permissions.QuotesManage, Permissions.QuotesApprove,
            Permissions.JobsView, Permissions.JobsManage,
            Permissions.UsersView, Permissions.UsersManage, Permissions.UsersManagePermissions,
            Permissions.CompanyDocsView, Permissions.CompanyDocsManage,
            Permissions.InvoicesView, Permissions.InvoicesManage,
            Permissions.InventoryView, Permissions.InventoryManage,
            Permissions.RequisitionsView, Permissions.RequisitionsManage, Permissions.RequisitionsApprove,
            Permissions.AssetsView, Permissions.AssetsManage,
            Permissions.SuppliersView, Permissions.SuppliersManage,
            Permissions.PurchaseOrdersView, Permissions.PurchaseOrdersManage,
            Permissions.FinanceView, Permissions.FinanceManage,
            Permissions.EmployeesView, Permissions.EmployeesManage,
            Permissions.PayrollView, Permissions.PayrollManage,
            Permissions.SalesOrdersView, Permissions.SalesOrdersManage,
            Permissions.OpportunitiesView, Permissions.OpportunitiesManage,
            Permissions.AuditView,
            Permissions.FieldView, Permissions.ApprovalsView,
            Permissions.DivisionsView, Permissions.DivisionsManage,
            Permissions.LeaveView, Permissions.LeaveManage, Permissions.LeaveApprove
        ],
        "Executive" =>
        [
            Permissions.TenantsView,
            Permissions.CustomersView, Permissions.CustomersManage,
            Permissions.QuotesView, Permissions.QuotesManage, Permissions.QuotesApprove,
            Permissions.JobsView, Permissions.JobsManage,
            Permissions.UsersView, Permissions.UsersManagePermissions,
            Permissions.InvoicesView, Permissions.InvoicesManage,
            Permissions.InventoryView,
            Permissions.AssetsView,
            Permissions.SuppliersView, Permissions.PurchaseOrdersView,
            Permissions.FinanceView, Permissions.FinanceManage,
            Permissions.EmployeesView, Permissions.EmployeesManage,
            Permissions.PayrollView, Permissions.PayrollManage,
            Permissions.SalesOrdersView, Permissions.SalesOrdersManage,
            Permissions.OpportunitiesView, Permissions.OpportunitiesManage,
            Permissions.AuditView,
            Permissions.ApprovalsView,
            Permissions.DivisionsView,
            Permissions.LeaveView, Permissions.LeaveApprove,
            Permissions.CompanyDocsView, Permissions.CompanyDocsManage,
            Permissions.RequisitionsView, Permissions.RequisitionsApprove
        ],
        "DivisionManager" or "Manager" =>
        [
            Permissions.CustomersView, Permissions.CustomersManage,
            Permissions.QuotesView, Permissions.QuotesManage,
            Permissions.JobsView, Permissions.JobsManage,
            Permissions.UsersView,
            Permissions.InvoicesView, Permissions.InvoicesManage,
            Permissions.InventoryView, Permissions.InventoryManage,
            Permissions.RequisitionsView, Permissions.RequisitionsManage, Permissions.RequisitionsApprove,
            Permissions.AssetsView, Permissions.AssetsManage,
            Permissions.SuppliersView, Permissions.PurchaseOrdersView, Permissions.PurchaseOrdersManage,
            Permissions.FinanceView,
            Permissions.EmployeesView, Permissions.EmployeesManage,
            Permissions.PayrollView,
            Permissions.SalesOrdersView, Permissions.SalesOrdersManage,
            Permissions.OpportunitiesView, Permissions.OpportunitiesManage,
            Permissions.AuditView,
            Permissions.FieldView, Permissions.ApprovalsView,
            Permissions.DivisionsView,
            Permissions.LeaveView, Permissions.LeaveApprove
        ],
        "Estimator" =>
        [
            Permissions.CustomersView,
            Permissions.QuotesView, Permissions.QuotesManage,
            Permissions.OpportunitiesView, Permissions.OpportunitiesManage,
            Permissions.JobsView,
            Permissions.InventoryView
        ],
        "Procurement" =>
        [
            Permissions.SuppliersView, Permissions.SuppliersManage,
            Permissions.PurchaseOrdersView, Permissions.PurchaseOrdersManage,
            Permissions.InventoryView,
            Permissions.RequisitionsView
        ],
        "Stores" =>
        [
            Permissions.InventoryView, Permissions.InventoryManage,
            Permissions.RequisitionsView, Permissions.RequisitionsManage,
            Permissions.PurchaseOrdersView,
            Permissions.ApprovalsView
        ],
        "Finance" =>
        [
            Permissions.CustomersView,
            Permissions.InvoicesView, Permissions.InvoicesManage,
            Permissions.FinanceView, Permissions.FinanceManage,
            Permissions.PayrollView, Permissions.PayrollManage,
            Permissions.JobsView,
            Permissions.AuditView
        ],
        "HrManager" =>
        [
            Permissions.EmployeesView, Permissions.EmployeesManage,
            Permissions.PayrollView, Permissions.PayrollManage,
            Permissions.UsersView, Permissions.UsersManagePermissions,
            Permissions.LeaveView, Permissions.LeaveManage, Permissions.LeaveApprove,
            Permissions.DivisionsView,
            Permissions.ApprovalsView,
            Permissions.AuditView,
            Permissions.CompanyDocsView, Permissions.CompanyDocsManage
        ],
        // Field portal only — office Jobs/AI/Scheduling stay manager-level (E2E access-denied suite).
        "Technician" =>
        [
            Permissions.FieldView,
            Permissions.LeaveView
        ],
        "Auditor" =>
        [
            Permissions.AuditView,
            Permissions.CustomersView,
            Permissions.QuotesView,
            Permissions.JobsView,
            Permissions.InvoicesView,
            Permissions.FinanceView
        ],
        _ => Array.Empty<string>()
    };
}