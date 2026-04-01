# Feature Specification: Distributed Healthcare Appointment System — Full System Specification

**Feature Branch**: `001-distributed-healthcare-system`  
**Created**: 2026-04-01  
**Status**: Draft  
**Constitution Version**: 1.0.0  
**Sprint Duration**: 6 weeks  
**Target Cohort**: Senior-level .NET engineering training

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Patient Self-Registration & Profile Management (Priority: P1)

A new patient visits the system, creates an account, provides their demographic information, and manages their profile. This is the entry point for all patient interactions and must work before any booking can occur.

**Why this priority**: Without patient identity and profile, no appointment can be attributed to a real person. This is the foundational capability of the entire system.

**Independent Test**: Can be fully tested by registering a patient via the API Gateway, verifying the patient record persists in the Patient Service database, and confirming the identity token grants access to patient-scoped resources — with zero dependency on other services.

**Acceptance Scenarios**:

1. **Given** an unauthenticated visitor, **When** they submit valid registration details (name, date of birth, contact email, phone number), **Then** the system creates a patient profile, issues an OIDC identity token, and returns a confirmation with the assigned patient identifier.
2. **Given** an authenticated patient, **When** they update their contact information, **Then** the system persists the changes, updates the audit trail with modification timestamp and actor, and the updated profile is retrievable within 1 second.
3. **Given** a registration attempt with a duplicate email address, **When** the request is submitted, **Then** the system rejects it with a clear error message and does not create a duplicate record.
4. **Given** an authenticated patient, **When** they request their profile, **Then** no sensitive PII appears in application logs or telemetry payloads.

---

### User Story 2 — Doctor/Provider Profile & Availability Management (Priority: P1)

A doctor or clinic administrator creates and manages a provider profile, defines specializations, and publishes recurring availability slots that patients can book against.

**Why this priority**: Without provider availability data, the scheduling engine cannot function. This must be operational before any appointment booking can be tested end-to-end.

**Independent Test**: Can be fully tested by creating a provider profile, adding availability slots, and querying the Provider Service API directly — the response must reflect accurate availability state with no double-counted or overlapping slots.

**Acceptance Scenarios**:

1. **Given** an authenticated clinic administrator, **When** they register a new doctor with specialization and credentials, **Then** the system creates the provider record and makes the profile queryable within 1 second.
2. **Given** an authenticated doctor, **When** they define a recurring weekly availability schedule (e.g., Monday–Thursday 09:00–17:00 in 30-minute slots), **Then** the system generates the individual time slots and stores them with correct status (Available).
3. **Given** an authenticated doctor, **When** they cancel or modify an existing availability window, **Then** the system marks affected slots as Unavailable and, for any already-booked slots, triggers a cancellation event to notify affected patients.
4. **Given** a query for provider availability by specialization and date range, **When** the result is served from cache, **Then** it must not be older than the configured TTL and must reflect the last confirmed write event.

---

### User Story 3 — Appointment Booking with Conflict Prevention (Priority: P1)

A patient searches for available doctors by specialization, selects a time slot, and books an appointment. The system must guarantee that no two patients can book the same doctor slot simultaneously, even under concurrent load.

**Why this priority**: This is the core value proposition of the system. Double-booking is a patient safety and trust issue; it is the single most critical correctness requirement.

**Independent Test**: Can be fully tested by submitting two simultaneous booking requests for the identical slot and verifying exactly one succeeds while the other receives a conflict rejection — measurable via integration test with two concurrent clients.

**Acceptance Scenarios**:

1. **Given** a patient has searched for available appointments and selected a slot, **When** they confirm the booking, **Then** the system reserves the slot atomically, creates an appointment record, and returns a booking confirmation with a unique reference number within 3 seconds.
2. **Given** two patients submit booking requests for the exact same doctor slot at the same moment, **When** both requests are processed, **Then** exactly one booking succeeds and the other receives a clear "slot no longer available" response — no partial or ghost bookings are created.
3. **Given** a booking is confirmed, **When** the scheduling engine updates the provider's slot status, **Then** the slot is marked as Booked in the Provider Service database via an asynchronous event, and the eventual consistency window does not exceed 5 seconds under normal load.
4. **Given** a patient books an appointment, **When** the appointment is confirmed, **Then** a domain event is emitted that triggers a notification to the patient via their preferred channel.

