# 001-Distributed-Healthcare-System: Complete Technical Documentation

**Last Updated**: April 2, 2026  
**Branch**: `001-distributed-healthcare-system`  
**Status**: Week 1 Foundation Complete (T001-T012)  
**Stack**: .NET 9 | C# 12 | Clean Architecture | CQRS | gRPC | RabbitMQ | EF Core | SQL Server

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Project Structure](#project-structure)
3. [Core Components](#core-components)
   - [SharedKernel](#sharedkernel)
   - [Contracts](#contracts)
   - [Service Layer Pattern](#service-layer-pattern)
4. [Week 1 Implementation Details](#week-1-implementation-details)
5. [Design Patterns & Best Practices](#design-patterns--best-practices)
6. [How to Use & Extend](#how-to-use--extend)
7. [Build & Deployment](#build--deployment)

---

## Architecture Overview

This is a **Distributed Healthcare Appointment System** built with a **microservices-based Clean Architecture**. The system is designed for scalability, maintainability, and resilience.

### Core Principles

| Principle | Implementation | Why It's Best Practice |
|---|---|---|
| **Single Responsibility** | Each service owns one business domain | Reduces coupling, enables independent scaling |
| **Clean Architecture** | Layered per service: Domain → Application → Infrastructure → API | Testability, dependency injection, framework independence |
| **Event-Driven** | Async messaging via RabbitMQ + Outbox Pattern | Loose coupling, eventual consistency, resilience |
| **CQRS** | Command/Query separation via MediatR | Read/write optimization, scalability, clear intent |
| **Domain Events** | Domain-driven design with aggregate roots | Business logic in domain, not infrastructure |
| **Code-First Migrations** | EF Core Fluent API + MigrateAsync on startup | Type-safe, version controlled, CI-friendly |
| **Resilience** | Polly circuit breakers, timeout, retry logic | Fault tolerance, graceful degradation, distributed system reliability |

### System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                         API Gateway (YARP)                          │
│              (JWT validation, rate limiting, routing)               │
└───┬────────────┬────────────┬────────────┬───────────────────────────┘
    │            │            │            │
    ▼            ▼            ▼            ▼
┌─────────┐ ┌─────────┐ ┌─────────────┐ ┌──────────────┐
│ Patient │ │Provider │ │Appointment  │ │Notification │
│Service  │ │Service  │ │  Service    │ │   Service    │
│ (5001)  │ │ (5002)  │ │   (5003)    │ │   (5004)     │
└────┬────┘ └────┬────┘ └──────┬──────┘ └──────┬───────┘
     │           │             │               │
     └───────────┼─────────────┼───────────────┘
                 │             │
                 ▼             ▼
         ┌────────────────────────────┐
         │  RabbitMQ (MassTransit)    │
         │  - Event Exchange          │
         │  - Dead Letter Queues      │
         └────────────────────────────┘
                 │
                 ▼
         ┌────────────────────────────┐
         │   SQL Server (4 DBs)       │
         │  - PatientDb               │
         │  - ProviderDb              │
         │  - AppointmentDb           │
         │  - NotificationDb          │
         └────────────────────────────┘

Cross-service calls:
  AppointmentService ──gRPC──► ProviderService (slot locking)
  AppointmentService ──gRPC──► PatientService (patient verification)
```

---

## Project Structure

### Folder Hierarchy

```
HealthBooking.sln
├── src/
│   ├── SharedKernel/
│   │   ├── HealthBooking.SharedKernel/          [Base types, interfaces, extensions]
│   │   └── HealthBooking.Contracts/             [gRPC protos, event records]
│   │
│   ├── Services/
│   │   ├── PatientService/
│   │   │   ├── PatientService.Domain/           [Patient entity, value objects]
│   │   │   ├── PatientService.Application/      [Commands, queries, validators]
│   │   │   ├── PatientService.Infrastructure/   [DbContext, EF migrations, gRPC client]
│   │   │   └── PatientService.API/              [REST endpoints, middleware]
│   │   │
│   │   ├── ProviderService/                     [Same structure as Patient]
│   │   │   └── ... (4 layers)
│   │   │
│   │   ├── AppointmentService/                  [Same structure as Patient]
│   │   │   └── ... (4 layers)
│   │   │
│   │   └── NotificationService/                 [Same structure as Patient]
│   │       └── ... (4 layers)
│   │
│   ├── ApiGateway/                              [YARP reverse proxy]
│   └── IdentityServer/                          [Duende IdentityServer for auth]
│
├── tests/
│   ├── PatientService.UnitTests/                [xUnit fast tests]
│   ├── PatientService.IntegrationTests/         [TestContainers + live DB]
│   ├── ProviderService.UnitTests/
│   ├── ProviderService.IntegrationTests/
│   ├── AppointmentService.UnitTests/
│   ├── AppointmentService.IntegrationTests/
│   ├── NotificationService.UnitTests/
│   └── NotificationService.IntegrationTests/
│
├── docs/
│   ├── 001-distributed-healthcare-system.md    [This file]
│   └── architecture/                            [Detailed diagrams]
│
├── docker-compose.yml                          [SQL Server, RabbitMQ, Redis, Jaeger]
├── .editorconfig                               [Code style enforcement]
├── Directory.Build.props                       [Build defaults for all projects]
├── global.json                                 [SDK constraints]
└── .env.example                                [Environment template]
```

### Why This Structure?

| Decision | Reasoning |
|---|---|
| **Separate Domain/Application/Infrastructure/API** | Clean Architecture: testable components, framework agnostic, dependency direction |
| **Shared SharedKernel** | DRY: base classes, behaviors, extensions used by all services |
| **Separate Contracts library** | proto files + event records decoupled from service logic |
| **Unit + Integration tests per service** | Unit tests are fast (no DB/network), Integration tests verify real dependencies |
| **All in src/Services/** | Easy service discovery, batch operations, consistent naming |

---

## Core Components

### SharedKernel

**Location**: `src/SharedKernel/HealthBooking.SharedKernel/`

The SharedKernel contains all cross-cutting concerns used by every service. This avoids code duplication and ensures consistency.

#### 1. Domain Base Types

**File**: `Domain/IDomainEvent.cs`

```csharp
using MediatR;

namespace HealthBooking.SharedKernel.Domain;

public interface IDomainEvent : INotification
{
}
```

**Why?**
- Marks events as publishable via MediatR
- Aggregates can raise events that bubble up to handlers
- Type-safe event routing

**How it works?**
- Any domain event implements this interface
- On save, `OutboxPublishingInterceptor` captures events
- Events written to OutboxMessages table
- Background processor publishes to RabbitMQ

---

**File**: `Domain/AuditableEntity.cs`

```csharp
namespace HealthBooking.SharedKernel.Domain;

public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = default!;
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
```

**Why?**
- **GDPR/Compliance**: Track who created/modified data and when
- **Auditing**: Essential for healthcare: every change is traceable
- **Soft deletes**: Can track deletion time
- **DRY**: Add audit fields once, inherit everywhere

**How it works?**
- `AuditInterceptor` in Infrastructure intercepts SaveChanges
- Reads `ICurrentUserService.UserId` from HttpContext
- Sets CreatedAt/CreatedBy on new entities, ModifiedAt/ModifiedBy on updates
- Works transparently without requiring code in handlers

---

**File**: `Domain/AggregateRoot.cs`

```csharp
namespace HealthBooking.SharedKernel.Domain;

public abstract class AggregateRoot : AuditableEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];
    
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

**Why?**
- **Domain-Driven Design**: Business logic lives in domain, not in handlers
- **Event Sourcing prep**: Tracks what happened to the aggregate
- **Testability**: Raise event, assert it was raised - no mocking infrastructure
- **Bounded Context**: Each service has own aggregates

**How it works?**
```csharp
// In PatientService.Domain
public class Patient : AggregateRoot
{
    public Guid Id { get; set; }
    public string FullName { get; set; }
    
    public static Patient Create(Guid id, string name)
    {
        var patient = new Patient { Id = id, FullName = name };
        patient.AddDomainEvent(new PatientCreatedDomainEvent(id, name));
        return patient;
    }
}

// In Application command handler
var patient = Patient.Create(request.Id, request.Name);
await _patientRepository.AddAsync(patient);
await _unitOfWork.SaveChangesAsync(); // Events auto-published to Outbox
```

---

#### 2. Outbox Pattern

**File**: `Persistence/OutboxMessage.cs` & `Persistence/OutboxMessageConfiguration.cs`

```csharp
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = default!;
    public string SchemaVersion { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public string DestinationExchange { get; init; } = default!;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int RetryCount { get; set; }
    public string Status { get; set; } = "Pending";
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DestinationExchange).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Pending");
        builder.Property(x => x.RetryCount).HasDefaultValue(0);
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
               .HasDatabaseName("IX_OutboxMessages_Status_CreatedAt");
    }
}
```

**Why the Outbox Pattern?**

| Problem | Solution | Benefit |
|---|---|---|
| Dual Write: Save to DB, publish to RabbitMQ | Both in same transaction → Outbox table | Exactly-once delivery guarantee |
| Service crashes between save + publish | Outbox processor retries from DB | No lost events, resilience |
| Message broker down temporarily | Events queued in DB, retry on recovery | Graceful degradation |

**How it works?**

```
1. Aggregate raises event
2. SaveChanges called
3. OutboxPublishingInterceptor intercepts:
   - Reads domain events from aggregate
   - Creates OutboxMessage for each
   - Adds to OutboxMessages table
   - All in same DB transaction
4. Transaction commits atomically
5. Background processor polls OutboxMessages table every 5s
6. For each "Pending" message:
   - Publish via MassTransit IBus
   - Mark as "Published"
   - If error after 3 retries: mark "Failed"
7. Dead letter queue for failed messages
```

**Configuration**:
- **Status index**: Query pending messages efficiently
- **RetryCount**: Track publication attempts
- **SchemaVersion**: Evolve event schema safely
- **DestinationExchange**: Route to correct RabbitMQ fanout exchange

---

#### 3. MediatR Pipeline Behaviors

**File**: `Behaviors/LoggingBehavior.cs`

```csharp
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName}", requestName);
        var response = await next();
        logger.LogInformation("Handled {RequestName}", requestName);
        return response;
    }
}
```

**Why?**
- **Cross-cutting concern**: Don't want logging in every handler
- **MediatR pipeline**: Automatic injection before/after handler
- **Generic**: Works for all commands/queries without modification

**How it works?**
```csharp
// Registered in DI:
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

// When CreatePatientCommand executed:
// LoggingBehavior.Handle (logs "Handling CreatePatientCommand")
//   → ValidationBehavior.Handle (validates request)
//     → PerformanceBehavior.Handle (starts timer)
//       → CreatePatientCommandHandler.Handle (executes business logic)
//       → PerformanceBehavior returns (logs if >500ms)
//     → ValidationBehavior returns
//   → LoggingBehavior returns (logs "Handled CreatePatientCommand")
```

---

**File**: `Behaviors/ValidationBehavior.cs`

```csharp
using FluentValidation;
using MediatR;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

**Why?**
- **Fail fast**: Invalid requests rejected before handler runs
- **Declarative**: FluentValidation rules right in application layer
- **Reusable**: One validator can be used by multiple handlers
- **DRY**: Don't repeat validation logic across services

**Usage Example** (Week 2):
```csharp
// Application/Commands/CreatePatientCommand.cs
public record CreatePatientCommand(string FullName, string Email);

// Application/Commands/Validators/CreatePatientCommandValidator.cs
public class CreatePatientCommandValidator : AbstractValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Email).EmailAddress();
    }
}

// On DI registration: FluentValidation auto-discovers and registers validators
```

---

**File**: `Behaviors/PerformanceBehavior.cs`

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

public sealed class PerformanceBehavior<TRequest, TResponse>(
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (sw.ElapsedMilliseconds > 500)
            logger.LogWarning("Long running request: {RequestName} ({ElapsedMs} ms)",
                typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return response;
    }
}
```

**Why?**
- **Performance monitoring**: Identify slow queries/handlers
- **Non-intrusive**: No code change in handlers
- **Automatic**: Applies to ALL commands/queries via pipeline
- **Alerting**: Front-end for observability (Week 6)

**Best practices**:
- 500ms threshold: APIs should respond in <1s, so 500ms handler is slow
- LogWarning: Makes it searchable in logs, not noise from LogInformation
- OpenTelemetry integration: Can track in Jaeger (Week 6)

---

#### 4. Resilience Pipeline (Polly)

**File**: `Extensions/ResilienceExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

public static class ResilienceExtensions
{
    public static IHttpClientBuilder AddHealthBookingResiliencePipeline(
        this IHttpClientBuilder builder, string pipelineName)
    {
        builder.AddResilienceHandler(pipelineName, pipeline =>
        {
            // 1. Timeout: If service takes >10s, fail fast
            pipeline.AddTimeout(TimeSpan.FromSeconds(10));

            // 2. Retry: If transient error, retry up to 3 times with backoff
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,  // 500ms, 1s, 2s
                UseJitter = true,                              // Add randomness to prevent thundering herd
                Delay = TimeSpan.FromMilliseconds(500)
            });

            // 3. Circuit Breaker: If 50% of requests fail, trip circuit for 30s
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,              // Trip at 50% failure rate
                SamplingDuration = TimeSpan.FromSeconds(30),  // Sample window
                MinimumThroughput = 5,           // Need at least 5 requests to evaluate
                BreakDuration = TimeSpan.FromSeconds(30)      // Wait 30s before trying again
            });
        });

        return builder;
    }
}
```

**Why?**
- **Distributed systems fail**: Networks are unreliable
- **Cascading failures**: One slow service brings down others
- **Circuit breaker pattern**: Stop calling failing service immediately
- **Exponential backoff**: Give service time to recover

**How used** (in Week 4 when calling other services via gRPC):
```csharp
// AppointmentService.Infrastructure
services.AddGrpcClient<ProviderGrpc.ProviderGrpcClient>(o =>
    o.Address = new Uri("grpc://provider-service:50052"))
    .AddHealthBookingResiliencePipeline("provider-client");

// Usage: automatic retry, timeout, circuit breaker
var response = await _providerClient.LockSlotAsync(request);
```

**Resilience strategy explained**:

```
Request Timeline:

Request 1: Fast → Success (100% success)
Request 2: Slow (5s) → Success (100% success)
Request 3: Timeout (11s) → Fail after 10s → Retry
  - Retry 1: Fail → Wait 500ms
  - Retry 2: Fail → Wait 1s
  - Retry 3: Fail → Return error (3 retries exhausted)

Requests 4-8: Fail → 5/5 failed → Circuit trips (IsBroken = true)

Request 9: Circuit open → Fail immediately (no HTTP call)
Request 10: Circuit open → Fail immediately
...wait 30s...
Request 40: Circuit half-open → Try 1 request
  - If success: circuit closes
  - If fail: circuit opens again
```

---

### Contracts Library

**Location**: `src/SharedKernel/HealthBooking.Contracts/`

Contains all cross-service communication contracts (gRPC + event records).

#### Event Records

**File**: `Appointments/V1/V1_AppointmentEvents.cs`

```csharp
namespace HealthBooking.Contracts.Appointments.V1;

public sealed record V1_AppointmentBookedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid SlotId,
    DateTimeOffset ScheduledStartUtc,
    DateTimeOffset ScheduledEndUtc,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

public sealed record V1_AppointmentCancelledEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid SlotId,
    string CancellationReason,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

public sealed record V1_AppointmentRescheduledEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid OldSlotId,
    Guid NewSlotId,
    DateTimeOffset NewStartUtc,
    DateTimeOffset NewEndUtc,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

public sealed record V1_SlotReleasedEvent(
    Guid SlotId,
    Guid ProviderId,
    Guid AppointmentId,
    DateTimeOffset OccurredAt);
```

**Why Records?**
- **Immutable**: Can't accidentally modify event after publishing
- **Structural equality**: Two events with same data are equal
- **Serializable**: Easy JSON serialization for RabbitMQ
- **Data class**: No ceremony, all properties in one line

**Versioning strategy**:
- `V1_` prefix: Can add `V2_` events without breaking consumers
- `SagaCorrelationId`: Track distributed transaction across services
- `OccurredAt`: Event timestamp (important for diagnostics)

**How events flow**:
```
1. AppointmentService domain raises V1_AppointmentBookedEvent
2. OutboxPublishingInterceptor serializes to JSON
3. Stored in OutboxMessages table
4. Background processor extracts, publishes to RabbitMQ
   RabbitMQ topic: "v1-appointment-booked-event" (fanout)
5. ProviderService + NotificationService subscribed
6. Each service handler receives event, updates own DB independently
```

---

#### gRPC Service Definitions

**File**: `Protos/provider.proto`

```protobuf
syntax = "proto3";
option csharp_namespace = "HealthBooking.Contracts.Grpc";
package provider;

service ProviderGrpc {
  rpc GetSlotById (GetSlotRequest) returns (SlotResponse);
  rpc LockSlot (LockSlotRequest) returns (LockSlotResponse);
  rpc ReleaseSlot (ReleaseSlotRequest) returns (ReleaseSlotResponse);
}

message GetSlotRequest { string slot_id = 1; }

message SlotResponse {
  string slot_id = 1;
  string provider_id = 2;
  string start_time_utc = 3;
  string end_time_utc = 4;
  string status = 5;
}

message LockSlotRequest {
  string slot_id = 1;
  string appointment_id = 2;
}

message LockSlotResponse {
  bool success = 1;
  string error_message = 2;
}

message ReleaseSlotRequest { string slot_id = 1; }

message ReleaseSlotResponse { bool success = 1; }
```

**File**: `Protos/patient.proto`

```protobuf
syntax = "proto3";
option csharp_namespace = "HealthBooking.Contracts.Grpc";
package patient;

service PatientGrpc {
  rpc GetPatientById (GetPatientByIdRequest) returns (PatientResponse);
}

message GetPatientByIdRequest { string patient_id = 1; }

message PatientResponse {
  string patient_id = 1;
  string full_name = 2;
  string contact_email = 3;
}
```

**Why gRPC?**

| Aspect | gRPC | REST |
|---|---|---|
| **Protocol** | HTTP/2 (multiplexing) | HTTP/1.1 |
| **Serialization** | Protobuf (binary) | JSON (text) |
| **Latency** | 10-20ms | 50-100ms |
| **Use case** | Sync critical calls | External APIs |

**When to use in this system**:
- ✅ gRPC: AppointmentService → ProviderService (LockSlot must be immediate + atomic)
- ✅ gRPC: AppointmentService → PatientService (Verify patient exists before booking)
- ✅ gRPC: Internal service-to-service calls
- ❌ No gRPC: External clients (use REST API Gateway)

**How Grpc.Tools works**:
1. `provider.proto` and `patient.proto` added to project
2. On build: protoc compiler generates C# classes
3. Output: `ProviderGrpc.ProviderGrpcClient`, `GetSlotRequest`, `SlotResponse`, etc.
4. Services implement `ProviderGrpc.ProviderGrpcBase`, register in DI
5. Clients use `ProviderGrpc.ProviderGrpcClient` for calls

---

### Service Layer Pattern

Each of 4 services follows identical structure. Example: **PatientService**

#### Layer 1: Domain

**Purpose**: Pure business logic, no dependencies on infrastructure or frameworks.

```csharp
// PatientService.Domain/Patient.cs
namespace PatientService.Domain;

public class Patient : AggregateRoot
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PhoneNumber { get; set; } = default!;
    
    // Business rules enforced in domain
    public static Patient Create(Guid id, string fullName, string email, string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new DomainException("Name required");
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Email required");
        
        var patient = new Patient
        {
            Id = id,
            FullName = fullName,
            Email = email,
            PhoneNumber = phoneNumber
        };
        
        patient.AddDomainEvent(new PatientCreatedDomainEvent(id, fullName, email));
        return patient;
    }
    
    public void UpdateContactInfo(string email, string phoneNumber)
    {
        Email = email;
        PhoneNumber = phoneNumber;
        AddDomainEvent(new PatientContactUpdatedDomainEvent(Id, email, phoneNumber));
    }
}

public sealed class PatientCreatedDomainEvent(Guid patientId, string name, string email) 
    : IDomainEvent;

public sealed class PatientContactUpdatedDomainEvent(Guid patientId, string email, string phone) 
    : IDomainEvent;
```

**Why placed in Domain?**
- **No framework dependencies**: Can test without DI container, database, HTTP
- **Business logic**: Domain knows rules, not application layer
- **Domain events**: Capture what happened, not how it was persisted
- **Testability**: `new Patient.Create(...)` → assert DomainEvents contains expected events

**Unit test example**:
```csharp
[Fact]
public void Create_WithValidData_RaisesPatientCreatedEvent()
{
    // Arrange
    var id = Guid.NewGuid();
    var name = "John Doe";
    var email = "john@example.com";
    
    // Act
    var patient = Patient.Create(id, name, email, "+1234567890");
    
    // Assert
    var createdEvent = Assert.Single(patient.DomainEvents);
    Assert.IsType<PatientCreatedDomainEvent>(createdEvent);
    Assert.Equal(id, ((PatientCreatedDomainEvent)createdEvent).PatientId);
}
```

---

#### Layer 2: Application

**Purpose**: Use cases (commands/queries), validation, orchestration.

```csharp
// PatientService.Application/Commands/CreatePatientCommand.cs
namespace PatientService.Application.Commands;

public record CreatePatientCommand(
    Guid PatientId,
    string FullName,
    string Email,
    string PhoneNumber) : ICommand;

// PatientService.Application/Commands/Handlers/CreatePatientCommandHandler.cs
public class CreatePatientCommandHandler(
    IPatientRepository patientRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreatePatientCommand>
{
    public async Task Handle(CreatePatientCommand request, CancellationToken ct)
    {
        // Create domain aggregate
        var patient = Patient.Create(request.PatientId, request.FullName, 
                                     request.Email, request.PhoneNumber);
        
        // Persist (triggers interceptor → OutboxMessage table)
        await patientRepository.AddAsync(patient, ct);
        await unitOfWork.SaveChangesAsync(ct);
        
        // Note: Domain events auto-captured, no explicit publishing needed
    }
}

// PatientService.Application/Commands/Validators/CreatePatientCommandValidator.cs
public class CreatePatientCommandValidator : AbstractValidator<CreatePatientCommand>
{
    private readonly IPatientRepository _patientRepository;

    public CreatePatientCommandValidator(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
        
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Name required")
            .Length(2, 100).WithMessage("Name 2-100 chars");
        
        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email");
        
        RuleFor(x => x.Email)
            .MustAsync(async (email, ct) => 
                !await _patientRepository.EmailExistsAsync(email, ct))
            .WithMessage("Email already registered");
    }
}

// PatientService.Application/Queries/GetPatientQuery.cs
public record GetPatientQuery(Guid PatientId) : IQuery<PatientDto>;

public class GetPatientQueryHandler(IPatientRepository patientRepository) 
    : IQueryHandler<GetPatientQuery, PatientDto>
{
    public async Task<PatientDto> Handle(GetPatientQuery request, CancellationToken ct)
    {
        var patient = await patientRepository.GetByIdAsync(request.PatientId, ct);
        return new PatientDto(patient.Id, patient.FullName, patient.Email, patient.PhoneNumber);
    }
}

public record PatientDto(Guid Id, string FullName, string Email, string PhoneNumber);
```

**Why this structure?**
- **MediatR**: Single entry point for all requests → pipeline behaviors apply automatically
- **CQRS**: Commands (write) separate from Queries (read) → optimize both differently
- **Validators**: Checked by ValidationBehavior before handler runs
- **DTOs**: Application returns DTOs (not domain entities) → contract stable even if domain changes

**How Layer 2 integrates with Layer 1**:
```
HTTP Request → API controller → MediatR.Send(Command)
  ↓
  LoggingBehavior (logs request)
    ↓
    ValidationBehavior (validates with FluentValidation)
      ↓
      PerformanceBehavior (starts timer)
        ↓
        CreatePatientCommandHandler
          ↓
          Patient.Create(...) [Domain logic]
            ↓
            Repository.Add(patient)
            UnitOfWork.SaveChanges()
              ↓
              OutboxPublishingInterceptor
                ↓
                OutboxProcessor (background)
                  ↓
                  RabbitMQ publisher
```

---

#### Layer 3: Infrastructure

**Purpose**: Database, external services, repositories, migrations.

```csharp
// PatientService.Infrastructure/Persistence/PatientDbContext.cs
namespace PatientService.Infrastructure.Persistence;

public sealed class PatientDbContext : DbContext
{
    public PatientDbContext(DbContextOptions<PatientDbContext> options) : base(options) { }
    
    public DbSet<Patient> Patients { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Apply entity configurations
        modelBuilder.ApplyConfiguration(new PatientConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}

// PatientService.Infrastructure/Persistence/Configurations/PatientConfiguration.cs
public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");
        builder.HasKey(p => p.Id);
        
        builder.Property(p => p.FullName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Email).HasMaxLength(255).IsRequired();
        builder.Property(p => p.PhoneNumber).HasMaxLength(20);
        
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(256);
        builder.Property(p => p.ModifiedAt);
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);
        
        // Unique email constraint
        builder.HasIndex(p => p.Email).IsUnique().HasDatabaseName("IX_Patients_Email_Unique");
    }
}

// PatientService.Infrastructure/Repositories/PatientRepository.cs
public sealed class PatientRepository(PatientDbContext dbContext) : IPatientRepository
{
    public async Task AddAsync(Patient patient, CancellationToken ct = default)
    {
        await dbContext.Patients.AddAsync(patient, ct);
    }
    
    public async Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await dbContext.Patients.FirstOrDefaultAsync(p => p.Id == id, ct);
    }
    
    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        return await dbContext.Patients.AnyAsync(p => p.Email == email, ct);
    }
}

// PatientService.Infrastructure/Persistence/Interceptors/AuditInterceptor.cs
public sealed class AuditInterceptor(ICurrentUserService currentUser) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var context = eventData.Context!;
        var now = DateTimeOffset.UtcNow;
        var userId = currentUser.UserId ?? "system";

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = userId;
            }
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = now;
                entry.Entity.ModifiedBy = userId;
                // Don't modify created fields
                entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
            }
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}

// PatientService.Infrastructure/Persistence/Interceptors/OutboxPublishingInterceptor.cs
public sealed class OutboxPublishingInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var context = eventData.Context!;
        var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var outboxMessage = new OutboxMessage
                {
                    EventType = domainEvent.GetType().FullName!,
                    SchemaVersion = "1.0",
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    DestinationExchange = domainEvent.GetType().Name
                        .Replace("DomainEvent", "").ToLowerInvariant(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                context.Set<OutboxMessage>().Add(outboxMessage);
            }
            aggregate.ClearDomainEvents();
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}

// PatientService.Infrastructure/Messaging/OutboxProcessor.cs
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IBus bus,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PatientDbContext>();

                var pendingMessages = await db.OutboxMessages
                    .Where(m => m.Status == "Pending")
                    .OrderBy(m => m.CreatedAt)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                foreach (var message in pendingMessages)
                {
                    try
                    {
                        var eventType = Type.GetType(message.EventType);
                        if (eventType is null) 
                        {
                            message.Status = "Failed";
                            continue;
                        }

                        var @event = JsonSerializer.Deserialize(message.Payload, eventType);
                        await bus.Publish(@event, stoppingToken);

                        message.PublishedAt = DateTimeOffset.UtcNow;
                        message.Status = "Published";
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to publish event {EventId}", message.Id);
                        message.RetryCount++;
                        if (message.RetryCount >= 3)
                            message.Status = "Failed";
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
                await Task.Delay(5000, stoppingToken); // Poll every 5 seconds
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in OutboxProcessor");
                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}
```

**Why Infrastructure layer?**
- **EF Core DbContext**: Database abstraction
- **Repositories**: Data access patterns
- **Interceptors**: Cross-cutting database concerns
- **Migrations**: Version-controlled schema changes
- **Background services**: Outbox processor, health checks

**EF Core Fluent API instead of Data Annotations**:
- ✅ Configurations in separate files → testable
- ✅ Complex rules possible (owned types, value objects)
- ✅ No framework attributes in domain entities
- ✅ Schema changes don't require model changes

---

#### Layer 4: API

**Purpose**: HTTP endpoints, middleware, dependency injection.

```csharp
// PatientService.API/Endpoints/CreatePatientEndpoint.cs
namespace PatientService.API.Endpoints;

public static class PatientEndpoints
{
    public static void MapPatientEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/patients")
            .WithName("Patients")
            .WithOpenApi();

        group.MapPost("", CreatePatient)
            .WithName("CreatePatient")
            .WithOpenApi();

        group.MapGet("{id}", GetPatient)
            .WithName("GetPatient")
            .WithOpenApi();
    }

    private static async Task<IResult> CreatePatient(
        CreatePatientCommand command,
        IMediator mediator,
        CancellationToken ct)
    {
        await mediator.Send(command, ct);
        return Results.Created($"/api/patients/{command.PatientId}", null);
    }

    private static async Task<IResult> GetPatient(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetPatientQuery(id), ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }
}

// PatientService.API/Program.cs
var builder = WebApplicationBuilder.CreateBuilder(args);

// Add infrastructure
builder.Services.AddScoped<PatientDbContext>();
builder.Services.AddScoped(typeof(SaveChangesInterceptor), typeof(AuditInterceptor));
builder.Services.AddScoped(typeof(SaveChangesInterceptor), typeof(OutboxPublishingInterceptor));
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddHostedService<OutboxProcessor>();

// Add application services
builder.Services
    .AddMediatR(config => config.RegisterServicesFromAssembly(typeof(CreatePatientCommand).Assembly))
    .AddValidatorsFromAssemblyContaining<CreatePatientCommandValidator>()
    .AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>))
    .AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>))
    .AddScoped(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

// Add authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "http://identity-server:5005";
        options.Audience = "patient-api";
        options.TokenValidationParameters.ValidateLifetime = true;
    });

// Add HTTP clients with resilience
builder.Services.AddHttpClient<PatientServiceClient>()
    .AddHealthBookingResiliencePipeline("patient-service");

// Add background services
builder.Services.AddHostedService<OutboxProcessor>();

var app = builder.Build();

// Middleware
app.UseAuthentication();
app.UseAuthorization();

// Apply migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PatientDbContext>();
    await db.Database.MigrateAsync();
}

// Map endpoints
app.MapPatientEndpoints();

app.Run();
```

**DI Registration Strategy**:
- **Scoped**: DbContext, repositories (per HTTP request)
- **Transient**: Command/query handlers (created fresh each time)
- **Singleton**: Configuration, ILogger factory
- **Hosted services**: Background processors (OutboxProcessor)

**Why this DI structure minimizes memory leaks?**
```
Scoped dependencies:
  - DBContext: Disposed with scope (end of request)
  - Repositories: GC'd with scope
  - EF change tracker: Cleared

Result: No memory leaks, predictable resource cleanup
```

---

## Week 1 Implementation Details

### What Was Built (T001-T012)

| Task | Component | Size | Purpose |
|---|---|---|---|
| T001-T005 | Project scaffolding | - | Solution structure, config files |
| T006 | SharedKernel | 6 files | Base types, audit, outbox |
| T007 | Contracts library | 1 file | Event records |
| T008 | Proto definitions | 2 files | gRPC service contracts |
| T009-T010 | MediatR + Polly | 4 files | Pipeline behaviors, resilience |
| T011 | Service projects | 16 projects | Domain/App/Infra/API per service |
| T012 | Test projects | 8 projects | Unit + Integration per service |

### Repository Initialization

**File**: `global.json`
```json
{
  "sdk": {
    "version": "9.0.0",
    "rollForward": "latestFeature"
  }
}
```
**Why?** Ensures team uses .NET 9, not older versions that might be installed

**File**: `Directory.Build.props`
```xml
<Project>
  <PropertyGroup>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>
```
**Why?**
- **Nullable=enable**: Forces null checks → prevents NullReferenceException bugs
- **ImplicitUsings**: Auto-imports common namespaces → less boilerplate
- **TreatWarningsAsErrors**: Compiler errors in CI if warnings exist → enforces quality
- **Inheritance**: Applied to ALL projects without per-project repetition

**File**: `.editorconfig`
```editorconfig
root = true

[*.cs]
indent_style = space
indent_size = 4
max_line_length = 120
csharp_style_var_for_built_in_types = false:warning
```
**Why?** IDE auto-formats code → consistent style across team without debates

---

## Design Patterns & Best Practices

### Dependency Injection - Why Constructor Injection?

```csharp
// ✅ Good: Explicit dependencies inject framework-agnostic
public class CreatePatientCommandHandler(
    IPatientRepository patientRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreatePatientCommand>
{
    public async Task Handle(CreatePatientCommand request, CancellationToken ct)
    {
        var patient = awaIt patientRepository.GetByIdAsync(request.PatientId, ct);
        // ...
    }
}

// ❌ Bad: Service Locator pattern (hidden dependencies)
public class CreatePatientCommandHandler : ICommandHandler<CreatePatientCommand>
{
    public async Task Handle(CreatePatientCommand request, CancellationToken ct)
    {
        var repository = ServiceLocator.GetService<IPatientRepository>();
        // ...
    }
}

// ❌ Bad: Static dependencies (non-testable)
public class CreatePatientCommandHandler : ICommandHandler<CreatePatientCommand>
{
    public async Task Handle(CreatePatientCommand request, CancellationToken ct)
    {
        var patient = await Database.GetPatient(request.PatientId);
        // ...
    }
}
```

**Why constructor injection?**
- **Testability**: Mock dependencies in unit tests
- **Visibility**: Clear what handler needs
- **Framework-agnostic**: Works without DI container
- **Compile-time safety**: Missing dependency → compile error, not runtime

---

### Records for Immutability

```csharp
// ✅ Good: Event is immutable after creation
public sealed record V1_AppointmentBookedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid SlotId,
    DateTimeOffset ScheduledStartUtc,
    DateTimeOffset ScheduledEndUtc,
    Guid SagaCorrelationId,
    DateTimeOffset OccurredAt);

// ❌ Bad: Mutable properties - someone could change event after publishing
public class V1_AppointmentBookedEvent
{
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    // ...
}
```

**Why immutable events?**
- **Audit trail**: Event == what actually happened, not what someone changed it to
- **Consistency**: Multiple subscribers see same data
- **Debugging**: Event value in logs equals what actually persisted
- **Concurrency**: No race conditions from simultaneous modifications

---

### Sealed Classes for ORM Predictability

```csharp
// ✅ Good: Sealed prevents proxy generation, predictable SQL
public sealed class Patient : AggregateRoot
{
    // ...
}

// ❌ Bad: Virtual may generate proxy - unpredictable lazy loading
public class Patient : AggregateRoot
{
    public virtual string FullName { get; set; }
    public virtual void UpdateEmail(string email) { }
}
```

**Why sealed?**
- EF Core creates proxies for virtual methods/properties → harder to debug
- No subclasses expected for aggregates
- Performance: no proxy generation overhead
- Clarity: what you read is what you execute

---

### Using Records for DTOs

```csharp
// ✅ Good: DTOs are data transfer, immutable
public record CreatePatientRequest(string FullName, string Email, string PhoneNumber);
public record PatientDto(Guid Id, string FullName, string Email, string PhoneNumber);

// ❌ Bad: Mutable DTO allows accidental data corruption
public class PatientDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
}
```

**Why immutable DTOs?**
- **Consistent API contract**: Client sees same data throughout request
- **Prevents bugs**: Handler can't accidentally modify request
- **Serialization safety**: JSON serializers love records
- **Value semantics**: Two PatientDto with same data are equal

---

### Saga Correlation ID for Distributed Tracing

```csharp
public sealed record V1_AppointmentBookedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    Guid SlotId,
    DateTimeOffset ScheduledStartUtc,
    DateTimeOffset ScheduledEndUtc,
    Guid SagaCorrelationId,  // ← Track across services
    DateTimeOffset OccurredAt);
```

**Why SagaCorrelationId?**
```
Request flow:
  1. Client calls POST /appointments
  2. AppointmentService generates SagaCorrelationId = GUID-123
  3. Publishes V1_AppointmentBookedEvent(sagaCorrelationId: GUID-123)
  4. ProviderService receives, logs "GUID-123: Locked slot"
  5. NotificationService receives, logs "GUID-123: Sent email"

In Jaeger/Logs:
  grep "GUID-123" → see entire distributed transaction
  Easy debugging of failures across services
```

---

### Why Outbox Pattern Over Direct Publishing?

```
❌ Bad Approach (Dual Write Problem):
┌─────────────────────────────────┐
│ AppointmentService              │
│ 1. Save appointment to DB       │ ← Success
│ 2. Publish to RabbitMQ          │ ← RabbitMQ down → Lost event!
└─────────────────────────────────┘

✅ Good Approach (Outbox Pattern):
┌──────────────────────────────────────────┐
│ AppointmentService                       │
│ 1. Save appointment to DB    ────┐       │
│ 2. Insert OutboxMessage to DB ──┤ Same  │ ← Atomic transaction
│                                  │Txn    │
│ ==> COMMIT ◄──────────────────────┘       │
└──────────────────────────────────────────┘
          ↓
┌──────────────────────────────────────────┐
│ Background OutboxProcessor                │
│ 1. Poll OutboxMessages (status=Pending)   │
│ 2. Publish via RabbitMQ                   │
│ 3. Mark as Published                      │
│ 4. If failed: Retry or log to DLQ         │
└──────────────────────────────────────────┘
          ↓
     RabbitMQ (fanout)
     ├─→ ProviderService
     ├─→ NotificationService
     └─→ Others
```

**Outbox guarantees**:
- ✅ No dual write problem
- ✅ Events not lost even if service crashes
- ✅ Retry logic built-in
- ✅ Dead letter queue for failures
- ✅ Idempotent (same event_id won't be processed twice)

---

## How to Use & Extend

### Adding a New Entity to PatientService

**1. Create Domain Entity**
```csharp
// PatientService.Domain/ValueObjects/ContactInfo.cs
public sealed record ContactInfo(string Email, string PhoneNumber)
{
    public static ContactInfo Create(string email, string phoneNumber)
    {
        if (string.IsNullOrEmpty(email)) throw new DomainException("Email required");
        return new(email, phoneNumber);
    }
}

// PatientService.Domain/EmergencyContact.cs
public class EmergencyContact : AuditableEntity
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string Name { get; set; }
    public ContactInfo ContactInfo { get; set; }
    
    public static EmergencyContact Create(Guid patientId, string name, string email, string phone)
    {
        return new EmergencyContact
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            Name = name,
            ContactInfo = ContactInfo.Create(email, phone)
        };
    }
}
```

**2. Add to DbContext**
```csharp
// PatientService.Infrastructure/Persistence/PatientDbContext.cs
public DbSet<EmergencyContact> EmergencyContacts { get; set; }

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfiguration(new EmergencyContactConfiguration());
}
```

**3. Configure with Fluent API**
```csharp
// PatientService.Infrastructure/Persistence/Configurations/EmergencyContactConfiguration.cs
public sealed class EmergencyContactConfiguration : IEntityTypeConfiguration<EmergencyContact>
{
    public void Configure(EntityTypeBuilder<EmergencyContact> builder)
    {
        builder.ToTable("EmergencyContacts");
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.PatientId).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        
        // Value object configuration
        builder.OwnsOne(e => e.ContactInfo, nav =>
        {
            nav.Property(c => c.Email).HasColumnName("Email").HasMaxLength(255);
            nav.Property(c => c.PhoneNumber).HasColumnName("PhoneNumber").HasMaxLength(20);
        });
        
        builder.HasIndex(e => e.PatientId);
    }
}
```

**4. Create Migration**
```bash
cd PatientService.Infrastructure
dotnet ef migrations add AddEmergencyContact
dotnet ef database update
```

**5. Add Repository**
```csharp
// PatientService.Application/Repositories/IEmergencyContactRepository.cs
public interface IEmergencyContactRepository
{
    Task AddAsync(EmergencyContact contact, CancellationToken ct = default);
    Task<List<EmergencyContact>> GetByPatientIdAsync(Guid patientId, CancellationToken ct = default);
}

// PatientService.Infrastructure/Repositories/EmergencyContactRepository.cs
public sealed class EmergencyContactRepository(PatientDbContext db) 
    : IEmergencyContactRepository
{
    public async Task AddAsync(EmergencyContact contact, CancellationToken ct = default)
    {
        await db.EmergencyContacts.AddAsync(contact, ct);
    }
    
    public async Task<List<EmergencyContact>> GetByPatientIdAsync(
        Guid patientId, CancellationToken ct = default)
    {
        return await db.EmergencyContacts
            .Where(e => e.PatientId == patientId)
            .ToListAsync(ct);
    }
}
```

**6. Register in DI**
```csharp
// PatientService.API/Program.cs
builder.Services.AddScoped<IEmergencyContactRepository, EmergencyContactRepository>();
```

**7. Use in Command**
```csharp
// PatientService.Application/Commands/AddEmergencyContactCommand.cs
public record AddEmergencyContactCommand(
    Guid PatientId,
    string Name,
    string Email,
    string PhoneNumber) : ICommand;

// PatientService.Application/Commands/Handlers/AddEmergencyContactCommandHandler.cs
public class AddEmergencyContactCommandHandler(
    IEmergencyContactRepository contactRepository,
    IUnitOfWork unitOfWork) : ICommandHandler<AddEmergencyContactCommand>
{
    public async Task Handle(AddEmergencyContactCommand request, CancellationToken ct)
    {
        var contact = EmergencyContact.Create(
            request.PatientId, request.Name, request.Email, request.PhoneNumber);
        
        await contactRepository.AddAsync(contact, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
```

---

### Publishing a New Event

**1. Define Event Record in Contracts**
```csharp
// HealthBooking.Contracts/Appointments/V1/V1_AppointmentConfirmedEvent.cs
public sealed record V1_AppointmentConfirmedEvent(
    Guid AppointmentId,
    Guid PatientId,
    Guid ProviderId,
    DateTimeOffset ConfirmedAt) : IDomainEvent;
```

**2. Raise in Domain**
```csharp
// AppointmentService.Domain/Appointment.cs
public void Confirm()
{
    Status = AppointmentStatus.Confirmed;
    AddDomainEvent(new V1_AppointmentConfirmedEvent(Id, PatientId, ProviderId, DateTimeOffset.UtcNow));
}
```

**3. Auto-Published**
- SaveChanges called → OutboxPublishingInterceptor captures event
- Event serialized to OutboxMessage
- Background processor publishes to RabbitMQ

**4. Subscribe in Another Service**
```csharp
// NotificationService.Application/EventHandlers/AppointmentConfirmedEventHandler.cs
public class AppointmentConfirmedEventHandler(
    INotificationRepository notificationRepository,
    IUnitOfWork unitOfWork) : INotificationHandler<V1_AppointmentConfirmedEvent>
{
    public async Task Handle(V1_AppointmentConfirmedEvent notification, CancellationToken ct)
    {
        // Send email to patient confirming appointment
        var notif = new NotificationRecord
        {
            Id = Guid.NewGuid(),
            PatientId = notification.PatientId,
            Type = "AppointmentConfirmed",
            Sent = DateTime.UtcNow
        };
        
        await notificationRepository.AddAsync(notif, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

// Register in NotificationService.API/Program.cs
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AppointmentConfirmedEventHandler>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq", 5672, "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        
        cfg.ReceiveEndpoint("appointment-confirmed-queue", e =>
        {
            e.ConfigureConsumer<AppointmentConfirmedEventHandler>(context);
        });
    });
});
```

---

## Build & Deployment

### Local Development

**Prerequisites:**
```bash
# Install .NET 9
dotnet --version  # Should be 9.0.0+

# Docker for SQL Server + RabbitMQ
docker --version
docker-compose --version
```

**Setup:**
```bash
cd "Booking .net"

# Restore packages
dotnet restore

# Build
dotnet build

# Run migrations
dotnet ef migrations list --project src/Services/PatientService/PatientService.Infrastructure

# Start Docker Compose stack
docker-compose up -d

# Run tests
dotnet test

# Start all services
cd src/Services/PatientService/PatientService.API
dotnet run --urls=http://localhost:5001

# In separate terminal, start other services...
cd src/Services/ProviderService/ProviderService.API
dotnet run --urls=http://localhost:5002
```

**Environment Variables (.env)**
```env
SQL_SERVER_CONNECTION_STRING=Server=localhost,1433;Database=HealthBooking;User Id=sa;Password=YourPassword123!;
RABBITMQ_HOST=localhost
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest
REDIS_CONNECTION_STRING=localhost:6379
IDENTITY_SERVER_URL=http://localhost:5005
JAEGER_ENDPOINT=http://localhost:4317
```

---

### CI/CD Pipeline

**GitHub Actions workflow** (.github/workflows/ci.yml):
```yaml
name: CI

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 9.0.x
      
      - run: dotnet restore
      - run: dotnet build
      - run: dotnet test --no-build
      
      - name: SonarCloud Scan
        uses: SonarSource/sonarcloud-github-action@master
```

---

### Deployment to Production

**Docker images:**
```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app
COPY . .

EXPOSE 5001
ENTRYPOINT ["dotnet", "PatientService.API.dll"]
```

**Kubernetes deployment:**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: patient-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: patient-service
  template:
    metadata:
      labels:
        app: patient-service
    spec:
      containers:
      - name: patient-service
        image: ghcr.io/your-org/patient-service:latest
        ports:
        - containerPort: 5001
        env:
        - name: SQL_CONNECTION
          valueFrom:
            secretKeyRef:
              name: db-credentials
              key: patient-conn-str
        - name: RABBITMQ_HOST
          value: rabbitmq
        livenessProbe:
          httpGet:
            path: /health/live
            port: 5001
          initialDelaySeconds: 30
          periodSeconds: 10
```

---

## Key Takeaways

| Concept | Why It Matters | Example |
|---|---|---|
| **Clean Architecture** | Testable, maintainable, framework-independent | Domain entity has no dependencies |
| **MediatR Pipeline** | Cross-cutting concerns (logging, validation) applied uniformly | All commands auto-validated, auto-logged |
| **Outbox Pattern** | Guaranteed event delivery even if service crashes | No lost events, eventual consistency |
| **Domain Events** | Business logic + audit trail | Aggregate raises events, infrastructure persists them |
| **Immutable Records** | Prevents accidental data corruption | Event = fact that happened, can't change |
| **Constructor Injection** | Testable, clear dependencies | Mock repository in unit test, works perfectly |
| **Code-First Migrations** | Schema is code, version controlled | One `dotnet ef migrations add` command |
| **Resilience Patterns** | Distributed systems fail gracefully | Timeout + retry + circuit breaker = 99.9% availability |

---

## Next Steps (Weeks 2-6)

- **Week 2**: Implement PatientService & ProviderService domain models + queries
- **Week 3**: Implement AppointmentService booking workflow (gRPC calls to patient/provider)
- **Week 4**: NotificationService + event 빠consumers for all events
- **Week 5**: API Gateway (YARP), Redis caching, health checks
- **Week 6**: OpenTelemetry (Jaeger), Docker Compose, E2E tests

---

**Document Version**: 1.0  
**Branch**: 001-distributed-healthcare-system  
**Last Commit**: `8687451`  
**Total Files**: 26 projects | 138 commits (when complete)
