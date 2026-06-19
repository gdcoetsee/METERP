# METERP Completion & Full Testing Plan

## 📋 SESSION 54 — COMPLETE (2026-06-19)

**Delivered:** **359/359 green** (275 unit + 29 web + 55 E2E).

### Session 54 deliverables
- **Invoice list cache tests:** full mirror of Quote/Job coverage — stale reads, search bypass, whitespace cache, CRUD + line mutations (+9 unit).
- **E2E:** `Tenants_Edit_Quota_Exceeded_Shows_Banner` (55th).

### Next session priorities
1. Push branch and verify GitHub Actions E2E job green.
2. SalesOrder / PurchaseOrder list cache tests (remaining document services).
3. Invoice pagination cache key test (mirror Quote page-size test).

---

## 📋 SESSION 53 — COMPLETE (2026-06-19)

**Delivered:** **349/349 green** (266 unit + 29 web + 54 E2E).

### Session 53 deliverables
- **Serilog test hardening:** `SerilogTestLoggerScope` (process-wide lock) + `TenantLoggingTestCollection` (disable parallelization); eliminates flaky `TenantLoggingMiddlewareTests`.
- **Tenants admin quota UX:** edit form uses `QuotaUsageBadge` (`tenants-edit-quota-*`); exceeded banner for admin view.
- **E2E:** `Tenants_Edit_Form_Shows_Quota_Badges` (54th).

### Next session priorities
1. Push branch and verify GitHub Actions E2E job green.
2. Invoice list cache tests (mirror Quote/Job coverage).
3. Quota exceeded banner on Tenants edit E2E (optional stretch).

---

## 📋 SESSION 52 — COMPLETE (2026-06-19)

**Delivered:** **347/347 green** (265 unit + 29 web + 53 E2E).

### Session 52 deliverables
- **Labor cache invalidation tests:** `AddLaborAsync` / `DeleteLaborAsync` bust job list cache (+2 unit).
- **Shared quota badge:** `QuotaUsageBadge.razor` component; `TenantQuotaDefaults.GetQuotaTestId`; Home + AccountBilling refactored (+2 unit theory).
- **Imports:** `METERP.Web.Components.Shared` in `_Imports.razor`.

### Next session priorities
1. Push branch and verify GitHub Actions E2E job green.
2. Harden flaky `TenantLoggingMiddlewareTests` (Serilog static logger isolation).
3. Tenants admin usage badges (optional) using `QuotaUsageBadge`.

---

## 📋 SESSION 51 — COMPLETE (2026-06-19)

**Delivered:** **343/343 green** (261 unit + 29 web + 53 E2E).

### Session 51 deliverables
- **Line/cost cache invalidation tests:** Quote `AddLineAsync` / `UpdateLineAsync` / `DeleteLineAsync`; Job `AddCostAsync` / `DeleteCostAsync` (+5 unit).
- **Account billing quota UX:** badges aligned with home (`data-quota-status`, tooltips, test ids); exceeded banner on billing usage card.
- **E2E:** `AccountBilling_Quota_Exceeded_Shows_Banner` (53rd); billing page asserts quota badge status + tooltip.

### Next session priorities
1. Push branch and verify GitHub Actions E2E job green.
2. Job labor cache invalidation tests (`AddLaborAsync` / `DeleteLaborAsync`).
3. Extract shared quota badge render helper (Home + AccountBilling + Tenants).

---

## 📋 SESSION 50 — COMPLETE (2026-06-19)

**Delivered:** **337/337 green** (256 unit + 29 web + 52 E2E).

### Session 50 deliverables
- **Quota UX polish:** `QuotaUsageStatus`, `GetQuotaStatus`, `GetQuotaTooltip`, `GetExceededQuotaLabels`; home badges with `title`/`aria-label`/`data-quota-status`; exceeded banner lists which limits are hit (`home-quota-exceeded-summary`).
- **Cache invalidation tests:** `UpdateAsync` + `DeleteAsync` invalidate quote/job list caches (+4 unit).
- **Quota helper tests:** warning threshold, tooltip copy, exceeded label filtering (+3 unit).
- **E2E:** `Home_Quota_Exceeded_Shows_Upgrade_Banner` asserts `data-quota-status`, summary text, and tooltip.

