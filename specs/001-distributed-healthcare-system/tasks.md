# Tasks: HealthBooking Gap-Remediation Sprint

**Feature Branch**: `001-distributed-healthcare-system`  
**Plan Version**: 1.1.0 | **Sprint**: Week 7 Gap-Remediation | **Generated**: 2026-04-04  
**Constitution Version**: 1.0.0  
**Plan Source**: `specs/001-distributed-healthcare-system/plan.md`  
**Target**: 22/37 FRs (59%) → 34/37 FRs (92%)

---

## Label Key

| Label | Scope |
|---|---|
| `[DOMAIN]` | Domain entities, aggregates, value objects, domain events |
| `[APP]` | Application layer — CQRS commands, queries, handlers, validators |
| `[INFRA]` | Infrastructure — EF Core config, migrations, consumers, MassTransit wiring |
| `[API]` | API endpoints, Program.cs DI wiring |
| `[TEST]` | Unit and integration tests |
| `[OPS]` | CI pipeline, dev ops |

`[P]` — task can be executed in parallel with sibling tasks (different files, no shared state).

---

## Phase 1: Setup (Foundational)

**Purpose**: EF Core schema changes that all story phases depend on — must complete before any command handlers are written.

### Independent Test Criteria
Run `dotnet ef migrations list` in AppointmentService.Infrastructure — migration `AddScheduledStartAndUniqueSlotId` must appear.

- [ ] T001 [INFRA] Add `ScheduledStartUtc` mapping and `IsUnique` on SlotId in `src/Services/AppointmentService/AppointmentService.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs`
- [ ] T002 [INFRA] Run EF Core migration: `dotnet ef migrations add AddScheduledStartAndUniqueSlotId --project AppointmentService.Infrastructure --startup-project AppointmentService.API` in `src/Services/AppointmentService/`

---

## Phase 2: User Story 4 — Appointment Lifecycle Management (FR-015, FR-014, FR-016)

**Story Goal**: A patient or doctor can view, reschedule, or cancel an existing appointment. Confirm and NoShow transitions allow providers to complete the lifecycle.

**Spec Reference**: US4 (P2) — FR-015 (reschedule), FR-014 (2h cancellation window), FR-016 (Confirm, NoShow)

### Independent Test Criteria
POST `/api/appointments/{id}/reschedule` with a valid new slot → 200 OK; slot released (old) and locked (new) via gRPC.  
POST `/api/appointments/{id}/confirm` on a Booked appointment → 204; status = Confirmed.  
POST `/api/appointments/{id}/no-show` on a Confirmed appointment → 204; status = NoShow.  
DELETE `/api/appointments/{id}` with appointment < 2h away → 400 "cannot cancel within notice window".

### Domain Tasks

- [ ] T003 [DOMAIN] Add `ScheduledStartUtc` property and update `Book(Guid, Guid, string, DateTimeOffset)` signature in `src/Services/AppointmentService/AppointmentService.Domain/Entities/Appointment.cs`
- [ ] T004 [P] [DOMAIN] Add `AppointmentRescheduledDomainEvent`, `AppointmentConfirmedDomainEvent`, `AppointmentNoShowDomainEvent` to `src/Services/AppointmentService/AppointmentService.Domain/Events/AppointmentEvents.cs`
- [ ] T005 [DOMAIN] Implement `Reschedule(Guid newSlotId, DateTimeOffset newStart)`, `Confirm()`, `MarkNoShow()` on `Appointment` in `src/Services/AppointmentService/AppointmentService.Domain/Entities/Appointment.cs`

### Infrastructure Tasks

- [ ] T006 [INFRA] Update `PersistAppointmentActivity` to inject `IProviderSlotGrpcClient`, call `GetSlotByIdAsync`, and pass `ScheduledStartUtc` to `Appointment.Book()` in `src/Services/AppointmentService/AppointmentService.Application/Saga/Activities/PersistAppointmentActivity.cs`

### Application Tasks

- [ ] T007 [P] [APP] Create `RescheduleAppointmentCommand` (record + validator + handler: release old slot, lock new slot, call `appointment.Reschedule()`, SaveChanges) in `src/Services/AppointmentService/AppointmentService.Application/Commands/RescheduleAppointment/RescheduleAppointmentCommand.cs`
- [ ] T008 [P] [APP] Create `ConfirmAppointmentCommand` (record + validator + handler: call `appointment.Confirm()`, SaveChanges) in `src/Services/AppointmentService/AppointmentService.Application/Commands/ConfirmAppointment/ConfirmAppointmentCommand.cs`
- [ ] T009 [P] [APP] Create `MarkNoShowCommand` (record + validator + handler: call `appointment.MarkNoShow()`, SaveChanges) in `src/Services/AppointmentService/AppointmentService.Application/Commands/MarkNoShow/MarkNoShowCommand.cs`
- [ ] T010 [APP] Update `CancelAppointmentCommand` handler to reject cancellations within 2h of `ScheduledStartUtc` using `IConfiguration["Appointment:CancellationNoticeHours"]` in `src/Services/AppointmentService/AppointmentService.Application/Commands/CancelAppointment/CancelAppointmentCommand.cs`
- [ ] T011 [OPS] Add `"Appointment": { "CancellationNoticeHours": 2 }` to `src/Services/AppointmentService/AppointmentService.API/appsettings.json`

### API Tasks

- [ ] T012 [API] Add `PUT /{id}/reschedule`, `POST /{id}/confirm`, `POST /{id}/no-show` endpoints to `src/Services/AppointmentService/AppointmentService.API/Endpoints/AppointmentsEndpoints.cs`

---

## Phase 3: User Story 3 — Booking Conflict Prevention (FR-010)

**Story Goal**: When an appointment is confirmed or rescheduled, ProviderService receives the event and transitions the slot to `Booked` or `Available`. Without these consumers, slots remain `Locked` permanently.

**Spec Reference**: US3 (P1) — FR-010, US4 (P2) — FR-015 (slot release on reschedule)

### Independent Test Criteria
Publish a `V1_AppointmentBookedEvent` on the bus → `AvailabilitySlot.Status` transitions to `Booked` in ProviderDB.  
Publish a `V1_SlotReleasedEvent` → slot status transitions to `Available`.

### Infrastructure Tasks (ProviderService)

- [ ] T013 [INFRA] Create folder `src/Services/ProviderService/ProviderService.Infrastructure/Messaging/Consumers/` and implement `AppointmentBookedConsumer` (idempotency guard via `AppointmentId == slot.AppointmentId`, call `slot.Book()`, cache invalidate) in that folder
- [ ] T014 [P] [INFRA] Implement `SlotReleasedConsumer` (idempotency guard, call `slot.Release()`, cache invalidate) in `src/Services/ProviderService/ProviderService.Infrastructure/Messaging/Consumers/SlotReleasedConsumer.cs`
- [ ] T015 [API] Wire MassTransit in `src/Services/ProviderService/ProviderService.API/Program.cs`: add `AddMassTransit()` block registering both consumers, RabbitMQ host, `AddRabbitMQ` health check

---

## Phase 4: User Story 5 — Real-Time Notification Delivery (FR-018, FR-020)

**Story Goal**: Notifications are delivered reliably (with retry) for booking, cancellation, AND rescheduling. Messages are never silently dropped on transient failure.

**Spec Reference**: US5 (P2) — FR-018, FR-020

### Independent Test Criteria
Publish `V1_AppointmentRescheduledEvent` on bus → `AppointmentRescheduledConsumer` creates a `NotificationLog` row.  
Simulate transient consumer fault → message retried up to 5× before going to error queue.

### Application Tasks (NotificationService)

- [ ] T016 [APP] Create `AppointmentRescheduledConsumer` following `AppointmentBookedConsumer` pattern (idempotency guard, build reschedule email, mark sent/failed) in `src/Services/NotificationService/NotificationService.Application/Consumers/AppointmentRescheduledConsumer.cs`
- [ ] T017 [API] Register `AppointmentRescheduledConsumer` in `src/Services/NotificationService/NotificationService.API/Program.cs`

### Infrastructure Tasks (Retry Policy)

- [ ] T018 [P] [INFRA] Add `cfg.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)))` inside `UsingRabbitMq` in `src/Services/AppointmentService/AppointmentService.API/Program.cs`
- [ ] T019 [P] [INFRA] Add same retry policy to `src/Services/NotificationService/NotificationService.API/Program.cs`
- [ ] T020 [P] [INFRA] Add same retry policy to `src/Services/ProviderService/ProviderService.API/Program.cs` (included as part of MassTransit wiring in T015)

---

## Phase 5: User Story 1 — PII Masking (FR-005, SC-008)

**Story Goal**: No patient PII (name, email, phone) appears in Serilog logs or OpenTelemetry spans. SC-008 requires zero PII in logs/traces.

**Spec Reference**: US1 (P1) — FR-005, SC-008

### Independent Test Criteria
Log a structured event containing a `PatientInfo` or `PatientResponse` object → masked fields show `***` in console/Seq output.

### API Tasks

- [ ] T021 [P] [API] Add Serilog `Destructure.ByTransforming<PatientInfo>()` masking first/last name and email to `src/Services/PatientService/PatientService.API/Program.cs`
- [ ] T022 [P] [API] Add same Serilog destructuring for `PatientInfo` to `src/Services/AppointmentService/AppointmentService.API/Program.cs`
- [ ] T023 [P] [API] Add same Serilog destructuring for `PatientInfo` to `src/Services/NotificationService/NotificationService.API/Program.cs`

---

## Phase 6: User Story 6 — Observability (FR-035, SC-009)

**Story Goal**: CI pipeline enforces ≥80% test coverage and fails the build when coverage drops below the threshold.

**Spec Reference**: US6 (P3) — FR-035, SC-009

### Independent Test Criteria
Push a commit that drops coverage below 80% → CI build fails with non-zero exit code and coverage report shows threshold breach.

### OPS Tasks

- [ ] T024 [OPS] Add ReportGenerator coverage threshold step to `.github/workflows/ci.yml`: run `reportgenerator` with `-reporttypes:TextSummary` and exit non-zero if line coverage < 80%

---

## Phase 7: Tests

**Purpose**: Validate all new domain methods and command handlers.

### Unit Test Tasks — AppointmentService

- [ ] T025 [P] [TEST] Add `Reschedule_ValidStatus_UpdatesSlotAndRaisesEvent`, `Reschedule_WrongStatus_Throws`, `Confirm_BookedStatus_SetsConfirmed`, `Confirm_WrongStatus_Throws`, `MarkNoShow_ConfirmedStatus_SetsNoShow`, `MarkNoShow_WrongStatus_Throws` to `tests/AppointmentService.UnitTests/Domain/AppointmentAggregateTests.cs`
- [ ] T026 [P] [TEST] Add `Handle_ValidReschedule_ReleasesOldSlotLocksNewSlot`, `Handle_SlotUnavailable_ThrowsConflict` to `tests/AppointmentService.UnitTests/Application/RescheduleAppointmentCommandHandlerTests.cs` (new file)
- [ ] T027 [P] [TEST] Add `Handle_ValidCancel_NoticeWindowOk_Succeeds`, `Handle_CancelWithinNoticeWindow_Throws` to `tests/AppointmentService.UnitTests/Application/CancelAppointmentCommandHandlerTests.cs` (new file)

### Unit Test Tasks — NotificationService

- [ ] T028 [P] [TEST] Add `Consume_NewEvent_SendsEmailAndLogsSuccess`, `Consume_DuplicateEvent_SkipsSend` to `tests/NotificationService.UnitTests/AppointmentRescheduledConsumerTests.cs` (new file)

