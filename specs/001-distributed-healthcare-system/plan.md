# Implementation Plan: HealthBooking Gap Remediation Sprint

**Branch**: `001-distributed-healthcare-system` | **Date**: 2026-04-04 | **Spec**: [spec.md](spec.md)
**Input**: Gap analysis from [`docs/SPEC-COMPLIANCE-AUDIT.md`](../../docs/SPEC-COMPLIANCE-AUDIT.md)
**Audit score before this plan**: 22/37 FRs fully implemented (59%)
**Target score after this plan**: 34/37 FRs (92%)  all critical + important gaps closed

---

## Summary

The HealthBooking distributed system has a solid architectural foundation (Clean Architecture + DDD + Saga + Outbox + YARP + Duende IdentityServer) but 15 spec gaps were found in a compliance audit. This plan addresses all 5 critical gaps and 7 important gaps, organised into 10 implementation phases:

- **Phase A**: Domain + Entity Changes (ScheduledStartUtc, Reschedule/Confirm/MarkNoShow methods)
- **Phase B**: EF Core migration (UNIQUE SlotId index + new column)
- **Phase C**: Application commands + endpoints (Reschedule, Confirm, NoShow, cancel window)
- **Phase D**: ProviderService MassTransit consumers (AppointmentBooked, SlotReleased)
- **Phase E**: Cross-service MassTransit retry + DLQ
- **Phase F**: NotificationService reschedule consumer
- **Phase G**: PII masking (Serilog destructuring)
- **Phase H**: CI coverage gate (80% threshold enforcement)
- **Phase I**: Documentation (booking-saga, message-versioning, risk-register, sprint-closure)
- **Phase J**: Unit tests for all new handlers and domain methods

