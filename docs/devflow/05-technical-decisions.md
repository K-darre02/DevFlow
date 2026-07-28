# Technical Decisions

Architecture Decision Record (ADR) format: Context → Decision → Alternatives Considered → Consequences. These are the choices worth defending in an interview, not an exhaustive list of every setting.

## 1. Pragmatic Application Services over full CQRS-per-operation

**Context**: The backend needs to stay testable and maintainable as feature surface grows (projects, tasks, comments, notifications, reporting all touch overlapping data).

**Revision note**: an earlier version of this design wrapped every use case as its own MediatR command or query handler (full CQRS). An architecture review flagged that as more ceremony than a solo-maintained CRUD-heavy app needs — one file per operation, for operations that mostly aren't complex enough to earn it. The decision below reflects the revised approach.

**Decision**: Clean Architecture layering is kept (Domain/Application/Infrastructure/API), but the Application layer is organized as straightforward **application services** with clear method signatures (`ITaskService.UpdateStatusAsync(...)`, `IProjectService.ArchiveAsync(...)`), each running its own FluentValidation and business rules directly — not a command class + handler class + validator class per operation. **MediatR is retained, narrowly, for post-commit domain-event fan-out**: when a write needs to trigger multiple independent side effects (activity log entry, notification creation, SignalR broadcast) without the service method needing to know about all of them, that's published as a lightweight `INotification` after the transaction commits. See [Architecture §5](01-architecture.md#5-real-time-updates) for where this fan-out is actually used.

**Alternatives considered**:
- *Full CQRS (every operation as a MediatR command/query)*: the originally documented approach. Each use case independently testable, but the boilerplate-to-value ratio doesn't hold up at this system's actual complexity for a solo build.
- *Fat service classes with no fan-out mechanism at all*: simplest, but the moment one write needs to trigger 2–3 unrelated side effects, the service method either grows a pile of direct calls (activity log, notification, SignalR — all inline) or those concerns leak into each other.

**Consequences**: Less boilerplate per feature than full CQRS; services stay easy to trace (open `TaskService`, read the method). The tradeoff is that services can grow multiple related methods over time — kept in check by one service per aggregate root (`TaskService`, `ProjectService`, `TenantService`), not one per use case. MediatR's footprint is now a single, easily-explained purpose (domain-event fan-out) instead of the primary request-handling mechanism, which is also an easier thing to defend in review than "we use MediatR for everything."

## 2. Multi-tenancy model: shared schema over schema-per-tenant or database-per-tenant

**Context**: Every SaaS system needs a tenant isolation strategy; this is a foundational, hard-to-reverse choice made before any feature work.

