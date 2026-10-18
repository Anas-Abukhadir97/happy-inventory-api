# Phase 8 — free Azure demonstration

Status (18 October 2026): the release package and GitHub workflows are prepared and pass on both repositories. Azure CLI confirms the owner's enabled **Azure subscription 1** is a Free Trial with its spending limit **On**. Resource group `rg-happy-inventory-free` was created in UAE North, but Azure rejected the Windows F1 App Service plan: **F1 VMs quota 0, usage 0, required 1**. No App Service plan, web app, hosted URL, or paid resource exists. GitHub deployment remains gated because `AZURE_WEBAPP_NAME` is unset. Do not describe this milestone as deployed until quota is available and the live checks below pass.

The Azure F1 quota API also returned 0 in Qatar Central, North Europe, East US, East US 2, Central US, UK South, and Japan East. Some other queried regions returned only a zero `*` total quota. This is a subscription quota restriction, not an application build failure. Azure's [App Service quota guidance](https://learn.microsoft.com/en-us/troubleshoot/azure/app-service/troubleshoot-non-zone-redundant-quota-requests) says free/trial offers may have a zero default and may not support an increase; its [quota quickstart](https://learn.microsoft.com/en-us/azure/quotas/quickstart-increase-quota-portal) says only paid subscriptions can request quota increases. Do not change the subscription offer, create a paid plan, or enable GitHub deployment as a workaround. Once Azure grants **F1 VMs limit 1** in an eligible region, recheck both F1 and total regional limits, create the Windows F1 plan, and continue with the configuration and acceptance below. If Azure requires conversion to a paid subscription, obtain a separate billing decision first, even though F1's base SKU is listed at $0.

## Hosting decision (17 October 2026)

Use one Windows **App Service F1** app for the published .NET 10 API and the compiled React files. The browser and API then share the default HTTPS `*.azurewebsites.net` origin, preserving the existing `SameSite=Strict`, Secure refresh cookie, CSRF header and exact-origin checks. The suggested separate Static Web Apps Free frontend cannot proxy an existing App Service API: that integration requires Static Web Apps Standard. Changing the browser to cross-site cookies/CORS just to use two free hosts would weaken the reviewed authentication design. This demo therefore has one public URL and retains two source repositories and their independent CI.

App Service F1 is listed at $0 for its base plan, with 60 CPU minutes/day and 1 GB storage, one shared instance and no SLA. Use the default hostname, HTTPS-only mode, Windows code deployment, no paid add-ons, no Storage Account, Azure SQL, VM, custom domain, Application Insights or upgraded SKU. Resource Group and GitHub Actions configuration do not add Azure compute charges. Verify the actual subscription, region, SKU and displayed cost before creation; no assumption about account eligibility or total bill is made. F1 is for a small assessment demo, not a production availability promise. Budget alerts are notifications rather than a spending cap.

The opted-in `Storage:UseAppServiceHome=true` places SQLite under `%HOME%/data/inventory.db` and JSON logs under `%HOME%/data/logs`, outside deployment output. App Service documents `%HOME%` as persistent across restarts. It is a network-backed content share, however; SQLite on network storage can have locking and corruption risks. Keep one app instance and one API process, back up with SQLite's online backup API, and verify restore after deployment. This is an assessment compromise, not a recommended production database. If live persistence or backup checks fail, do not claim Phase 8 complete or switch silently to a paid service. The Azure SQL Free offer is a possible later provider migration only if eligible and separately tested; it is not used here.

## Configuration and delivery

Each repository has an Azure free workflow. Both check out the latest `main` of the other repository, run API and web checks, publish .NET, and copy `frontend/dist` to the API's `wwwroot`. This allows a merge in either repository to deliver a complete same-origin package. Deploy runs only when GitHub variable `AZURE_WEBAPP_NAME` is set. Each `azure-free` environment uses OIDC secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; no publishing profile or long-lived Azure password is stored. The Azure identity needs two GitHub federated credentials, each scoped to one repository's `azure-free` environment, with deployment rights limited to this web app. Merge the two repositories sequentially: GitHub concurrency locks are repository-local, so simultaneous merges could race to publish an older cross-repository pair. Record the exact source SHAs after every deployment. Never assume an image published to GHCR is the code deployed to F1.

Set these App Service settings in **Production** before first startup:

| Setting | Value / source |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Web__ServeSpa` | `true` |
| `Storage__UseAppServiceHome` | `true` |
| `Auth__BrowserOrigin` | Exact `https://<default-hostname>` returned by Azure |
| `Auth__SigningKey` | Random, stable secret of at least 32 UTF-8 bytes |
| `BootstrapAdmin__Password` | New strong first-start password, never the development demo password |
| `BootstrapAdmin__Email` | Intended owner email |
| `SupplierOutbox__WorkerEnabled` | `true` only for the local recording adapter; no real supplier message |

Do not set `ConnectionStrings__Inventory` together with `Storage__UseAppServiceHome`. Preserve the signing key and database across deploys; losing either invalidates sessions or data. Set HTTPS-only on the Web App and keep SCM basic publishing credentials disabled once OIDC deployment is operational. Confirm in the portal that the plan is **F1**, the OS is **Windows**, and any budget or trial choices do not opt into paid overage.

## Acceptance before calling it deployed

1. Record subscription, region, F1 plan ID/SKU, default URL, backend and frontend commits, workflow URL, and actual monthly cost view without copying secrets.
2. Verify HTTPS root and a direct SPA route, then confirm `/api/v1/health` gives 401 without a bearer token and Swagger is unavailable in Production.
3. Log in with the newly configured bootstrap credentials; check CSRF and refresh on reload, English/Arabic pages, and an authenticated health response. Do not publish credentials in a screenshot or log.
4. Create a temporary warehouse/item, restart and redeploy the app, then verify both persist. Remove the test records through the app/API.
5. Produce a consistent SQLite online backup, restore it in a controlled stopped-app test, and verify the records. A raw copy of a live SQLite file is not accepted as a backup.
6. Record a rollback of the previous package and the database restore point. An image/package rollback alone cannot undo a schema migration.
7. Check the deployment workflow after a merge and document the exact API/web commit pair. Monitor F1 CPU, storage and bandwidth quotas; exceeding them can make the demo unavailable.

Live supplier delivery is still unconfigured; do not send a provider test message without its recipient, channel and credentials. The free demo also retains SQLite's single-writer limit and has no uptime SLA.

## Sources

- [App Service Windows pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/windows/)
- [App Service `%HOME%` persistence](https://learn.microsoft.com/en-us/azure/app-service/operating-system-functionality)
- [Static Web Apps existing-API plan requirement](https://learn.microsoft.com/en-us/azure/static-web-apps/apis-overview)
- [App Service .NET 10 quickstart](https://learn.microsoft.com/en-us/azure/app-service/quickstart-dotnetcore)
- [Azure SQLite network filesystem caution](https://learn.microsoft.com/en-us/azure/app-service/configure-connect-to-azure-storage)
