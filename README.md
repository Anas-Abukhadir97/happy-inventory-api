# Happy Inventory API

ASP.NET Core .NET 10 API for Happy Inventory. Personal owner: [Anas-Abukhadir97](https://github.com/Anas-Abukhadir97). Companion: [happy-inventory-web](https://github.com/Anas-Abukhadir97/happy-inventory-web).

## Status

Phase 7 assessment release is complete: reviewer guide, architecture, bilingual report screenshots, fresh-checkout checks and checksummed source archives are prepared. Phase 6 delivered two non-root images, persistent local Compose, recovery checks and a tested digest pair. Phase 5 integrated verification completed independent HTTP and real Chromium checks for deductions, roles, ETags, browser refresh, local supplier outbox and English/Arabic reports. [Phase 5 evidence](docs/phase-5.md) records the checks and limits. Authentication, assessment CRUD/dashboard/logs, atomic eCommerce deductions and a manual supplier-order outbox with local test delivery are live. Writes enforce roles and `If-Match` ETags for CRUD; lists have a hard 10-row page limit. Serilog writes structured daily JSON files under ignored `data/logs`, with 14 retained files and 1 MiB rolling segments. Development Swagger describes live endpoints; the full contract is [openapi.v1.json](docs/contracts/openapi.v1.json). Business routes return 401 without a token. Development-only Swagger uses a separate loopback listener.

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

## Roles and notifications

Admin, Management and Auditor permissions are implemented in the API and summarized in the [roles and notification guide](docs/notifications.md). Only the protected Admin is seeded on a fresh database; Admin creates the other users through the web app. The notification hub stores recipient-only messages in SQLite and pushes updates over an authenticated server-sent event stream. Admin may optionally request SMTP email or SMS through an Android phone's local gateway after configuring that channel and choosing a recipient. Neither channel is configured by default, and no message is sent automatically to a real recipient. The phone/SIM may still incur carrier charges.

## Swagger

Run the Development launch profile above, then open http://localhost:5081/swagger. Live-host OpenAPI JSON: http://localhost:5081/swagger/v1/swagger.json. The API port remains 5080; documentation is unavailable there and disabled in Production. Swagger uses its own loopback listener, and the gate checks the actual connection port instead of Host headers. The Authorize dialog supports bearer JWTs without persisting authorization in browser storage. Auth, assessment and Phase 4A diagram-integration operations are live and described by the versioned [contract](docs/contracts/README.md). Browser-mode login requires a CSRF cookie/header and exact HTTPS Origin, so use the frontend for that flow. For an independent local client, POST `/api/v1/auth/login` with `{"email":"admin@happywarehouse.com","password":"P@ssw0rd","sessionMode":"external"}` and send the returned access token as `Authorization: Bearer …` to `/api/v1/auth/me`. External mode sets no refresh cookie. Use HTTPS outside local development.

An independent Admin/Management client can POST `/api/v1/integrations/inventory-deductions` with `Authorization: Bearer <access-token>`, `Idempotency-Key: order-42-line-1` and `{"externalReference":"order-42-line-1","warehouseId":1,"itemId":2,"quantity":2}`. With stock 5, success returns 200 with `resultQuantity:3` and `replayed:false`. Retry the identical request with the **same** key after an uncertain result: it returns the same `deductionId`, `resultQuantity:3` and `replayed:true`, without a second decrement. Reusing the key with changed payload returns 409 `idempotency_conflict`; a request that would leave zero returns 409 `insufficient_stock`; a wrong warehouse/item returns 404. Keys are printable ASCII, 1–128 characters, scoped to the caller and retained without expiry. `POST /api/v1/supplier-orders` prepares a manual request and local-test outbox entry; `GET /api/v1/supplier-orders` reports Pending/Sent/Failed, where Sent means recorded locally. No supplier is contacted.

## Local containers and persistence

Clone the web repository beside this one (`../frontend`). Copy `.env.example` to an ignored `.env`, then set a random signing key of at least 32 UTF-8 bytes and a strong first-admin password. Keep the signing key stable across restarts or existing access tokens will fail. From this directory, run `docker compose up -d --build` and open `https://localhost:8443`. Caddy uses its own local CA; trust its certificate for browser use. The HTTPS listener is bound to host loopback only. The API is on a private Compose network and runs as the .NET non-root UID; the web image runs as UID 10001. The API container name prevents accidental horizontal scaling against the single SQLite file. Authenticated `/api/v1/health` reports database readiness; internal `/health/live` and `/health/ready` are loopback-only probes and are not routed by Caddy.

The `inventory_data` named volume holds SQLite and JSON logs. `docker compose restart api` preserves them. `docker compose down` also preserves the volume; **`docker compose down -v` deletes it**. Run `python scripts/sqlite_volume.py backup` for a SQLite online backup under ignored `backups/`. Run `python scripts/sqlite_volume.py restore backups/<name>.db` to stop the API, verify and replace its database, and restart it; restore replaces current data. Keep a copy of each backup outside the Docker host and protect it as sensitive data. CI proves clean start, non-root UIDs, HTTPS proxy/authenticated health, restart persistence and backup/restore on an ephemeral runner.

`compose.release.yaml` is the same topology without builds. Run `python scripts/release_pair.py pull` and `python scripts/release_pair.py up -d --no-build` to use the tested immutable image pair from `releases/phase-6-tested.json`; the helper also supports `backup`, `restore <file>` and `down`. Each repository publishes its own commit-tagged image to GHCR only after its checks pass on `main`; the uploaded digest artifact identifies the immutable image. [Phase 6 evidence](docs/phase-6.md) records both source commits, CI runs and recovery procedure. Registry package visibility is checked separately from source visibility. These local containers are not Azure deployment; Azure remains Phase 8 behind the cost decision.

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

Integration tests verify deny-by-default routing, real SQLite migrations and seeding, database constraints, money conversion, CSRF/Origin checks, refresh rotation, account invalidation, warehouse/item/user CRUD, ETags, page caps, dashboards, roles, log access, atomic deductions, supplier outbox retry and authenticated health. Independent CI runs these checks plus the Compose persistence/restore test and image publication.

## Design and roadmap

Domain -> no infrastructure; Application -> Domain; Infrastructure -> Application/Domain; Api -> Application/Infrastructure. Controllers use application service interfaces; EF Core remains in Infrastructure. Use existing .resx resources for API-owned text.

See the [reviewer guide](docs/reviewer-guide.md), [architecture](docs/architecture.md), [requirements](docs/requirements.md), [decisions](docs/decisions.md), [backlog](docs/backlog.md), [Phase 4A evidence](docs/phase-4a.md) and [contract](docs/contracts/README.md). The web repository pins a checksum of this contract and contains the print view and English/Arabic screenshots. Production bootstrap and JWT secrets have no default.

The [Phase 8 free Azure runbook](docs/phase-8-free-azure.md) prepares a single Windows App Service F1 site for the API and compiled web app on one HTTPS origin. The owner's Azure Free Trial subscription is enabled, but Azure currently grants **zero F1 VMs quota**, so the free plan could not be created and there is no hosted URL. The hosted SQLite option is limited to one process on App Service `%HOME%` storage and requires a live persistence/backup test before Phase 8 can be declared complete. Live supplier delivery is unconfigured and requires explicit authorization.

## Future features and license

Product/warehouse-stock separation, zero-stock semantics, transfers/reservations, provider-specific connectors, procurement workflows, advanced report exports, full countries, currencies, warehouse permissions, MFA and machine-client OAuth are in the [canonical backlog](docs/backlog.md). No open-source license has been selected. Original assessment materials are not distributed here.
