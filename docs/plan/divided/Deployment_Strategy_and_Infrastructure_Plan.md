# Deployment, Cost, Infrastructure, and CI/CD Plan

## 1. Deployment principles

- Optimize for low fixed cost during the pilot, but never use free/sleeping infrastructure for customer webhooks or production orders.
- Deploy ordinary Docker containers. Aspire models and runs the topology locally; it does not lock the product to Azure or any other host.
- Keep development/staging and production data, credentials, provider connections, object-storage prefixes, and domains separate.
- Build an image once, test it, and promote that exact immutable image to production. Never rebuild from source during a production deploy.

## 2. Recommended low-cost topology

### Local development

```text
Laptop
└── .NET Aspire AppHost
    ├── ASP.NET Core API + co-hosted Hangfire worker
    ├── Next.js seller/public app(s)
    ├── PostgreSQL container
    └── optional Redis container
```

Use Aspire locally for one-command startup, service discovery, health checks, logs, traces, and debugging. A Cloudflare Tunnel or ngrok tunnel can expose the local webhook endpoint temporarily for Meta/provider development. A tunnel is never production hosting.

### Pilot production

```text
Cloudflare DNS/CDN/TLS
├── app.yourdomain.com
├── api.yourdomain.com
└── *.yourdomain.com             # seller storefronts
             │
             ▼
One VPS running Docker Compose
├── Caddy reverse proxy
├── Next.js app (dashboard + storefront route groups)
├── ASP.NET Core API + Hangfire worker
└── PostgreSQL

Cloudflare R2
├── product/knowledge assets
└── encrypted database backup copies
```

For the first paid pilot, use one 4–8 GB VPS. Keep API and Hangfire in one process, run PostgreSQL locally, omit Redis, and use Cloudflare R2 instead of local file storage. This is a deliberate cost optimization, not the long-term scale target.

### Growth topology

```text
CDN/DNS
├── Next.js frontend container(s)
├── API containers (multiple replicas)
├── dedicated worker containers
├── managed PostgreSQL
├── Redis
└── object storage
```

Move to this when pilot revenue/traffic, background-job load, database recovery requirements, or uptime expectations justify it.

## 3. Provider choices and cost analysis

### Recommended first production choice: a single VPS

Use a Hetzner VPS plus Docker Compose for the least fixed cost. Hetzner’s published June 2026 pricing lists CAX21 at US$12.49/month and CAX31 at US$24.99/month in Germany/Finland, excluding VAT; choose a 4–8 GB plan only after measuring the application. Singapore-region instances cost more but may improve Nepal latency. Pricing changes, so verify before purchase. Source: [Hetzner price list](https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/).

| Item | Pilot estimate | Notes |
|---|---:|---|
| VPS | US$13–25/month | API, web, worker, and PostgreSQL together. |
| Cloudflare DNS/CDN | US$0 initially | Supports wildcard DNS records for seller subdomains. |
| Cloudflare R2 | US$0 initially | Includes 10 GB-month free storage; product photos and backups remain small at pilot stage. |
| Domain | Varies annually | Buy/renew separately; Cloudflare Registrar sells at registry cost where available. |
| Monitoring/error tracking | US$0–10/month | Start with OpenTelemetry logs/metrics and an affordable error tracker. |
| Fixed infrastructure total | **~US$15–30/month** | Excludes domain, AI, payment, email/SMS, and channel fees. |

