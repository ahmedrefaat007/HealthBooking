# Week 2 — Core Domain: PatientService & ProviderService
**Branch:** `001-w2-core-domain`  
**Merged from:** `001-w1-foundation`  
**Tasks:** T021–T065  
**Commit:** `d24e320`

---

## Overview

Week 2 delivers the two foundational bounded contexts of the system:

- **PatientService** — patient registration, profile management, JWT-protected REST endpoints, gRPC server (`GetPatientById`), EF Core persistence with full audit log and transactional outbox.
- **ProviderService** — provider registration, 30-minute availability slot generation, gRPC server (`GetSlotById`, `LockSlot`, `ReleaseSlot`) with optimistic concurrency, Redis cache-aside for available slots, EF Core persistence.

Both services follow identical vertical-slice architecture: `Domain → Application → Infrastructure → API`, with all cross-cutting concerns (validation, audit, outbox, resilience) handled inside the **SharedKernel**.

---

## Architecture Diagram

```
┌──────────────────────────────────────────────────────────────┐
│  ApiGateway (YARP, JWT, Rate Limiter)                        │
│    /api/patients/** → PatientService:5001                    │
│    /api/providers/** → ProviderService:5002                  │
└───────────────┬──────────────────────┬───────────────────────┘
                │ REST (HTTP/1.1)       │ REST (HTTP/1.1)
       ┌────────▼────────┐    ┌────────▼────────┐
       │  PatientService │    │  ProviderService │
       │  :5001          │    │  :5002           │
       │  gRPC :5051     │    │  gRPC :5052      │
       └────────┬────────┘    └────────┬─────────┘
                │ SQL Server (1433)     │ SQL Server (1434) + Redis
```

---

## PackageReference Summary — Week 2 additions

| Package | Version | Where |
|---|---|---|
| MassTransit | 8.5.x | Infrastructure |
| MassTransit.RabbitMQ | 8.5.x | Infrastructure |
| Microsoft.EntityFrameworkCore.SqlServer | 9.0.x | Infrastructure |
| Grpc.Net.Client | 2.x | Infrastructure |
| Microsoft.Extensions.Caching.StackExchangeRedis | 9.0.x | ProviderService.Infrastructure |
| StackExchange.Redis | 2.x | ProviderService.Infrastructure |
| Grpc.AspNetCore | 2.x (via Contracts) | HealthBooking.Contracts |
| Google.Protobuf | 3.x | HealthBooking.Contracts |
| FluentValidation | 11.x | Application |
| NSubstitute | 5.x | Test projects |
| Bogus | 35.x | Test projects |
| FluentAssertions | 7.x | Test projects |
| Testcontainers.MsSql | 4.x | Integration test projects |

---

## Proto Strategy — DRY, Single Source of Truth

All `.proto` files live in **`src/SharedKernel/HealthBooking.Contracts/Protos/`**.  
`HealthBooking.Contracts.csproj` owns both protos with `GrpcServices="Both"` (generates server base classes + client stubs in one assembly).  
Services reference `HealthBooking.Contracts` by project reference — no duplicate proto items anywhere.

```
src/SharedKernel/HealthBooking.Contracts/
  Protos/
    patient.proto   ← PatientGrpc service: GetPatientById
    provider.proto  ← ProviderGrpc service: GetSlotById, LockSlot, ReleaseSlot
```

---

## T021–T023: PatientService Domain

### `PatientService.Domain/ValueObjects/PatientValueObjects.cs`

Four value objects guard primitive obsession at the boundary:

| Type | Validation |
|---|---|
| `PatientId` | Wraps `Guid`; factory `New()` / `From(Guid)` |
| `Email` | Must contain `@` and `.`; lowercased on construction |
| `PhoneNumber` | 7–15 digits extracted after stripping non-digit chars |
| `FullName` | `FirstName` and `LastName` non-whitespace; `Full` property combines them |

