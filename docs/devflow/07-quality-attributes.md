# Quality Attributes

Engineering targets and constraints beyond functional behavior. Security is covered separately in [Security](04-security.md); this document covers performance, scalability, availability, observability, and maintainability.

*As-built — a few items below (test coverage as a CI gate, background-job/email scaling limits) describe what was originally planned rather than what exists; called out inline rather than left implying automated enforcement that isn't there.*

## 1. Performance

- No formal load testing has been done against this system — the numbers an earlier draft of this document stated as targets (p95 < 300ms, board render < 1s, real-time delivery < 500ms) were aspirational, not measured. `GET /tasks?projectId=` is a single-round-trip query with `.Include()` for the assignee ([Engineering Challenges §4](06-engineering-challenges.md)), which is the right shape for those targets to be achievable, but nothing in CI currently asserts them.

## 2. Scalability

This system's scalability story has real limits, held deliberately rather than left implicit. Stated explicitly below, with the mitigation path named for each.

**What scales well:**
- The API is stateless (no in-process session state beyond SignalR's own connection tracking), so request handling scales horizontally behind Azure App Service autoscale rules without sticky sessions.
- The shared-schema data model supports thousands of tenants without per-tenant infrastructure provisioning — see [Technical Decisions §2](05-technical-decisions.md) for the tradeoff analysis.
- Tenant-prefixed composite indexes keep query performance stable as tenant count grows (see [Database Design §Indexing Strategy](02-database-design.md#indexing-strategy)).

**Named limits, and the trigger for addressing each:**

| Limit | Why it exists | Mitigation, and when to reach for it |
|---|---|---|
| **Noisy-neighbor risk** — all tenants share one database's compute/IO budget, with no per-tenant resource governor | Direct consequence of the shared-schema decision ([Technical Decisions §2](05-technical-decisions.md)) | Vertical scaling of the Postgres instance, connection-level limits per tenant (PgBouncer), or Citus-based sharding (Azure Cosmos DB for PostgreSQL) if true per-tenant resource isolation is ever needed. Trigger: one tenant's query load measurably degrading latency for others in Application Insights, not a preemptive build. |
| **No caching layer** — dashboard counts and unread-notification counts hit SQL directly on every request | Not yet justified at expected data volume | Azure Cache for Redis in front of these specific read paths. Trigger: p95 latency on those endpoints exceeding a measured target under real load. |
| **Self-hosted SignalR tied to a single App Service instance** — real-time connections aren't externalized to a managed service, so they can't survive an instance being rebalanced, and connection count is capped by what one instance can hold | A deliberate single-instance simplification — see [Technical Decisions §5](05-technical-decisions.md)'s revision note | Azure SignalR Service, which is what this was originally designed around. Trigger: any need to run more than one API instance at once. |
| **Real-time fan-out is per-tenant, not per-project** — every connected client in a tenant receives every task/project event for that tenant | Never narrowed to per-project groups after the original design called for it — see [Engineering Challenges §3](06-engineering-challenges.md) | Per-project SignalR groups, joined/left as clients navigate between boards. Trigger: same as above — a tenant with enough concurrent projects/users that broadcast volume becomes measurable. |
| **Full-text search runs on the primary transactional database, with no persisted/indexed `tsvector` column yet** | Avoids a second system at a scale that doesn't need one; the indexed-column optimization was deferred, not implemented — see [Technical Decisions §8](05-technical-decisions.md) | A generated, GIN-indexed `tsvector` column, or a dedicated search service (Azure Cognitive Search) behind the existing `ISearchService` interface boundary. Trigger and full discussion in [Engineering Challenges §6](06-engineering-challenges.md). |
| **No email delivery of any kind** — invites and notifications are in-app/manual-share only, not a scale limit so much as a missing capability | Never built — see [Architecture §6](01-architecture.md#6-background-processing) | Not a scaling lever to pull later so much as a feature to build: a background worker (or Azure Function) plus an email provider, if email delivery is ever needed. |
| **Single-region deployment** | Solo-developer operational simplicity | Out of scope for v1 by design, not a gap to silently accept — see [Availability §3](#3-availability) below. |

## 3. Availability

- No formal uptime target has been measured or committed to — this is a portfolio deployment, not an operated production service with an SLA.
- Multi-region failover and disaster recovery are explicitly out of scope — a documented tradeoff, not an oversight, made in exchange for solo-developer operational simplicity.
- `/health` reports the database dependency's status (`AddHealthChecks().AddDbContextCheck<DevFlowDbContext>()`) for deploy verification.

## 4. Observability

- Structured logs (Serilog) are enriched with the current request's ASP.NET Core `TraceIdentifier` via `LogContext.PushProperty`, so every log statement written while handling a request — not just a one-line summary — carries the same value, including calls made from within Application-layer services. `UseSerilogRequestLogging()` additionally emits one summary line per request (path, status, elapsed).
- `LogWarning` on failed login attempts (`AuthController.Login`) and authorization denials (`ForbiddenException`, handled centrally in `GlobalExceptionHandler`) — the two categories of event actually worth alerting on an anomalous rate of; nothing else in the Application layer logs at Warning/Information today beyond error paths.
- Application Insights (via the Azure Monitor OpenTelemetry distro, `UseAzureMonitor()`) captures request/dependency/exception telemetry automatically when a connection string is configured — see [`infra/README.md`](../../infra/README.md#secrets-handling). No alert rules are provisioned in Bicep; configuring them is a manual follow-up in the Azure Portal, not automated here.

## 5. Maintainability

- Clean Architecture layering (Domain/Application/Infrastructure/API) with dependencies pointing inward only — see [Architecture §3](01-architecture.md#3-backend-internal-layering-clean-architecture).
- `ci.yml` runs backend build + test and frontend lint + build on every pull request; `deploy.yml` re-runs the same gate before deploying to `main`. Neither enforces a coverage threshold — there is no CI-gated coverage percentage, despite an earlier draft of this document claiming one; the backend integration test suite (194 tests, all in `DevFlow.IntegrationTests`) is the actual signal, not a coverage number.
- Search and Blob Storage are each isolated behind an interface (`ISearchService`, `IBlobStorageService`) with a production implementation and a portable local/test substitute — see [Engineering Challenges §6](06-engineering-challenges.md) and [Architecture §9](01-architecture.md#9-local-development). No such interface exists for email, since no email integration was ever built.

## 6. Accessibility

No formal WCAG audit has been performed. What's actually true as of this review: icon-only interactive controls carry `aria-label`s, modals trap focus and restore it to the triggering element on close, the Kanban board supports both pointer/touch drag (via `@dnd-kit`'s `PointerSensor`) and an explicit non-drag "Open" button on each card so keyboard-only users aren't limited to reordering via arrow keys, and secondary/meta text (timestamps, counts, empty-state copy) uses `slate-500`, not a lighter gray that fails contrast at normal text size. This is the result of a targeted accessibility pass during this review, not a certified AA audit — gaps almost certainly remain outside what that pass covered.