**Decision**: Shared database, shared schema, `TenantId` row-level isolation enforced by EF Core global query filters (full detail in [Security §2](04-security.md#2-tenant-isolation)).

**Alternatives considered**:
- *Schema-per-tenant*: stronger logical isolation, but every migration has to run against N schemas — operationally heavier, and EF Core's tooling isn't built around dynamic schema selection.
- *Database-per-tenant*: strongest isolation (a query bug literally cannot cross tenants), closest to what a real enterprise SaaS might use — but means provisioning/monitoring/backing up N databases, which doesn't fit this system's scale or ops budget.

**Consequences**: A missed `TenantId` filter is a code-level bug, not something the infrastructure prevents outright — which is why isolation is enforced globally (not per-query) and covered by cross-tenant integration tests rather than trusted to convention. The other side of this tradeoff is **noisy-neighbor risk**: every tenant shares the same database's compute and IO budget, with no per-tenant resource governor. One tenant with an unusually large or active workload can degrade query latency for every other tenant on the same database. This is accepted, explicitly, as out of scope for v1 — the documented mitigation path if it ever becomes real is Azure SQL Elastic Pools (still shared-schema, but with per-tenant resource ceilings), not a re-architecture. See [Quality Attributes §2](07-quality-attributes.md#2-scalability).

## 3. JWT (stateless) over server-side sessions

**Context**: Need an auth mechanism for a SPA talking to a horizontally-scalable API.

**Decision**: Short-lived JWT access tokens + rotated refresh tokens (detail in [Security §4](04-security.md#4-authentication)).

**Alternatives considered**: Server-side session with a cookie + session store (e.g. Redis). Would centralize revocation (kill a session server-side, instantly) but adds a stateful dependency the API doesn't otherwise need, and complicates horizontal scaling (sticky sessions or a shared session store to keep in sync).

**Consequences**: Revocation is harder with pure JWTs — solved here by keeping access tokens short-lived (15 min) and tracking refresh tokens server-side (hashed) so *those* remain revocable, getting most of the operational benefit of sessions without the stateful access-token dependency. This design still relies on a cookie for the refresh token, which only stays simple (`SameSite=Strict`, no CSRF token needed) because of the same-origin deployment decided in §6 below — a stateless JWT design and a same-origin deployment topology are a package deal here, not two independent choices.

## 4. EF Core global query filters as the tenant-isolation mechanism (not per-repository manual filtering)

**Context**: Given the shared-schema decision (§2), something has to guarantee every query is tenant-scoped.

**Decision**: A single global query filter per tenant-scoped entity, configured once in `DbContext.OnModelCreating`, rather than requiring each service method to remember to add `.Where(x => x.TenantId == tenantId)`.

**Alternatives considered**: Manual filtering per query/service method. Simpler to understand at a glance, but isolation becomes a *convention* every future contributor has to remember on every new query — one missed `.Where()` is a cross-tenant data leak.

**Consequences**: Isolation becomes a structural property of the `DbContext`, not a per-developer discipline. The cost is that bypassing it (rare legitimate cases, e.g. a platform-admin cross-tenant report) requires an explicit, greppable `IgnoreQueryFilters()` call — which is the point: it makes bypasses visible instead of making the default case error-prone.

## 5. Azure SignalR Service over polling or a self-hosted WebSocket server

**Context**: The board needs to reflect other users' changes without a manual refresh (real-time collaboration is a core product requirement).

**Decision**: Azure SignalR Service, with per-project groups (detail in [API Design §6](03-api-design.md#6-real-time-surface-signalr)).

**Alternatives considered**:
- *Short-polling*: simplest to implement, but either wastes requests at low activity or lags noticeably at a poll interval long enough to be efficient.
- *Self-hosted WebSockets (raw `ConnectionMapping` in ASP.NET Core)*: works, but ties WebSocket connection state to a specific App Service instance, which fights horizontal autoscaling (a client's persistent connection would need to survive instance rebalancing).

**Consequences**: Azure SignalR Service externalizes connection management, so the API layer stays stateless and scales independently of open WebSocket count. Adds an Azure-specific dependency (acceptable, given the whole stack targets Azure) and a service-tier ceiling — the Free tier caps concurrent connections and daily messages, which is fine for a portfolio demo but is a documented scaling limit, not an oversight; see [Quality Attributes §2](07-quality-attributes.md#2-scalability).

## 6. React SPA + separate API, deployed same-origin via a linked backend

**Context**: ASP.NET Core supports server-rendered UI natively (Razor Pages/MVC, Blazor Server/WASM) — a separate SPA is an explicit choice to add a second stack, and that choice has a real consequence for how auth cookies behave.

**Decision**: React + TypeScript SPA consuming a versioned REST API, with the two deployed so they share one origin: Azure Static Web Apps' **linked backend** feature proxies `/api/*` requests through to the API (Azure App Service) under the SPA's own domain. The browser only ever talks to one origin.

**Alternatives considered**:
- *Razor/Blazor (single stack)*: would keep everything in one language/repo and sidestep the origin question entirely. Rejected because a real-time, drag-and-drop-heavy board benefits from a mature client-side state/rendering model (React + TanStack Query), and demonstrating a clean API contract between an independent frontend and backend is itself part of what this project is meant to show.
- *SPA and API on their default, unrelated Azure domains (`*.azurestaticapps.net` / `*.azurewebsites.net`), talking cross-origin*: this was the original design. It breaks silently: an `HttpOnly, SameSite=Strict` refresh-token cookie (§3) is never sent cross-site, so refresh/logout would fail in a way that's easy to miss in early manual testing (access tokens still work for 15 minutes) and only surfaces once a token expires. Caught in architecture review, not left as a runtime surprise.
- *Custom subdomains under one parent domain (`app.devflow.dev` / `api.devflow.dev`) without a linked backend*: would also resolve the cookie issue (same registrable domain = same-site) and is a reasonable alternative, but requires owning and configuring a custom domain; the linked-backend approach achieves the same same-origin property using Azure's default domains, so it was preferred for a project without a purchased domain.

**Consequences**: Two codebases, two dependency graphs, and an API contract that has to be deliberately versioned and documented ([API Design](03-api-design.md)) rather than being an implementation detail hidden inside server-rendered pages — but the origin question is resolved at the infrastructure level, not worked around in application code (no permissive CORS, no dropped cookie security attributes to make cross-origin work).

## 7. Fixed task-status workflow, modeled as an enum rather than a lookup table

**Context**: Tools like Jira let each project define custom status columns and transition rules; that's a substantial subsystem on its own.

**Decision**: A fixed status set (`Backlog, ToDo, InProgress, InReview, Done`) modeled as a C# enum stored directly on `TaskItems.Status` — see [Database Design](02-database-design.md#table-notes). An earlier draft modeled this as a per-board `TaskStatuses` lookup table (seeded, FK-referenced); that indirection was removed once it was clear the set is fixed system-wide and never actually varies per board — a table exists to support variation, and there was none to support.

**Alternatives considered**:
- *Configurable workflow engine* (custom statuses, per-project transition rules, conditional automations): more realistic for an enterprise PM tool, but a significant scope and complexity increase that doesn't add proportional engineering-depth value over the fixed model — the interesting engineering problems here (isolation, concurrency, real-time fan-out) don't require it.
- *`TaskStatuses` lookup table, GUID FK from `TaskItems`* (the original design): models a workflow as if it could vary, when by decision it doesn't. Costs a join on the board query (the hottest read path) and needed a separate mechanism to know which status counts as "terminal" for completion tracking.

**Consequences**: Simpler schema, one less join on the board query, and the terminal status ("Done") is simply the enum's defined last value — no string-matching, no extra `IsTerminal` flag needed. The tradeoff is genuine: if configurable per-project workflows ever become a real requirement, that's a schema migration (enum column → lookup table + FK), not a config toggle. Judged unlikely to be worth pre-building for a fixed, well-understood workflow.

## 8. SQL Server Full-Text Search over a dedicated search service

**Context**: Task search needs to cover title/description across a project.

**Decision**: SQL Server Full-Text Search directly on `TaskItems`, scoped by the same `(TenantId, ProjectId)` index used elsewhere.

**Alternatives considered**: A dedicated search service (Elasticsearch, Azure Cognitive Search) — better relevance tuning and scales further, but introduces a second system to keep in sync with the source of truth (indexing pipeline, eventual consistency) for a search surface that, at this system's expected data volume, doesn't need it.

**Consequences**: One less moving part and no index-sync problem to solve. The explicit tradeoff — and the point at which this stops being sufficient — is discussed in [Engineering Challenges §6](06-engineering-challenges.md).

## 9. Board collapsed into Project — no separate `Board` entity

**Context**: The original schema modeled `Board` as its own entity, one-to-many under `Project`, largely to give `TaskStatuses` something to be seeded per. Once §7 removed the per-board status table, `Board` had no remaining reason to exist as a distinct row: every project has exactly one board, and `TaskItems` already pointed at `ProjectId` directly.

**Decision**: There is no `Boards` table. `GET /projects/{id}/board` ([API Design §2](03-api-design.md#2-resource-endpoints)) returns the project's tasks grouped by `Status` — a computed response shape, not a stored resource.

**Alternatives considered**: Keep `Board` as a 1:1 shadow table alongside `Project` for semantic clarity. Rejected — a 1:1 table that always exists in lockstep with its parent isn't modeling anything a nullable/embedded concept on `Project` couldn't, and it was only ever load-bearing for the now-removed per-board status configurability.

**Consequences**: One fewer table, one fewer join on the board-fetch path (already improved by §7). If multi-board-per-project ever becomes a real feature (e.g. separate boards for separate workflows within one project), reintroducing `Board` as an explicit entity is a straightforward additive migration — nothing about removing it now forecloses that.

## 10. Secure attachment access via object keys + short-lived SAS URLs, not public Blob URLs

**Context**: The original design stored a `BlobUrl` directly on `TaskAttachments`. On review, this was flagged as a tenant-isolation gap: depending on container configuration, that URL is either publicly fetchable by anyone who obtains it (no authorization check at all) or simply unusable without a mechanism that was never specified — either way, attachment access wasn't going through the same tenant/role checks every other resource in this system goes through.

**Decision**: `TaskAttachments` stores a `BlobKey` — an opaque, server-generated object key, never returned to clients as a usable URL. Reads go through `GET /tasks/{id}/attachments/{id}/download` ([API Design §5](03-api-design.md#5-example-attachment-download)), which authorizes the request against the task's tenant/role first, then mints a short-lived (5 min), read-only SAS URL scoped to that one object.

**Alternatives considered**:
- *Public Blob URL stored and returned directly* (the original design): simplest to implement, but access control lives entirely in "can you guess or find this URL," which isn't access control at all in a multi-tenant system.
- *Container-level access policy (private container, app-issued long-lived SAS at upload time)*: better than fully public, but a long-lived SAS baked in at upload time is effectively a permanent bearer credential for that file — if it leaks (browser history, referrer headers, logs), there's no way to revoke access short of rotating the whole container's keys.

**Consequences**: Every attachment read re-runs authorization and produces a credential that expires in minutes, closing both gaps above at the cost of one extra request (client hits the download endpoint, then follows the redirect) instead of using a stored URL directly — a small latency cost for a materially stronger isolation guarantee, consistent with how every other tenant-scoped resource in this system is treated.