### Next session priorities
1. Push branch and verify GitHub Actions E2E job green.
2. List-cache invalidation on line mutations (Quote/Job line add/update/delete).
3. Account billing panel quota badges aligned with home (tooltips + status).

---

## 📋 SESSION 49 — COMPLETE (2026-06-19)

**Delivered:** **330/330 green** (249 unit + 29 web + 52 E2E).

### Session 49 deliverables
- **List-cache unit tests:** `GetAllAsync_WithWhitespaceSearch_UsesCache` (Quote + Job); `GetAllAsync_DifferentPageSizes_UseSeparateCacheEntries` (Quote pagination cache keys).
- **Mailpit E2E:** `AccountSecurity_EnableTwoFactor_Delivers_To_Mailpit_When_Smtp_Configured` (52nd E2E); helpers for Mailpit API (`IsMailpitAvailableAsync`, `DeleteAllMailpitMessagesAsync`, `GetMailpitMessagesAsync`, `WaitForMailpitSubjectAsync`) with correct `To[].Address` parsing.
- **CI:** wait for Mailpit at `:8025/api/v1/info`; E2E env `METERP_REQUIRE_MAILPIT=true` + `METERP_MAILPIT_API_URL`.

### Next session priorities
1. Push branch and verify GitHub Actions E2E job green (Mailpit gate).
2. Quota enforcement UX polish (per-quota tooltips, upgrade flow).
3. List-cache performance profiling or invalidation-on-mutation tests.

---

## 📋 SESSION 48 — COMPLETE (2026-06-19)

**Delivered:** **326/326 green** (246 unit + 29 web + 51 E2E).

### Session 48 deliverables
- **CI E2E hardening:** explicit `docker compose build --pull web`; smoke-check `/e2e/reset-demo-quotas` + `/e2e/begin-email-capture` before Playwright.
- **Quota UX:** `TenantQuotaDefaults.IsAtOrOverLimit` / `HasAnyQuotaAtOrOverLimit`; home `home-quota-exceeded-banner` + per-quota test ids; `Home_Quota_Exceeded_Shows_Upgrade_Banner` (51st E2E).
- **E2E stability:** job→invoice test timeout 30s for invoice detail panel.

### Next session priorities
1. List-cache performance profiling or additional search-bypass tests.
2. Mailpit API E2E stretch (optional).
3. Push branch and verify GitHub Actions E2E job green.

---

## 📋 SESSION 47 — COMPLETE (2026-06-19)

**Delivered:** **323/323 green** (244 unit + 29 web + 50 E2E) — full suite verified locally.

### Session 47 deliverables
- **Full E2E verification:** 50/50 Playwright green against Release build on `localhost:8080` (~5m13s).
- **Mailpit in docker-compose:** `mailpit` service (SMTP :1025, UI :8025); web defaults `Email__SmtpHost=mailpit` for real SMTP demos.
- **Package bumps:** OTel Exporter/Console/Hosting **1.16.0**; `AspNetCore.HealthChecks.UI.Client` **9.0.0**; `dotnet list package --vulnerable` still **zero**.

### Next session priorities
1. CI workflow: ensure E2E job uses fresh build (avoid stale 404 on `/e2e/*` endpoints).
2. Mailpit E2E stretch — assert message in Mailpit API after 2FA enable (optional; capture test already covers flow).
3. Remaining sellable hardening: quota enforcement UX polish, performance profiling on list caches.

---

## 📋 SESSION 46 — COMPLETE (2026-06-19)

**Delivered:** **323/323 green** (244 unit + 29 web + 50 E2E when app running).