---

### User Story 4 — Appointment Lifecycle Management (Priority: P2)

A patient or doctor can view, reschedule, or cancel an existing appointment. The system propagates the state change to all involved services and sends appropriate notifications.

**Why this priority**: Appointment modifications are high-frequency operations in real healthcare settings. Incomplete lifecycle management would render the system unusable in practice.

**Independent Test**: Can be fully tested by booking an appointment, cancelling it, verifying the slot returns to Available in the Provider Service, and confirming the patient receives a cancellation notification — all observable in a single integration test run.

**Acceptance Scenarios**:

1. **Given** a patient has a confirmed appointment, **When** they request cancellation at least 2 hours before the scheduled time, **Then** the appointment status changes to Cancelled, the provider slot is released, and the patient receives a cancellation confirmation.
2. **Given** a doctor cancels their availability for a day, **When** there are existing booked appointments that overlap, **Then** all affected appointments are automatically cancelled, each patient receives a cancellation notification, and the slots are made available for rebooking.
3. **Given** a patient requests to reschedule, **When** they select a new available slot, **Then** the original slot is released, the new slot is reserved, and the appointment record is updated — all as a single consistent operation.
4. **Given** any appointment state transition, **When** the change is persisted, **Then** the full audit trail (who changed what, when) is automatically recorded without manual intervention in application code.

---

### User Story 5 — Real-Time Notification Delivery (Priority: P2)

The system automatically sends notifications to patients for booking confirmations, cancellations, reminders, and rescheduling events via email channel.

**Why this priority**: Notifications are essential for reducing no-show rates and building user trust, but the core booking feature works without them. They are high value but non-blocking.

**Independent Test**: Can be fully tested by triggering an appointment-booked event on the message bus and verifying the Notification Service receives it, de-duplicates it on retry, and marks it as delivered — without requiring the Appointment Service to be running.

**Acceptance Scenarios**:

1. **Given** an appointment is confirmed, **When** the booking event is published to the message bus, **Then** the Notification Service consumes the event, sends a confirmation message to the patient within 60 seconds, and records the delivery attempt.
2. **Given** the notification delivery fails on first attempt, **When** the retry mechanism activates, **Then** the system retries up to 3 times with exponential back-off and marks the notification as Failed if all retries are exhausted — without re-triggering side effects.
3. **Given** the same notification event is delivered twice by the message bus (duplicate delivery), **When** the consumer processes it, **Then** only one notification is sent to the patient — idempotency is enforced.
4. **Given** a scheduled appointment is 24 hours away, **When** the reminder window is reached, **Then** an automated reminder is sent to the patient without any manual trigger.

---

### User Story 6 — System Observability & Health Monitoring (Priority: P3)

An operations team or senior developer can observe the health, latency, and trace flows of all services through a unified dashboard, and each service exposes readiness/liveness probes for orchestration.

**Why this priority**: Observability enables diagnosis and confidence without being required for functional correctness. It is essential for Week 5–6 training goals.

**Independent Test**: Can be fully tested by starting all services via `docker compose up`, sending a sample booking flow, and verifying that a complete distributed trace appears in the tracing tooling showing spans across the API Gateway, Appointment Service, Provider Service, and Notification Service.

**Acceptance Scenarios**:

1. **Given** a request traverses multiple services, **When** the trace is inspected in the observability tooling, **Then** a single correlation ID links all spans across services, and each span shows service name, operation name, and duration.
2. **Given** any service is starting up, **When** its dependencies (database, message broker) are not yet ready, **Then** the `/health/ready` endpoint returns a non-healthy status and the orchestrator does not route traffic to it.
3. **Given** a circuit breaker opens on an outbound service call, **When** the event occurs, **Then** a structured log entry is emitted with the circuit breaker state, the affected downstream service, and the correlation ID.
4. **Given** any service request or message processing, **When** patient PII fields are present, **Then** those fields are masked or omitted in all log output and telemetry spans.

