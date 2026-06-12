# METERP Completion & Full Testing Plan

## 🚨 CURSOR / NEXT SESSION HANDOFF (Read this FIRST - Current as of 2026-06-12 session)

**SO spine chain + conversion guards + quote audit (2026-06-12 latest).**

### Exact Work Completed — Latest (2026-06-12, continue-the-plan session 30)
- **Phase 1 — Extended spine + compliance hooks**:
  - `SpineChainTests` (+1): Sales Order → Job → Invoice chain; SO status `InProgress`; invoice copies quote travel lines.
  - `InvoiceTests` (+1): `CreateFromJobAsync` job-not-found guard.
  - `SalesOrderServiceTests` (+1): `ConvertToJobAsync` SO-not-found guard.
  - `QuoteTests` (+1): `ConvertToJobAsync` writes `CONVERT` audit entry via `IAuditService`.
- **Testing**: **244/244 green** (213 unit + 9 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 29)
- **Phase 1 — Quote → Job → Invoice spine (unit chain)**:
  - `SpineChainTests` (+1): full chain preserves explicit travel `JobCost` + invoice travel line + matching totals.
  - `QuoteTests` (+3): `ConvertToJobAsync` travel `JobCost`, quote-not-found guard, all-lines-soft-deleted still converts.
  - `InvoiceTests` (+1): `DeleteAsync` soft-deletes invoice + lines.
- **Testing**: **240/240 green** (209 unit + 9 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 28)
- **Phase 1 — Core spine service tests (continued)**:
  - `QuoteTests` (+2): `UpdateLineAsync` recalc, `DeleteAsync` soft-deletes quote + lines.
  - `InvoiceTests` (+2): `UpdateLineAsync` recalc, `DeleteLineAsync` soft-delete + recalc.
  - `JobTests` (+1): `UpdateStatusAsync` sets `CompletedDate` when `Invoiced`.
  - `OpportunityServiceTests` (+1): `AdvanceStageAsync` does not advance from `ClosedLost`.
- **Testing**: **235/235 green** (204 unit + 9 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 27)
- **Phase 1 — JobService spine hardening (continued)**:
  - `JobTests` (+4): `DeleteCostAsync` recalc + soft-delete, `DeleteAsync` cascades costs, `UpdateStatusAsync` sets `CompletedDate`, `GetMarginPercent` zero-quoted edge.
  - Shared `SeedJobAsync` helper for service tests with proper Customer + `CreateAsync` path.
- **Phase 5 — Dev config**: `.env.example` Seq comment aligned with docker-compose `seq` service.
- **Testing**: **229/229 green** (198 unit + 9 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 26)
- **Phase 1/4 — `JobService.SetCrewAssignmentsAsync` direct unit tests**:
  - `JobTests` (+4): distinct crew add (dedupe + skip `Guid.Empty`), soft-delete sync on removal, re-activate previously deleted crew, job-not-found guard.
  - Complements indirect coverage via `SchedulingServiceTests`; validates `IgnoreQueryFilters` re-activation path.
- **Phase 5 — Dev config**: `.env.example` documents `Billing__WebhookEventRetentionDays`.
- **Testing**: **225/225 green** (194 unit + 9 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 25)
- **Phase 5 — Webhook idempotency retention on startup**:
  - `BillingOptions.WebhookEventRetentionDays` (default 90; 0 = disabled).
  - `DatabaseSeeder` calls `IBillingWebhookMaintenanceService` after migrate/seed (idempotent purge).
  - `appsettings.json` + `docker-compose.yml` documented; `BillingOptionsTests` (1).
- **Testing**: **221/221 green** (190 unit + 9 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 24)
- **Phase 5 — Billing webhook hardening (continued)**:
  - `BillingWebhookResult.UpdatedTier` returned on tier-changing events; `/webhooks/stripe` JSON includes `tier`.
  - `IBillingWebhookMaintenanceService` / `BillingWebhookMaintenanceService`: purge processed Stripe events older than retention window.
  - `BillingWebhookMaintenanceServiceTests` (2); webhook endpoint asserts `Enterprise` in response body.
- **Testing**: **220/220 green** (189 unit + 9 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 23)
- **Phase 5 — Billing webhook idempotency**:
  - `ProcessedStripeWebhookEvent` ledger + EF migration `AddProcessedStripeWebhookEvents`.
  - `BillingWebhookService`: dedupe by Stripe `event.id`; `BillingWebhookOutcome.Duplicate`.
  - `BillingWebhookServiceTests` (+1 duplicate); `BillingWebhookEndpointTests` (+2 duplicate + not rate limited).
- **Testing**: **218/218 green** (187 unit + 9 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 22)
- **Phase 5 — Unified Account hub**:
  - `Account.razor` at `/account`, `/account-billing`, `/account-security` — tabbed hub (`account-hub-ready`).
  - `AccountBillingPanel` + `AccountSecurityPanel` components (all prior `data-testid` markers preserved).
  - Nav: single **My Account** link replaces separate Billing & Security entries.
  - E2E `Account_Hub_Shows_Billing_And_Security_Tabs` (22nd Playwright test).
- **Testing**: **215/215 green** (186 unit + 7 web + 22 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 21)
- **Phase 5 — Webhook → Account Billing tier refresh**:
  - `ITenantBillingViewService` / `TenantBillingViewService`: fresh billing read model (tier, status, AI feature, past-due).
  - `AccountBilling.razor`: uses view service + **Refresh plan** button (`account-billing-refresh-button`).
  - `TenantBillingViewServiceTests` (2); E2E `AccountBilling_Reflects_Webhook_Tier_Update` (21st Playwright test — POST `/webhooks/stripe` then beta tier).
  - `E2EHelpers.PostStripeWebhookAsync` for unsigned dev webhook calls.
