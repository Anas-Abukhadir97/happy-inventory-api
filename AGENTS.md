# Project instructions

Read README.md and docs/decisions.md before changing behavior. API owns the canonical requirements, backlog and versioned contract. Work within the phase authorized by the user; Phase 7 reviewer handoff is complete, with Azure still gated for Phase 8.

Use the existing localization system for all new or changed user-facing text, including labels, validation, notifications and accessibility text. Reuse resource keys or add them to existing en/ar resources. Preserve authorization role identifiers Admin/Management/Auditor and backend-owned messages.

Keep credentials, tokens, runtime databases, logs and original assessment attachments out of commits and logs. Preserve the agreed React/TypeScript/Vite and .NET 10/EF Core 10/SQLite stack. Do not silently change the API contract, authentication design or provider. Inventory quantities must remain >=1, item names globally unique and list pages capped at 10; preserve protected bootstrap-admin rules.

Use focused Conventional Commits and feature branches/PRs. Run relevant checks and report actual results. Default to one primary agent; parallel agents require explicit authorization. Azure is Phase 8 and requires a concrete approved cost proposal before provisioning. Supplier messages require explicit recipient/channel and sending authorization. Public visibility does not imply an open-source license.

## Backend boundaries and checks
Domain has no dependencies; Application references Domain; Infrastructure implements Application ports; Api composes them. No DbContext in controllers. Schema/migrations/auth have one writer. Use real SQLite integration tests when persistence exists. Localized API-owned resources live in src/HappyInventory.Api/Resources/*.resx.

Run dotnet restore HappyInventory.sln --locked-mode, python scripts/generate_contract.py --check, python scripts/validate_contract.py, dotnet build HappyInventory.sln -c Release --no-restore, dotnet format HappyInventory.sln --verify-no-changes --no-restore, and dotnet test HappyInventory.sln -c Release --no-build. The draft OpenAPI is canonical for planned routes, but does not mean runtime endpoints exist.