### Session 46 deliverables
- **.NET 9.0.x patch bumps:** Microsoft.AspNetCore/EF/Extensions `9.0.*` → resolved **9.0.17**; `Microsoft.AspNetCore.Http.Abstractions` **2.3.11**; `Serilog.Sinks.Console` **6.1.1**; `dotnet list package --vulnerable` still **zero** on Web.
- **2FA email E2E:** `E2EEmailCaptureStore` + `CapturingEmailSender` (Development); `/e2e/begin-email-capture`, `/e2e/captured-emails`, `/e2e/clear-email-capture`; `AccountSecurity_EnableTwoFactor_Captures_Security_Email` (50th Playwright); `E2EEmailCaptureStoreTests` (+2 web).
- **Observability docs:** `.env.example` + README — Seq + OTel collector profile usage.

### Next session priorities
1. Run full E2E suite in CI / against docker-compose to confirm 323 green.
2. Mailpit or real SMTP in docker-compose for non-capture email integration demos.
3. Evaluate OTel 1.16.x bump (when CVE-free) and AspNetCore.HealthChecks.UI.Client 9.x.

---

## 📋 SESSION 45 — COMPLETE (2026-06-19)

**Delivered:** **320/320 green** (244 unit + 27 web + 49 E2E).

### Session 45 deliverables
- **OTel collector docker-compose profile:** `otel-collector` service (`--profile observability`); `docker/otel-collector-config.yaml`; web env passthrough for `OpenTelemetry__OtlpEndpoint`.
- **OTLP export integration tests:** `LoopbackOtlpCollector` + `OpenTelemetryOtlpExportTests` — verifies trace/metric payloads reach loopback OTLP/HTTP receiver (service name in protobuf).
- **OTLP HTTP path fix:** `OpenTelemetryOptions.OtlpProtocol` (Grpc/HttpProtobuf); signal-specific `/v1/traces` + `/v1/metrics` endpoints for HTTP.
- **2FA security email delivery:** `TwoFactorAuthService` sends best-effort SMTP notifications on enable/disable; `TwoFactorAuthServiceTests` (+2).

### Next session priorities
1. ASP.NET / EF 9.0.x runtime patch bumps when available (framework-level CVEs).
2. E2E for 2FA email path when SMTP configured (stretch).
3. Production: wire Seq + OTel collector together in observability profile docs/README.

---

## 📋 SESSION 44 — COMPLETE (2026-06-19)

**Delivered:** **315/315 green** (241 unit + 25 web + 49 E2E).

### Session 44 deliverables
- **OpenTelemetry CVE fix (NU1902):** All OTel packages aligned to 1.15.x (`Exporter.*` 1.15.3, `Instrumentation.AspNetCore` 1.15.2, `Instrumentation.Http` 1.15.1, `Instrumentation.EntityFrameworkCore` 1.15.1-beta.1).
- **Transitive CVE cleanup:** `System.Security.Cryptography.Xml` 9.0.15, `System.Text.Encodings.Web` 9.0.0, `Microsoft.AspNetCore.Http.Abstractions` 2.3.0 — `dotnet list package --vulnerable` now reports **zero** vulnerable packages on Web.
- **MailKit:** Already at 4.17.0 (latest on NuGet; no newer patch available).
- **OpenTelemetry integration tests:** `OpenTelemetryExtensionsTests` — DI registration, OTLP config, host startup with OTLP endpoint (3 new web tests).

### Next session priorities
1. OpenTelemetry integration test with real test OTLP collector (stretch — verify export payloads).
2. ASP.NET / EF 9.0.x runtime patch bumps when available (framework-level CVEs).
3. Production maturity: 2FA email delivery, OTel collector docker-compose profile.

---

## 📋 SESSION 43 — COMPLETE (2026-06-19)

**Delivered:** **312/312 green** (241 unit + 22 web + 49 E2E).

