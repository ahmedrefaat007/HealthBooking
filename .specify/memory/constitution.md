<!--
SYNC IMPACT REPORT
==================
Version change:      0.0.0 (uninitialized template) → 1.0.0
Modified principles: N/A — initial population from template
Added sections:
  - Project Identity
  - I. Clean Architecture & Separation of Concerns
  - II. Microservices Boundaries & Database-per-Service
  - III. Asynchronous-First Writes & Eventual Consistency
  - IV. Test-First & Coverage Gates (NON-NEGOTIABLE)
  - V. Resilience & Observability by Default
  - VI. Security & Compliance Baseline
  - VII. Delivery Cadence & Definition of Done
  - Technology Stack Conventions
  - Governance
Removed sections:    None
Templates requiring updates:
  ✅ .specify/templates/plan-template.md — Constitution Check gates align with principles I–VII
  ✅ .specify/templates/spec-template.md — no structural changes needed; principle references intact
  ✅ .specify/templates/tasks-template.md — Phase labels and observability/testing task types align
  ✅ .specify/templates/constitution-template.md — source only; no change required
Deferred TODOs:      None
-->

# Distributed Healthcare Appointment System — Constitution

## Project Identity

**Project Name**: Distributed Healthcare Appointment System
**Short Name**: HealthBooking
**Purpose**: Provide a training-grade, production-patterned distributed system for senior .NET engineers
covering patient management, provider scheduling, real-time notifications, and cross-service coordination.
**Timeline**: 6-week development sprint (≈ 2 weekly milestones per sprint cycle)
**Team Context**: Senior-level training cohort; all contributors are expected to apply patterns without
hand-holding. Architectural shortcuts MUST be explicitly justified in PR descriptions.

---

## Core Principles

### I. Clean Architecture & Separation of Concerns

Every microservice MUST follow a strict four-layer layout:

```
<Service>/
├── Domain/           # Entities, Value Objects, Domain Events, Aggregates — zero external deps
├── Application/      # Use Cases, CQRS Commands/Queries (MediatR), DTOs, Interfaces
├── Infrastructure/   # EF Core DbContext, Repositories, External Clients, Message Publishers
└── API/              # Controllers / gRPC Endpoints, Middleware, DI Wiring
```

**Rules (all NON-NEGOTIABLE)**:
- The Domain layer MUST NOT reference any NuGet package beyond `System.*` and domain-internal abstractions.
- The Application layer MUST NOT reference Infrastructure directly; dependencies flow inward only.
- CQRS via MediatR is mandatory for all write (Command) and read (Query) operations in Application.
- EF Core Interceptors MUST be used for automated audit fields (`CreatedAt`, `CreatedBy`,
  `ModifiedAt`, `ModifiedBy`) — manual property assignment in handlers is a defect.
- No business logic is permitted in Controllers, Repositories, or Infrastructure adapters.

**Rationale**: Enforces testability, enables independent evolution of persistence/transport layers, and
produces a codebase that mirrors real-world enterprise .NET architecture.

---

### II. Microservices Boundaries & Database-per-Service

The system is composed of exactly four bounded contexts, each as an independent deployable:

| Service | Responsibility | Port (local) |
|---|---|---|
| **PatientService** | Patient registration, profile, demographics | 5001 |
| **ProviderService** | Doctor profiles, specializations, availability slots | 5002 |
| **AppointmentService** | Booking lifecycle, scheduling, conflict resolution | 5003 |
| **NotificationService** | Email/SMS/push delivery triggered by domain events | 5004 |
| **ApiGateway (YARP)** | Request routing, auth delegation, rate limiting | 5000 |

**Rules**:
- Each service owns exactly one SQL Server database; cross-database joins are FORBIDDEN.
- Inter-service reads MUST go through the API Gateway or a dedicated gRPC contract — never via
  shared database views or direct connection strings.
- Synchronous communication (gRPC or REST) is reserved for reads and query-style operations.
- Asynchronous communication (RabbitMQ or Azure Service Bus) MUST be used for all state-changing
  cross-service operations (domain events, saga steps, notifications).
- The Outbox Pattern MUST be implemented for every domain event publication to guarantee
  at-least-once delivery and prevent dual-write inconsistencies.
- Saga coordination awareness: long-running workflows (e.g., appointment booking involving provider
  availability + patient confirmation + notification) MUST be documented as sagas even if implemented
  as choreography-based event chains.

**Rationale**: Preserves bounded-context autonomy, eliminates hidden coupling, and builds the skills
required for genuine microservices production operations.

---

