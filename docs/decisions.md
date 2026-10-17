# Accepted foundation decisions

- Two public repositories under Anas-Abukhadir97; backend and frontend are independent sibling checkouts.
- .NET 10 LTS / EF Core 10 / SQLite, React / TypeScript / Vite, N-tier backend. React and .NET 10 supersede the older assessment stack.
- English/Arabic resources, Arabic RTL, stable role identifiers.
- JWT plus rotating refresh sessions are planned for Phase 3, not implemented here. The Phase 2 contract defines a reload-safe CSRF bootstrap endpoint, browser cookie mode, and token-only external login mode.
- Global item-name uniqueness, quantity >=1, decimal prices represented as minor units internally and decimal strings externally; ETag concurrency planned.
- Phase 2 adds SQLite migrations and first-admin seeding, a draft OpenAPI generator/validator, and contract drift checks in CI. Docker/image publishing begins Phase 6; Azure is final Phase 8 after cost approval.
- Active users, warehouses and items use soft deletion so audit, refresh, deduction and supplier records retain valid foreign keys. Active names/emails remain unique; a deleted name/email may be reused. Application services must enforce protected-admin and nonempty-warehouse deletion rules.
- No software license selected yet. Original assessment documents and private planning material remain outside both repositories.