- **Testing**: **214/214 green** (186 unit + 7 web + 21 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 20)
- **Phase 4 — Scheduling → JobLabor quick-add**:
  - `ISchedulingService.AddCrewLaborAsync` — one-click JobLabor for lead + crew (EmployeeId, rates, work date).
  - `Scheduling.razor`: quick-add panel (`scheduling-quick-labor-panel`) with hours, date, crew checkboxes.
  - `SchedulingServiceTests` (+2); E2E `Scheduling_Quick_Adds_Labor_From_Assigned_Crew` (20th Playwright test).
- **Testing**: **211/211 green** (184 unit + 7 web + 20 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 19)
- **Phase 4 — Cashflow forecast reporting**:
  - `ICashflowReportService` / `CashflowReportService`: receivables (sent/partial/overdue invoices) + accepted quote pipeline − open PO commitments.
  - `Reports.razor`: real cashflow card (`reports-cashflow-card`) replaces hardcoded +R 245k stub; compact currency formatting.
  - `CashflowReportServiceTests` (2); E2E `Reports_Page_Shows_Cashflow_Forecast_From_Receivables` (19th Playwright test).
- **Testing**: **208/208 green** (182 unit + 7 web + 19 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 18)
- **Phase 4 — Job profitability reporting**:
  - `Job.GetMarginPercent()` — pure margin from quoted vs actual (travel + labor + costs).
  - `IJobReportService` / `JobReportService`: average margin + top performer from jobs with recorded costs.
  - `Reports.razor`: real profitability card (`reports-profitability-card`) replaces hardcoded 18% stub.
  - `JobReportServiceTests` (3); E2E `Reports_Page_Shows_Job_Profitability_From_Variance` (18th Playwright test).
- **Testing**: **205/205 green** (180 unit + 7 web + 18 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 17)
- **Phase 4 — Technician utilization reporting**:
  - `IWorkforceReportService` / `WorkforceReportService`: monthly hours vs 160h capacity per active employee.
  - `Reports.razor`: real utilization card (`reports-utilization-card`), team average bar, per-tech rows from JobLabor.
  - `WorkforceReportServiceTests` (2); E2E `Reports_Page_Shows_Technician_Utilization` (17th Playwright test).
- **Testing**: **201/201 green** (177 unit + 7 web + 17 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 16)
- **Phase 4 — Payroll from JobLabor**:
  - `IPayrollService` / `PayrollService`: monthly summaries per employee (hours, gross, entry count from linked `JobLabor`).
  - `Payroll.razor` refactored off hardcoded stubs — real data, `data-testid` markers, payslip card from summaries.
  - `PayrollServiceTests` (2): aggregation + zero-labor employees.
  - E2E: `Payroll_Page_Shows_JobLabor_Summaries` (16th Playwright test).
- **Testing**: **198/198 green** (175 unit + 7 web + 16 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 15)
- **Phase 4 — Crew → payroll labor linkage**:
  - `JobLabor.EmployeeId` + `Employee` navigation; EF migration `AddJobLaborEmployeeId`.
  - `JobService.AddLaborAsync` fills `Technician` + `HourlyRate` from employee when linked.
  - Seeder backfill: match existing demo labor `Technician` names to employees.
  - `JobTests.JobService_AddLaborAsync_LinksEmployee_DefaultsTechnicianAndRate` (+1 unit).
- **Phase 5 — Blazor DbContext disposal hardening**:
  - `Login.razor` / `LoginComplete.razor` use `IServiceScopeFactory` for pre-login user lookup (not circuit-scoped `AppDbContext`).
- **Testing**: **195/195 green** (173 unit + 7 web + 15 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 14)
- **Phase 5 — Tenant Account Billing page**:
  - `AccountBilling.razor` at `/account-billing` — plan tier, subscription status, monthly quotas, lifetime totals, Stripe portal button.
  - Nav menu: **Billing & Plan** link for all authenticated users.
  - Seeder: idempotent Acme `StripeCustomerId` + `SubscriptionStatus` backfill on existing DBs.
  - E2E: `AccountBilling_Page_Shows_Plan_And_Manage_Billing` (15th Playwright test).
- **Testing**: **194/194 green** (172 unit + 7 web + 15 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 13)
- **Phase 4 — Multi-employee job crews**:
  - `JobCrewAssignment` entity + EF migration `AddJobCrewAssignments`.
  - `Job.GetCrewEmployees()`; `IJobService.SetCrewAssignmentsAsync` (soft-delete sync).
  - `SchedulingService.AssignJobResourcesAsync` — lead + additional crew list; lead always in crew.
  - `Scheduling.razor`: crew checkboxes (`scheduling-crew-panel`), lead + crew badges in table.
  - `ListCacheGraphHelper` strips crew back-references for job list cache.
  - `SchedulingServiceTests` (+1 crew); E2E scheduling test covers crew panel + save.
- **Testing**: **193/193 green** (172 unit + 7 web + 14 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 12)
- **Phase 5 — Tenant-facing billing portal**:
  - `Home.razor`: `home-manage-billing-button` on quota card; `home-billing-past-due-manage-button` on past-due banner.
  - Resolves portal via `IBillingPortalService.ResolveCustomerPortalUrlAsync` for current tenant (no Tenants admin page required).
- **Phase 5 — AI HTTP rate limit tests**:
  - `AiCopilotRateLimitTests` (2): `/ai-copilot` returns 429 after 30/min; `/health/ready` exempt.
