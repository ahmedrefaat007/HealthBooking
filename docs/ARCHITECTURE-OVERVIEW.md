# HealthBooking — Software Architecture Overview

> High-level technical guide for software architects, senior engineers, and technical leads

---

## Table of Contents

1. [System Context](#1-system-context)
2. [Architectural Style & Principles](#2-architectural-style--principles)
3. [Service Decomposition](#3-service-decomposition)
4. [Communication Patterns](#4-communication-patterns)
5. [Data Architecture](#5-data-architecture)
6. [Security Architecture](#6-security-architecture)
7. [Resilience Architecture](#7-resilience-architecture)
8. [Observability Architecture](#8-observability-architecture)
9. [Deployment Architecture](#9-deployment-architecture)
10. [Technology Decisions (ADRs)](#10-technology-decisions-adrs)
11. [Quality Attributes](#11-quality-attributes)
12. [Scalability & Evolution Paths](#12-scalability--evolution-paths)

---

## 1. System Context

```
                        ┌─────────────────────────────────┐
                        │          External Users          │
                        │                                  │
                        │  ┌───────────┐  ┌────────────┐  │
                        │  │  Patient  │  │  Provider  │  │
                        │  │   (SPA)   │  │  (SPA/App) │  │
                        │  └─────┬─────┘  └─────┬──────┘  │
                        └────────┼──────────────┼──────────┘
                                 │ HTTPS        │ HTTPS
                                 ▼              ▼
                        ┌────────────────────────────────┐
                        │          API Gateway           │
                        │    (YARP + JWT + Rate Limit)   │
                        └──────────────┬─────────────────┘
                                       │
              ┌────────────────────────┼──────────────────────┐
              ▼                        ▼                       ▼
    ┌──────────────────┐   ┌──────────────────┐   ┌──────────────────────┐
    │  PatientService  │   │ ProviderService  │   │ AppointmentService   │
    │  (HTTP + gRPC)   │   │  (HTTP + gRPC)   │   │  (HTTP + Saga)       │
    └──────────────────┘   └──────────────────┘   └──────────────────────┘
              │                        │                       │
              └────────────────────────┼──────────────────────┘
                                       │ RabbitMQ events
                                       ▼
                             ┌──────────────────────┐
                             │  NotificationService │
                             │  (Event Consumer)    │
                             └──────────────────────┘

    ┌────────────────┐    Infrastructure (shared, not coupled)
    │ IdentityServer │    ┌───────┐  ┌───────┐  ┌────────┐  ┌────────┐
    │ (Duende OIDC)  │    │ SQL×5 │  │ Redis │  │Rabbit  │  │ Jaeger │
    └────────────────┘    └───────┘  └───────┘  └────────┘  └────────┘
```

---

## 2. Architectural Style & Principles

### Microservices with Clean Architecture per Service

The system is a **microservices application** where each service is independently deployed and owns its data. Within each service, a strict **Clean Architecture** layering rule is enforced:

```
┌──────────────────────────────────────────────────────┐
│  API (Minimal API / gRPC)  — depends on Application  │
├──────────────────────────────────────────────────────┤
│  Infrastructure            — depends on Application  │
├──────────────────────────────────────────────────────┤
│  Application               — depends on Domain only  │
├──────────────────────────────────────────────────────┤
│  Domain                    — zero external deps       │
└──────────────────────────────────────────────────────┘
```

**Benefits:**
- Business rules in Domain/Application are testable without starting a database or HTTP server
- Infrastructure (SQL Server, Redis, RabbitMQ) is an implementation detail that can be swapped
- Following the Dependency Rule means the most stable, valuable code has the fewest dependencies

### Domain-Driven Design

| DDD Concept | How it's applied |
|---|---|
| **Aggregate** | `Patient`, `Provider`, `Appointment` — each enforces its own consistency |
| **Factory Method** | `Patient.Register(...)`, `Provider.Register(...)` — invalid objects cannot be constructed |
| **Value Object** | `Email`, `PhoneNumber`, `FullName`, `SpecialtyName` — immutable, self-validating |
| **Domain Event** | `PatientRegisteredEvent` etc. — raised inside aggregates, captured by OutboxPublishingInterceptor |
| **Repository** | Interface in Application, EF Core implementation in Infrastructure |
| **Bounded Context** | Each microservice IS a bounded context |

### CQRS (Command Query Responsibility Segregation)

Every operation is either a Command (writes) or a Query (reads), routed through MediatR. This separates write and read concerns, allows different validation rules per direction, and enables future read-model optimisation (e.g., projecting to a read-optimised view or search index) without touching write logic.

---

## 3. Service Decomposition

### Decomposition Rationale

Services are split along **domain capability boundaries** (not technical concerns):

| Service | Domain Capability | Why Separated |
|---|---|---|
| PatientService | Patient identity & profile | Different change rate; patient data is PII — isolated security boundary |
| ProviderService | Provider scheduling & availability | High read throughput for slot browsing; needs Redis cache; separate scaling |
| AppointmentService | Booking orchestration | Most complex business logic; owns the booking saga |
| NotificationService | Patient communication | Pure consumer; can fail without blocking booking; deploy-independently scalable |
| IdentityServer | Auth & identity | Industry-standard isolation; externally replaceable (Auth0, Keycloak) |
| ApiGateway | Cross-cutting concerns | Single ingress point; auth, rate limiting, routing |

### Inter-Service Dependencies

```
                  PatientService
                  /    |
                 /     |  (gRPC)
     AppointmentService  NotificationService
                 \     |  (gRPC)
                  \    |
                  ProviderService

     All events via RabbitMQ (loose coupling):
     AppointmentService ──[V1_AppointmentBookedEvent]──▶ NotificationService
```

Key principle: **services never share a database**. Cross-service state reads use gRPC calls; cross-service reactions use events.

---

## 4. Communication Patterns

### Pattern 1: Synchronous REST (Client → Gateway → Service)

Used for all external-facing CRUD operations. The gateway validates the JWT and proxies to the relevant service. Clients receive an immediate response.

**Latency budget:** < 100 ms for simple reads (DB query + JSON response).

### Pattern 2: gRPC (Service → Service, internal)

Used for cross-service data lookups required **during** a transaction or saga step, where consistency is critical.

| Call Direction | Why gRPC, not REST |
|---|---|
| AppointmentService → PatientService (verify patient) | Strongly typed; binary; lower latency |
| AppointmentService → ProviderService (lock slot) | Optimistic concurrency on slot requires server-side logic |
| NotificationService → PatientService (get email) | Lightweight lookup; Polly retry handles transient failures |

Proto definitions live in `HealthBooking.Contracts` — the single source of truth for both client and server.

### Pattern 3: Asynchronous Events (Service → RabbitMQ → Service)

Used for operations that don't need an immediate response and where the producer should not be coupled to the consumer's availability.

```
Producer (AppointmentService)          Consumer (NotificationService)
       │                                          │
       │  Writes OutboxMessage (same transaction) │
       │                                          │
       │  OutboxProcessor polls every 5 s         │
       │  Publishes to RabbitMQ exchange           │
       │                                          │
       └── "fire and forget" ───────────────────▶ │
                                                  │  IConsumer<V1_AppointmentBookedEvent>
                                                  │  Sends email + logs NotificationLog
```

**Guarantee:** At-least-once delivery (outbox + RabbitMQ durability). Consumers are idempotent (write `NotificationLog` with unique index if needed).

### Pattern 4: Saga / Request-Response (Booking Flow)

The booking operation requires coordinating three services atomically with compensation. A synchronous solution would create tight coupling and no compensation path. The saga solves this:

```
HTTP (30 s timeout)
    ├─ awaits ◀─────────────────────────────────────────────┐
    │                                                       │
    ▼  V1_InitiateBookingCommand                           V1_BookingCompletedEvent
AppointmentService                                         OR V1_BookingFailedEvent
    │
    ▼  3 steps, each with a compensation Faulted handler
BookingStateMachine
```

MassTransit's `IRequestClient<T>` implements the Correlation-based Request/Response pattern over RabbitMQ, so the "saga response" is still async under the hood, but the HTTP caller awaits it with a timeout.

---

## 5. Data Architecture

### Database per Service

Each service has its own SQL Server instance (separate container). No shared DB, no cross-service joins.

```
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────────┐
│ healthbooking_   │  │ healthbooking_   │  │ healthbooking_           │
│   patients       │  │   providers      │  │   appointments           │
│                  │  │                  │  │                          │
│ Patients         │  │ Providers        │  │ Appointments             │
│ OutboxMessages   │  │ AvailabilitySlots│  │ BookingIdempotencyKeys   │
│                  │  │ OutboxMessages   │  │ BookingSagaStates        │
│                  │  │                  │  │ OutboxMessages           │
└──────────────────┘  └──────────────────┘  └──────────────────────────┘

┌──────────────────┐  ┌────────────────────────────────────────────────┐
│ healthbooking_   │  │ healthbooking_identity                         │
│   notifications  │  │                                                │
│                  │  │ Clients, ApiResources, ApiScopes               │
│ NotificationLogs │  │ PersistedGrants, DeviceCodes                   │
└──────────────────┘  └────────────────────────────────────────────────┘
```

### Eventual Consistency

The Outbox Pattern ensures domain events are delivered **eventually** (within seconds) even after a service restart. The system is strongly consistent within a single service boundary and eventually consistent across service boundaries.

### Caching Strategy

Provider slot lists are cached in Redis with a 60-second TTL. Cache is **eagerly evicted** on any slot mutation (lock/release). This gives:
- Fast reads for the common case (slot browsing)
- Consistent writes (no stale lookup during booking)

### Optimistic Concurrency

`AvailabilitySlot` has a `RowVersion` (byte[], concurrency token) column. If two saga instances try to lock the same slot simultaneously, EF Core throws `DbUpdateConcurrencyException` on the second writer — the second saga fails cleanly and the first wins.

---

## 6. Security Architecture

### Authentication Flow

```
Patient/Admin → IdentityServer (OIDC Resource Owner Password)
                        │
                        ▼  issues JWT (RS256)
Client stores JWT → sends in Authorization: Bearer header
                        │
                        ▼
API Gateway validates JWT (IdentityServer discovery endpoint)
                        │
            ┌-----------┴-----------┐
            ▼                       ▼
    Authenticated routes      Unauthenticated
    (require JwtBearer)       (/api/patients/register)
```

### Token Scopes

Fine-grained scopes allow future enforcement at service level:

| Scope | Meaning |
|---|---|
| `healthbooking-api` | General access (current gateway check) |
| `patient:read/write` | (Future: service-level enforcement) |
| `provider:read/write` | (Future) |
| `appointment:read/write` | (Future) |

### Rate Limiting (OWASP: API4 — Unrestricted Resource Consumption)

Sliding window: 300 requests/minute per client, 6 segments (50 req/10 s burst). Queue limit = 0 (reject immediately when limit reached, return 429).

### Correlation IDs (Traceability)

`CorrelationIdMiddleware` ensures every request carries a `X-Correlation-Id` through the entire call chain. Logs and OTel spans reference this ID, making security incident tracing across services feasible.

### No Secrets in Code

All database passwords, client secrets, and connection strings come from environment variables or `.env` files (never committed). `.env.example` shows the required variables with placeholder values.

---

## 7. Resilience Architecture

### Failure Modes & Mitigations

| Failure | Mitigation |
|---|---|
| Database transient timeout | EF Core retry-on-failure (SQL Server retry provider) |
| gRPC call timeout | Polly: 10 s timeout + 3 retries (exponential backoff + jitter) |
| gRPC persistent failure | Polly circuit breaker (opens at 50% failure rate, 30 s break) |
| RabbitMQ downtime | Outbox: events persisted in SQL, retried when RabbitMQ recovers |
| Duplicate booking request | Idempotency key: pre-check before saga + re-check inside `PersistAppointmentActivity` |
| Concurrent slot booking | Optimistic concurrency: `RowVersion` on `AvailabilitySlot` |
| Partial saga failure | Compensation: `LockSlotActivity.Faulted` releases the slot if it was locked |
| Service restart mid-saga | MassTransit saga state persisted in SQL: resumes from last committed state |
| Slow downstream service | `PerformanceBehavior`: logs warning at 500 ms; gateway circuit breaker |

### Polly Pipeline (per gRPC client)

```
Request
  │
  ▼ [Timeout: 10 s]
  │  ─▶ TimeoutRejectedException if exceeded
  │
  ▼ [Retry: 3 attempts, exponential backoff 500 ms base + jitter]
  │  ─▶ Retries on HttpRequestException, 5xx, timeout
  │
  ▼ [Circuit Breaker]
  │  Closed (normal) → counts failures
  │  ─▶ Opens when ≥ 50% failures in 30 s window (min 5 calls)
  │  Open → rejects immediately for 30 s (HalfOpen after)
  │
  ▼ [Downstream gRPC service]
```

---

## 8. Observability Architecture

### Three Pillars

| Pillar | Implementation |
|---|---|
| **Logs** | Serilog (structured JSON) — all services; Serilog request logging middleware |
| **Traces** | OpenTelemetry 1.11 → OTLP gRPC → Jaeger |
| **Metrics** | (Future: `OpenTelemetry.Instrumentation.Runtime` + Prometheus) |

### Trace Propagation

```
External Client
    │  HTTP request with no trace context
    ▼
API Gateway  ─────────────────────── generates root span
    │         passes W3C TraceContext headers downstream
    ▼
PatientService / ProviderService / AppointmentService
    │         each service creates child spans
    ▼
MassTransit   creates spans for publish + consume
    │
    ▼
All → OTLP gRPC → Jaeger
```

W3C `traceparent` / `tracestate` headers are propagated automatically by OpenTelemetry's `ActivityContext`. A single booking request produces a **distributed trace** across 4 services visible as a single timeline in Jaeger.

### Structured Logging

Serilog is configured on all services with:
- `MinimumLevel: Information` (override `Microsoft: Warning`, `Yarp: Warning` to reduce noise)
- `WriteTo.Console` (Docker-friendly; no file sinks in containers)
- `Enrich.FromLogContext` (attaches OTel `TraceId` / `SpanId` if available)

---

## 9. Deployment Architecture

### Current: Docker Compose (Local / Dev)

```
Docker Engine
│
├── docker network: default
│   ├── infrastructure containers (SQL ×5, RabbitMQ, Redis, Jaeger)
│   └── application containers (IdentityServer, 4 services, ApiGateway)
│
└── volumes: named volumes per SQL Server instance
```

All containers communicate over the Docker bridge network by **container name** (service discovery via DNS). No Kubernetes, no service mesh — intentionally simple.

### Production Path (Kubernetes)

The Dockerfiles, health checks (`/health/live`, `/health/ready`), and environment variable design are all Kubernetes-ready:

| K8s Feature | How it maps |
|---|---|
| `livenessProbe` | `GET /health/live` |
| `readinessProbe` | `GET /health/ready` |
| `ConfigMap` | Non-secret config (URLs, TTLs) |
| `Secret` | Connection strings, client secrets |
| `HorizontalPodAutoscaler` | `AppointmentService`, `PatientService` scale independently |
| `Ingress` | Replace API Gateway or use Nginx Ingress + YARP |

### Dockerfile Pattern (all services)

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "<Service>.API.dll"]
```

Multi-stage builds keep runtime images lean (no SDK, no source code).

---

## 10. Technology Decisions (ADRs)

### ADR-001: Minimal API over MVC Controllers

**Decision:** Use ASP.NET Core Minimal API for all HTTP endpoints.  
**Reason:** Minimal API has less ceremony, maps well to vertical-slice or CQRS patterns. Each endpoint is a lambda that delegates to MediatR. No controller base class inheritance. Easier to test in isolation.  
**Trade-off:** No built-in model binding for complex scenarios (mitigated by explicit record parameters).

### ADR-002: MediatR for Application Layer

**Decision:** All commands and queries go through `ISender.Send(...)`.  
**Reason:** Decouples the API from the handler; pipeline behaviors apply cross-cutting concerns uniformly (logging, validation, performance). Enables adding a new behavior (e.g., distributed caching) without touching individual handlers.

### ADR-003: Outbox Pattern over Direct RabbitMQ Publish

**Decision:** Persist domain events to `OutboxMessages` table in the same transaction, then relay via background service.  
**Reason:** Direct publish after `SaveChanges` is not atomic — process crash between save and publish loses the event. The Outbox guarantees at-least-once delivery.  
**Trade-off:** ~5-second event delay (polling interval). Acceptable for email notifications, not for real-time features.

### ADR-004: MassTransit Saga over Choreography

**Decision:** Use a MassTransit saga (orchestration) for the booking flow instead of choreography.  
**Reason:** Three-step flow (verify patient → lock slot → persist appointment) requires compensation if any step fails. Choreography would scatter compensation logic across services. The saga centralises it.  
**Trade-off:** Single point of orchestration; saga state table is a potential bottleneck at very high booking volumes (mitigate with sharding or saga partitioning).

### ADR-005: gRPC for Internal Service Calls

**Decision:** Inter-service synchronous calls use gRPC, not REST.  
**Reason:** Proto3 contracts are strongly typed; code generation eliminates serialisation bugs. Binary encoding is smaller and faster than JSON. Streaming support available for future use.  
**Trade-off:** Proto files must be maintained; no browser-native support (mitigated — browsers don't call internal services directly).

### ADR-006: Database-per-Service (separate SQL Server instances)

**Decision:** Each service has its own SQL Server container and schema — no shared DB.  
**Reason:** Shared databases couple services at the data level, defeating the purpose of microservices. One schema change can break another service.  
**Trade-off:** More DB connections, more infra, more cost. For development this is acceptable; in production, consider managed cloud SQL (5 × Azure SQL S1 = ~$25/month).

### ADR-007: Redis for Slot Caching (Read-aside, Eager Eviction)

**Decision:** Cache `GetProviderSlots` results in Redis, evict on every mutation.  
**Reason:** Slot browsing is a high-read, low-write operation. A 60-second stale window is unacceptable during booking (patient could book a slot that was just locked). Eager eviction on mutation ensures consistency.  
**Trade-off:** Cache miss on first request after mutation — one extra DB round-trip. Acceptable.

### ADR-008: Duende IdentityServer (self-hosted OIDC)

**Decision:** Self-host Duende IdentityServer rather than using a cloud provider (Auth0, Azure AD B2C).  
**Reason:** Full control over token content, client configuration, and user store. No external dependency for auth during development. Community edition is free.  
**Trade-off:** Responsible for patching, uptime, and key rotation. For production scale, evaluate managed alternatives.

### ADR-009: OpenTelemetry (no vendor lock-in)

**Decision:** Export traces via OTLP to Jaeger. No vendor SDK used.  
**Reason:** OTLP is the open standard. The same instrumentation code works with Jaeger, Tempo, Honeycomb, Datadog, Azure Monitor — just change the exporter endpoint.

---

## 11. Quality Attributes

| Attribute | Mechanism |
|---|---|
| **Maintainability** | Clean Architecture, DDD, single-responsibility files, MediatR pipeline |
| **Testability** | Domain has no deps; repositories are interfaces; all handlers unit-testable with mocks |
| **Reliability** | Outbox Pattern, Polly resilience, saga with compensation, optimistic concurrency |
| **Security** | JWT/OIDC, scoped API access, rate limiting, CorrelationId tracing, no secrets in code |
| **Observability** | Structured logging (Serilog), distributed tracing (OTel + Jaeger), health checks |
| **Performance** | Redis cache for hot read path; binary gRPC for internal calls; async/await throughout |
| **Scalability** | Stateless services (session-less); independent scaling per service; horizontal pod scaling |
| **Portability** | Docker Compose → Kubernetes; OTLP → any trace backend; SQL Server → PostgreSQL (EF Core) |
| **Evolvability** | Versioned event contracts (`V1_*`); new consumers without producer changes |

---

## 12. Scalability & Evolution Paths

### Horizontal Scaling

Because all application state lives in the database (not in-process), every service can be scaled to multiple replicas:

- **PatientService, ProviderService, AppointmentService** — stateless; add replicas freely
- **NotificationService** — MassTransit distributes RabbitMQ messages across competing consumers automatically
- **API Gateway** — stateless YARP; load-balanced by cloud ingress
- **IdentityServer** — requires shared `PersistedGrantDbContext` (already SQL-backed)

### Evolution Paths

| Current | Future Option |
|---|---|
| SQL Server | Migrate to PostgreSQL (EF Core provider swap) or CosmosDB |
| RabbitMQ | Replace with Azure Service Bus or AWS SQS (MassTransit transport swap) |
| Redis | Replace with Valkey or Azure Cache for Redis |
| Duende IdentityServer | Migrate to Auth0 / Azure AD B2C (JWT format unchanged) |
| Docker Compose | Helm charts for Kubernetes |
| `LoggingEmailService` | Real email via SendGrid, Mailgun, SMTP |
| Outbox 5s poll | Change stream / CDC (e.g., Debezium) for near-zero latency events |
| Single monorepo | Split into separate repositories per service (API contract in own versioned package) |

### Adding a New Service

1. Create 4 projects: `Domain`, `Application`, `Infrastructure`, `API`
2. Reference `HealthBooking.SharedKernel` and `HealthBooking.Contracts`
3. Add `DbContext` with interceptors, migrations
4. Register `AddHealthBookingTelemetry`, health checks, Serilog, MediatR in `Program.cs`
5. Add container to `docker-compose.yml`
6. Add YARP route in `ApiGateway/appsettings.json`
7. Add unit and integration test projects

No existing service needs to change.
