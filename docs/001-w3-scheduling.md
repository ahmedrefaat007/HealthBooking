# Week 3 — Scheduling: AppointmentService
**Branch:** `001-w3-scheduling`  
**Merged from:** `001-w2-core-domain`  
**Tasks:** T066–T092  
**Commit:** `e5aba9d`

---

## Overview

Week 3 delivers the **AppointmentService** — the critical bounded context that orchestrates bookings between patients and provider availability slots. It consumes the two gRPC APIs built in Week 2 and introduces several advanced patterns:

- **Idempotent booking** — every request carried a client-supplied `Idempotency-Key` header; duplicate requests return the original result without side effects.
- **Distributed lock via gRPC + optimistic concurrency** — the booking flow calls `ProviderService.LockSlot` over gRPC; SQL Server's `RowVersion` on the slot propagates `ABORTED` back on a concurrent race, which AppointmentService re-surfaces as HTTP `409 Conflict`.
- **Transactional Outbox** — `Appointment` entity + `BookingIdempotencyKey` + `OutboxMessage` rows committed in a single `SaveChangesAsync` call; an `OutboxProcessor` background service polls and publishes to RabbitMQ.
- **Cross-service gRPC client resilience** — `AddHealthBookingResiliencePipeline` (timeout 10s → retry 3× → circuit breaker) applied to both `PatientGrpcClient` and `ProviderSlotGrpcClient`.

---