- **E2E**: `Home_Quota_Usage_Card_Shows_Monthly_Usage` asserts Manage billing button when portal configured.
- **Testing**: **192/192 green** (171 unit + 7 web + 14 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 11)
- **Phase 5 — Stripe Customer Portal API**:
  - `IStripeCustomerPortalClient` / `StripeCustomerPortalClient` — POST `billing_portal/sessions` via named `stripe` HttpClient.
  - `BillingOptions`: `StripeSecretKey`, `CustomerPortalReturnUrl`; `CanCreateApiSessions` when key + return URL set.
  - `IBillingPortalService.ResolveCustomerPortalUrlAsync` — API session first, static `CustomerPortalBaseUrl` fallback.
  - `Tenants.razor`: pre-resolved portal URLs on load; edit form opens fresh session via JS.
  - Config: `appsettings.json`, `docker-compose.yml`, `.env.example`.
  - `StripeCustomerPortalClientTests` (3) + `BillingPortalServiceTests` (+3 async/fallback).
- **Testing**: **190/190 green** (171 unit + 5 web + 14 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 10)
- **Phase 4 — Scheduling employee assignment (real field)**:
  - `Job.AssignedEmployeeId` + `AssignedEmployee` navigation (replaces notes-tag stub).
  - EF migration `AddJobAssignedEmployee`.
  - `SchedulingService.AssignJobResourcesAsync` sets/clears `AssignedEmployeeId`; `JobService` includes employee on list/detail.
  - `Scheduling.razor`: employee badge (`scheduling-assigned-employee`), pre-select on View/Assign.
  - `SchedulingServiceTests` (+1): assign + clear employee; E2E scheduling test saves assignment + asserts toast + badge.
- **Dev UX**: `scripts/stop-local-e2e.ps1` — frees port 8080 / stops `METERP.Web` (pair with `run-local-e2e.ps1`).
- **Testing**: **184/184 green** (165 unit + 5 web + 14 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 9)
- **Phase 2 E2E — Finance export hardened**:
  - `Finance_Page_Loads_Chart_Of_Accounts_And_Export` asserts success toast + captured CSV (`EntryDate` header, account `4000`).
  - `meterp-clipboard.js` + `meterpClipboard.write` JS interop (testable in headless Playwright).
  - `E2EHelpers.InstallMeterpClipboardStubAsync` / `ReadCapturedClipboardAsync`.
- **Seeder**: idempotent GL journal **line** backfill when CoA exists but `JournalEntryLines` empty (fixes older dev DBs).
- **Dev UX**: `scripts/run-local-e2e.ps1` stops processes bound to port 8080 before `dotnet run` (fixes port-in-use exit 1).
- **Testing**: **183/183 green** (164 unit + 5 web + 14 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 8)
- **Phase 4 — Scheduling service layer**:
  - `ISchedulingService` / `SchedulingService`: `GetBoardAsync`, `AssignJobResourcesAsync` (asset + employee note stub).
  - `Scheduling.razor` refactored to use `ISchedulingService` only (no direct `IJobService`/`IAssetService`/`IEmployeeService` in page).
  - `SchedulingServiceTests` (2): board load, resource assignment.
- **Phase 5 — Stripe customer portal (static URL)**:
  - `IBillingPortalService` / `BillingPortalService`; `BillingOptions.CustomerPortalBaseUrl`.
  - `Tenants.razor`: billing portal link (`tenant-billing-portal`), Stripe fields in edit form.
  - Seeder: Acme demo `StripeCustomerId = cus_demo_acme`, `SubscriptionStatus = active`.
  - `appsettings.json` + `docker-compose.yml`: `Billing__CustomerPortalBaseUrl`.
  - `BillingPortalServiceTests` (3).
  - E2E: `Tenants_Page_Loads_Commercial_Usage_Table` asserts portal `href` contains `cus_` when link present.
- **Testing**: **183/183 green** (164 unit + 5 web + 14 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 7)
- **Phase 2 E2E — 2FA + Finance**:
  - `AccountSecurity_Enables_TwoFactor_And_Login_Challenge` — beta user TOTP setup, login challenge, cleanup.
  - `Finance_Page_Loads_Chart_Of_Accounts_And_Export` — GL table + CSV export button.
  - `data-testid` on Finance + `account-security-ready`; Playwright-friendly `@oninput` on 2FA code fields.
- **Phase 5 — 2FA + billing UX**:
  - `TwoFactorAuthServiceTests` (5): setup, confirm, verify, disable with Identity token provider.
  - `TotpHelper` (Otp.NET) for E2E codes; `home-billing-past-due-banner` on dashboard.
  - `BillingWebhookServiceTests`: `past_due` status preserves tier.
- **Testing**: **178/178 green** (159 unit + 5 web + 14 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 6)
- **Phase 5 — SaaS billing webhooks (Stripe-compatible)**:
  - `BillingOptions`, `IBillingWebhookService` / `BillingWebhookService`.
  - `POST /webhooks/stripe` with signature validation (`StripeWebhookSignatureValidator`).
  - Events: `customer.subscription.updated/deleted`, `checkout.session.completed`.
  - Tenant fields: `StripeCustomerId`, `SubscriptionStatus`; migration `AddTenantBillingFields`.
  - `TenantQuotaDefaults.ApplyBillingTier` for quota/feature reset on tier change.
  - `BillingWebhookServiceTests` (4) + `BillingWebhookEndpointTests` (1 web integration).
- **Phase 2 E2E — Sales Orders**:
  - `data-testid` on `SalesOrders.razor`; `SalesOrders_Page_Loads_And_Shows_Detail` (12th Playwright test).
- **UI**: Tenants table shows `SubscriptionStatus` when set via webhook.
- **Testing**: **170/170 green** (153 unit + 5 web + 12 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 5)
- **Phase 5 — cache startup crash fix**:
  - `ListCacheGraphHelper` strips EF back-references before JSON cache (Quote↔QuoteLine, Job↔JobCost).
  - Applied in `QuoteService.LoadQuotesAsync` + `JobService.LoadJobsAsync`.
  - `TenantDistributedCacheServiceTests`: serializes quotes with lines without cycle.
- **Phase 2 E2E — Audit**:
  - `audit-ready` marker on `Audit.razor`.
  - `Audit_Page_Loads_Compliance_Trail` (11th Playwright test).
