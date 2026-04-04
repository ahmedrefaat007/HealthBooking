# Risk Register

**Project**: Distributed Healthcare Booking System  
**Version**: 1.1 | **Updated**: 2026-04-04  
**Branch**: `001-w6-observability`  
**Owner**: Engineering Team

---

## Legend

| Likelihood | Impact | Risk Score |
|---|---|---|
| 1 = Rare | 1 = Negligible | 1–4 = Low |
| 2 = Unlikely | 2 = Minor | 5–9 = Medium |
| 3 = Possible | 3 = Moderate | 10–16 = High |
| 4 = Likely | 4 = Severe | 17–25 = Critical |
| 5 = Almost Certain | 5 = Catastrophic | |

Score = Likelihood × Impact

---

## Current Risk Register

### RISK-001 — Double-Booking (Concurrent Slot Contention)

| Field | Value |
|---|---|
| **ID** | RISK-001 |
| **Category** | Data Integrity |
| **Description** | Two concurrent booking requests for the same slot both pass in-memory validation and attempt to insert Appointment rows, creating a duplicate booking for the same `SlotId`. |
| **Likelihood** | 3 (Possible) |
| **Impact** | 5 (Catastrophic — patient and provider conflicts, trust loss) |
| **Score** | **15 (High)** |
| **Current Mitigation** | AvailabilitySlot uses EF Core RowVersion optimistic concurrency in ProviderService. LockSlot gRPC returns failure if slot is already locked. |
| **Residual Gap** | `AppointmentConfiguration.cs` does NOT have `IsUnique()` on `SlotId` column. If LockSlot race condition is exploited or gRPC partially fails, two Appointment rows with the same SlotId can exist. |
| **Planned Fix** | Add `HasIndex(a => a.SlotId).IsUnique()` in AppointmentConfiguration (Phase B of plan.md). |
| **Status** | 🔴 Open — gap-remediation sprint Phase B |
| **Target** | Sprint 7 |

---

### RISK-002 — Message Loss Before Retry Policy Added

| Field | Value |
|---|---|
| **ID** | RISK-002 |
| **Category** | Reliability |
| **Description** | MassTransit consumers (NotificationService, ProviderService) currently have NO `UseMessageRetry` configuration. A transient failure during consumer processing (e.g., SMTP timeout, gRPC timeout) will cause the message to be immediately moved to the `_error` queue without any retry attempts. |
| **Likelihood** | 4 (Likely — transient failures are expected in distributed systems) |
| **Impact** | 3 (Moderate — notification not sent or slot not released) |
| **Score** | **12 (High)** |
| **Current Mitigation** | None |
| **Residual Gap** | No `UseMessageRetry` in AppointmentService, NotificationService, or ProviderService consumers. |
| **Planned Fix** | Add Exponential retry (5 attempts, 1s–30s) in all consumer configurations (Phase E of plan.md). |
| **Status** | 🔴 Open — gap-remediation sprint Phase E |
| **Target** | Sprint 7 |

---

### RISK-003 — Slot Permanently Locked (ProviderService Never Receives SlotReleasedEvent)

| Field | Value |
|---|---|
| **ID** | RISK-003 |
| **Category** | Data Integrity / Availability |
| **Description** | When an Appointment is cancelled or rescheduled, `V1_SlotReleasedEvent` is published via Outbox → MassTransit. If ProviderService has no MassTransit consumer for this event (current state), the slot stays in `Locked` or `Booked` status indefinitely, blocking future bookings for that slot. |
| **Likelihood** | 5 (Almost Certain — ProviderService has zero MassTransit config today) |
| **Impact** | 4 (Severe — slot permanently unavailable, revenue loss) |
| **Score** | **20 (Critical)** |
| **Current Mitigation** | None — release is manual or absent |
| **Residual Gap** | ProviderService has no MassTransit NuGet, no AddMassTransit(), no Messaging consumers. |
| **Planned Fix** | Add MassTransit.RabbitMQ to ProviderService + wiring + SlotReleasedConsumer + AppointmentBookedConsumer (Phase F of plan.md). |
| **Status** | 🔴 Open — gap-remediation sprint Phase F |
| **Target** | Sprint 7 |

---

### RISK-004 — Reschedule Leaves Old Slot Permanently Locked

| Field | Value |
|---|---|
| **ID** | RISK-004 |
| **Category** | Data Integrity |
| **Description** | `RescheduleAppointmentCommand` does not yet exist. Without it, a patient cannot reschedule. If a workaround is attempted (cancel + re-book manually), the old slot release depends on RISK-003 being fixed. Until both are fixed, reschedule leaves the old slot permanently locked/booked. |
| **Likelihood** | 5 (Almost Certain — no reschedule endpoint; no ProviderService consumer) |
| **Impact** | 3 (Moderate — specific to reschedule scenario) |
| **Score** | **15 (High)** |
| **Current Mitigation** | None |
| **Planned Fix** | Implement `RescheduleAppointmentCommand` (Phase A + C + D of plan.md) AND ProviderService consumers (RISK-003 fix). |
| **Status** | 🔴 Open — gap-remediation sprint Phase A/C/D/F |
| **Target** | Sprint 7 |

---

### RISK-005 — CI Coverage Gate Not Enforced