### Session 43 deliverables
- **Job/invoice quota E2E:** `/e2e/ensure-job-quota-exceeded`, `/e2e/ensure-invoice-quota-exceeded`; `Quote_Convert_To_Job_Shows_Quota_Exceeded_Toast` + `Job_Create_Invoice_Shows_Quota_Exceeded_Toast` (48th–49th Playwright).
- **OpenTelemetry production wiring:** `OpenTelemetryOptions`, `AddMeterpOpenTelemetry` (OTLP when configured, console in Development); `.env.example` docs; `OpenTelemetryOptionsTests`.
- **2FA hardening:** `PendingTwoFactorChallengeStoreTests` (create/consume/unknown); `LoginTwoFactorEndpointTests` (invalid/missing token → login redirect).
- **Invoices UI:** `SaveInvoice` catches `QuotaExceededException` with toast.

### Next session priorities
1. Bump `OpenTelemetry.Exporter.OpenTelemetryProtocol` (NU1902) + MailKit CVE when patched versions available.
2. OpenTelemetry integration test with test OTLP collector (stretch).

---

## 📋 SESSION 42 — COMPLETE (2026-06-19)

**Delivered:** **303/303 green** (239 unit + 17 web + 47 E2E).

### Session 42 deliverables
- **Quota-exceeded E2E:** `E2EDemoQuotaSeeder` + `/e2e/ensure-quote-quota-exceeded` + `/e2e/reset-demo-quotas`; `Quotes_Save_Shows_Quota_Exceeded_Toast` (47th Playwright test).
- **Phase C — Production hardening:**
  - `TenantService.IncrementCounterAsync` retries on `DbUpdateConcurrencyException`.
  - `IncrementQuoteCountAsync_ConcurrentIncrements_AllPersisted` (SQLite, 12 parallel).
  - `TenantLoggingMiddlewareTests` — Serilog `LogContext` tenant enrichment.
  - `SecretsAuditTests` — `.env` gitignore, `.env.example`, no hardcoded keys, `UserSecretsId`.
- **Scheduling E2E:** `Scheduling_Quick_Adds_Labor` fallback when `scheduling-ready` slow.

### Next session priorities
1. OpenTelemetry / 2FA production stubs.
2. Additional quota E2E (job/invoice limits) if billing UX needs coverage.

---

## 📋 SESSION 41 — COMPLETE (2026-06-19)

**Delivered:** **295/295 green** (238 unit + 11 web + 46 E2E).

### Session 41 deliverables
- **Job→invoice E2E stability:** `E2EDemoInvoiceJobSeeder.EnsureInvoiceReadyDemoJobAsync` + `POST /e2e/ensure-demo-invoice-job` (resets invoices, ensures travel/labor, bumps `CreatedDate`); `jobs-search` on [`Jobs.razor`](src/METERP.Web/Components/Pages/Jobs.razor).
- **Sales Order → Job E2E:** `E2EConvertibleSalesOrderSeeder` + `/e2e/ensure-convertible-sales-order`; `sales-order-row-e2e-convertible`, `sales-order-convert-to-job`, `sales-orders-search` test ids; `SalesOrder_Convert_To_Job_Creates_Job_With_Travel` (46th Playwright test).
- **E2E helpers:** `EnsureDemoInvoiceJobAsync`, `EnsureConvertibleSalesOrderAsync` wired into `EnsureAppReadyAsync`.
- **E2E stability fixes:** `Quotes_Edit_Opens_Lines` uses new-quote flow (draft rows consumed by prior runs); `PurchaseOrders_Receive` asserts by PO number + toast (row test id changes on receive); AI copilot test longer timeouts + enabled-button guard.

### Next session priorities (credit-efficient)
1. **Stretch:** quota-exceeded toast E2E; scheduling E2E flakes if full suite regresses.
2. **Phase C — Production hardening:** Concurrent `TenantService` increment test; secrets audit; Serilog tenant logging verification.

**Per chunk:** `dotnet test` → update this handoff → commit with session prefix + test counts.

---

