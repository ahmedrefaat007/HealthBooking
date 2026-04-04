# Week 1 Foundation — Complete Technical Documentation

**Branch**: `001-w1-foundation`  
**Tasks Completed**: T001–T020  
**Build Status**: ✅ `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Project Structure Added This Week](#project-structure)
3. [T001–T005: Scaffolding & Config Files](#t001-t005-scaffolding)
4. [T006: SharedKernel Base Types](#t006-sharedkernel)
5. [T007: Contracts Library](#t007-contracts)
6. [T008: Proto Files (gRPC)](#t008-proto-files)
7. [T009–T010: MediatR Behaviors + Polly Resilience](#t009-t010-behaviors)
8. [T011: Service Project Scaffolding (16 Projects)](#t011-service-projects)
9. [T012: Test Projects (8 Projects)](#t012-test-projects)
10. [T013–T014: IdentityServer (Duende)](#t013-t014-identityserver)
11. [T015–T016: API Gateway (YARP)](#t015-t016-apigateway)
12. [T017–T018: Docker Compose](#t017-t018-docker)
13. [T019: CI Workflow](#t019-ci)
14. [T020: Auth Smoke Tests](#t020-auth-smoke)
15. [Design Decisions & Best Practices](#design-decisions)
16. [How to Run Locally](#how-to-run)
17. [Week 2 Preview](#week-2-preview)

---

## 1. Architecture Overview {#architecture-overview}

```
┌─────────────────────────────────────────────────────────┐
│  Client (Browser / Mobile App)                          │
└───────────────┬─────────────────────────────────────────┘
                │ HTTP (JWT Bearer)
┌───────────────▼─────────────────────────────────────────┐
│  API Gateway  :5000  (YARP + Rate Limiter)              │
│  - JWT validation against IdentityServer               │
│  - Sliding-window rate limiter (300 req/min)           │
│  - CorrelationIdMiddleware injects X-Correlation-Id    │
│  - Active health checks every 10s on all clusters      │
└──┬────────────┬──────────────┬──────────────────────────┘
   │            │              │
   ▼            ▼              ▼
PatientSvc  ProviderSvc  AppointmentSvc  NotificationSvc
  :5001       :5002          :5003           :5004
   │            │              │
   └────────gRPC ──────────────┘
                │
         Infrastructure
     (SQL Server × 4, RabbitMQ, Redis, Jaeger)
```

### Core Principles

| Principle | Implementation |
|---|---|
| Clean Architecture | Domain → Application → Infrastructure → API (strict reference flow) |
| CQRS | MediatR commands (mutate state) + queries (read state) |
| Domain Events | `AggregateRoot.AddDomainEvent()` → published via Outbox |
| Outbox Pattern | `OutboxMessage` table written in same transaction as domain change |
| Idempotency | `BookingIdempotencyKey` table; consumers check `ProcessedEvents` |
| Resilience | Polly: timeout 10s, retry 3× exponential, circuit breaker 50%/30s break |
| Observability | OpenTelemetry → Jaeger; Serilog structured logging; CorrelationId propagation |
| Security | JWT Bearer (IdentityServer 7); scoped permissions per service |

---

## 2. Project Structure Added This Week {#project-structure}

```
HealthBooking.sln                         ← 28 projects (26 + IdServer + Gateway)
├── src/
│   ├── SharedKernel/
│   │   ├── HealthBooking.SharedKernel/   ← T006: base types, behaviors, resilience
│   │   └── HealthBooking.Contracts/      ← T007-T008: events + proto files
│   ├── IdentityServer/
│   │   └── HealthBooking.IdentityServer/ ← T013-T014: Duende IdentityServer 7
│   ├── ApiGateway/
│   │   └── HealthBooking.ApiGateway/     ← T015-T016: YARP reverse proxy
│   └── Services/
│       ├── PatientService/               ← T011: .Domain .Application .Infrastructure .API
│       ├── ProviderService/              ← T011: same 4 layers
│       ├── AppointmentService/           ← T011: same 4 layers
│       └── NotificationService/          ← T011: same 4 layers
├── tests/
│   ├── PatientService.UnitTests/         ← T012 + T020: auth smoke tests
│   ├── PatientService.IntegrationTests/
│   ├── ProviderService.UnitTests/
│   ├── ProviderService.IntegrationTests/
│   ├── AppointmentService.UnitTests/
│   ├── AppointmentService.IntegrationTests/
│   ├── NotificationService.UnitTests/
│   └── NotificationService.IntegrationTests/
├── docker-compose.yml                    ← T017: infrastructure services
├── docker-compose.override.yml           ← T018: application services
├── .github/workflows/ci.yml             ← T019: GitHub Actions CI
└── docs/
    └── 001-w1-foundation.md              ← this file
