# Phase 6: containers and publication

The API and web remain separate public repositories with independent checks and GHCR publication. The API image is built from .NET 10 and runs as its non-root `APP_UID`; the web image builds with pinned Node 24 dependencies and runs Caddy as UID 10001. The release manifest pins the tested pair by immutable digest and records each source commit. These images are for local Compose and later deployment planning; Azure has not been provisioned.

## Local setup

Clone `happy-inventory-api` as `backend/` and `happy-inventory-web` as its sibling `frontend/`. In `backend/`, copy `.env.example` to ignored `.env`. Set a cryptographically random `AUTH_SIGNING_KEY` (at least 32 bytes) and a strong `BOOTSTRAP_ADMIN_PASSWORD`. The first admin email defaults to `admin@happywarehouse.com`; the password has no Production default. Do not copy or use the Development demo password. Keep the signing key stable while sessions exist.

Run `docker compose up -d --build` and open `https://localhost:8443`. Caddy generates a local CA and retains it in `caddy_data`; trust that CA only on the development machine. The host port is bound to `127.0.0.1`, the API port is not published, and only Caddy's fixed private-network IP can provide forwarded HTTPS headers. Browser refresh cookies remain `Secure`, `HttpOnly`, and same-origin. The authenticated `/api/v1/health` reports database readiness. `/health/live` and `/health/ready` are loopback-only container probes; Caddy does not route them.

The named `inventory_data` volume holds the SQLite database and JSON logs. Run one API instance only. `docker compose restart api` or `docker compose down` preserves data; `docker compose down -v` destroys all Compose volumes. The API container name also prevents scaling the SQLite writer within this Compose stack.

## Backup and recovery

Run `python scripts/sqlite_volume.py backup` while the API is running. It uses SQLite's online `.backup` command inside the API container, copies the consistent snapshot to ignored `backups/`, and removes the temporary volume copy. Keep protected off-host copies of backups, since they contain user and inventory data. Run `python scripts/sqlite_volume.py restore backups/<file>.db` to validate the backup, stop the API, replace its database, clear stale WAL/SHM files, and restart. Restore replaces all current database content. Inspect the authenticated health route and application data afterward.

For the published pair, `python scripts/release_pair.py pull` and `python scripts/release_pair.py up -d --no-build` load the image digests from `releases/phase-6-tested.json`; the helper also supports `backup`, `restore <file>`, and `down`. Never use `down -v` unless intentionally deleting the database. The manifest is a pair of already-built images: the web and API sources have different commits and must not be assumed to share a revision.

## Evidence and limits

- Web PR [#7](https://github.com/Anas-Abukhadir97/happy-inventory-web/pull/7): hosted container build, existing web checks and real browser E2E passed. Its merged-main [CI run](https://github.com/Anas-Abukhadir97/happy-inventory-web/actions/runs/39063103680) published the web digest recorded in the manifest.
- API PR [#7](https://github.com/Anas-Abukhadir97/happy-inventory-api/pull/7): 41 integration tests, contract/build/format checks, and a hosted Compose acceptance job passed. Its merged-main [CI run](https://github.com/Anas-Abukhadir97/happy-inventory-api/actions/runs/39063278474) published the API digest recorded in the manifest.
- The API CI `release-pair` job pulls both digest-pinned images without sibling source checkouts and repeats clean start, non-root, HTTPS/authenticated health, restart persistence, and online backup/restore on disposable named volumes. Its green status is the acceptance evidence for the exact manifest pair.

The GitHub runner supplies Docker because Docker Desktop's engine could not be started in the local Windows session; local .NET, web, contract and Compose-configuration checks passed. The Caddy local CA is not a publicly trusted certificate. Live supplier delivery stays unconfigured. Azure hosting, public TLS, rollback and cost approval remain Phase 8; reviewer packaging and handoff remain Phase 7.