## 📋 SESSION 40 — COMPLETE (2026-06-18)

**Baseline delivered:** **294/294 green** (238 unit + 11 web + 45 E2E). Commits: `01ba331` (GP pricing, AI settings) + `09c594f` (convertible quote seeder, CRM search E2E, quota/E2E stability).

### Session 40 deliverables
- **Quote GP pricing:** `UnitCost`, `GrossProfitPercent`, blended margin UI; `QuotePricing` + unit tests.
- **Tenant AI settings:** `/settings/ai`, `TenantAiSettingsService`, `AiConfigurationResolver`.
- **Blazor fixes:** async Redis cache invalidation; `UpdateLineAsync` tracked-entity patch; convert-to-job visible in view mode.
- **E2E hardening:** `E2EReceiveDemoPoSeeder` + endpoint; `E2EConvertibleQuoteSeeder` + `/e2e/ensure-convertible-quote` (endpoint-only — **not** startup seed, avoids quota crash); demo Acme `Max*PerMonth = 10_000`.
- **Phase B (partial):** `Assets_Search_FiltersByName` + `Employees_Search_FiltersByName` E2E.

---

## 🚨 CURSOR / NEXT SESSION HANDOFF (Read this FIRST - Current as of 2026-06-19 session)

**Session 54 complete — 359/359 green. Invoice cache tests, Tenants quota E2E.**

### Exact Work Completed — Latest (2026-06-19, session 54)
- **Invoice cache tests:** 9 tests covering list cache + invalidation on mutations.
- **E2E:** Tenants edit quota-exceeded banner (+1).
- **Testing target**: **359/359 green** (275 unit + 29 web + 55 E2E).

### Exact Work Completed — Prior (2026-06-19, session 53)
- **Tests:** Serilog logger isolation for middleware tests (no flake).
- **Tenants UX:** `QuotaUsageBadge` on edit form + exceeded banner (+1 E2E).
- **Testing target**: **349/349 green** (266 unit + 29 web + 54 E2E).

### Exact Work Completed — Prior (2026-06-19, session 52)
- **Cache tests:** job labor add/delete invalidate list cache (+2 unit).
- **Quota UX:** shared `QuotaUsageBadge` + `GetQuotaTestId` (+2 unit).
- **Testing target**: **347/347 green** (265 unit + 29 web + 53 E2E).

### Exact Work Completed — Prior (2026-06-19, session 51)
- **Cache tests:** quote line + job cost mutations invalidate list cache (+5 unit).
- **Billing UX:** quota badges/tooltips/exceeded banner on `AccountBillingPanel` (+1 E2E).
- **Testing target**: **343/343 green** (261 unit + 29 web + 53 E2E).

### Exact Work Completed — Prior (2026-06-19, session 50)
- **Quota UX:** status/tooltip helpers; per-badge `data-quota-status`; exceeded summary banner (+3 unit, E2E extended).
- **Cache tests:** Update/Delete invalidation for Quote + Job lists (+4 unit).
- **Testing target**: **337/337 green** (256 unit + 29 web + 52 E2E).

### Exact Work Completed — Prior (2026-06-19, session 49)
- **Cache tests:** whitespace search bypass + pagination cache keys (+3 unit).
- **Mailpit E2E:** 2FA enable delivers to Mailpit SMTP; API helpers with Address parsing (+1 E2E).
- **CI:** Mailpit readiness wait + `METERP_REQUIRE_MAILPIT` in E2E job.
- **Testing target**: **330/330 green** (249 unit + 29 web + 52 E2E).

### Exact Work Completed — Prior (2026-06-19, session 48)
- **CI:** fresh docker web build + E2E dev endpoint smoke checks.
- **Quota UX:** exceeded banner on dashboard + upgrade CTA; +2 unit tests, +1 E2E.
- **Testing target**: **326/326 green** (246 unit + 29 web + 51 E2E).

