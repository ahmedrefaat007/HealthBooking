# HealthBooking — Distributed Healthcare Appointment System

**Status**: 🔨 Under Construction (6-week sprint)  
**Stack**: .NET 9 | SQL Server | Clean Architecture | Microservices | gRPC | RabbitMQ | Redis | YARP | Duende IdentityServer  
**Branch**: `001-distributed-healthcare-system`  
**Generated**: 2026-04-01

---

## Quick Start

```bash
# 1. Clone repo + all branches
git clone <repo-url>
cd Booking.net

# 2. Copy environment template
cp .env.example .env

# 3. Start Docker Compose stack (infrastructure)
docker compose up -d

# 4. Build & run services
dotnet build
dotnet run --project src/Services/PatientService/PatientService.API
```

---

## Service Architecture

| Service | Port | Purpose |
|---|---|---|
| 🔐 IdentityServer | 5005 | OAuth2/OIDC token issuer |
| 🚪 API Gateway (YARP) | 5000 | JWT validation, rate limiting, routing |
| 👤 PatientService | 5001 | Patient registration & profiles |
| 👨‍⚕️ ProviderService | 5002 | Provider availability & slots |
| 📅 AppointmentService | 5003 | Booking engine & saga orchestration |
| 📧 NotificationService | 5004 | Email/SMS notification dispatch |

---

## Infrastructure

| Component | Port | Purpose |
|---|---|---|
| 🐘 SQL Server (4 instances) | 1433 | Per-service databases |
| 🐰 RabbitMQ | 5672 | Message broker (MassTransit) |
| 🔴 Redis | 6379 | Distributed cache |
| 🕵️ Jaeger | 16686 | Distributed tracing UI |

---

## Project Layout

```
HealthBooking.sln
├── src/
│   ├── SharedKernel/
│   │   ├── HealthBooking.SharedKernel/        # Base types, domain events, DI extensions
│   │   └── HealthBooking.Contracts/           # Versioned event contracts (V1_*), gRPC protos
│   ├── ApiGateway/
│   │   └── HealthBooking.ApiGateway/          # YARP, JWT validation, correlation ID
│   ├── IdentityServer/
│   │   └── HealthBooking.IdentityServer/      # Duende IdentityServer host
│   └── Services/
│       ├── PatientService/
│       │   ├── .Domain/
│       │   ├── .Application/
│       │   ├── .Infrastructure/
│       │   └── .API/
│       ├── ProviderService/
│       │   ├── .Domain/
│       │   ├── .Application/
│       │   ├── .Infrastructure/
│       │   └── .API/
│       ├── AppointmentService/
│       │   ├── .Domain/
│       │   ├── .Application/
│       │   ├── .Infrastructure/
│       │   └── .API/
│       └── NotificationService/
│           ├── .Domain/
│           ├── .Application/
│           ├── .Infrastructure/
│           └── .API/
├── tests/
│   ├── PatientService.UnitTests/
│   ├── PatientService.IntegrationTests/
│   ├── ... [7 more test projects]
├── docs/
│   ├── booking-saga.md                      # Saga choreography & compensation
│   ├── message-versioning.md                # Event contract evolution
│   └── risk-register.md                     # Mitigation status
├── docker-compose.yml
├── docker-compose.override.yml
├── .env.example
└── .gitignore
```

---

## Weekly Milestones

| Week | Focus | DoD |
|---|---|---|
| 1️⃣ | Scaffolding, Auth, API Gateway | All `/health/ready` green; JWT smoke test |
| 2️⃣ | PatientService + ProviderService | 80% coverage; CRUD endpoints; gRPC services |
| 3️⃣ | AppointmentService | Double-booking prevention; Outbox atomicity |
| 4️⃣ | NotificationService + Message Wiring | Idempotent delivery; Slot status consistency |
| 5️⃣ | Resilience & Caching | Circuit breaker test; Cache invalidation |
| 6️⃣ | Observability & Integration Tests | OpenTelemetry traces; E2E booking flow |

---

## Constitution

👉 See [.specify/memory/constitution.md](.specify/memory/constitution.md) for:
- 7 architectural principles
- Tech stack conventions (Clean Architecture, CQRS/MediatR, EF Core code-first, Polly)
- Testing requirements (80% coverage gate, TestContainers)
- Security (PII masking, JWT validation)
- Definition of Done per week

---

## Specification & Plan

- **Full Spec**: [specs/001-distributed-healthcare-system/spec.md](specs/001-distributed-healthcare-system/spec.md)  
- **Implementation Plan**: [specs/001-distributed-healthcare-system/plan.md](specs/001-distributed-healthcare-system/plan.md)  
- **Task Breakdown**: [specs/001-distributed-healthcare-system/tasks.md](specs/001-distributed-healthcare-system/tasks.md)

---

## Getting Help

- **Task stuck?** See [tasks.md — Agent Reference (R1–R10)](specs/001-distributed-healthcare-system/tasks.md#-agent-reference-self-contained--do-not-cross-reference-planmd)
- **Verification failing?** Run: `dotnet build; dotnet test --no-build`
- **Docker issues?** Check: `docker compose ps` → look for `(unhealthy)` containers

---

**Maintained by**: Ahmed Refaat  
**Last Updated**: 2026-04-01