---

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: MediatR 12, MassTransit 8 (RabbitMQ), EF Core 9, Duende IdentityServer 7, YARP, Polly 8, OpenTelemetry 1.11, Serilog
**Storage**: SQL Server 2022 (5 isolated DBs in Docker), Redis 7 (ProviderService only)
**Testing**: xUnit 2.x, NSubstitute 5.x, Bogus 35.x, TestContainers 4.x, FluentAssertions 7.x
**Target Platform**: Docker Compose (local dev), Linux container deployment
**Project Type**: Distributed microservices  4 services + 1 gateway + 1 identity server (27 C# projects)
**Performance Goals**: 200 concurrent bookings, 95% < 3s; notification within 60s (SC-004, SC-003)
**Constraints**: `<200ms p95` for booking confirmation; all tests must pass in CI
**Scale/Scope**: 4 bounded contexts, 5 databases, 55 existing unit tests, 8 test projects

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-design gates (IVII):

| Gate | Principle | Status | Notes |
|---|---|---|---|
| Domain layer has no external deps | I | PASS | New domain events are record types implementing IDomainEvent |
| CQRS for all writes | I | PASS | All 3 new commands use MediatR IRequest pattern |
| No business logic in Infrastructure | I | PASS | Consumers only orchestrate; guard is one-liner idempotency check |
| Async for cross-service state changes | II+III | PASS | ProviderService consumers receive domain events; no new sync RPCs |
| Outbox for domain event publication | III | PASS | Reschedule command uses SaveChangesAsync  OutboxPublishingInterceptor |
| Idempotent consumers | III | PASS | All new consumers have idempotency guard |
| Redis invalidation on write | III | PASS | Both ProviderService consumers call cache.RemoveAsync after mutation |
| >=80% coverage enforced | IV | ADDRESSED | Plan includes CI threshold enforcement (Gap I4) |
| Polly on all outbound clients | V | PASS | No new HTTP/gRPC clients; existing clients already have Polly |
| No PII in logs | VI | ADDRESSED | Plan includes Serilog destructuring (Gap C6) |
| Secrets via env vars | VI | PASS | No new secrets introduced |
| YAGNI check | VII | PASS | RescheduleAppointmentCommand is minimum  no saga; reuses existing gRPC client |

**No constitution violations. No complexity justification table required.**

---

## Project Structure

### Documentation (this feature)

```text
specs/001-distributed-healthcare-system/
 plan.md              <- this file
 research.md          <- Phase 0 output (all NEEDS CLARIFICATION resolved)
 data-model.md        <- Phase 1 output (entity changes + EF Core config)
 quickstart.md        <- Phase 1 output (step-by-step implementation guide)
 contracts/
    appointments-v1.1-openapi.yaml   <- 3 new endpoints documented
 tasks.md             (existing reference  gap tasks detailed in plan below)
```

### Source Code  Files Changed / Created

```text
src/Services/AppointmentService/
 AppointmentService.Domain/
    Entities/Appointment.cs             MODIFY
    Events/AppointmentEvents.cs         MODIFY
 AppointmentService.Application/
    Commands/RescheduleAppointment/     CREATE
    Commands/ConfirmAppointment/        CREATE
    Commands/MarkNoShow/               CREATE
 AppointmentService.Infrastructure/
    Persistence/Configurations/AppointmentConfiguration.cs  MODIFY
    Persistence/Migrations/<ts>_AddScheduledStartAndUniqueSlotId.cs  CREATE
 AppointmentService.API/
     Endpoints/AppointmentsEndpoints.cs   MODIFY
     Program.cs                           MODIFY

src/Services/ProviderService/
 ProviderService.Infrastructure/Messaging/Consumers/  CREATE (new folder)
    AppointmentBookedConsumer.cs    CREATE
    SlotReleasedConsumer.cs         CREATE
 ProviderService.API/Program.cs      MODIFY

src/Services/NotificationService/
 NotificationService.Application/Consumers/AppointmentRescheduledConsumer.cs  CREATE
 NotificationService.API/Program.cs                                            MODIFY

.github/workflows/ci.yml    MODIFY
docs/booking-saga.md        CREATE
docs/message-versioning.md  CREATE
docs/risk-register.md       CREATE
docs/sprint-closure.md      CREATE
docs/SPEC-COMPLIANCE-AUDIT.md  UPDATE

tests/
 AppointmentService.UnitTests/    MODIFY (new test methods)
 NotificationService.UnitTests/   MODIFY (new test method)
```

---

## Implementation Phases

### Phase A  Domain + Entity Changes

| Step | File | Change | Gap Closed |
|---|---|---|---|
| A1 | Appointment.cs | Add ScheduledStartUtc; update Book() signature | Enables C1+I3 |
| A2 | AppointmentEvents.cs | Add AppointmentRescheduledDomainEvent, AppointmentConfirmedDomainEvent, AppointmentNoShowDomainEvent | Enables C1+I1 |
| A3 | Appointment.cs | Add Reschedule(), Confirm(), MarkNoShow() | C1+I1 |
| A4 | PersistAppointmentActivity.cs | Pass slotInfo.StartTimeUtc to Book() | A1 prerequisite |

### Phase B  EF Core + Migration

| Step | File | Change | Gap Closed |
|---|---|---|---|
| B1 | AppointmentConfiguration.cs | Add IsUnique() on SlotId; map ScheduledStartUtc | C2 |
| B2 | Run dotnet ef migrations add | AddScheduledStartAndUniqueSlotId | C2 |

### Phase C  Application Commands + Endpoints

| Step | File | Change | Gap Closed |
|---|---|---|---|
| C1 | RescheduleAppointmentCommand.cs | Create command + validator + handler | C1 (FR-015) |
| C2 | ConfirmAppointmentCommand.cs | Create command + handler | I1 (FR-016) |
| C3 | MarkNoShowCommand.cs | Create command + handler | I1 (FR-016) |
| C4 | AppointmentsEndpoints.cs | Add PUT /{id}/reschedule, POST /{id}/confirm, POST /{id}/no-show | C1+I1 |
| C5 | CancelAppointmentCommand.cs | Add notice-window check using ScheduledStartUtc | I3 (FR-014) |
| C6 | appsettings.json (AppointmentService) | Add Appointment:CancellationNoticeHours: 2 | I3 |

### Phase D  ProviderService MassTransit Consumers

| Step | File | Change | Gap Closed |
|---|---|---|---|
| D1 | ProviderService.Infrastructure.csproj | Add MassTransit.RabbitMQ package | C3 prerequisite |
| D2 | ProviderService.API.csproj | Add AspNetCore.HealthChecks.Rabbitmq package | C3 prerequisite |
| D3 | AppointmentBookedConsumer.cs | Create consumer: V1_AppointmentBookedEvent -> slot.Book() + cache invalidate | C3 (FR-010) |
| D4 | SlotReleasedConsumer.cs | Create consumer: V1_SlotReleasedEvent -> slot.Release() + cache invalidate | C3 (FR-010) |
| D5 | ProviderService.API/Program.cs | Wire MassTransit + both consumers + RabbitMQ health check | C3 |

### Phase E  Cross-Service MassTransit Retry

| Step | File | Change | Gap Closed |
|---|---|---|---|
| E1 | AppointmentService.API/Program.cs | Add UseMessageRetry(Exponential 3x) | C4 (FR-020) |
| E2 | NotificationService.API/Program.cs | Same retry policy | C4 (FR-020) |
| E3 | ProviderService.API/Program.cs | Same retry policy (included with Phase D) | C4 (FR-020) |

### Phase F  NotificationService Reschedule Consumer

| Step | File | Change | Gap Closed |
|---|---|---|---|
| F1 | AppointmentRescheduledConsumer.cs | Create consumer following Booked/Cancelled pattern | C5 (FR-018) |
| F2 | NotificationService.API/Program.cs | Register new consumer | C5 |

### Phase G  PII Masking

| Step | File | Change | Gap Closed |
|---|---|---|---|
| G1 | Each service Program.cs (Patient, Appointment, Notification) | Add Serilog Destructure.ByTransforming<PatientInfo>() | C6 (FR-005, FR-032, SC-008) |

### Phase H  CI Coverage Gate

| Step | File | Change | Gap Closed |
|---|---|---|---|
| H1 | .github/workflows/ci.yml | Add ReportGenerator + 80% threshold step | I4 (FR-035, SC-009) |

### Phase I  Documentation

| Step | File | Change |
|---|---|---|
| I1 | docs/booking-saga.md | State machine diagram + activity descriptions + compensation logic |
| I2 | docs/message-versioning.md | V1 contract inventory + versioning policy + consumer mapping |
| I3 | docs/risk-register.md | 5 constitution risks + 3 new audit risks |
| I4 | docs/sprint-closure.md | Week-by-week deliverables + test counts + gap closure summary |
| I5 | docs/SPEC-COMPLIANCE-AUDIT.md | Update gap status as phases complete |

### Phase J  Tests

| Step | File | New Tests |
|---|---|---|
| J1 | AppointmentService.UnitTests | Reschedule() success + wrong-status guard; Confirm() + MarkNoShow() success + wrong-status; RescheduleCommandHandler success + slot-unavailable; cancellation notice window check |
| J2 | NotificationService.UnitTests | AppointmentRescheduledConsumer success + idempotent duplicate |

---

## Gap -> FR Cross-Reference

| Gap | FRs Closed | Phase | Priority |
|---|---|---|---|
| C1 Reschedule command | FR-015 | A+B+C | Critical |
| C2 SlotId UNIQUE constraint | FR-012 | B | Critical |
| C3 ProviderService consumers | FR-010 | D | Critical |
| C4 MassTransit retry | FR-020 | E | Critical |
| C5 Reschedule notification | FR-018 | F | Critical |
| C6 PII masking | FR-005, FR-032, SC-008 | G | Critical |
| I1 Confirm/NoShow transitions | FR-016 | A+C | Important |
| I3 Cancellation notice window | FR-014 | C | Important |
| I4 CI coverage gate | FR-035, SC-009 | H | Important |
| D1 booking-saga.md | T089 DoD | I | Docs |
| D2 message-versioning.md | T114 DoD | I | Docs |
| D3 risk-register.md | T138 DoD | I | Docs |
| D4 sprint-closure.md | T141 DoD | I | Docs |

---

## Expected Outcome

| Metric | Before | After |
|---|---|---|
| FRs fully implemented | 22/37 (59%) | 34/37 (92%) |
| FR-015 Reschedule | Missing | Implemented |
| FR-010 Slot event consumers | Missing | Implemented |
| FR-020 Retry + DLQ | Missing | Implemented |
| FR-018 Reschedule notification | Partial | Implemented |
| FR-016 Full state machine | Partial | Implemented |
| FR-014 Cancellation window | Missing | Implemented |
| SC-008 Zero PII in logs | Violated | Compliant |
| SC-009 >=80% coverage enforced | Not enforced | Enforced in CI |
| Missing docs | 4 | 0 |
| Unit tests | 55 | ~70 |