```

---

## 3. T001–T005: Scaffolding & Config Files {#t001-t005-scaffolding}

### What was created

| File | Purpose |
|---|---|
| `HealthBooking.sln` | MSBuild solution aggregating all 28 projects |
| `Directory.Build.props` | Enforces `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=12` |
| `.editorconfig` | 4-space indent, 120-char max line, C# naming conventions |
| `.gitignore` | Excludes `.env`, `bin/`, `obj/`, `*.user` |
| `.env.example` | Template for `SQL_SA_PASSWORD`, `RABBITMQ_USER`, `RABBITMQ_PASS`, `REDIS_PASSWORD`, `IDENTITY_DB_CONN` |

### Why `Directory.Build.props` matters

`TreatWarningsAsErrors=true` means every compiler warning fails the build. This enforces:
- Nullable reference type annotations (no undeclared null)
- No unused variables or imports
- Consistent code quality across 28 projects with a single file

### Why `LangVersion=12`

Enables:
- **Collection expressions** `[]` instead of `new List<> {}`
- **Primary constructors** `class Foo(ILogger logger)` — used in behaviors
- **Required members** — enforces init-only property assignment

---

## 4. T006: SharedKernel Base Types {#t006-sharedkernel}

### Files Created

```
src/SharedKernel/HealthBooking.SharedKernel/
├── Domain/
│   ├── IDomainEvent.cs
│   ├── AuditableEntity.cs
│   └── AggregateRoot.cs
├── Persistence/
│   ├── OutboxMessage.cs
│   └── OutboxMessageConfiguration.cs
├── Behaviors/
│   ├── LoggingBehavior.cs
│   ├── ValidationBehavior.cs
│   └── PerformanceBehavior.cs
└── Extensions/
    └── ResilienceExtensions.cs
```

### `IDomainEvent.cs` — Why it extends `INotification`

```csharp
public interface IDomainEvent : INotification { }
```

By extending MediatR's `INotification`, every domain event can be dispatched in-process via `IPublisher.Publish()` **and** published out-of-process via the Outbox. This dual-dispatch capability lets us decouple within-service event handling (e.g., raise a log entry) from cross-service messaging (e.g., notify RabbitMQ).

### `AggregateRoot.cs` — Domain Events Collection

```csharp
public abstract class AggregateRoot : AuditableEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) 
        => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

**Why private backing list + read-only public surface?**  
The aggregate is the only object that knows when state changes occur. External code cannot add or remove domain events — it can only read them. The `OutboxPublishingInterceptor` reads `DomainEvents` and then calls `ClearDomainEvents()` exactly once per `SaveChanges`, ensuring events are captured atomically with persistence.

### `OutboxMessage.cs` — 7 fields explained

