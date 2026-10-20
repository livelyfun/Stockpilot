# Deployment

StockPilot is a single ASP.NET Core container plus PostgreSQL. This guide covers container builds,
configuration, a migration-job pattern, proxy/TLS notes and free-tier options.

> This document is deployment *preparation* guidance. Nothing here has been applied to a live
> environment, and no production systems are touched by the repository.

## Container image

`Dockerfile` is a two-stage build:

1. **build** – `mcr.microsoft.com/dotnet/sdk:10.0` restores and publishes the web project in Release
   mode (`UseAppHost=false`, so no native launcher is produced).
2. **runtime** – `mcr.microsoft.com/dotnet/aspnet:10.0` copies the published output, listens on
   `http://+:8080` and runs as the non-root `app` user (`$APP_UID`).

Build and run locally:

```bash
docker build -t stockpilot:local .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Host=db;Port=5432;Database=stockpilot;Username=stockpilot;Password=…" \
  -e Seed__AdminEmail="admin@example.com" \
  -e Seed__AdminPassword="…" \
  stockpilot:local
```

`/health` returns `200 OK` once the app is listening; point your platform's health check at it.

## Compose stack

`docker-compose.yml` runs PostgreSQL 17 and the web container:

```bash
cp .env.example .env
# set POSTGRES_PASSWORD, Seed__AdminEmail, Seed__AdminPassword in .env
docker compose up --build
```

- `db` publishes port `55432` **for local inspection only**; remove that mapping in production.
- `web` waits for the database health check, then starts and applies migrations.
- `pgdata` is a named volume; back it up or snapshot it.

## Configuration reference

All settings come from environment variables using the `__` separator.

| Key | Required | Notes |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | yes | PostgreSQL connection string |
| `Seed__AdminEmail` | bootstrap | initial administrator email |
| `Seed__AdminPassword` | bootstrap | strong password; used only when the account does not exist |
| `Seed__AdminFullName` | no | display name |
| `Seed__DemoData` | no | keep `false` in production |
| `Seed__DemoPassword` | no | development demo accounts only |
| `Database__AutoMigrate` | no | `true` applies migrations at startup |
| `HttpsRedirection__Enabled` | no | set `false` when a proxy terminates TLS |
| `ForwardedHeaders__Enabled` | no | `true` behind a reverse proxy so the app sees the original scheme/IP |
| `ASPNETCORE_ENVIRONMENT` | no | `Production` outside development |
| `ASPNETCORE_URLS` | no | defaults to `http://+:8080` in the image |

Never bake secrets into the image or commit them. Use your platform's secret store (environment
variables, container secrets, Key Vault, etc.).

## Migrations in production

Two supported patterns:

1. **Startup migration (simple, single instance).** Keep `Database__AutoMigrate=true`. The app applies
   pending migrations before serving traffic. Fine for small deployments, but two instances starting
   at once can race on the migration lock.

2. **Separate migration job (zero-downtime).** Set `Database__AutoMigrate=false` on the web containers
   and run migrations once per release as a job or init container:

   ```bash
   dotnet tool restore
   dotnet ef database update --project src/StockPilot.Web --startup-project src/StockPilot.Web
   ```

   Roll out the new app version after the job succeeds. Always take a backup before destructive
   migrations.

## Proxy, TLS and headers

- Terminate TLS at the platform load balancer/proxy and forward `X-Forwarded-Proto` and
  `X-Forwarded-For`.
- Set `ForwardedHeaders__Enabled=true` and `HttpsRedirection__Enabled=false` so the app does not
  redirect into a loop and does log the real client IP.
- The app already sends `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`,
  `Permissions-Policy` and a Content-Security-Policy header. Tighten the CSP at the proxy if you embed
  the app in a portal.
- Auth cookies are HTTP-only and SameSite=Lax; behind HTTPS they are marked `Secure` automatically.

## Database

- Use managed PostgreSQL when possible; it handles backups, PITR and patching.
- Set connection pooling appropriately (`Maximum Pool Size`) for your plan; the app uses a scoped
  `DbContext` per request.
- The app runs `SELECT … FOR UPDATE` on documents and balances. Keep the database in a single primary
  (no multi-master) so row locks behave as expected.
- Schedule regular backups of `AuditLogs`, documents and the ledger; that history is the system's value.

## Free-tier options

These are commonly available, low-cost starting points for a student or hobby deployment. Verify
current limits before relying on them.

| Option | Notes |
|---|---|
| **Render / Railway / Fly.io** | Container hosting with a free or low-cost tier; add a managed PostgreSQL add-on. Enable HTTPS and forwarded headers. |
| **Azure App Service (F1/B1)** | Container or code deploy; Azure Database for PostgreSQL Flexible Server for data. |
| **Neon / Supabase** | Serverless PostgreSQL free tiers; use the pooled connection string. Cold starts may add latency. |
| **A small VPS** | Run `docker compose` directly; put Nginx or Caddy in front for TLS. |
| **Container registry** | GitHub Container Registry (public images are free) works well with CI. |

Free tiers usually scale to zero and have small connection limits. Keep `Database__AutoMigrate=true`
only if you run a single instance, and expect the first request after idle to be slower.

## Production checklist

- [ ] Strong, unique `Seed__AdminPassword`; the bootstrap admin signs in and changes it.
- [ ] `Seed__DemoData=false` and no demo accounts.
- [ ] `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] HTTPS enforced at the proxy; `ForwardedHeaders__Enabled=true`.
- [ ] `Database__AutoMigrate=false` once you move to the migration-job pattern.
- [ ] Database backups and a restore rehearsal.
- [ ] `/health` wired to the platform health check.
- [ ] Log aggregation for `Microsoft.AspNetCore` warnings and audit failures.
- [ ] Secrets stored outside the image and the repository.