## Architecture Diagram

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  ApiGateway (YARP)                                                           │
│    /api/appointments/** → AppointmentService:5003                            │
└───────────────────────────────┬──────────────────────────────────────────────┘
                                │ REST (HTTP/1.1)
                       ┌────────▼────────────┐
                       │  AppointmentService │
                       │  :5003              │
                       └────────┬────────────┘
                                │
                ┌───────────────┼───────────────────┐
                │ gRPC :5051    │                   │ gRPC :5052
       ┌────────▼────────┐      │         ┌─────────▼────────┐
       │  PatientService │      │         │  ProviderService │
       │  GetPatientById │      │         │  LockSlot        │
       └─────────────────┘      │         │  ReleaseSlot     │
                                │         └──────────────────┘
                    ┌───────────▼──────────┐
                    │  SQL Server :1435    │
                    │  AppointmentDb       │
                    │  OutboxMessages      │
                    └───────────┬──────────┘
                                │ polls every 5s
                    ┌───────────▼──────────┐
                    │  OutboxProcessor     │
                    │  (BackgroundService) │
                    └───────────┬──────────┘
                                │ IBus.Publish
                    ┌───────────▼──────────┐
                    │  RabbitMQ            │
                    │  appointment.*       │
                    └──────────────────────┘
```

---

## New PackageReferences — Week 3

| Package | Version | Project |
|---|---|---|
| MassTransit | 8.x | AppointmentService.Infrastructure |
| MassTransit.RabbitMQ | 8.x | AppointmentService.Infrastructure |
| Microsoft.EntityFrameworkCore.SqlServer | 9.0.x | AppointmentService.Infrastructure |
| Grpc.Net.Client | 2.x | AppointmentService.Infrastructure |
| AspNetCore.HealthChecks.Rabbitmq | 8.x | AppointmentService.API |
| AspNetCore.HealthChecks.SqlServer | 8.x | AppointmentService.API |
| Testcontainers.RabbitMq | 4.x | Integration tests |

---

## T066–T068: AppointmentService Domain

### `Domain/Enums/AppointmentStatus.cs`

```csharp
public enum AppointmentStatus
{
    Booked    = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3,
    NoShow    = 4
}
```

Five terminal and non-terminal states. `Booked` is the entry state on creation; `Cancelled`, `Completed`, `NoShow` are terminal (no transitions out). `Confirmed` is reserved for a future manual confirmation step by the provider.

### `Domain/Events/AppointmentEvents.cs`

All three events are `sealed record`s implementing `IDomainEvent` from SharedKernel:

```csharp
public sealed record AppointmentBookedEvent(
    Guid AppointmentId, Guid PatientId, Guid SlotId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentCancelledEvent(
    Guid AppointmentId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentCompletedEvent(
    Guid AppointmentId, DateTimeOffset OccurredAt) : IDomainEvent;
```

### `Domain/Entities/Appointment.cs` — Aggregate Root

```csharp
public sealed class Appointment : AggregateRoot
{
    public Guid              Id           { get; private set; }
    public Guid              PatientId    { get; private set; }
    public Guid              SlotId       { get; private set; }
    public string            PatientName  { get; private set; }
    public AppointmentStatus Status       { get; private set; }
    public string?           CancelReason { get; private set; }
}
```

**Factory:** `Appointment.Book(patientId, slotId, patientName)` — validates non-empty `patientName`, assigns `Guid.NewGuid()` Id, sets `Status = Booked`, raises `AppointmentBookedEvent`.

**State machine guards:**
- `Cancel(reason)` — throws `InvalidOperationException` if already `Completed` or `Cancelled`
- `Complete()` — throws `InvalidOperationException` if not `Booked` or `Confirmed`

Both methods raise their corresponding domain event before returning.

### `Domain/Entities/BookingIdempotencyKey.cs`

```csharp
public sealed class BookingIdempotencyKey
{
    public Guid           Id            { get; init; }
    public string         Key           { get; init; }   // client-supplied token ≤ 200 chars
    public Guid           AppointmentId { get; init; }
    public DateTimeOffset CreatedAt     { get; init; }
}
```

A simple deduplication record. A unique DB index on `Key` prevents concurrent inserts of the same idempotency token; the first writer wins and second caller hits `DbUpdateException` at the DB level (second line of defence — handler checks before inserting).

---

## T069–T075: AppointmentService Application

### Interfaces (`IAppointmentInterfaces.cs`)

```csharp
IAppointmentRepository   — GetByIdAsync, GetByPatientIdAsync, AddAsync, SaveChangesAsync
IPatientGrpcClient       — GetPatientByIdAsync(Guid) → PatientInfo?
IProviderSlotGrpcClient  — GetSlotByIdAsync(Guid) → SlotInfo?
                           LockSlotAsync(Guid slotId, Guid appointmentId) → bool
                           ReleaseSlotAsync(Guid slotId) → bool
IIdempotencyRepository   — FindAsync(key) → BookingIdempotencyKey?, AddAsync, SaveChangesAsync
ICurrentUserService      — UserId
```

The `PatientInfo` and `SlotInfo` value types are also defined in this file — they are the application-layer DTOs that abstract away the generated gRPC message types.

### `BookAppointmentCommand` — 4-Step Saga

The most complex handler in the solution:

```csharp
// Step 1 — Idempotency key lookup
var existing = await idempotency.FindAsync(request.IdempotencyKey, ct);
if (existing is not null) { /* return cached result */ }

// Step 2 — Verify patient via gRPC
var patient = await patientClient.GetPatientByIdAsync(request.PatientId, ct)
    ?? throw new InvalidOperationException($"Patient {request.PatientId} not found.");

// Step 3 — Lock slot via ProviderService gRPC
var locked = await slotClient.LockSlotAsync(request.SlotId, appointmentId, ct);
if (!locked) throw new SlotConflictException($"Slot {request.SlotId} is no longer available.");

// Step 4 — Atomic persist: Appointment + IdempotencyKey + OutboxMessage
await appointments.AddAsync(appointment, ct);
await idempotency.AddAsync(key, ct);
await appointments.SaveChangesAsync(ct);  // single SaveChanges = single transaction
```

The key insight: steps 1-3 are **read-only** external calls. Step 4 is the single commit point. If the DB commit fails, the slot lock is not rolled back (ProviderService has `Locked` state), but the slot lock TTL (future enhancement) or a compensating saga would release it. In the current implementation, Step 3 (`ReleaseSlot`) is called from `CancelAppointmentCommand` to clean up.

**`SlotConflictException`** is a custom exception class defined in the same file — caught in `AppointmentsEndpoints.cs` and mapped to HTTP `409 Conflict`.

### `CancelAppointmentCommand`

Enforces caller ownership:
```csharp
if (appointment.PatientId.ToString() != request.CallerUserId)
    throw new UnauthorizedAccessException("...");
