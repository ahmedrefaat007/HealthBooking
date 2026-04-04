# Booking Saga Documentation

**Version**: 1.0 | **Date**: 2026-04-04 | **Service**: AppointmentService  
**Spec Reference**: FR-017, T089  
**Technology**: MassTransit 8 `MassTransitStateMachine<TState>`, EF Core 9 saga state persistence

---

## Overview

The booking saga is an **orchestration-based distributed transaction** that coordinates three services (Patient, Provider, Appointment) to book a healthcare appointment. It uses MassTransit state machine persistence backed by SQL Server (same AppointmentService database).

The saga is triggered by a `V1_InitiateBookingCommand` published via MassTransit request/response from the AppointmentService API endpoint. It completes with either `V1_BookingCompletedEvent` (success) or `V1_BookingFailedEvent` (failure / timeout).

---

## State Machine Diagram

```
                            ┌─────────────────────────────────┐
                            │ V1_InitiateBookingCommand        │
                            │ (from POST /api/appointments)    │
                            └────────────────┬────────────────┘
                                             │ Initial
                                             ▼
                                     ┌──────────────┐
                                     │   Verifying  │  ← State 1
                                     └──────┬───────┘
                                            │ VerifyPatientActivity
                                            │ [gRPC: GetPatientById]
                              ┌─────────────┴──────────────┐
                              │ Patient found               │ Patient not found
                              ▼                             ▼
                     ┌────────────────┐           ┌─────────────────┐
                     │  LockingSlot   │           │     Faulted     │
                     └───────┬────────┘           │  (BookingFailed)│
                             │ LockSlotActivity    └─────────────────┘
                             │ [gRPC: LockSlot]
                ┌────────────┴──────────────┐
                │ Lock succeeded             │ Lock failed (conflict/timeout)
                ▼                           ▼
     ┌──────────────────────┐     ┌─────────────────┐
     │ PersistingAppointment│     │     Faulted     │
     └──────────┬───────────┘     │  (BookingFailed)│
                │ PersistAppointmentActivity          └─────────────────┘
                │ [EF Core: INSERT Appointment + OutboxMessage]
     ┌──────────┴──────────┐
     │ Success              │ DbUpdateException (unique slot violated)
     ▼                     ▼
┌──────────┐     ┌─────────────────┐
│Completed │     │     Faulted     │
│(BookingCompleted)│ (BookingFailed) │
└──────────┘     └─────────────────┘

30-second timeout at any state → Faulted + compensation
```

---

## States

| State | Description |
|---|---|
| `Verifying` | Saga has started; `VerifyPatientActivity` is executing or scheduled |
| `LockingSlot` | Patient verified; `LockSlotActivity` is executing |
| `PersistingAppointment` | Slot locked; `PersistAppointmentActivity` is executing |
| `Completed` | All 3 activities succeeded; `V1_BookingCompletedEvent` published |
| `Faulted` | Any activity failed; `V1_BookingFailedEvent` published; compensation executed |

---

## Activities

### Activity 1: VerifyPatientActivity

**File**: `AppointmentService.Infrastructure/Saga/Activities/VerifyPatientActivity.cs`

```
Execute:
  patient = gRPC PatientGrpcClient.GetPatientByIdAsync(context.PatientId)
  if patient is null → throw NotFoundException("Patient not found")
  context.Saga.PatientName = patient.FullName
  context.Saga.PatientVerified = true

Faulted (compensation):
  No side effect to undo (read-only operation)
```

**Failure modes**: gRPC timeout → Faulted state; patient not found → Faulted state

---

### Activity 2: LockSlotActivity

**File**: `AppointmentService.Infrastructure/Saga/Activities/LockSlotActivity.cs`

```
Execute:
  success = gRPC ProviderSlotGrpcClient.LockSlotAsync(context.SlotId, context.Saga.CorrelationId)
  if !success → throw SlotConflictException("Slot unavailable")
  context.Saga.SlotWasLocked = true

Faulted (compensation):
  if context.Saga.SlotWasLocked:
    gRPC ProviderSlotGrpcClient.ReleaseSlotAsync(context.SlotId)
    context.Saga.SlotWasLocked = false
```

