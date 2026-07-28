# DevFlow — Engineering Design Documentation

DevFlow is a multi-tenant SaaS project management platform (a Kanban-style Jira/Linear hybrid) — projects, boards, tasks, comments, and real-time collaboration behind organization-scoped tenancy and role-based access control. This is a solo-built portfolio project; the documentation here focuses on the engineering decisions and problems worth discussing in a technical interview, not product management process.

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
- **Frontend**: React 18 + TypeScript SPA, TanStack Query
- **Multi-tenancy**: Shared database, shared schema, row-level isolation via `TenantId` (EF Core global query filters)
- **Real-time**: Azure SignalR Service, per-project connection groups
- **Hosting**: Microsoft Azure — Static Web Apps and App Service deployed same-origin via a linked backend (App Service, Azure SQL, Blob Storage, Key Vault, Application Insights)
- **Local development**: Docker Compose (SQL Server container, Azurite, local SignalR) — no live Azure resources needed for day-to-day work

## What This Project Demonstrates

- A multi-tenant data model with isolation enforced structurally, not by convention ([Security §2](04-security.md#2-tenant-isolation))
- A layered, testable backend architecture with a clear dependency rule ([Architecture §3](01-architecture.md#3-backend-internal-layering-clean-architecture))
- A versioned REST API with deliberate conventions for pagination, errors, and optimistic concurrency ([API Design](03-api-design.md))
- Real problems that don't have a textbook answer — conflicting concurrent edits, real-time fan-out cost, N+1 avoidance, idempotent background work ([Engineering Challenges](06-engineering-challenges.md))
- Decisions made with named alternatives and stated tradeoffs, not just a single "right answer" presented in isolation ([Technical Decisions](05-technical-decisions.md))

## Cross-Referencing

These documents reference each other rather than repeating themselves — e.g. the concurrency token in [Database Design](02-database-design.md) is explained by the conflict-handling flow in [Engineering Challenges §2](06-engineering-challenges.md), which is exposed via the `If-Match` contract in [API Design §4](03-api-design.md#4-example-status-change-with-conflict-detection). Start with Architecture for the system overview, then follow links into whichever area is most relevant.
