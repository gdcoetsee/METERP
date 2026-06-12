# METERP — Agent Instructions

Read this file first when starting work in this repository.

## What we are building

**METERP** is a **sellable, multi-tenant ERP for contractors** (field/project businesses: quotes, jobs, travel, invoicing, inventory, assets). It must work as:

1. **A demo product** — polished UI, seeded Acme tenant, docker-compose one-command run.
2. **A SaaS foundation** — per-tenant isolation, usage counters, feature flags, white-label CSS, billing hooks.

This is **not** a throwaway prototype. Prefer changes that strengthen correctness, tenant safety, and sellability.

## Tech stack

| Layer | Choice |
|-------|--------|
| Runtime | .NET 9 |
| UI | Blazor Server (Interactive Server components) |
| Database | PostgreSQL (Npgsql + EF Core) |
| Auth | ASP.NET Core Identity, claim-based permissions |
| Deploy | Docker + docker-compose |

## Architecture (Clean Architecture — do not break layers)

```
src/
  METERP.Domain/          # Entities, enums — no infra references
  METERP.Application/     # Interfaces (I*Service), ITenantProvider
  METERP.Infrastructure/  # EF DbContext, service implementations, Identity, migrations
  METERP.Web/             # Blazor pages, Program.cs, wwwroot, Reports
  METERP.Common/          # Permissions constants, shared helpers
tests/
  METERP.Application.Tests/
  METERP.E2ETests/        # Playwright stubs — expand here
```

**Dependency rule:** Web → Infrastructure → Application → Domain. Domain has zero outward deps.

When adding a feature, follow the existing pattern:
1. Entity in `Domain`
2. `I*Service` in `Application`
3. Implementation in `Infrastructure/Services`
4. Register in `Program.cs`
5. Blazor page under `Web/Components/Pages`
6. Permission constants in `Common/Permissions.cs` + policy registration in `Program.cs`
7. EF migration if schema changes

## Core business flow

The spine of the product:

**Opportunity (CRM) → Quote → Sales Order → Job → Invoice**

Supporting modules: Customers/Contacts, Inventory, Purchase Orders, Suppliers, Assets, Finance (GL), Employees/Payroll (linked to `JobLabor`), Scheduling, Reports, Notifications, Audit, AI Copilot.

**Travel costs** must remain explicit across Quote/Job/Invoice flows — this is a differentiator for contractor use.

## Non-negotiable constraints

### Multi-tenancy
- All tenant-scoped entities inherit `BaseEntity` (`TenantId`, audit, soft-delete, `RowVersion`).
- `AppDbContext` applies **global query filters** for tenant isolation and soft delete.
- `ITenantProvider` / `CurrentTenantProvider` reads `TenantId` from claims after login.
- **Never** bypass tenant filters in services unless explicitly system-level (e.g. tenant admin).

### Document line totals
- `LineTotal` on `QuoteLine`, `SalesOrderLine`, `PurchaseOrderLine`, `InvoiceLine` is **computed**, not manually set in UI/services.

### Seeding
- Seeder is **safe by default** (migrate + seed missing data only).
- Full reset requires `METERP_SEED_RESET=true` — do not reintroduce auto-`EnsureDeleted` on startup.

### Commercial / sellable
- Increment tenant usage counters in services when creating quotes, jobs, invoices, AI calls.
- Respect `Tenant.EnabledFeatures` / `HasFeature()` before enabling AI or premium features.
- AI calls are throttled per-tenant in `AiAssistantService` — preserve cost protection.

### UI
- Use existing `professional.css`, toast (`ToastService`), ARIA patterns, and gateway components (`QuoteList`, `JobList`).
- Blazor pages use `@rendermode InteractiveServer` where interactivity is needed.
- Avoid Razor parser breakage — prefer separate components over huge inline blocks.

## Demo & local dev

```bash
docker-compose up --build    # http://localhost:8080
dotnet test
dotnet run --project src/METERP.Web
```

| Item | Value |
|------|-------|
| Demo login | `admin@acme.demo` / `Demo123!` |
| AI features | Require `Ai:ApiKey` in config/secrets |
| DB reset | Set `METERP_SEED_RESET=true` in web service env |

## Testing expectations

- **Run `dotnet test` before finishing any non-trivial change.** This is mandatory.
- The goal is now **full testing** (see COMPLETION_PLAN.md). Unit tests must cover service behavior (not just entity calculations), side effects (recalculation, usage counters, soft deletes), and core flows.
- Add and maintain unit tests in `tests/METERP.Application.Tests` for all I*Service methods with business logic.
- Expand `METERP.E2ETests` with real Playwright tests for the critical flows: login, AI create + PDF, quote → job conversion (with travel), job costs/labor → invoice.
- Follow the detailed standards in `.cursor/rules/meterp-testing.mdc` (and the other rule files). Extract pure calculations for testability where helpful.
- Definition of Done includes green tests + committed test code.

## Current state (as of latest commit)

- Full module surface built and demo-ready.
- EF migrations present through `AddTenantCommercialAndFeatureFields`.
- Build clean, unit tests green.
- Many "production" items are **documented stubs** (Serilog, OpenTelemetry, real email, 2FA, Redis cache) — improve incrementally, don't rip out working demo paths.

## Roadmap — likely next priorities

See `COMPLETION_PLAN.md` for the detailed, testable work plan aligned with the current goal of "build to completion with everything tested fully".

High-level order (confirm with user before major deviations):

1. **Core Spine Testing & Hardening** (top priority) — Full unit test coverage of Quote/Job/Invoice services, conversions (Quote→Job, Job→Invoice), recalculation, explicit travel/variance, usage counter side effects, and soft-delete safety.
2. **E2E coverage** — real Playwright tests for login, quote→job convert, AI create + PDF, job with costs → invoice.
3. **AI & Commercial** — Thorough tests for AiAssistantService (throttling, feature flags, suggestions/apply, usage). Strengthen counters toward quotas.
4. **Supporting modules to same standard** — Customer, Inventory, Assets, POs, Employees, etc.
5. **Production hardening** — Serilog, health checks, rate limiting (esp. AI), reliable async side effects, secrets.
6. **Sellable / billing maturity** + performance + integrations.

Always cross-reference the enhanced `.cursor/rules/` (meterp-testing.mdc is now critical).

## What to avoid

- SQL Server — project standardized on **PostgreSQL**.
- Putting business logic in `.razor` files — keep in services.
- Cross-tenant data leaks — always test with multiple tenants in mind.
- Drive-by refactors unrelated to the task.
- Committing `mcps/`, `tempdiag/`, `bin/`, `obj/`, `.vs/` — already gitignored or local-only.

## Key files to consult

| File | Why |
|------|-----|
| `README.md` | Status, manual flows, deployment |
| `AGENTS.md` + `.cursor/rules/*.mdc` + `COMPLETION_PLAN.md` | Full context, strict rules (especially testing), and prioritized work plan |
| `src/METERP.Web/Program.cs` | DI, auth policies, seeding |
| `src/METERP.Infrastructure/Persistence/AppDbContext.cs` | Filters, DbSets |
| `src/METERP.Common/Permissions.cs` | Permission naming |
| `src/METERP.Domain/Tenant.cs` | Commercial/feature model |
| Core services (`QuoteService`, `JobService`, `InvoiceService`, `AiAssistantService`, `TenantService`) | Where most business logic + side effects live |

When unsure of intent, ask the user — but default to **sellable multi-tenant contractor ERP** as the north star.