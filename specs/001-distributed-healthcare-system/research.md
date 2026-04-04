# Research: HealthBooking Gap Remediation Sprint

**Phase 0 Output** | **Date**: 2026-04-04 | **Plan**: plan.md  
**Scope**: All critical and important gaps identified in `docs/SPEC-COMPLIANCE-AUDIT.md`

---

## R1 — Reschedule as Atomic Domain Operation

**Decision**: Implement `RescheduleAppointmentCommand` as a dedicated Application command with two gRPC side-effects (release old slot → lock new slot) before calling `Appointment.Reschedule()`.

**Rationale**:
- `Appointment` entity currently stores only `SlotId`, `PatientId`, `PatientName`, `Status` — it has **no time fields**.  
- Time data lives in `AvailabilitySlot` (ProviderService), retrieved via gRPC.  
- The handler must fetch both old and new slot `SlotInfo` via `IProviderSlotGrpcClient.GetSlotByIdAsync()` to populate the domain event with `OldSlotId`, `OldStart`, `NewSlotId`, `NewStart`.  
- `appointment.Reschedule(newSlotId)` is the minimal domain mutation; time coordinates are included in the domain event via handler-resolved gRPC data.  
- This mirrors the existing `CancelAppointmentCommand` pattern which performs a side-effecting gRPC `ReleaseSlotAsync` call from within the handler.

**Alternatives considered**:
- *Add `ScheduledStartUtc`/`EndUtc` to `Appointment` entity*: Rejected — duplicates data owned by ProviderService; violates single-source-of-truth and Clean Architecture boundary rules.  
- *Generic `PUT /api/appointments/{id}`*: Rejected — no such endpoint exists; cannot perform gRPC side-effects; bypasses domain invariants; violates DDD encapsulation (constitution §I).
- *A new Saga for reschedule*: YAGNI — a single MediatR handler with sequential gRPC calls + SaveChanges is sufficient for the transactional requirement. Reserve sagas for distributed rollback across multiple persistence stores.

**Existing contracts available** (no new files needed):
- `V1_AppointmentRescheduledEvent` in `HealthBooking.Contracts/Appointments/V1/V1_AppointmentEvents.cs` ✅  
- `IProviderSlotGrpcClient.GetSlotByIdAsync()`, `ReleaseSlotAsync()`, `LockSlotAsync()` ✅

---

## R2 — Database-Level Double-Booking Prevention

**Decision**: Add `UNIQUE` index on `Appointments.SlotId` via EF Core fluent API + new migration.

**Rationale**:
- The saga `LockSlotActivity` + `RowVersion` on `AvailabilitySlot` handles most concurrent races at the ProviderService level.  
- However, if two `PersistAppointmentActivity` instances execute near-simultaneously for the same (unlucky timing) `SlotId`, both could `SaveChangesAsync()` before the other's transaction is visible — a final-mile race.  
- A `UNIQUE` constraint on `Appointments.SlotId` turns this into a `DbUpdateException` with inner `SqlException (unique constraint violation)` which the saga can catch and fail gracefully.  
- The constraint requires **zero domain logic changes** — purely an EF Core configuration + migration.

**Alternatives considered**:
- *`SELECT FOR UPDATE` pessimistic lock*: Not natively supported by EF Core; requires raw SQL; rejected per constitution §I (no `FromSqlRaw` without review approval).  
- *Application-level duplicate check before insert*: Time-of-check/time-of-use (TOCTOU) race still exists; rejected.

---

## R3 — ProviderService MassTransit Wiring

**Decision**: Add MassTransit RabbitMQ to ProviderService; create `AppointmentBookedConsumer` and `SlotReleasedConsumer`.

**Rationale**:
- `AvailabilitySlot.Book()` and `AvailabilitySlot.Release()` domain methods already exist and are fully implemented (RQ10 confirms).  
- `V1_AppointmentBookedEvent` and `V1_SlotReleasedEvent` contracts exist in SharedKernel (RQ7 confirms).  
- The consumers are idempotent by design: `if (slot.Status == SlotStatus.Booked) return;` guard prevents re-processing.  
- Cache invalidation (`ICacheService.RemoveAsync`) must be called after slot state changes to honour constitution §III (Redis invalidation on write events).

**Consumers to create**:

| Consumer | Trigger | Action |
|---|---|---|
| `AppointmentBookedConsumer` | `V1_AppointmentBookedEvent` | `slot.Book()` → `SaveChanges` → cache invalidate |
| `SlotReleasedConsumer` | `V1_SlotReleasedEvent` | `slot.Release()` → `SaveChanges` → cache invalidate |

**MassTransit package** already in Solution as SharedKernel dependency; `MassTransit.RabbitMQ` NuGet must be added to `ProviderService.Infrastructure.csproj` and `AspNetCore.HealthChecks.Rabbitmq` to `ProviderService.API.csproj`.

**Alternatives considered**:
- *Synchronous gRPC call from AppointmentService to mark slot Booked*: Violated constitution §II (sync calls for state-changing ops are forbidden); also adds temporal coupling in a compensation path.

---

## R4 — MassTransit Retry + Dead-Letter Queue

**Decision**: Add `UseMessageRetry` with exponential back-off to all service MassTransit configurations; configure MassTransit dead-letter queue via `x.AddConfigureEndpointsCallback`.