---

### Edge Cases

- What happens when the appointment service receives a booking request but the provider service is temporarily unreachable and the circuit breaker is open?
- How does the system handle a saga failure mid-flow where the appointment is created but the provider slot update message fails to publish?
- What happens when the distributed cache returns stale availability data that conflicts with the actual database state at the moment of booking?
- How does the system respond when the outbox processor crashes between inserting the outbox record and publishing the event to the broker?
- What happens when two concurrent booking requests attempt to acquire a lock on the same provider slot row simultaneously?
- How are appointment records handled if the notification service is down for an extended period and the dead-letter queue grows large?
- What occurs when a message contract version mismatch is encountered by a consumer?
- What happens if the identity provider is unreachable and a patient attempts to register during that window?

---

## Requirements *(mandatory)*

### Functional Requirements

#### Patient Management (PatientService)

- **FR-001**: System MUST allow new patients to register with full name, date of birth, contact email, and phone number; registration MUST issue identity credentials via the OIDC provider.
- **FR-002**: System MUST allow authenticated patients to view and update their demographic profile; all updates MUST be recorded in an immutable audit trail automatically via EF Core interceptors.
- **FR-003**: System MUST prevent registration with a duplicate email address and return a descriptive conflict error.
- **FR-004**: System MUST expose patient query capabilities for use by the AppointmentService via the API Gateway; direct cross-database access is prohibited.
- **FR-005**: System MUST mask or omit patient PII in all structured log output and distributed trace payloads.

#### Provider Management (ProviderService)

- **FR-006**: System MUST allow clinic administrators to create and manage doctor profiles including name, specialization(s), license number, and contact details.
- **FR-007**: System MUST allow doctors or administrators to define recurring and one-off availability windows that are automatically expanded into bookable time slots.
- **FR-008**: System MUST prevent the creation of overlapping availability slots for the same provider.
- **FR-009**: System MUST expose provider availability queries, with results served from distributed cache and invalidated on every confirmed write event — not lazily on next read.
- **FR-010**: System MUST update provider slot status (Available → Booked → Available) via asynchronous domain events published by the AppointmentService; slot state MUST eventually be consistent within 5 seconds under normal conditions.

#### Appointment Scheduling (AppointmentService)

- **FR-011**: System MUST allow authenticated patients to search for available providers by specialization and date range.
- **FR-012**: System MUST atomically reserve a provider time slot upon booking confirmation, preventing double-booking via database-level concurrency control.
- **FR-013**: System MUST publish an `AppointmentBooked` domain event via the Outbox Pattern immediately after a successful booking transaction commits; the event MUST be written to the outbox table within the same database transaction.
- **FR-014**: System MUST support appointment cancellation by the patient (subject to a configurable minimum notice window) and by the provider (with automatic cascade notification to affected patients).
- **FR-015**: System MUST support appointment rescheduling as an atomic cancel-and-rebook operation that either fully succeeds or fully rolls back.
- **FR-016**: System MUST maintain a full state machine for appointment lifecycle: Pending → Confirmed → Cancelled | Completed | NoShow.
- **FR-017**: System MUST document the booking saga as a choreography-based event chain; every saga step MUST be identifiable by a shared saga/correlation ID present in all events and log entries.

#### Notification Delivery (NotificationService)

- **FR-018**: System MUST consume appointment domain events from the message bus and deliver confirmation, cancellation, reschedule, and 24-hour-reminder notifications to patients.
- **FR-019**: System MUST enforce consumer idempotency; duplicate event delivery MUST NOT result in duplicate notifications being sent.
- **FR-020**: System MUST retry failed notification delivery up to 3 times with exponential back-off before routing the message to a dead-letter queue.
- **FR-021**: System MUST record a delivery attempt log entry (status, retry count, timestamp) for every notification processed.

