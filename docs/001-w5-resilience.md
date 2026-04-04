# Week 5 — Resilience & MassTransit Booking Saga (T121–T155)

Branch: `001-w5-resilience`  
Merged from: `001-w4-integration`

---

## Overview

Week 5 hardens the booking flow with three complementary improvements:

1. **NotificationService — real patient email via gRPC** — consumers no longer use a placeholder email. A new `IPatientEmailClient` abstraction makes a gRPC call to PatientService and falls back gracefully when the call fails.
2. **ProviderService — Redis cache invalidation** — `ProviderGrpcService` now evicts the slot-list cache key whenever a slot is locked or released, so the 60-second TTL never serves stale availability data after a mutation.
3. **AppointmentService — MassTransit Booking Saga** — the synchronous `BookAppointmentCommandHandler` is replaced by a durable `MassTransitStateMachine<BookingState>` that orchestrates three activities with built-in compensation. The HTTP endpoint switches to a request/response pattern with a 30-second timeout.

---

## 1. Contracts — `HealthBooking.Contracts`

### New messages (`Appointments/V1/V1_BookingMessages.cs`)

| Message | Direction | Purpose |
|---|---|---|
| `V1_InitiateBookingCommand` | HTTP endpoint → Saga | Starts the booking flow; carries `CorrelationId`, `PatientId`, `SlotId`, `IdempotencyKey` |
| `V1_BookingCompletedEvent` | Saga → HTTP endpoint | Success response; carries `AppointmentId` |
| `V1_BookingFailedEvent` | Saga → HTTP endpoint | Failure response; carries `Reason` |

All three are `sealed record` types (reference types) to satisfy MassTransit's generic constraints.

---

## 2. NotificationService — real gRPC patient email

### Problem
`AppointmentBookedConsumer` and `AppointmentCancelledConsumer` used the hard-coded placeholder `patient-{id}@placeholder.local`. This was acceptable during scaffolding but not production-ready.

### Solution
| Component | File | Change |
|---|---|---|
| Interface | `INotificationInterfaces.cs` | Added `IPatientEmailClient` — `Task<string?> GetPatientEmailAsync(Guid, CancellationToken)` |
| Implementation | `NotificationService.Infrastructure/Clients/NotificationPatientGrpcClient.cs` | Wraps `PatientGrpc.PatientGrpcClient`; returns `null` on `NotFound` RpcException |
| Consumers | `AppointmentBookedConsumer`, `AppointmentCancelledConsumer` | Resolve real email; fall back to placeholder if gRPC returns null |
| DI | `NotificationService.API/Program.cs` | `AddGrpcClient<PatientGrpc.PatientGrpcClient>` + `AddHealthBookingResiliencePipeline("notification-patient-grpc")` + `IPatientEmailClient → NotificationPatientGrpcClient` |
| Config | `appsettings.json` | `GrpcClients:PatientService` base address |
| Package | `NotificationService.API.csproj` | `Grpc.Net.ClientFactory 2.76.0` |

The Polly resilience pipeline (retry + circuit breaker) from `HealthBooking.SharedKernel` is reused, keeping the pattern consistent with all other gRPC clients in the solution.

---

## 3. ProviderService — Redis slot-list cache invalidation

### Problem
`GetProviderSlotsQueryHandler` caches the slot list for a provider with a 60-second TTL. However, `ProviderGrpcService.LockSlot` and `ReleaseSlot` mutated the DB without evicting the cache, so bookings made through the saga could run against stale slot states.

### Solution

`ProviderGrpcService` now accepts `ICacheService` (already in the DI container from Week 3) and calls:

```csharp
await cache.RemoveAsync($"slots:{slot.ProviderId}");
```

immediately after each successful `SaveChangesAsync` in both `LockSlot` and `ReleaseSlot`. This ensures subsequent `GetProviderSlots` queries always reflect the latest DB state.

---

## 4. AppointmentService — MassTransit Booking Saga

### Motivation
The original `BookAppointmentCommandHandler` was synchronous and had no compensation path — if the DB write failed after the slot was locked, the slot stayed locked forever. The saga replaces this with a durable, compensatable orchestration.

### State machine design