| Field | Type | Why |
|---|---|---|
| `Id` | `Guid` | Auto-generated via `Guid.NewGuid()` — no DB identity dependency |
| `EventType` | `string` | `typeof(Event).FullName` — used to deserialize on consumer side |
| `SchemaVersion` | `string` | Enables backward-compatible schema evolution (`"1.0"`, `"2.0"`) |
| `Payload` | `nvarchar(max)` | JSON-serialized event — `System.Text.Json` (no Newtonsoft dependency) |
| `DestinationExchange` | `string` | RabbitMQ exchange name derived from event type name |
| `Status` | `string` | `Pending → Published → Failed` state machine |
| `RetryCount` | `int` | Incremented by `OutboxProcessor`; capped at 3 before marking `Failed` |

### `OutboxMessageConfiguration.cs` — EF Fluent API Explained

```csharp
builder.HasIndex(x => new { x.Status, x.CreatedAt })
       .HasDatabaseName("IX_OutboxMessages_Status_CreatedAt");
```

**Why this composite index?** The `OutboxProcessor` queries `WHERE Status = 'Pending' ORDER BY CreatedAt`. The composite index on `(Status, CreatedAt)` means SQL Server can seek directly to `Pending` rows ordered by time — no full table scan even with millions of messages.

### NuGet Packages (resolved versions)

| Package | Version | Reason for version |
|---|---|---|
| `MediatR` | 12.4.0 | Stable release with C# 12 compatibility |
| `FluentValidation` | 11.10.0 | Current LTS |
| `Microsoft.EntityFrameworkCore.Relational` | 9.0.0 | Required for `ToTable()`, `HasDefaultValue()`, `HasDatabaseName()` |
| `Microsoft.Extensions.Http.Resilience` | 9.0.0 | Microsoft's Polly 8 wrapper — avoids direct Polly dependency |
| `Serilog.Expressions` | 5.0.0 | 4.x not available; 5.x has same API surface |
| `Serilog.Sinks.Console` | 6.1.1 | 6.2.0 not available on NuGet at time of creation |

---

## 5. T007: Contracts Library {#t007-contracts}

### File Created

```
src/SharedKernel/HealthBooking.Contracts/
└── Appointments/V1/
    └── V1_AppointmentEvents.cs
```

### Event Records — Design Choices

```csharp
public sealed record V1_AppointmentBookedEvent(
    Guid AppointmentId, Guid PatientId, Guid ProviderId, Guid SlotId,
    DateTimeOffset ScheduledStartUtc, DateTimeOffset ScheduledEndUtc,
    Guid SagaCorrelationId, DateTimeOffset OccurredAt);
```

**Why `sealed record`?**
1. `record` — immutable value type; structural equality; built-in `ToString()` for logging
2. `sealed` — prevents inheritance that could inadvertently change the contract
3. Named positional parameters — compiler-generated deconstruction and pattern matching

**Why `V1_` prefix?**  
Explicit versioning in the type name prevents accidentally using `V1` consumers with `V2` publishers. The version is visible at the call site, not hidden in a namespace.

**Why `SagaCorrelationId`?**  
AppointmentService may initiate a booking saga spanning multiple services. The `SagaCorrelationId` threads through all events, enabling Jaeger to correlate all spans to a single business transaction.

**Why `DateTimeOffset` not `DateTime`?**  
`DateTimeOffset` carries timezone offset information. Appointments span timezone boundaries (patient in UTC+2, provider in UTC+0). `DateTime` would silently discard offset information.

---

## 6. T008: Proto Files (gRPC) {#t008-proto-files}

### Files Created

```
src/SharedKernel/HealthBooking.Contracts/
└── Protos/
    ├── provider.proto
    └── patient.proto
```

### `provider.proto` — Three RPCs Explained

```protobuf
service ProviderGrpc {
  rpc GetSlotById (GetSlotRequest) returns (SlotResponse);
  rpc LockSlot    (LockSlotRequest) returns (LockSlotResponse);
  rpc ReleaseSlot (ReleaseSlotRequest) returns (ReleaseSlotResponse);
}
```