```

Then calls `slotClient.ReleaseSlotAsync` to unblock the slot before `SaveChangesAsync`.

### Validators

```csharp
// BookAppointmentCommandValidator
RuleFor(x => x.PatientId).NotEmpty();
RuleFor(x => x.SlotId).NotEmpty();
RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(200);

// CancelAppointmentCommandValidator
RuleFor(x => x.AppointmentId).NotEmpty();
RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
RuleFor(x => x.CallerUserId).NotEmpty();
```

---

## T076–T083: AppointmentService Infrastructure

### EF Configuration (`AppointmentConfiguration.cs`)

```csharp
// Appointment table
builder.ToTable("Appointments",
    t => t.HasCheckConstraint("CK_Appointments_Status", "[Status] IN (0,1,2,3,4)"));
builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
builder.Property(a => a.Status).HasConversion<int>();
builder.Property(a => a.PatientName).HasMaxLength(200);
builder.Property(a => a.CancelReason).HasMaxLength(500);
builder.HasIndex(a => a.PatientId).HasDatabaseName("IX_Appointments_PatientId");

// BookingIdempotencyKey table
builder.HasIndex(k => k.Key).IsUnique().HasDatabaseName("UQ_IdempotencyKeys_Key");
```

The `[Status] IN (0,1,2,3,4)` check constraint is a DB-level safeguard against any stored integer that doesn't map to a valid enum member.

### Interceptors

Identical pattern to PatientService and ProviderService:

- **`AuditInterceptor`** — reads `ICurrentUserService.UserId`, stamps `CreatedAt`/`CreatedBy` on `Added`, `ModifiedAt`/`ModifiedBy` on `Modified`, protects `CreatedAt`/`CreatedBy` from being overwritten.
- **`OutboxPublishingInterceptor`** — on every `SaveChangesAsync`, harvests domain events from all `AggregateRoot` entries in the change tracker, serialises each to `OutboxMessage` rows in the same commit, then clears the event list.

### `AppointmentDbContext`

```csharp
public sealed class AppointmentDbContext(
    DbContextOptions<AppointmentDbContext> options,
    AuditInterceptor auditInterceptor,
    OutboxPublishingInterceptor outboxInterceptor) : DbContext(options)
{
    public DbSet<Appointment>           Appointments           => Set<Appointment>();
    public DbSet<BookingIdempotencyKey> BookingIdempotencyKeys => Set<BookingIdempotencyKey>();
    public DbSet<OutboxMessage>         OutboxMessages         => Set<OutboxMessage>();
}
```

Uses `OnConfiguring` to register both interceptors via `optionsBuilder.AddInterceptors(...)`.

### gRPC Clients

#### `PatientGrpcClient`

Wraps the generated `PatientGrpc.PatientGrpcClient` stub:
```csharp
// gRPC NotFound → return null (service-layer null = 404 to the consumer)
catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) { return null; }
```

Maps `PatientResponse` proto message → `PatientInfo` application record.

#### `ProviderSlotGrpcClient`

The critical concurrency-handling client:
```csharp
catch (RpcException ex) when (ex.StatusCode == StatusCode.Aborted)
{
    // ProviderService translated DbUpdateConcurrencyException → gRPC ABORTED
    // We re-raise as our own domain exception
    throw new SlotConflictException($"Slot {slotId} is no longer available: {ex.Status.Detail}");
}
```

The translation chain: SQL `RowVersion` mismatch → `DbUpdateConcurrencyException` in ProviderService → gRPC `ABORTED` → `SlotConflictException` in AppointmentService → HTTP `409 Conflict` to client.

### `OutboxProcessor` (BackgroundService)

The event relay from SQL Server to RabbitMQ:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await ProcessBatchAsync(stoppingToken);
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
    }
}

private async Task ProcessBatchAsync(CancellationToken ct)
{
    // New DI scope per batch (DbContext is scoped, BackgroundService is singleton)
    using var scope = scopeFactory.CreateScope();
    var db  = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
    var bus = scope.ServiceProvider.GetRequiredService<IBus>();

    var messages = await db.OutboxMessages
        .Where(m => m.Status == "Pending")
        .OrderBy(m => m.CreatedAt)
        .Take(20)
        .ToListAsync(ct);

    foreach (var msg in messages)
    {
        var eventType = Type.GetType(msg.EventType);
        var payload   = JsonSerializer.Deserialize(msg.Payload, eventType);
        await bus.Publish(payload, eventType, ct);

        msg.Status      = "Published";
        msg.PublishedAt = DateTimeOffset.UtcNow;
    }

    await db.SaveChangesAsync(ct);  // batch mark-published in one round-trip
}
```