### Exact Work Completed — Prior (2026-06-19, session 47)
- **E2E:** 50/50 Playwright green (Release app on :8080).
- **Mailpit:** docker-compose service + default Email__* env on web.
- **Packages:** OTel 1.16.0, HealthChecks.UI.Client 9.0.0; zero vulnerable packages.
- **Testing target**: **323/323 green** (244 unit + 29 web + 50 E2E).

### Exact Work Completed — Prior (2026-06-19, session 46)
- **Package patches:** EF/ASP.NET 9.0.17, Http.Abstractions 2.3.11; zero vulnerable packages.
- **2FA email E2E:** dev email capture decorator + endpoints; 50th Playwright test.
- **Docs:** observability stack in `.env.example` + README.
- **Testing target**: **323/323 green** (244 unit + 29 web + 50 E2E).

### Exact Work Completed — Prior (2026-06-19, session 45)
- **OTel collector profile:** `docker compose --profile observability up`; collector on 4317/4318.
- **OTLP export tests:** `OpenTelemetryOtlpExportTests` + `LoopbackOtlpCollector` (trace + metric payload verification).
- **OTLP HTTP:** `OtlpProtocol` option; `/v1/traces` + `/v1/metrics` path construction.
- **2FA email:** security notifications on enable/disable via `IEmailSender` (best-effort).
- **Testing target**: **320/320 green** (244 unit + 27 web + 49 E2E).

### Exact Work Completed — Prior (2026-06-19, session 44)
- **OpenTelemetry CVE:** All OTel packages bumped to 1.15.x; NU1902 cleared.
- **Transitive CVEs:** `System.Security.Cryptography.Xml` 9.0.15, `System.Text.Encodings.Web` 9.0.0 — zero vulnerable packages on Web.
- **OpenTelemetry tests:** `OpenTelemetryExtensionsTests` (DI + OTLP host startup).
- **Testing target**: **315/315 green** (241 unit + 25 web + 49 E2E).

### Exact Work Completed — Prior (2026-06-19, session 43)
- **Job/invoice quota E2E:** dev endpoints + `Quote_Convert_To_Job_Shows_Quota_Exceeded_Toast` + `Job_Create_Invoice_Shows_Quota_Exceeded_Toast`.
- **OpenTelemetry:** `OpenTelemetryOptions` + `AddMeterpOpenTelemetry` (OTLP + console); `OpenTelemetryOptionsTests`.
- **2FA:** `PendingTwoFactorChallengeStoreTests` + `LoginTwoFactorEndpointTests`.
- **Testing target**: **312/312 green** (241 unit + 22 web + 49 E2E).

### Exact Work Completed — Prior (2026-06-19, session 42)
- **Quota-exceeded toast E2E:** `E2EDemoQuotaSeeder` + dev endpoints; `Quotes_Save_Shows_Quota_Exceeded_Toast` (47th).
- **Phase C:** concurrent `TenantService` increment test + concurrency retry; `TenantLoggingMiddlewareTests`; `SecretsAuditTests`.
- **Scheduling E2E:** quick-add labor ready-marker fallback.
- **Testing target**: **303/303 green** (239 unit + 17 web + 47 E2E).

### Exact Work Completed — Prior (2026-06-19, session 41)
- **Job→invoice E2E reset:** `POST /e2e/ensure-demo-invoice-job`; per-test `EnsureDemoInvoiceJobAsync`; `jobs-search` test id.
- **Sales Order → Job E2E:** `E2EConvertibleSalesOrderSeeder` + endpoint; `SalesOrder_Convert_To_Job_Creates_Job_With_Travel` (46th).
- **SalesOrders UI:** `sales-order-row-e2e-convertible`, `sales-order-convert-to-job`, `sales-orders-search`.
- **Testing target**: **295/295 green** (238 unit + 11 web + 46 E2E).

