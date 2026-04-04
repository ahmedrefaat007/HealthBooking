# Week 4 — API Gateway & Integration (T093–T120)

Branch: `001-w4-integration`  
Merged from: `001-w3-scheduling`

---

## Overview

Week 4 adds the final integration layer: a YARP-based API gateway sits in front of all three domain services, a MassTransit consumer service handles email notifications asynchronously, all services are containerised with multi-stage Dockerfiles, and the full stack is wired together via docker-compose.

---

## 1. API Gateway (`src/ApiGateway/HealthBooking.ApiGateway`)

### Technology choices
| Concern | Choice | Reason |
|---|---|---|
| Reverse proxy | YARP 2.x | Native ASP.NET Core, hot-reload config |
| Auth guard | JWT Bearer | Validates token once at the edge; downstream services trust the forwarded header |
| Rate limiting | Sliding window | Prevents burst traffic; 300 req/min / 6 segments |
| Correlation | Custom middleware | Propagates `X-Correlation-Id` so logs tie together across services |
| Observability | Serilog + enriched with `CorrelationId` | Structured JSON logs ready for Seq/Elastic |

### Route table (`appsettings.json`)
| Route | Requires auth | Upstream cluster |
|---|---|---|
| `POST /api/patients/register` | No | `patient-cluster` → `:5001` |
| `/api/patients/**` | JWT | `patient-cluster` → `:5001` |
| `/api/providers/**` | JWT | `provider-cluster` → `:5002` |
| `/api/appointments/**` | JWT | `appointment-cluster` → `:5003` |

JWT audience: `healthbooking-api`  
JWT authority: read from `IdentityServer:Authority` env var (override in docker-compose).

### Active health checks
All clusters are configured with active health checks every 10 s against `/health/ready`. Unhealthy destinations are removed from rotation automatically.

---

## 2. NotificationService (`src/Services/NotificationService`)

### Architecture
NotificationService is a **pure consumer service** — it has no REST endpoints, no JWT middleware, and no MediatR pipeline. It subscribes to domain events via MassTransit/RabbitMQ and sends emails.

```
RabbitMQ → AppointmentBookedConsumer  ──▶ IEmailService (stub)
         → AppointmentCancelledConsumer   ──▶ INotificationLogRepository
                                              ──▶ NotificationDbContext (SQL Server)
```

### Idempotency
Each event is deduplicated by a unique index on `(CorrelationId, EventType)` in the `NotificationLogs` table.  
The consumer performs a pre-check (`ExistsByCorrelationAndTypeAsync`) before sending; any duplicate delivery is silently discarded.

### Events consumed
| Event | Source | Action |
|---|---|---|
| `V1_AppointmentBookedEvent` | AppointmentService | Sends booking confirmation email |
| `V1_AppointmentCancelledEvent` | AppointmentService | Sends cancellation notification email |

### Email service
`LoggingEmailService` is a development stub that logs email content via `ILogger`. Full SMTP / SendGrid integration is planned for Week 6.

### `NotificationLog` entity
| Property | Notes |
|---|---|
| `CorrelationId` | `AppointmentId` from the event |
| `EventType` | Fully qualified event class name |
| `Status` | `Pending → Sent` or `Pending → Failed` |
| `FailureReason` | Exception message if send fails |
| `RetryCount` | Incremented on each `MarkFailed()` call |

### Database
- Server: `sqlserver-notification:1433` in Docker; `localhost,1436` locally
- Database: `HealthBooking_Notification`
- Migration: `InitialCreate` — auto-applied on startup via `db.Database.MigrateAsync()`

---

## 3. Dockerfiles

All Dockerfiles follow the same **multi-stage** pattern:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish "path/to/Project.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Project.dll"]
```

Build context is the **repository root** (`.`) in all cases, matching the `docker-compose.override.yml` configuration.

| Service | Dockerfile |
|---|---|
| PatientService | `src/Services/PatientService/PatientService.API/Dockerfile` |
| ProviderService | `src/Services/ProviderService/ProviderService.API/Dockerfile` |
| AppointmentService | `src/Services/AppointmentService/AppointmentService.API/Dockerfile` |
| NotificationService | `src/Services/NotificationService/NotificationService.API/Dockerfile` |
| ApiGateway | `src/ApiGateway/HealthBooking.ApiGateway/Dockerfile` |
| IdentityServer | `src/IdentityServer/HealthBooking.IdentityServer/Dockerfile` |

---

## 4. docker-compose

### Infrastructure (`docker-compose.yml`)
| Container | Image | Port |
|---|---|---|
| `sqlserver-identity` | `mssql/server:2022-latest` | 1433 |
| `sqlserver-patient` | `mssql/server:2022-latest` | 1434 |
| `sqlserver-provider` | `mssql/server:2022-latest` | 1435 |
| `sqlserver-appointment` | `mssql/server:2022-latest` | 1436 (wait — notification on 1436 locally; appointment on 1435 locally) |
| `sqlserver-notification` | `mssql/server:2022-latest` | 1437 |
| `rabbitmq` | `rabbitmq:3-management` | 5672 / 15672 |
| `redis` | `redis:7-alpine` | 6379 |
| `jaeger` | `jaegertracing/all-in-one` | 16686 / 4317 |

### Services (`docker-compose.override.yml`)
All six services are fully defined with `build`, `environment`, `ports`, `depends_on`, and `healthcheck` blocks. All health checks poll `/health/ready`.

---

## 5. Tests

### Unit tests (`tests/NotificationService.UnitTests`)
`AppointmentBookedConsumerTests` — 3 scenarios:

| Test | Verifies |
|---|---|
| `Consume_NewEvent_SendsEmailAndPersistsSentLog` | Happy path — email sent, `NotificationLog` saved as `Sent` |
| `Consume_DuplicateEvent_SkipsEmailAndPersist` | Idempotency — duplicate event: no email, no persist |
| `Consume_EmailThrows_PersistsFailedLogAndDoesNotRethrow` | Fault tolerance — email failure: log saved as `Failed`, no exception re-throw |

Dependencies mocked with **NSubstitute 5**.

### Integration tests (`tests/NotificationService.IntegrationTests`)
`NotificationPersistenceTests` — 3 scenarios using **Testcontainers.MsSql** (SQL Server 2022):

| Test | Verifies |
|---|---|
| `AddAsync_CanPersistAndReload_NotificationLog` | Round-trip persist + reload |
| `UniqueIndex_PreventsDuplicateCorrelationAndEventType` | DB-level idempotency constraint |
| `ExistsByCorrelationAndTypeAsync_ReturnsTrueWhenRecordExists` | Repository query correctness |

---

## 6. Running the full stack

```bash
# Copy and configure the env file
cp .env.example .env   # set SQL_SA_PASSWORD, RABBITMQ_USER/PASS, REDIS_PASSWORD, JWT_SECRET

# Start all services
docker compose up -d --build

# Verify
curl http://localhost:5000/health/live     # ApiGateway
curl http://localhost:5001/health/ready   # PatientService
curl http://localhost:5004/health/ready   # NotificationService
```

RabbitMQ management UI: http://localhost:15672  
Jaeger UI: http://localhost:16686

---

## 7. Week 5 Preview — gRPC & Patient Lookup

- NotificationService will resolve real patient email via gRPC call to PatientService (replacing the placeholder `patient-{id}@placeholder.local` email)
- AppointmentService Saga — replace optimistic-lock slot booking with a full MassTransit saga for distributed consistency
- Provider slot caching improvements (Redis `WATCH` / Lua scripts)
