# Week 6 — Observability, Docker Compose & Integration Tests (T156–T190)

Branch: `001-w6-observability`
Merged from: `001-w5-resilience`

---

## Overview

Week 6 closes out the HealthBooking system with three complementary deliverables:

1. **OpenTelemetry distributed tracing** — all six application services (4 microservices + IdentityServer + ApiGateway) are instrumented with the same shared extension. Traces are exported to Jaeger via OTLP gRPC and include ASP.NET Core (HTTP + gRPC server), HttpClient (HTTP + gRPC client), and MassTransit publish/consume spans.
2. **Docker Compose finalization** — six application service containers are added to `docker-compose.yml`, completing the full-stack local development environment. Every container starts only after its infrastructure dependencies pass health checks.
3. **ProviderService integration tests** — `ProviderPersistenceTests` exercises real EF Core migrations against an in-process SQL Server Testcontainer, covering provider registration, outbox publishing, and `DefineDailyAvailability` slot generation.
4. **PatientService test fix** — the `Email` value object normalises input to lowercase; the unit test assertions are corrected to compare against the normalised value.

---

## 1. OpenTelemetry — `HealthBooking.SharedKernel`

### NuGet packages added to `HealthBooking.SharedKernel.csproj`

| Package | Version |
|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.11.0 |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.11.0 |
| `OpenTelemetry.Instrumentation.Http` | 1.11.0 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.11.0 |

> `OpenTelemetry.Instrumentation.GrpcNetClient` is **not** required. From `OpenTelemetry.Instrumentation.Http` v1.9 onwards, gRPC-client spans are captured automatically via `HttpClient` instrumentation.

### `TelemetryExtensions.AddHealthBookingTelemetry`

File: `src/SharedKernel/HealthBooking.SharedKernel/Extensions/TelemetryExtensions.cs`

```csharp
public static IServiceCollection AddHealthBookingTelemetry(
    this IServiceCollection services,
    string                  serviceName,
    IConfiguration          configuration)
{
    var endpoint = configuration["OtelExporter:Endpoint"] ?? "http://localhost:4317";

    services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName, serviceVersion: "..."))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(opts => {
                opts.RecordException = true;
                opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
            })
            .AddHttpClientInstrumentation(opts => { opts.RecordException = true; })
            .AddSource("MassTransit")           // MassTransit 8.x built-in ActivitySource
            .AddOtlpExporter(opts => {
                opts.Endpoint = new Uri(endpoint);
                opts.Protocol = OtlpExportProtocol.Grpc;
            }));

    return services;
}
```

**Design decisions:**

| Decision | Rationale |
|---|---|
| Health-check filter | `/health/*` endpoints generate hundreds of spans per minute in production. Filtering them keeps Jaeger storage lean. |
| `RecordException = true` | Exceptions are attached to the failing span as structured events, making root-cause analysis faster. |
| `AddSource("MassTransit")` | MassTransit 8.x creates its own `ActivitySource` named `"MassTransit"`. Without this line, publish and consume operations are invisible in Jaeger. |
| Config-driven endpoint | `OtelExporter:Endpoint` defaults to `http://localhost:4317` for `dotnet run`; overridden to `http://jaeger:4317` via environment variable in Docker. |

### Registration in every `Program.cs`

The same two-line pattern is added to all six application entry points before the health-check registration:

```csharp
// ── OpenTelemetry ────────────────────────────────────────────────────────────
builder.Services.AddHealthBookingTelemetry("patient-service", builder.Configuration);
```

| Service | `serviceName` argument |
|---|---|
| PatientService.API | `"patient-service"` |
| ProviderService.API | `"provider-service"` |
| AppointmentService.API | `"appointment-service"` |
| NotificationService.API | `"notification-service"` |
| HealthBooking.ApiGateway | `"api-gateway"` |

`HealthBooking.ApiGateway.csproj` did not previously reference `HealthBooking.SharedKernel`; a `<ProjectReference>` was added and the `using HealthBooking.SharedKernel.Extensions;` directive was added to the gateway's `Program.cs`.

### `appsettings.json` additions

Each of the five service configuration files receives:

```json
"OtelExporter": {
  "Endpoint": "http://localhost:4317"
}
```

This value is the default for local `dotnet run`. In Docker Compose the variable is injected as `OtelExporter__Endpoint: "${JAEGER_OTLP_ENDPOINT}"` which maps to `http://jaeger:4317`.