| RPC | Caller | Purpose | Idempotent? |
|---|---|---|---|
| `GetSlotById` | AppointmentService query handlers | Verify slot exists and is available | Yes |
| `LockSlot` | `BookAppointmentCommandHandler` | Atomically claim slot for appointment | No — must handle `DbUpdateConcurrencyException` |
| `ReleaseSlot` | `RescheduleAppointmentCommandHandler` or compensation path | Return slot to Available status | Yes |

**Why gRPC not REST for inter-service calls?**
- Binary protocol (Protobuf) is ~5× smaller than JSON
- Strongly typed — no schema drift between producer/consumer
- Built-in deadlines, cancellation, and retries via Polly pipeline
- Code generated from `.proto` — no manual HTTP client code

### `patient.proto` — PII Note

```protobuf
message PatientResponse {
  string patient_id = 1; string full_name = 2; string contact_email = 3;
}
```

The task specification explicitly marked `full_name` and `contact_email` as PII fields that **must not** be cached or stored downstream. In Week 6, we add Serilog destructuring policies that mask these fields in all log output.

---

## 7. T009–T010: MediatR Behaviors + Polly Resilience {#t009-t010-behaviors}

### Behavior Pipeline Order

MediatR executes behaviors as a chain of responsibility. Registration order determines execution order:

```
Request
  → LoggingBehavior (logs request name in, out)
    → ValidationBehavior (throws ValidationException if invalid)
      → PerformanceBehavior (starts stopwatch, warns if >500ms)
        → CommandHandler (actual business logic)
      ← PerformanceBehavior (stops stopwatch)
    ← ValidationBehavior
  ← LoggingBehavior
```

**Why this order?**
- Logging wraps everything — captures the full elapsed time including validation
- Validation is before the handler — the handler never runs with bad data
- Performance is innermost — measures only the handler, not validation overhead

### `ValidationBehavior.cs` — FluentValidation Integration

```csharp
if (!validators.Any()) return await next();
```

**Why early return?** Most query handlers don't have validators. Checking `validators.Any()` avoids building a `ValidationContext` for every request that doesn't need one. In a high-throughput system handling thousands of requests per second, this saves allocations.

### `ResilienceExtensions.cs` — The Pipeline

```csharp
pipeline.AddTimeout(TimeSpan.FromSeconds(10));   // 1st: fail fast
pipeline.AddRetry(new HttpRetryStrategyOptions   // 2nd: retry if transient
{
    MaxRetryAttempts = 3,
    BackoffType = DelayBackoffType.Exponential,
    UseJitter = true,
    Delay = TimeSpan.FromMilliseconds(500)
});
pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions  // 3rd: stop hammering
{
    FailureRatio = 0.5,             // open if 50% of calls fail
    SamplingDuration = TimeSpan.FromSeconds(30),
    MinimumThroughput = 5,          // require at least 5 calls before opening
    BreakDuration = TimeSpan.FromSeconds(30)  // stay open 30s
});
```

**Pipeline order — timeout → retry → circuit breaker:**
- Timeout applies per individual attempt
- Retry only retries transient failures (not timeouts from the breaker-open state)
- Circuit breaker is outermost — when open, it immediately returns without hitting retry

**Why jitter?** Without jitter, retrying 100 concurrent clients would synchronize and hit the downstream service in waves. Jitter randomizes the retry window, spreading load.

---

## 8. T011: Service Project Scaffolding (16 Projects) {#t011-service-projects}

### Project Reference Graph

```
{Service}.Domain ─────────────────── → SharedKernel
{Service}.Application ───────────── → Domain + SharedKernel
{Service}.Infrastructure ─────────── → Application + SharedKernel + Contracts
{Service}.API ────────────────────── → Infrastructure
```

**Why no Domain reference to Application?**  
Domain must be pure business logic — no infrastructure, no application orchestration. If Domain referenced Application, it would create a circular dependency and violate the Dependency Inversion Principle.