- **Phase 5 — job list cache integration**:
  - `JobServiceCacheTests` (3): stale reads, invalidation, search bypass.
- **CI**: unit job runs full solution `Category!=E2E` (includes Web.Tests).
- **Testing**: **163/163 green** (148 unit + 4 web + 11 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 4)
- **Phase 2 E2E — Scheduling**:
  - `data-testid` on `Scheduling.razor` (table, rows, assign panel, AI buttons).
  - `Scheduling_Page_Loads_Jobs_And_Assignment_Panel` (10th Playwright test; fallbacks for older builds).
- **Phase 5 — quote list cache integration**:
  - `QuoteServiceCacheTests` (3): cached stale reads until invalidation, `CreateAsync` invalidates, search bypasses cache.
- **Phase 5 — billing/webhook hardening**:
  - `InvoiceIntegrationTests`: invalid webhook URL skipped (no HTTP call).
- **Testing**: **158/158 green** (144 unit + 4 web + 10 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 3)
- **Phase 5 — Redis distributed cache**:
  - `TenantDistributedCacheService` via `IDistributedCache` (Redis when `Cache:RedisConnection` set, else distributed memory).
  - `CacheOptions`, docker-compose `redis:7-alpine`, `Cache__RedisConnection=redis:6379` on web.
  - JSON serialization with cycle handling for EF list caches.
  - `TenantDistributedCacheServiceTests` (2): invalidation + tenant isolation.
- **E2E**: `Tenants_Page_Loads_Commercial_Usage_Table` (9th Playwright test).
- **Testing**: **153/153 green** (140 unit + 4 web + 9 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 2)
- **Phase 1/4 — quota enforcement on spine**:
  - `SpineQuotaEnforcementTests` (4): Quote/Job/Invoice create + Quote→Job blocked at monthly limits via real `QuotaService`.
  - `TenantQuotaDefaultsTests` (7): tier limits, enterprise unlimited, overrides, `ApplyTierDefaults`.
  - `QuotaServiceTests` expanded (+3 theory cases): Job, Invoice, AiCall at limit.
- **Phase 2 E2E**: `Home_Quota_Usage_Card_Shows_Monthly_Usage` (8th Playwright test).
- **UI**: `tenants-table` data-testid on Tenants page.
- **Testing**: **150/150 green** (138 unit + 4 web + 8 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session)
- **E2E — CRM handoff**: `Opportunity_Converts_To_Quote_Via_Ai_Copilot` (Opportunities → AI Copilot → draft quote).
- **Phase 4 — UserService tests** (6): tenant isolation, search, create+claims, role change, available roles.
- **Seeder**: `SyncUserPermissionClaimsFromRoleAsync` — existing demo admins pick up new role permissions on startup.
- **AI throttle fix**: per-tenant throttle keys (was global `"ai"` — caused parallel test flakes).
- **Testing**: **135/135 green** (124 unit + 4 web + 7 E2E).

### Exact Work Completed — Prior (2026-06-12, sellable-ready push)
- **CRM Opportunities (real DB, not localStorage)**:
  - `Opportunity` + `OpportunityStage` domain; `IOpportunityService` / `OpportunityService` with audit hooks.
  - `Opportunities.razor` — pipeline UI, AI handoff via `pendingAiScope` + `pendingOpportunityId`.
  - `AICopilot.razor` — reads `pendingOpportunityId`, uses linked customer, calls `MarkConvertedToQuoteAsync` after quote apply.
  - EF migration `AddOpportunitiesAndAuditLog`; seeder seeds demo opportunities; Admin/Manager get `Opportunities.*` + `Audit.View`.
- **Audit trail (compliance story)**:
  - `AuditLogEntry` + `IAuditService` / `AuditService`; `Audit.razor` with CSV export; `QuoteService` logs CREATE + CONVERT.
- **Two-factor authentication (production path)**:
  - `ITwoFactorAuthService` / `TwoFactorAuthService` (Identity TOTP); login → challenge → `/login-2fa` → `LoginComplete`.
  - `AccountSecurity.razor` — enable/confirm/disable authenticator 2FA for current user.
- **Observability**: OpenTelemetry console trace/metrics in `Program.cs`.
- **MailKit** bumped to 4.17.0 (CVE fix).
- **Testing**: **128/128 green** (118 unit + 4 web + 6 E2E). `OpportunityServiceTests` (+6 incl. `MarkConvertedToQuote`).

### Exact Work Completed — Prior (2026-06-12, after 05ed879)
- **Phase 4 — supporting module tests (continued)**:
  - `SalesOrderServiceTests` (4): SO number + totals, line recalc, soft delete, convert-to-job.
  - `FinanceServiceTests` expanded (+3): unbalanced journal guard, entry number, account balance.
- **Phase 3 — full AI HTTP path**:
  - `AiAssistantServiceHttpTests` (+2): `AnalyzeJobVarianceAsync` + `AskCopilotAsync` with mocked LLM.
- **Phase 5 — real SMTP integration**:
  - `LoopbackSmtpServer` test harness; `SendEmailAsync_DeliversToLoopbackSmtpServer` integration test.
  - `SmtpEmailSender` uses `SecureSocketOptions.None` when SSL disabled (plain SMTP/dev).
- **Testing**: **120/120 green** (110 unit + 4 web + 6 E2E).

### Exact Work Completed — Prior (2026-06-12, after d75a396)
- **Phase 4 — supporting module tests (continued)**:
  - `AssetServiceTests` (5): asset number, search, status, maintenance notes, soft delete.
  - `PurchaseOrderServiceTests` (4): PO number + totals, line recalc, soft delete lines, receive → inventory.
  - `EmployeeServiceTests` (4): create, inactive filter, search, soft delete.
