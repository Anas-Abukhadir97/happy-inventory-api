# Accepted foundation decisions

- Two public repositories under Anas-Abukhadir97; backend and frontend are independent sibling checkouts.
- .NET 10 LTS / EF Core 10 / SQLite, React / TypeScript / Vite, N-tier backend. React and .NET 10 supersede the older assessment stack.
- English/Arabic resources, Arabic RTL, stable role identifiers.
- JWT plus rotating refresh sessions are planned for Phase 3, not implemented here. Define reload CSRF issuance and external login transport in Phase 2.
- Global item-name uniqueness, quantity >=1, decimal prices represented as minor units internally and decimal strings externally; ETag concurrency planned.
- Phase 1 CI covers current scaffold build/tests. Contract drift checks begin Phase 2; Docker/image publishing begins Phase 6; Azure is final Phase 8 after cost approval.
- No software license selected yet. Original assessment documents and private planning material remain outside both repositories.