---

## Dependencies

```
T001 → T002 (migration after config)
T001 → T003 (ScheduledStartUtc config before Book() update)
T003 → T005 (entity methods after Book() updated)
T003 → T007 (reschedule handler needs ScheduledStartUtc)
T004 → T005 (domain events needed for Reschedule/Confirm/NoShow methods)
T005 → T006 (PersistAppointmentActivity passes ScheduledStartUtc)
T007, T008, T009 → T012 (handlers before endpoints)
T010 → T011 (config key needed by handler)
T013, T014 → T015 (consumers before Program.cs wiring)
T016 → T017 (consumer before registration)
T015 includes T020 (retry in same Program.cs block)
T025–T028 can execute after their respective implementation tasks
```

## Parallel Execution

**Phase 2 parallel group**: T004, T007, T008, T009, T021, T022, T023 (all different files)  
**Phase 3–4 parallel group**: T013, T014, T016, T018, T019 (all different files, no common deps)  
**Test group**: T025, T026, T027, T028 (all different files)

## Implementation Strategy

**MVP scope (minimum shippable increment)**:  
Complete Phase 1 + Phase 2 (T001–T012) → Reschedule/Confirm/NoShow working end-to-end.  
Then Phase 3 (T013–T015) → slots correctly transition via events.  
Then Phase 4 (T016–T020) → notifications reliable.  
Then Phase 5–7 (T021–T028) → security + quality gates.

**Format validation**: All tasks follow `- [ ] [TID] [Labels?] Description with file path` ✅

---

## Label Key

| Label | Scope |
|---|---|
| `[INFRA]` | Infrastructure, persistence, Docker, EF Core, messaging wiring |
| `[DOMAIN]` | Domain entities, aggregates, value objects, domain events |
| `[APP]` | Application layer — CQRS handlers, validators, interfaces |
| `[API]` | API endpoints, gRPC services, DI wiring, middleware |
| `[TEST]` | Unit tests, integration tests, CI gates |
| `[OPS]` | DevOps, observability, health checks, documentation |

`[P]` — task can be executed in parallel with sibling tasks in the same week.  
`[US1–US6]` — traceability to user story (from spec.md).

---

## ⚙️ Agent Reference (Self-Contained — Do NOT cross-reference plan.md)

> **IMPORTANT**: Every task in this file is self-contained. All NuGet packages, code templates, file paths, and verification steps are inlined. Do NOT look up `plan.md` or `spec.md` — everything you need is here.

### R1. NuGet Packages Per Project Type

Install these exact packages when creating each project type. Use `dotnet add package <Name>`.

**SharedKernel** (`HealthBooking.SharedKernel`):
```
Microsoft.EntityFrameworkCore (9.0.*)
MediatR.Contracts (2.*)
FluentValidation (11.*)
FluentValidation.DependencyInjectionExtensions (11.*)
Microsoft.Extensions.Http.Resilience (9.0.*)   # Polly 8 via MS wrapper
Serilog.Sinks.Console (6.*)
Serilog.Sinks.File (6.*)
Serilog.Expressions (4.*)
OpenTelemetry.Api (1.*)
```

**Contracts** (`HealthBooking.Contracts`):
```
Google.Protobuf (3.*)
Grpc.Tools (2.*)        # for .proto compilation
```

**Domain** (per service — e.g. `PatientService.Domain`):
```
(No NuGet — references only HealthBooking.SharedKernel)
```

**Application** (per service — e.g. `PatientService.Application`):
```
MediatR (12.*)
FluentValidation (11.*)
FluentValidation.DependencyInjectionExtensions (11.*)
(References own .Domain project + HealthBooking.SharedKernel)
```

**Infrastructure** (per service — e.g. `PatientService.Infrastructure`):
```
Microsoft.EntityFrameworkCore.SqlServer (9.0.*)
Microsoft.EntityFrameworkCore.Tools (9.0.*)
MassTransit.RabbitMQ (8.*)
MassTransit (8.*)
Grpc.Net.Client (2.*)
Microsoft.Extensions.Caching.StackExchangeRedis (9.0.*)  # ProviderService only
StackExchange.Redis (2.*)                                  # ProviderService only
(References own .Application project + HealthBooking.SharedKernel + HealthBooking.Contracts)
```

**API** (per service — e.g. `PatientService.API`):
```
Microsoft.AspNetCore.Authentication.JwtBearer (9.0.*)
Serilog.AspNetCore (9.*)
Grpc.AspNetCore (2.*)
MediatR (12.*)
AspNetCore.HealthChecks.SqlServer (8.*)
AspNetCore.HealthChecks.Rabbitmq (8.*)         # services with MassTransit consumers
AspNetCore.HealthChecks.Redis (8.*)            # ProviderService only
(References own .Infrastructure project)
```

**Test Projects**:
```
xunit (2.*)
xunit.runner.visualstudio (2.*)
Microsoft.NET.Test.Sdk (17.*)
NSubstitute (5.*)
Bogus (35.*)
FluentAssertions (7.*)
Testcontainers (4.*)
Testcontainers.MsSql (4.*)
Testcontainers.RabbitMq (4.*)
Microsoft.AspNetCore.Mvc.Testing (9.0.*)
```

**IdentityServer** (`HealthBooking.IdentityServer`):
```
Duende.IdentityServer (7.*)
Duende.IdentityServer.EntityFramework (7.*)
Microsoft.EntityFrameworkCore.SqlServer (9.0.*)
Microsoft.AspNetCore.Authentication.JwtBearer (9.0.*)
Serilog.AspNetCore (9.*)
AspNetCore.HealthChecks.SqlServer (8.*)
```

**API Gateway** (`HealthBooking.ApiGateway`):
```
Yarp.ReverseProxy (2.*)
Microsoft.AspNetCore.Authentication.JwtBearer (9.0.*)
Serilog.AspNetCore (9.*)
AspNetCore.HealthChecks.Uris (8.*)
```

### R2. Shared Base Classes (SharedKernel — implement in T006)

```csharp
// src/SharedKernel/HealthBooking.SharedKernel/Domain/IDomainEvent.cs
using MediatR;
public interface IDomainEvent : INotification { }

// src/SharedKernel/HealthBooking.SharedKernel/Domain/AuditableEntity.cs
public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt  { get; set; }
    public string         CreatedBy  { get; set; } = default!;
    public DateTimeOffset? ModifiedAt { get; set; }
    public string?        ModifiedBy { get; set; }
}

// src/SharedKernel/HealthBooking.SharedKernel/Domain/AggregateRoot.cs
public abstract class AggregateRoot : AuditableEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

// src/SharedKernel/HealthBooking.SharedKernel/Persistence/OutboxMessage.cs
public sealed class OutboxMessage
{
    public Guid   Id                  { get; init; } = Guid.NewGuid();
    public string EventType           { get; init; } = default!;
    public string SchemaVersion       { get; init; } = default!;
    public string Payload             { get; init; } = default!;
    public string DestinationExchange { get; init; } = default!;
    public DateTimeOffset CreatedAt   { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int    RetryCount          { get; set; }
    public string Status              { get; set; } = "Pending";
}

public enum OutboxStatus { Pending, Published, Failed }

// src/SharedKernel/HealthBooking.SharedKernel/Persistence/OutboxMessageConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DestinationExchange).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Pending");
        builder.Property(x => x.RetryCount).HasDefaultValue(0);
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
               .HasDatabaseName("IX_OutboxMessages_Status_CreatedAt");
    }
}
```

### R3. Reusable Pattern: AuditInterceptor (copy into EACH service's Infrastructure)

Each service gets its own copy at `{Service}.Infrastructure/Persistence/Interceptors/AuditInterceptor.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

public sealed class AuditInterceptor(ICurrentUserService currentUser) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var context = eventData.Context!;
        var now = DateTimeOffset.UtcNow;
        var userId = currentUser.UserId ?? "system";

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = userId;
            }
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = now;
                entry.Entity.ModifiedBy = userId;
                entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
            }
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}

// ICurrentUserService — in Application/Interfaces/
public interface ICurrentUserService
{
    string? UserId { get; }
}

// CurrentUserService — in Infrastructure/Services/
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string? UserId => accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
```

### R4. Reusable Pattern: OutboxPublishingInterceptor (copy into EACH service)

Each service gets its own copy at `{Service}.Infrastructure/Persistence/Interceptors/OutboxPublishingInterceptor.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

public sealed class OutboxPublishingInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var context = eventData.Context!;
        var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var outboxMessage = new OutboxMessage
                {
                    EventType = domainEvent.GetType().FullName!,
                    SchemaVersion = "1.0",
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    DestinationExchange = domainEvent.GetType().Name
                        .Replace("DomainEvent", "").ToLowerInvariant(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                context.Set<OutboxMessage>().Add(outboxMessage);
            }
            aggregate.ClearDomainEvents();
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}
```

### R5. Reusable Pattern: OutboxProcessor BackgroundService (copy into EACH service)

Each service gets its own copy at `{Service}.Infrastructure/Messaging/OutboxProcessor.cs`:

```csharp
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IBus bus,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                // REPLACE: Use the service-specific DbContext type below
                var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();

                var messages = await db.OutboxMessages
                    .Where(m => m.Status == "Pending")
                    .OrderBy(m => m.CreatedAt)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                foreach (var msg in messages)
                {
                    try
                    {
                        var eventType = Type.GetType(msg.EventType);
                        if (eventType is null) { msg.Status = "Failed"; continue; }

                        var payload = System.Text.Json.JsonSerializer.Deserialize(msg.Payload, eventType);
                        if (payload is null) { msg.Status = "Failed"; continue; }

                        await bus.Publish(payload, eventType, stoppingToken);

                        msg.Status = "Published";
                        msg.PublishedAt = DateTimeOffset.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Outbox publish failed for {MessageId}", msg.Id);
                        msg.RetryCount++;
                        if (msg.RetryCount >= 3) msg.Status = "Failed";
                    }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxProcessor cycle failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

> **When copying**: Replace `AppointmentDbContext` with the correct DbContext for each service (`PatientDbContext`, `ProviderDbContext`, `NotificationDbContext`).

### R6. Reusable Pattern: MediatR Pipeline Behaviors (implement in T009)

```csharp
// src/SharedKernel/HealthBooking.SharedKernel/Behaviors/LoggingBehavior.cs
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName}", requestName);
        var response = await next();
        logger.LogInformation("Handled {RequestName}", requestName);
        return response;
    }
}

// src/SharedKernel/HealthBooking.SharedKernel/Behaviors/ValidationBehavior.cs
using FluentValidation;
using MediatR;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}

// src/SharedKernel/HealthBooking.SharedKernel/Behaviors/PerformanceBehavior.cs
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (sw.ElapsedMilliseconds > 500)
            logger.LogWarning("Long running request: {RequestName} ({ElapsedMs} ms)",
                typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return response;
    }
}
```

### R7. Reusable Pattern: Resilience Pipeline (implement in T010)

```csharp
// src/SharedKernel/HealthBooking.SharedKernel/Extensions/ResilienceExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

public static class ResilienceExtensions
{
    public static IHttpClientBuilder AddHealthBookingResiliencePipeline(
        this IHttpClientBuilder builder, string pipelineName)
    {
        builder.AddResilienceHandler(pipelineName, pipeline =>
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(10));

            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500)
            });

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            });
        });

        return builder;
    }
}
```

### R8. Proto Files (implement in T008)

```protobuf
// src/SharedKernel/HealthBooking.Contracts/Protos/provider.proto
syntax = "proto3";
option csharp_namespace = "HealthBooking.Contracts.Grpc";
package provider;

