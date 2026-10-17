# Happy Inventory API

ASP.NET Core .NET 10 API for Happy Inventory. Personal owner: [Anas-Abukhadir97](https://github.com/Anas-Abukhadir97). Companion: [happy-inventory-web](https://github.com/Anas-Abukhadir97/happy-inventory-web).

## Status

Phase 3: email/password login, 10-minute JWTs, rotating browser refresh sessions, CSRF/Origin checks, logout, current-user lookup, inactive-account rejection and immediate account/session invalidation are implemented. EF Core SQLite migrations and the draft versioned contract remain in place. Inventory, dashboard, user-management and external-integration endpoints are **not implemented yet**. Development Swagger describes the live auth endpoints; the full planned contract is [openapi.v1.json](docs/contracts/openapi.v1.json). Business routes still return 401 without a token. Development-only Swagger uses a separate loopback listener.

## Local setup

Install .NET SDK 10.0.401 (global.json allows patch servicing). NuGet.Config isolates this personal repository from machine-level corporate feeds.

```sh
git clone https://github.com/Anas-Abukhadir97/happy-inventory-api.git backend
git clone https://github.com/Anas-Abukhadir97/happy-inventory-web.git frontend
cd backend
dotnet restore HappyInventory.sln --locked-mode
dotnet run --project src/HappyInventory.Api --launch-profile http
```

The sibling frontend proxy targets port 5080 from an HTTPS page at `https://localhost:5173`. The development database is `src/HappyInventory.Api/data/inventory.db` when launched with `dotnet run --project`, and is migrated on startup. First development startup seeds `admin@happywarehouse.com` with demo-only password `P@ssw0rd`. Development creates an ignored `src/HappyInventory.Api/data/dev-jwt-key` so sessions survive API restarts. Production requires `ConnectionStrings__Inventory` on persistent storage, `BootstrapAdmin__Password` before first startup, a random `Auth__SigningKey` of at least 32 UTF-8 bytes, and an exact HTTPS `Auth__BrowserOrigin`; set `BootstrapAdmin__Email`, `Auth__Issuer` and `Auth__Audience` as appropriate. Production rejects plain HTTP and needs trusted HTTPS termination at the API boundary. Do not use demo credentials in production. No environment file is loaded automatically.

## Swagger

Run the Development launch profile above, then open http://localhost:5081/swagger. Live-host OpenAPI JSON: http://localhost:5081/swagger/v1/swagger.json. The API port remains 5080; documentation is unavailable there and disabled in Production. Swagger uses its own loopback listener, and the gate checks the actual connection port instead of Host headers. The Authorize dialog supports bearer JWTs without persisting authorization in browser storage. Auth operations are live; other operations remain in the separately versioned [draft contract](docs/contracts/README.md). Browser-mode login requires a CSRF cookie/header and exact HTTPS Origin, so use the frontend for that flow. For an independent local client, POST `/api/v1/auth/login` with `{"email":"admin@happywarehouse.com","password":"P@ssw0rd","sessionMode":"external"}` and send the returned access token as `Authorization: Bearer …` to `/api/v1/auth/me`. External mode sets no refresh cookie. Use HTTPS outside local development.

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

Integration tests verify deny-by-default routing, rejected arbitrary bearer credentials, en/ar resource resolution, real SQLite migrations and seeding, database constraints, money conversion, CSRF/Origin checks, rotation/replay, concurrent redemption, expiry, logout, role/active/password invalidation, rate limiting and bootstrap-admin triggers. UnitTests remains empty until independent application behavior exists. Independent CI runs these checks. Container builds and publishing arrive in Phase 6.

## Design and roadmap

Domain -> no infrastructure; Application -> Domain; Infrastructure -> Application/Domain; Api -> Application/Infrastructure. Controllers and services will be added in later phases. Use existing .resx resources for API-owned text.

See [requirements](docs/requirements.md), [decisions](docs/decisions.md), [backlog](docs/backlog.md), [Phase 3 evidence](docs/phase-3.md) and [contract](docs/contracts/README.md). The web repository pins a checksum of this contract. Inventory CRUD, users, dashboard, logs, print view and bounded external integrations are planned later phases. Production bootstrap and JWT secrets have no default.

Azure is the final planned phase, not yet provisioned; it requires approved cost/topology. SQLite is intended for a single API instance on persistent local disk. Backups, restore, containers, release manifests and deployment workflows will be verified in later phases. Live supplier delivery is unconfigured and requires explicit authorization.

## Future features and license

Product/warehouse-stock separation, zero-stock semantics, transfers/reservations, provider-specific connectors, procurement workflows, advanced report exports, full countries, currencies, warehouse permissions, MFA and machine-client OAuth are in the [canonical backlog](docs/backlog.md). No open-source license has been selected. Original assessment materials are not distributed here.