### `PatientService.Domain/Events/PatientEvents.cs`

```csharp
public sealed record PatientRegisteredEvent(
    Guid PatientId, string FirstName, string LastName,
    string ContactEmail, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record PatientProfileUpdatedEvent(
    Guid PatientId, string FirstName, string LastName,
    string PhoneNumber, DateTimeOffset OccurredAt) : IDomainEvent;
```

Both are `sealed record`s for structural equality and immutability. Both implement `IDomainEvent` from SharedKernel.

### `PatientService.Domain/Entities/Patient.cs`

```csharp
public sealed class Patient : AggregateRoot        // inherits domain-event list
{
    public Guid     Id              { get; private set; }
    public string   FirstName       { get; private set; }
    public string   LastName        { get; private set; }
    public string   ContactEmail    { get; private set; }  // normalised lowercase
    public string   PhoneNumber     { get; private set; }
    public DateOnly DateOfBirth     { get; private set; }
    public DateTimeOffset RegistrationDate { get; private set; }

    private Patient() { }   // EF Core constructor

    public static Patient Register(...) { ... }   // factory, raises PatientRegisteredEvent
    public void UpdateProfile(...) { ... }          // raises PatientProfileUpdatedEvent
}
```

`Register()` passes raw strings through value-object constructors — any invalid input throws `ArgumentException` before the entity is created. This ensures the aggregate is **always valid**.

---

## T024–T028: PatientService Application

### Interfaces (`IPatientInterfaces.cs`)

```csharp
public interface IPatientRepository
{
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Patient?> GetByEmailAsync(string email, CancellationToken ct);
    Task AddAsync(Patient patient, CancellationToken ct);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IIdentityProvisioningService
{
    Task ProvisionUserAsync(Guid patientId, string email, CancellationToken ct);
}

public interface ICurrentUserService
{
    string? UserId { get; }
}
```

All interfaces belong to the **Application** layer — no EF, no HTTP, no Redis leaks into the domain.

### Commands & Queries

| Handler | Input | Output | Side Effects |
|---|---|---|---|
| `RegisterPatientCommandHandler` | `RegisterPatientCommand` | `RegisterPatientResult(Guid)` | Saves patient, calls identity provisioning |
| `UpdatePatientProfileCommandHandler` | `UpdatePatientProfileCommand` | `Unit` | Loads patient, checks caller ownership, updates |
| `GetPatientByIdQueryHandler` | `GetPatientByIdQuery(Guid)` | `PatientDto?` | Read-only |
| `GetPatientByEmailQueryHandler` | `GetPatientByEmailQuery(email)` | `PatientDto?` | Read-only |

**Duplicate-email guard** in `RegisterPatientCommandHandler`:
```csharp
if (await repository.ExistsByEmailAsync(request.Email, ct))
    throw new InvalidOperationException($"A patient with email '{request.Email}' already exists.");
```

**Owner guard** in `UpdatePatientProfileCommandHandler`:
```csharp
if (patient.Id.ToString() != request.CallerUserId && ...)
    throw new UnauthorizedAccessException("...");
```

### FluentValidation Rules

```csharp
RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
RuleFor(x => x.DateOfBirth)
    .Must(d => d < DateOnly.FromDateTime(DateTime.UtcNow))
    .WithMessage("Date of birth must be in the past.");
```

Validators are wired as a `ValidationBehavior<TRequest, TResponse>` MediatR pipeline behavior (from SharedKernel) — no controller boilerplate.

---

## T029–T035: PatientService Infrastructure

### EF Core Configuration (`PatientConfiguration.cs`)

Key decisions:

```csharp
builder.Property(p => p.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
builder.Property<DateTimeOffset>("CreatedAt").HasDefaultValueSql("SYSDATETIMEOFFSET()");
builder.HasIndex(p => p.ContactEmail).IsUnique().HasDatabaseName("UQ_Patients_Email");
```

