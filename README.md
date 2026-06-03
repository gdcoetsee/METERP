# METERP - Modular ERP System

**Target Industry**: Electrical & Mechanical Contracting Companies (South Africa)

**Goal**: Build a professional, multi-tenant, sellable ERP that starts with your company's needs and can be commercialized.

## Architecture
- **Clean Architecture** (Domain -> Application -> Infrastructure -> Web)
- **Multi-tenancy** from day one (TenantId + Global Query Filters)
- **Blazor Server** for UI (rich, C# end-to-end)
- **.NET 9**

## Phase 1 Foundation (What we are building now)
- Multi-tenant DbContext (PostgreSQL via Npgsql)
- Base entities with audit + soft delete + concurrency token
- ASP.NET Core Identity (multi-tenant users + roles) + claim-based Permissions
- Current user & tenant services
- Dependency Injection setup
- Docker support (Postgres included)
- Basic Tenant management + demo UI with login
- Authorization policies

**Tech decisions made for the foundation:**
- **Database**: PostgreSQL (better cost/scalability for a sellable multi-tenant product). Easy to switch via connection string.
- **Identity**: Full ASP.NET Identity with `TenantId` on users/roles. Permissions implemented as claims + policies.
- Blazor Server interactive components with cascading auth state.

## Next Modules (after foundation)
1. Customer & Contact Management
2. Quote -> Sales Order -> Job workflow
3. Inventory & Stock Transactions
4. Assets / Transformers
5. Job Costing & Timesheets
6. Invoicing