### Exact Work Completed — Prior (2026-06-18, session 40)
- **Quote GP pricing + Blazor:** inline unit cost / GP% / blended margin; `QuoteUiHelper.DetachForUi`; cache deadlock fix.
- **Tenant AI settings:** `AiSettings.razor`, migrations, resolver profiles.
- **E2E stability:** `E2EConvertibleQuoteSeeder` + `POST /e2e/ensure-convertible-quote` (endpoint-only, not startup); demo quota 10k.
- **Phase 2 E2E:**
  - `Assets_Search_FiltersByName` (38th) — Transformer / Warehouse filter assertions.
  - `Employees_Search_FiltersByName` (39th) — Johan / Thabo filter assertions.
- **Testing**: **294/294 green** (238 unit + 11 web + 45 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 39)
- **Phase 4 — CRM / HR / Assets UI markers**:
  - `Customers.razor`: `customers-ready`, `customers-table`, `customers-search`; `_customersLoadGeneration`.
  - `Assets.razor`: `assets-ready`, `assets-table`, `assets-search`; `_assetsLoadGeneration`.
  - `Employees.razor`: `employees-ready`, `employees-table`, `employees-search`; `_employeesLoadGeneration`.
- **Phase 2 E2E**:
  - `Customers_Page_Loads_Demo_Customer` (34th) — `Johannesburg General Hospital`.
  - `Customers_Search_FiltersByName` (35th) — search `Hospital` filters table.
  - `Assets_Page_Loads_Demo_Transformer` (36th) — hospital 11kV transformer asset.
  - `Employees_Page_Loads_Demo_Staff` (37th) — `EMP-001` + `Johan`.
  - `PurchaseOrders_Receive_Updates_Inventory` hardened (inventory page reload retry).
- **Testing**: **276/276 green** (228 unit + 11 web + 37 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 38)
- **Phase 4 — Purchasing receive flow**:
  - Seeder: idempotent Sent `E2E receive demo PO` (Panel Supplies → `LED-HB-150` qty 3); soft-deletes stale received demo POs on startup.
  - `PurchaseOrders.razor`: `purchase-order-receive` test id on list + detail receive buttons.
- **Phase 2 E2E**:
  - `PurchaseOrders_Search_FiltersBySupplier` (32nd) — search `Electro` filters PO table.
  - `PurchaseOrders_Receive_Updates_Inventory` (33rd) — confirm dialog, receive updates `LED-HB-150` on-hand +3.
- **Testing**: **272/272 green** (228 unit + 11 web + 33 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 37)
- **Phase 4 — Purchasing UI + tests**:
  - `PurchaseOrderServiceTests` (+1): `GetAllAsync_FiltersBySupplierName`.
  - `PurchaseOrders.razor`: `purchase-orders-ready`, `purchase-orders-table`, `purchase-orders-search`, `purchase-order-view`, `purchase-order-detail` test ids; `_posLoadGeneration` async guard.
  - `Suppliers.razor`: `_suppliersLoadGeneration` async guard.
  - Seeder: idempotent `Panel Supplies CC` second supplier for search filter E2E.
- **Phase 2 E2E**:
  - `PurchaseOrders_Page_Loads_Demo_Po` (30th) — demo `PO-` + `ElectroSupply SA`, view detail shows totals.
  - `Suppliers_Search_FiltersByName` (31st) — search `Panel` filters table rows.
- **Testing**: **270/270 green** (228 unit + 11 web + 31 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 36)
- **Phase 4 — PO receive edge cases**:
  - `PurchaseOrderServiceTests` (+3): `ReceiveAsync_WhenPoNotFound_DoesNotThrow`, `ReceiveAsync_WithoutInventoryLinks_StillMarksReceived`, `ReceiveAsync_OnlyReceiptsLinkedInventoryLines`.
- **Phase 2 E2E**:
  - `Suppliers_Page_Loads_Demo_Vendor` (29th) — demo `ElectroSupply SA` in suppliers table.
  - `Suppliers.razor`: `suppliers-ready`, `suppliers-table`, `suppliers-search` test ids.
