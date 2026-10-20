# Canonical backlog

The assessment features and Phase 7 handoff are delivered. Azure deployment remains a separate infrastructure decision: the current subscription has no F1 quota, so no paid resource should be provisioned without a costed topology. The items below are future product work, not claims about current behavior.

## Recommended order

| Priority | Feature | Dependency and first deliverable | Acceptance target |
| --- | --- | --- | --- |
| 1 | Movement ledger and stock model | Define `Product` separately from `WarehouseStock`; migrate existing item records without losing IDs/history. Record an immutable movement for every adjustment and deduction. | For each warehouse/product, opening stock plus movements equals current stock; concurrent deductions cannot oversell. |
| 2 | Zero-stock, transfers and reservations | Agree whether zero stock is a valid active record. Add transfer and reservation state machines on top of the ledger. | A transfer either commits both warehouse movements or neither; reservation expiry and release restore availability exactly once. |
| 3 | Warehouse-scoped authorization | Define role-to-warehouse grants and migration for existing users; review every read, export and write query. | A user cannot infer or mutate data from an unassigned warehouse, including through IDs and reports. |
| 4 | Account recovery and MFA | Select a verified email channel and recovery policy; add short-lived single-use tokens and TOTP or passkey enrollment. | Recovery does not reveal account existence; revocation invalidates sessions; rate limits cover all recovery routes. |
| 5 | Exports and reference data | Add streamed CSV/PDF export with role and warehouse filters; source complete country and currency datasets, then choose exchange-rate and rounding policy. | Exports match on-screen filters and locale, escape spreadsheet formulas, and remain bounded for large inventories. |
| 6 | Procurement workflow and supplier integration | Add approval states, supplier identity and a provider-neutral connector contract; retain the local outbox and idempotency. | Approval/audit history is immutable; a retry cannot create a duplicate external order; delivery state distinguishes local recording from provider acceptance. |
| 7 | Machine clients and scale-out | Add scoped OAuth client credentials only after a defined machine-client use case. Move from single-process SQLite/SSE to a multi-instance data and event topology if demand requires it. | Client credentials have narrow scopes and rotation; distributed rate limiting and notification fan-out work across instances. |

## Implementation gates

Each feature needs a short design decision, an additive API contract proposal, migration and rollback plan, English/Arabic resources, role-matrix review, and real SQLite integration tests (or a new provider's equivalent). The web repository should pin the reviewed contract in a separate PR. Performance limits should be measured before raising API budgets; the current in-memory rate limiter is per process.

Live supplier notification still requires an approved destination, channel and credentials plus explicit test-send authorization. A local adapter does not prove live delivery.