**RowVersion concurrency**: `AvailabilitySlot` in ProviderService uses EF Core `RowVersion` optimistic concurrency. Two concurrent `LockSlot` gRPC calls for the same slot → one gets `DbUpdateConcurrencyException` → gRPC returns `StatusCode.Aborted` → saga throws `SlotConflictException`.

**Compensation trigger**: On fault at this or any later step, `LockSlotActivity.Faulted()` releasses the slot if `SlotWasLocked == true`.

---

### Activity 3: PersistAppointmentActivity

**File**: `AppointmentService.Infrastructure/Saga/Activities/PersistAppointmentActivity.cs`

```
Execute:
  appointment = Appointment.Book(
    patientId:        context.Saga.PatientId,
    slotId:           context.Saga.SlotId,
    patientName:      context.Saga.PatientName,
    scheduledStart:   slotInfo.StartTimeUtc)      ← gRPC GetSlotById for time
  repository.AddAsync(appointment)
  repository.SaveChangesAsync()                  ← triggers OutboxPublishingInterceptor
  // OutboxMessage with AppointmentBookedEvent written in SAME transaction
  context.Saga.AppointmentId = appointment.Id

Faulted (compensation):
  if context.Saga.SlotWasLocked:
    gRPC ProviderSlotGrpcClient.ReleaseSlotAsync(context.SlotId)
  // Appointment was NOT committed → no rollback needed
```

**Atomicity**: The `Appointment` row and the `OutboxMessage` row are committed in the same `SaveChangesAsync()` EF Core transaction. This guarantees:
- Either both are persisted → Outbox processor will publish the event
- Or neither is persisted → saga can retry or fail gracefully

---

## Correlation ID

Every saga instance has a `CorrelationId` (type `Guid`) used as:

1. **Saga state identifier** — `BookingState.CorrelationId` is the primary key in `BookingStates` table
2. **Message correlation** — embedded in `V1_InitiateBookingCommand`, `V1_BookingCompletedEvent`, `V1_BookingFailedEvent`
3. **Domain event payload** — `AppointmentBookedEvent` carries `SagaCorrelationId` which flows into `V1_AppointmentBookedEvent` and ultimately appears in NotificationService logs

### Correlation ID in logs

Every HTTP handler that initiates a saga sets:
```http
X-Correlation-Id: {correlationId}
```
The `CorrelationIdMiddleware` pushes this to Serilog's `LogContext`, so all log entries within the request carry `CorrelationId`. The saga's `CorrelationId` matches the HTTP correlation ID for full traceability.

---

## Timeout & Dead-Letter

| Scenario | Outcome |
|---|---|
| Saga completes within 30s | `V1_BookingCompletedEvent` returned to caller |
| Any activity throws | `BookingStateMachine.OnFault()` → compensation → `V1_BookingFailedEvent` |
| 30s timeout (e.g., gRPC hang) | `BookingTimeoutExpired` scheduled event fires → transitions to `Faulted` → compensation → `V1_BookingFailedEvent` |
| Saga state machine exception | MassTransit moves saga instance to `_error` queue; manual review required |

---

## Saga State Persistence

**Table**: `BookingStates` (AppointmentService DB)

| Column | Type | Notes |
|---|---|---|
| `CorrelationId` | `uniqueidentifier` PK | Saga instance identifier |
| `CurrentState` | `nvarchar(64)` | State name string |
| `PatientId` | `uniqueidentifier` | |
| `SlotId` | `uniqueidentifier` | |
| `PatientName` | `nvarchar(200)` | Set by VerifyPatientActivity |
| `PatientVerified` | `bit` | Compensation guard |
| `SlotWasLocked` | `bit` | Compensation guard |
| `AppointmentId` | `uniqueidentifier?` | Set by PersistAppointmentActivity |
| `IdempotencyKey` | `nvarchar(500)` | From HTTP Idempotency-Key header |
| `RowVersion` | `rowversion` | Optimistic concurrency for concurrent saga messages |

---

## Idempotency

Before initiating the saga, the API endpoint checks `BookingIdempotencyKey` repository:
- If key found → return existing `Appointment` without re-triggering saga
- If key not found → initiate saga

This prevents duplicate bookings from network retries at the client level.