#### API Gateway & Cross-Cutting

- **FR-022**: System MUST route all external client requests through the API Gateway; services MUST NOT be directly reachable from outside the internal network.
- **FR-023**: System MUST validate JWT bearer tokens at the API Gateway boundary and at each individual service boundary before processing any request.
- **FR-024**: System MUST enforce rate limiting at the API Gateway level to protect downstream services from traffic spikes.
- **FR-025**: System MUST propagate a `X-Correlation-Id` header through all synchronous service-to-service calls and embed it in every structured log entry and message envelope.
- **FR-026**: Every cross-service state-changing operation MUST use asynchronous messaging; synchronous calls are reserved for read/query operations only.
- **FR-027**: All message contracts published to the broker MUST be versioned from day one (e.g., `V1_AppointmentBookedEvent`) and MUST be backward compatible for at least one major version.

#### Infrastructure & Operational

- **FR-028**: System MUST be launchable with a single `docker compose up` command from the repository root, bringing all services, databases, broker, cache, and observability tooling to a ready state.
- **FR-029**: Every service MUST expose `/health/live` and `/health/ready` endpoints reflecting the health of its critical dependencies (database, message broker, cache).
- **FR-030**: Every outbound HTTP/gRPC call MUST be wrapped in a resilience pipeline: retry (max 3, exponential back-off with jitter), circuit breaker (5 failures → 30-second open window), and 10-second timeout.
- **FR-031**: Every service MUST emit distributed traces for incoming requests, outbound calls, database commands, and message broker publish/consume operations, exported to the configured tracing backend.
- **FR-032**: Structured logging MUST use JSON format in production; every log entry MUST include the correlation ID, service name, and timestamp; no secrets or PII MUST appear in any log entry.

#### Testing

- **FR-033**: Every MediatR Command/Query handler MUST have at minimum one success test and one failure/validation test, using only programmatically generated test data (no hand-crafted magic strings).
- **FR-034**: Each service MUST have an integration test suite using containerized dependencies covering: repository persistence, audit interceptor field population, outbox publishing, and API endpoint contract validation.
- **FR-035**: Domain and Application layer unit test coverage MUST meet or exceed 80% line coverage, enforced in CI on every pull request.
- **FR-036**: Integration tests MUST NOT share state between test classes; each test class uses its own isolated fixture.
- **FR-037**: Mocking in unit tests MUST use a single consistent mocking library across all services; inconsistent mocking approaches are not permitted.

---

### Key Entities

