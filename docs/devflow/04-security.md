# Security

## 1. Threat Model Summary

DevFlow is a multi-tenant SaaS system, so the top threat is **cross-tenant data exposure** — one organization seeing or modifying another's data, whether through a missed authorization check, a query bug, or a compromised/forged token. Every design decision in this document is ordered by how directly it defends against that.

Secondary threats: account takeover (credential stuffing, token theft), privilege escalation within a tenant (Member acting as Admin), and injection/XSS via user-generated content (task descriptions, comments — rendered back to other users, so stored-XSS is a real vector).

## 2. Tenant Isolation

This is the system's core security boundary, and it's enforced twice, deliberately redundant:

1. **Data-access layer (primary)**: every tenant-scoped `DbSet` has an EF Core global query filter (`HasQueryFilter(e => e.TenantId == _currentTenant.Id)`), applied automatically to every LINQ query. A developer writing a new query gets tenant scoping for free and would have to explicitly call `.IgnoreQueryFilters()` to bypass it — a call that doesn't appear anywhere in normal application code, making it a clear red flag in review. See [Architecture §4](01-architecture.md#4-authentication--tenant-isolation) for the request-flow diagram.
2. **API layer (defense in depth)**: the tenant ID used for the query filter comes from the validated JWT's `tenant_id` claim, set once per request by tenant-resolution middleware — never from a client-supplied route/query parameter. This closes the class of bug where a client passes `?tenantId=<someone-else's-id>` and an unguarded endpoint honors it.

Both layers are covered by integration tests that authenticate as Tenant A and assert `404` (not `403` — existence of another tenant's resource is not confirmed) when requesting a resource ID known to belong to Tenant B.

## 3. Authorization

Role hierarchy: `Owner > Admin > Member > Viewer`, stored per-tenant in `TenantMemberships.Role` (a user can hold different roles in different tenants). Authorization is policy-based (ASP.NET Core `AuthorizationPolicy`), declared per-endpoint, and evaluated server-side on every request — see [API Design §7](03-api-design.md#7-authorization-enforcement) for how policies map onto routes.

Two invariants enforced at the application-service level (not just the DB constraint layer, so a clear domain error is returned instead of a raw SQL failure):
- A tenant can never be left with zero `Owner` members (checked before any role change or removal commits).
- `Viewer` role is rejected on every mutating operation, independent of what the SPA's UI currently shows — the frontend hiding an edit button is a UX affordance, not a security control.

## 4. Authentication

- **Credentials**: ASP.NET Core Identity's default password hasher (PBKDF2, per-user salt, configurable iteration count). Plaintext passwords are never logged, never persisted, and excluded from Application Insights request-body capture.
- **Tokens**: short-lived JWT access token (15 min) + longer-lived opaque refresh token (7 days), rotated on every use (refresh token reuse after rotation is treated as a signal of token theft and revokes the whole token family).
- **Storage**: access token held in memory in the SPA (not `localStorage`, to reduce XSS exfiltration risk); refresh token in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie, unreadable from JavaScript.
- **Same-origin by design**: `SameSite=Strict` only does its job if the cookie is genuinely same-site — a naive deployment (SPA on Azure Static Web Apps' default domain, API on Azure App Service's default domain) puts them on *unrelated* domains, which would silently break the cookie and the refresh flow with it. DevFlow avoids this by having Static Web Apps proxy `/api/*` to the API as a **linked backend**, so the browser only ever talks to one origin — the API is reached through the same domain the SPA is served from, not a separate one (see [Architecture §2](01-architecture.md#2-component-diagram)). This also means CSRF exposure on the cookie-authenticated `/auth/refresh` and `/auth/logout` endpoints is limited to genuinely same-site requests, which `SameSite=Strict` already excludes.
- **Revocation**: refresh tokens are stored hashed (`TokenHash`, never the raw token) so a database read doesn't leak usable tokens; removing a member revokes their refresh tokens for that tenant immediately.

## 5. Input Validation & Injection Defense

- All command/query inputs validated via FluentValidation at the Application layer boundary, before any domain logic runs — rejects malformed input with a structured `400` before it reaches the database.
- All database access goes through EF Core's parameterized queries; no raw SQL string concatenation anywhere in the codebase (enforced by code review + a static-analysis lint rule).
- User-generated rich text (task descriptions, comments) is sanitized server-side against an allowlist before storage, and the React frontend never uses `dangerouslySetInnerHTML` on it — defeating stored XSS at both ends rather than relying on either alone.
- **File attachments**: the server validates content-type against an allowlist and enforces a max size independent of the client-declared `Content-Length`. Files are stored in Blob Storage under a server-generated **object key** (`TaskAttachments.BlobKey`) — never the client-supplied filename, and never exposed to clients as a directly-usable URL. Retrieval goes through `GET /tasks/{id}/attachments/{id}/download` ([API Design §5](03-api-design.md#5-example-attachment-download)), which re-checks tenant/role authorization on every request and only then mints a short-lived (5 min), read-only SAS URL. This closes the gap a plain public-URL design would have: possession of a leaked or guessed URL alone is never sufficient to read another tenant's file, because the object key isn't reachable without a signature the API controls per-request. Full rationale in [Engineering Challenges §7](06-engineering-challenges.md).

## 6. Secrets & Transport

- All connection strings, API keys (SendGrid, SignalR, Blob Storage) live in Azure Key Vault; App Service reads them via managed identity at startup — nothing sensitive in `appsettings.json`, environment variables checked into CI config, or source control. (Local development substitutes .NET User Secrets and emulators instead of live Key Vault access — see [Architecture §9](01-architecture.md#9-local-development).)
- TLS enforced end-to-end; HTTP requests redirected to HTTPS; HSTS enabled.
- Data at rest encrypted via Azure SQL Transparent Data Encryption and Blob Storage's default encryption-at-rest.

## 7. Rate Limiting & Abuse Prevention

- `POST /auth/login` and `POST /auth/register` are rate-limited per IP and per email (ASP.NET Core's built-in rate-limiting middleware) to blunt credential stuffing and account-enumeration attempts.
- Invite creation is rate-limited per tenant to prevent invite-spam being used as an email-bombing vector against arbitrary addresses.

## 8. OWASP Top 10 Mapping

| Risk | Mitigation |
|---|---|
| Broken Access Control | Dual-layer tenant isolation (§2) + server-side RBAC policies (§3) |
| Cryptographic Failures | TLS in transit, TDE/Blob encryption at rest (§6), hashed passwords and refresh tokens (§4) |
| Injection | Parameterized EF Core queries only, FluentValidation at the boundary (§5) |
| Insecure Design | Tenant isolation designed as a structural property (global query filter), not a per-endpoint convention; attachment access designed around short-lived signed URLs rather than public object storage (§5) |
| Security Misconfiguration | Secrets in Key Vault, no default credentials, HTTPS/HSTS enforced (§6), same-origin deployment avoiding permissive CORS (§4) |
| Vulnerable/Outdated Components | Dependabot/CI dependency scanning on the .NET and npm package graphs |
| Auth Failures | Short-lived JWTs, rotated refresh tokens with reuse detection, rate-limited auth endpoints (§4, §7) |
| Software/Data Integrity | Signed CI/CD pipeline (GitHub Actions with pinned action versions), no unreviewed deploys to `main` |
| Logging/Monitoring Failures | Structured logs with correlation IDs, Application Insights alerting on anomalous auth failure rates |
| SSRF | Attachment/URL-accepting inputs validated against an allowlist; no server-side fetch of arbitrary client-supplied URLs |

## 9. Data Privacy

- A tenant `Owner` can request export of all tenant data in a machine-readable format.
- A tenant `Owner` can request tenant deletion — soft-deleted with a grace period (recoverable), then hard-purged, including cascading deletes across every tenant-scoped table in [Database Design](02-database-design.md).