| Field | Value |
|---|---|
| **ID** | RISK-005 |
| **Category** | Quality |
| **Description** | GitHub Actions CI collects test coverage metrics but does NOT fail the build if coverage falls below 80% (constitution §IV target). Developers can merge code that regresses coverage without any pipeline gate. |
| **Likelihood** | 3 (Possible) |
| **Impact** | 2 (Minor — code quality drift, not runtime failure) |
| **Score** | **6 (Medium)** |
| **Current Mitigation** | Manual PR review (not automated). |
| **Planned Fix** | Add `--threshold 80` flag or `coverlet.threshold` MSBuild property to CI coverage step. |
| **Status** | 🟡 Open — Phase I of plan.md |
| **Target** | Sprint 7 |

---

### RISK-006 — Appointment Confirm / No-Show Endpoints Missing

| Field | Value |
|---|---|
| **ID** | RISK-006 |
| **Category** | Feature Completeness |
| **Description** | FR-011 (provider search) and FR-015 (reschedule) are not the only gaps. `ConfirmAppointmentCommand` and `MarkNoShowCommand` are required by spec (task T073) but no endpoints or commands exist. Without these commands, appointment lifecycle transitions (Booked → Confirmed, Booked → NoShow) cannot be executed. |
| **Likelihood** | 5 (Certain — endpoints verified absent) |
| **Impact** | 2 (Minor for current sprint; spec compliance risk) |
| **Score** | **10 (High)** |
| **Current Mitigation** | None |
| **Planned Fix** | Add `ConfirmAppointmentCommand` and `MarkNoShowCommand` with handler + endpoint (Phase C of plan.md). |
| **Status** | 🟡 Open — gap-remediation sprint Phase C |
| **Target** | Sprint 7 |

---

### RISK-007 — Notification Gap for Reschedule

| Field | Value |
|---|---|
| **ID** | RISK-007 |
| **Category** | Feature Completeness |
| **Description** | NotificationService has consumers for `V1_AppointmentBookedEvent` and `V1_AppointmentCancelledEvent` but has NO consumer for `V1_AppointmentRescheduledEvent`. When a reschedule occurs, both the patient and provider should receive a notification, but they will not until this consumer is added. |
| **Likelihood** | 5 (Certain — consumer verified absent) |
| **Impact** | 2 (Minor — degraded UX, no safety impact) |
| **Score** | **10 (High)** |
| **Current Mitigation** | None |
| **Planned Fix** | Add `AppointmentRescheduledConsumer` to NotificationService following direct consumer pattern (Phase G of plan.md). |
| **Status** | 🟡 Open — gap-remediation sprint Phase G |
| **Target** | Sprint 7 |

---

### RISK-008 — Provider Search Not Implemented (FR-011)

| Field | Value |
|---|---|
| **ID** | RISK-008 |
| **Category** | Feature Completeness |
| **Description** | FR-011 (Search Providers by specialty, location, availability) is not implemented in this sprint. The spec requires a public search capability but it is the largest remaining gap and not addressed in the gap-remediation plan. |
| **Likelihood** | 5 (Certain — no implementation) |
| **Impact** | 3 (Moderate — core discovery feature missing) |
| **Score** | **15 (High)** |
| **Current Mitigation** | None in current sprint |
| **Planned Fix** | Deferred to Sprint 8 (out of scope for gap-remediation sprint). Requires: full-text search or Elasticsearch integration, YARP route, ProviderService query handler. |
| **Status** | 🔵 Deferred — Sprint 8 |
| **Target** | Sprint 8 |

---

## Risk Summary

| ID | Description | Score | Severity | Status |
|---|---|---|---|---|
| RISK-003 | Slot permanently locked (no ProviderService consumers) | 20 | Critical | 🔴 Sprint 7 |
| RISK-002 | Message loss (no retry policy) | 12 | High | 🔴 Sprint 7 |
| RISK-001 | Double-booking (no UNIQUE on SlotId) | 15 | High | 🔴 Sprint 7 |
| RISK-004 | Reschedule leaves old slot locked | 15 | High | 🔴 Sprint 7 |
| RISK-006 | Confirm/NoShow endpoints missing | 10 | High | 🟡 Sprint 7 |
| RISK-007 | Reschedule notification missing | 10 | High | 🟡 Sprint 7 |
| RISK-008 | Provider search not implemented | 15 | High | 🔵 Sprint 8 |
| RISK-005 | CI coverage gate not enforced | 6 | Medium | 🟡 Sprint 7 |

**Total open + planned**: 7 risks (Sprint 7) | 1 risk (deferred Sprint 8)

---

## Closed Risks

| ID | Description | Resolution | Closed Date |
|---|---|---|---|
| — | gRPC concurrency on LockSlot | EF Core RowVersion on AvailabilitySlot + StatusCode.Aborted on conflict | Week 4 |
| — | Saga state loss on restart | MS SQL saga persistence via EF Core + `BookingStates` table | Week 3 |
| — | Missing distributed tracing | OpenTelemetry 1.11 → Jaeger → all services | Week 6 |
| — | Missing structured logging | Serilog + Seq in all services | Week 5 |
| — | Health check gaps | `/health` + `/health/ready` + `/health/live` in all services | Week 5 |