- **Patient**: A registered healthcare consumer. Key attributes: patient identifier, full name, date of birth, contact email, phone number, registration date. Owns their appointment history; PII fields are protected from logging.
- **Provider (Doctor)**: A licensed healthcare professional. Key attributes: provider identifier, full name, specialization(s), license number, contact email. Owns their availability schedule and is referenced by appointments.
- **AvailabilitySlot**: A discrete bookable time unit belonging to a Provider. Key attributes: slot identifier, provider reference, start time (UTC), end time (UTC), duration (minutes, fixed at 30), status (Available / Booked / Blocked), concurrency version token.
- **Appointment**: The binding record between a Patient and a Provider Slot. Key attributes: appointment identifier, patient reference, provider reference, slot reference, scheduled start/end time (UTC), status (Pending / Confirmed / Cancelled / Completed / NoShow), cancellation reason, saga correlation ID, full audit fields (created/modified with actor).
- **OutboxMessage**: An internal reliability record for guaranteed event publication. Key attributes: message identifier, event type and version, serialized payload, destination exchange/topic, created timestamp, published timestamp, retry count, status (Pending / Published / Failed).
- **NotificationRecord**: A delivery audit log for each notification attempt. Key attributes: record identifier, appointment or event reference, recipient type (patient), channel (email), template type, send status, retry count, last attempted timestamp.
- **DomainEvent (versioned contracts)**: Cross-service event messages carried over the message bus. Examples: `V1_AppointmentBookedEvent`, `V1_AppointmentCancelledEvent`, `V1_SlotReleasedEvent`, `V1_NotificationRequestedEvent`. Every event envelope carries: event type, schema version, correlation ID, source service, event timestamp, and payload.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A patient can complete the full booking journey — registration through provider search, slot selection, and confirmation — in under 3 minutes from a cold start in the test environment.
- **SC-002**: The system correctly rejects 100% of concurrent double-booking attempts; zero ghost or duplicate appointments exist in the database after any concurrent load test scenario.
- **SC-003**: Appointment domain events are consumed and patient notifications are dispatched within 60 seconds of a booking confirmation under normal operating conditions.
- **SC-004**: The system sustains 200 concurrent appointment booking requests without service degradation, with 95% of successful responses completing within 3 seconds.
- **SC-005**: All six weekly milestones produce independently deployable and demonstrable increments; no milestone requires a future milestone's deliverables to be demonstrable.
- **SC-006**: A complete distributed trace for any end-to-end booking flow is visible in the tracing tooling within 5 seconds of the request completing, with spans for every participating service.
- **SC-007**: The entire local development environment reaches a fully ready state (all health probes passing) within 3 minutes of running the single-command startup procedure.
- **SC-008**: Zero patient PII fields appear in plain text in any log file, distributed trace, or telemetry payload across all execution paths covered by the integration test suite.
- **SC-009**: Domain and Application layer test coverage meets or exceeds 80% lines covered across all four microservices, verified by the CI pipeline on every pull request.
- **SC-010**: All six weeks' Definition of Done checklists are fully satisfied at milestone review time, with zero deferred constitution compliance issues carried forward between milestones.

---

## Weekly Milestone Delivery Requirements

### Week 1 — Foundation: Scaffolding, Identity & Gateway

**Deliverable**: A running environment with all four service skeletons, the API Gateway, the Identity Provider, and shared infrastructure (databases, message broker, cache, tracing) all healthy.

**Definition of Done**:
- [ ] Solution structure follows Clean Architecture four-layer layout for all four services.
- [ ] `docker compose up` brings the full stack to a ready state within 3 minutes.
- [ ] API Gateway routes are defined and validate JWT tokens from the Identity Provider.
- [ ] Shared Kernel library contains base entity types, domain event interfaces, outbox message schema, and versioned event contract base.
- [ ] Code style enforcement (EditorConfig + formatter) configured and passing in CI.
- [ ] Auth smoke test confirms token issuance and gateway validation end-to-end.
- [ ] Environment variable template committed; actual secrets file gitignored; no secrets hardcoded anywhere.

---

### Week 2 — Core Domain: Patient & Provider Services

**Deliverable**: Fully functional Patient and Provider services with CRUD operations, domain logic, unit tests, and integration tests passing.

**Definition of Done**:
- [ ] PatientService: registration, profile read/update implemented as CQRS commands and queries.
- [ ] ProviderService: provider profile management and availability slot generation fully functional.
- [ ] Audit fields (`CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy`) populated automatically on all entities; no manual assignment in handlers.
- [ ] Unit tests cover all command/query handlers with programmatically generated test data; ≥80% line coverage on Domain and Application layers.
- [ ] Integration tests using containerized dependencies cover persistence round-trips, audit field population, and API endpoint contracts.
- [ ] No test class shares state with another (isolated fixtures per class).

---

### Week 3 — Scheduling Engine: Booking, Concurrency & Outbox

**Deliverable**: AppointmentService with complete booking, cancellation, rescheduling, and double-booking prevention; Outbox Pattern publishing domain events.

**Definition of Done**:
- [ ] Booking endpoint atomically reserves a slot using database-level concurrency control; double-booking verified impossible by concurrent integration test (two simultaneous requests, exactly one succeeds).
- [ ] Appointment state machine implemented with all transitions (Pending → Confirmed → Cancelled | Completed | NoShow).
- [ ] Outbox Pattern implemented: domain events written to outbox table within the booking transaction; outbox processor publishes to message broker independently.
- [ ] Booking saga documented: a sequence diagram or textual flow description covering all event steps and compensation/rollback paths.
- [ ] Unit and integration tests pass for booking, cancellation, rescheduling, and outbox publishing.