**Design notes:**
- `IServiceScopeFactory` is used because `BackgroundService` is registered as a singleton while `AppointmentDbContext` is scoped — creating a new scope per batch is the idiomatic ASP.NET Core pattern.
- Retries are tracked via `RetryCount`; after 5 failures, `Status` is set to `"Failed"` and the message is excluded from future polls (prevents poison-pill loops).
- Polling interval is 5 seconds — a practical balance between latency and DB load.

### Repositories

**`AppointmentRepository`** — `GetByPatientIdAsync` orders by shadow property `CreatedAt` descending, using `EF.Property<DateTimeOffset>(a, "CreatedAt")` to access the shadow property in a LINQ query.

**`IdempotencyRepository`** — simple key lookup; no complex queries needed.

### EF Migration

```
AppointmentService.Infrastructure/Migrations/
  20260404183320_InitialCreate.cs
  AppointmentDbContextModelSnapshot.cs
```

Tables created: `Appointments`, `BookingIdempotencyKeys`, `OutboxMessages`.

A `IDesignTimeDbContextFactory<AppointmentDbContext>` (`AppointmentDbContextFactory.cs`) was added to the Infrastructure project to support `dotnet ef` tooling without requiring the API host to start.

---

## T084–T086: AppointmentService API

### REST Endpoints (`AppointmentsEndpoints.cs`)

| Method | Route | Auth | Description |
|---|---|---|---|
| `POST` | `/api/appointments` | Bearer | Book appointment — reads `PatientId` from JWT claim, `Idempotency-Key` from header |
| `GET` | `/api/appointments/{id}` | Bearer | Get appointment by id |
| `DELETE` | `/api/appointments/{id}` | Bearer | Cancel appointment — requires `CallerUserId` claim match |
| `GET` | `/api/appointments/patient/{patientId}` | Bearer | List all appointments for a patient |

**Idempotency-Key enforcement at the endpoint:**
```csharp
var idempotencyKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
if (string.IsNullOrWhiteSpace(idempotencyKey))
    return Results.BadRequest("Idempotency-Key header is required.");
```

**409 mapping:**
```csharp
catch (SlotConflictException ex)
{
    return Results.Conflict(new { error = ex.Message });
}
```

### `Program.cs` — DI Wiring

Key registrations unique to AppointmentService:

```csharp
// gRPC clients with resilience pipeline
builder.Services.AddGrpcClient<PatientGrpc.PatientGrpcClient>(opts =>
    opts.Address = new Uri(config["GrpcClients:PatientService"]))
.AddHealthBookingResiliencePipeline("patient-grpc");

builder.Services.AddGrpcClient<ProviderGrpc.ProviderGrpcClient>(opts =>
    opts.Address = new Uri(config["GrpcClients:ProviderService"]))
.AddHealthBookingResiliencePipeline("provider-grpc");

builder.Services.AddScoped<IPatientGrpcClient, PatientGrpcClient>();
builder.Services.AddScoped<IProviderSlotGrpcClient, ProviderSlotGrpcClient>();

// MassTransit / RabbitMQ
builder.Services.AddMassTransit(cfg =>
{
    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(config.GetConnectionString("RabbitMq"));
        rmq.ConfigureEndpoints(ctx);
    });
});

// OutboxProcessor
builder.Services.AddHostedService<OutboxProcessor>();
```