- **Phase 3 — deeper AI tests**:
  - `AiAssistantService` accepts optional `HttpClient` for test injection.
  - `AiAssistantServiceHttpTests` (2): full `SuggestQuoteLinesAsync` path with mocked LLM + quota block.
  - `ClearThrottleStateForTesting()` + `InternalsVisibleTo` for stable parallel unit tests.
- **Testing**: **110/110 green** (100 unit + 4 web + 6 E2E).

### Exact Work Completed — Prior (2026-06-12, after cc7cd84)
- **Phase 3 — AI apply fully extracted**:
  - `IAiProjectPlanApplyService` / `AiProjectPlanApplyService` — quote + job skeleton from AI text; feature-gated.
  - `AiQuoteSuggestionParser` — pure JSON parsing extracted from `AiAssistantService` (unit-tested).
  - `AICopilot.razor` delegates all three apply paths (quote, job, project plan).
  - Tests: `AiQuoteSuggestionParserTests` (4), `AiProjectPlanApplyServiceTests` (4).
- **Phase 4 — supporting module tests**:
  - `CustomerServiceTests` (4): create, search, soft-delete + contact cascade, primary contact.
  - `SupplierServiceTests` (4): create, inactive filter, search, soft delete.
- **Phase 5 — email + quota UX**:
  - `SmtpEmailSenderTests` (4): configured flag + no-op when unconfigured/empty recipient.
  - `InvoiceIntegrationTests`: email path when tenant `NotificationEmail` set.
  - `QuotaExceededException` toasts on Quotes convert, Jobs invoice create, AICopilot apply actions.
- **Testing**: **95/95 green** (85 unit + 4 web + 6 E2E).

### Exact Work Completed — Prior (2026-06-12, after 051085a)
- **Phase 3 — AI apply complete (quote + job)**:
  - `IAiJobApplyService` / `AiJobApplyService` — draft job + explicit `Travel` JobCost; feature-gated via `AiCopilotAccessGuard`.
  - `AiQuoteApplyService` now enforces `HasFeature("ai")` (`AiFeatureDisabledException`).
  - `AiJobApplyServiceTests` (3) + `AiQuoteApplyServiceTests` feature-gating test; AICopilot delegates job apply + shows quota/feature banners.
- **Phase 5 — quota UI + Seq**:
  - Home dashboard: `home-quota-usage-card` with period usage vs limits (color-coded).
  - AICopilot: `ai-feature-disabled-banner`, `ai-quota-exhausted-banner`; buttons disabled when blocked (E2E still works without API key).
  - `docker-compose.yml`: Seq service enabled + `Seq__ServerUrl` on web.
- **Phase 4 — supporting module tests**:
  - `InventoryServiceTests` (3): create SKU, stock transaction, low-stock filter.
  - `NotificationServiceTests` (2): add + mark-all-read with in-memory JS mock.
- **Testing**: **74/74 green** (64 unit + 4 web + 6 E2E).

### Exact Work Completed — Continued Session (2026-06-12, commits after aa0a146)
- **Phase 3 — AI apply logic**:
  - New `IAiQuoteApplyService` + `AiQuoteApplyService` (structured suggestion lines + explicit travel fallback).
  - `AICopilot.razor` delegates `CreateQuoteFromAiText` to service (logic out of Razor).
  - `AiQuoteApplyServiceTests.cs` — 5 tests (suggestions, fallback travel, error paths).
- **Phase 5 — continued**:
  - `METERP.Web.Tests` with `WebApplicationFactory` — `/health` + `/health/ready` integration tests (2 green).
  - `Program.cs`: Testing env uses EF InMemory + skips seeder; AI Copilot path rate limit 30/min.
  - `docker-compose.yml`: documented optional Seq service block.
  - E2E convert test hardened (job detail view asserts J-/Q-/travel).
- **Testing**: **65/65 green** (57 unit + 2 web integration + 6 E2E).

### Exact Work Completed in This Session (2026-06-12, commits d689951 + aa0a146)
- **E2E (Phase 2) — EXECUTED & VERIFIED**:
  - Ran full E2E suite against live app at `http://localhost:8080` (docker unavailable in agent env; app was already running locally).
  - **6/6 Playwright tests passed** (~24s): Login, AI quote+travel+PDF, quote→job, job→invoice, multi-tenant isolation, notifications.
  - Added `E2EHelpers.EnsureAppReadyAsync()` — polls `/health/ready` before browser launch for CI/docker reliability.
  - No selector fixes required; existing data-testid + helpers are stable.

- **Phase 5 (Production Hardening) — completed items**:
  - **Serilog**: Added `UseSerilogRequestLogging` with tenant id in request log template (after `TenantLoggingMiddleware`).
  - **Health checks**: `/health` and `/health/ready` now return structured JSON via `UIResponseWriter` (verified HTTP 200).
  - **Counter reliability**: Added `TenantServiceTests.cs` (6 tests) for all `Increment*` methods + tenant isolation; counters use isolated scoped DbContext (no `Task.Run`). Updated stale fire-and-forget comment in `AiAssistantServiceTests.cs`.
  - **Security**: Gated `/login-complete` to Development/Testing environments only (E2E still works via docker `ASPNETCORE_ENVIRONMENT=Development`).

- **Testing totals**: **58/58 green** (52 unit + 6 E2E) after `dotnet test`.