**Why Infrastructure references Contracts?**  
Infrastructure contains gRPC clients and MassTransit consumers that work with the proto-generated types. Application layer only works with `interface` abstractions (`IProviderSlotGrpcClient`) — the concrete gRPC client lives in Infrastructure.

### Port Assignments

| Service | HTTP Port | gRPC Port |
|---|---|---|
| IdentityServer | 5005 | — |
| ApiGateway | 5000 | — |
| PatientService | 5001 | 5011 |
| ProviderService | 5002 | 5012 |
| AppointmentService | 5003 | 5013 |
| NotificationService | 5004 | — |

---

## 9. T012: Test Projects (8 Projects) {#t012-test-projects}

```
tests/
├── PatientService.UnitTests/         → refs PatientService.Application + Domain
├── PatientService.IntegrationTests/  → refs PatientService.API (WebApplicationFactory)
├── ProviderService.UnitTests/
├── ProviderService.IntegrationTests/
├── AppointmentService.UnitTests/
├── AppointmentService.IntegrationTests/
├── NotificationService.UnitTests/
└── NotificationService.IntegrationTests/
```

### Test Package Stack

| Package | Version | Role |
|---|---|---|
| `xunit` | 2.x | Test runner framework |
| `NSubstitute` | 5.x | Mocking — simpler API than Moq |
| `Bogus` | 35.x | Realistic fake data (names, emails, dates) |
| `FluentAssertions` | 7.x | Human-readable assertions |
| `Testcontainers.MsSql` | 4.x | Real SQL Server in Docker for integration tests |
| `Testcontainers.RabbitMq` | 4.x | Real RabbitMQ for consumer tests |
| `Microsoft.AspNetCore.Mvc.Testing` | 9.0.x | In-memory host for API integration tests |

**Why Testcontainers over SQLite for integration tests?**  
SQLite doesn't support `NEWSEQUENTIALID()`, `SYSDATETIMEOFFSET()`, or `ROWVERSION` — all used in our EF Core configurations. Using the real SQL Server 2022 image (via Testcontainers) ensures migrations and queries behave identically in CI and production.

---

## 10. T013–T014: IdentityServer (Duende) {#t013-t014-identityserver}

### Files Created

```
src/IdentityServer/HealthBooking.IdentityServer/
├── Config.cs       ← clients, scopes, resources
├── SeedData.cs     ← idempotent DB seed
└── Program.cs      ← DI wiring, middleware pipeline
```

### `Config.cs` — Three Clients Explained

| Client | Grant Type | Scopes | Use Case |
|---|---|---|---|
| `api-gateway` | `ClientCredentials` | `healthbooking-api` | Machine-to-machine gateway internal calls |
| `patient-spa` | `ResourceOwnerPassword` | `patient:read/write`, `appointment:read/write` | Browser SPA logging in as patient |
| `admin-client` | `ResourceOwnerPassword` | All scopes | Admin portal managing all resources |

**Why `ResourceOwnerPassword` for SPA?**  
In a real production system, SPA should use Authorization Code + PKCE. `ResourceOwnerPassword` is used here for simplified training demo flow — it avoids requiring a browser redirect in integration tests.

**Why separate `patient:read` and `patient:write` scopes?**  
Principle of least privilege. A read-only reporting service gets `patient:read` only. Separating read and write allows fine-grained API policy checks:
```csharp
.RequireAuthorization("PatientWrite")  // requires patient:write scope
```

### `SeedData.cs` — Idempotency Guard

```csharp
if (!await configDb.ApiScopes.AnyAsync(s => s.Name == scope2.Name))
    configDb.ApiScopes.Add(scope2.ToEntity());
```