JWT audience is `"appointment-service"`.

Health checks cover SQL Server + RabbitMQ.

---

## T087–T092: Tests

### Unit Tests (`AppointmentService.UnitTests/`)

#### `BookAppointmentCommandHandlerTests`

Four test cases covering the full booking state machine:

| Test | Scenario | Verifies |
|---|---|---|
| `Handle_NewBooking_ReturnsAppointmentDto` | Happy path | `AddAsync`, `SaveChangesAsync`, `idempotency.AddAsync` each called once |
| `Handle_DuplicateIdempotencyKey_ReturnsExistingAppointment` | Duplicate key | No gRPC calls made; returns existing appointment |
| `Handle_PatientNotFound_ThrowsInvalidOperationException` | Patient doesn't exist | `InvalidOperationException` with `"not found"` message |
| `Handle_SlotNotAvailable_ThrowsSlotConflictException` | Slot lock returns false | `SlotConflictException` thrown |

All dependencies are NSubstitute mocks; no I/O.

#### `AppointmentAggregateTests`

Six tests covering the domain state machine:

| Test | Scenario |
|---|---|
| `Book_ValidArgs_SetsPropertiesAndRaisesEvent` | Happy path factory |
| `Book_EmptyPatientName_ThrowsArgumentException` | Guard validation |
| `Cancel_BookedAppointment_UpdatesStatusAndRaisesEvent` | Valid cancel transition |
| `Cancel_AlreadyCancelled_ThrowsInvalidOperationException` | Invalid transition |
| `Complete_BookedAppointment_UpdatesStatusAndRaisesEvent` | Valid complete transition |
| `Complete_CancelledAppointment_ThrowsInvalidOperationException` | Invalid transition |

### Integration Tests (`AppointmentService.IntegrationTests/`)

Uses `Testcontainers.MsSql` (real SQL Server 2022 container):

```csharp
private readonly MsSqlContainer _sqlContainer =
    new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
```

| Test | Verifies |
|---|---|
| `BookAppointment_PersistsAndIsRetrievable` | Full save + load round-trip; all fields preserved |
| `SaveChanges_WithDomainEvent_WritesOutboxMessage` | Outbox atomicity — one booking → one `Pending` outbox row |
| `DoubleBooking_SameIdempotencyKey_ReturnsSameAppointment` | Unique DB constraint on `BookingIdempotencyKey.Key` fires `DbUpdateException` |

`TestDbContextFactory` creates a real `AppointmentDbContext` with `HttpContextAccessor` and both interceptors, matching the production DI composition.

---

## Key Design Decisions

### 1. Idempotency at Two Levels

**Level 1 — Handler check:** `IIdempotencyRepository.FindAsync` is called before any gRPC or DB write. If found, the existing `Appointment` is fetched and returned immediately — no slot is locked, no patient is looked up.

**Level 2 — DB unique constraint:** `UQ_IdempotencyKeys_Key` ensures that even if two concurrent requests pass the handler check simultaneously, only one INSERT wins. The loser gets `DbUpdateException`.

This defence-in-depth approach means the system is safe under both sequential retry scenarios and true concurrent races.

### 2. Cross-Service Failure Handling

The four-step booking saga has asymmetric failure modes:

| Step | Failure | Outcome |
|---|---|---|
| Patient gRPC | `NotFound` | 404 to caller — abort cleanly |
| Patient gRPC | Network error | Polly retries 3× then circuit breaks — 500 to caller |
| Slot lock gRPC | `ABORTED` (concurrency) | `SlotConflictException` → 409 to caller |
| Slot lock gRPC | `false` (unavailable) | `SlotConflictException` → 409 to caller |
| DB commit | Failure after slot locked | Slot stuck `Locked` — requires compensating release (future: saga with timeout) |

