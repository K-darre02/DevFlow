# Engineering Challenges

Concrete hard problems in this system and how the design addresses them — the parts of DevFlow most worth walking through in a technical interview.

## 1. Enforcing tenant isolation by construction, not by convention

**Problem**: In a shared-schema multi-tenant database, the single most damaging bug class is a query that forgets to filter by tenant — it doesn't crash, it just silently returns or mutates another organization's data.

**Approach**: Isolation is pushed down to the data-access layer as a structural property rather than left as a per-query discipline. EF Core's global query filters apply `TenantId` scoping to every LINQ query against a tenant-scoped `DbSet` automatically, and the current tenant is derived once per request, from the validated JWT, by middleware — never from anything client-suppliable. Bypassing the filter requires an explicit `IgnoreQueryFilters()` call, which doesn't appear anywhere in normal feature code, so it's a clear signal in review rather than something that can happen by omission. Verified with integration tests that authenticate as one tenant and assert a `404` when requesting a resource ID known to belong to another. Full detail in [Security §2](04-security.md#2-tenant-isolation).

**Why it's hard**: The failure mode is silent — a missing filter doesn't throw, it just returns the wrong (or too much) data — so the mitigation has to be structural (impossible to omit by accident) rather than something caught by a test someone remembered to write.

## 2. Optimistic drag-and-drop with concurrent-edit conflicts

**Problem**: Dragging a task card to a new status column needs to feel instant — waiting on a network round-trip before the card moves reads as laggy. But if two users move the same card at nearly the same time, a naive "last write wins" silently discards one user's change with no indication anything went wrong.

**Approach**: The frontend applies the status change optimistically (TanStack Query's optimistic update pattern) the instant the drag completes, then confirms against the server. `TaskItems` carries a `RowVersion` concurrency token ([Database Design](02-database-design.md)); the update request sends it as `If-Match`. If the version is stale — someone else moved the card first — the API returns `409 Conflict` with the task's current state instead of blindly overwriting ([API Design §4](03-api-design.md#4-example-status-change-with-conflict-detection)). The client rolls the optimistic move back, shows the actual current state, and lets the user decide whether to reapply their change.

**Why it's hard**: Getting instant-feeling UI *and* correctness under concurrent writes are in tension — most naive optimistic-UI implementations either skip conflict detection entirely (silent data loss) or fall back to pessimistic locking (kills the responsiveness that was the point). The concurrency token makes the conflict visible without blocking the common (uncontended) case at all.

## 3. Real-time fan-out without over-broadcasting

**Problem**: Pushing every task mutation to every connected client is the naive implementation of "real-time board updates" — and it doesn't scale: a tenant with 50 active projects and 30 users would have every client receiving updates for boards they aren't even looking at.

**Approach**: SignalR connections join a group scoped to the specific project board being viewed (`project:{projectId}`), not the tenant as a whole. A task mutation broadcasts only to that project's group ([API Design §5](03-api-design.md#5-real-time-surface-signalr)). Clients subscribe/unsubscribe as they navigate between boards, so connection group membership always matches what's actually rendered.

**Why it's hard**: The easy version (broadcast to the whole tenant, let clients filter) pushes the cost onto every connected client and grows with total tenant activity rather than with what any individual user is looking at — the wrong axis to scale on for a system meant to support many concurrent projects per tenant.

## 4. Avoiding N+1 queries on the board endpoint

**Problem**: Rendering a board means fetching every task in a project along with its status, assignee, and tags — a naive implementation (fetch tasks, then loop and fetch each task's assignee/tags) issues one query per task, degrading linearly as board size grows.

**Approach**: `GET /projects/{id}/board` ([API Design §2](03-api-design.md#2-resource-endpoints)) is backed by a single query using EF Core's `Include`/`ThenInclude` projection to eager-load assignee and tags in one round trip, rather than a repository method that returns bare `TaskItem`s for the caller to hydrate. This is enforced as a review checklist item on any new list endpoint: does it project everything the response needs in one query, or does it leave the caller to N+1 it.

**Why it's hard**: N+1 queries don't show up in local testing with a handful of seed rows — they only become visible (and by then, painful) at realistic data volume, which is exactly the kind of bug that's cheap to prevent early and expensive to find later.

## 5. Idempotent background jobs

**Problem**: Invite emails and notification digests are dispatched from a background worker, decoupled from the API request that triggered them (so the request isn't blocked on SendGrid latency). Decoupling introduces at-least-once delivery semantics — a transient failure and retry could otherwise send a duplicate invite or a duplicate digest email.

**Approach**: Each job run is keyed (invite ID, or a `(tenantId, date)` composite for digests) and checks for prior completion before dispatching. Mutating endpoints that trigger a job also accept an `Idempotency-Key` at the API layer ([API Design §1](03-api-design.md#1-conventions)) so a client-side retry of the triggering request doesn't queue a second job either.

**Why it's hard**: "Just retry on failure" is the easy half of the story; the harder half is making the retried operation safe to run twice, which has to be designed into the job itself (a dedup key), not bolted on after the fact.

## 6. Knowing where full-text search stops being enough

**Problem**: SQL Server Full-Text Search ([Technical Decisions §8](05-technical-decisions.md)) is the pragmatic choice for search at this system's scale, but it isn't infinitely scalable — relevance ranking is basic, and query load on the primary database competes with transactional traffic as data grows.

**Approach**: Search is isolated behind a single query-handler interface (`ISearchTasks`), so the implementation — currently a SQL full-text query — can be swapped for a dedicated search service (Azure Cognitive Search, Elasticsearch) without touching any caller. The trigger for making that swap would be either measurable relevance complaints or full-text query latency showing up in Application Insights as a meaningful fraction of database load.

**Why it's hard**: The temptation is to either over-engineer search from day one (standing up a search service and an indexing pipeline before there's any data to justify it) or to hardcode the simple version so deeply that migrating later means a rewrite. The interface boundary is what keeps both extremes avoidable.

## 7. Serving tenant-scoped attachments without a public URL becoming the access control

**Problem**: Task attachments live in Blob Storage, but Blob Storage has no concept of this system's tenants or roles — if a client is simply handed a URL to the blob, that URL *is* the access control, whether that's intended or not. A public container makes every attachment fetchable by anyone who obtains the URL (chat logs, browser history, a forwarded link); a private container with a long-lived SAS baked in at upload time is better, but that SAS is then a standing bearer credential for the file's lifetime, with no clean way to revoke it if it leaks.

**Approach**: `TaskAttachments` never stores a client-usable URL — only an opaque, server-generated `BlobKey` ([Database Design](02-database-design.md#table-notes)). Every download goes through `GET /tasks/{id}/attachments/{id}/download` ([API Design §5](03-api-design.md#5-example-attachment-download)), which re-runs the same tenant/role authorization check every other endpoint in this system runs, and only then mints a SAS URL scoped to that one object and valid for 5 minutes. Full rationale in [Technical Decisions §10](05-technical-decisions.md) and [Security §5](04-security.md#5-input-validation--injection-defense).

**Why it's hard**: Attachments are easy to treat as "just a file" and bolt on outside the system's normal authorization path — but a leaked or logged URL is a realistic exposure vector, and the fix isn't more secrecy around the URL, it's making the URL itself short-lived and worthless without a fresh authorization check behind it. The interesting part is recognizing that a resource served through a *different* system (Blob Storage) still has to honor the *same* tenant boundary as everything served directly by the API — isolation isn't a property of one layer, it has to hold everywhere a tenant's data actually lives.