- **`NEWSEQUENTIALID()`** — avoids index fragmentation compared to `NEWID()`.
- **Shadow properties** for audit columns (`CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy`) — keeps the domain entity clean, written only by `AuditInterceptor`.
- **Unique email index** enforced at database level as a second line of defence (application layer checks first).

### Interceptors

**`AuditInterceptor`** — `SaveChangesInterceptor` override:
- On `EntityState.Added` → sets `CreatedAt = now`, `CreatedBy = currentUser.UserId`.
- On `EntityState.Modified` → sets `ModifiedAt = now`, `ModifiedBy`, and **prevents** `CreatedAt`/`CreatedBy` from being overwritten.

**`OutboxPublishingInterceptor`** — runs in the same `SaveChanges` transaction:
```csharp
foreach (var domainEvent in aggregate.DomainEvents)
{
    context.Set<OutboxMessage>().Add(new OutboxMessage {
        EventType     = domainEvent.GetType().FullName!,
        Payload       = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        ...
    });
}
aggregate.ClearDomainEvents();
```
This guarantees exactly-once semantics for outbox writes: the patient row and the outbox message are committed atomically in a single `SaveChangesAsync`.

### `PatientDbContext`

```csharp
public sealed class PatientDbContext(
    DbContextOptions<PatientDbContext> options,
    AuditInterceptor auditInterceptor,
    OutboxPublishingInterceptor outboxInterceptor) : DbContext(options)
{
    public DbSet<Patient>       Patients      => Set<Patient>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
}
```

### `IdentityProvisioningClient` (T035)

HTTP client to IdentityServer's internal provisioning endpoint with full Polly resilience pipeline:

```csharp
builder.Services.AddHttpClient("identity-provisioning", c => {
    c.BaseAddress = new Uri(config["IdentityServer:BaseUrl"]);
})
.AddHealthBookingResiliencePipeline("identity-provisioning");
// Pipeline: timeout 10s → retry 3× exponential+jitter → circuit breaker 50%/30s
```

Provisioning failures are non-fatal at the HTTP level (swallowed with a comment for DLQ). The patient record has already been persisted. In production, a compensating SAGA would handle rollback.

### EF Migration

```
PatientService.Infrastructure/Persistence/Migrations/
  20260404180934_InitialCreate.cs
  PatientDbContextModelSnapshot.cs
```

Run via: `dotnet ef database update` on startup (called automatically from `Program.cs`).

---

## T036–T039: PatientService API

### REST Endpoints (`PatientsEndpoints.cs`)

| Method | Route | Auth | Handler |
|---|---|---|---|
| `POST` | `/api/patients/register` | Anonymous | `RegisterPatientCommand` |
| `GET` | `/api/patients/{id}` | Bearer | `GetPatientByIdQuery` |
| `PUT` | `/api/patients/{id}` | Bearer | `UpdatePatientProfileCommand` |
| `GET` | `/api/patients/me` | Bearer | `GetPatientByEmailQuery` via email claim |

All wired as Minimal API endpoint groups — no controllers needed.

### gRPC Service (`PatientGrpcService.cs`)

Implements `PatientGrpc.PatientGrpcBase` generated from `patient.proto`:

```proto
service PatientGrpc {
  rpc GetPatientById (GetPatientByIdRequest) returns (PatientResponse);
}

message PatientResponse {
  string patient_id    = 1;
  string full_name     = 2;   // "FirstName LastName" combined
  string contact_email = 3;
}
```

Returns only `full_name` and `contact_email` (not individual fields) by design — PII minimisation for internal service-to-service calls.

### `Program.cs` — DI Wiring

