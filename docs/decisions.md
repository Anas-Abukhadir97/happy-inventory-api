# Accepted foundation decisions

- Two public repositories under Anas-Abukhadir97; backend and frontend are independent sibling checkouts.
- .NET 10 LTS / EF Core 10 / SQLite, React / TypeScript / Vite, N-tier backend. React and .NET 10 supersede the older assessment stack.
- English/Arabic resources, Arabic RTL, stable role identifiers.
- Phase 3 implements 10-minute HS256 JWTs and 7-day-idle/30-day-absolute rotating browser refresh sessions. Access tokens stay in web memory; a refresh family is revoked after reuse or logout. External login issues no cookie. Current account/role/security version is checked on every JWT request.
- Browser login, refresh and logout require exact configured HTTPS Origin and matching CSRF cookie/header. Vite uses HTTPS locally and proxies to loopback HTTP API; production API rejects plain HTTP and requires trusted TLS termination. The development signing key is generated under ignored `src/HappyInventory.Api/data/`; production requires an explicit signing key.
- SQLite triggers protect the bootstrap administrator from deletion, deactivation and demotion, and bump security version on password/role/active/deletion changes. Login has a per-IP fixed-window limit and per-account lockout. User-management routes remain Phase 4.
- Global item-name uniqueness, quantity >=1, decimal prices represented as minor units internally and decimal strings externally; ETag concurrency planned.
- Phase 2 adds SQLite migrations and first-admin seeding, a draft OpenAPI generator/validator, and contract drift checks in CI. Docker/image publishing begins Phase 6; Azure is final Phase 8 after cost approval.
- Active users, warehouses and items use soft deletion so audit, refresh, deduction and supplier records retain valid foreign keys. Active names/emails remain unique; a deleted name/email may be reused. Application services must enforce protected-admin and nonempty-warehouse deletion rules.
- No software license selected yet. Original assessment documents and private planning material remain outside both repositories.