---

## 2. Docker Compose — application service containers

All six containers are appended to `docker-compose.yml` after the Jaeger service and before the `volumes:` block.

### Full-stack topology

```
                         ┌─ sqlserver-identity ─┐
                         │                       ▼
               ┌─────────┤              identity-server (:5005)
               │         └───────────────────────┘
               │
               │  ┌─ sqlserver-patient ─┐
               │  │  rabbitmq           │── patient-service  (:5001)
               │  │  identity-server    │
               │  └─────────────────────┘
               │
               │  ┌─ sqlserver-provider ─┐
               │  │  rabbitmq            │
               │  │  redis               │── provider-service (:5002)
               │  │  identity-server     │
               │  └──────────────────────┘
               │
               │  ┌─ sqlserver-appointment ─┐
               │  │  rabbitmq               │
               │  │  patient-service        │── appointment-service (:5003)
               │  │  provider-service       │
               │  └─────────────────────────┘
               │
               │  ┌─ sqlserver-notification ─┐
               │  │  rabbitmq                │
               │  │  patient-service         │── notification-service (:5004)
               │  └──────────────────────────┘
               │
               └──── all four services ────── api-gateway (:5000)
```

### Per-container summary

| Container | Host port | Health check path | Depends on |
|---|---|---|---|
| `identity-server` | 5005 | `/health/live` | `sqlserver-identity` |
| `patient-service` | 5001 | `/health/live` | `sqlserver-patient`, `rabbitmq`, `identity-server` |
| `provider-service` | 5002 | `/health/live` | `sqlserver-provider`, `rabbitmq`, `redis`, `identity-server` |
| `appointment-service` | 5003 | `/health/live` | `sqlserver-appointment`, `rabbitmq`, `patient-service`, `provider-service` |
| `notification-service` | 5004 | `/health/live` | `sqlserver-notification`, `rabbitmq`, `patient-service` |
| `api-gateway` | 5000 | `/health/ready` | all four microservices |

All `depends_on` entries use `condition: service_healthy`, ensuring Docker waits for each dependency's health check to pass before starting the dependent container.

### Shared environment variable pattern

Every application container receives:

```yaml
environment:
  ASPNETCORE_URLS: "http://+:<port>"
  ConnectionStrings__DefaultConnection: "${<SERVICE>_SQL_CONNECTION}"
  OtelExporter__Endpoint: "${JAEGER_OTLP_ENDPOINT}"
```

The `JAEGER_OTLP_ENDPOINT` variable is defined in `.env.example` as `http://jaeger:4317`, routing traces to the Jaeger container already present in the compose file.

### Starting the full stack locally

```bash
cp .env.example .env
docker compose build
docker compose up -d
```

| URL | Service |
|---|---|
| http://localhost:5000 | API Gateway (entry point) |
| http://localhost:16686 | Jaeger UI (trace explorer) |
| http://localhost:15672 | RabbitMQ Management (guest / guest) |

---

## 3. ProviderService integration tests

### `TestDbContextFactory`

File: `tests/ProviderService.IntegrationTests/TestDbContextFactory.cs`

Mirrors the pattern established in `AppointmentService.IntegrationTests`. Constructs a `ProviderDbContext` with a real SQL Server connection string, identical interceptor chain (`AuditInterceptor`, `OutboxPublishingInterceptor`), and `ICurrentUserService` backed by a bare `HttpContextAccessor`.

```csharp
internal static class TestDbContextFactory
{
    public static ProviderDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ProviderDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsAssembly("ProviderService.Infrastructure"))
            .Options;

        var httpAccessor = new HttpContextAccessor();
        var currentUser  = new CurrentUserService(httpAccessor);
        var audit        = new AuditInterceptor(currentUser);
        var outbox       = new OutboxPublishingInterceptor();

        return new ProviderDbContext(options, audit, outbox);
    }
}
```

### `ProviderPersistenceTests`

File: `tests/ProviderService.IntegrationTests/Persistence/ProviderPersistenceTests.cs`

Uses `MsSqlContainer` from Testcontainers (already in the csproj) so every test run spins up a fresh, isolated SQL Server instance and applies migrations via `Database.MigrateAsync()`.