```csharp
builder.Services.AddDbContext<PatientDbContext>(...);
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddScoped<OutboxPublishingInterceptor>();
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddHttpClient("identity-provisioning").AddHealthBookingResiliencePipeline(...);
builder.Services.AddScoped<IIdentityProvisioningService, IdentityProvisioningClient>();
builder.Services.AddMediatR(...).AddBehavior(ValidationBehavior<,>);
builder.Services.AddValidatorsFromAssembly(...);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts => { opts.Authority = ...; ValidAudiences = ["patient-service"]; });
builder.Services.AddGrpc();
builder.Services.AddHealthChecks().AddSqlServer(..., tags: ["ready"]);
```

Auto-migration on startup:
```csharp
using var scope = app.Services.CreateScope();
await scope.ServiceProvider.GetRequiredService<PatientDbContext>().Database.MigrateAsync();
```

---

## T040–T041: PatientService Tests

### Unit Tests (`PatientService.UnitTests/`)

**`RegisterPatientCommandHandlerTests`**
- `Handle_NewPatient_ReturnsPatientId` — verifies `AddAsync`, `SaveChangesAsync`, and `ProvisionUserAsync` are all called exactly once.
- `Handle_DuplicateEmail_ThrowsInvalidOperationException` — verifies `AddAsync` is **not** called.

**`PatientAggregateTests`**
- `Register_ValidArgs_SetsPropertiesAndRaisesEvent`
- `Register_EmptyFirstName_Throws`
- `Register_InvalidEmail_Throws`
- `UpdateProfile_RaisesProfileUpdatedEvent_AndClearsOldEvents`
- `ClearDomainEvents_RemovesAllEvents`

**`ValueObjectTests`**
- Email: valid/invalid formats; normalization to lowercase.
- PhoneNumber: valid digit counts; too-short throws.
- FullName: empty first name throws; `Full` concatenation.

### Integration Tests (`PatientService.IntegrationTests/`)

**`PatientPersistenceTests`** — uses `Testcontainers.MsSql` (real SQL Server container):
- `AddAsync_NewPatient_PersistsAndRetrievable` — save + load round-trip.
- `AddAsync_DuplicateEmail_ThrowsDueToUniqueConstraint` — database-level enforcement.
- `SaveChanges_WithDomainEvents_WritesOutboxMessage` — atomicity of entity + outbox in one `SaveChanges`.

---

## T042–T065: ProviderService

### Domain (T042–T044)

#### `ValueObjects/ProviderValueObjects.cs`
| Type | Rule |
|---|---|
| `ProviderId` | Wraps `Guid` |
| `SpecialtyName` | Non-empty, max 100 chars |
| `SlotId` | Wraps `Guid` |

#### `Enums/SlotStatus.cs`
```csharp
public enum SlotStatus { Available = 0, Locked = 1, Booked = 2, Cancelled = 3 }
```

#### `Entities/AvailabilitySlot.cs`
- `DurationMinutes` always `30` (ensured by factory + DB check constraint).
- `RowVersion` byte array for **optimistic concurrency** — prevents two AppointmentService instances from double-booking the same slot.
- State machine: `Available → Locked → Booked | Available`.

```csharp
public void Lock(Guid appointmentId) {
    if (Status != SlotStatus.Available) throw new InvalidOperationException(...);
    Status = SlotStatus.Locked; AppointmentId = appointmentId;
}
public void Release() {
    if (Status != SlotStatus.Locked) throw new InvalidOperationException(...);
    Status = SlotStatus.Available; AppointmentId = null;
}
```

#### `Entities/Provider.cs`

`DefineDailyAvailability` generates non-overlapping 30-min slots:

```csharp
public IReadOnlyList<AvailabilitySlot> DefineDailyAvailability(
    DateOnly date, TimeOnly startTime, TimeOnly endTime)
{
    // Guard: endTime > startTime
    // Load existing non-cancelled slots for that date
    // Cursor loop: if slot (cursor, cursor+30) doesn't overlap existing → create
    // Raise AvailabilityDefinedEvent with count
}
```

**Overlap guard** ensures idempotent re-runs (e.g. calling the same time range twice produces zero new slots on the second call).

