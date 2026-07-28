# Security

*As-built. A few items from an earlier draft — a `Viewer` role, rotated refresh tokens, server-side rich-text sanitization, tenant data export/deletion — were never implemented; each is called out below rather than left describing controls that don't exist.*

## 1. Threat Model Summary

DevFlow is a multi-tenant SaaS system, so the top threat is **cross-tenant data exposure** — one organization seeing or modifying another's data, whether through a missed authorization check, a query bug, or a compromised/forged token. Every design decision in this document is ordered by how directly it defends against that.

Secondary threats: account takeover (credential stuffing, token theft) and privilege escalation within a tenant (Member acting as Admin).

## 2. Tenant Isolation

This is the system's core security boundary, and it's enforced twice, deliberately redundant:

1. **Data-access layer (primary)**: every tenant-scoped `DbSet` has an EF Core global query filter (`HasQueryFilter(e => e.TenantId == _currentUserService.TenantId)`), applied automatically to every LINQ query. A developer writing a new query gets tenant scoping for free and would have to explicitly call `.IgnoreQueryFilters()` to bypass it — a call that appears in exactly three places in this codebase (all pre-tenant-context flows: login's membership lookup, invitation-accept, and one team-removal invariant check), making any *new* occurrence a clear red flag in review. See [Architecture §4](01-architecture.md#4-authentication--tenant-isolation) for the request-flow diagram.
2. **API layer (defense in depth)**: the tenant ID used for the query filter comes from the validated JWT's `tenant_id` claim — never from a client-supplied route/query parameter.

Both layers are covered by integration tests that authenticate as Tenant A and assert `404` (not `403` — existence of another tenant's resource is not confirmed) when requesting a resource ID known to belong to Tenant B.

`Users` is the one entity with no query filter at all — it's intentionally global (see [Database Design](02-database-design.md)). Anything that needs "users in my tenant" (team management, task assignment, global search's "People" results) goes through `TenantMembers` instead, which does carry the filter; querying `Users` directly for a tenant-scoped feature would leak every tenant's users and is treated as a bug, not a style preference.

## 3. Authorization

Role hierarchy: `Member < Admin < Owner`, stored per-tenant on `TenantMember.Role` (a user can hold different roles in different tenants). There is no `Viewer` role — an earlier draft of this document described a four-tier hierarchy (`Owner > Admin > Member > Viewer`); only the three above were built. Authorization is policy-based (ASP.NET Core `AuthorizationPolicy`) on the handful of endpoints that need role gating beyond "any authenticated tenant member" — see [API Design §8](03-api-design.md#8-authorization-enforcement) for the full per-endpoint mapping.

Two invariants enforced at the application-service level (not just the DB constraint layer, so a clear domain error is returned instead of a raw SQL failure):
- A tenant can never be left with zero `Owner` members (checked before any role change or removal commits).
- An Admin may not remove an Owner (only an Owner can remove another Owner).

## 4. Authentication

- **Credentials**: ASP.NET Core Identity's `PasswordHasher<T>` (PBKDF2, per-user salt). Plaintext passwords are never logged or persisted.
- **Tokens**: a single short-lived JWT access token (15 min), issued by `/auth/login`/`/auth/register`, held client-side (Zustand store). **There is no refresh token and no `/auth/logout` endpoint** — an earlier draft of this document specified a rotated refresh token in an `HttpOnly, Secure, SameSite=Strict` cookie; it was never built. As shipped, a session simply stops working after 15 minutes and the SPA redirects to `/login` on the resulting `401`. This is a real, known limitation for anything beyond a short demo session — tracked in the root [README](../../README.md#known-limitations), deliberately not built during this review to keep it focused on fixing what exists rather than adding a new auth flow.
- **Same-origin by design**: even without a refresh cookie, the same-origin deployment (Static Web Apps' linked backend, [Architecture §2](01-architecture.md#2-component-diagram)) still matters — it's what makes the access token's `Authorization` header reach the API without any CORS configuration, and is the precondition a future cookie-based refresh flow would need anyway.

## 5. Input Validation & Injection Defense

- Mutating endpoints validate input via a combination of DataAnnotations on the API-layer request DTO (required fields, max length) and FluentValidation at the Application layer (cross-entity/DB-aware checks — e.g. "does this `ProjectId` exist in my tenant") — see `CreateTaskInputValidator` for the pattern. Both run before any domain logic, rejecting malformed input with a `400`.
- All database access goes through EF Core's parameterized LINQ queries; the one exception (`PostgresFullTextSearchService`) uses Npgsql's `EF.Functions.ToTsVector`/`PlainToTsQuery`, still ordinary parameterized LINQ, not string-concatenated SQL.
- **Rich text / stored XSS**: there is no server-side HTML sanitization step — an earlier draft of this document claimed task descriptions were "sanitized server-side against an allowlist." What actually defends against stored XSS here is that the React frontend never renders task/project/user text via `dangerouslySetInnerHTML` anywhere in the codebase; React's default JSX text rendering escapes HTML automatically. This holds today, but unlike the isolation/authorization checks above, it isn't structurally enforced — a future component that *does* use `dangerouslySetInnerHTML` on user-supplied text would reopen this without a validator or filter catching it.
- **File attachments**: the server validates content-type against an allowlist and enforces a 10&nbsp;MB max size independent of the client-declared `Content-Length`. Files are stored under a server-generated **object key** (`TaskAttachments.BlobKey`) — never the client-supplied filename, and never exposed to clients as a directly-usable URL. Retrieval goes through `GET /tasks/{id}/attachments/{id}/download` ([API Design §5](03-api-design.md#5-example-attachment-download)), which re-checks tenant/task authorization on every request and only then mints a short-lived (5 min), read-only SAS URL.

## 6. Secrets & Transport

- Production connection strings and keys (Postgres, Blob Storage, the JWT signing key, Application Insights) live in Azure Key Vault; the API reads them via its App Service system-assigned managed identity at startup — nothing sensitive in `appsettings.json`, GitHub Actions workflow YAML, or source control. Full mapping in [`infra/README.md`](../../infra/README.md#secrets-handling). Local development substitutes .NET User Secrets instead of live Key Vault access — see [Architecture §9](01-architecture.md#9-local-development).
- TLS enforced end-to-end (`UseHttpsRedirection`, with `X-Forwarded-Proto` honored so this works correctly behind Azure App Service's TLS-terminating front-end).
- Data at rest encrypted via Azure Database for PostgreSQL's and Blob Storage's default encryption at rest.

## 7. Rate Limiting & Abuse Prevention

- `POST /auth/login` and `POST /auth/register` are rate-limited to 10 requests/minute per client IP (ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting` middleware, a fixed-window limiter partitioned by `RemoteIpAddress`), returning `429` once exceeded — see `DependencyInjection.AuthRateLimitPolicy`.
- Nothing else is rate-limited — invite creation, in particular, has no per-tenant throttle. Low risk today (invite creation is role-gated to Admin/Owner and never sends real email — there's no delivery channel to abuse), but worth revisiting if email delivery is ever added.

## 8. OWASP Top 10 Mapping

| Risk | Mitigation |
|---|---|
| Broken Access Control | Dual-layer tenant isolation (§2) + server-side RBAC policies (§3) |
| Cryptographic Failures | TLS in transit, database/Blob encryption at rest (§6), hashed passwords (§4) |
| Injection | Parameterized EF Core queries only (§5) |
| Insecure Design | Tenant isolation designed as a structural property (global query filter), not a per-endpoint convention; attachment access designed around short-lived signed URLs rather than public object storage (§5) |
| Security Misconfiguration | Secrets in Key Vault, no default credentials, HTTPS enforced (§6), same-origin deployment avoiding permissive CORS in production |
| Vulnerable/Outdated Components | Not currently automated — no Dependabot/CI dependency scanning configured. A gap, not a documented tradeoff. |
| Auth Failures | Short-lived JWTs, rate-limited auth endpoints (§4, §7). No refresh-token reuse detection, because there is no refresh token (§4) — a materially weaker position than the originally-drafted design here. |
| Software/Data Integrity | CI (`ci.yml`) gates every pull request on build + test before merge to `main`; `deploy.yml` re-runs the same gate before deploying |
| Logging/Monitoring Failures | Structured logs (Serilog) enriched with the request's `TraceIdentifier`, `LogWarning` on failed logins and authorization denials; Application Insights via the Azure Monitor OpenTelemetry distro for request/dependency/exception telemetry when a connection string is configured |
| SSRF | No server-side fetch of arbitrary client-supplied URLs anywhere in the codebase |

## 9. Data Privacy

No tenant data export or tenant deletion feature exists — an earlier draft of this document described both (an `Owner`-initiated export and a soft-delete-then-purge flow). Neither was built. There is currently no way for a tenant to remove their own data from the system short of a direct database operation.
