# Happy Inventory API

ASP.NET Core .NET 10 foundations for Happy Inventory. Personal owner: [Anas-Abukhadir97](https://github.com/Anas-Abukhadir97). Companion: [happy-inventory-web](https://github.com/Anas-Abukhadir97/happy-inventory-web).

## Status

Phase 1: N-tier project boundaries, locked dependency restore, localization resources, fail-closed API host, integration-test harness and CI. Business endpoints, login/refresh, EF Core persistence and OpenAPI are **not implemented yet**. All requests currently return 401; this is intentional. No demo credentials work at this stage.

## Local setup

Install .NET SDK 10.0.401 (global.json allows patch servicing). NuGet.Config isolates this personal repository from machine-level corporate feeds.

```sh
git clone https://github.com/Anas-Abukhadir97/happy-inventory-api.git backend
git clone https://github.com/Anas-Abukhadir97/happy-inventory-web.git frontend
cd backend
dotnet restore HappyInventory.sln --locked-mode
dotnet run --project src/HappyInventory.Api --no-launch-profile --urls http://localhost:5080
```

The sibling frontend proxy targets port 5080. Local HTTP has no real authentication credentials in Phase 1. Secure-cookie development transport will be decided before authentication is implemented. No environment file is loaded automatically; .env.example documents planned host settings. No secret is needed for the scaffold.

## Verify

```sh
dotnet restore HappyInventory.sln --locked-mode
dotnet build HappyInventory.sln -c Release --no-restore
dotnet format HappyInventory.sln --verify-no-changes --no-restore
dotnet test HappyInventory.sln -c Release --no-build --collect:"XPlat Code Coverage" --logger trx --results-directory artifacts/tests
```

Integration tests verify deny-by-default routing, rejected arbitrary bearer credentials, and en/ar resource resolution. UnitTests is intentionally empty until domain/application behavior exists; no artificial passing tests or business coverage claims. Independent CI runs these checks. Container builds and publishing arrive in Phase 6.

## Design and roadmap

Domain -> no infrastructure; Application -> Domain; Infrastructure -> Application/Domain; Api -> Application/Infrastructure. Controllers and services will be added in later phases. Use existing .resx resources for API-owned text.

See [requirements](docs/requirements.md), [decisions](docs/decisions.md), [backlog](docs/backlog.md), and [phase evidence](docs/phase-1.md). Phase 2 owns the canonical OpenAPI contract; web will pin it. Secure refresh, inventory CRUD, users, dashboard, logs, print view and bounded external integrations are planned current scope. Demo-only bootstrap credentials will be documented when seeding exists; production secrets will never have defaults.

Azure is the final planned phase, not yet provisioned; it requires approved cost/topology. SQLite is intended for a single API instance on persistent local disk. Backups, restore, containers, release manifests and deployment workflows will be verified in later phases. Live supplier delivery is unconfigured and requires explicit authorization.

## Future features and license

Product/warehouse-stock separation, zero-stock semantics, transfers/reservations, provider-specific connectors, procurement workflows, advanced report exports, full countries, currencies, warehouse permissions, MFA and machine-client OAuth are in the [canonical backlog](docs/backlog.md). No open-source license has been selected. Original assessment materials are not distributed here.