---

### Application (T045–T051)

#### Interfaces (`IProviderInterfaces.cs`)
```csharp
IProviderRepository  — GetByIdAsync, ExistsByLicenseAsync, AddAsync, SaveChangesAsync
ISlotRepository      — GetByIdAsync, GetByProviderAndDateAsync, GetAvailableByProviderAsync,
                       AddRangeAsync, SaveChangesAsync
ICacheService        — GetAsync<T>, SetAsync<T>, RemoveAsync
ICurrentUserService  — UserId
```

#### `RegisterProviderCommand`
Mirror of `RegisterPatientCommand`. Checks `ExistsByLicenseAsync` for duplicate license guard.

#### `DefineAvailabilityCommand`
Calls `provider.DefineDailyAvailability(...)`, then invalidates the Redis cache key `slots:{providerId}`.

**Validator** enforces `Date >= today`:
```csharp
RuleFor(x => x.Date)
    .Must(d => d >= DateOnly.FromDateTime(DateTime.UtcNow))
    .WithMessage("Availability date must be today or in the future.");
```

#### `GetProviderSlotsQuery` — Cache-Aside Pattern

```csharp
var cached = await cache.GetAsync<List<SlotDto>>(key, ct);
if (cached is not null) return cached;

var available = await slots.GetAvailableByProviderAsync(request.ProviderId, ct);
// ... map to DTOs ...
await cache.SetAsync(key, dtos, ttl: TimeSpan.FromSeconds(60), ct);
return dtos;
```

TTL = 60 seconds. Cache is invalidated on `DefineAvailabilityCommand` (new slots added) and implicitly stale on slot status changes.

---

### Infrastructure (T052–T059)

#### `ProviderConfiguration.cs`
- `NEWSEQUENTIALID()` primary key, `SYSDATETIMEOFFSET()` audit columns.
- `UQ_Providers_LicenseNumber` unique index.
- `HasMany(p => p.Slots)...HasForeignKey(s => s.ProviderId).OnDelete(DeleteBehavior.Cascade)`.
- `.AutoInclude(false)` — slots are NOT loaded by default; use `ISlotRepository` when needed.

#### `AvailabilitySlotConfiguration.cs`
```csharp
builder.ToTable("AvailabilitySlots",
    t => t.HasCheckConstraint("CK_Slots_Duration", "[DurationMinutes] = 30"));

builder.Property(s => s.RowVersion).IsRowVersion();  // optimistic concurrency token

builder.HasIndex(s => new { s.ProviderId, s.Date, s.StartTime })
    .IsUnique()
    .HasFilter("[Status] <> 'Cancelled'")            // partial unique index
    .HasDatabaseName("UQ_Slots_Provider_Date_Start");
```

The **partial unique index** (`HasFilter`) allows a new `Available` slot to replace a `Cancelled` one at the same time — necessary for re-scheduling.

#### `RedisCacheService`

```csharp
public sealed class RedisCacheService(IDistributedCache cache) : ICacheService
{
    public async Task<T?> GetAsync<T>(string key, ...) where T : class {
        var json = await cache.GetStringAsync(key, ct);
        return json is null ? null : JsonSerializer.Deserialize<T>(json);
    }
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, ...) where T : class {
        await cache.SetStringAsync(key, JsonSerializer.Serialize(value),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
    }
}
```

Uses `IDistributedCache` (ASP.NET Core abstraction) wired to `StackExchangeRedisCache` — making the implementation easily swappable in tests (use `MemoryDistributedCache`).

### EF Migration

```
ProviderService.Infrastructure/Persistence/Migrations/
  20260404181834_InitialCreate.cs
  ProviderDbContextModelSnapshot.cs
```

---

### API (T060–T062)

#### REST Endpoints (`ProvidersEndpoints.cs`)