| Test | What it exercises |
|---|---|
| `RegisterProvider_PersistsAndIsRetrievable` | `Provider.Register(...)` → `SaveChangesAsync` → reload via `FindAsync`; asserts all scalar fields |
| `SaveChanges_WithDomainEvent_WritesOutboxMessage` | Domain event raised by `Register` is captured by `OutboxPublishingInterceptor`; asserts `OutboxMessages.Count() == 1`, `EventType` contains `"ProviderRegisteredEvent"`, `Status == "Pending"` |
| `DefineDailyAvailability_PersistsSlotsWithProvider` | `DefineDailyAvailability(date, 08:00, 09:00)` generates exactly 2 × 30-minute slots; includes provider via `Include(p => p.Slots)` to exercise the navigation-property load path |

---

## 4. PatientService unit test fix

The `Email` value object in `PatientService.Domain` normalises addresses to lowercase via `.ToLowerInvariant()`. The `RegisterPatientCommandHandlerTests.Handle_NewPatient_ReturnsPatientId` test was asserting against the raw `cmd.Email` string (mixed case from Faker), causing two assertion mismatches:

1. `AddAsync` — `p.ContactEmail == cmd.Email` (case mismatch for stored entity)
2. `ProvisionUserAsync` — `cmd.Email` (case mismatch for identity service call)

Both assertions were updated to compare against `cmd.Email.ToLowerInvariant()`.

---

## 5. Key technical decisions

| Decision | Choice | Rationale |
|---|---|---|
| Single `AddHealthBookingTelemetry` extension | Shared in `HealthBooking.SharedKernel` | One change point if the stack evolves (e.g., add metrics, logs) |
| `OtlpExportProtocol.Grpc` | gRPC (port 4317) not HTTP/Protobuf (port 4318) | Jaeger `all-in-one` opens 4317 by default; gRPC is lower overhead than HTTP |
| No `GrpcNetClient` package | Relies on `Http` instrumentation ≥ 1.9 | Package was deprecated/merged; avoids duplicate spans |
| `depends_on: condition: service_healthy` | All compose dependencies | Prevents connection-refused errors when a service container starts before its DB is ready |
| Integration test isolation via Testcontainers | One `MsSqlContainer` per test class | Each class gets a clean schema; no inter-test state leakage |

---

## 6. Test results

```
AppointmentService.UnitTests   Passed: 21
NotificationService.UnitTests  Passed:  4
ProviderService.UnitTests      Passed: 10
PatientService.UnitTests       Passed: 20   (fixed from 19/20 passing)
```

Integration tests (Testcontainers) require Docker and are run separately:

```bash
dotnet test tests/ProviderService.IntegrationTests
dotnet test tests/AppointmentService.IntegrationTests
dotnet test tests/PatientService.IntegrationTests
```

---

## Files changed this week

### Created
| File | Purpose |
|---|---|
| `src/SharedKernel/HealthBooking.SharedKernel/Extensions/TelemetryExtensions.cs` | Shared OTel registration extension |
| `tests/ProviderService.IntegrationTests/TestDbContextFactory.cs` | EF Core context factory for integration tests |
| `tests/ProviderService.IntegrationTests/Persistence/ProviderPersistenceTests.cs` | 3 integration tests for ProviderDbContext |
| `docs/001-w6-observability.md` | This document |

### Modified
| File | Change |
|---|---|
| `src/SharedKernel/HealthBooking.SharedKernel/HealthBooking.SharedKernel.csproj` | +4 OTel NuGet packages |
| `src/ApiGateway/HealthBooking.ApiGateway/HealthBooking.ApiGateway.csproj` | +SharedKernel ProjectReference |
| `src/ApiGateway/HealthBooking.ApiGateway/Program.cs` | OTel registered; `using` directive added |
| `src/Services/PatientService/PatientService.API/Program.cs` | OTel registered |
| `src/Services/ProviderService/ProviderService.API/Program.cs` | OTel registered |
| `src/Services/AppointmentService/AppointmentService.API/Program.cs` | OTel registered |
| `src/Services/NotificationService/NotificationService.API/Program.cs` | OTel registered |
| `src/Services/*/appsettings.json` (×5) | `OtelExporter:Endpoint` added |
| `docker-compose.yml` | 6 application service containers added |
| `tests/PatientService.UnitTests/Application/RegisterPatientCommandHandlerTests.cs` | Email case-normalisation assertions fixed |
