# Message Versioning Guide

**Version**: 1.0 | **Date**: 2026-04-04  
**Technology**: MassTransit 8 + RabbitMQ 3  
**Spec Reference**: FR-016–FR-018, T089, T108

---

## Overview

All inter-service messages in this system use an explicit versioning scheme based on the **V-number prefix convention**. Every message contract carries a `V{n}_` prefix in its C# class name and assembly location. This document defines:

1. The versioning contract convention
2. The full current message inventory
3. Consumer-to-contract mapping
4. Breaking vs. non-breaking change rules
5. V2 introduction procedure

---

## Versioning Convention

### Namespace Structure

```
HealthBooking.Contracts/
  Appointments/
    V1/
      V1_AppointmentEvents.cs      ← all Appointment-scoped events
      V1_BookingCommands.cs        ← saga commands & responses
    V2/                            ← introduced only when a breaking change is needed
      V2_AppointmentEvents.cs
```

### Class Naming Pattern

```csharp
// Pattern: V{version}_{ConceptualName}{Type}
V1_AppointmentBookedEvent
V1_AppointmentCancelledEvent
V1_AppointmentRescheduledEvent
V1_SlotReleasedEvent
V1_InitiateBookingCommand
V1_BookingCompletedEvent
V1_BookingFailedEvent
```

**Rule**: Every message MUST carry the version prefix. Plain-name messages (e.g., `AppointmentBookedEvent`) are internal domain events only and MUST NOT be published to RabbitMQ.

### Exchange Naming (RabbitMQ)

MassTransit derives the exchange name from the fully-qualified type name by default. With the V-prefix convention, exchange names are automatically versioned:

```
health-booking.contracts.appointments.v1:v1_appointmentbookedevent
health-booking.contracts.appointments.v1:v1_appointmentcancelledevent
health-booking.contracts.appointments.v1:v1_appointmentrescheduledevent
health-booking.contracts.appointments.v1:v1_slotreleasedevent
health-booking.contracts.bookings.v1:v1_initiatebookingcommand
health-booking.contracts.bookings.v1:v1_bookingcompletedevent
health-booking.contracts.bookings.v1:v1_bookingfailedevent
```

---

## Contract Inventory (V1 — Current)

### Commands

| Contract Class | Source Service | Target | Exchange | Purpose |
|---|---|---|---|---|
| `V1_InitiateBookingCommand` | AppointmentService API | BookingStateMachine | `v1_initiatebookingcommand` | Trigger saga via MassTransit request/response |

### Events

| Contract Class | Publisher | Exchange | Purpose |
|---|---|---|---|
| `V1_BookingCompletedEvent` | BookingStateMachine | `v1_bookingcompletedevent` | Saga complete response |
| `V1_BookingFailedEvent` | BookingStateMachine | `v1_bookingfailedevent` | Saga failed response |
| `V1_AppointmentBookedEvent` | AppointmentService Outbox | `v1_appointmentbookedevent` | Notify downstream after booking persisted |
| `V1_AppointmentCancelledEvent` | AppointmentService Outbox | `v1_appointmentcancelledevent` | Notify downstream on cancellation |
| `V1_AppointmentRescheduledEvent` | AppointmentService Outbox | `v1_appointmentrescheduledevent` | Notify downstream on reschedule |
| `V1_SlotReleasedEvent` | AppointmentService Outbox | `v1_slotreleasedevent` | Instruct ProviderService to release slot |

---

## Contract Field Reference

### V1_AppointmentBookedEvent
```csharp
public record V1_AppointmentBookedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    string PatientName,
    DateTime ScheduledStartUtc,         // added in gap-remediation sprint
    DateTime OccurredOnUtc
);
```

### V1_AppointmentCancelledEvent
```csharp
public record V1_AppointmentCancelledEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid SlotId,
    string? CancelReason,
    DateTime OccurredOnUtc
);
```

### V1_AppointmentRescheduledEvent
```csharp
public record V1_AppointmentRescheduledEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid OldSlotId,
    Guid NewSlotId,
    DateTime NewScheduledStartUtc,
    DateTime OccurredOnUtc
);
```

