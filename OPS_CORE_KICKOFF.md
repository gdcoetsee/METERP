# Ops Core — Kickoff for implementers (Composer / agents)

**Read this first**, then `COMPLETION_PLAN.md` handoff (2026-07-09), then `AGENTS.md`.

This document is the **execution contract** for the Ops Core sprint. Do not invent product rules that contradict it.

**Status (2026-07-10):** Ops Core through R5; R6 partial; E2E docker run done with confirm-dialog + field-stock multi-line fixes. Next: remaining E2E flakes (field report modal, AI), then R6 quota UX / pilot polish. Primary implementer: **Grok**.  
**Advisory:** Flag plan risks and consult the user before changing product rules or roadmap order. Update `COMPLETION_PLAN.md` whenever product rules change.

---

## Goal

Build a stable **job-centric operational core**:

1. Correct **stock requisition** flow (including partial stock + procure remainder).
2. **Job costs** stay accurate when materials/labor are posted.
3. **Invoicing does not close the job**; costs remain editable after invoice.
4. **Executive close** after P&L review (hard lock + reopen with reason).
5. **Job Command Center** at `/jobs/{id}` as the primary ops surface.

**Do not** add AI features, cache-test sprawl, or “nice polish” until Ops Core success criteria are met.

---

## Locked product rules (non-negotiable)

| # | Rule |
|---|------|
| 1 | **Job ID** is the central key for **job operations** (materials *to job*, labor, costs, field reports, invoices, closeout). |
| 2 | **Creating an invoice must not close or freeze the job.** |
| 3 | Operators **must still capture costs** (labor, materials, travel, field reports) **after** invoices exist, until the job is **Closed**. |
| 4 | Job **closes only** when an **executive** reviews performance / costs / profit and **approves close**. |
| 5 | **Closed** = hard lock (no new costs/reqs/invoices). **Reopen** = executive only, **reason required**, audit logged. |
| 6 | **Work sign-off** (mgr → exec) is **separate** from **executive close**. Do not merge them. |
| 7 | Multiple deposit / partial / progress / **final** invoices are allowed while job is open. “Final” is a document type, not job death. Soft warnings only (e.g. large unbilled residual on close). |
| 8 | **PPE** is **stock issued to employees**, maintained in a **PPE register**. **EmployeeId required; JobId optional.** Do not model PPE as job-primary. |
| 9 | **AI freeze** until Ops Core is done. |
| 10 | Prefer **modals** for create/edit/approve/issue. Follow Clean Architecture layers. |
| 11 | **Definition of Done:** service rules + UI + audit + tests (`dotnet test` green). No “implemented — verify polish.” |

### Status vocabulary

`Not started` | `In progress` | `Done (DoD met)` | `Stub (labeled)` | `Surface only`

---

## Recommended domain model (implement toward this)

```text
JobStatus: Scheduled | InProgress | OnHold | Completed | Closed | Cancelled
  - Retire terminal semantics of Invoiced (map existing Invoiced rows carefully in migration:
    prefer Completed or Closed depending on data, document choice in commit message)

Work sign-off: JobSignOffStatus (expand toward dual chain later if not in first chunk)
Close fields: ClosedAt, ClosedByUserId, CloseNotes (optional)
Reopen: audit log (+ optional LastReopened* fields)

Invoices: many per JobId while Status != Closed
CommandCenter summary: QuotedTotal, ActualTotal, BilledToDate, UnbilledResidual, Margin%, invoices, requisitions

PPE: EmployeePpeIssue.EmployeeId required; JobId nullable
```

---

## Implementation order (do in this sequence)

### Chunk 1 — Partial requisition shortfall (R2 hotfix)

**Bug today:** Partial stock → `Approved` → Create PO only when `AwaitingProcurement` → shortfall orphaned. Issue marks whole REQ `Issued` even if only partial.

**Fix:**

1. `ApproveExecutiveAsync`: **any shortfall** → `AwaitingProcurement` (even if some qty reserved). Full reserve → `Approved`.
2. `IssueAsync`: allow `Approved` | `AwaitingProcurement` | `ProcurementOrdered` when reserved > issued.
3. Mark `Issued` **only when** every line has `QuantityIssued >= QuantityRequested`.
4. `CreateFromRequisitionAsync` already orders `Requested - Reserved` — keep that; ensure UI shows Create PO for `AwaitingProcurement`.
5. UI (`Requisitions.razor`): Issue button when there is reservable qty to issue (not only `Approved`).
6. **Unit tests** covering partial reserve + Create PO shortfall + partial issue not fully closing REQ.

**Files:**  
`StockRequisitionService.cs`, `Requisitions.razor`, `StockRequisitionServiceTests.cs`, maybe `PurchaseOrderService` only if status guards need tweak.

---

### Chunk 2 — Job cost integrity

**Bug today:** REQ issue adds `JobCost` but may not recompute `Job.ActualCost`.

**Fix:**

1. After REQ material issue (and preferably labor add paths), recompute `Job.ActualCost` from non-deleted `JobCost` (and decide if ActualCost includes labor or UI always uses `GetActualTotal()` — prefer one source of truth).
2. Unit tests: issue → job actuals reflect material cost.

**Files:**  
`StockRequisitionService.cs`, `JobService.cs`, `JobTests.cs` / requisition tests.

---

### Chunk 3 — Billing ≠ close (domain + services)

**Fix:**

1. Invoice create **never** sets job to closed/frozen.
2. Add **CloseAsync** / **ReopenAsync** on `IJobService` (executive user id + notes/reason).
3. Guard mutations when `Status == Closed` (costs, labor, reqs, new invoices).
4. Command center / ready-to-invoice: billing summary separate from status; do not treat “has invoice” as done.
5. Soft-migrate `JobStatus.Invoiced` away from “terminal done.”
6. Unit tests: invoice while open → add cost → close → mutations fail → reopen with reason works.