service ProviderGrpc {
  rpc GetSlotById (GetSlotRequest) returns (SlotResponse);
  rpc LockSlot (LockSlotRequest) returns (LockSlotResponse);
  rpc ReleaseSlot (ReleaseSlotRequest) returns (ReleaseSlotResponse);
}

message GetSlotRequest { string slot_id = 1; }
message SlotResponse {
  string slot_id = 1; string provider_id = 2;
  string start_time_utc = 3; string end_time_utc = 4;
  string status = 5;
}
message LockSlotRequest { string slot_id = 1; string appointment_id = 2; }
message LockSlotResponse { bool success = 1; string error_message = 2; }
message ReleaseSlotRequest { string slot_id = 1; }
message ReleaseSlotResponse { bool success = 1; }

// src/SharedKernel/HealthBooking.Contracts/Protos/patient.proto
syntax = "proto3";
option csharp_namespace = "HealthBooking.Contracts.Grpc";
package patient;

service PatientGrpc {
  rpc GetPatientById (GetPatientByIdRequest) returns (PatientResponse);
}

message GetPatientByIdRequest { string patient_id = 1; }
message PatientResponse {
  string patient_id = 1; string full_name = 2; string contact_email = 3;
}
```

### R9. Verification Commands

After completing each task, run the appropriate command to verify:

| After | Run | Expected |
|---|---|---|
| Any code change | `dotnet build HealthBooking.sln` | `Build succeeded. 0 Warning(s). 0 Error(s).` |
| Migration created | `dotnet ef migrations list --project {Service}.Infrastructure --startup-project {Service}.API` | Migration name listed |
| Test task | `dotnet test tests/{TestProject}/ --verbosity normal` | All tests pass |
| Docker Compose | `docker compose up -d; docker compose ps` | All containers `healthy` |
| Full solution | `dotnet build; dotnet test --no-build` | 0 errors, all tests green |

### R10. Event Contract Records (implement in T007)

```csharp
// src/SharedKernel/HealthBooking.Contracts/Appointments/V1/V1_AppointmentBookedEvent.cs
namespace HealthBooking.Contracts.Appointments.V1;

public sealed record V1_AppointmentBookedEvent(
    Guid AppointmentId, Guid PatientId, Guid ProviderId, Guid SlotId,
    DateTimeOffset ScheduledStartUtc, DateTimeOffset ScheduledEndUtc,
    Guid SagaCorrelationId, DateTimeOffset OccurredAt);

public sealed record V1_AppointmentCancelledEvent(
    Guid AppointmentId, Guid PatientId, Guid ProviderId, Guid SlotId,
    string CancellationReason, Guid SagaCorrelationId, DateTimeOffset OccurredAt);

public sealed record V1_AppointmentRescheduledEvent(
    Guid AppointmentId, Guid PatientId, Guid ProviderId,
    Guid OldSlotId, Guid NewSlotId,
    DateTimeOffset NewStartUtc, DateTimeOffset NewEndUtc,
    Guid SagaCorrelationId, DateTimeOffset OccurredAt);

public sealed record V1_SlotReleasedEvent(
    Guid SlotId, Guid ProviderId, Guid AppointmentId, DateTimeOffset OccurredAt);
```

---

## Dependency Graph — User Story Completion Order

```
Week 1 (Scaffolding + Auth)
  └── Week 2a: PatientService [US1] ─────────────────────────────────────────┐
  └── Week 2b: ProviderService [US2] ────────────────────────────────────────┤
                                                                              │
                          Week 3: AppointmentService [US3, US4] ─────────────┤
                                                                              │
                              Week 4: NotificationService [US5] ─────────────┤
                                                                              │
                Week 5: API Gateway + Redis + Circuit Breakers ───────────────┤
                                                                              │
              Week 6: Observability + Full E2E Test Suite [US6] ─────────────┘
```

```
Cross-service call dependencies (runtime):
AppointmentService ──gRPC──► ProviderService (LockSlot / ReleaseSlot)
AppointmentService ──gRPC──► PatientService  (GetPatientById)

Async event dependencies (RabbitMQ):
AppointmentService ──V1_AppointmentBookedEvent──► ProviderService (slot update)
AppointmentService ──V1_AppointmentBookedEvent──► NotificationService
AppointmentService ──V1_AppointmentCancelledEvent──► ProviderService + NotificationService
AppointmentService ──V1_AppointmentRescheduledEvent──► NotificationService
```

---

## Week 1 — Foundation: Scaffolding, Identity & Gateway

**Milestone Goal**: Runnable environment. Solution skeleton exists, Docker Compose stack reaches healthy, JWT tokens can be issued and validated end-to-end.

**DoD Gate**: `docker compose up` → all `/health/ready` return 200. Auth smoke test passes in CI.

### Phase 1: Repository & Solution Structure

- [ ] T001 [INFRA] Create `HealthBooking.sln` solution file at repository root  
  _Prereq: none_  
  _✅ Verify: `dotnet sln HealthBooking.sln list` runs without error_

- [ ] T002 [P] [INFRA] Create full solution folder tree: `src/SharedKernel/`, `src/ApiGateway/`, `src/Services/PatientService/`, `src/Services/ProviderService/`, `src/Services/AppointmentService/`, `src/Services/NotificationService/`, `src/IdentityServer/`, `tests/`, `docs/`  
  _Prereq: T001_

- [ ] T003 [P] [INFRA] Add `Directory.Build.props` at repository root (`<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `LangVersion=12`)  
  _Prereq: T001_