The current implementation handles the common cases (422 lines of code). The stuck-lock scenario is a known limitation documented for a future Week 5 saga.

### 3. OutboxProcessor Scope Pattern

`BackgroundService` is a singleton but `AppointmentDbContext` is scoped. The processor creates a new `IServiceScope` per polling cycle, ensuring:
- No `DbContext` reuse across batches (avoids stale change tracker state).
- Each batch lives in its own unit of work — a publish error on message N does not roll back messages 1..N-1 that were already committed as `Published`.

### 4. Design-Time Factory

`AppointmentDbContextFactory` (in Infrastructure, not API) allows `dotnet ef migrations add` to work without starting the API host — critical in CI where no database is available. It uses hard-coded connection string for local tooling only; production always uses the value from `appsettings.json`.

---

## File Reference

```
src/Services/AppointmentService/
  AppointmentService.Domain/
    Enums/AppointmentStatus.cs
    Events/AppointmentEvents.cs
    Entities/Appointment.cs
    Entities/BookingIdempotencyKey.cs

  AppointmentService.Application/
    Interfaces/IAppointmentInterfaces.cs
    Dtos/AppointmentDto.cs
    Commands/BookAppointment/BookAppointmentCommand.cs     ← 4-step saga handler
    Commands/CancelAppointment/CancelAppointmentCommand.cs
    Queries/GetAppointmentById/GetAppointmentByIdQuery.cs
    Queries/GetPatientAppointments/GetPatientAppointmentsQuery.cs

  AppointmentService.Infrastructure/
    Persistence/
      AppointmentDbContext.cs
      AppointmentDbContextFactory.cs                      ← design-time only
      Configurations/
        AppointmentConfiguration.cs                       ← incl. BookingIdempotencyKeyConfiguration
      Interceptors/
        AuditInterceptor.cs
        OutboxPublishingInterceptor.cs
      Repositories/
        AppointmentRepository.cs
        IdempotencyRepository.cs
      Migrations/
        20260404183320_InitialCreate.cs
    Clients/
      PatientGrpcClient.cs                                ← wraps PatientGrpc.PatientGrpcClient
      ProviderSlotGrpcClient.cs                           ← wraps ProviderGrpc.ProviderGrpcClient
    BackgroundServices/
      OutboxProcessor.cs                                  ← SQL → RabbitMQ relay
    Services/
      CurrentUserService.cs

  AppointmentService.API/
    Endpoints/AppointmentsEndpoints.cs
    Program.cs

tests/
  AppointmentService.UnitTests/
    Application/BookAppointmentCommandHandlerTests.cs
    Domain/AppointmentAggregateTests.cs
  AppointmentService.IntegrationTests/
    Persistence/AppointmentPersistenceTests.cs
    TestDbContextFactory.cs
```

---

## Configuration (appsettings additions)

```json
{
  "ConnectionStrings": {
    "AppointmentDb": "Server=localhost,1435;Database=AppointmentDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;",
    "RabbitMq": "amqp://guest:guest@localhost:5672"
  },
  "GrpcClients": {
    "PatientService":  "http://localhost:5051",
    "ProviderService": "http://localhost:5052"
  },
  "IdentityServer": {
    "BaseUrl": "http://localhost:5000"
  }
}
```

---

## Week 4 Preview — API Gateway & Integration (T093–T120)

Key additions in `001-w4-integration`:

- **YARP Reverse Proxy** — configured in `ApiGateway` project routing all three service paths.
- **Rate limiting** — fixed-window by `sub` claim with configurable limits per route.
- **JWT propagation** — gateway validates token once then forwards `Authorization` header downstream.
- **Notification consumer** — `NotificationService` subscribes to `AppointmentBookedEvent` via MassTransit and sends email.
- **docker-compose** wiring all five services + SQL Server + Redis + RabbitMQ.
- **Integration smoke tests** — `WebApplicationFactory` + `Testcontainers` for end-to-end booking flow.
