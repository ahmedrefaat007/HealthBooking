# Data Model: Gap Remediation Changes

**Phase 1 Output** | **Date**: 2026-04-04  
**Scope**: Entity changes, new migration requirements, and relationship updates needed to close all critical + important spec gaps.

---

## 1. Appointment Entity — Modified

**File**: `src/Services/AppointmentService/AppointmentService.Domain/Entities/Appointment.cs`

### New Property Added

| Property | Type | Nullable | Source | Notes |
|---|---|---|---|---|
| `ScheduledStartUtc` | `DateTimeOffset` | No | Set at `Book()` via resolved `SlotInfo.StartTimeUtc` | Required for cancellation notice-window check; eliminates gRPC call in cancel handler |

### New State-Transition Methods Added

| Method | FromStatus | ToStatus | Guard | Domain Event Raised |
|---|---|---|---|---|
| `Reschedule(Guid newSlotId, DateTimeOffset newStart)` | `Booked` or `Confirmed` | _(status unchanged, SlotId mutated)_ | Must not be Cancelled/Completed | `AppointmentRescheduledDomainEvent` |
| `Confirm()` | `Booked` | `Confirmed` | Must be `Booked` | `AppointmentConfirmedDomainEvent` |
| `MarkNoShow()` | `Confirmed` | `NoShow` | Must be `Confirmed` | `AppointmentNoShowDomainEvent` |

### Full Updated Entity Shape

```csharp
public sealed class Appointment : AggregateRoot
{
    public Guid              Id                  { get; private set; }
    public Guid              PatientId           { get; private set; }
    public Guid              SlotId              { get; private set; }        // mutated on Reschedule
    public string            PatientName         { get; private set; }
    public DateTimeOffset    ScheduledStartUtc   { get; private set; }       // NEW
    public AppointmentStatus Status              { get; private set; }
    public string?           CancelReason        { get; private set; }

    // Factory
    public static Appointment Book(Guid patientId, Guid slotId,
                                   string patientName, DateTimeOffset scheduledStart);
    // Transitions
    public void Cancel(string reason);       // existing
    public void Complete();                  // existing
    public void Confirm();                   // NEW
    public void MarkNoShow();               // NEW
    public void Reschedule(Guid newSlotId, DateTimeOffset newStart); // NEW
}
```

### EF Core Configuration Changes

**File**: `src/Services/AppointmentService/AppointmentService.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs`

```csharp
// ADD: unique index to prevent double-booking at DB level (Gap C2)
builder.HasIndex(a => a.SlotId)
    .IsUnique()
    .HasDatabaseName("UQ_Appointments_SlotId");

// ADD: new column mapping
builder.Property(a => a.ScheduledStartUtc).IsRequired();
```

### New Migration Required

```
Migration name: AddScheduledStartAndUniqueSlotId
Changes:
  - ADD COLUMN [ScheduledStartUtc] datetimeoffset NOT NULL DEFAULT '0001-01-01'
  - CREATE UNIQUE INDEX [UQ_Appointments_SlotId] ON [Appointments] ([SlotId])
```

**Backward compatibility**: `DEFAULT '0001-01-01'` on existing rows; acceptable for a development/training environment.

---

## 2. AppointmentEvents — New Domain Events

**File**: `src/Services/AppointmentService/AppointmentService.Domain/Events/AppointmentEvents.cs`

### Additions

```csharp
// Existing (unchanged):
public sealed record AppointmentBookedEvent(Guid AppointmentId, Guid PatientId,
    Guid SlotId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentCancelledEvent(Guid AppointmentId,
    string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentCompletedEvent(Guid AppointmentId,
    DateTimeOffset OccurredAt) : IDomainEvent;

// ── NEW ─────────────────────────────────────────────────────────────────────

public sealed record AppointmentRescheduledDomainEvent(
    Guid   AppointmentId,
    Guid   PatientId,
    Guid   ProviderId,
    Guid   OldSlotId,
    Guid   NewSlotId,
    DateTimeOffset NewStartUtc,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentConfirmedDomainEvent(
    Guid   AppointmentId,
    Guid   PatientId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentNoShowDomainEvent(
    Guid   AppointmentId,
    Guid   PatientId,
    DateTimeOffset OccurredAt) : IDomainEvent;
```

---

## 3. AvailabilitySlot — No Changes Required

The `AvailabilitySlot` entity in ProviderService already has:
- `Lock(Guid appointmentId)` ✅
- `Release()` ✅
- `Book()` ✅
- `RowVersion` optimistic concurrency token ✅
- `SlotStatus` enum with all required values ✅

**No entity changes needed in ProviderService.**

---

## 4. New MassTransit Consumers — ProviderService

These consumers are **new Infrastructure files** (no entity changes needed):

### `AppointmentBookedConsumer`

**File**: `src/Services/ProviderService/ProviderService.Infrastructure/Messaging/Consumers/AppointmentBookedConsumer.cs`

```
Consumes:  V1_AppointmentBookedEvent
Reads:     ISlotRepository.GetByIdAsync(msg.SlotId)
Mutates:   slot.Book()  [Status: Locked → Booked]
Writes:    ISlotRepository.SaveChangesAsync()
Invalidates: ICacheService.RemoveAsync("provider:slots:{ProviderId}:{Date}")
Idempotent: if (slot is null || slot.Status == SlotStatus.Booked) return
```

### `SlotReleasedConsumer`

**File**: `src/Services/ProviderService/ProviderService.Infrastructure/Messaging/Consumers/SlotReleasedConsumer.cs`

