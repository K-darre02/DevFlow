# Engineering Challenges

Concrete hard problems in this system and how the design addresses them — the parts of DevFlow most worth walking through in a technical interview.

## 1. Enforcing tenant isolation by construction, not by convention

**Problem**: In a shared-schema multi-tenant database, the single most damaging bug class is a query that forgets to filter by tenant — it doesn't crash, it just silently returns or mutates another organization's data.

**Approach**: Isolation is pushed down to the data-access layer as a structural property rather than left as a per-query discipline. EF Core's global query filters apply `TenantId` scoping to every LINQ query against a tenant-scoped `DbSet` automatically, and the current tenant is derived once per request from the validated JWT's `tenant_id` claim. Bypassing the filter requires an explicit `IgnoreQueryFilters()` call, which appears in exactly three places in this codebase — all pre-tenant-context flows (login's membership lookup, invitation-accept, one team-removal invariant check) — so any *new* occurrence is a clear signal in review rather than something that can happen by omission. Verified with integration tests that authenticate as one tenant and assert a `404` when requesting a resource ID known to belong to another. Full detail in [Security §2](04-security.md#2-tenant-isolation).

**Why it's hard**: The failure mode is silent — a missing filter doesn't throw, it just returns the wrong (or too much) data — so the mitigation has to be structural (impossible to omit by accident) rather than something caught by a test someone remembered to write.

## 2. Optimistic drag-and-drop with concurrent-edit conflicts

**Problem**: Dragging a task card to a new status column needs to feel instant — waiting on a network round-trip before the card moves reads as laggy. But if two users move the same card at nearly the same time, a naive "last write wins" silently discards one user's change with no indication anything went wrong.

**Approach**: The frontend applies the status change optimistically (TanStack Query's optimistic update pattern) the instant the drag completes, then confirms against the server. `TaskItems` carries a `Version` concurrency token ([Database Design](02-database-design.md)) — an application-managed incrementing counter, not a database-generated `rowversion`, so it works identically across every EF Core provider this project targets, including Sqlite in tests. The update request sends it as `If-Match`; if the version is stale — someone else moved the card first — the API returns `409 Conflict` with the task's current state instead of blindly overwriting ([API Design §4](03-api-design.md#4-example-status-change-with-conflict-detection)). The client rolls the optimistic move back, shows the actual current state, and lets the user decide whether to reapply their change.

**Why it's hard**: Getting instant-feeling UI *and* correctness under concurrent writes are in tension — most naive optimistic-UI implementations either skip conflict detection entirely (silent data loss) or fall back to pessimistic locking (kills the responsiveness that was the point). The concurrency token makes the conflict visible without blocking the common (uncontended) case at all.

## 3. Real-time fan-out scope: per-tenant today, not per-project

**Problem**: Pushing every task mutation to every connected client in a tenant is the simplest implementation of "real-time board updates" — but it doesn't scale indefinitely: a tenant with 50 active projects and 30 users has every client receiving updates for boards they aren't even looking at.

**Approach, as built**: SignalR connections join a group scoped to the tenant (`tenant:{tenantId}`), not the specific project board being viewed. A task mutation broadcasts to every connection in that group; the frontend filters to whatever board is currently rendered. This was originally designed as per-project groups (`project:{projectId}`) specifically to avoid the over-broadcast problem below — that narrower scoping was never implemented, and per-tenant is what actually ships.

**Why it's hard, and the real state of it**: the easy version (broadcast to the whole tenant, let clients filter) pushes the cost onto every connected client and grows with total tenant activity rather than with what any individual user is looking at — the wrong axis to scale on for a system meant to support many concurrent projects per tenant. That's a real, live tradeoff in this codebase today, not a hypothetical one avoided by design — narrowing SignalR groups to per-project (clients `Groups.AddToGroupAsync`ing into `project:{projectId}` as they navigate boards, leaving as they navigate away) is the concrete next step, deferred here rather than implemented, to keep this review focused on fixing existing behavior over building new group-management logic.

## 4. Avoiding N+1 queries on list endpoints

**Problem**: Rendering a board means fetching every task in a project along with its assignee — a naive implementation (fetch tasks, then loop and fetch each task's assignee) issues one query per task, degrading linearly as board size grows.

**Approach**: `GET /tasks?projectId=` ([API Design §2](03-api-design.md#2-resource-endpoints)) is backed by a single query using EF Core's `.Include()` to eager-load the assignee in one round trip. Search similarly avoids N+1 by projecting everything a result needs (including, for tasks, the parent project's name) directly in the query rather than resolving it per-row afterward.

**Why it's hard**: N+1 queries don't show up in local testing with a handful of seed rows — they only become visible (and by then, painful) at realistic data volume, which is exactly the kind of bug that's cheap to prevent early and expensive to find later.

## 5. What was cut: idempotent background jobs

An earlier design for this system included a background worker for invite emails and notification digests, with each job keyed for idempotency (an `Idempotency-Key` at the API layer too, so a retried triggering request wouldn't queue a second job). None of it was built — there is no background worker, no email delivery, and no `Idempotency-Key` support anywhere in the API. Invitations are created with a raw token returned directly in the response body, shared manually by the inviter; notifications are in-app only. This is a real, uncontroversial scope cut for a solo build, not a subtle design problem worth walking through — recorded here so it isn't mistaken for an oversight elsewhere in this document, which otherwise describes things that *are* actually built.

## 6. Knowing where full-text search stops being enough

**Problem**: PostgreSQL's built-in full-text search ([Technical Decisions §8](05-technical-decisions.md)) is the pragmatic choice for search at this system's scale, but it isn't infinitely scalable — relevance ranking is basic, and query load on the primary database competes with transactional traffic as data grows.

**Approach**: Search is isolated behind a single interface (`ISearchService`), so the implementation — currently ad hoc `tsvector`/`ts_rank` queries with no dedicated index — can be swapped for a dedicated search service (Azure Cognitive Search, Elasticsearch), or upgraded to a persisted/GIN-indexed `tsvector` column, without any caller change. The trigger for either would be measurable relevance complaints or full-text query latency showing up in Application Insights as a meaningful fraction of database load.

**Why it's hard**: The temptation is to either over-engineer search from day one (standing up a search service and an indexing pipeline before there's any data to justify it) or to hardcode the simple version so deeply that migrating later means a rewrite. The interface boundary is what keeps both extremes avoidable — and is also what makes this system testable at all without a real PostgreSQL instance: `LikeSearchService`, a portable `LIKE`-based implementation of the same interface, is what this project's Sqlite-backed test suite actually runs against, since Sqlite has no translation path for Postgres' full-text functions.

## 7. Serving tenant-scoped attachments without a public URL becoming the access control

**Problem**: Task attachments live in Blob Storage, but Blob Storage has no concept of this system's tenants or roles — if a client is simply handed a URL to the blob, that URL *is* the access control, whether that's intended or not. A public container makes every attachment fetchable by anyone who obtains the URL; a private container with a long-lived SAS baked in at upload time is better, but that SAS is then a standing bearer credential for the file's lifetime, with no clean way to revoke it if it leaks.

**Approach**: `TaskAttachments` never stores a client-usable URL — only an opaque, server-generated `BlobKey` ([Database Design](02-database-design.md#table-notes)). Every download goes through `GET /tasks/{id}/attachments/{id}/download` ([API Design §5](03-api-design.md#5-example-attachment-download)), which re-runs the same tenant/task authorization check every other endpoint in this system runs, and only then mints a SAS URL scoped to that one object and valid for 5 minutes. Locally, where there's no real Azure Storage account, the same shape is reproduced with an HMAC-signed URL (`SignedBlobUrl`) pointing at an intentionally anonymous local endpoint — anonymous because a browser following a redirect can't attach an `Authorization` header, so the signature itself has to be the credential.

**Why it's hard**: Attachments are easy to treat as "just a file" and bolt on outside the system's normal authorization path — but a leaked or logged URL is a realistic exposure vector, and the fix isn't more secrecy around the URL, it's making the URL itself short-lived and worthless without a fresh authorization check behind it. The interesting part is recognizing that a resource served through a *different* system (Blob Storage) still has to honor the *same* tenant boundary as everything served directly by the API — isolation isn't a property of one layer, it has to hold everywhere a tenant's data actually lives.