---

### Week 4 — Integration: Message Broker & Eventual Consistency

**Deliverable**: All four services connected via message broker events; Notification Service consuming and delivering notifications; Provider slot status updated via events.

**Definition of Done**:
- [ ] Notification Service consumes `V1_AppointmentBookedEvent` and `V1_AppointmentCancelledEvent` and delivers notifications; idempotency verified by duplicate-delivery test.
- [ ] Provider Service consumes `V1_AppointmentBookedEvent` and updates slot status to Booked.
- [ ] Dead-letter queue configured; messages failing after 3 retries routed automatically.
- [ ] Message contracts versioned (`V1_*`); backward compatibility approach documented.
- [ ] Integration tests verify end-to-end event flow with containerized message broker.
- [ ] Distributed cache invalidated on slot status write events; no stale availability served beyond configured TTL.

---

### Week 5 — Resilience & Caching: Circuit Breakers, Gateway, Redis

**Deliverable**: All outbound calls wrapped in resilience pipelines; API Gateway enforcing rate limiting and auth; distributed caching operational; circuit breaker behavior verified.

**Definition of Done**:
- [ ] Every registered HTTP/gRPC client has a resilience pipeline: retry (exponential back-off with jitter), circuit breaker (5 failures → 30-second open), 10-second timeout.
- [ ] Circuit breaker state changes (open/close/half-open) emit structured log entries with correlation ID.
- [ ] API Gateway fully configured: route definitions, JWT validation, rate limiting.
- [ ] Distributed cache in use for provider availability; invalidation on write events verified by test.
- [ ] `/health/live` and `/health/ready` endpoints functional on all services; container orchestration health checks wired.
- [ ] Resilience behaviors covered by integration tests (simulated downstream outage, circuit opens and prevents cascading calls).

---

### Week 6 — Observability, Testing & Closure

**Deliverable**: Full distributed tracing across all services; complete integration test suite passing in CI; risk register reviewed; project documentation finalized.

**Definition of Done**:
- [ ] Distributed tracing configured on all services: HTTP/gRPC spans, database command instrumentation, message broker publish/consume spans, exporting to the tracing backend.
- [ ] Correlation IDs propagated across all synchronous calls and present in all log entries and message envelopes.
- [ ] Full end-to-end trace for a complete booking flow visible in the tracing tooling.
- [ ] Correlation ID propagation verified end-to-end by integration test assertion.
- [ ] All service integration test suites passing with containerized dependencies; CI pipeline green on `main`.
- [ ] Risk Register reviewed and updated; all High-impact risks mitigated or formally accepted with documented rationale.
- [ ] Docker Compose structure finalized; full-stack startup verified in a clean environment.
- [ ] Project README with local setup instructions, architecture overview, service port map, and constitution reference committed.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation Strategy |
|---|---|---|---|
| **Distributed transaction inconsistency** (booking created but slot not updated) | Medium | High | Outbox Pattern mandatory for all cross-service writes; saga correlation IDs enable compensating transaction tracing; verified by Week 3 integration tests. |
| **Double-booking under concurrent load** | Medium | High | Database-level concurrency control on AvailabilitySlot; verified by concurrent integration test (two simultaneous requests, exactly one succeeds) before Week 3 milestone is closed. |
| **Clock drift across services** affecting UTC appointment comparisons | Low | High | All datetimes stored and compared in UTC only; NTP sync enforced in Docker Compose; no local-time assumptions permitted in domain logic. |
| **Message schema drift across services** | Medium | High | Versioned contracts (`V1_*`) from day one; breaking schema changes require a new version; backward compatibility documented; consumer contract tests in Week 4. |
| **Stale cache serving incorrect availability** | Medium | Medium | Cache invalidated on every write event (not lazily); short TTL (60 seconds) as safety net; cache-aside pattern strictly enforced; verified by integration test. |
| **Circuit breaker masking cascading failures silently** | Low | Medium | Circuit breaker state changes logged with full context and correlation ID; health check `/health/ready` reflects downstream health; Polly metrics exported via distributed tracing. |
| **Outbox processor lag under high write volume** | Low | Medium | Outbox processor uses configurable polling interval; queue depth Observable via message broker management UI; dead-letter alerting configured. |
| **TestContainers startup latency in CI** | Medium | Low | Parallel test collection enabled; `IClassFixture` reuses containers within a suite; tests never share state across classes; containers started once per suite. |
| **Over-engineering beyond training scope** | High | Medium | All architectural additions require justification in PR description; weekly milestone review includes YAGNI compliance check; complexity without justification is a blocking review comment. |
| **Auth token misconfiguration between Gateway and services** | Low | High | Auth smoke test in integration suite from Week 1; OIDC discovery endpoint used exclusively (no hardcoded JWKS); validated in every CI run. |
| **PII leaking into logs or telemetry** | Low | High | Logging framework destructuring policies mask PII fields globally at configuration level; tracing attribute filtering applied at instrumentation; verified by integration test assertions on captured log output. |

