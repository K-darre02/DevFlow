# API Design

## 1. Conventions

- **Versioning**: all routes namespaced `/api/v1/...`. Breaking changes ship as `/api/v2/...` rather than mutating v1 in place.
- **Origin**: the SPA and API are served from the same origin — Azure Static Web Apps proxies `/api/*` to the API as a linked backend, so the browser never makes a cross-origin call to reach it. This is what makes the auth model in §Auth below actually work; see [Architecture §2](01-architecture.md#2-component-diagram) and [Security §4](04-security.md#4-authentication) for why that matters.
- **Auth**: `Authorization: Bearer <jwt>` on every request except `POST /auth/login`, `POST /auth/register`, `POST /auth/refresh`, and invite-accept. The JWT carries `sub` (user ID), `tenant_id`, and `role` claims. The refresh token travels in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie — safe to rely on here specifically because same-origin (above) means the cookie is always same-site.
- **Tenant scope**: the active tenant is derived entirely from the JWT's `tenant_id` claim — never from a client-supplied header, query param, or route segment. A client cannot ask the API to act on a tenant it isn't currently authenticated into.
- **Resource naming**: plural nouns, nested under their parent where ownership is fixed (`/tenants/current/members`), flat where a resource is queried independently of its parent (`/tasks?projectId=...`).
- **Pagination**: cursor-based (`?cursor=<opaque>&limit=50`) on all list endpoints, response includes `nextCursor: string | null`. Chosen over offset pagination because task/notification lists are written to concurrently — offset pagination skips/duplicates rows under concurrent inserts, cursor pagination doesn't.
- **Error format**: [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `application/problem+json` — `{ type, title, status, detail, errors? }`, with `errors` populated for validation failures (field → message[]).
- **Idempotency**: mutating endpoints that can be safely retried (e.g. invite creation) accept an optional `Idempotency-Key` header; the server deduplicates on that key for 24h.
- **Optimistic concurrency**: endpoints that mutate a versioned resource (`PATCH /tasks/{id}`) require an `If-Match: "<rowVersion>"` header; a stale version returns `409 Conflict` with the current resource state in the body. Rationale in [Engineering Challenges §2](06-engineering-challenges.md).

## 2. Resource Endpoints

### Auth
| Method | Route | Notes |
|---|---|---|
| POST | `/auth/register` | Creates `User` + `Tenant` + `Owner` membership atomically |
| POST | `/auth/login` | Returns access token in the response body + refresh token as a same-origin `HttpOnly` cookie |
| POST | `/auth/refresh` | Rotates refresh token, issues new access token |
| POST | `/auth/logout` | Revokes the presented refresh token, clears the cookie |

### Tenants & Membership
| Method | Route | Notes |
|---|---|---|
| GET | `/tenants/current` | Active tenant details |
| PATCH | `/tenants/current` | Owner-only: rename/settings |
| GET | `/tenants/current/members` | List members + roles |
| PATCH | `/tenants/current/members/{userId}` | Change role; rejected if it would leave zero Owners |
| DELETE | `/tenants/current/members/{userId}` | Remove member, revoke their refresh tokens for this tenant |
| POST | `/tenants/current/invites` | Create pending invite |
| POST | `/invites/{token}/accept` | Public (token-authenticated) — converts invite to active membership |

### Projects & Tasks
| Method | Route | Notes |
|---|---|---|
| GET | `/projects` | List non-archived projects in the active tenant |
| POST | `/projects` | Create project |
| PATCH | `/projects/{id}` | Update, including `isArchived` |
| GET | `/projects/{id}/board` | The project's tasks grouped by `Status` — a computed view, not a separate stored resource ([Database Design](02-database-design.md#table-notes)) |

### Tasks
| Method | Route | Notes |
|---|---|---|
| GET | `/tasks?projectId=&status=&assigneeId=&tag=&q=` | Filtered/searched list, cursor-paginated |
| POST | `/tasks` | Create task |
| GET | `/tasks/{id}` | Full task detail, including comments/attachments/activity |
| PATCH | `/tasks/{id}` | Update fields; status changes go through this endpoint with `If-Match` |
| POST | `/tasks/{id}/comments` | Add comment; server parses `@mentions` and creates notifications |
| POST | `/tasks/{id}/attachments` | Multipart upload → Blob Storage under a generated object key; returns attachment metadata (no public URL) |
| GET | `/tasks/{id}/attachments/{attachmentId}/download` | Authorizes the request, then issues a `302` redirect to a short-lived (5 min) read-only SAS URL — see [Security §5](04-security.md#5-input-validation--injection-defense) and [Engineering Challenges §7](06-engineering-challenges.md) |
| GET | `/tasks/{id}/activity` | Chronological activity log for the task |

### Notifications
| Method | Route | Notes |
|---|---|---|
| GET | `/notifications?unreadOnly=` | Cursor-paginated |
| PATCH | `/notifications/{id}` | Mark read |
| PATCH | `/notifications/read-all` | Bulk mark-read |

### Dashboard
| Method | Route | Notes |
|---|---|---|
| GET | `/projects/{id}/dashboard` | Task counts by status |
| GET | `/projects/{id}/burndown?days=30` | Burndown series |

## 3. Example: Task Creation

```http
POST /api/v1/tasks
Authorization: Bearer eyJhbGciOi...
Content-Type: application/json

{
  "projectId": "3f1a...",
  "title": "Wire up SignalR board group",
  "description": "Broadcast task updates to project-scoped groups only.",
  "assigneeUserId": "9c2b...",
  "dueDate": "2026-08-15",
  "tags": ["backend", "realtime"]
}
```

```http
201 Created
Location: /api/v1/tasks/7e4d...
ETag: "AAAAAAAAB9E="

{
  "id": "7e4d...",
  "projectId": "3f1a...",
  "status": "Backlog",
  "title": "Wire up SignalR board group",
  "assigneeUserId": "9c2b...",
  "dueDate": "2026-08-15",
  "tags": ["backend", "realtime"],
  "createdAt": "2026-07-28T10:15:00Z"
}
```

`status` is the string name of the `TaskStatus` enum (`Backlog | ToDo | InProgress | InReview | Done`), serialized as a string rather than its underlying `int` for readability — no lookup table behind it ([Database Design](02-database-design.md#table-notes)).

## 4. Example: Status Change With Conflict Detection

```http
PATCH /api/v1/tasks/7e4d...
Authorization: Bearer eyJhbGciOi...
If-Match: "AAAAAAAAB9E="
Content-Type: application/json

{ "status": "InProgress" }
```

If another client already moved the same task (stale `If-Match`):

```http
409 Conflict
Content-Type: application/problem+json

{
  "type": "https://devflow.dev/errors/conflict",
  "title": "Task was modified by another user",
  "status": 409,
  "detail": "The task's status has changed since it was last fetched.",
  "current": { "id": "7e4d...", "status": "InReview", "rowVersion": "AAAAAAAAB9F=" }
}
```

The client reconciles by re-fetching and re-applying the drag if still valid — see [Engineering Challenges §2](06-engineering-challenges.md) for the full optimistic-update flow this supports.

## 5. Example: Attachment Download

```http
GET /api/v1/tasks/7e4d.../attachments/a1b2.../download
Authorization: Bearer eyJhbGciOi...
```

```http
302 Found
Location: https://devflowstorage.blob.core.windows.net/attachments/<opaque-key>?sv=...&se=2026-07-28T10:20:00Z&sig=...
Cache-Control: no-store
```

The server validates that the requester's tenant/role can access this task before minting the SAS URL — the `BlobKey` alone (even if somehow guessed) is never sufficient, because Blob Storage isn't reachable without a valid, time-boxed signature the API controls. See [Engineering Challenges §7](06-engineering-challenges.md).

## 6. Real-Time Surface (SignalR)

Not REST, but part of the API contract: clients viewing a board connect to `wss://<same-origin>/hubs/board?projectId={id}` (authenticated via the same JWT, passed as an access token query param per SignalR's connection negotiation; same-origin deployment means no additional CORS configuration is needed for the handshake). The server adds the connection to a SignalR group named `project:{projectId}`. Events pushed to the group:

| Event | Payload | Trigger |
|---|---|---|
| `task.updated` | `{ taskId, changes }` | Any successful task mutation |
| `task.created` | `{ task }` | New task added to the project |
| `task.deleted` | `{ taskId }` | Task removed |

Clients never receive events for boards they aren't currently viewing — group membership is scoped per project, not per tenant, to avoid needless fan-out (see [Engineering Challenges §3](06-engineering-challenges.md)).

## 7. Authorization Enforcement

Every endpoint above declares a minimum role via an ASP.NET Core authorization policy (e.g. `[Authorize(Policy = "MemberOrAbove")]`). `Viewer` role is accepted on all `GET` routes and rejected on all mutating routes at the policy level — this is enforced identically regardless of what the frontend does, per [Security §3](04-security.md#3-authorization).