- [ ] T004 [P] [INFRA] Add `.editorconfig` at repository root (4-space indent, max line length 120, C# naming conventions per specification)  
  _Prereq: T001_

- [ ] T005 [P] [INFRA] Add `.gitignore` (exclude `.env`, `bin/`, `obj/`, `*.user`) and commit `.env.example` with all required variables (`SQL_SA_PASSWORD`, `RABBITMQ_USER`, `RABBITMQ_PASS`, `REDIS_PASSWORD`, `IDENTITY_DB_CONN`) to repository root  
  _Prereq: T001_

### Phase 2: SharedKernel & Contracts Libraries

- [ ] T006 [DOMAIN] Scaffold `HealthBooking.SharedKernel` class library (target `netstandard2.1`) and add to solution; implement base types by copying exact code from **Reference R2** above: `AuditableEntity`, `AggregateRoot`, `IDomainEvent`, `OutboxMessage`, `OutboxStatus` enum, `OutboxMessageConfiguration` into `src/SharedKernel/HealthBooking.SharedKernel/`. Install NuGet packages listed in **Reference R1 → SharedKernel**.  
  _Prereq: T002, T003_  
  _✅ Verify: `dotnet build src/SharedKernel/HealthBooking.SharedKernel/` succeeds_

- [ ] T007 [P] [DOMAIN] Scaffold `HealthBooking.Contracts` class library (target `netstandard2.1`) and add to solution; implement all event records by copying exact code from **Reference R10** above into `src/SharedKernel/HealthBooking.Contracts/Appointments/V1/`. Install NuGet packages listed in **Reference R1 → Contracts**.  
  _Prereq: T002, T003_  
  _✅ Verify: `dotnet build src/SharedKernel/HealthBooking.Contracts/` succeeds_

- [ ] T008 [P] [INFRA] Add `.proto` contract files — copy exact code from **Reference R8** above into `src/SharedKernel/HealthBooking.Contracts/Protos/provider.proto` and `patient.proto`  
  _Prereq: T007_  
  _✅ Verify: `dotnet build src/SharedKernel/HealthBooking.Contracts/` compiles proto files; generated C# classes appear in `obj/`_

- [ ] T009 [P] [APP] Implement shared MediatR pipeline behaviors — copy exact code from **Reference R6** above into `src/SharedKernel/HealthBooking.SharedKernel/Behaviors/`: `LoggingBehavior.cs`, `ValidationBehavior.cs`, `PerformanceBehavior.cs`  
  _Prereq: T006_  
  _✅ Verify: `dotnet build src/SharedKernel/HealthBooking.SharedKernel/` succeeds_

- [ ] T010 [P] [INFRA] Implement `AddHealthBookingResiliencePipeline()` — copy exact code from **Reference R7** above into `src/SharedKernel/HealthBooking.SharedKernel/Extensions/ResilienceExtensions.cs`  
  _Prereq: T006_  
  _✅ Verify: `dotnet build src/SharedKernel/HealthBooking.SharedKernel/` succeeds_

### Phase 3: Service Project Scaffolding

- [ ] T011 [INFRA] Scaffold all four service project groups with empty class library/web projects and correct layer references; add to solution. Install NuGet packages per project type from **Reference R1** above.  
  `PatientService.Domain`, `PatientService.Application`, `PatientService.Infrastructure`, `PatientService.API` (ports: 5001);  
  `ProviderService.Domain/Application/Infrastructure/API` (5002);  
  `AppointmentService.Domain/Application/Infrastructure/API` (5003);  
  `NotificationService.Domain/Application/Infrastructure/API` (5004)  
  **Project references** (same for each service): `.Domain` → refs `SharedKernel`; `.Application` → refs `.Domain` + `SharedKernel`; `.Infrastructure` → refs `.Application` + `SharedKernel` + `Contracts`; `.API` → refs `.Infrastructure`  
  _Prereq: T002, T006, T007_  
  _✅ Verify: `dotnet build HealthBooking.sln` succeeds with 0 errors_

- [ ] T012 [P] [INFRA] Scaffold `tests/` projects: `PatientService.UnitTests`, `PatientService.IntegrationTests`, `ProviderService.UnitTests`, `ProviderService.IntegrationTests`, `AppointmentService.UnitTests`, `AppointmentService.IntegrationTests`, `NotificationService.UnitTests`, `NotificationService.IntegrationTests`. Install NuGet packages from **Reference R1 → Test Projects**. Each test project references corresponding `.Application` + `.Infrastructure` + `.API` projects.  
  _Prereq: T011_  
  _✅ Verify: `dotnet build HealthBooking.sln` succeeds_

### Phase 4: IdentityServer

- [ ] T013 [INFRA] Scaffold `HealthBooking.IdentityServer` ASP.NET Core project; add Duende IdentityServer NuGet packages; implement `Config.cs` with clients (`api-gateway`, `patient-spa`, `admin-client`), API scopes (`healthbooking-api`, `patient:read/write`, `provider:read/write`, `appointment:read/write`), identity resources in `src/IdentityServer/HealthBooking.IdentityServer/`  
  _Prereq: T002, T003_

- [ ] T014 [P] [INFRA] Configure IdentityServer `Program.cs`: add Duende services, EF Core operational/configuration stores, SeedData with test users (`alice@test.com`, `admin@test.com`), health endpoint at `/health/ready` in `src/IdentityServer/HealthBooking.IdentityServer/Program.cs`  
  _Prereq: T013_

### Phase 5: API Gateway Skeleton

- [ ] T015 [API] Scaffold `HealthBooking.ApiGateway` ASP.NET Core project; implement `Program.cs` with YARP reverse proxy, JWT bearer validation (OIDC discovery, audience `healthbooking-api`), sliding-window rate limiter (300 req/min), `CorrelationIdMiddleware` in `src/ApiGateway/HealthBooking.ApiGateway/`  
  _Prereq: T002, T003_

- [ ] T016 [P] [API] Write YARP route + cluster configuration in `src/ApiGateway/HealthBooking.ApiGateway/appsettings.json`: routes for `/api/patients/register` (anonymous), `/api/patients/{**}`, `/api/providers/{**}`, `/api/appointments/{**}` with `JwtBearer` policy; clusters with `condition: service_healthy` health checks  
  _Prereq: T015_

### Phase 6: Docker Compose & OPS

- [ ] T017 [OPS] Write `docker-compose.yml` at repository root with all infrastructure services: 4× SQL Server 2022 containers (`sqlserver-patient/provider/appointment/notification`) each with SA password, `healthcheck`, named volumes; `rabbitmq:3-management` with health check; `redis:7-alpine` with password; `jaeger:all-in-one` with OTLP ports  
  _Prereq: T005_

- [ ] T018 [P] [OPS] Write `docker-compose.override.yml` with application service definitions (identity-server, patient-service, provider-service, appointment-service, notification-service, api-gateway) referencing correct ports, `depends_on: condition: service_healthy` chains, and environment variable bindings from `.env`  
  _Prereq: T017_

- [ ] T019 [P] [OPS] Configure `dotnet format` enforcement: add `dotnet format --verify-no-changes` step to CI workflow file (`.github/workflows/ci.yml` or `azure-pipelines.yml`); add `dotnet build` and `dotnet test` stages  
  _Prereq: T003, T004_

### Phase 7: Auth Smoke Test

- [ ] T020 [TEST] Write auth smoke integration test in `tests/PatientService.IntegrationTests/Auth/AuthSmokeTests.cs`: `POST /connect/token` to IdentityServer returns valid JWT; `GET /api/patients/me` without token returns 401; `GET /api/patients/me` with valid token returns 403 (no patient record yet, not 401)  
  _Prereq: T013, T014, T015, T016_

---

## Week 2 — Core Domain: PatientService & ProviderService (US1 + US2)

**Milestone Goal**: PatientService (US1) and ProviderService (US2) fully functional with CRUD, gRPC endpoints, unit tests ≥80%, integration tests green.

**DoD Gate**: 80% coverage CI gate green. Integration tests pass with containerised SQL Server. gRPC endpoints reachable from AppointmentService scaffold.

### PatientService — Domain [US1]

- [ ] T021 [P] [DOMAIN] [US1] Implement `Patient` aggregate root in `src/Services/PatientService/PatientService.Domain/Entities/Patient.cs`: `Register()` static factory raising `PatientRegisteredEvent`; `UpdateProfile()` raising `PatientProfileUpdatedEvent`; EF Core private constructor  
  _Prereq: T006, T011_

- [ ] T022 [P] [DOMAIN] [US1] Implement value objects in `src/Services/PatientService/PatientService.Domain/ValueObjects/`: `PatientId` (wraps `Guid`), `Email` (format validation), `PhoneNumber` (E.164 guard), `FullName` (non-empty guard)  
  _Prereq: T006, T011_

- [ ] T023 [P] [DOMAIN] [US1] Implement domain events in `src/Services/PatientService/PatientService.Domain/Events/`: `PatientRegisteredEvent`, `PatientProfileUpdatedEvent` — both implementing `IDomainEvent` from SharedKernel  
  _Prereq: T006, T021_

### PatientService — Application [US1]

- [ ] T024 [APP] [US1] Define `IPatientRepository` and `IIdentityProvisioningService` interfaces in `src/Services/PatientService/PatientService.Application/Interfaces/`  
  _Prereq: T021, T022_

- [ ] T025 [APP] [US1] Implement `RegisterPatientCommand` record, `RegisterPatientCommandValidator` (FluentValidation: non-empty fields, valid email format, date-of-birth in past), and `RegisterPatientCommandHandler` in `src/Services/PatientService/PatientService.Application/Commands/RegisterPatient/`; handler calls `Patient.Register()`, persists via `IPatientRepository`, calls `IIdentityProvisioningService`  
  _Prereq: T009, T021, T022, T024_

- [ ] T026 [P] [APP] [US1] Implement `UpdatePatientProfileCommand` + validator + handler in `src/Services/PatientService/PatientService.Application/Commands/UpdatePatientProfile/`; handler loads aggregate, calls `UpdateProfile()`, saves; validates caller is owner  
  _Prereq: T025_

- [ ] T027 [P] [APP] [US1] Implement `GetPatientByIdQuery` + handler returning `PatientDto` in `src/Services/PatientService/PatientService.Application/Queries/GetPatientById/`  
  _Prereq: T024_

- [ ] T028 [P] [APP] [US1] Implement `GetPatientByEmailQuery` + handler in `src/Services/PatientService/PatientService.Application/Queries/GetPatientByEmail/`  
  _Prereq: T024_

### PatientService — Infrastructure [US1]

- [ ] T029 [INFRA] [US1] Implement `PatientConfiguration : IEntityTypeConfiguration<Patient>` in `src/Services/PatientService/PatientService.Infrastructure/Persistence/Configurations/PatientConfiguration.cs`: `builder.ToTable("Patients")`, `HasKey(p => p.Id)`, `Property(Id).HasDefaultValueSql("NEWSEQUENTIALID()")`, `Property(FirstName).HasMaxLength(100).IsRequired()`, `Property(LastName).HasMaxLength(100).IsRequired()`, `Property(DateOfBirth).HasColumnType("date").IsRequired()`, `Property(ContactEmail).HasMaxLength(256).IsRequired()`, `Property(PhoneNumber).HasMaxLength(30).IsRequired()`, `Property(RegistrationDate).HasDefaultValueSql("SYSDATETIMEOFFSET()")`, audit columns (`CreatedAt/By/ModifiedAt/By`), `HasIndex(ContactEmail).IsUnique().HasDatabaseName("UQ_Patients_Email")`  
  _Prereq: T021, T022_

- [ ] T030 [INFRA] [US1] Implement `PatientDbContext` in `src/Services/PatientService/PatientService.Infrastructure/Persistence/PatientDbContext.cs`: `DbSet<Patient>`, `DbSet<OutboxMessage>`, apply both configurations, register `AuditInterceptor` + `OutboxPublishingInterceptor` in `OnConfiguring`  
  _Prereq: T006, T029_

- [ ] T031 [INFRA] [US1] Implement `AuditInterceptor` — copy exact code from **Reference R3** above into `src/Services/PatientService/PatientService.Infrastructure/Persistence/Interceptors/AuditInterceptor.cs`; also create `ICurrentUserService` in `PatientService.Application/Interfaces/` and `CurrentUserService` in `PatientService.Infrastructure/Services/` (both from R3)  
  _Prereq: T030_  
  _✅ Verify: `dotnet build src/Services/PatientService/PatientService.Infrastructure/` succeeds_

- [ ] T032 [P] [INFRA] [US1] Implement `OutboxPublishingInterceptor` — copy exact code from **Reference R4** above into `src/Services/PatientService/PatientService.Infrastructure/Persistence/Interceptors/OutboxPublishingInterceptor.cs`  
  _Prereq: T030_  
  _✅ Verify: `dotnet build src/Services/PatientService/PatientService.Infrastructure/` succeeds_

- [ ] T033 [INFRA] [US1] Run `dotnet ef migrations add InitialCreate --project src/Services/PatientService/PatientService.Infrastructure --startup-project src/Services/PatientService/PatientService.API`; verify migration files appear in `src/Services/PatientService/PatientService.Infrastructure/Persistence/Migrations/`; add `MigrateAsync()` call to `PatientService.API/Program.cs` startup: `await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PatientDbContext>(); await db.Database.MigrateAsync();`  
  _Prereq: T030, T031, T032_  
  _✅ Verify: `dotnet ef migrations list --project src/Services/PatientService/PatientService.Infrastructure --startup-project src/Services/PatientService/PatientService.API` shows "InitialCreate"_

- [ ] T034 [P] [INFRA] [US1] Implement `PatientRepository : IPatientRepository` in `src/Services/PatientService/PatientService.Infrastructure/Persistence/Repositories/PatientRepository.cs`: `GetByIdAsync`, `GetByEmailAsync`, `AddAsync`, `ExistsByEmailAsync`, `FindByIdempotencyKeyAsync`  
  _Prereq: T024, T030_

- [ ] T035 [P] [INFRA] [US1] Implement `IdentityProvisioningClient : IIdentityProvisioningService` in `src/Services/PatientService/PatientService.Infrastructure/Clients/IdentityProvisioningClient.cs`: HTTP client to IdentityServer using `IHttpClientFactory`; apply `AddHealthBookingResiliencePipeline("identity-provisioning")`  
  _Prereq: T010, T024_

### PatientService — API [US1]

- [ ] T036 [API] [US1] Implement `PatientsEndpoints` (Minimal API) in `src/Services/PatientService/PatientService.API/Endpoints/PatientsEndpoints.cs`: `POST /api/patients/register` (anonymous), `GET /api/patients/{id}` (bearer), `PUT /api/patients/{id}` (bearer, owner-only), `GET /api/patients/me` (bearer); map MediatR commands/queries  
  _Prereq: T025, T026, T027, T028_

- [ ] T037 [P] [API] [US1] Implement PatientService gRPC service `PatientGrpcService` in `src/Services/PatientService/PatientService.API/Grpc/PatientGrpcService.cs`: `GetPatientById` RPC returning `PatientResponse` (PII fields: `FullName`, `ContactEmail` — NOT cached, NOT stored downstream)  
  _Prereq: T008, T027_

- [ ] T038 [P] [API] [US1] Add `/health/live` + `/health/ready` health check endpoints to PatientService in `src/Services/PatientService/PatientService.API/Endpoints/HealthCheckEndpoints.cs`; `/health/ready` checks SQL Server connectivity via `IHealthCheck`  
  _Prereq: T011_

- [ ] T039 [API] [US1] Wire full DI in `src/Services/PatientService/PatientService.API/Program.cs`: MediatR with pipeline behaviors, EF Core with `PatientDbContext` + interceptors, MassTransit (RabbitMQ; no consumers in Week 2), JWT bearer auth, `ICurrentUserService`, `MigrateAsync()` on startup, gRPC service, Serilog  
  _Prereq: T033, T034, T035, T036, T037, T038_

### PatientService — Tests [US1]

- [ ] T040 [TEST] [US1] Write PatientService unit tests in `tests/PatientService.UnitTests/`: `RegisterPatientCommandHandler` success + duplicate-email failure; `UpdatePatientProfileCommandHandler` success + unauthorized; `GetPatientByIdQuery` hit + not-found; `GetPatientByEmailQuery`; `Patient` aggregate state machine; `Email`/`PhoneNumber` value object guards — all with Bogus test data, NSubstitute mocks; ≥80% Domain + Application line coverage  
  _Prereq: T025, T026, T027, T028_

- [ ] T041 [P] [TEST] [US1] Write PatientService integration tests in `tests/PatientService.IntegrationTests/`: persistence round-trip (register → retrieve from SQL Server via TestContainers); `AuditInterceptor` populates `CreatedAt/By` on insert; duplicate email → 409 from API; outbox row written in same transaction as patient row; JWT 401 on unauthenticated request  
  _Prereq: T039, T040_

### ProviderService — Domain [US2]

- [ ] T042 [P] [DOMAIN] [US2] Implement `Provider` aggregate root in `src/Services/ProviderService/ProviderService.Domain/Entities/Provider.cs`: `Create()` factory; `AddSpecialization()`; `DefineDailyAvailability()` generating list of 30-minute `AvailabilitySlot` entities with overlap guard raising `DomainException`  
  _Prereq: T006, T011_

- [ ] T043 [P] [DOMAIN] [US2] Implement `AvailabilitySlot` entity in `src/Services/ProviderService/ProviderService.Domain/Entities/AvailabilitySlot.cs`: `Create()` factory; `MarkBooked()` with `SlotStatus` guard raising `DomainException`; `Release()`; `SlotStatus` enum (`Available`, `Booked`, `Blocked`); `RowVersion` property for optimistic concurrency  
  _Prereq: T006, T011_

- [ ] T044 [P] [DOMAIN] [US2] Implement domain events in `src/Services/ProviderService/ProviderService.Domain/Events/`: `SlotBookedEvent`, `SlotReleasedEvent`, `ProviderCreatedEvent`, `AvailabilityWindowCancelledEvent`  
  _Prereq: T006, T042, T043_

- [ ] T045 [P] [DOMAIN] [US2] Implement value objects in `src/Services/ProviderService/ProviderService.Domain/ValueObjects/`: `ProviderId`, `SlotId`  
  _Prereq: T006, T011_

### ProviderService — Application [US2]

- [ ] T046 [APP] [US2] Define interfaces in `src/Services/ProviderService/ProviderService.Application/Interfaces/`: `IProviderRepository`, `ISlotRepository`, `ICacheService` (with `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`, `RemoveByPatternAsync`)  
  _Prereq: T042, T043_

- [ ] T047 [APP] [US2] Implement `CreateProviderCommand` + validator + handler in `src/Services/ProviderService/ProviderService.Application/Commands/CreateProvider/`  
  _Prereq: T009, T042, T046_

- [ ] T048 [P] [APP] [US2] Implement `UpdateProviderProfileCommand` + validator + handler in `src/Services/ProviderService/ProviderService.Application/Commands/UpdateProviderProfile/`  
  _Prereq: T047_

- [ ] T049 [P] [APP] [US2] Implement `DefineAvailabilityCommand` + validator + handler in `src/Services/ProviderService/ProviderService.Application/Commands/DefineAvailability/`; handler calls `Provider.DefineDailyAvailability()`, persists all generated slots atomically  
  _Prereq: T042, T046_

- [ ] T050 [P] [APP] [US2] Implement `CancelAvailabilityWindowCommand` + validator + handler in `src/Services/ProviderService/ProviderService.Application/Commands/CancelAvailabilityWindow/`; releases slots, raises `AvailabilityWindowCancelledEvent` for each booked slot  
  _Prereq: T044, T046_

- [ ] T051 [P] [APP] [US2] Implement `GetProviderByIdQuery` + handler with cache-aside (`ICacheService` key: `provider:profile:{id}`, TTL 300 s) in `src/Services/ProviderService/ProviderService.Application/Queries/GetProviderById/`  
  _Prereq: T046_

- [ ] T052 [P] [APP] [US2] Implement `SearchProvidersBySpecializationQuery` + handler in `src/Services/ProviderService/ProviderService.Application/Queries/SearchProviders/`; cache key: `provider:search:{specialization}:{from}:{to}`, TTL 60 s  
  _Prereq: T046_

- [ ] T053 [P] [APP] [US2] Implement `GetProviderSlotsQuery` + handler in `src/Services/ProviderService/ProviderService.Application/Queries/GetProviderSlots/`; handler logic: (1) build cache key `$"provider:slots:{request.ProviderId}:{request.DateUtc:yyyyMMdd}"`, (2) call `_cache.GetAsync<List<SlotDto>>(cacheKey)` → if not null, return cached, (3) otherwise call `_slotRepository.GetAvailableSlotsByProviderAndDateAsync()`, (4) call `_cache.SetAsync(cacheKey, dtos, TimeSpan.FromSeconds(60))`, (5) return dtos  
  _Prereq: T046_

### ProviderService — Infrastructure [US2]

- [ ] T054 [INFRA] [US2] Implement `ProviderConfiguration` + `AvailabilitySlotConfiguration` in `src/Services/ProviderService/ProviderService.Infrastructure/Persistence/Configurations/`:
  **ProviderConfiguration**: `ToTable("Providers")`, `HasKey(Id)`, `Property(Id).HasDefaultValueSql("NEWSEQUENTIALID()")`, `Property(FirstName).HasMaxLength(100)`, `Property(LastName).HasMaxLength(100)`, `Property(LicenseNumber).HasMaxLength(100)`, `Property(ContactEmail).HasMaxLength(256)`, audit columns, `HasIndex(LicenseNumber).IsUnique().HasDatabaseName("UQ_Providers_License")`, `OwnsMany(Specializations)` → `ToTable("ProviderSpecializations")` with composite key `(ProviderId, Value)`, `HasMany(Slots).WithOne().HasForeignKey(s => s.ProviderId)`  
  **AvailabilitySlotConfiguration**: `ToTable("AvailabilitySlots")`, `HasKey(Id)`, `Property(Id).HasDefaultValueSql("NEWSEQUENTIALID()")`, `Property(Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(SlotStatus.Available)`, `Property(RowVersion).IsRowVersion().IsConcurrencyToken()`, `HasCheckConstraint("CK_Slots_Duration", "[DurationMinutes] = 30")`, `HasIndex(ProviderId, StartTimeUtc).IsUnique().HasFilter("[Status] != 'Blocked'").HasDatabaseName("UX_Slots_Provider_Start")`  
  _Prereq: T042, T043_

- [ ] T055 [INFRA] [US2] Implement `ProviderDbContext` in `src/Services/ProviderService/ProviderService.Infrastructure/Persistence/ProviderDbContext.cs`: `DbSet<Provider>`, `DbSet<AvailabilitySlot>`, `DbSet<OutboxMessage>`, register interceptors  
  _Prereq: T006, T054_

- [ ] T056 [INFRA] [US2] Implement `AuditInterceptor` + `OutboxPublishingInterceptor` for ProviderService — copy exact code from **Reference R3** and **Reference R4** above into `src/Services/ProviderService/ProviderService.Infrastructure/Persistence/Interceptors/AuditInterceptor.cs` and `OutboxPublishingInterceptor.cs`; also create `ICurrentUserService` in `ProviderService.Application/Interfaces/` and `CurrentUserService` in `ProviderService.Infrastructure/Services/` (same code as R3); register both interceptors in `ProviderDbContext`  
  _Prereq: T055_  
  _✅ Verify: `dotnet build src/Services/ProviderService/ProviderService.Infrastructure/` succeeds_

- [ ] T057 [P] [INFRA] [US2] Implement `ProviderRepository : IProviderRepository` + `SlotRepository : ISlotRepository` in `src/Services/ProviderService/ProviderService.Infrastructure/Persistence/Repositories/`; `SlotRepository.GetAvailableSlotsByProviderAndDateAsync()` returns ordered slots  
  _Prereq: T046, T055_

- [ ] T058 [P] [INFRA] [US2] Implement `RedisCacheService : ICacheService` in `src/Services/ProviderService/ProviderService.Infrastructure/Caching/RedisCacheService.cs` using `IDistributedCache` (system.text.json serialization); no direct `StackExchange.Redis` calls in Application layer  
  _Prereq: T046_

- [ ] T059 [INFRA] [US2] Run `dotnet ef migrations add InitialCreate` for ProviderService; verify migration files in `src/Services/ProviderService/ProviderService.Infrastructure/Persistence/Migrations/`; add `MigrateAsync()` to startup  
  _Prereq: T055, T056_

### ProviderService — API [US2]

- [ ] T060 [API] [US2] Implement `ProvidersEndpoints` + `SlotsEndpoints` (Minimal API) in `src/Services/ProviderService/ProviderService.API/Endpoints/`:
  `POST /api/providers` → `.RequireAuthorization("ProviderWrite")` → send `CreateProviderCommand` → return `201 Created`  
  `GET /api/providers/{id}` → `.RequireAuthorization()` → send `GetProviderByIdQuery` → return `200 OK`  
  `GET /api/providers/search?specialization=X&from=Y&to=Z` → `.RequireAuthorization()` → send `SearchProvidersBySpecializationQuery` → return `200 OK`  
  `POST /api/providers/{id}/availability` → `.RequireAuthorization("ProviderWrite")` → send `DefineAvailabilityCommand` → return `201 Created`  
  `DELETE /api/providers/{id}/availability/{windowId}` → `.RequireAuthorization("ProviderWrite")` → send `CancelAvailabilityWindowCommand` → return `204 No Content`  
  `GET /api/providers/{id}/slots?date=YYYY-MM-DD` → `.RequireAuthorization()` → send `GetProviderSlotsQuery` → return `200 OK`  
  _Prereq: T047, T048, T049, T050, T051, T052, T053_

- [ ] T061 [P] [API] [US2] Implement gRPC `ProviderGrpcService` in `src/Services/ProviderService/ProviderService.API/Grpc/ProviderGrpcService.cs`: implement `GetSlotById`, `LockSlot` (uses `AvailabilitySlot.MarkBooked()` with optimistic concurrency — `DbUpdateConcurrencyException` → `LockSlotResponse { success: false }`), `ReleaseSlot`  
  _Prereq: T008, T043, T057_

- [ ] T062 [P] [API] [US2] Add `/health/live` + `/health/ready` to ProviderService; `/health/ready` checks SQL Server + Redis connectivity  
  _Prereq: T011_

- [ ] T063 [API] [US2] Wire full DI in `src/Services/ProviderService/ProviderService.API/Program.cs`: MediatR, EF Core + interceptors, MassTransit (no consumers yet), Redis `IDistributedCache`, gRPC service, JWT auth, `MigrateAsync()`, Serilog  
  _Prereq: T055, T056, T057, T058, T059, T060, T061, T062_

### ProviderService — Tests [US2]

- [ ] T064 [TEST] [US2] Write ProviderService unit tests in `tests/ProviderService.UnitTests/`: `CreateProviderCommandHandler` success; `DefineAvailabilityCommandHandler` generates correct slot count + overlap raises `DomainException`; `CancelAvailabilityWindowCommandHandler`; all query handlers (cache hit / miss); `AvailabilitySlot.MarkBooked()` guard; ≥80% Domain + Application coverage  
  _Prereq: T047, T048, T049, T050, T051, T052, T053_

- [ ] T065 [P] [TEST] [US2] Write ProviderService integration tests in `tests/ProviderService.IntegrationTests/`: create provider → retrieve; define availability → verify slot count; audit fields populated; gRPC `LockSlot` succeeds + concurrent lock returns conflict; API endpoint contract validation  
  _Prereq: T063, T064_

---

## Week 3 — Scheduling Engine: AppointmentService (US3 + US4)

**Milestone Goal**: AppointmentService — booking, double-booking prevention (optimistic concurrency), cancellation, rescheduling, Outbox publishing verified.

**DoD Gate**: Concurrent double-booking integration test passes. Outbox atomicity verified. All command handlers covered with ≥80% coverage.

### AppointmentService — Domain [US3, US4]

- [ ] T066 [DOMAIN] [US3] Implement `Appointment` aggregate root in `src/Services/AppointmentService/AppointmentService.Domain/Entities/Appointment.cs`: `Book()` static factory; `Confirm()`, `Cancel(reason)`, `Reschedule()`, `MarkCompleted()`, `MarkNoShow()` state-machine methods; `AppointmentStatus` enum (`Pending`, `Confirmed`, `Cancelled`, `Completed`, `NoShow`); `SagaCorrelationId`; each transition raises a corresponding domain event  
  _Prereq: T006, T011_

- [ ] T067 [P] [DOMAIN] [US3] Implement `BookingIdempotencyKey` entity in `src/Services/AppointmentService/AppointmentService.Domain/Entities/BookingIdempotencyKey.cs`  
  _Prereq: T011_

- [ ] T068 [P] [DOMAIN] [US3] Implement value objects in `src/Services/AppointmentService/AppointmentService.Domain/ValueObjects/`: `AppointmentId`, `SagaCorrelationId`  
  _Prereq: T011_

- [ ] T069 [P] [DOMAIN] [US3] Implement domain events in `src/Services/AppointmentService/AppointmentService.Domain/Events/`: `AppointmentBookedDomainEvent`, `AppointmentConfirmedDomainEvent`, `AppointmentCancelledDomainEvent`, `AppointmentRescheduledDomainEvent` — all implementing `IDomainEvent`  
  _Prereq: T006, T066_

### AppointmentService — Application [US3, US4]

- [ ] T070 [APP] [US3] Define interfaces in `src/Services/AppointmentService/AppointmentService.Application/Interfaces/`: `IAppointmentRepository` (including `FindByIdempotencyKeyAsync`), `IProviderSlotGrpcClient` (`LockSlotAsync`, `ReleaseSlotAsync`, `GetSlotByIdAsync`), `IPatientGrpcClient` (`GetPatientByIdAsync`)  
  _Prereq: T066, T067_

- [ ] T071 [APP] [US3] Implement `BookAppointmentCommand` record, `BookAppointmentCommandValidator` (non-empty `PatientId`, `SlotId`, `IdempotencyKey`), and `BookAppointmentCommandHandler` in `src/Services/AppointmentService/AppointmentService.Application/Commands/BookAppointment/`; handler: (1) idempotency check, (2) `IPatientGrpcClient.GetPatientById` verify exists, (3) `IProviderSlotGrpcClient.LockSlot` → `ConflictException` on failure, (4) `Appointment.Book()`, (5) persist + outbox via interceptor in single `SaveChanges`; returns `BookAppointmentResult` with reference number  
  _Prereq: T009, T066, T067, T068, T070_

- [ ] T072 [APP] [US4] Implement `CancelAppointmentCommand` + validator (≥2-hour-before-scheduled-time guard) + handler in `src/Services/AppointmentService/AppointmentService.Application/Commands/CancelAppointment/`; handler loads aggregate, calls `Cancel(reason)`, emits cancellation event via outbox  
  _Prereq: T071_

- [ ] T073 [P] [APP] [US4] Implement `RescheduleAppointmentCommand` + validator + handler in `src/Services/AppointmentService/AppointmentService.Application/Commands/RescheduleAppointment/`; handler: release old slot via gRPC, lock new slot, update appointment in single transaction; raises `AppointmentRescheduledDomainEvent`  
  _Prereq: T071_

- [ ] T074 [P] [APP] [US4] Implement `MarkAppointmentCompletedCommand` + handler in `src/Services/AppointmentService/AppointmentService.Application/Commands/MarkCompleted/`  
  _Prereq: T066_

- [ ] T075 [P] [APP] [US4] Implement `MarkAppointmentNoShowCommand` + handler in `src/Services/AppointmentService/AppointmentService.Application/Commands/MarkNoShow/`  
  _Prereq: T066_

- [ ] T076 [P] [APP] [US3] Implement `GetAppointmentByIdQuery` + handler in `src/Services/AppointmentService/AppointmentService.Application/Queries/GetAppointmentById/`  
  _Prereq: T070_

- [ ] T077 [P] [APP] [US4] Implement `GetPatientAppointmentsQuery` + handler in `src/Services/AppointmentService/AppointmentService.Application/Queries/GetPatientAppointments/`  
  _Prereq: T070_

### AppointmentService — Infrastructure [US3]

- [ ] T078 [INFRA] [US3] Implement `AppointmentConfiguration` + `BookingIdempotencyKeyConfiguration` in `src/Services/AppointmentService/AppointmentService.Infrastructure/Persistence/Configurations/`:
  **AppointmentConfiguration**: `ToTable("Appointments")`, `HasKey(Id)`, `Property(Id).HasDefaultValueSql("NEWSEQUENTIALID()")`, `Property(PatientId).IsRequired()`, `Property(ProviderId).IsRequired()`, `Property(SlotId).IsRequired()`, `Property(ScheduledStartUtc).IsRequired()`, `Property(ScheduledEndUtc).IsRequired()`, `Property(Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(AppointmentStatus.Pending)`, `Property(CancellationReason).HasMaxLength(500)`, `Property(SagaCorrelationId).IsRequired()`, audit columns, `HasIndex(PatientId).HasDatabaseName("IX_Appointments_PatientId")`, `HasIndex(ProviderId, Status).HasDatabaseName("IX_Appointments_ProviderId_Status")`, `HasIndex(SlotId).HasDatabaseName("IX_Appointments_SlotId")`  
  **BookingIdempotencyKeyConfiguration**: `ToTable("BookingIdempotencyKeys")`, `HasKey(Id)`, `Property(AppointmentId).IsRequired()`, `Property(CreatedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()")`  
  _Prereq: T066, T067_

- [ ] T079 [INFRA] [US3] Implement `AppointmentDbContext` in `src/Services/AppointmentService/AppointmentService.Infrastructure/Persistence/AppointmentDbContext.cs`: `DbSet<Appointment>`, `DbSet<BookingIdempotencyKey>`, `DbSet<OutboxMessage>`, register `AuditInterceptor` + `OutboxPublishingInterceptor`  
  _Prereq: T006, T078_

- [ ] T080 [P] [INFRA] [US3] Implement `AuditInterceptor` + `OutboxPublishingInterceptor` for AppointmentService — copy exact code from **Reference R3** and **Reference R4** above into `src/Services/AppointmentService/AppointmentService.Infrastructure/Persistence/Interceptors/AuditInterceptor.cs` and `OutboxPublishingInterceptor.cs`; also create `ICurrentUserService` in `AppointmentService.Application/Interfaces/` and `CurrentUserService` in `AppointmentService.Infrastructure/Services/`  
  _Prereq: T079_  
  _✅ Verify: `dotnet build src/Services/AppointmentService/AppointmentService.Infrastructure/` succeeds_

- [ ] T081 [P] [INFRA] [US3] Implement `AppointmentRepository : IAppointmentRepository` in `src/Services/AppointmentService/AppointmentService.Infrastructure/Persistence/Repositories/AppointmentRepository.cs`  
  _Prereq: T070, T079_

- [ ] T082 [INFRA] [US3] Implement `ProviderSlotGrpcClient : IProviderSlotGrpcClient` in `src/Services/AppointmentService/AppointmentService.Infrastructure/Clients/ProviderSlotGrpcClient.cs`: inject `ProviderGrpc.ProviderGrpcClient` (generated from proto); implement `LockSlotAsync` → calls `client.LockSlotAsync(new LockSlotRequest { SlotId = ..., AppointmentId = ... })` → returns `response.Success`; implement `ReleaseSlotAsync` → calls `client.ReleaseSlotAsync(...)`. Register in DI: `builder.Services.AddGrpcClient<ProviderGrpc.ProviderGrpcClient>(o => o.Address = new Uri(config["ProviderService:GrpcUrl"]!)).AddHealthBookingResiliencePipeline("provider-grpc")`  
  _Prereq: T008, T010, T070_  
  _✅ Verify: `dotnet build src/Services/AppointmentService/AppointmentService.Infrastructure/` succeeds_

- [ ] T083 [P] [INFRA] [US3] Implement `PatientGrpcClient : IPatientGrpcClient` in `src/Services/AppointmentService/AppointmentService.Infrastructure/Clients/PatientGrpcClient.cs`; apply `AddHealthBookingResiliencePipeline("patient-grpc")`  
  _Prereq: T008, T010, T070_

- [ ] T084 [INFRA] [US3] Implement `OutboxProcessor` — copy exact code from **Reference R5** above into `src/Services/AppointmentService/AppointmentService.Infrastructure/Messaging/OutboxProcessor.cs`; use `AppointmentDbContext` as the DbContext type  
  _Prereq: T079, T081_  
  _✅ Verify: `dotnet build src/Services/AppointmentService/AppointmentService.Infrastructure/` succeeds_

- [ ] T085 [INFRA] [US3] Run `dotnet ef migrations add InitialCreate` for AppointmentService; commit migration files; add `MigrateAsync()` to startup  
  _Prereq: T079, T080_

### AppointmentService — API [US3, US4]

- [ ] T086 [API] [US3] Implement `AppointmentsEndpoints` (Minimal API) in `src/Services/AppointmentService/AppointmentService.API/Endpoints/AppointmentsEndpoints.cs`:
  `POST /api/appointments` → `.RequireAuthorization("AppointmentWrite")` → read `Idempotency-Key` from `HttpContext.Request.Headers` → send `BookAppointmentCommand` via MediatR → return `201 Created`  
  `GET /api/appointments/{id}` → `.RequireAuthorization()` → send `GetAppointmentByIdQuery` → return `200 OK`  
  `GET /api/appointments/me` → `.RequireAuthorization()` → extract `userId` from JWT claims → send `GetPatientAppointmentsQuery` → return `200 OK`  
  `DELETE /api/appointments/{id}` → `.RequireAuthorization()` → send `CancelAppointmentCommand` → return `204 No Content`  
  `PUT /api/appointments/{id}/reschedule` → `.RequireAuthorization()` → send `RescheduleAppointmentCommand` → return `200 OK`  
  _Prereq: T071, T072, T073, T076, T077_

- [ ] T087 [P] [API] [US3] Add `/health/live` + `/health/ready` to AppointmentService; `/health/ready` checks SQL Server + RabbitMQ + ProviderService gRPC reachability  
  _Prereq: T011_

- [ ] T088 [API] [US3] Wire full DI in `src/Services/AppointmentService/AppointmentService.API/Program.cs`: MediatR, EF Core + interceptors, MassTransit (RabbitMQ; no consumers in Week 3), gRPC clients, JWT auth, `OutboxProcessor` hosted service, `MigrateAsync()`, Serilog  
  _Prereq: T079, T080, T081, T082, T083, T084, T085, T086, T087_

### AppointmentService — Documentation

- [ ] T089 [OPS] Write booking saga sequence diagram and compensation paths document in `docs/booking-saga.md`: normal flow, compensation path (slot locked but DB commit fails → auto-release after 30 s TTL), idempotency re-entry path  
  _Prereq: T071, T084_

### AppointmentService — Tests [US3, US4]

- [ ] T090 [TEST] [US3] Write AppointmentService unit tests in `tests/AppointmentService.UnitTests/`: `BookAppointmentCommandHandler` success, idempotent re-entry returns existing result, patient-not-found throws `NotFoundException`, slot conflict throws `ConflictException`; `CancelAppointmentCommandHandler` success + within-2-hours guard; `RescheduleAppointmentCommandHandler`; `MarkCompleted` + `MarkNoShow`; state machine transitions; ≥80% Domain + Application coverage  
  _Prereq: T071, T072, T073, T074, T075_

- [ ] T091 [TEST] [US3] Write concurrent double-booking integration test in `tests/AppointmentService.IntegrationTests/ConcurrentBookingTests.cs`: spin up two concurrent `HttpClient` requests for the same slot via TestContainers; assert exactly one `201 Created` and one `409 Conflict`; assert no ghost booking records  
  _Prereq: T088, T090_

- [ ] T092 [P] [TEST] [US3] Write Outbox atomicity integration test in `tests/AppointmentService.IntegrationTests/OutboxTests.cs`: `BookAppointmentCommand` → verify `Appointment` row and `OutboxMessage` row written in same DB transaction; simulate outbox processor publishing → verify message appears in RabbitMQ test exchange (TestContainers RabbitMQ)  
  _Prereq: T088, T090_

---

## Week 4 — Integration: RabbitMQ Event Wiring & NotificationService (US5)

**Milestone Goal**: All four services connected via RabbitMQ. NotificationService consuming events. Provider slot status updated asynchronously. Idempotency enforced.

**DoD Gate**: Duplicate delivery test passes. Slot status consistency test passes. All notifications delivered within 60 s in test container environment.

### ProviderService — Message Consumers

- [ ] T093 [INFRA] [US3] Implement `AppointmentBookedConsumer : IConsumer<V1_AppointmentBookedEvent>` in `src/Services/ProviderService/ProviderService.Infrastructure/Messaging/Consumers/AppointmentBookedConsumer.cs`: (1) idempotency check via `ProcessedEvents`-style guard (check if `SlotId` is already `Booked`), (2) `SlotRepository.MarkBookedAsync()`, (3) invalidate Redis cache key `provider:slots:{ProviderId}:{date}` + `provider:search:*` via `ICacheService`  
  _Prereq: T057, T058, T043_

- [ ] T094 [P] [INFRA] [US4] Implement `SlotReleasedConsumer : IConsumer<V1_SlotReleasedEvent>` in `src/Services/ProviderService/ProviderService.Infrastructure/Messaging/Consumers/SlotReleasedConsumer.cs`: `SlotRepository.ReleaseSlotAsync()` + invalidate cache  
  _Prereq: T057, T058_

- [ ] T095 [INFRA] Register `AppointmentBookedConsumer` + `SlotReleasedConsumer` in ProviderService MassTransit configuration in `src/Services/ProviderService/ProviderService.Infrastructure/DependencyInjection.cs`; configure exponential retry (3× → DLQ); configure dead-letter queue for all queues  
  _Prereq: T093, T094_

### NotificationService — Domain [US5]

- [ ] T096 [P] [DOMAIN] [US5] Implement `NotificationRecord` entity in `src/Services/NotificationService/NotificationService.Domain/Entities/NotificationRecord.cs`: properties: `Id (Guid)`, `AppointmentId`, `CorrelationId`, `RecipientPatientId`, `Channel (NotificationChannel enum: Email, Sms)`, `TemplateType (string)`, `Status (NotificationStatus enum: Pending, Delivered, Failed)`, `RetryCount (int)`, `LastAttemptedAt (DateTimeOffset?)`, `DeliveredAt (DateTimeOffset?)`, `FailureReason (string?)`, `CreatedAt`; private constructor; `Create()` static factory; `MarkDelivered()` sets `Status=Delivered`, `DeliveredAt=UtcNow`; `RecordFailure(reason)` increments `RetryCount`, sets `LastAttemptedAt=UtcNow`, `FailureReason=reason`, if `RetryCount >= 3` sets `Status=Failed`  
  _Prereq: T006, T011_

- [ ] T097 [P] [DOMAIN] [US5] Implement `ProcessedEvent` entity for idempotency in `src/Services/NotificationService/NotificationService.Domain/Entities/ProcessedEvent.cs` (stores MassTransit `MessageId`)  
  _Prereq: T011_

### NotificationService — Application [US5]

- [ ] T098 [APP] [US5] Define `INotificationRepository`, `IEmailDeliveryService` interfaces in `src/Services/NotificationService/NotificationService.Application/Interfaces/`  
  _Prereq: T096, T097_

- [ ] T099 [APP] [US5] Implement `ProcessAppointmentBookedNotificationCommand` + handler in `src/Services/NotificationService/NotificationService.Application/Commands/ProcessBooked/`; creates `NotificationRecord` and calls `IEmailDeliveryService.SendBookingConfirmationAsync()`  
  _Prereq: T009, T096, T098_

- [ ] T100 [P] [APP] [US5] Implement `ProcessAppointmentCancelledNotificationCommand` + handler in `src/Services/NotificationService/NotificationService.Application/Commands/ProcessCancelled/`  
  _Prereq: T098, T099_

- [ ] T101 [P] [APP] [US5] Implement `ProcessAppointmentRescheduledNotificationCommand` + handler in `src/Services/NotificationService/NotificationService.Application/Commands/ProcessRescheduled/`  
  _Prereq: T098, T099_

- [ ] T102 [P] [APP] [US5] Implement `SendReminderNotificationCommand` + handler in `src/Services/NotificationService/NotificationService.Application/Commands/SendReminder/`  
  _Prereq: T098_

### NotificationService — Infrastructure [US5]

- [ ] T103 [INFRA] [US5] Implement `NotificationRecordConfiguration` + `ProcessedEventConfiguration` in `src/Services/NotificationService/NotificationService.Infrastructure/Persistence/Configurations/`:
  **NotificationRecordConfiguration**: `ToTable("NotificationRecords")`, `HasKey(Id)`, `Property(Id).HasDefaultValueSql("NEWSEQUENTIALID()")`, `Property(AppointmentId).IsRequired()`, `Property(CorrelationId).IsRequired()`, `Property(RecipientPatientId).IsRequired()`, `Property(Channel).HasConversion<string>().HasMaxLength(20).HasDefaultValue(NotificationChannel.Email)`, `Property(TemplateType).HasMaxLength(100).IsRequired()`, `Property(Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(NotificationStatus.Pending)`, `Property(RetryCount).HasDefaultValue(0)`, `Property(FailureReason).HasMaxLength(500)`, `HasIndex(CorrelationId).HasDatabaseName("IX_Notifications_CorrelationId")`, `HasIndex(Status, LastAttemptedAt).HasDatabaseName("IX_Notifications_Status_LastAttemptedAt")`  
  **ProcessedEventConfiguration**: `ToTable("ProcessedEvents")`, `HasKey(MessageId)`, `Property(RecordId).IsRequired()`, `Property(ProcessedAt).HasDefaultValueSql("SYSDATETIMEOFFSET()")`  
  _Prereq: T096, T097_

- [ ] T104 [INFRA] [US5] Implement `NotificationDbContext` + `NotificationRepository : INotificationRepository` in `src/Services/NotificationService/NotificationService.Infrastructure/Persistence/`  
  _Prereq: T098, T103_

- [ ] T105 [P] [INFRA] [US5] Implement `StubEmailDeliveryService : IEmailDeliveryService` in `src/Services/NotificationService/NotificationService.Infrastructure/Email/StubEmailDeliveryService.cs`; logs delivery attempt with correlation ID; does NOT actually send external email  
  _Prereq: T098_

- [ ] T106 [INFRA] [US5] Implement `AppointmentBookedConsumer : IConsumer<V1_AppointmentBookedEvent>` in `src/Services/NotificationService/NotificationService.Infrastructure/Messaging/Consumers/AppointmentBookedConsumer.cs`: (1) idempotency check via `ProcessedEvents` table using `context.MessageId`, (2) `ProcessAppointmentBookedNotificationCommand` via MediatR, (3) write `ProcessedEvent` row; duplicate delivery must NOT call `IEmailDeliveryService` twice  
  _Prereq: T099, T104, T105_

- [ ] T107 [P] [INFRA] [US5] Implement `AppointmentCancelledConsumer` in `src/Services/NotificationService/NotificationService.Infrastructure/Messaging/Consumers/AppointmentCancelledConsumer.cs` (same idempotency pattern)  
  _Prereq: T100, T104_

- [ ] T108 [P] [INFRA] [US5] Implement `AppointmentRescheduledConsumer` in `src/Services/NotificationService/NotificationService.Infrastructure/Messaging/Consumers/AppointmentRescheduledConsumer.cs`  
  _Prereq: T101, T104_

- [ ] T109 [INFRA] [US5] Implement 24-hour reminder `ReminderScheduler` as `BackgroundService` in `src/Services/NotificationService/NotificationService.Infrastructure/Scheduling/ReminderScheduler.cs`; polls for appointments with `ScheduledStartUtc` within 23–25-hour window; dispatches `SendReminderNotificationCommand`  
  _Prereq: T102, T104_

- [ ] T110 [INFRA] [US5] Configure dead-letter queues in MassTransit for ALL service consumers (PatientService, ProviderService, AppointmentService, NotificationService): `UseMessageRetry(3, exponential)` → `Dead-Letter` queue — update each service's MassTransit DI wiring  
  _Prereq: T093, T094, T095, T106, T107, T108_

- [ ] T111 [INFRA] [US5] Run `dotnet ef migrations add InitialCreate` for NotificationService; commit migration files  
  _Prereq: T104_

### NotificationService — API [US5]

- [ ] T112 [API] [US5] Configure `NotificationService.API/Program.cs`: MassTransit consumers registered, `/health/live` + `/health/ready` (SQL Server + RabbitMQ health checks), `MigrateAsync()` on startup, Serilog; no external REST endpoints  
  _Prereq: T104, T106, T107, T108, T109, T110, T111_

### OutboxProcessor — ProviderService & NotificationService

- [ ] T113 [INFRA] Implement `OutboxProcessor` for ProviderService — copy exact code from **Reference R5** above into `src/Services/ProviderService/ProviderService.Infrastructure/Messaging/OutboxProcessor.cs`; **change** `AppointmentDbContext` → `ProviderDbContext` in the `GetRequiredService<>()` call; register as `builder.Services.AddHostedService<OutboxProcessor>()` in `ProviderService.API/Program.cs`  
  _Prereq: T055_  
  _✅ Verify: `dotnet build src/Services/ProviderService/ProviderService.Infrastructure/` succeeds_

### Documentation

- [ ] T114 [OPS] Write message contract versioning documentation in `docs/message-versioning.md`: naming convention (`V1_*`), backward-compatibility rules, consumer-upgrade procedure, DLQ monitoring guidance  
  _Prereq: T110_

### Tests — Week 4

- [ ] T115 [TEST] [US5] Write NotificationService unit tests in `tests/NotificationService.UnitTests/`: `ProcessBookedNotificationHandler` creates record + calls delivery; `ProcessCancelledNotificationHandler`; `ProcessRescheduledNotificationHandler`; `NotificationRecord.RecordFailure()` marks Failed at retry 3; ≥80% coverage  
  _Prereq: T099, T100, T101, T102_

- [ ] T116 [TEST] [US5] Write NotificationService integration test in `tests/NotificationService.IntegrationTests/IdempotencyTests.cs`: publish same `V1_AppointmentBookedEvent` twice to TestContainers RabbitMQ → assert `StubEmailDeliveryService` called exactly once; assert single `ProcessedEvent` row; assert single `NotificationRecord` row  
  _Prereq: T112_

- [ ] T117 [P] [TEST] [US3] Write ProviderService integration test in `tests/ProviderService.IntegrationTests/EventConsumerTests.cs`: publish `V1_AppointmentBookedEvent` → assert slot `Status` changes to `Booked` within 5 s; assert Redis cache key removed; publish `V1_SlotReleasedEvent` → assert slot returns to `Available`  
  _Prereq: T095, T063_

---

## Week 5 — Resilience & Caching: Circuit Breakers, YARP, Redis (US3, US6)

**Milestone Goal**: All outbound clients wrapped in Polly pipelines. YARP fully configured. Redis caching validated. Circuit breaker behavior tested.

**DoD Gate**: Circuit breaker integration test passes. Cache invalidation test passes. All health probes green in `docker compose up`.

### Polly Resilience Pipelines

- [ ] T118 [INFRA] Verify and finalize `AddHealthBookingResiliencePipeline()` from T010 is applied to every outbound HTTP/gRPC client across all services: `IIdentityProvisioningService` (PatientService), `ProviderGrpcClient` + `PatientGrpcClient` (AppointmentService); add any missing `AddHealthBookingResiliencePipeline()` calls in DI wiring files  
  _Prereq: T010, T035, T082, T083_

- [ ] T119 [P] [INFRA] Add explicit circuit breaker state-change logging: `OnOpened` emits `Log.Warning("Circuit breaker OPENED for {ClientName}; CorrelationId: {CorrelationId}")`, `OnClosed` emits `Log.Information(...)` — verify in all resilience pipeline registrations in T010/T118  
  _Prereq: T118_

- [ ] T120 [P] [INFRA] Audit all Polly retry strategies across services; verify exponential back-off with `Random.Shared.Next(0, 500)` ms jitter applied consistently; fix any missing jitter configurations  
  _Prereq: T118_

### YARP API Gateway — Finalization

- [ ] T121 [API] Finalize all YARP route definitions in `src/ApiGateway/HealthBooking.ApiGateway/appsettings.json`: confirm `patient-register-route` (anonymous), all other routes with `JwtBearer` policy; ensure `NotificationService` internal-only (no public route); add `X-Forwarded-For` transform  
  _Prereq: T016_

- [ ] T122 [P] [API] Verify YARP active health checks for all clusters poll `/health/ready` every 10 s; add passive health check settings (mark destinations unhealthy on 5xx), update `appsettings.json`  
  _Prereq: T121_

- [ ] T123 [P] [API] Confirm `CorrelationIdMiddleware` injects `X-Correlation-Id` into all forwarded requests; verify Serilog `LogContext.PushProperty("CorrelationId", ...)` is active in `HealthBooking.ApiGateway/Middleware/CorrelationIdMiddleware.cs`  
  _Prereq: T015_

### Health Checks — All Services

- [ ] T124 [API] Implement comprehensive `/health/ready` health checks in all services using `Microsoft.Extensions.Diagnostics.HealthChecks` with `AddSqlServer`, `AddRabbitMQ` (via `AddHealthChecks().AddRabbitMQ(...)`), `AddRedis` (ProviderService only); update all `Endpoints/HealthCheckEndpoints.cs` files  
  _Prereq: T038, T062, T087, T112_

- [ ] T125 [P] [OPS] Verify all Docker Compose `depends_on: condition: service_healthy` chains are correct and complete in `docker-compose.yml` and `docker-compose.override.yml`; validate that `api-gateway` only starts after all service health checks pass  
  _Prereq: T017, T018, T124_

### Redis End-to-End Validation

- [ ] T126 [APP] [US2] Validate `ICacheService` end-to-end in ProviderService: write provider slot → confirm cache populated with 60 s TTL → `AppointmentBookedConsumer` fires → confirm cache key removed via `RemoveAsync`; verify no stale data served beyond TTL  
  _Prereq: T058, T093_

### Tests — Week 5

- [ ] T127 [TEST] [US3] Write AppointmentService circuit breaker integration test in `tests/AppointmentService.IntegrationTests/ResilienceTests.cs`: configure TestContainers to make ProviderService gRPC endpoint unreachable; send `BookAppointmentCommand`; assert `503 Service Unavailable` returned; assert circuit breaker `OnOpened` log entry present with correlation ID  
  _Prereq: T088, T118, T119_

- [ ] T128 [P] [TEST] [US2] Write ProviderService cache integration test in `tests/ProviderService.IntegrationTests/CacheTests.cs`: define availability → query slots (cache miss → populated) → query again (cache hit) → publish `V1_AppointmentBookedEvent` → query again (cache miss: key invalidated); assert TTL-based expiry within 60 s  
  _Prereq: T063, T093, T126_

---

## Week 6 — Observability, Final Suite & Sprint Closure (US6)

**Milestone Goal**: Full distributed tracing. End-to-end integration test green. PII masking confirmed by test. Risk register reviewed. README complete.

**DoD Gate**: All six weekly DoD checklists fully satisfied. CI green on `main`. Distributed trace end-to-end visible in Jaeger. Zero PII in logs confirmed by test.

### OpenTelemetry Instrumentation [US6]

- [ ] T129 [OPS] Implement `AddHealthBookingObservability(string serviceName)` extension method in `src/SharedKernel/HealthBooking.SharedKernel/Extensions/ObservabilityExtensions.cs`. Required NuGet packages: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.GrpcNetClient`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`. Implementation:
  ```csharp
  public static IServiceCollection AddHealthBookingObservability(this IServiceCollection services, string serviceName)
  {
      services.AddOpenTelemetry()
          .WithTracing(tracing => tracing
              .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
              .AddAspNetCoreInstrumentation(o => o.RecordException = true)
              .AddGrpcClientInstrumentation()
              .AddEntityFrameworkCoreInstrumentation(o => { o.SetDbStatementForText = false; o.SetDbStatementForStoredProcedure = false; })
              .AddSource("MassTransit")
              .AddOtlpExporter(o => o.Endpoint = new Uri("http://jaeger:4317")));
      return services;
  }
  ```  
  _Prereq: T006_

- [ ] T130 [P] [OPS] Apply `AddHealthBookingObservability()` in all five service `Program.cs` files: `PatientService.API`, `ProviderService.API`, `AppointmentService.API`, `NotificationService.API`, `HealthBooking.ApiGateway`; pass correct `serviceName` to each  
  _Prereq: T129, T039, T063, T088, T112, T015_

- [ ] T131 [P] [OPS] Implement Serilog PII destructuring policies in each service's `Program.cs` (or in `AddHealthBookingObservability`): `PatientResponse` → mask `FullName = "***"`, `ContactEmail = "***"`; `Patient` entity → log only `Id`; verify policy applied before any logger usage  
  _Prereq: T129_

- [ ] T132 [P] [INFRA] Implement `CorrelationIdPublishFilter<T>` + `CorrelationIdConsumeFilter<T>` (MassTransit filters) in `src/SharedKernel/HealthBooking.SharedKernel/Messaging/Filters/`; embed `CorrelationId` in every message envelope; register filters in all service MassTransit configurations  
  _Prereq: T006_

- [ ] T133 [P] [OPS] Verify Jaeger container is reachable within Docker Compose network and spans flow from all services → Jaeger UI (`http://localhost:16686`); confirm `patient-service`, `provider-service`, `appointment-service`, `notification-service`, `api-gateway` appear as distinct services in Jaeger  
  _Prereq: T017, T130_

### Tests — Week 6 [US6]

- [ ] T134 [TEST] [US6] Write correlation ID propagation integration test in `tests/AppointmentService.IntegrationTests/ObservabilityTests.cs`: submit booking request with known `X-Correlation-Id`; assert correlation ID appears in all Serilog log entries (captured via test sink) from at least AppointmentService and ProviderService  
  _Prereq: T088, T130, T132_

- [ ] T135 [P] [TEST] [US6] Write PII masking integration tests in each service's `IntegrationTests` project: capture log output via in-memory Serilog sink; submit registration / booking requests; assert no email addresses or patient full names appear as plain text in any log entry or OTel span attribute  
  _Prereq: T041, T065, T092, T116, T131_

- [ ] T136 [TEST] [US1,US2,US3,US5] Write full booking-flow end-to-end integration test in `tests/AppointmentService.IntegrationTests/E2EBookingFlowTests.cs` using all TestContainers: (1) register patient via PatientService, (2) create provider + availability via ProviderService, (3) book appointment via AppointmentService, (4) verify `V1_AppointmentBookedEvent` published to RabbitMQ, (5) verify NotificationService writes `NotificationRecord` with status `Delivered`, (6) verify ProviderService slot status eventually `Booked`  
  _Prereq: T041, T065, T112, T117, T130_

- [ ] T137 [P] [TEST] Confirm 80% line coverage CI gate is enforced across all four service Domain+Application projects; update CI workflow to use `--collect:"XPlat Code Coverage"` + `reportgenerator` with minimum threshold; fail build if any service falls below 80%  
  _Prereq: T040, T041, T064, T065, T090, T091, T092, T115, T116_

### Sprint Closure — Documentation & Review

- [ ] T138 [OPS] Review Risk Register; document mitigation status or formal acceptance with written rationale for all Medium/High-impact risks from constitution table in `docs/risk-register.md`: distributed transaction inconsistency, message schema drift, TestContainers startup latency, auth token misconfiguration  
  _Prereq: T089, T114_

- [ ] T139 [P] [OPS] Write `README.md` at repository root: local setup steps (`docker compose up`), port map (5000: Gateway, 5001: Patient, 5002: Provider, 5003: Appointment, 5004: Notification, 5005: IdentityServer, 16686: Jaeger), service architecture diagram, test execution instructions (`dotnet test`), constitution link  
  _Prereq: T017, T018_

- [ ] T140 [OPS] Perform `docker compose up` cold-start validation: confirm all containers reach `healthy` state within 3 minutes on a clean environment; measure and document startup sequence; fix any reliability issues in `docker-compose.yml` health check `retries`/`interval` settings  
  _Prereq: T017, T018, T124, T125, T130_

- [ ] T141 [OPS] Conduct final milestone constitution compliance walk-through per constitution section VII: verify all DoD checklists for weeks 1–6 are ticked; verify no hardcoded secrets (`grep -r "Password="` passes), no direct EF Core cross-DB joins, no manual audit field assignment; log results in `docs/sprint-closure.md`  
  _Prereq: T137, T138, T139, T140_

---

## Summary

| Week | Tasks | Labels Breakdown | US Coverage |
|---|---|---|---|
| Week 1 | T001–T020 (20) | 12× INFRA, 3× DOMAIN, 2× APP, 2× API, 1× TEST, 2× OPS | Foundation |
| Week 2 | T021–T065 (45) | 14× INFRA, 8× DOMAIN, 16× APP, 6× API, 5× TEST | US1, US2 |
| Week 3 | T066–T092 (27) | 8× INFRA, 4× DOMAIN, 12× APP, 2× API, 3× TEST, 1× OPS | US3, US4 |
| Week 4 | T093–T117 (25) | 11× INFRA, 2× DOMAIN, 6× APP, 1× API, 3× TEST, 2× OPS | US5 |
| Week 5 | T118–T128 (11) | 4× INFRA, 0× DOMAIN, 1× APP, 3× API, 2× TEST, 2× OPS | US3, US6 |
| Week 6 | T129–T141 (13) | 1× INFRA, 0× DOMAIN, 0× APP, 0× API, 4× TEST, 6× OPS | US6 |
| **Total** | **141** | 50× INFRA, 17× DOMAIN, 37× APP, 14× API, 18× TEST, 15× OPS | All US |

### MVP Scope (Minimum Viable Training Demo)

Weeks 1–3 deliver a demonstrable vertical slice:
- Patient registers and gets a JWT
- Provider availability is defined and queryable
- Patient books a slot (idempotent, conflict-safe)
- Outbox publishes the event to RabbitMQ

Weeks 4–6 complete the cross-service integration, resilience guarantees, and observability requirements.

### Parallel Execution Opportunities Per Week

**Week 1**: T003, T004, T005 (repo structure) can run in parallel after T001–T002; T007, T008, T009, T010 can run in parallel after T006.  
**Week 2**: PatientService domain (T021–T023) and ProviderService domain (T042–T045) run fully in parallel after T011; same for application + infrastructure layers within each service.  
**Week 3**: AppointmentService domain tasks (T067–T069) run in parallel with each other; application tasks (T073–T077) run in parallel once T071 completes.  
**Week 4**: ProviderService consumers (T093–T094) and NotificationService domain (T096–T097) run in parallel; notification application commands (T099–T102) run in parallel once T098 is done.  
**Week 5**: Polly audit (T119–T120) and YARP finalization (T121–T123) run in parallel; testing (T127–T128) runs in parallel after T124.  
**Week 6**: All OTel application tasks (T130–T132) run in parallel once T129 is done; test tasks (T134–T137) run in parallel after T130.