**Files:**  
`Job.cs`, `JobStatus.cs`, `JobService.cs`, `InvoiceService.cs`, EF migration, tests.

---

### Chunk 4 — Job Command Center UI

**Fix:**

1. New page `@page "/jobs/{JobId:guid}"` (Command Center).
2. Sections: header/status, **P&L** (quoted / actual / billed), invoice list + create invoice (while open), requisitions, add cost/labor **modals**, status actions, **executive close** (modal with notes), reopen if closed.
3. Jobs list: ops-first copy (de-emphasize AI-first marketing); row links to Command Center.
4. Prefer modals for add cost / labor / requisition / close.

**Files:**  
`Jobs.razor`, `JobList.razor`, new `JobCommandCenter.razor` (or similar), `JobCommandCenterSummary.cs`, permissions unchanged unless close needs `Jobs.Manage` / exec policy.

---

### Chunk 5 — E2E + handoff  ← **DONE (test committed)**

1. Playwright: `Job_CommandCenter_Invoice_While_Open_Cost_Then_Executive_Close` (deposit → cost → close → reopen).
2. Unit: deposit without sign-off; invoice-while-open cost until close (existing + new).
3. Command Center invoice modal: Deposit / Proforma / Partial / Final with correct gates; stay on job after create.
4. Demo job seeder reopens Closed jobs for E2E reset.

---

### Later (same Ops Core epic, after above)

- Dual work sign-off (mgr → exec) UI/service  
- Job cancel/void  
- Multi-line office/field requisition  
- PPE: make `JobId` optional; issue-to-employee register UI  
- Negative stock guard  

Do **not** start these before Chunks 1–4 unless blocked.

---

## Explicit anti-goals

- Do **not** set job closed/Invoiced-as-done on invoice create  
- Do **not** require JobId for PPE as primary model  
- Do **not** add new AI copilots / apply features  
- Do **not** mass-rewrite cache tests or README sales pitch  
- Do **not** bypass tenant filters  
- Do **not** manually set document `LineTotal` in services/UI  

---

## How to work (session discipline)

```text
1. One chunk at a time (Chunk 1 → 2 → 3 → 4 → 5)
2. Implement + unit tests
3. dotnet test (at least Application.Tests for the chunk; full solution before claiming done)
4. Update COMPLETION_PLAN.md handoff status
5. Commit with clear message (e.g. "Ops Core chunk 1: partial REQ shortfall fix")
6. Next chunk
```

Architecture: Domain → Application (`I*Service`) → Infrastructure → Web. Register DI in `Program.cs`. Migrations for schema changes.

---

## Paste this to Composer 2.5 (first message)

```text
You are implementing METERP Ops Core on branch main (or a feature branch off main).

MANDATORY reading (in order):
1. OPS_CORE_KICKOFF.md  ← execution contract
2. COMPLETION_PLAN.md   ← handoff + locked decisions (2026-07-09)
3. AGENTS.md            ← architecture & testing rules

Execute Chunk 1 first (partial requisition shortfall), then stop for review if large; otherwise continue Chunk 2–4 in order.

Locked rules you must not violate:
- Invoice does NOT close the job
- Costs allowed after invoice until executive Close
- Close = executive P&L review only; hard lock + reopen with reason
- PPE = employee stock register; JobId optional (do not deepen job-primary PPE this sprint unless in later chunk)
- No new AI features
- Run dotnet test after each chunk; commit with clear messages

Prefer modals. Follow Clean Architecture. Definition of Done = service + UI + audit + tests.
```

---

## What the human should do after switching

1. Confirm latest `main` is pulled (this kickoff should already be on GitHub).  
2. Open Composer 2.5 on the **METERP** repo.  
3. Paste the **first message** block above.  
4. Optionally: `Create a branch ops-core/chunk-1` before large edits.  
5. After each chunk: skim diff for lifecycle/PPE rule violations; run `dotnet test` yourself if Composer’s report is unclear.  
6. Do **not** ask Composer to “complete the whole ERP” in one shot — enforce chunk order.  
7. When Ops Core success criteria in `COMPLETION_PLAN.md` are checked, then plan R3 (employees/payslip) or PPE register depth.

---

## Success criteria (Ops Core done)

- [ ] Partial stock REQ can issue reserved qty **and** create PO for shortfall  
- [ ] Job ActualCost / P&L reflects material issues  
- [ ] Invoice leaves job open; cost after invoice works  
- [ ] Executive close locks job; reopen with reason works  
- [ ] `/jobs/{id}` Command Center is primary job UX  
- [ ] Unit + at least one E2E path green  
- [ ] Jobs UI is ops-first, not AI-first  

---

## Key file index

| Area | Paths |
|------|--------|
| Plan | `COMPLETION_PLAN.md`, `AGENTS.md`, this file |
| Job | `src/METERP.Domain/Job.cs`, `JobStatus.cs`, `.../JobService.cs`, `.../InvoiceService.cs`, `.../Pages/Jobs.razor`, `.../JobList.razor` |
| Stock | `.../StockRequisitionService.cs`, `.../PurchaseOrderService.cs`, `.../Pages/Requisitions.razor` |
| PPE (later) | `.../EmployeePpeIssue.cs`, `.../PpeIssueService.cs`, `.../Pages/PpeHistory.razor` |
| Tests | `tests/METERP.Application.Tests/StockRequisitionServiceTests.cs`, `JobTests.cs`, `ProcurementSpineFlowTests.cs` |
