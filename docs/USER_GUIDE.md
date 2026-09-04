# METERP user manual

The in-app copy of this guide is at **[/help](http://localhost:8080/help)** after `docker compose up`.

## Doors into the product

| Who | URL | Demo login |
|-----|-----|------------|
| Office / executive | `/login` | `admin@acme.demo` / `Demo123!` |
| Field technician | `/field` after staff login | field user on the Acme tenant |
| Customer | `/portal/login` | `procurement@jhgh.co.za` / `Demo123!` |

Staff may also use **Continue with Google / Microsoft** when those client IDs are set. Only emails that already have a METERP user can sign in that way.

## Daily office flow

1. **Home** — cash desk: approve, send, convert, invoice, chase, receive, return PPE.
2. **Quotes** — customer + lines (always a Travel line) → executive approval → Sent → convert to job.
3. **Command Center** `/jobs/{id}` — labour, materials, travel, invoices. Invoicing does **not** close the job.
4. **Close** — executive P&L review only. Reopen needs a reason.
5. **Finance** — pick Sage or Xero and export sales CSV for the bookkeeper.

## Grok Bot

`/ai-copilot` or the sparkle button. `/settings/ai` → **Continue with Grok** (or Google / OpenAI) → paste API key from the provider console. Platform default is xAI `https://api.x.ai/v1` + `grok-4.6`. You can also set `XAI_API_KEY`.

## Customer portal

Customers see **only their** quotes and invoices and the outstanding balance. They can accept a sent quote and send an “I've paid” notice (office still records the receipt). They cannot see other customers, jobs, or stock. Create portal logins from **Users** → New User → Customer portal login.

## Sage / Xero

Finance → package + sales account code → **Export sales CSV**. Sage = sales daybook. Xero = official invoice import columns (ZAR). Optional tenant invoice webhook still posts JSON when an invoice is created.