```
Consumes:  V1_SlotReleasedEvent
Reads:     ISlotRepository.GetByIdAsync(msg.SlotId)
Mutates:   slot.Release()  [Status: Locked → Available]
Writes:    ISlotRepository.SaveChangesAsync()
Invalidates: ICacheService.RemoveAsync("provider:slots:{ProviderId}:{Date}")
Idempotent: if (slot is null || slot.Status == SlotStatus.Available) return
```

---

## 5. New MassTransit Consumer — NotificationService

### `AppointmentRescheduledConsumer`

**File**: `src/Services/NotificationService/NotificationService.Application/Consumers/AppointmentRescheduledConsumer.cs`

```
Consumes:  V1_AppointmentRescheduledEvent
Pattern:   Identical to AppointmentBookedConsumer (idempotency check → gRPC patient lookup → email)
Template:  "Your appointment has been rescheduled to {NewStartUtc:f}"
```

---

## 6. New Application Commands — AppointmentService

### `RescheduleAppointmentCommand`

**File**: `src/Services/AppointmentService/AppointmentService.Application/Commands/RescheduleAppointment/RescheduleAppointmentCommand.cs`

```
Command:   RescheduleAppointmentCommand(Guid AppointmentId, Guid NewSlotId, string CallerUserId)
Validator: AppointmentId.NotEmpty, NewSlotId.NotEmpty, CallerUserId.NotEmpty
Handler sequence:
  1. appointments.GetByIdAsync(AppointmentId) → NotFoundException
  2. Authorization: appointment.PatientId.ToString() == CallerUserId
  3. slotClient.GetSlotByIdAsync(appointment.SlotId) → get old slot times
  4. slotClient.GetSlotByIdAsync(NewSlotId) → get new slot info (validate Available status)
  5. slotClient.ReleaseSlotAsync(appointment.SlotId)
  6. slotClient.LockSlotAsync(NewSlotId, AppointmentId)
  7. appointment.Reschedule(NewSlotId, newSlot.StartTimeUtc)
  8. appointments.SaveChangesAsync()  ← outbox captures AppointmentRescheduledDomainEvent
```

### `ConfirmAppointmentCommand`

**File**: `src/Services/AppointmentService/AppointmentService.Application/Commands/ConfirmAppointment/ConfirmAppointmentCommand.cs`

```
Command:   ConfirmAppointmentCommand(Guid AppointmentId, string CallerUserId)
Handler:   Load → authorize → appointment.Confirm() → SaveChanges
```

### `MarkNoShowCommand`

**File**: `src/Services/AppointmentService/AppointmentService.Application/Commands/MarkNoShow/MarkNoShowCommand.cs`

```
Command:   MarkNoShowCommand(Guid AppointmentId, string CallerUserId)
Handler:   Load → authorize → appointment.MarkNoShow() → SaveChanges
```

---

## 7. New API Endpoints — AppointmentService

**File**: `src/Services/AppointmentService/AppointmentService.API/Endpoints/AppointmentsEndpoints.cs`

| Method | Route | Command |
|---|---|---|
| `PUT` | `/api/appointments/{id}/reschedule` | `RescheduleAppointmentCommand` |
| `POST` | `/api/appointments/{id}/confirm` | `ConfirmAppointmentCommand` |
| `POST` | `/api/appointments/{id}/no-show` | `MarkNoShowCommand` |

---

## 8. State Machine Summary (Updated)

```
                       ┌────────┐
          Book()       │ Booked │
     ───────────────►  └───┬────┘
                           │ Confirm()          cancel window check
                       ┌───▼─────┐  Cancel()  ┌───────────┐
                       │Confirmed│ ──────────► │ Cancelled │
                       └───┬─────┘             └───────────┘
                           │
               ┌───────────┼────────────┐
               │           │            │
          Complete()  MarkNoShow()  Reschedule()
               │           │            │ (mutates SlotId,
          ┌────▼───┐ ┌─────▼──┐        │  raises event,
          │Complete│ │ NoShow │        │  stays Confirmed)
          └────────┘ └────────┘        └────► (same Confirmed state,
                                               new SlotId)
```

---

## 9. Outbox → Contract Event Mapping

| Domain Event (AppointmentService.Domain) | Contract (SharedKernel V1) | Consumer |
|---|---|---|
| `AppointmentBookedEvent` | `V1_AppointmentBookedEvent` | NotificationService.AppointmentBookedConsumer, ProviderService.AppointmentBookedConsumer |
| `AppointmentCancelledEvent` | `V1_AppointmentCancelledEvent` | NotificationService.AppointmentCancelledConsumer |
| `AppointmentRescheduledDomainEvent` | `V1_AppointmentRescheduledEvent` | NotificationService.AppointmentRescheduledConsumer |
| _(no domain event needed)_ | `V1_SlotReleasedEvent` | ProviderService.SlotReleasedConsumer |

**Note**: `V1_SlotReleasedEvent` is published by `CancelAppointmentCommandHandler` directly via `IPublishEndpoint` (same as how `ReleaseSlotAsync` gRPC call is made), since slot release is not an Appointment aggregate state change — it is a side effect.

---

## 10. CI Pipeline Changes

**File**: `.github/workflows/ci.yml`

New job step after `dotnet test`:

```yaml
- name: Generate coverage report
  run: |
    dotnet tool install --global dotnet-reportgenerator-globaltool
    reportgenerator \
      -reports:**/coverage.cobertura.xml \
      -targetdir:coverage-report \
      -reporttypes:TextSummary

- name: Enforce 80% coverage threshold
  run: |
    $summary = Get-Content coverage-report/Summary.txt
    $line = $summary | Select-String "Line coverage:"
    $pct = [double]($line -replace '[^0-9\.]')
    if ($pct -lt 80.0) { throw "Coverage $pct% is below 80% threshold" }
  shell: pwsh
```