### V1_SlotReleasedEvent
```csharp
public record V1_SlotReleasedEvent(
    Guid SlotId,
    Guid AppointmentId,
    string Reason,               // "Cancelled" | "Rescheduled"
    DateTime OccurredOnUtc
);
```

### V1_InitiateBookingCommand
```csharp
public record V1_InitiateBookingCommand(
    Guid CorrelationId,
    Guid PatientId,
    Guid SlotId,
    string IdempotencyKey
);
```

### V1_BookingCompletedEvent
```csharp
public record V1_BookingCompletedEvent(
    Guid CorrelationId,
    Guid AppointmentId
);
```

### V1_BookingFailedEvent
```csharp
public record V1_BookingFailedEvent(
    Guid CorrelationId,
    string Reason
);
```

---

## Consumer-to-Contract Mapping

| Service | Consumer Class | Consumes |
|---|---|---|
| AppointmentService | `BookingStateMachine` | `V1_InitiateBookingCommand` |
| NotificationService | `AppointmentBookedConsumer` | `V1_AppointmentBookedEvent` |
| NotificationService | `AppointmentCancelledConsumer` | `V1_AppointmentCancelledEvent` |
| NotificationService | `AppointmentRescheduledConsumer` *(planned)* | `V1_AppointmentRescheduledEvent` |
| ProviderService | `AppointmentBookedConsumer` *(planned)* | `V1_AppointmentBookedEvent` |
| ProviderService | `SlotReleasedConsumer` *(planned)* | `V1_SlotReleasedEvent` |

---

## Change Classification

### Non-breaking changes (safe to publish; no consumer change needed)

- Adding a **new optional field** (with a default value) to an existing contract
- Adding a **new event type** — existing consumers silently ignore unknown exchanges
- Adding a **new consumer** to an existing exchange

**Example**: Adding `ScheduledStartUtc` to `V1_AppointmentBookedEvent` is non-breaking if existing consumers ignore the new field (or deserialise it via `[JsonIgnore]`).

### Breaking changes (require V2 introduction)

- **Removing** an existing field
- **Renaming** an existing field
- **Changing the type** of an existing field (e.g., `string → Guid`)
- **Changing semantics** of an existing field in a backward-incompatible way

---

## V2 Introduction Procedure

When a breaking change is required:

1. **Create V2 contract** alongside V1 in a new `V2/` folder — do NOT delete V1
2. **Dual-publish** from the producer: publish both `V1_*` and `V2_*` events simultaneously for one release cycle
3. **Migrate consumers** one-by-one to consume V2 and drop V1
4. **Remove V1 dual-publish** once all consumers are on V2
5. **Deprecate V1 class** with `[Obsolete]` attribute and a target-removal release comment
6. **Remove V1 class** in the release after deprecation

```csharp
// Step 5: mark for removal
[Obsolete("Use V2_AppointmentBookedEvent. Remove in v3.0.")]
public record V1_AppointmentBookedEvent(...);
```

### Timeline Recommendation

| Phase | Duration |
|---|---|
| Dual-publish (V1 + V2) | 1 sprint (2 weeks) |
| Consumer migration | 1 sprint per service |
| V1 deprecation | 1 sprint |
| V1 removal | Next major version |

---

## Retry & Dead-Letter Policy

All consumers should configure:

```csharp
cfg.UseMessageRetry(r => r.Exponential(
    retryLimit: 5,
    minInterval: TimeSpan.FromSeconds(1),
    maxInterval: TimeSpan.FromSeconds(30),
    intervalDelta: TimeSpan.FromSeconds(5)));
```

After 5 failed retries, MassTransit moves the message to the dead-letter queue:

| Queue | Exchange | Policy |
|---|---|---|
| `{consumer-queue}_error` | Auto-created by MassTransit | Manual review + replaying required |
| `{consumer-queue}_skipped` | Auto-created | Message type not handled; safe to ignore |

**Operations requirement**: Monitor `_error` queues. Any message older than 24h in `_error` requires incident response.
