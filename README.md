# METERP - Modular ERP System

**Target Industry**: Electrical & Mechanical Contracting Companies (South Africa)

**Goal**: Build a professional, multi-tenant, sellable ERP that starts with your companyâ€™s needs and can be commercialized.

## Architecture
- **Clean Architecture** (Domain â†’ Application â†’ Infrastructure â†’ Web)
- **Multi-tenancy** from day one (TenantId + Global Query Filters)
- **Blazor Server** for UI (rich, C# end-to-end)
- **.NET 9**

## Phase 1 Foundation (What we are building now)
- Multi-tenant DbContext
- Base entities with audit + soft delete
- ASP.NET Identity + Permissions
- Dependency Injection setup
- Docker support
- Basic Tenant management

## Next Modules (after foundation)
1. Customer & Contact Management
2. Quote â†’ Sales Order â†’ Job workflow
3. Inventory & Stock Transactions
4. Assets / Transformers
5. Job Costing & Timesheets
6. Invoicing
