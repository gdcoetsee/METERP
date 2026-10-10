# METERP user manual

The in-app copy of this guide is at **[/help](http://localhost:8080/help)** after `docker compose up`.

## Doors into the product

| Who | URL | Demo login |
|-----|-----|------------|
| Office / executive | `/login` | `admin@met.demo` / `Demo123!` |
| Field technician | `/field` after staff login | field user on the MET Electrical tenant |
| Customer | `/portal/login` | `portal@met.demo` / `Demo123!` |

Staff may also use **Continue with Google / Microsoft** when those client IDs are set. Only emails that already have a METERP user can sign in that way.

## Daily office flow

1. **Home** — cash desk: approve, send, convert, invoice, chase, receive, return PPE.
2. **Quotes** — customer + lines (always a Travel line) → executive approval → Sent → convert to job.
3. **Command Center** `/jobs/{id}` — labour, materials, travel, invoices. Invoicing does **not** close the job.
4. **Customers** — open a customer for the account statement (invoices, credit notes, receipts, VAT-inclusive balance). Draft, proforma, and cancelled documents are left off.
5. **Close** — executive P&L review only. Reopen needs a reason.
6. **Finance** — pick Sage or Xero and export sales CSV for the bookkeeper. Output VAT for a month is on the same page: stored tax, and credit notes reduce the total.

## Cash desk rules

- **Quotes.** Tick Includes VAT to type the gross figure: at 15%, R 115.00 stores ex-VAT R 100.00 and VAT R 15.00, and travel uses the same tick. The register From and To include those days, and the VAT column is the tax stored on the quote.
- **Quote print.** The printed quote shows our VAT number and the customer VAT number when both are set. A blank customer VAT number is left off, and the quote still prints.
- **Quote to job.** Converting a quote sets the job quoted total to the VAT-inclusive quote total once, travel stays a job cost, and converting the same quote again is refused.
- **Customers.** Payment terms (days) sets the invoice due date (blank or 0 stays 30 days), and search matches the VAT number as well as the name, email, and phone. On the account statement, Export CSV uses the lines on the screen and the last balance is the closing balance; draft, proforma, and cancelled documents stay off it.
- **Invoices.** Issue credit note on a draft so the VAT-inclusive credit reduces what the customer owes; a credit left in Draft does not. Credit this amount credits a VAT-inclusive part that cannot exceed the balance due and needs a reason, while Create credit note still credits the whole invoice.
- **Customers in credit.** On Invoices, customers whose open credit notes exceed open invoices show in credit and are left off the overdue list. Draft credits are left out.
- **Receipts.** On Invoices, Allocate one receipt splits one EFT across that customer's open invoices, the amounts must add up to the bank amount, and draft, proforma, cancelled, and credit notes are left off. Reverse needs a reason and puts the receipt back on the balance, and a receipt cannot be reversed twice.
- **Deposit.** On Command Center, issuing a full credit of the only deposit clears the deposit flag while the job is still open, and a partial credit that still covers the deposit does not. Reversing a deposit receipt clears that flag only when the deposit is no longer fully paid and no other deposit still covers it; a closed job is left unchanged.
- **Chase.** Chasing an overdue invoice states the net still owed after receipts and open credit notes. A paid invoice, or one already covered by credit, is not chased.
- **Command Center.** The job card shows TRFid, job card number, team, region, and customer order, and a missing value is a dash. The customer order number saves on the job; blank stays empty and does not change quote or invoice totals.
- **Inventory.** The register shows on hand, reorder level, and shortfall. Inactive items stay off the list.
- **Purchase orders.** On Purchase orders, Still to receive lists open lines with ordered, received, and outstanding. Fully received and cancelled orders are left off.
- **Goods received.** On GRV Register, the delivery note saved with the goods receipt is shown. A GRV with no note shows a dash.
- **Payroll.** On Payroll, the weekly timesheet is Monday to Sunday, hours sit on the posted day, and closed-job hours stay. The total equals the cells.
- **Portal.** On the customer portal, the outstanding balance matches that customer's statement as at today.
- **Finance.** Output VAT uses the tax already stored, credit notes reduce the total, and the CSV total matches the screen. Draft, proforma, and cancelled documents are left out.

## Grok Bot

`/ai-copilot` or the sparkle button. `/settings/ai` → **Continue with Grok** (or Google / OpenAI) → paste API key from the provider console. Platform default is xAI `https://api.x.ai/v1` + `grok-4.6`. You can also set `XAI_API_KEY`.

## Customer portal

Customers see **only their** quotes and invoices. On the customer portal, the outstanding balance matches that customer's statement as at today. They can accept a sent quote and send an “I've paid” notice (office still records the receipt). They cannot see other customers, jobs, or stock. Create portal logins from **Users** → New User → Customer portal login.

## Sage / Xero

Finance → package + sales account code → **Export sales CSV**. Sage = sales daybook. Xero = official invoice import columns (ZAR). Optional tenant invoice webhook still posts JSON when an invoice is created. **Output VAT** on Finance is the month worksheet (date, number, customer, net, VAT, gross). It uses the tax already stored. Draft, proforma, and cancelled documents are left out. Credit notes reduce the total. The CSV total line matches the screen.
