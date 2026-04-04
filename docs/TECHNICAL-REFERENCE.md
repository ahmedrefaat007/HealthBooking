# HealthBooking — Complete Technical Reference

> Every service · Every file · Every folder · Every design decision · Every API flow  
> Weeks 0 through 6 · Branch: `001-w6-observability`

---

## Table of Contents

1. [Solution Overview](#1-solution-overview)
2. [Repository Structure](#2-repository-structure)
3. [Shared Kernel](#3-shared-kernel)
4. [IdentityServer](#4-identityserver)
5. [API Gateway](#5-api-gateway)
6. [PatientService](#6-patientservice)
7. [ProviderService](#7-providerservice)
8. [AppointmentService](#8-appointmentservice)
9. [NotificationService](#9-notificationservice)
10. [Infrastructure Patterns (cross-cutting)](#10-infrastructure-patterns)
11. [API Endpoint Reference](#11-api-endpoint-reference)
12. [API Request Flows](#12-api-request-flows)
13. [Docker Compose & Local Dev](#13-docker-compose--local-dev)
14. [OpenTelemetry & Observability](#14-opentelemetry--observability)
15. [testing Strategy](#15-testing-strategy)
16. [CI/CD Pipeline](#16-cicd-pipeline)
17. [Week-by-Week Build Log](#17-week-by-week-build-log)

---

## 1. Solution Overview

HealthBooking is a **microservices-based healthcare appointment booking platform** built on **.NET 9** using **Clean Architecture** and **Domain-Driven Design (DDD)** principles.

### Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 9 / ASP.NET Core 9 |
| Language | C# 13 (nullable enabled, implicit usings) |
| Architecture | Clean Architecture + DDD |
| API Style | Minimal API (HTTP), gRPC (inter-service) |
| Async messaging | MassTransit 8 + RabbitMQ |
| Identity | Duende IdentityServer 7 (OAuth2 / OIDC) |
| Gateway | YARP (Yet Another Reverse Proxy) |
| Persistence | Entity Framework Core 9 + SQL Server 2022 |
| Cache | Redis 7 (StackExchange.Redis) |
| Logging | Serilog |
| Resilience | Microsoft.Extensions.Http.Resilience (Polly 8) |
| Observability | OpenTelemetry 1.11 → Jaeger |
| Tests | xUnit, FluentAssertions, NSubstitute, Bogus, Testcontainers |
| Containers | Docker Compose |
| CI | GitHub Actions |

### Service Ports

| Service | Port | Protocol |
|---|---|---|
| API Gateway | 5000 | HTTP |
| IdentityServer | 5005 | HTTP |
| PatientService | 5001 | HTTP + gRPC |
| ProviderService | 5002 | HTTP + gRPC |
| AppointmentService | 5003 | HTTP |
| NotificationService | 5004 | HTTP |
| Jaeger UI | 16686 | HTTP |
| RabbitMQ Management | 15672 | HTTP |
| Redis | 6379 | TCP |

---

## 2. Repository Structure

```
HealthBooking.sln
├── src/
│   ├── SharedKernel/
│   │   ├── HealthBooking.SharedKernel/       # Domain primitives, behaviors, extensions
│   │   └── HealthBooking.Contracts/          # Proto files + MassTransit message contracts
│   ├── IdentityServer/
│   │   └── HealthBooking.IdentityServer/     # Duende IdentityServer, OAuth2 / OIDC
│   ├── ApiGateway/
│   │   └── HealthBooking.ApiGateway/         # YARP reverse proxy, rate limiting, auth
│   └── Services/
│       ├── PatientService/                   # Patient registration, profile, gRPC lookup
│       ├── ProviderService/                  # Provider registration, slot management, Redis cache
│       ├── AppointmentService/               # Booking saga, appointment lifecycle
│       └── NotificationService/             # Email notifications, event consumers
├── tests/
│   ├── PatientService.UnitTests/
│   ├── PatientService.IntegrationTests/
│   ├── ProviderService.UnitTests/
│   ├── ProviderService.IntegrationTests/
│   ├── AppointmentService.UnitTests/
│   ├── AppointmentService.IntegrationTests/
│   ├── NotificationService.UnitTests/
│   └── NotificationService.IntegrationTests/
├── docs/                                     # Weekly documentation + this file
├── specs/                                    # Feature specification + tasks
├── docker-compose.yml
├── docker-compose.override.yml
├── .env.example
└── .github/workflows/ci.yml
```

**Why this layout?**  
Each microservice is an independent deployable unit. Separating `src/Services/*` from `src/SharedKernel/*` enforces the rule that services may only depend on shared abstractions, not on each other's internals. The `tests/` directory mirrors the `src/` structure so test projects are easy to locate and CI can target them individually.

---

## 3. Shared Kernel

Path: `src/SharedKernel/`

### 3.1 `HealthBooking.SharedKernel`

Provides reusable base classes, MediatR pipeline behaviors, and extension methods that all services reference. Services depend on this library; it does not depend on any service.

#### `Domain/AuditableEntity.cs`
```csharp
public abstract class AuditableEntity
{
    public DateTimeOffset  CreatedAt  { get; set; }
    public string          CreatedBy  { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public string?         ModifiedBy { get; set; }
}
```
**Why:** Every persisted entity needs audit columns for compliance and debugging. Placing this at the base means the `AuditInterceptor` can detect any changed `AuditableEntity` via EF Core's `ChangeTracker` without service-specific code.

#### `Domain/AggregateRoot.cs`
```csharp
public abstract class AggregateRoot : AuditableEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    protected void AddDomainEvent(IDomainEvent e) => _domainEvents.Add(e);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```
**Why:** The Aggregate Root pattern ensures that all mutations to a domain object cluster happen through a single entry point, raising events as side-effects. The `OutboxPublishingInterceptor` reads `DomainEvents` at save time and serialises them to the Outbox table.

#### `Domain/IDomainEvent.cs`
Marker interface. All domain events (`PatientRegisteredEvent`, `ProviderRegisteredEvent`, `AppointmentBookedEvent`, etc.) implement this.

#### `Persistence/OutboxMessage.cs`
Database row that represents a pending domain event for guaranteed delivery:

| Column | Type | Purpose |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `EventType` | `string` | Fully qualified type name for deserialisation |
| `SchemaVersion` | `string` | Version for future backward-compat migrations |
| `Payload` | `string` | JSON-serialised event body |
| `DestinationExchange` | `string` | RabbitMQ exchange name |
| `CreatedAt` | `DateTimeOffset` | For ordering |
| `PublishedAt` | `DateTimeOffset?` | Set when successfully dispatched |
| `RetryCount` | `int` | Incremented on each failed dispatch attempt |
| `Status` | `string` | `"Pending"` / `"Published"` / `"Failed"` |

#### `Persistence/OutboxMessageConfiguration.cs`
EF Core `IEntityTypeConfiguration<OutboxMessage>` — table name, key, column lengths. Every service's `DbContext` applies this via `modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration())`.

#### `Behaviors/ValidationBehavior.cs`
MediatR `IPipelineBehavior<TRequest, TResponse>`. Runs all registered FluentValidation `IValidator<TRequest>` instances before the handler. Throws `ValidationException` on failure (mapped to `400 Bad Request` by ASP.NET Core Problem Details).

#### `Behaviors/LoggingBehavior.cs`
Logs `Handling <RequestType>` with parameters before, and `Handled <RequestType>` with elapsed time after every MediatR request. Uses `ILogger<T>`.

#### `Behaviors/PerformanceBehavior.cs`
Logs a warning if a MediatR handler takes longer than 500 ms. Useful for catching slow queries during development.

#### `Extensions/ResilienceExtensions.cs`
Single method `AddHealthBookingResiliencePipeline(pipelineName)` applied to any `IHttpClientBuilder`. Configures:
- **Timeout**: 10 seconds total
- **Retry**: 3 attempts, exponential backoff from 500 ms with jitter
- **Circuit Breaker**: opens when 50% of requests fail over 30 s with at least 5 requests; stays open for 30 s

**Why centralised:** All four gRPC client registrations in AppointmentService and NotificationService use the same resilience pipeline, ensuring a single policy change propagates everywhere.

#### `Extensions/TelemetryExtensions.cs`
`AddHealthBookingTelemetry(serviceName, configuration)` — registered in every service `Program.cs`. Instruments ASP.NET Core, HttpClient (including gRPC client), and MassTransit. Exports to Jaeger via OTLP gRPC. See §14 for details.

---

### 3.2 `HealthBooking.Contracts`

Shared message / proto definitions referenced by producer and consumer services. This library has **zero implementation code** — only contracts.

#### `Protos/patient.proto`
```protobuf
service PatientGrpc {
  rpc GetPatientById (GetPatientByIdRequest) returns (PatientResponse);
}
```
Used by: PatientService (server), AppointmentService (client), NotificationService (client).

#### `Protos/provider.proto`
```protobuf
service ProviderGrpc {
  rpc GetSlotById     (GetSlotRequest)     returns (SlotResponse);
  rpc LockSlot        (LockSlotRequest)    returns (LockSlotResponse);
  rpc ReleaseSlot     (ReleaseSlotRequest) returns (ReleaseSlotResponse);
}
```
Used by: ProviderService (server), AppointmentService (client via booking saga).

#### `Appointments/V1/V1_AppointmentEvents.cs`
MassTransit integration events published to RabbitMQ after an appointment is booked or cancelled:

| Event | Published by | Consumed by |
|---|---|---|
| `V1_AppointmentBookedEvent` | AppointmentService OutboxProcessor | NotificationService |
| `V1_AppointmentCancelledEvent` | AppointmentService OutboxProcessor | NotificationService |
| `V1_AppointmentRescheduledEvent` | AppointmentService | (future) |
| `V1_SlotReleasedEvent` | AppointmentService | (future) |

#### `Appointments/V1/V1_BookingMessages.cs`
Saga control messages (internal to AppointmentService, but placed in Contracts so the API layer can reference them):

| Message | Direction |
|---|---|
| `V1_InitiateBookingCommand` | HTTP endpoint → Saga |
| `V1_BookingCompletedEvent` | Saga → HTTP endpoint (response) |
| `V1_BookingFailedEvent` | Saga → HTTP endpoint (response) |

---

## 4. IdentityServer

Path: `src/IdentityServer/HealthBooking.IdentityServer/`

**Technology:** Duende IdentityServer 7 (Community edition), backed by SQL Server via EF Core.

### Files

#### `Config.cs`
Declares all OAuth2 / OIDC resources and clients in code (seeded into DB on startup):

**Identity Resources** (standard OIDC scopes):
- `openid`, `profile`, `email`

**API Scopes** (fine-grained permissions):
- `healthbooking-api` (coarse all-access)
- `patient:read`, `patient:write`
- `provider:read`, `provider:write`
- `appointment:read`, `appointment:write`

**Clients:**

| Client ID | Grant Type | Used by |
|---|---|---|
| `api-gateway` | Client Credentials | Service-to-service M2M |
| `patient-spa` | Resource Owner Password + Client Credentials | Angular SPA (patients) |
| `admin-client` | Resource Owner Password + Client Credentials | Admin tools / Postman |

> **Note:** Resource Owner Password grant is used here for simplicity in early development. For production, replace with PKCE Authorization Code flow for SPAs.

#### `SeedData.cs`
Called from `Program.cs` at startup. Runs EF Core `MigrateAsync` on both `ConfigurationDbContext` and `PersistedGrantDbContext`, then idempotently seeds the resources, scopes, and clients from `Config.cs`.

#### `Program.cs`
- Uses SQL Server for both the configuration store and the persisted grant store (separate DB `healthbooking_identity`)
- Exposes OIDC discovery endpoint at `http://identity-server:5005/.well-known/openid-configuration`
- Health check: `/health/live`

---

## 5. API Gateway

Path: `src/ApiGateway/HealthBooking.ApiGateway/`

**Technology:** YARP 2.x (Microsoft's reverse proxy library for ASP.NET Core).

### Responsibility
The gateway is the **single entry point** for all external clients. It handles:
1. JWT authentication (validates tokens issued by IdentityServer)
2. Route-based forwarding to downstream services
3. Sliding-window rate limiting (300 req/min)
4. Correlation ID propagation
5. Structured logging (Serilog)
6. OpenTelemetry tracing

### Files

#### `appsettings.json` — YARP Route Table

| Route | Auth Required | Cluster | Destination |
|---|---|---|---|
| `POST /api/patients/register` | No | patient-cluster | `http://patient-service:5001` |
| `/api/patients/**` | Yes | patient-cluster | `http://patient-service:5001` |
| `/api/providers/**` | Yes | provider-cluster | `http://provider-service:5002` |
| `/api/appointments/**` | Yes | appointment-cluster | `http://appointment-service:5003` |

Each cluster has an **active health check** polling `/health/ready` every 10 seconds with a 5-second timeout. Failed destinations are removed from rotation (ConsecutiveFailures policy).

#### `Program.cs`
```
JWT Authentication (IdentityServer:Authority)
→ Authorisation policies (JwtBearer = RequireAuthenticatedUser)
→ Rate Limiter (sliding window, 300/min, 6 segments)
→ YARP reverse proxy
→ CorrelationIdMiddleware
→ Health: /health/ready (composite), /health/live (always 200)
```

#### `Middleware/CorrelationIdMiddleware.cs`
Reads `X-Correlation-Id` header from incoming requests. If absent, generates a new `Guid`. Writes it to `HttpContext.TraceIdentifier` and echoes it in the response headers. This correlates log entries and OTel spans across all downstream services for a single request.

---

## 6. PatientService

Path: `src/Services/PatientService/`

**Responsibility:** Registration, authentication identity provisioning, and profile management for patients. Exposes HTTP endpoints (REST) and a gRPC server for internal patient lookups.

### 6.1 Domain Layer (`PatientService.Domain`)

#### `Entities/Patient.cs`
The aggregate root. All mutations go through factory methods or named methods — no public setters.

Key properties:
- `Id` (Guid, PK)
- `FirstName`, `LastName` (normalised via `FullName` value object)
- `ContactEmail` (normalised lowercase via `Email` value object)
- `PhoneNumber` (validated E.164 digits via `PhoneNumber` value object)
- `DateOfBirth` (must be in the past)
- `RegistrationDate` (set at creation)
- Inherits `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy` from `AuditableEntity`

Factory method: `Patient.Register(firstName, lastName, email, phone, dob)` — validates all inputs using value objects, raises `PatientRegisteredEvent`.

#### `ValueObjects/PatientValueObjects.cs`

| Value Object | Validation logic |
|---|---|
| `Email` | Must contain `@` and `.`; stored as `value.Trim().ToLowerInvariant()` |
| `PhoneNumber` | E.164: 7–15 digits after stripping non-digit characters |
| `FullName` | Both parts non-empty, trimmed |
| `PatientId` | Wrapper around `Guid` |

**Why value objects?** They make invalid states unrepresentable. You cannot construct a `Patient` with an invalid email — the value object constructor throws before the entity is created.

#### `Events/PatientEvents.cs`
- `PatientRegisteredEvent(Guid PatientId, string FullName, string Email, DateTimeOffset OccurredAt)`
- `PatientProfileUpdatedEvent(Guid PatientId, DateTimeOffset OccurredAt)`

### 6.2 Application Layer (`PatientService.Application`)

#### `Commands/RegisterPatient/RegisterPatientCommand.cs`
MediatR command + validator + handler in one file (co-location pattern):

**Flow:**
1. `RegisterPatientCommandValidator` (FluentValidation) — validates before handler runs (via `ValidationBehavior`)
2. `IPatientRepository.ExistsByEmailAsync` — duplicate email guard
3. `Patient.Register(...)` — domain factory
4. `IPatientRepository.AddAsync` + `SaveChangesAsync` — persistence (OutboxPublishingInterceptor fires)
5. `IIdentityProvisioningService.ProvisionUserAsync` — creates user in IdentityServer
6. Returns `RegisterPatientResult(PatientId)`

#### `Commands/UpdatePatientProfile/UpdatePatientProfileCommand.cs`
Loads patient by ID, calls `patient.UpdateProfile(...)`, saves. Raises `PatientProfileUpdatedEvent`.

#### `Queries/GetPatientById/`, `Queries/GetPatientByEmail/`
Simple read-through queries. `GetPatientByIdQuery` returns `PatientDto`; if not found, returns `null` (endpoint maps to 404).

#### `Interfaces/IPatientInterfaces.cs`
- `IPatientRepository` — `GetByIdAsync`, `GetByEmailAsync`, `AddAsync`, `ExistsByEmailAsync`, `SaveChangesAsync`
- `IIdentityProvisioningService` — `ProvisionUserAsync(patientId, email, ct)`
- `ICurrentUserService` — `UserId` (read from JWT claim by `CurrentUserService`)

### 6.3 Infrastructure Layer (`PatientService.Infrastructure`)

#### `Persistence/PatientDbContext.cs`
EF Core `DbContext` with constructor injection of `AuditInterceptor` and `OutboxPublishingInterceptor`.

DbSets: `Patients`, `OutboxMessages`

#### `Persistence/Configurations/PatientConfiguration.cs`
- Table: `Patients`
- PK: `Id`
- `ContactEmail` indexed (for `ExistsByEmailAsync` performance)
- All string lengths capped

#### `Persistence/Interceptors/AuditInterceptor.cs`
`SaveChangesInterceptor` — before save, sets `CreatedAt`/`CreatedBy` on new entities and `ModifiedAt`/`ModifiedBy` on modified entities. Reads `ICurrentUserService.UserId` for the `By` columns.

#### `Persistence/Interceptors/OutboxPublishingInterceptor.cs`
`SaveChangesInterceptor` — after detecting entities that inherit `AggregateRoot`, reads `DomainEvents`, serialises each to JSON, writes an `OutboxMessage` row in the same transaction, then calls `ClearDomainEvents`. This guarantees atomicity: either the entity is saved **and** the outbox row exists, or neither does.

#### `Persistence/Repositories/PatientRepository.cs`
EF Core implementation of `IPatientRepository`. Uses `FirstOrDefaultAsync` for reads, `AddAsync` for inserts, delegates `SaveChanges` to the `DbContext`.

#### `Clients/IdentityProvisioningClient.cs`
`IIdentityProvisioningService` implementation. Issues a `client_credentials` token to IdentityServer (`api-gateway` client), then calls the IdentityServer admin API to create the user account. Protected by `AddHealthBookingResiliencePipeline`.

#### `Services/CurrentUserService.cs`
`ICurrentUserService` implementation. Reads `ClaimTypes.NameIdentifier` from `IHttpContextAccessor.HttpContext.User`.

### 6.4 API Layer (`PatientService.API`)

#### `Endpoints/PatientsEndpoints.cs`

| Method | Path | Auth | Handler |
|---|---|---|---|
| `POST` | `/api/patients/register` | Anonymous | `RegisterPatientCommand` via MediatR |
| `GET` | `/api/patients/{id}` | JWT | `GetPatientByIdQuery` |
| `PUT` | `/api/patients/{id}` | JWT | `UpdatePatientProfileCommand` |
| `GET` | `/api/patients/me` | JWT | `GetPatientByEmailQuery` using email claim |

#### `Grpc/PatientGrpcService.cs`
Implements `PatientGrpc.PatientGrpcBase`. `GetPatientById` — loads patient from DB, returns `PatientResponse`. Used internally by AppointmentService and NotificationService.

#### `Program.cs` (registration order)
```
Serilog → MediatR (+ SharedKernel behaviors) → FluentValidation
→ EF Core (SqlServer, migrations from Infrastructure)
→ AuditInterceptor, OutboxPublishingInterceptor, CurrentUserService
→ PatientRepository, IdentityProvisioningClient (+ resilience pipeline)
→ gRPC services
→ OpenTelemetry (AddHealthBookingTelemetry)
→ Health checks (/health/ready, /health/live)
→ Minimal API endpoints
```

---

## 7. ProviderService

Path: `src/Services/ProviderService/`

**Responsibility:** Provider (doctor/specialist) registration, daily availability definition, slot management. Exposes REST endpoints and a gRPC server. Caches slot lists in Redis.

### 7.1 Domain Layer (`ProviderService.Domain`)

#### `Entities/Provider.cs`
Aggregate root with navigation property `Slots` (`List<AvailabilitySlot>`).

Factory: `Provider.Register(firstName, lastName, specialty, licenseNumber)`
- Validates `SpecialtyName` value object
- Raises `ProviderRegisteredEvent`

Method: `DefineDailyAvailability(DateOnly date, TimeOnly start, TimeOnly end)`
- Generates 30-minute `AvailabilitySlot` intervals
- Guards against overlapping slots on the same date
- Raises `AvailabilityDefinedEvent`

#### `Entities/AvailabilitySlot.cs`
Not a standalone aggregate — it is a **child entity** inside `Provider`. Factory: `AvailabilitySlot.Create(providerId, date, startTime)` is `internal` (only callable by `Provider`).

Properties: `Id`, `ProviderId`, `Date` (DateOnly), `StartTime`, `EndTime`, `DurationMinutes` (30), `Status` (SlotStatus enum), `AppointmentId?` (set when locked), `RowVersion` (optimistic concurrency byte[]).

#### `Enums/SlotStatus.cs`
`Available` → `Locked` → `Booked` / `Cancelled`

#### `ValueObjects/ProviderValueObjects.cs`
`SpecialtyName` — validates that the specialty string is non-empty and within 100 characters.

### 7.2 Application Layer (`ProviderService.Application`)

#### `Commands/RegisterProvider/RegisterProviderCommand.cs`
Calls `Provider.Register(...)`, saves, returns `RegisterProviderResult(ProviderId)`.

#### `Commands/DefineAvailability/DefineAvailabilityCommand.cs`
Loads provider, calls `DefineDailyAvailability(...)`, saves. Returns created slot count.

#### `Queries/GetProviderSlots/GetProviderSlotsQuery.cs`
Checks Redis cache (`slots:{providerId}`) before hitting DB. Cache TTL: 60 seconds. Cache key is evicted by `ProviderGrpcService` after every `LockSlot` / `ReleaseSlot` mutation.

#### `Interfaces/IProviderInterfaces.cs`
- `IProviderRepository` — `GetByIdAsync`, `AddAsync`, `SaveChangesAsync`
- `ISlotRepository` — `GetAvailableSlotsAsync(providerId)`, `GetByIdAsync(slotId)`, `SaveChangesAsync`
- `ICacheService` — `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`
- `ICurrentUserService`

### 7.3 Infrastructure Layer (`ProviderService.Infrastructure`)

#### `Persistence/ProviderDbContext.cs`
DbSets: `Providers`, `AvailabilitySlots`, `OutboxMessages`. Same interceptor pattern as PatientService.

#### `Persistence/Configurations/ProviderConfiguration.cs`
- Table: `Providers`; `AvailabilitySlots` table with FK to `Providers`
- `RowVersion` column on `AvailabilitySlot` mapped as concurrency token
- Owned-type or flat mapping for slot navigation

#### `Services/RedisCacheService.cs`
`ICacheService` implementation using `IConnectionMultiplexer` (StackExchange.Redis). `GetAsync<T>` deserialises JSON; `SetAsync<T>` serialises and sets TTL; `RemoveAsync` deletes key.

### 7.4 API Layer (`ProviderService.API`)

#### `Endpoints/ProvidersEndpoints.cs`

| Method | Path | Auth | Handler |
|---|---|---|---|
| `POST` | `/api/providers/register` | JWT | `RegisterProviderCommand` |
| `GET` | `/api/providers/{id}` | JWT | `GetProviderByIdQuery` |
| `GET` | `/api/providers/{id}/slots` | JWT | `GetProviderSlotsQuery` (Redis cached) |
| `POST` | `/api/providers/{id}/availability` | JWT | `DefineAvailabilityCommand` |

#### `Grpc/ProviderGrpcService.cs`
Implements `ProviderGrpc.ProviderGrpcBase`:
- `GetSlotById` — returns slot details
- `LockSlot(slotId, appointmentId)` — sets `slot.Status = Locked`, saves (optimistic concurrency on `RowVersion`), **evicts Redis cache** (`slots:{providerId}`)
- `ReleaseSlot(slotId)` — reverts slot to `Available`, **evicts Redis cache**

**Why cache eviction on mutation?** Without it, a 60-second TTL means one booking can follow another using a slot that is already locked. Immediate eviction ensures the next `GetProviderSlots` call fetches fresh data.

---

## 8. AppointmentService

Path: `src/Services/AppointmentService/`

The most complex service. Orchestrates the booking flow via a **MassTransit Saga** and manages the appointment lifecycle.

### 8.1 Domain Layer (`AppointmentService.Domain`)

#### `Entities/Appointment.cs`
Factory: `Appointment.Book(patientId, slotId, patientName)` — raises `AppointmentBookedEvent`.  
Method: `Cancel(reason, cancelledBy)` — sets status, raises `AppointmentCancelledEvent`.

Properties: `Id`, `PatientId`, `SlotId`, `PatientName`, `Status` (AppointmentStatus), `CancellationReason?`, `CancelledBy?`

#### `Entities/BookingIdempotencyKey.cs`
Flat entity (no domain logic). Properties: `Key` (unique string), `AppointmentId` (FK). Enforces at-most-once booking for a given client-supplied key.

#### `Enums/AppointmentStatus.cs`
`Booked` → `Cancelled` / `Completed`

### 8.2 Application Layer — Commands, Queries

#### `Commands/CancelAppointment/CancelAppointmentCommand.cs`
Loads appointment, calls `appointment.Cancel(reason, callerId)`, saves. The `OutboxPublishingInterceptor` captures `AppointmentCancelledEvent`.

#### `Queries/GetAppointmentById/`, `Queries/GetPatientAppointments/`
Read-only queries returning `AppointmentDto` or list.

### 8.3 Application Layer — Booking Saga

#### `Saga/BookingState.cs`
EF Core entity, persisted to `BookingSagaStates` table. Stores:
`CorrelationId`, `CurrentState`, `PatientId`, `SlotId`, `IdempotencyKey`, `AppointmentId?`, `PatientName?`, `SlotWasLocked` (bool), `FailureReason?`, `CreatedAt`

`SlotWasLocked` is the compensation flag — `LockSlotActivity.Faulted` checks it before calling `ReleaseSlot`.

#### `Saga/BookingStateMachine.cs`
```
Initial ──[V1_InitiateBookingCommand]──▶ Submitted
           │
           ├── VerifyPatientActivity  (gRPC → PatientService)
           ├── LockSlotActivity       (gRPC → ProviderService, sets SlotWasLocked)
           ├── PersistAppointmentActivity (DB write + idempotency key)
           │
           ├── On success ──▶ Respond(V1_BookingCompletedEvent) ──▶ Completed
           └── On exception ──▶ LockSlotActivity.Faulted compensates
                            ──▶ Respond(V1_BookingFailedEvent)  ──▶ Failed
```

`SetCompletedWhenFinalized()` removes `Completed`/`Failed` rows from the saga table, keeping it lean.

#### `Saga/Activities/VerifyPatientActivity.cs`
- `Execute`: gRPC `GetPatientById` → stores `saga.PatientName`
- `Faulted`: no-op (read-only, nothing to undo)

#### `Saga/Activities/LockSlotActivity.cs`
- `Execute`: gRPC `LockSlot(slotId, correlationId)` → sets `saga.SlotWasLocked = true`
- `Faulted`: if `SlotWasLocked`, calls gRPC `ReleaseSlot`

#### `Saga/Activities/PersistAppointmentActivity.cs`
- `Execute`: re-checks idempotency key in DB; if new → creates `Appointment.Book(...)`, saves, writes `BookingIdempotencyKey`; if duplicate → reuses existing `AppointmentId`
- `Faulted`: no-op (slot already released upstream)

### 8.4 Infrastructure Layer (`AppointmentService.Infrastructure`)

#### `BackgroundServices/OutboxProcessor.cs`
`IHostedService` (BackgroundService). Polls `OutboxMessages` every **5 seconds**, batch size **20**.

For each `Pending` message:
1. Deserialises `Payload` as the type named in `EventType`
2. Publishes to RabbitMQ via `IBus.Publish` (MassTransit routing by message type)
3. Sets `Status = Published`, `PublishedAt = UtcNow`
4. On failure, increments `RetryCount`; after 5 retries, sets `Status = Failed`

**Why Outbox?** Without it, if the service crashes after saving the appointment but before publishing to RabbitMQ, the event is lost. The Outbox and the DB write happen in the **same transaction** — the processor retries until the message is delivered.

#### `Clients/PatientGrpcClient.cs`
Wraps `PatientGrpc.PatientGrpcClient`. Used by `VerifyPatientActivity`.

#### `Clients/ProviderSlotGrpcClient.cs`
Wraps `ProviderGrpc.ProviderGrpcClient` for `LockSlot` and `ReleaseSlot`. Used by `LockSlotActivity`.

#### `Persistence/Configurations/BookingStateConfiguration.cs`
EF Core configuration for the saga state table. Unique index on `IdempotencyKey`. `ConcurrencyMode = Optimistic` (MassTransit EF Core).

### 8.5 API Layer (`AppointmentService.API`)

#### `Endpoints/AppointmentsEndpoints.cs`

| Method | Path | Auth | Behaviour |
|---|---|---|---|
| `POST` | `/api/appointments` | JWT | Idempotency pre-check → publish saga → await response (30 s) |
| `GET` | `/api/appointments/{id}` | JWT | `GetAppointmentByIdQuery` |
| `DELETE` | `/api/appointments/{id}` | JWT | `CancelAppointmentCommand` |
| `GET` | `/api/appointments/patient/{patientId}` | JWT | `GetPatientAppointmentsQuery` |

**Booking `POST` flow detail:**
1. Extract `patientId` from JWT claim
2. Read `Idempotency-Key` header (required)
3. Fast path: if key already in DB, return existing appointment immediately
4. Publish `V1_InitiateBookingCommand` via `IRequestClient<T>` (MassTransit request/response)
5. Await `V1_BookingCompletedEvent` or `V1_BookingFailedEvent` within 30 seconds
6. `Completed` → `201 Created`; `Failed` → `409 Conflict`; timeout → `503`

---

## 9. NotificationService

Path: `src/Services/NotificationService/`

**Responsibility:** Listen for appointment events on RabbitMQ and send email notifications to patients.

### 9.1 Domain Layer (`NotificationService.Domain`)

#### `Entities/NotificationLog.cs`
Records every notification attempt: `Id`, `AppointmentId`, `PatientId`, `RecipientEmail`, `NotificationType` (`Booked` / `Cancelled`), `Status` (`Sent` / `Failed`), `SentAt?`, `ErrorMessage?`

### 9.2 Application Layer (`NotificationService.Application`)

#### `Consumers/AppointmentBookedConsumer.cs`
MassTransit `IConsumer<V1_AppointmentBookedEvent>`:
1. Calls `IPatientEmailClient.GetPatientEmailAsync(patientId)` (gRPC to PatientService)
2. Falls back to `patient-{id}@placeholder.local` if gRPC returns null
3. Calls `IEmailService.SendBookingConfirmationAsync(email, appointmentId, scheduledTime)`
4. Saves `NotificationLog` entry with result

#### `Consumers/AppointmentCancelledConsumer.cs`
Same pattern, sends cancellation email.

#### `Interfaces/INotificationInterfaces.cs`
- `IEmailService` — `SendBookingConfirmationAsync`, `SendCancellationNotificationAsync`
- `IPatientEmailClient` — `GetPatientEmailAsync(Guid patientId, CancellationToken ct) → Task<string?>`
- `INotificationLogRepository`

### 9.3 Infrastructure Layer (`NotificationService.Infrastructure`)

#### `Clients/NotificationPatientGrpcClient.cs`
`IPatientEmailClient` implementation. Makes gRPC `GetPatientById` call to PatientService. On `RpcException` with `StatusCode.NotFound`, returns `null`. Protected by `AddHealthBookingResiliencePipeline`.

#### `Email/LoggingEmailService.cs`
`IEmailService` implementation for development. Logs the email to the console instead of actually sending. Replace with SendGrid / SMTP implementation for production.

#### `Persistence/NotificationDbContext.cs`
DbSet: `NotificationLogs`. No outbox (this service only reads events; it doesn't raise domain events).

---

## 10. Infrastructure Patterns (cross-cutting)

### The Clean Architecture Layer Rule

```
Domain → Application → Infrastructure → API
  ↑           ↑            ↑
  └── No reference to anything further right
```

- `Domain` has zero external dependencies
- `Application` depends only on `Domain` + abstractions (interfaces)
- `Infrastructure` implements those interfaces
- `API` wires everything together in `Program.cs`

### Database Per Service

Each microservice owns its own SQL Server database. No cross-database joins. Cross-service reads go through APIs or gRPC calls.

| Service | Database |
|---|---|
| PatientService | `healthbooking_patients` |
| ProviderService | `healthbooking_providers` |
| AppointmentService | `healthbooking_appointments` |
| NotificationService | `healthbooking_notifications` |
| IdentityServer | `healthbooking_identity` |

### Migrations

Each service's `Infrastructure` project holds its own EF Core migrations. Applied via `Database.MigrateAsync()` at startup (acceptable for development; in production, run migrations separately in CI).

### Request Validation Pipeline

Every inbound MediatR request passes through:
1. `LoggingBehavior` — logs request name and parameters
2. `ValidationBehavior` — runs all FluentValidation validators
3. `PerformanceBehavior` — warns if handler > 500 ms
4. **Handler**

### Transaction Guarantee for Events (Outbox Pattern)

```
HTTP Request
    │
    ▼
Save Entity + OutboxMessage (same DB transaction)
    │
    ▼ (5-second poll)
OutboxProcessor reads OutboxMessages WHERE Status='Pending'
    │
    ▼
Publish to RabbitMQ via MassTransit (IBus.Publish)
    │
    ▼
Update OutboxMessage Status='Published'
```

If the process crashes between saving and publishing, the next startup will re-process the `Pending` messages. If RabbitMQ is temporarily unavailable, `RetryCount` increments; after 5 failures the message is `Status='Failed'` for investigation.

---

## 11. API Endpoint Reference

### Public Endpoints (via API Gateway at :5000)

#### Patient Endpoints

| Method | Path | Auth | Request Body | Response |
|---|---|---|---|---|
| `POST` | `/api/patients/register` | - | `{firstName, lastName, email, phoneNumber, dateOfBirth}` | `201 {patientId}` |
| `GET` | `/api/patients/{id}` | JWT | - | `200 PatientDto` / `404` |
| `PUT` | `/api/patients/{id}` | JWT | `{firstName, lastName, phoneNumber}` | `204` |
| `GET` | `/api/patients/me` | JWT | - | `200 PatientDto` |

#### Provider Endpoints

| Method | Path | Auth | Request Body | Response |
|---|---|---|---|---|
| `POST` | `/api/providers/register` | JWT | `{firstName, lastName, specialty, licenseNumber}` | `201 {providerId}` |
| `GET` | `/api/providers/{id}` | JWT | - | `200 ProviderDto` / `404` |
| `GET` | `/api/providers/{id}/slots` | JWT | - | `200 SlotDto[]` (Redis cached) |
| `POST` | `/api/providers/{id}/availability` | JWT | `{date, startTime, endTime}` | `200 {slotsCreated}` |

#### Appointment Endpoints

| Method | Path | Auth | Headers | Request Body | Response |
|---|---|---|---|---|---|
| `POST` | `/api/appointments` | JWT | `Idempotency-Key: <uuid>` | `{slotId}` | `201 {appointmentId}` / `409` / `503` |
| `GET` | `/api/appointments/{id}` | JWT | - | - | `200 AppointmentDto` / `404` |
| `DELETE` | `/api/appointments/{id}` | JWT | - | `{reason}` | `204` |
| `GET` | `/api/appointments/patient/{patientId}` | JWT | - | - | `200 AppointmentDto[]` |

#### Auth (IdentityServer at :5005)

| Grant Type | Endpoint | Parameters | Used by |
|---|---|---|---|
| Resource Owner Password | `/connect/token` | `grant_type=password&client_id=patient-spa&client_secret=...&username=&password=&scope=openid profile email healthbooking-api` | Angular SPA login |
| Client Credentials | `/connect/token` | `grant_type=client_credentials&client_id=api-gateway&client_secret=...` | M2M service calls |
| Discovery | `/.well-known/openid-configuration` | — | JWT validation config |

---

## 12. API Request Flows

### Flow A: Patient Registration

```
Angular SPA
    │
    ▼  POST /api/patients/register (no auth required)
API Gateway (:5000)
    │  YARP forwards to patient-cluster (no auth check for this route)
    ▼
PatientService (:5001)
    ├── ValidationBehavior → RegisterPatientCommandValidator
    ├── RegisterPatientCommandHandler
    │       ├── IPatientRepository.ExistsByEmailAsync → No duplicate
    │       ├── Patient.Register(...) → raises PatientRegisteredEvent
    │       ├── IPatientRepository.AddAsync + SaveChangesAsync
    │       │       ├── AuditInterceptor → sets CreatedAt/CreatedBy
    │       │       └── OutboxPublishingInterceptor → writes OutboxMessage
    │       └── IIdentityProvisioningService.ProvisionUserAsync
    │               └── HTTP POST to IdentityServer admin API
    └── Returns 201 {patientId}
```

### Flow B: JWT Login (Angular SPA)

```
Angular SPA
    │
    ▼  POST http://identity-server:5005/connect/token
    │  grant_type=password, client_id=patient-spa, username/password
IdentityServer
    │  Validates credentials (IdentityDbContext)
    ▼
Returns {access_token, refresh_token, expires_in}
    │
Angular stores token in memory / HttpOnly cookie
```

### Flow C: Book Appointment (Full Saga Flow)

```
Angular SPA
    │  POST /api/appointments
    │  Authorization: Bearer <jwt>
    │  Idempotency-Key: <uuid>
    │  Body: {slotId}
    ▼
API Gateway (:5000)
    ├── JWT validation (validates with IdentityServer)
    ├── Rate limiter check (sliding window)
    ├── CorrelationIdMiddleware (add X-Correlation-Id)
    └── YARP forward to appointment-cluster
    ▼
AppointmentService (:5003)
    ├── Idempotency pre-check (fast path: return existing appt if key found)
    ├── Publish V1_InitiateBookingCommand (RabbitMQ, request/response with 30s timeout)
    │         │
    │         ▼  (MassTransit routes to BookingStateMachine)
    │   BookingStateMachine
    │         ├── VerifyPatientActivity
    │         │       └── gRPC GetPatientById → PatientService (:5001)
    │         │               Returns PatientName, stores in saga state
    │         ├── LockSlotActivity
    │         │       └── gRPC LockSlot(slotId, correlationId) → ProviderService (:5002)
    │         │               Slot.Status = Locked, RowVersion check (optimistic concurrency)
    │         │               Redis cache evicted: slots:{providerId}
    │         ├── PersistAppointmentActivity
    │         │       ├── Re-checks idempotency key in DB
    │         │       ├── Appointment.Book(patientId, slotId, patientName)
    │         │       │       └── Raises AppointmentBookedEvent
    │         │       ├── SaveChangesAsync
    │         │       │       ├── AuditInterceptor
    │         │       │       └── OutboxPublishingInterceptor
    │         │       └── Writes BookingIdempotencyKey row
    │         └── Responds V1_BookingCompletedEvent
    │
    └── Returns 201 {appointmentId}
```

### Flow D: Notification After Booking

```
OutboxProcessor (background, every 5 s)
    │  Queries OutboxMessages WHERE Status='Pending'
    ▼
Deserialise AppointmentBookedEvent
    │
    ▼  MassTransit IBus.Publish → RabbitMQ exchange
    │
NotificationService (:5004) - AppointmentBookedConsumer
    ├── IPatientEmailClient.GetPatientEmailAsync(patientId)
    │       └── gRPC GetPatientById → PatientService (:5001)
    │               Protected by Polly (retry + circuit breaker)
    ├── IEmailService.SendBookingConfirmationAsync(email, ...)
    │       └── (dev: logs to console; prod: SendGrid/SMTP)
    └── NotificationLogRepository.Save(NotificationLog {Status=Sent})
```

### Flow E: Cancel Appointment

```
Angular / Client
    │  DELETE /api/appointments/{id}
    │  Body: {reason: "schedule conflict"}
    ▼
API Gateway → AppointmentService
    │
AppointmentsEndpoints.CancelAppointment
    │
    ▼  CancelAppointmentCommand
    ├── GetByIdAsync (loads appointment)
    ├── appointment.Cancel(reason, callerId)
    │       └── Raises AppointmentCancelledEvent
    └── SaveChangesAsync
            ├── AuditInterceptor (ModifiedAt/By)
            └── OutboxPublishingInterceptor → OutboxMessage
                    └── (async) → NotificationService sends cancellation email
```

### Flow F: Provider Slot Query (with Redis Cache)

```
Angular (Provider Dashboard)
    │  GET /api/providers/{id}/slots
    ▼
API Gateway → ProviderService
    │
GetProviderSlotsQueryHandler
    │  Check Redis: GET slots:{providerId}
    ├── Cache HIT → deserialise and return immediately (< 1 ms)
    └── Cache MISS
            ├── DB query: AvailabilitySlots WHERE ProviderId = ? AND Status = Available
            ├── Redis: SET slots:{providerId} TTL=60s
            └── Return list
```

---

## 13. Docker Compose & Local Dev

### Full stack (`docker-compose.yml`)

**Infrastructure containers (pre-existing):**

| Container | Image | Purpose |
|---|---|---|
| `sqlserver-identity` | sql2022 | IdentityServer DB |
| `sqlserver-patient` | sql2022 | PatientService DB |
| `sqlserver-provider` | sql2022 | ProviderService DB |
| `sqlserver-appointment` | sql2022 | AppointmentService DB |
| `sqlserver-notification` | sql2022 | NotificationService DB |
| `rabbitmq` | rabbitmq:3-management | Message broker |
| `redis` | redis:7-alpine | Cache |
| `jaeger` | jaegertracing/all-in-one | Trace backend |

**Application containers (added Week 6):**

Each built from the service's `Dockerfile` (multi-stage: `sdk:9.0 → aspnet:9.0`).

| Container | Port | Depends On (healthy) |
|---|---|---|
| `identity-server` | 5005 | sqlserver-identity |
| `patient-service` | 5001 | sqlserver-patient, rabbitmq, identity-server |
| `provider-service` | 5002 | sqlserver-provider, rabbitmq, redis, identity-server |
| `appointment-service` | 5003 | sqlserver-appointment, rabbitmq, patient-service, provider-service |
| `notification-service` | 5004 | sqlserver-notification, rabbitmq, patient-service |
| `api-gateway` | 5000 | all four microservices |

### Starting the stack

```bash
cp .env.example .env        # fill in any overrides
docker compose build
docker compose up -d
docker compose logs -f      # watch logs
```

### Health check endpoints

| URL | Meaning |
|---|---|
| `http://localhost:5000/health/ready` | Gateway + all clusters healthy |
| `http://localhost:5001/health/ready` | PatientService + DB healthy |
| `http://localhost:16686` | Jaeger trace UI |
| `http://localhost:15672` | RabbitMQ Management (guest/guest) |

---

## 14. OpenTelemetry & Observability

### Instrumentation Points per span

| Instrumentation | Span examples |
|---|---|
| ASP.NET Core | `POST /api/appointments`, `GET /api/patients/{id}` |
| HttpClient | gRPC calls between services (`grpc.POST patient/PatientGrpc/GetPatientById`) |
| MassTransit | `send BookingInitiated`, `consume V1_AppointmentBookedEvent` |

All health check paths (`/health/*`) are excluded from tracing to reduce noise.

### Finding a trace in Jaeger

1. Open http://localhost:16686
2. Select service: `appointment-service`
3. Find a `POST /api/appointments` trace
4. Expand child spans to see: `LockSlot` gRPC call to `provider-service`, `GetPatientById` to `patient-service`, DB `SaveChanges`, outbox write

### Configuration

```json
// appsettings.json (default for dotnet run)
"OtelExporter": { "Endpoint": "http://localhost:4317" }

// docker-compose.yml (production-local)
OtelExporter__Endpoint: "http://jaeger:4317"
```

---

## 15. Testing Strategy

### Unit Tests (no I/O — all dependencies mocked)

| Project | Coverage focus |
|---|---|
| `PatientService.UnitTests` | `RegisterPatient` handler; domain entity; value objects |
| `ProviderService.UnitTests` | `RegisterProvider` handler; `DefineDailyAvailability` domain logic |
| `AppointmentService.UnitTests` | Appointment aggregate; saga activities (all 3, happy + error + compensation); `BookAppointmentCommandHandler` |
| `NotificationService.UnitTests` | `AppointmentBookedConsumer` (real email, gRPC null fallback) |

**Mocking pattern:** NSubstitute (`Substitute.For<IRepository>()`), Bogus for fake data, FluentAssertions for readable assertions.

### Integration Tests (real SQL Server via Testcontainers)

| Project | What it exercises |
|---|---|
| `PatientService.IntegrationTests` | EF Core migrations, `SaveChanges` + outbox, JWT claim smoke tests |
| `ProviderService.IntegrationTests` | Provider persist, slot generation, outbox |
| `AppointmentService.IntegrationTests` | Appointment persist, idempotency key constraint, outbox |
| `NotificationService.IntegrationTests` | NotificationLog persistence |

Each test class:
1. Creates a `MsSqlContainer` (isolated DB per class)
2. Calls `Database.MigrateAsync()` to apply all migrations
3. Uses `TestDbContextFactory.Create(connectionString)` with the real interceptor chain
4. Disposes the container after the class completes

### Running tests

```bash
# Unit tests only (fast, no Docker required)
dotnet test HealthBooking.sln --filter "FullyQualifiedName!~IntegrationTests"

# All tests (requires Docker for Testcontainers)
dotnet test HealthBooking.sln

# Single service
dotnet test tests/AppointmentService.UnitTests
```

### Test counts (Week 6 state)

| Assembly | Pass | Fail |
|---|---|---|
| AppointmentService.UnitTests | 21 | 0 |
| NotificationService.UnitTests | 4 | 0 |
| ProviderService.UnitTests | 10 | 0 |
| PatientService.UnitTests | 20 | 0 |

---

## 16. CI/CD Pipeline

File: `.github/workflows/ci.yml`

**Triggers:** Push to any branch, PR to `master` or `001-distributed-healthcare-system`.

**Steps:**
1. Checkout
2. Setup .NET 9
3. `dotnet restore`
4. `dotnet format --verify-no-changes` (enforces consistent formatting)
5. `dotnet build --configuration Release`
6. `dotnet test --collect:"XPlat Code Coverage"`
7. Upload coverage artifact

**Note:** Integration tests run in CI against Testcontainers (the GitHub Actions runner has Docker available). `RYUK_DISABLED=true` is not required on ubuntu runners.

---

## 17. Week-by-Week Build Log

### Week 0 — Solution Scaffold
- Created `HealthBooking.sln`
- Created project structure: 4 services × 4 layers + SharedKernel + Contracts + Gateway + IdentityServer
- Added `global.json` (pinned SDK 9.x), `.editorconfig`, `.gitignore`
- Empty `Class1.cs` stubs in every project — no logic yet

### Week 1 — Foundation
- `AuditableEntity`, `AggregateRoot`, `IDomainEvent` in SharedKernel
- `OutboxMessage`, `OutboxMessageConfiguration` in SharedKernel.Persistence
- MediatR behaviors: `ValidationBehavior`, `LoggingBehavior`, `PerformanceBehavior`
- Duende IdentityServer scaffolded: `Config.cs`, `SeedData.cs`, `Program.cs`
- EF Core DbContexts created for all 4 services (with interceptors)
- Initial migrations run; SQL Server Testcontainers added to all integration test projects

### Week 2 — Core Domain
- `Patient` aggregate with `Register`, `UpdateProfile`, value objects (`Email`, `FullName`, `PhoneNumber`)
- `Provider` aggregate with `Register`, `DefineDailyAvailability`, `AvailabilitySlot` child entity, `SpecialtyName`
- Domain events for patient and provider operations
- CQRS: `RegisterPatient`, `UpdatePatientProfile`, `RegisterProvider`, `DefineAvailability` commands
- Queries: `GetPatientById`, `GetPatientByEmail`, `GetProviderById`, `GetProviderSlots`
- PatientService and ProviderService REST endpoints (Minimal API)
- PatientGrpcService (server) + PatientGrpcClient (client in AppointmentService)
- Integration tests: `PatientPersistenceTests`, EF migrations applied and round-trip tested

### Week 3 — Scheduling
- `Appointment` aggregate with `Book`, `Cancel`; `BookingIdempotencyKey` entity
- `AppointmentsEndpoints` (REST)
- ProviderGrpcService (server) with `LockSlot`, `ReleaseSlot`
- ProviderSlotGrpcClient in AppointmentService
- `GetProviderSlotsQueryHandler` with Redis caching (`RedisCacheService`)
- `BookAppointmentCommandHandler` (synchronous, replaced in Week 5)
- `proto3` definitions in `HealthBooking.Contracts/Protos`

### Week 4 — Integration (NotificationService + YARP)
- NotificationService consumers: `AppointmentBookedConsumer`, `AppointmentCancelledConsumer`
- MassTransit configured on all services (RabbitMQ transport)
- `OutboxProcessor` background service in AppointmentService
- `V1_AppointmentBookedEvent`, `V1_AppointmentCancelledEvent` in Contracts
- YARP API Gateway: JWT auth, rate limiting, route table, `CorrelationIdMiddleware`
- Health checks on all services (`/health/ready`, `/health/live`)

### Week 5 — Resilience
- Polly resilience pipeline (`ResilienceExtensions`) at SharedKernel level
- MassTransit Booking Saga replacing synchronous command handler
  - `BookingStateMachine`, `BookingState`, 3 activities, compensation
  - `V1_InitiateBookingCommand`, `V1_BookingCompletedEvent`, `V1_BookingFailedEvent`
  - `BookingSagaStates` EF Core table + migration `AddBookingSaga`
- `IRequestClient<T>` request/response pattern in AppointmentService endpoint
- NotificationService real gRPC patient email lookup (`NotificationPatientGrpcClient`)
- Redis cache eviction in `ProviderGrpcService.LockSlot` / `ReleaseSlot`

### Week 6 — Observability
- OpenTelemetry distributed tracing added to all services
  - 4 NuGet packages in SharedKernel
  - `TelemetryExtensions.AddHealthBookingTelemetry` shared extension
  - ASP.NET Core + HttpClient + MassTransit instrumentation
  - OTLP gRPC export to Jaeger
- 6 application service containers added to `docker-compose.yml`
  - All use `depends_on: condition: service_healthy`
- `ProviderService.IntegrationTests` — `TestDbContextFactory` + 3 persistence tests
- `PatientService.UnitTests` — email normalisation assertion fix