```
                     ┌─────────────────────────────────────────────┐
V1_InitiateBooking   │  VerifyPatient   LockSlot   PersistAppt     │
           ─────────►│  Activity   ──►  Activity ──► Activity  ──► │──► Completed
  Initial             │                                             │
                      └──────────────────────────────┬────────────┘
                                                     │ Exception
                                                     ▼
                                              Faulted chain
                                         (LockSlotActivity.Faulted
                                          releases slot if locked)
                                                     │
                                                     ▼
                                                  Failed
```

### States

| State | Meaning |
|---|---|
| `Submitted` | Command received; activities running |
| `Completed` | All three activities succeeded; appointment persisted |
| `Failed` | One activity threw; compensation ran |

### Activities

| Activity | Execute | Faulted (compensation) |
|---|---|---|
| `VerifyPatientActivity` | gRPC call to PatientService; sets `saga.PatientName` | Nothing — read-only |
| `LockSlotActivity` | gRPC call to ProviderService; sets `saga.SlotWasLocked = true` | Releases slot if `SlotWasLocked` |
| `PersistAppointmentActivity` | Checks idempotency; creates `Appointment.Book()`; saves | Nothing — slot released upstream |

### Idempotency handling
- **Endpoint level**: pre-checks `IIdempotencyRepository` before publishing the command. Returns `200 OK` immediately for duplicate keys.
- **Activity level**: `PersistAppointmentActivity` re-checks inside the saga. If the key was already committed (e.g., a retried message), it reuses the existing `AppointmentId`.

### Request/Response pattern

The HTTP endpoint publishes `V1_InitiateBookingCommand` via `IRequestClient<T>` and awaits `Response<V1_BookingCompletedEvent, V1_BookingFailedEvent>` with a 30-second timeout:

| Result | HTTP response |
|---|---|
| `V1_BookingCompletedEvent` | `201 Created` with `appointmentId` |
| `V1_BookingFailedEvent` | `409 Conflict` with reason |
| `RequestTimeoutException` | `503 Service Unavailable` |

### Persistence

`BookingState` is persisted in SQL Server via `MassTransit.EntityFrameworkCore` with `ConcurrencyMode.Optimistic`. The table is created by EF migration `AddBookingSaga`:

```
Table: BookingSagaStates
  CorrelationId  uniqueidentifier NOT NULL (PK)
  CurrentState   nvarchar(64)
  PatientId      uniqueidentifier
  SlotId         uniqueidentifier
  IdempotencyKey nvarchar(256) (UNIQUE index)
  AppointmentId  uniqueidentifier (nullable)
  PatientName    nvarchar(256) (nullable)
  SlotWasLocked  bit
  FailureReason  nvarchar(1024) (nullable)
  CreatedAt      datetimeoffset
```

`SetCompletedWhenFinalized()` is configured so that completed/failed sagas are eventually removed from the table, keeping it lean.

---

## 5. Key technical decisions

| Decision | Choice | Reason |
|---|---|---|
| Activity registration | `services.AddTransient<TActivity>()` | `IStateMachineActivity<T,D>` uses standard DI; `cfg.AddActivity<>()` is for Courier routing-slip only |
| `ConcurrencyMode` | `Optimistic` | Saga rows have a `CorrelationId` PK; SQL-level row versioning prevents duplicate processing |
| Lock reference | `saga.CorrelationId` | Keeps the appointment ID and the lock reference consistent throughout the saga lifetime |
| gRPC version | Pinned (`2.76.0`) | `Version="2.*"` caused `MSBuild::VersionLessThan` failures in `Microsoft.Extensions.Http.Resilience.targets` |

---

## 6. Tests

### New tests

| File | Count | Coverage |
|---|---|---|
| `AppointmentService.UnitTests/Saga/BookingActivitiesTests.cs` | 10 | All three activities: happy path, error paths, compensation paths |

### Updated tests

| File | Change |
|---|---|
| `NotificationService.UnitTests/Application/AppointmentBookedConsumerTests.cs` | Added `IPatientEmailClient` mock (returns `null` → fallback email) to match updated constructor signature |

### Test results

```
AppointmentService.UnitTests   Passed: 21   (was 11 before Week 5)
NotificationService.UnitTests  Passed:  4
ProviderService.UnitTests      Passed: 10
PatientService.UnitTests       Passed: 19 / Failed: 1 (pre-existing, unrelated to Week 5)
```