| Method | Route | Handler |
|---|---|---|
| `POST` | `/api/providers/register` | `RegisterProviderCommand` |
| `GET` | `/api/providers/{id}` | `GetProviderByIdQuery` |
| `GET` | `/api/providers/{id}/slots` | `GetProviderSlotsQuery` |
| `POST` | `/api/providers/{id}/availability` | `DefineAvailabilityCommand` |

#### gRPC Service (`ProviderGrpcService.cs`)

Implements `ProviderGrpc.ProviderGrpcBase` from `provider.proto`:

```proto
service ProviderGrpc {
  rpc GetSlotById  (GetSlotRequest)    returns (SlotResponse);
  rpc LockSlot     (LockSlotRequest)  returns (LockSlotResponse);
  rpc ReleaseSlot  (ReleaseSlotRequest) returns (ReleaseSlotResponse);
}
```

**`LockSlot` optimistic concurrency handling:**
```csharp
catch (DbUpdateConcurrencyException)
{
    throw new RpcException(new Status(StatusCode.Aborted, "Slot was modified concurrently. Retry."));
}
```
AppointmentService (Week 3) will retry on `StatusCode.Aborted` using its resilience pipeline.

---

### Tests (T063–T065)

**`RegisterProviderCommandHandlerTests`**
- Success path + duplicate license path.

**`ProviderAggregateTests`**
- `Register_ValidArgs_SetsPropertiesAndRaisesEvent`
- `Register_EmptySpecialty_Throws`
- `DefineDailyAvailability_NoOverlap_CreatesExpectedSlots` — 9:00–11:00 → 4 × 30-min slots
- `DefineDailyAvailability_EndBeforeStart_Throws`

**`AvailabilitySlotTests`**
- Lock available → `Locked`; AppointmentId set.
- Lock already-locked → `InvalidOperationException`.
- Release locked → `Available`; AppointmentId `null`.

---

## Key Design Decisions

### 1. Shared Proto Ownership
`HealthBooking.Contracts` owns **all** proto files and generates both client stubs and server base classes with `GrpcServices="Both"`. No service has its own proto; all use the shared package. This enforces a single API contract across the solution.

### 2. Transactional Outbox Pattern
Domain events are never published directly to RabbitMQ. They are serialised to `OutboxMessage` rows in the **same SQL transaction** as the business entity. A background `OutboxProcessor` (Week 3) polls and publishes. This prevents the two-phase-commit problem.

### 3. Optimistic Concurrency for Slots
`AvailabilitySlot.RowVersion` mapped via `IsRowVersion()` automatically appends a `WHERE RowVersion = @original` clause on `UPDATE`. If two requests race to lock the same slot, the second gets a `DbUpdateConcurrencyException` which is translated to gRPC `ABORTED` — safe to retry.

### 4. No AutoInclude on Slots
`provider.Navigation(p => p.Slots).AutoInclude(false)` — prevents accidental N+1 when dozens of providers are listed. Slots are always queried through `ISlotRepository` with explicit filters.

### 5. Validation Pipeline
`ValidationBehavior<TRequest, TResponse>` in SharedKernel runs all registered `IValidator<T>` implementations before each handler. A command with an invalid email address never reaches the repository.

---

## File Reference