The seed is idempotent — safe to call on every startup (and it's called in `Program.cs`). Without the exists check, repeated starts would insert duplicate rows and Duende would throw on duplicate scope names.

### `Program.cs` — Key Design Decisions

```csharp
.AddOperationalStore(opts =>
{
    opts.EnableTokenCleanup   = true;
    opts.TokenCleanupInterval = 3600;  // clean expired tokens every hour
})
.AddDeveloperSigningCredential();  // auto-generated RSA key for local dev
```

`AddDeveloperSigningCredential()` generates an ephemeral RSA key stored in memory. **This is intentional for dev only** — in production, replace with `AddSigningCredential(cert)` pointing to an X.509 certificate stored in Azure Key Vault or similar.

---

## 11. T015–T016: API Gateway (YARP) {#t015-t016-apigateway}

### Files Created

```
src/ApiGateway/HealthBooking.ApiGateway/
├── Middleware/
│   └── CorrelationIdMiddleware.cs
├── Program.cs
└── appsettings.json   ← YARP routes + clusters
```

### `CorrelationIdMiddleware.cs` — Correlation ID Flow

```csharp
var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
    ?? Guid.NewGuid().ToString("N");
context.Response.Headers[HeaderName] = correlationId;
using (LogContext.PushProperty("CorrelationId", correlationId))
{
    await next(context);
}
```

**Why `Guid.NewGuid().ToString("N")`?**  
The `"N"` format produces 32 hex chars with no hyphens — shorter and safe for HTTP headers. The middleware accepts an incoming `X-Correlation-Id` from the client (allowing client-side tracing correlation) or generates a new one if absent.

**Why `LogContext.PushProperty`?**  
Serilog's `LogContext` is async-flow ambient state. All log entries emitted within the `using` block automatically include `CorrelationId`. When Serilog forwards to Jaeger, the trace ID links all spans from gateway → service → database.

### `appsettings.json` — YARP Routes

```json
"patient-register-route": {
    "ClusterId": "patient-cluster",
    "Match": { "Path": "/api/patients/register" }
    // No AuthorizationPolicy — anonymous registration
},
"patient-route": {
    "ClusterId": "patient-cluster",
    "AuthorizationPolicy": "JwtBearer",
    "Match": { "Path": "/api/patients/{**remainder}" }
}
```

**Why anonymous register vs. JWT-required routes?**  
A new patient cannot have a JWT before registering. The register endpoint (`POST /api/patients/register`) deliberately skips auth. All other patient routes require a valid JWT.

**Why active health checks in clusters?**
```json
"HealthCheck": {
    "Active": {
        "Enabled": true,
        "Interval": "00:00:10",
        "Policy": "ConsecutiveFailures",
        "Path": "/health/ready"
    }
}
```
YARP's active health check probes `/health/ready` every 10 seconds. If a service is unhealthy (SQL Server restart, etc.), YARP stops forwarding requests to it until it recovers. This prevents a cascade of errors at the gateway.

### Rate Limiter — Sliding Window 300 req/min

```csharp
opts.AddSlidingWindowLimiter("gateway", limiterOpts =>
{
    limiterOpts.PermitLimit       = 300;
    limiterOpts.Window            = TimeSpan.FromMinutes(1);
    limiterOpts.SegmentsPerWindow = 6;  // 10-second sub-windows
});
```

**Why sliding window over fixed window?**  
A fixed window at 300 req/min allows a burst of 300 at 00:59 and 300 more at 01:00 — effectively 600 in 2 seconds. Sliding window distributes the limit evenly across the window, preventing this burst amplification.

---

## 12. T017–T018: Docker Compose {#t017-t018-docker}

### `docker-compose.yml` — Infrastructure Services

```
Services:
  sqlserver-patient    :1433  SQL Server 2022 → HealthBooking_Patient database
  sqlserver-provider   :1434  SQL Server 2022 → HealthBooking_Provider database
  sqlserver-appointment:1435  SQL Server 2022 → HealthBooking_Appointment database
  sqlserver-notification:1436 SQL Server 2022 → HealthBooking_Notification database
  sqlserver-identity   :1437  SQL Server 2022 → HealthBooking_Identity database
  rabbitmq             :5672  RabbitMQ 3 Management → event bus
  redis                :6379  Redis 7 Alpine → provider slot cache
  jaeger               :4317  Jaeger all-in-one → distributed traces
```

**Why 5 separate SQL Server instances?**  
Each service owns its own database — Database-per-Service pattern. Benefits:
- Independent schema evolution (no cross-service EF migrations)
- Independent scaling (appointment DB can scale without affecting patient DB)
- Failure isolation (patient DB down doesn't affect appointment DB)

**Health check pattern used on all containers:**
```yaml
healthcheck:
  test: ["CMD", "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost",
         "-U", "sa", "-P", "${SQL_SA_PASSWORD}", "-Q", "SELECT 1", "-No"]
  interval: 10s
  timeout: 5s
  retries: 10
  start_period: 30s
```
`start_period: 30s` tells Docker to ignore failures for the first 30 seconds (SQL Server needs ~20s to initialize). `retries: 10` gives 100 seconds total to reach healthy state.

### `docker-compose.override.yml` — Application Services

```yaml
appointment-service:
  depends_on:
    sqlserver-appointment:
      condition: service_healthy
    rabbitmq:
      condition: service_healthy
    provider-service:
      condition: service_healthy   ← must start before AppointmentService
    patient-service:
      condition: service_healthy   ← because AppointmentService calls them via gRPC
```

**Why `condition: service_healthy`?**  
`depends_on` by default only waits for the container to **start**, not to be **ready**. `condition: service_healthy` waits for the health check to pass — ensuring the database is accepting connections before the service tries to run EF Core migrations.

---

## 13. T019: CI Workflow {#t019-ci}

### `.github/workflows/ci.yml` — Pipeline Steps

```yaml
steps:
  - Restore
  - Format check: dotnet format --verify-no-changes    ← fails if files need formatting
  - Build:        dotnet build --configuration Release
  - Test:         dotnet test --collect:"XPlat Code Coverage"
  - Upload coverage artifacts
```

**Why `dotnet format --verify-no-changes`?**  
This step fails if any file would be changed by the formatter. It enforces the `.editorconfig` rules defined in T004. Without this, developers could bypass formatting rules locally and push unformatted code that still builds.

**Why `--configuration Release`?**  
Release builds apply optimizations and don't include debug symbols. Running tests against Release binaries catches issues that only appear with compiler optimizations active.

---

## 14. T020: Auth Smoke Tests {#t020-auth-smoke}

### `AuthSmokeTests.cs` — Three Scenarios

```csharp
[Fact(Skip = "Requires full docker-compose stack")]
public async Task PostConnectToken_WithValidClientCredentials_ReturnsJwt()
```

**Why `Skip`?**  
These tests require the full `docker compose up` stack. They're skipped in automated CI (which doesn't provision Docker) but can be run manually: `dotnet test --filter "Category=Integration"`.

### Test Scenarios — Why These Three?

| Test | Validates |
|---|---|
| `PostConnectToken` → JWT returned | IdentityServer is running and issuing tokens |
| `GET /api/patients/me` no token → 401 | JWT Bearer middleware is enforcing auth |
| `GET /api/patients/me` with token → 403/404 | Token is valid but no patient record (not 401 — token was accepted) |

The third test is subtle: a 401 would mean the token was rejected. A 403/404 proves the token was accepted but the user has no patient record yet — exactly the expected state in an empty database.

---

## 15. Design Decisions & Best Practices {#design-decisions}

### Why Not Use `netstandard2.1` for SharedKernel?

Original plan used `netstandard2.1`. This was changed to `net9.0` because:
- `Microsoft.Extensions.Http.Resilience 9.0.0` requires `net6.0` or later
- `Microsoft.EntityFrameworkCore.Relational 9.0.0` emits NU1701 (framework compatibility warnings treated as errors)
- Using `net9.0` ensures full compatibility with all .NET 9 packages

### Why `sealed` on All Classes?

```csharp
public sealed class LoggingBehavior<TRequest, TResponse> ...
public sealed class OutboxPublishingInterceptor ...
public sealed class CorrelationIdMiddleware ...
```

Sealing concrete implementation classes:
- Communicates intent: "this is not designed for inheritance"
- Enables JIT devirtualization — `sealed` method calls can be inlined
- Prevents unintended subclassing that could break behavior guarantees

### Why Primary Constructors (C# 12)?

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
```

Primary constructors:
- Eliminate boilerplate `private readonly ILogger _logger;` + `_logger = logger;`
- Parameters are in scope throughout the class body
- Enable compiler to verify constructor arguments are used (unlike field injection)

### Why Outbox Pattern Instead of Direct MassTransit Publish?

```
Without Outbox:
SaveChanges() → succeeds
bus.Publish() → fails ← EVENT LOST

With Outbox:
SaveChanges() → writes OutboxMessage in SAME transaction
  if fails → both rolled back (consistent)
  if succeeds → OutboxProcessor publishes within 5s
```

The Outbox eliminates the dual-write problem. Direct `bus.Publish()` after `SaveChanges()` has a window where the DB commit succeeds but the message publish fails — the event is silently lost. The Outbox makes event publishing part of the same ACID transaction.

---

## 16. How to Run Locally {#how-to-run}

### Prerequisites
- .NET 9 SDK
- Docker Desktop (for integration tests and local run)

### Quick Start

```bash
# 1. Clone and restore
git clone https://github.com/ahmedrefaat007/HealthBooking
cd HealthBooking
cp .env.example .env
# Edit .env with your passwords

# 2. Start infrastructure
docker compose up -d

# 3. Build all projects
dotnet build HealthBooking.sln

# 4. Run unit tests (no Docker needed)
dotnet test HealthBooking.sln --filter "Category!=Integration"

# 5. Run all tests including integration (Docker required)
dotnet test HealthBooking.sln

# 6. Check IdentityServer
curl http://localhost:5005/.well-known/openid-configuration

# 7. Get a token
curl -X POST http://localhost:5005/connect/token \
  -d "grant_type=client_credentials&client_id=api-gateway&client_secret=api-gateway-secret&scope=healthbooking-api"
```

### Port Reference

| Service | Port | UI |
|---|---|---|
| API Gateway | 5000 | — |
| Patient Service | 5001 | — |
| Provider Service | 5002 | — |
| Appointment Service | 5003 | — |
| Notification Service | 5004 | — |
| Identity Server | 5005 | `/.well-known/openid-configuration` |
| RabbitMQ Management | 15672 | `http://localhost:15672` |
| Jaeger UI | 16686 | `http://localhost:16686` |
| SQL Server (Patient) | 1433 | SSMS or Azure Data Studio |

---

## 17. Week 2 Preview {#week-2-preview}

**Branch**: `001-w2-core-domain`  
**Tasks**: T021–T065 (45 tasks)  
**Goal**: PatientService (US1) and ProviderService (US2) fully functional with:
- CRUD endpoints
- gRPC services
- EF Core migrations + Outbox
- Cache-aside pattern (Redis) for provider slots
- Unit tests ≥80% coverage
- Integration tests with TestContainers

### Key New Patterns in Week 2
- **Value Objects**: `Email`, `PhoneNumber`, `PatientId`, `SlotId` — type-safe domain primitives
- **Factory Methods**: `Patient.Register()`, `Provider.Create()` — no public constructors
- **Optimistic Concurrency**: `RowVersion` on `AvailabilitySlot` — prevents double-booking at DB level
- **Cache-Aside**: `GetProviderSlotsQuery` checks Redis first, fallback to SQL, store result with 60s TTL
- **AuditInterceptor**: Automatically populates `CreatedAt/By/ModifiedAt/By` on every save

---

*Generated by GitHub Copilot for HealthBooking distributed healthcare system.*  
*Last updated: Week 1 completion (T001–T020)*
