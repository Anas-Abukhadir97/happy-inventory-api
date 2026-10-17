# Requirements and phase traceability

| Capability | Phase | Current state |
|---|---|---|
| Independent repositories, pinned tools, N-tier scaffold, en/ar resources, CI | 1 | Foundation implemented; verification in phase-1.md |
| Schema constraints, country/role seed, money mapping, draft contract/ETags | 2 | Implemented and tested; see phase-2.md. Runtime ETags belong to Phase 4. |
| JWT, one role, inactive checks, protected admin, refresh rotation/CSRF | 3 | Planned |
| Warehouses/items/users CRUD, pagination <=10, dashboard, Admin logs | 4 | Planned |
| Idempotent deductions retaining quantity >=1, full print report, local supplier outbox | 4A | Planned |
| Browser/SQLite/auth/concurrency/integration acceptance | 5 | Planned |
| Containers, Compose, persistence/restore and separate GHCR images | 6 | Planned |
| Documentation, tested release pair, source archives | 7 | Planned |
| Azure, HTTPS, hosted restore/rollback | 8 | Cost approval required |

Roles remain Admin/Management/Auditor. Inventory names are globally unique; SKU optional/nonunique; zero stock disallowed by current requirements. All business endpoints require JWT. Browser refresh/logout will be documented credential-flow exceptions. No public registration. Protect the seeded administrator in the service layer.