**Rationale**:
- MassTransit 8 supports `UseMessageRetry` at the bus or endpoint level.  
- Retry policy: 3 attempts, exponential back-off with 1s base, 30s max, matching constitution §III (circuit breaker 3-retry pattern).  
- After 3 failures, MassTransit automatically routes to the `_error` queue (dead-letter) on RabbitMQ. No custom DLQ setup required — MassTransit handles it natively.  
- Applies to: NotificationService (2 existing consumers + new reschedule consumer), AppointmentService (saga error handling is separate — no consumer retry needed).  
- ProviderService (new consumers as per R3).

**Implementation pattern** (same for all services):
```csharp
cfg.UsingRabbitMq((ctx, rmq) =>
{
    rmq.Host(...);
    rmq.UseMessageRetry(r => r.Exponential(3,
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
    rmq.ConfigureEndpoints(ctx);
});
```

**Alternatives considered**:
- *Polly retry wrapping in consumer's `Consume()` method*: Self-managed retry; loses message metadata and doesn't integrate with RabbitMQ's dead-letter topology. Rejected — MassTransit's built-in retry is correct for this.

---

## R5 — AppointmentRescheduledConsumer (NotificationService)

**Decision**: Add a third consumer `AppointmentRescheduledConsumer` following the exact same pattern as the existing two consumers.

**Rationale**:
- NotificationService uses **direct MassTransit consumers** (no MediatR) — this was confirmed by research (RQ6): no `Commands/` folder exists; `AppointmentBookedConsumer` and `AppointmentCancelledConsumer` call `_emailService.SendAsync()` directly.  
- The new consumer follows identical idempotency + logging pattern.  
- `V1_AppointmentRescheduledEvent` contract already exists with `OldSlotId`, `NewSlotId`, `NewStartUtc`, `NewEndUtc` (RQ7 confirms).

---

## R6 — PII Masking in Serilog + OTel

**Decision**: Add Serilog `Destructure.ByTransforming<T>()` policies for PII-bearing DTOs; disable automatic HTTP body capture in OTel HttpClient instrumentation.

**Rationale**:
- Constitution §VI states: *"Healthcare data fields (patient PII) MUST never be logged in plain text"* — this is a constitution-level compliance requirement.  
- Destructuring policies in Serilog apply per-type at log-event creation time — zero runtime overhead.  
- The `PatientInfo` and `PatientDto` types are the primary PII vectors (FullName, ContactEmail, PhoneNumber).  
- OTel `AddHttpClientInstrumentation()` does not capture request/response bodies by default — no additional filter needed for that. The risk is from structured log calls like `_logger.LogInformation("{@Patient}", patient)`.

---

## R7 — Missing Appointment State Transitions (Confirm, MarkNoShow)

**Decision**: Add `Confirm()` and `MarkNoShow()` methods to `Appointment` aggregate; add corresponding domain events; create MediatR commands and endpoints.

**Rationale**:
- `AppointmentStatus` enum already has `Confirmed` and `NoShow` values.  
- `Complete()` already follows the pattern to copy — minimal implementation effort.  
- These are required by FR-016 and the spec state machine (spec.md §FR-016).

---

## R8 — Cancellation Notice Window

**Decision**: Add configurable `CancellationNoticePeriodHours` setting to `AppointmentService` `appsettings.json`; read via `IConfiguration` in `CancelAppointmentCommandHandler`; validate `appointment.ScheduledStartUtc - UtcNow >= noticeWindow`.

**Blocker identified**: `Appointment` entity does not store `ScheduledStartUtc` — the time is in `AvailabilitySlot` (ProviderService). Two options:  
- Option A: Add `ScheduledStartUtc` to `Appointment` entity, populated at booking time from `SlotInfo`.  
- Option B: Fetch slot via gRPC in the cancel handler to get the time.

**Decision**: Option A — store `ScheduledStartUtc` on `Appointment` during `Book()` factory. This eliminates a gRPC call in the cancellation hot path and makes the Appointment self-contained for time-window logic. `PersistAppointmentActivity` already has `SlotInfo` available at booking time.

---

## R9 — CI Coverage Threshold

**Decision**: Add a `reportgenerator` step to `.github/workflows/ci.yml` that fails the build if Domain + Application coverage is below 80%.

**Tool**: `dotnet-reportgenerator-globaltool` — widely used, supports Cobertura XML output from `dotnet test --collect:"XPlat Code Coverage"`.

---

## R10 — Missing Documentation

**Decisions**:

| Document | Content | Priority |
|---|---|---|
| `docs/booking-saga.md` | State machine diagram + activity descriptions + compensation logic | P0 (spec required, T089) |
| `docs/message-versioning.md` | Versioning policy, all V1 contracts, upgrade guidance | P1 (T114) |
| `docs/risk-register.md` | 5 risks from constitution + 3 new risks from audit | P1 (T138) |
| `docs/sprint-closure.md` | Week-by-week deliverables summary, test counts, gaps closed | P1 (T141) |

---

## Resolution Matrix

All NEEDS CLARIFICATION items from Technical Context are now resolved:

| Item | Resolved To |
|---|---|
| Does Reschedule need entity time fields? | Yes — add `ScheduledStartUtc` to `Appointment` (needed for cancel notice window too) |
| Which services need MassTransit added? | ProviderService (new), AppointmentService + NotificationService (retry config only) |
| NotificationService command vs consumer? | Direct consumer pattern — no MediatR commands needed |
| Is V1_AppointmentRescheduledEvent missing? | No — exists in Contracts library; domain event is missing |
| AvailabilitySlot.Book() available? | Yes — fully implemented, just not called by any consumer |