- **Testing**: **267/267 green** (227 unit + 11 web + 29 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 35)
- **Phase 4 — Inventory + PO + suppliers**:
  - `InventoryServiceTests` (+1): `GetAllItemsAsync_FiltersBySearchTerm`.
  - `PurchaseOrderServiceTests` (+1): `UpdateStatusAsync_SetsPurchaseOrderStatus`.
  - `SupplierServiceTests` (+1): `UpdateAsync_PersistsContactAndEmail`.
  - `Inventory.razor`: `inventory-search` test id; search uses `value` + `@oninput` (matches other list pages); `_itemsLoadGeneration` prevents stale async search results.
- **Phase 2 E2E**:
  - `Inventory_Search_FiltersBySku` (28th) — table row count + SKU filter assertions (not whole-page HTML).
- **Testing**: **263/263 green** (224 unit + 11 web + 28 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 34)
- **Phase 4 — Inventory + PO line operations**:
  - `InventoryServiceTests` (+1): `GetRecentTransactionsAsync` newest-first across items.
  - `PurchaseOrderServiceTests` (+2): `UpdateLineAsync` recalc, `DeleteLineAsync` soft-delete + recalc.
  - Seeder: idempotent `OIL-TR-5L` low-stock demo item (qty 2, reorder 5).
  - `Inventory.razor`: `inventory-low-stock-filter` test id.
- **Phase 2 E2E**:
  - `Inventory_LowStock_Filter_ShowsLowItemsOnly` (27th) — filter shows `OIL-TR-5L`, hides `DB-12W-001`.
  - `Notifications_Triggered_From_LowStock_Or_JobEvent` hardened (clear localStorage before seed).
- **Testing**: **259/259 green** (221 unit + 11 web + 27 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 33)
- **Phase 4 — Inventory + notifications**:
  - `InventoryServiceTests` (+1): `RecordStockTransactionAsync` no-op when item missing.
  - `NotificationServiceTests` (+1): `MarkReadAsync` marks single item read.
  - `Inventory.razor`: `inventory-ready` + `inventory-table` test ids.
- **Phase 2 E2E**:
  - `Audit_Shows_Invoice_Create_After_Job_Invoice` (25th) — job→invoice then audit shows `CREATE` + `Invoice`.
  - `Inventory_Page_Loads_Stock_Table` (26th) — demo SKU `DB-12W-001` visible.
- **Testing**: **255/255 green** (218 unit + 11 web + 26 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 32)
- **Phase 4 — Inventory service tests**:
  - `InventoryServiceTests` (+2): `UpdateItemAsync` persists qty/reorder; `GetAllItemsAsync` excludes inactive items.
- **Phase 5 — Health rate-limit coverage**:
  - `HealthEndpointTests` (+1): `/health` liveness not rate-limited under burst (35 requests).
- **Phase 2 E2E — Audit after spine conversion**:
  - `Audit_Shows_Convert_After_Quote_To_Job` (24th Playwright test) — quote→job then audit page shows `CONVERT` + `Quote`.
- **Testing**: **251/251 green** (217 unit + 10 web + 24 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 31)
- **Phase 1/5 — Invoice audit on job conversion**:
  - `InvoiceService.CreateFromJobAsync` logs `CREATE` audit entry (job number + total) via optional `IAuditService`.
  - `InvoiceTests` (+1): audit entry on `CreateFromJobAsync`.
- **Phase 4 — CRM pipeline E2E**:
  - `Opportunities.razor`: `opportunity-stage` test id on detail panel.
  - E2E `Opportunity_Advances_Stage_On_Advance_Button` (23rd Playwright test) — creates fresh Lead opp, advances to Qualified.
  - `OpportunityServiceTests` (+1): `AdvanceStageAsync` audit `UPDATE` entry.
- **Testing**: **247/247 green** (215 unit + 9 web + 23 E2E).

### Exact Work Completed — Prior (2026-06-12, continue-the-plan session 30)
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