### III. Asynchronous-First Writes & Eventual Consistency

**Rules**:
- Any operation that modifies state across two or more services MUST be modeled as an event, not a
  synchronous RPC call.
- Message contracts (events and commands on the bus) MUST be versioned from day one
  (`V1_AppointmentBookedEvent`, etc.) and MUST be backward compatible for at least one major version.
- Consumers MUST be idempotent — duplicate message delivery MUST NOT cause side effects.
- Redis distributed cache MUST be invalidated or updated on write events, not lazily on next read,
  to avoid serving stale data beyond the configured TTL.
- Circuit breakers (Polly) MUST wrap every outbound HTTP/gRPC call; retry policies MUST use
  exponential back-off with jitter.

**Rationale**: Real distributed systems fail partially; eventual consistency and resilience patterns
are not optional add-ons but fundamental correctness requirements.

---

### IV. Test-First & Coverage Gates (NON-NEGOTIABLE)

**Unit Tests**:
- Written with xUnit; test data seeded exclusively via Bogus (no hand-crafted magic strings).
- Minimum 80% line coverage per service Domain and Application layers, enforced in CI.
- Every MediatR handler MUST have at least one success test and one failure/validation test.

**Integration Tests**:
- Written with xUnit + TestContainers; each service spins its own SQL Server and RabbitMQ containers.
- MUST cover: repository persistence round-trips, EF Core interceptor audit fields, outbox publishing,
  and API endpoint contract validation.
- Integration tests MUST NOT share state between test classes (use `IClassFixture` per class).

**General**:
- No PR merges into `main` or `develop` without passing CI (build + lint + unit + integration).
- Test project naming convention: `<Service>.UnitTests`, `<Service>.IntegrationTests`.
- Mocking framework: NSubstitute; Moq is NOT permitted (consistency rule).

**Rationale**: A distributed system with poor test coverage becomes untraceable noise in production.
Training goals include demonstrating professional test discipline, not just shipping features.

---

### V. Resilience & Observability by Default

**Resilience**:
- Every HTTP/gRPC client MUST be registered via `IHttpClientFactory` with a Polly pipeline
  including: retry (max 3, exponential back-off), circuit breaker (5 failures → 30 s open),
  and timeout (10 s default).
- All services MUST expose `/health/live` and `/health/ready` endpoints using
  `Microsoft.Extensions.Diagnostics.HealthChecks`.

**Observability**:
- OpenTelemetry SDK MUST be configured in every service exporting to Jaeger (local) and
  optionally to an OTLP collector.
- Traces MUST include: incoming HTTP/gRPC spans, outbound calls, database commands
  (EF Core instrumentation), and message bus publish/consume spans.
- Structured logging via Serilog (JSON sink in production, console in development).
- Correlation IDs (`X-Correlation-Id` header) MUST be propagated across all synchronous calls
  and embedded in every log entry and message envelope.

**Rationale**: In a distributed system, observability is the primary debugging tool. Training must
build the habit of instrumenting code before it goes wrong, not after.

---

### VI. Security & Compliance Baseline

**Rules**:
- Authentication: OIDC/OAuth 2.0 via IdentityServer or Auth0; JWT bearer tokens validated at the
  API Gateway (YARP) and at each individual service boundary.
- No service MUST trust an unauthenticated inbound request from outside the Docker network.
- Healthcare data fields (patient PII, medical history references) MUST never be logged in plain
  text; mask or omit sensitive fields in all log sinks and telemetry payloads.
- HTTPS enforced for all external-facing endpoints (TLS terminated at gateway); internal
  Docker-network communication MAY use HTTP but MUST use mTLS in a staging/production deployment.
- SQL injection prevention: EF Core parameterized queries only; raw SQL (`FromSqlRaw`) requires
  review approval and MUST use parameters — no string interpolation.
- Secrets (connection strings, client secrets) MUST be supplied via environment variables or a
  secrets manager; hardcoded credentials are a blocking defect.

**Rationale**: Healthcare data carries regulatory sensitivity (HIPAA-adjacent concerns even in
training); forming correct security habits now avoids catastrophic patterns in production.

---

### VII. Delivery Cadence & Definition of Done

**Weekly Milestone Structure** (6 weeks):

| Week | Milestone |
|---|---|
| 1 | Solution scaffolding, Docker Compose baseline, Auth wiring, shared contracts library |
| 2 | PatientService + ProviderService core CRUD with unit + integration tests |
| 3 | AppointmentService booking flow + outbox + saga documentation |
| 4 | NotificationService + RabbitMQ event wiring across all services |
| 5 | API Gateway (YARP) routing, Redis caching, circuit breakers verified |
| 6 | Observability (OpenTelemetry/Jaeger), health checks, full integration test pass, risk review |