### Prior session work (still valid — do not redo)
- **E2E (Phase 2) - Code production-ready for key flows**:
  - New `tests/METERP.E2ETests/E2EHelpers.cs`: Centralized helpers including `LoginAsync` (uses data-testid), `ClickByTestIdAsync`/`FillByTestIdAsync`/`WaitForTestIdAsync`, `GotoRelativeAsync`, `WaitAndSaveDownloadAsync` (for PDFs), `RunWithScreenshotOnFailureAsync`, `WaitForAppReadyAsync`, `TakeScreenshotAsync`.
  - Major refactor of `tests/METERP.E2ETests/PlaywrightE2EStub.cs`: Now uses helpers everywhere, data-testid selectors, auto-screenshots on failure, proper waits. Covers:
    1. Login with demo creds.
    2. AI Copilot creates Quote (travel in prompt) + multiple PDF downloads (real post-apply + demo).
    3. Convert Quote → Job (travel preserved).
    4. Job (travel + labor) → Invoice.
    5-6. Expansion skeletons for multi-tenant isolation + notifications (improved with helpers/assertions).
  - **Extensive data-testid added** (20+ for reliability):
    - Login.razor: login-email, login-password, login-submit.
    - AICopilot.razor: ai-prompt-input, ai-ask-button, ai-apply-quote/job, ai-export-full-pdf, ai-create-real-*, ai-export-response-pdf, ai-demo-*-pdf, ai-download-real-*-pdf (and -detail), ai-quick-prompt-* (variance, travel, utilization, transformer, lowstock), ai-optimize-bid, ai-copy-response, ai-feedback-thumbs-*, ai-copilot-launcher (MainLayout).
    - QuoteList.razor / Quotes.razor: quotes-table, quote-list-item, enhance-with-ai, convert-to-job.
    - JobList.razor / Jobs.razor: jobs-table, create-invoice-from-job-detail (new button + callback wiring).
    - Invoices.razor: invoices-table, create-invoice-from-job, new-invoice-button, view-invoice, close-invoice-view, invoice-line-items-header.
    - Notifications.razor: notifications-list, notifications-mark-all, notification-item.
  - AI E2E test enhanced to cover real PDF downloads using new ids + download helper.
  - E2E project now has proper .csproj, added to solution, builds cleanly.

- **Phase 3 (AI/Commercial) light start**:
  - Expanded `tests/METERP.Application.Tests/AiAssistantServiceTests.cs` (now 35 unit tests total):
    - IsConfigured (false/true cases).
    - Suggest/Analyze return null when !configured, throttled, or ai feature disabled.
    - Counter increment wiring (mocks).
    - Throttling behavior.
  - Direct support for E2E AI flows and sellable commercial model.