---

## Docker Compose Structure

The following service topology defines the local development environment. All services MUST be defined in a single `docker-compose.yml` at the repository root.

```
docker-compose.yml              ← Root compose file (all services and infrastructure)
docker-compose.override.yml     ← Local dev overrides (volume mounts, debug ports)
.env.example                    ← Template for all required environment variables (committed)
.env                            ← Populated by developer locally (gitignored)

Infrastructure containers:
  sqlserver-patient             SQL Server 2022 for PatientService (isolated database)
  sqlserver-provider            SQL Server 2022 for ProviderService (isolated database)
  sqlserver-appointment         SQL Server 2022 for AppointmentService (isolated database)
  sqlserver-notification        SQL Server 2022 for NotificationService (isolated database)
  rabbitmq                      RabbitMQ 3 with management plugin
  redis                         Redis 7 Alpine
  identity-server               Duende IdentityServer (local dev mode)
  jaeger                        Jaeger all-in-one (trace UI + OTLP collector)

Application containers:
  api-gateway                   YARP ApiGateway (external entry point, port 5000)
  patient-service               PatientService (port 5001)
  provider-service              ProviderService (port 5002)
  appointment-service           AppointmentService (port 5003)
  notification-service          NotificationService (port 5004)
```

**Startup contract**: Every application container defines `depends_on` with `condition: service_healthy` for all its critical infrastructure dependencies. The API Gateway becomes the last service to reach healthy state. `docker compose up` with no arguments MUST bring the entire stack to a ready state.

---

## Assumptions

- The system targets local and staging environments for the 6-week sprint; production cloud deployment is out of scope but architectural decisions must not prevent it.
- The Identity Provider is pre-configured with client credentials and test user seeds for local development; external OAuth provider integration (e.g., Google, Microsoft) is out of scope.
- Email and SMS notification delivery uses a stub or local mail transport during the sprint; real SMTP/SMS provider integration is out of scope, but the consumer architecture must be production-ready for drop-in replacement.
- No browser-based or mobile front-end is in scope; deliverables are documented API endpoints accessible via the API Gateway.
- Appointment slot duration is fixed at 30 minutes; variable-duration appointments are a future extension and out of scope.
- A patient may have multiple appointments; a provider slot may be booked by at most one patient at a time.
- The training cohort operates Docker Desktop and Git on local machines; no shared cloud infrastructure is assumed.
- Performance and concurrency targets are validated by integration test scenarios, not dedicated load-testing tooling.
- HIPAA compliance is out of scope, but PII-handling practices deliberately align with HIPAA-adjacent patterns as mandated by the constitution's Security Baseline.
- Provider notifications (e.g., booking alerts to the doctor) are out of scope for the initial 6-week sprint; only patient-facing notifications are required.
