# Happy Inventory API

ASP.NET Core .NET 10 API for Happy Inventory. Personal owner: [Anas-Abukhadir97](https://github.com/Anas-Abukhadir97). Companion: [happy-inventory-web](https://github.com/Anas-Abukhadir97/happy-inventory-web).

## Status

Phase 2: EF Core SQLite schema and migrations, role/country/first-admin seeding, a draft versioned OpenAPI contract, and independent CI contract checks are implemented. Business endpoints and login/refresh are **not implemented yet**. Development Swagger describes the live host, which still has no business operations; the planned contract is [openapi.v1.json](docs/contracts/openapi.v1.json). Business/public-port requests still return 401. Development-only Swagger uses a separate loopback listener. The seeded demo credentials cannot authenticate until Phase 3.

## Local setup

Install .NET SDK 10.0.401 (global.json allows patch servicing). NuGet.Config isolates this personal repository from machine-level corporate feeds.

```sh
git clone https://github.com/Anas-Abukhadir97/happy-inventory-api.git backend
git clone https://github.com/Anas-Abukhadir97/happy-inventory-web.git frontend
cd backend
dotnet restore HappyInventory.sln --locked-mode
dotnet run --project src/HappyInventory.Api --launch-profile http
```

The sibling frontend proxy targets port 5080. The development database is `data/inventory.db` and is migrated on startup. First development startup seeds `admin@happywarehouse.com` with demo-only password `P@ssw0rd`; no login works yet. Production requires `ConnectionStrings__Inventory` pointing to persistent SQLite storage and `BootstrapAdmin__Password` before first startup; set `BootstrapAdmin__Email` to override the default email. Do not use the demo password in production. No environment file is loaded automatically. Secure cookie transport will be implemented in Phase 3.

## Swagger

Run the Development launch profile above, then open http://localhost:5081/swagger. Live-host OpenAPI JSON: http://localhost:5081/swagger/v1/swagger.json. The API port remains 5080; documentation is unavailable there and disabled in Production. Swagger uses its own loopback listener, and the gate checks the actual connection port instead of Host headers. The Authorize dialog supports bearer JWTs without persisting authorization in browser storage. There are no business operations to try until later phases. The separately versioned [draft contract](docs/contracts/README.md) specifies planned operations. Custom documentation title/description use the existing en/ar resources.

## Verify

```sh
dotnet restore HappyInventory.sln --locked-mode
dotnet tool restore
python scripts/generate_contract.py --check
python scripts/validate_contract.py
dotnet build HappyInventory.sln -c Release --no-restore
dotnet format HappyInventory.sln --verify-no-changes --no-restore
dotnet test HappyInventory.sln -c Release --no-build --collect:"XPlat Code Coverage" --logger trx --results-directory artifacts/tests
```

Integration tests verify deny-by-default routing, rejected arbitrary bearer credentials, en/ar resource resolution, real SQLite migrations and seeding, database constraints and money conversion. UnitTests is intentionally empty until application behavior exists. Independent CI runs these checks. Container builds and publishing arrive in Phase 6.

## Design and roadmap

Domain -> no infrastructure; Application -> Domain; Infrastructure -> Application/Domain; Api -> Application/Infrastructure. Controllers and services will be added in later phases. Use existing .resx resources for API-owned text.

See [requirements](docs/requirements.md), [decisions](docs/decisions.md), [backlog](docs/backlog.md), [Phase 2 evidence](docs/phase-2.md) and [contract](docs/contracts/README.md). The web repository pins a checksum of this contract. Secure refresh, inventory CRUD, users, dashboard, logs, print view and bounded external integrations are planned later phases. Production bootstrap passwords have no default.

Azure is the final planned phase, not yet provisioned; it requires approved cost/topology. SQLite is intended for a single API instance on persistent local disk. Backups, restore, containers, release manifests and deployment workflows will be verified in later phases. Live supplier delivery is unconfigured and requires explicit authorization.

## Future features and license

Product/warehouse-stock separation, zero-stock semantics, transfers/reservations, provider-specific connectors, procurement workflows, advanced report exports, full countries, currencies, warehouse permissions, MFA and machine-client OAuth are in the [canonical backlog](docs/backlog.md). No open-source license has been selected. Original assessment materials are not distributed here.