- **Other**:
  - Updated AGENTS.md, .cursor/rules/*.mdc (testing emphasis), and this plan multiple times.
  - All changes: Follow layers, extracted pure calc logic, used data-testid, ran `dotnet test` after changes (units 35/35 green, E2E builds), no business logic in Razor, preserved travel explicit / soft-delete / counters / multi-tenancy.
  - Verified: `dotnet build` E2E clean, units 35 green after every step.

### Current Exact State (pick up here)
- **Testing**: **120/120 green** — 110 unit + 4 web integration + 6 E2E.
- **Phase 2**: **Complete**.
- **Phase 3**: **Complete** — apply services + parser + full mocked HTTP paths (suggest, analyze, ask).
- **Phase 5**: **Largely complete** — Seq, quota UI, Serilog, health, AI rate limit, loopback SMTP integration test. Remaining: 2FA, OpenTelemetry, MailKit CVE bump.
- **Phase 4**: **Largely complete** — Inventory, Notification, Customer, Supplier, Asset, PO, Employee, SalesOrder, Finance/GL tested. Remaining: Opportunities (needs real entity/service).
- **Git**: pending commit for this chunk.

### Immediate Next Steps for Cursor (Start Here to Continue Exactly)
1. **Phase 4**: Opportunity entity + `IOpportunityService` (replace localStorage stub); unit tests.
2. **Phase 5**: 2FA stub hardening; bump MailKit for NU1902; OpenTelemetry wiring.
3. **E2E**: Expand Playwright for Sales Orders page or Finance CSV export if UI flows stabilize.
4. Run `dotnet test` before any commit.

**Key files for continuity**: This COMPLETION_PLAN.md (always read top first), AGENTS.md, .cursor/rules/meterp-*.mdc, the E2E files (helpers + stub), AiAssistantServiceTests.cs, and recently touched Razor files (for data-testid).

**Git**: All changes in this session should be committed with message referencing "E2E helpers + data-testid + AI tests + plan handoff for Cursor continuity".

**Do not**: Start unrelated refactors. Ask user before large scope changes. Preserve demo paths.

---

# METERP Completion & Full Testing Plan (original content continues below)

**Purpose:** Drive the ERP from "broad surface + minimal tests" to a complete, sellable, well-tested system where **no feature or flow is considered done without comprehensive automated tests**.

This plan is the source of truth for work while the goal is "everything tested fully". Follow it in addition to `AGENTS.md` and the `.cursor/rules/*.mdc` files (especially the new `meterp-testing.mdc`).

## Guiding Principles
- **Testing is non-negotiable.** A change is not complete until `dotnet test` is green and the new behavior has dedicated tests.
- Follow the strict new-feature checklist in the Cursor rules (including tests as step 8).
- Prefer extracting pure, testable calculation logic (recalc, variance, totals) so it can be verified without full service + DB setup.
- Preserve and strengthen: multi-tenancy (global filters + TenantId), computed `LineTotal`s, explicit travel costs, soft-delete safety, usage counter increments, `HasFeature` gating.
- Core spine: **Opportunity (CRM) → Quote → Sales Order → Job → Invoice** with travel explicit at every stage.
- Use small, verifiable increments. Run tests frequently.
- Definition of Done (DoD) for any work item:
  1. Implementation follows layers (Domain → I*Service → Impl → registration → UI if needed).
  2. All affected public service methods have unit tests covering happy path + edges + side effects (recalc, counters, soft deletes).
  3. For spine flows, relevant E2E test(s) exist or are expanded.
  4. `dotnet test` passes (full suite or targeted).
  5. No cross-tenant leaks, no manual LineTotal assignment, no business logic in Razor.
  6. Documentation / comments updated if behavior changed.
  7. Committed with clear message referencing tests.

## Current State Snapshot (from audit)
- Many I*Service interfaces + implementations exist (QuoteService, JobService, InvoiceService, AiAssistantService, TenantService, etc.).
- Recalculation logic lives as private methods in services; usage counters are incremented (often via fire-and-forget `Task.Run`).
- Core conversions exist: `QuoteService.ConvertToJobAsync`, `InvoiceService.CreateFromJobAsync`.
- Only 12 unit tests, all entity/calculation focused in one file (`JobLaborTests.cs`). No service tests.
- E2E is a single placeholder stub.
- Commercial features (counters on Tenant, feature flags) are wired but lightly verified.
- AI has throttling + feature check but no unit tests.

## Phase 1: Core Spine Testing & Hardening (Highest Priority)
Goal: Every important method on Quote/Job/Invoice services + the conversion flows has solid unit tests. Make recalc, travel, soft-delete, and counter side-effects reliable and verified.

### 1.1 QuoteService & Quote Flow
- **Work:** Review and harden `CreateAsync`, `AddLineAsync`/`UpdateLineAsync`/`DeleteLineAsync`, `RecalculateTotals`, `ConvertToJobAsync`.
- **Test Requirements (minimum):**
  - Unit tests for recalc after every line operation (subtotal, tax at various rates, total). Verify soft-deleted lines are excluded.
  - Full test for `ConvertToJobAsync`: creates Job with correct `QuotedTotal`, links to original Quote/Customer, sets status, increments tenant quote counter (verify via mock or side effect).
  - Tests for status transition on conversion.
  - Edge cases: quote with no lines, all lines deleted, zero tax rate, large numbers.
- **Extract for testability:** Consider moving `RecalculateTotals` logic to a static `QuoteCalculations` helper in Domain or Common.
- **E2E tie-in:** See Phase 2.

### 1.2 JobService + Variance + Travel (Contractor Differentiator)
- **Work:** Harden `CreateAsync`, `AddCostAsync`/`DeleteCostAsync`, `AddLaborAsync`/`DeleteLaborAsync`, `UpdateStatusAsync`. Ensure explicit "Travel" costs and labor are handled.
- **Test Requirements:**
  - Expand / replace manual variance logic from existing tests with tests against real service methods or extracted calculators.
  - `ActualTotal_IncludesMaterialLaborAndExplicitTravel` style cases, now driven through the service.
  - Soft-deleted costs and labor must not affect `ActualCost` or any variance calculation.
  - Add labor + travel cost → verify totals exposed via GetById (includes collections).
  - Counter increment on Create (Job count).
- **Key assertions:** Travel costs remain visible and separate in the model and calculations.

### 1.3 InvoiceService + Job → Invoice Flow
- **Work:** Harden `CreateAsync`, line management, `CreateFromJobAsync`, `RecalculateTotals`.
- **Test Requirements:**
  - `CreateFromJobAsync` copies lines from originating Quote when present (preferred path) and falls back correctly.
  - Revenue is passed to `IncrementInvoiceCountAsync`.
  - Recalc works after Add/Update/Delete line (soft deletes excluded).
  - Counter + revenue tracking tests.
- **Spine completeness:** Test the full Quote → Job → Invoice chain in unit tests (even if using in-memory or mocks).

### 1.4 TenantService & Commercial Usage (Sellable Foundation)
- **Work:** Review increment methods. Consider making side effects more testable (e.g. return success or use a testable abstraction later).
- **Test Requirements:**
  - Direct tests for all `Increment*` methods (job, quote, invoice with revenue, AI).
  - Verify `LastActivityUtc` updates where applicable.
  - `HasFeature` tests (already partially present — expand).
  - Isolation: increments on one tenant do not affect another (use real or mocked context).
- **Future:** Move toward quota enforcement (Phase 3/4).

### 1.5 Cross-Cutting for Spine
- Extract or centralize recalculation helpers.
- Add tests that would have caught previous issues (e.g. manual LineTotal, ignoring IsDeleted).
- Verify that `ITenantProvider` / current tenant is respected in all service operations.

**Phase 1 Exit Criteria:** All public methods on the four core services (Quote, Job, Invoice, Tenant) that have logic have passing unit tests. `dotnet test` green. Core conversions exercised in tests.

**Progress (as of this session - continued without stopping):**
- Added Moq + EF InMemory + Infrastructure ref to METERP.Application.Tests.
- Extracted `RecalculateTotals()` to Quote/Invoice in Domain; added `GetActualTotal()` + `GetVariance()` to Job (pure, explicit travel + labor + soft-delete safe).
- Updated services to use entity methods.
- Cleaned old manual calc tests to use new pure methods.
- **Quote phase**: `QuoteTests.cs` (6+ tests for recalc, Create, ConvertToJob, line mgmt, counters).
- **Job phase**: `JobTests.cs` (6+ tests for GetActualTotal/Variance with travel explicit, AddCost/AddLabor, soft deletes, counters). Old variance tests updated.
- **Invoice phase**: `InvoiceTests.cs` (3 tests for CreateFromJob preferring quote lines (travel preserved), fallback, AddLine recalc, revenue counter via mock). Fixed service to do revenue Increment *after* final recalc for correctness.
- Tenant/commercial covered via mocks in spine tests + existing CommercialUsageTrackingTests.
- All old/new tests updated for consistency with new pure calcs.
- Multiple full `dotnet test` runs (with fixes to service for revenue timing, Get methods, test data); reached 27 tests, 0 failures (24-27 range across runs as methods added).
- Phase 1 core spine (Quote/Job/Invoice conversions, recalc, explicit travel, counters, soft deletes) now has solid, repeatable unit test coverage following the strict rules.
- Additional cleanup: old manual variance tests converted to use pure entity methods. Service revenue side-effect moved after final recalc for correctness.
- Counter side-effects tested via mocks (fire-and-forget remains but intent verified; noted for future reliability hardening in Phase 5).

## Phase 2: Real E2E Coverage (Critical for Sellable Confidence)
**Progress (continued work):**
- Created missing `METERP.E2ETests.csproj` (Playwright + xunit) and added to solution via `dotnet sln add`.
- Completely replaced the placeholder content in `PlaywrightE2EStub.cs` with a proper `E2EFlowTests` class implementing `IAsyncLifetime` for browser lifecycle.
- Implemented 4 key tests matching the required flows:
  1. `Login_Succeeds_With_Demo_Credentials`
  2. `AI_Copilot_Creates_Quote_With_Travel_And_Downloads_PDF` (includes travel in prompt, verifies travel line and PDF download)
  3. `Convert_Quote_To_Job_Preserves_Travel_Costs`
  4. `Job_With_Travel_And_Labor_Creates_Invoice_With_Correct_Totals`
- All tests use `[Trait("Category", "E2E")]`.
- Realistic selectors based on current UI (aria-labels, button text like "Apply", "PDF", "Generate", table roles, etc.). Base URL set to docker 8080.
- Detailed comments for setup (`playwright install`) and running (`dotnet test --filter "Category=E2E"`).
- **Current status (Phase 2 COMPLETE — 2026-06-12):** 6/6 E2E tests executed and green (~24s). Added `EnsureAppReadyAsync` health polling. Full suite: 58/58 green.

- **Exit:** ✅ At least the 4-5 key flows have working, non-flaky E2E tests that can run locally and in CI. **Phase 2 exit criteria met.**

## Phase 3: AI Copilot & Commercial Features — Full Test Coverage
**Progress (2026-06-12 continued):**
- `IAiQuoteApplyService` / `AiQuoteApplyService` — apply logic extracted from AICopilot; travel fallback + structured lines.
- `AiQuoteApplyServiceTests` — 5 tests. Units now 57 + 2 web + 6 E2E = 65 total.

**Progress (started after E2E stabilization):**
- Created `AiAssistantServiceTests.cs` with coverage for:
  - `IsConfigured` (key + enabled flag).
  - Returns null when not configured.
  - Throttling behavior (rapid calls return null).
  - Feature flag enforcement (`HasFeature("ai")` blocks and returns null).
  - Counter increment wiring on the call path (via mocks).
- This directly supports the AI E2E flows (suggestions, apply, commercial tracking).
- Units now at 35 green (added IsConfigured true test + previous Analyze feature disabled). E2E phase code is now mature with extensive data-testid, helpers, and full coverage of key flows (including real PDF downloads).

- **AiAssistantService tests (remaining):**
  - `SuggestQuoteLinesAsync` and `AnalyzeJobVarianceAsync` produce usable output (or null gracefully) when possible.
  - Apply logic (per-entry) exercised (integration with Quote/Job services).
- Commercial visibility and enforcement in UI/services.
- Begin turning counters into something closer to quotas.

## Phase 4: Supporting Modules — Bring to Same Standard
For each module with an I*Service (Customer, Inventory, Asset, PurchaseOrder, Supplier, Employee, Finance, SalesOrder, User, etc.):
- Ensure the service methods with business logic have unit tests (CRUD + any domain rules).
- Add tests for any cross-cutting concerns (soft delete, tenant isolation).
- If a module participates in the spine or AI (e.g. Inventory usage on jobs, Opportunities feeding Quotes), add flow tests.
- Priority order: modules that feed the spine first, then others.

Specific callouts:
- Inventory + StockTransaction + job usage tracking.
- Opportunities (CRM) → Quote handoff.
- Notifications triggered from key events.
- Audit log for sensitive actions.

## Phase 5: Production Hardening + Sellable Maturity
**Progress (2026-06-12):**
- ✅ Usage counter increments reliable (isolated scoped DbContext; `TenantServiceTests` — 6 tests).
- ✅ Structured logging (Serilog console/file/Seq + `TenantLoggingMiddleware` + `UseSerilogRequestLogging`).
- ✅ Health checks (`/health` liveness, `/health/ready` DB+AI with JSON response); docker-compose healthcheck wired.
- ✅ Rate limiting (global 300/min; health excluded).
- ✅ Secrets: `.env.example`, UserSecretsId on Web project.
- ✅ Security: `/login-complete` gated to Development/Testing.
- Remaining: Seq in compose, WebApplicationFactory health tests, AI-specific rate limits, Redis cache, real email, billing quota UI.

- E2E + unit tests must still pass after any hardening. **Currently 58/58 green.**

## Execution Guidelines
- **Work in this order** unless the user directs otherwise: Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5.
- For every item, create or update tests as part of the same logical change.
- Use the Cursor rules checklist on every task.
- Before large sessions in Cursor, paste the relevant section of this plan + key rules into the prompt.
- Track progress by updating this file (mark sections complete with dates or commit refs).
- Run full `dotnet test` (or at least the Application.Tests + any E2E that can run) before marking a phase or major item complete.

## Quick Start for Next Session (Cursor or here)
1. Read AGENTS.md + all .cursor/rules/*.mdc + this COMPLETION_PLAN.md.
2. Pick the next item in Phase 1 (start with QuoteService recalc + line operations + ConvertToJob if not already heavily tested).
3. Write the tests first or TDD-style.
4. Implement / harden.
5. `dotnet test`
6. Commit with test evidence.
7. Update this plan.

## Notes & Open Questions (update as we go)
- Should we add Moq/NSubstitute to the test project for clean service unit tests? (Recommended for Phase 1.)
- Should recalc helpers be extracted early to simplify all downstream tests?
- How far to push the usage counters toward real quotas in this cycle?
- Any specific module the user wants accelerated?

**This plan + the enhanced rules should give Cursor (or any agent) a much higher chance of delivering "everything tested fully" instead of more surface area.**

Update this file as work progresses. Ask the user for reprioritization when needed.