**Definition of Done (per feature/PR)**:
- [ ] Clean Architecture layer boundaries respected (verified by reviewer).
- [ ] All MediatR handlers covered by unit tests (xUnit + Bogus).
- [ ] Integration test suite passes with TestContainers.
- [ ] Outbox pattern applied where cross-service events are emitted.
- [ ] Polly resilience pipeline registered for any new outbound client.
- [ ] OpenTelemetry spans present for new code paths.
- [ ] `/health/ready` reflects new dependency (if added).
- [ ] No secrets hardcoded; no PII in logs.
- [ ] PR description links to relevant spec or milestone.

**Risk Register** (updated each milestone):

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Distributed transaction inconsistency | Medium | High | Outbox + idempotent consumers mandatory |
| Message schema drift across services | Medium | High | Versioned contracts; consumer-driven contract tests |
| Over-engineering for training scope | High | Medium | YAGNI enforced; complexity requires justification |
| TestContainers startup latency in CI | Medium | Low | Parallel test collection; reuse containers per fixture |
| Auth token misconfiguration | Low | High | Auth smoke test in integration suite from Week 1 |

---

## Technology Stack Conventions

**Runtime & Framework**:
- .NET 8 (minimum); .NET 9 permitted for new services if team consensus reached.
- C# 12; nullable reference types enabled (`<Nullable>enable</Nullable>`) in all projects.
- ASP.NET Core Minimal APIs preferred for new endpoints; Controller-based acceptable for complex
  scenarios requiring action filters.

**Data**:
- SQL Server 2022 (Docker image: `mcr.microsoft.com/mssql/server:2022-latest`).
- EF Core 8 (Code-First migrations); migration files committed to source control.
- Redis 7 (`redis:7-alpine`) for distributed cache; `IDistributedCache` abstraction only —
  no direct `StackExchange.Redis` calls in Application layer.

**Messaging**:
- RabbitMQ (`rabbitmq:3-management`) for local development; Azure Service Bus in cloud targets.
- MassTransit as the messaging abstraction layer (transport-agnostic consumer registration).

**API Gateway**:
- YARP (Yet Another Reverse Proxy) configured in a dedicated `ApiGateway` project.

**Auth**:
- Duende IdentityServer (self-hosted, local dev) or Auth0 (cloud); OIDC discovery endpoint
  MUST be used — no hardcoded JWKS URLs.

**Testing**:
- xUnit 2.x, Bogus 35.x, NSubstitute 5.x, TestContainers 3.x (dotnet).

**Observability**:
- OpenTelemetry .NET SDK (`OpenTelemetry.Extensions.Hosting`).
- Jaeger (local): `jaegertracing/all-in-one:latest`.
- Serilog with `Serilog.Sinks.Console` and `Serilog.Sinks.File`.

**Container Runtime**:
- Docker Compose v2; all services defined in `docker-compose.yml` at repository root.
- `docker compose up` MUST bring the entire stack to a ready state with a single command.
- `.env.example` MUST be kept current; `.env` is gitignored.

**Code Style**:
- EditorConfig (`.editorconfig`) at repository root governs indentation (4 spaces), max line
  length (120), and naming conventions.
- `dotnet format` enforced in CI before build step.

---

## Governance

- This constitution supersedes all other practices, README conventions, and verbal agreements.
  Conflicts MUST be resolved by amending the constitution, not by making exceptions.
- **Amendment Procedure**: Any team member may propose an amendment via PR to this file.
  Amendment PRs require at least one senior reviewer approval and MUST include:
  1. Reason for change.
  2. Impact on existing code (migration required or not).
  3. Version bump per semantic rules (see below).
- **Versioning Policy**:
  - MAJOR: Removal or redefinition of a numbered principle (I–VII), or architectural boundary change.
  - MINOR: Addition of a new principle, technology, or mandatory process.
  - PATCH: Wording clarification, typo correction, example updates.
- **Compliance Review**: Each weekly milestone review MUST include a 15-minute constitution
  compliance walk-through. Non-compliant code identified at review MUST be remediated before
  the milestone is marked complete.
- **Template Alignment**: When the constitution is amended, `.specify/templates/plan-template.md`,
  `spec-template.md`, and `tasks-template.md` MUST be reviewed for consistency within the same PR.

**Version**: 1.0.0 | **Ratified**: 2026-04-01 | **Last Amended**: 2026-04-01
