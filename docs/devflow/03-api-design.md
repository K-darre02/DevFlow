# API Design

*As-built. Several conventions in an earlier draft of this document — versioning, cursor pagination, an `Idempotency-Key` header, RFC 7807 `type`/`errors` on every response — were simplified or dropped during implementation; corrected below rather than left describing a contract that doesn't exist.*

## 1. Conventions

- **Versioning**: none. Routes are `/api/projects`, `/api/tasks`, etc. — no `/v1/` prefix. Simplified away during implementation; a real `v2` would be introduced only once a breaking change actually needed one.
- **Origin**: the SPA and API are served from the same origin — Azure Static Web Apps proxies `/api/*` and `/hubs/*` to the API as a linked backend, so the browser never makes a cross-origin call to reach either. See [Architecture §2](01-architecture.md#2-component-diagram).
- **Auth**: `Authorization: Bearer <jwt>` on every request except `POST /auth/register`, `POST /auth/login`, and `POST /team/invitations/accept`. The JWT carries `sub` (user ID), `tenant_id`, and `role` claims and expires in 15 minutes. There is no refresh token and no `/auth/logout` — see the revision note in [Architecture §4](01-architecture.md#4-authentication--tenant-isolation). A request with an expired or missing token gets `401`; the SPA responds by clearing local session state and redirecting to `/login`.
- **Tenant scope**: the active tenant is derived entirely from the JWT's `tenant_id` claim — never from a client-supplied header, query param, or route segment.
- **Resource naming**: plural nouns, flat (`/tasks?projectId=...`), except where an action doesn't map to a CRUD verb (`/team/invitations/accept`, `/tasks/{id}/attachments/{attachmentId}/download`).
- **Pagination**: page-based (`?page=&pageSize=`), not cursor-based — used by `GET /activity`, `GET /notifications`, and `GET /search`. Response shape is `{ items, totalCount, page, pageSize }`. `page`/`pageSize` are clamped server-side to `page >= 1` and `pageSize` in `[1, 100]` regardless of what's passed. `GET /projects` and `GET /tasks` are **not** paginated — they return every matching row; both are expected to stay small at this system's scale (a tenant's project count, or a single project's task count), and `GET /tasks?projectId=` specifically needs "all tasks in this project" in one response to render the Kanban board.
- **Error format**: ASP.NET Core `ProblemDetails` (`application/problem+json`) — `{ status, title, detail }`, with a `ValidationProblemDetails` shape (`errors: { field: string[] }`) for `400`s from FluentValidation or model-binding failures.
- **Idempotency**: no `Idempotency-Key` support — a retried mutating request (e.g. a double-submitted invite) is not deduplicated server-side.
- **Optimistic concurrency**: `PATCH /tasks/{id}` requires an `If-Match: "<version>"` header, where `<version>` is `TaskItem.Version` from a prior `GET`. A stale version returns `409 Conflict` with the current resource state under `current` in the body. Rationale in [Engineering Challenges §2](06-engineering-challenges.md).

## 2. Resource Endpoints

### Auth (`AllowAnonymous`)
| Method | Route | Notes |
|---|---|---|
| POST | `/auth/register` | Creates `Tenant` + `User` + `Owner` `TenantMember`, atomically. Returns an access token. |
| POST | `/auth/login` | Returns an access token for the caller's earliest-joined tenant membership (no tenant-picker UI exists — see `AuthController.Login`'s own comment) |

Both are rate-limited per client IP (10 requests/minute) — see [Security §7](04-security.md#7-rate-limiting--abuse-prevention).

### Team (`/team`)
| Method | Route | Authorization | Notes |
|---|---|---|---|
| GET | `/team` | Any tenant member | List members + roles |
| POST | `/team/invitations` | Admin or Owner | Creates a pending invitation; returns the raw token in the response body (no email delivery — see the revision note in [Architecture §6](01-architecture.md#6-background-processing)) |
| POST | `/team/invitations/accept` | Anonymous, token-authenticated | Converts a pending invitation into an active membership + returns a scoped access token |
| PATCH | `/team/{memberId}/role` | Owner only | Change a member's role; rejected if it would leave zero Owners |
| DELETE | `/team/{memberId}` | Admin or Owner | Remove a member (an Admin may not remove an Owner — enforced at the service level via `ForbiddenException`, not a static policy, since it depends on the target member's role) |

### Projects (`/projects`)
| Method | Route | Notes |
|---|---|---|
| GET | `/projects?includeArchived=` | List projects in the active tenant |
| GET | `/projects/{id}` | Single project; cross-tenant IDs return `404`, not `403` |
| POST | `/projects` | Create project |
| PATCH | `/projects/{id}` | Update `name`/`isArchived` |
| DELETE | `/projects/{id}` | Cascades to the project's `TaskItems` at the database level |

### Tasks (`/tasks`)
| Method | Route | Notes |
|---|---|---|
| GET | `/tasks?projectId=&status=&assigneeUserId=` | Filtered list — this is what the frontend groups by `Status` client-side to render a project's board |
| POST | `/tasks` | Create task |
| GET | `/tasks/{id}` | Single task |
| PATCH | `/tasks/{id}` | Requires `If-Match` (§1) — status changes go through this same endpoint |
| DELETE | `/tasks/{id}` | |
| GET/POST | `/tasks/{taskId}/attachments` | List / upload (multipart) attachments — see [§5](#5-example-attachment-download) |
| GET | `/tasks/{taskId}/attachments/{attachmentId}/download` | `302` redirect to a short-lived SAS URL |
| DELETE | `/tasks/{taskId}/attachments/{attachmentId}` | |

### Activity, Notifications, Dashboard, Search
| Method | Route | Notes |
|---|---|---|
| GET | `/activity?page=&pageSize=&entityType=&userId=&activityType=` | Paginated, newest first |
| GET | `/notifications?page=&pageSize=&unreadOnly=` | Paginated; scoped to the caller (`TenantId` **and** `UserId`) |
| GET | `/notifications/unread-count` | |
| PATCH | `/notifications/{id}/read` / `/notifications/read-all` | |
| GET | `/dashboard` | Tenant-wide counts, tasks-by-status/priority, recent activity, overdue tasks — not per-project |
| GET | `/search?q=&page=&pageSize=` | Grouped results across Projects/Tasks/People — see [§6](#6-example-global-search) |

## 3. Example: Task Creation

```http
POST /api/tasks
Authorization: Bearer eyJhbGciOi...
Content-Type: application/json

{
  "projectId": "3f1a...",
  "title": "Wire up SignalR reconnection handling",
  "description": "Auto-reconnect after a temporary connection loss.",
  "assigneeUserId": "9c2b...",
  "priority": "High",
  "dueDate": "2026-08-15"
}
```

```http
201 Created
Location: /api/tasks/7e4d...

{
  "id": "7e4d...",
  "projectId": "3f1a...",
  "status": "Backlog",
  "title": "Wire up SignalR reconnection handling",
  "priority": "High",
  "assigneeUserId": "9c2b...",
  "dueDate": "2026-08-15",
  "version": 1,
  "createdAt": "2026-07-28T10:15:00Z"
}
```

`status`/`priority` are the string names of their enums (`Backlog | ToDo | InProgress | InReview | Done`, `Low | Medium | High | Urgent`), not their underlying `int` — no lookup table behind either ([Database Design](02-database-design.md#table-notes)).

## 4. Example: Status Change With Conflict Detection

```http
PATCH /api/tasks/7e4d...
Authorization: Bearer eyJhbGciOi...
If-Match: "1"
Content-Type: application/json

{ "status": "InProgress" }
```

If another client already moved the same task (stale `If-Match`):

```http
409 Conflict
Content-Type: application/json

{
  "title": "Task was modified by another request",
  "status": 409,
  "detail": "The task's data has changed since it was last fetched.",
  "current": { "id": "7e4d...", "status": "InReview", "version": 2 }
}
```

The client rolls the optimistic drag back, shows the real current state, and lets the user decide whether to reapply their change — see [Engineering Challenges §2](06-engineering-challenges.md) for the full flow this supports.

## 5. Example: Attachment Download

```http
GET /api/tasks/7e4d.../attachments/a1b2.../download
Authorization: Bearer eyJhbGciOi...
```

```http
302 Found
Location: https://<storage-account>.blob.core.windows.net/task-attachments/<opaque-key>?sv=...&se=2026-07-28T10:20:00Z&sig=...
```

The server validates tenant/task access before minting the SAS URL — the `BlobKey` alone (even if somehow guessed) is never sufficient, because Blob Storage isn't reachable without a valid, time-boxed signature the API controls. Locally (no Azure Storage account), the redirect target is `GET /api/blob-downloads?...&sig=...` instead — an intentionally anonymous endpoint whose only authorization is an HMAC signature in the URL itself (`SignedBlobUrl`), since a browser following a redirect can't attach an `Authorization` header. See [Engineering Challenges §7](06-engineering-challenges.md).

## 6. Example: Global Search

```http
GET /api/search?q=voyager&pageSize=5
Authorization: Bearer eyJhbGciOi...
```

```http
200 OK

{
  "projects": { "items": [{ "id": "...", "name": "Voyager Launch", "isArchived": false, "rank": 3 }], "totalCount": 1 },
  "tasks": { "items": [{ "id": "...", "title": "Voyager checklist review", "projectId": "...", "projectName": "Voyager Launch", "status": "ToDo", "rank": 2 }], "totalCount": 1 },
  "people": { "items": [], "totalCount": 0 }
}
```

Each group is independently ranked and paginated. In production, ranking is PostgreSQL's `ts_rank` over `to_tsvector`/`plainto_tsquery`; against Sqlite (this project's test suite) it falls back to a prefix/substring heuristic — see [Technical Decisions §8](05-technical-decisions.md) and [Engineering Challenges §6](06-engineering-challenges.md). "People" search goes through `TenantMembers`, never the global `Users` table directly, so it can never return a user from another tenant.

## 7. Real-Time Surface (SignalR)

Not REST, but part of the API contract: `wss://<same-origin>/hubs/tasks`, authenticated via the same JWT (SignalR's JS client can't set an `Authorization` header on the WebSocket handshake, so the token travels as an `access_token` query string parameter instead — bridged onto the same JWT bearer handler, scoped to `/hubs` paths only). On connect, the server adds the connection to two groups: `tenant:{tenantId}` (all task/project events for the caller's tenant) and `user:{userId}` (notification events for the caller specifically).

| Event | Group | Trigger |
|---|---|---|
| `taskCreated` / `taskUpdated` / `taskMoved` / `taskDeleted` | `tenant:{tenantId}` | Task mutation, broadcast only after the write's transaction commits |
| `projectCreated` / `projectArchived` | `tenant:{tenantId}` | Project mutation |
| `notificationCreated` | `user:{userId}` | A notification was created for that specific user |

Fan-out is per-tenant, not per-project-board — every connected client for a tenant receives every task event for that tenant, filtered client-side to whatever board is currently open. The cost this accepts, and the trigger for narrowing it to per-project groups, is discussed in [Engineering Challenges §3](06-engineering-challenges.md).

## 8. Authorization Enforcement

Most endpoints require only a valid JWT (`[Authorize]`) — any authenticated member of the active tenant may read/write, since role hierarchy in this system is `Member < Admin < Owner` with no `Viewer` tier (an earlier draft of this document described a `Viewer` role; it was never built — see [Security §3](04-security.md#3-authorization)). The few endpoints that do gate by role use an ASP.NET Core authorization policy (`[Authorize(Policy = "OwnerOnly")]` / `"AdminOrOwner"`) — listed per-endpoint in [§2](#2-resource-endpoints) above. Rules that depend on the *specific* member being acted on (an Admin can't remove an Owner; a tenant can't be left with zero Owners) aren't expressible as a static policy and are enforced in `TeamService` instead, via `ForbiddenException`.
