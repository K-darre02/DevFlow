# DevFlow — Engineering Design Documentation

DevFlow is a multi-tenant SaaS project management platform (a Kanban-style Jira/Linear hybrid) — projects, boards, tasks, team management, real-time collaboration, notifications, file attachments, a reporting dashboard, and global search, behind organization-scoped tenancy and role-based access control. This is a solo-built portfolio project; the documentation here focuses on the engineering decisions and problems worth discussing in a technical interview, not product management process.

These documents were largely written up front, before implementation — several decisions changed along the way (a fixed 15-minute session instead of refresh tokens, self-hosted SignalR instead of Azure SignalR Service, no `/v1` API versioning, comments/tags never built). Each document below now says so explicitly wherever it applies, via inline revision notes, rather than silently describing a system that doesn't quite match what's in `src/`.

## Documents

| # | Document | Purpose |
|---|----------|---------|
| 1 | [Architecture](01-architecture.md) | System design, component diagram, tech stack rationale, request/auth flow |
| 2 | [Database Design](02-database-design.md) | Entity model, multi-tenancy strategy, ER diagram, indexing |
| 3 | [API Design](03-api-design.md) | REST conventions, resource endpoints, real-time (SignalR) surface, example payloads |
| 4 | [Security](04-security.md) | Threat model, tenant isolation, authN/authZ, OWASP Top 10 mapping |
| 5 | [Technical Decisions](05-technical-decisions.md) | ADR-style records of key choices and alternatives considered |
| 6 | [Engineering Challenges](06-engineering-challenges.md) | Concrete hard problems and how the design solves them |
| 7 | [Quality Attributes](07-quality-attributes.md) | Performance, scalability, availability, observability, maintainability |

## Stack at a Glance

- **Backend**: ASP.NET Core 8 Web API, C#, Clean Architecture with pragmatic Application Services, EF Core (MediatR used narrowly for post-commit domain-event fan-out, not as the primary request pattern)
- **Frontend**: React 19 + TypeScript SPA, TanStack Query, hand-rolled UI primitives (no component library)
- **Multi-tenancy**: Shared database, shared schema, row-level isolation via `TenantId` (EF Core global query filters)
- **Real-time**: Self-hosted ASP.NET Core SignalR (not Azure SignalR Service — see [Technical Decisions §5](05-technical-decisions.md)), per-tenant connection groups
- **Hosting**: Microsoft Azure — Static Web Apps and App Service deployed same-origin via a linked backend (App Service, Azure Database for PostgreSQL, Blob Storage, Key Vault, Application Insights)
- **Local development**: Docker Compose (PostgreSQL container, Azurite, local SignalR) — no live Azure resources needed for day-to-day work; PostgreSQL was chosen over SQL Server specifically for native Apple Silicon (arm64) support while remaining production-ready on Azure — [Technical Decisions §11](05-technical-decisions.md)

## What This Project Demonstrates

- A multi-tenant data model with isolation enforced structurally, not by convention ([Security §2](04-security.md#2-tenant-isolation))
- A layered, testable backend architecture with a clear dependency rule ([Architecture §3](01-architecture.md#3-backend-internal-layering-clean-architecture))
- A REST API with deliberate conventions for pagination, errors, and optimistic concurrency ([API Design](03-api-design.md))
- Real problems that don't have a textbook answer — conflicting concurrent edits, real-time fan-out cost, N+1 avoidance ([Engineering Challenges](06-engineering-challenges.md))
- Decisions made with named alternatives and stated tradeoffs, not just a single "right answer" presented in isolation ([Technical Decisions](05-technical-decisions.md)) — including honest revision notes where the as-built system diverged from the original plan

## Cross-Referencing

These documents reference each other rather than repeating themselves — e.g. the concurrency token in [Database Design](02-database-design.md) is explained by the conflict-handling flow in [Engineering Challenges §2](06-engineering-challenges.md), which is exposed via the `If-Match` contract in [API Design §4](03-api-design.md#4-example-status-change-with-conflict-detection). Start with Architecture for the system overview, then follow links into whichever area is most relevant.