Cloudflare documents wildcard DNS on all plans and R2’s 10 GB-month free storage allowance/no-egress pricing model. Sources: [wildcard DNS](https://developers.cloudflare.com/dns/manage-dns-records/reference/wildcard-dns-records/), [R2 pricing](https://www.cloudflare.com/products/r2/).

### Managed PaaS alternatives

| Provider | Expected pilot cost | Trade-off |
|---|---:|---|
| Railway | ~US$15–35/month | Very easy deployment; usage pricing grows with API/database/worker memory. Hobby has a US$5 minimum/credit, with published RAM at US$10/GB-month and CPU at US$20/vCPU-month. |
| Render | ~US$25–40+/month | Managed Postgres/Redis and simple Docker deploys; higher fixed cost when API, database and Redis are separated. |
| Azure Container Apps | Usually higher for this MVP | Best later for managed scaling and Azure-native operations; avoid as the first cost-minimal pilot. |
| AWS EC2 | Comparable VPS model but more configuration | Valid later; not the simplest low-cost starting point. |

Railway source: [pricing](https://docs.railway.com/pricing). Render currently lists managed Postgres from US$6/month and Redis-compatible Key Value from US$10/month before application compute; its free instances are unsuitable for production webhooks because free web services sleep and free Postgres is time-limited. Sources: [Render pricing](https://render.com/pricing), [Render FAQ](https://render.com/docs/faq).

### Variable costs to meter from the first release

- AI inference: enforce provider/model budgets, per-tenant usage events, and hard/soft quotas.
- WhatsApp/channel conversation or provider fees.
- Payment-provider transaction fees, refunds, settlement, and platform-service fees if the business model activates them.
- Email/SMS notifications.
- Image-storage volume and CDN/bandwidth once catalogs grow.

## 4. What to use and what not to use initially

| Use now | Do not require at MVP | Add when needed |
|---|---|---|
| .NET 10, Next.js, PostgreSQL, Docker Compose, Caddy, Cloudflare, R2, Aspire local, Hangfire/Postgres storage | Kubernetes, managed Redis, managed Postgres, separate workers, custom domains, Azure Container Apps | Managed Postgres, Redis, dedicated workers, autoscaling containers, custom domain/TLS lifecycle, Azure/AWS services |

Do not use GitHub Pages for the product. It only hosts static files and cannot run the API, database, background jobs, webhook receiver, checkout, or dynamic tenant storefront. It can host public documentation only.

## 5. Environment model

| Environment | Purpose | Infrastructure |
|---|---|---|
| Local | Daily development and integration testing. | Aspire + Docker on developer machine; fake/test provider credentials. |
| Development/staging | Shared integration environment. | Separate small VPS or managed environment; auto-deployed from `develop`; separate database/storage/credentials. |
| Production | Seller/customer traffic. | Separate VPS initially; protected deployment from `main`/version tags; backups, alerts, and no test data. |

For the absolute lowest cost, keep only local + production until real pilot work requires a shared staging server. Do not mix unfinished development traffic/data with real production sellers once payments or customer PII are active.

## 6. CI/CD with GitHub Actions

### Branch strategy

```text
feature/* → pull request → develop → release pull request → main
```

- `feature/*`: CI only; developers use local Aspire. Optional disposable preview environments later.
- `develop`: automatically deploy to the shared development/staging server.
- `main` or signed version tag: production candidate only, protected by approval.
- Hotfixes merge to `main` and are back-merged to `develop`.

### Workflows

| Workflow | Trigger | Required work |
|---|---|---|
| `ci.yml` | Pull requests and pushes | .NET restore/build/test, Next.js lint/type-check/test/build, Docker build validation, dependency/security checks. |
| `deploy-dev.yml` | Push to `develop` | Build immutable images, push to GHCR, run controlled migration, deploy dev Compose stack, smoke test. |
| `deploy-prod.yml` | Merge to `main` or release tag | Promote tested image digest, require approval, backup/migrate/deploy, health/smoke tests, alert watch. |
| `rollback.yml` | Manual dispatch only | Redeploy known good image digest/tag and run post-rollback smoke test. |

### Deployment flow

```text
PR → CI passes
  → merge develop → build image tagged with commit SHA → push GHCR
  → deploy the exact SHA image to development
  → release approval → deploy the same SHA/digest to production
```

On each target server, a restricted deployment account runs a trusted deploy script:

1. Verify the desired image digest/tag.
2. Take/confirm a backup before a production migration.
3. Run a one-off migration container. Do not let every API instance migrate on startup.
4. Pull images and run `docker compose up -d`.
5. Verify API readiness, worker health, frontend response, and a safe smoke path.
6. Keep the previous image digest for rollback.

### GitHub environments and secrets

Create GitHub environments named `development` and `production`.

- `development` accepts `develop` deployments automatically.
- `production` accepts only `main`/release tags and requires manual approval.
- Environment secrets contain only deployment connectivity (SSH host/user/key, registry pull credential). Keep application secrets in each server’s protected environment file or later secret manager.
- Never place DB passwords, SMTP credentials, social tokens, AI keys, payment secrets, or private keys in Git, container images, or workflow logs. Inject SMTP settings through the target's protected environment file or an approved secret manager; production reset URLs use HTTPS and TLS SMTP.

GitHub environments can restrict deployment branches, protect environment secrets, require reviews, and provide deployment history. Sources: [GitHub environments](https://docs.github.com/en/actions/concepts/workflows-and-actions/deployment-environments), [deployment protection rules](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments). GitHub Actions can build and publish Docker images to GitHub Container Registry: [guide](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images).

## 7. Backups, monitoring, and recovery requirements

- Encrypted daily PostgreSQL backup to R2; retain an appropriate rolling history.
- Test restoring the backup before accepting pilot sellers and periodically after launch.
- Health/readiness checks for API, worker, PostgreSQL and disk capacity.
- Alert on unavailable API, webhook processing lag, worker failures/DLQ, backup failure, disk pressure, elevated errors, and failed checkout/payment verification.
- Document recovery for webhook replay, stuck reservation, payment dispute, lost provider token, database restore, and rollback.

## 8. Deployment decision

**Start:** Aspire locally; Docker Compose on a single Hetzner VPS; Cloudflare DNS/R2; GitHub Actions + GHCR; no Redis; API and worker co-hosted.

**Upgrade trigger:** move database/worker/cache to managed/separate services once the pilot has real recurring sellers, resource measurements exceed safe limits, recovery requirements rise, or downtime becomes commercially unacceptable.

## 9. Master reference

This file is the deployment division. The full product/architecture details live in [`../plan.md`](../plan.md).