```
src/SharedKernel/HealthBooking.Contracts/
  Protos/
    patient.proto                                  ← PatientGrpc contract
    provider.proto                                 ← ProviderGrpc contract

src/Services/PatientService/
  PatientService.Domain/
    Entities/Patient.cs
    Events/PatientEvents.cs
    ValueObjects/PatientValueObjects.cs
  PatientService.Application/
    Interfaces/IPatientInterfaces.cs
    Dtos/PatientDto.cs
    Commands/RegisterPatient/RegisterPatientCommand.cs
    Commands/UpdatePatientProfile/UpdatePatientProfileCommand.cs
    Queries/GetPatientById/GetPatientByIdQuery.cs
    Queries/GetPatientByEmail/GetPatientByEmailQuery.cs
  PatientService.Infrastructure/
    Clients/IdentityProvisioningClient.cs
    Persistence/Configurations/PatientConfiguration.cs
    Persistence/Interceptors/AuditInterceptor.cs
    Persistence/Interceptors/OutboxPublishingInterceptor.cs
    Persistence/PatientDbContext.cs
    Persistence/Repositories/PatientRepository.cs
    Persistence/Migrations/20260404180934_InitialCreate.cs
    Services/CurrentUserService.cs
  PatientService.API/
    Endpoints/PatientsEndpoints.cs
    Grpc/PatientGrpcService.cs
    Program.cs

src/Services/ProviderService/
  ProviderService.Domain/
    Enums/SlotStatus.cs
    Entities/Provider.cs
    Entities/AvailabilitySlot.cs
    Events/ProviderEvents.cs
    ValueObjects/ProviderValueObjects.cs
  ProviderService.Application/
    Interfaces/IProviderInterfaces.cs
    Dtos/ProviderDtos.cs
    Commands/RegisterProvider/RegisterProviderCommand.cs
    Commands/DefineAvailability/DefineAvailabilityCommand.cs
    Queries/GetProviderById/GetProviderByIdQuery.cs
    Queries/GetProviderSlots/GetProviderSlotsQuery.cs
  ProviderService.Infrastructure/
    Persistence/Configurations/ProviderConfiguration.cs    ← incl. AvailabilitySlotConfiguration
    Persistence/Interceptors/AuditInterceptor.cs
    Persistence/Interceptors/OutboxPublishingInterceptor.cs
    Persistence/ProviderDbContext.cs
    Persistence/Repositories/ProviderRepository.cs
    Persistence/Repositories/SlotRepository.cs
    Persistence/Migrations/20260404181834_InitialCreate.cs
    Services/CurrentUserService.cs
    Services/RedisCacheService.cs
  ProviderService.API/
    Endpoints/ProvidersEndpoints.cs
    Grpc/ProviderGrpcService.cs
    Program.cs

tests/
  PatientService.UnitTests/
    Application/RegisterPatientCommandHandlerTests.cs
    Domain/PatientAggregateTests.cs
    Domain/ValueObjectTests.cs
  PatientService.IntegrationTests/
    Auth/AuthSmokeTests.cs            ← from Week 1
    Persistence/PatientPersistenceTests.cs
  ProviderService.UnitTests/
    Application/RegisterProviderCommandHandlerTests.cs
    Domain/ProviderAggregateTests.cs
  ProviderService.IntegrationTests/
    (scaffolded — code in Week 3)
```

---

## Connection Strings (docker-compose.override)

| Service | DB | Port |
|---|---|---|
| PatientService | PatientDb | 1433 |
| ProviderService | ProviderDb | 1434 |
| AppointmentService | AppointmentDb | 1435 |
| NotificationService | NotificationDb | 1436 |
| IdentityServer | IdentityDb | 1437 |
| ProviderService | Redis | 6379 |

---

## Week 3 Preview — AppointmentService (T066–T092)

Key additions in `001-w3-scheduling`:

- `Appointment` aggregate with `AppointmentStatus` state machine.
- `BookingIdempotencyKey` entity — prevents duplicate booking from network retries.
- `BookAppointmentCommandHandler` — 4-step saga:
  1. Idempotency key check
  2. `PatientGrpcClient.GetPatientById` 
  3. `ProviderSlotGrpcClient.LockSlot` — returns `ABORTED` if slot taken → `ConflictException`
  4. Persist `Appointment` + outbox in single `SaveChanges`
- `OutboxProcessor` BackgroundService — polls `OutboxMessages` where `Status = 'Pending'`, publishes to RabbitMQ via MassTransit, marks `Published`.
- Concurrent double-booking integration test — two parallel HTTP clients racing to book same slot, expect one 201 and one 409.
