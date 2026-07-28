# Quality Attributes

Engineering targets and constraints beyond functional behavior. Security is covered separately in [Security](04-security.md); this document covers performance, scalability, availability, observability, and maintainability.

## 1. Performance

- API p95 latency < 300ms for standard CRUD operations under nominal load.
- Board view (project with ≤200 tasks) renders in < 1s from API response received — driven by the single-round-trip board query, see [Engineering Challenges §4](06-engineering-challenges.md).
- Real-time task updates delivered to connected clients within 500ms of the originating write.

## 2. Scalability

This system's scalability story has real limits, held deliberately rather than left implicit. Stated explicitly below, with the mitigation path named for each — not implemented now, but not undiscovered either.

**What scales well:**
- The API is stateless (no in-process session state), so it scales horizontally behind Azure App Service autoscale rules without sticky sessions.
- The shared-schema data model supports thousands of tenants without per-tenant infrastructure provisioning — see [Technical Decisions §2](05-technical-decisions.md) for the tradeoff analysis.
- Tenant-prefixed composite indexes keep query performance stable as tenant count grows (see [Database Design §Indexing Strategy](02-database-design.md#indexing-strategy)).
- Real-time fan-out is scoped per-project, not per-tenant, so connection/broadcast cost scales with what users are actually viewing, not total tenant activity — see [Engineering Challenges §3](06-engineering-challenges.md).

**Named limits, and the trigger for addressing each:**

| Limit | Why it exists | Mitigation, and when to reach for it |
|---|---|---|
| **Noisy-neighbor risk** — all tenants share one database's compute/IO budget, with no per-tenant resource governor | Direct consequence of the shared-schema decision ([Technical Decisions §2](05-technical-decisions.md)) | Azure SQL Elastic Pools (per-tenant resource ceilings within the same shared-schema model). Trigger: one tenant's query load measurably degrading latency for others in Application Insights, not a preemptive build. |
| **No caching layer** — dashboard counts, burndown series, and unread-notification counts hit SQL directly on every request | Not yet justified at expected data volume; adding a cache before there's a measured hot path is premature | Azure Cache for Redis in front of these specific read paths. Trigger: p95 latency on those endpoints (§1) exceeding target under realistic load, not "caching is generally a good idea." |
| **SignalR Service tier ceiling** — the Free tier caps concurrent connections and daily message volume | Cost-appropriate for a portfolio demo | Azure SignalR Service Standard tier (scaling units). Trigger: concurrent connection count or message volume approaching the Free tier's documented limits. |
| **Single background worker, colocated with the API's App Service** — notification digests and invite emails run as an in-process `IHostedService` sharing the API's compute | Simplest possible deployment for v1; also means the job's reliability depends on the App Service plan's "Always On" setting, and it competes with request handling for the same process's resources under load | Promote to a standalone Azure Function (Timer trigger). Trigger: either observed digest/email delivery gaps, or API request latency showing contention with background work in Application Insights. |
| **Full-text search runs on the primary transactional database** | Avoids a second system (indexing pipeline, eventual consistency) at a scale that doesn't need it | Dedicated search service (Azure Cognitive Search) behind the existing `ISearchTasks` interface boundary. Trigger and full discussion in [Engineering Challenges §6](06-engineering-challenges.md). |
| **Single-region deployment** | Solo-developer operational simplicity | Out of scope for v1 by design, not a gap to silently accept — see [Availability §3](#3-availability) below. |

The common thread: every one of these is a scale-out lever that exists and is named, deliberately not pulled until there's a measured reason to pull it. None of them require an architectural rewrite when that day comes — each has a specific, bounded next step.

## 3. Availability

- Target 99.9% uptime, single-region Azure deployment.
- Multi-region failover and disaster recovery are explicitly out of scope — a documented tradeoff, not an oversight, made in exchange for solo-developer operational simplicity.
- `/health` endpoint reports dependency status (database, blob storage, SignalR) for uptime monitoring and deploy verification.

## 4. Observability

- Structured logs (Serilog) carry a correlation ID per request, propagated through async/background work so a single request's full trace — including any background job it triggered — can be reconstructed.
- Application Insights captures request telemetry, dependency calls (DB, blob, SignalR), and unhandled exceptions.
- Background jobs are idempotent and safely retryable on transient failure — see [Engineering Challenges §5](06-engineering-challenges.md).

## 5. Maintainability

- Clean Architecture layering (Domain/Application/Infrastructure/API) with dependencies pointing inward only — see [Architecture §3](01-architecture.md#3-backend-internal-layering-clean-architecture).
- Automated test coverage exceeds 70% on Domain and Application layers, enforced as a CI gate.
- CI runs build, lint, and test on every pull request; merges to `main` are blocked on a red pipeline.
- Search is isolated behind an interface boundary so its implementation can change without touching callers — see [Engineering Challenges §6](06-engineering-challenges.md), a pattern applied consistently at other integration points (email delivery, blob storage) for the same reason.

## 6. Accessibility

- WCAG 2.1 AA target on the React frontend — keyboard navigability, sufficient color contrast, ARIA labeling on interactive components, notably a non-drag keyboard fallback for the drag-and-drop board.
