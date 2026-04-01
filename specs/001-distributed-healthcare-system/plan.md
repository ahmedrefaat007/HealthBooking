# Implementation Plan — Distributed Healthcare Appointment System

**Feature Branch**: `001-distributed-healthcare-system`
**Plan Version**: 1.0.0
**Created**: 2026-04-01
**Constitution Version**: 1.0.0
**Sprint Duration**: 6 weeks

---

## Table of Contents

1. [Solution Structure & Project Layout](#1-solution-structure--project-layout)
2. [Microservice Breakdown — Clean Architecture Layers](#2-microservice-breakdown--clean-architecture-layers)
3. [Database Schema Design Per Service](#3-database-schema-design-per-service)
4. [API Contracts — REST Endpoints](#4-api-contracts--rest-endpoints)
5. [gRPC Contracts](#5-grpc-contracts)
6. [Message Broker Event Contracts — RabbitMQ / MassTransit](#6-message-broker-event-contracts--rabbitmq--masstransit)
7. [YARP API Gateway Configuration](#7-yarp-api-gateway-configuration)
8. [IdentityServer OIDC Configuration](#8-identityserver-oidc-configuration)
9. [Redis Caching Strategy](#9-redis-caching-strategy)
10. [Outbox Pattern Implementation Design](#10-outbox-pattern-implementation-design)
11. [CQRS / MediatR Command & Query Structure](#11-cqrs--mediatr-command--query-structure)
12. [EF Core Interceptors — Auditing](#12-ef-core-interceptors--auditing)
13. [Polly Resilience Policies](#13-polly-resilience-policies)
14. [OpenTelemetry + Jaeger Setup](#14-opentelemetry--jaeger-setup)
15. [Docker Compose Topology](#15-docker-compose-topology)
16. [Weekly Milestone Sequencing](#16-weekly-milestone-sequencing)

---

## 1. Solution Structure & Project Layout

```
HealthBooking.sln
│
├── src/
│   ├── SharedKernel/
│   │   ├── HealthBooking.SharedKernel/                   # Base types, domain event interfaces, outbox schema
│   │   └── HealthBooking.Contracts/                      # Versioned message event contracts (V1_*)
│   │
│   ├── ApiGateway/
│   │   └── HealthBooking.ApiGateway/                     # YARP gateway — routing, auth, rate limiting
│   │
│   ├── Services/
│   │   ├── PatientService/
│   │   │   ├── PatientService.Domain/
│   │   │   ├── PatientService.Application/
│   │   │   ├── PatientService.Infrastructure/
│   │   │   └── PatientService.API/
│   │   │
│   │   ├── ProviderService/
│   │   │   ├── ProviderService.Domain/
│   │   │   ├── ProviderService.Application/
│   │   │   ├── ProviderService.Infrastructure/
│   │   │   └── ProviderService.API/
│   │   │
│   │   ├── AppointmentService/
│   │   │   ├── AppointmentService.Domain/
│   │   │   ├── AppointmentService.Application/
│   │   │   ├── AppointmentService.Infrastructure/
│   │   │   └── AppointmentService.API/
│   │   │
│   │   └── NotificationService/
│   │       ├── NotificationService.Domain/
│   │       ├── NotificationService.Application/
│   │       ├── NotificationService.Infrastructure/
│   │       └── NotificationService.API/
│   │
│   └── IdentityServer/
│       └── HealthBooking.IdentityServer/                 # Duende IdentityServer host
│
├── tests/
│   ├── PatientService.UnitTests/
│   ├── PatientService.IntegrationTests/
│   ├── ProviderService.UnitTests/
│   ├── ProviderService.IntegrationTests/
│   ├── AppointmentService.UnitTests/
│   ├── AppointmentService.IntegrationTests/
│   ├── NotificationService.UnitTests/
│   └── NotificationService.IntegrationTests/
│
├── docker-compose.yml
├── docker-compose.override.yml
├── .env.example
├── .editorconfig
└── README.md
```

### SharedKernel Library — Exported Types

`HealthBooking.SharedKernel` targets `netstandard2.1` to avoid coupling services to a specific .NET version.

```csharp
// Base entity with audit fields
public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }
}

// Domain event marker interface
public interface IDomainEvent
{
    Guid EventId { get; }
    string EventType { get; }
    string SchemaVersion { get; }
    string SourceService { get; }
    Guid CorrelationId { get; }
    DateTimeOffset OccurredAt { get; }
}

// Outbox message schema
public class OutboxMessage
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string DestinationExchange { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int RetryCount { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
}

public enum OutboxStatus { Pending, Published, Failed }

// Aggregate root base
public abstract class AggregateRoot : AuditableEntity
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    protected void AddDomainEvent(IDomainEvent @event) => _domainEvents.Add(@event);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

`HealthBooking.Contracts` holds all versioned message contracts as plain C# records (no service dependencies):

```csharp
namespace HealthBooking.Contracts.Appointments.V1;

public record V1_AppointmentBookedEvent(
    Guid EventId, string EventType, string SchemaVersion, string SourceService,
    Guid CorrelationId, DateTimeOffset OccurredAt,
    Guid AppointmentId, Guid PatientId, Guid ProviderId,
    Guid SlotId, DateTimeOffset ScheduledStart, DateTimeOffset ScheduledEnd
) : IVersionedEvent;

public record V1_AppointmentCancelledEvent(...) : IVersionedEvent;
public record V1_AppointmentRescheduledEvent(...) : IVersionedEvent;
public record V1_SlotReleasedEvent(...) : IVersionedEvent;
public record V1_NotificationRequestedEvent(...) : IVersionedEvent;
```

---

## 2. Microservice Breakdown — Clean Architecture Layers

Each service strictly follows the four-layer layout mandated by the constitution.

### 2.1 PatientService

| Layer | Key Contents |
|---|---|
| **Domain** | `Patient` (aggregate root), `PatientId` (value object), `PatientRegisteredEvent` (domain event) |
| **Application** | `RegisterPatientCommand`, `UpdatePatientProfileCommand`, `GetPatientByIdQuery`, `GetPatientByEmailQuery`; `IPatientRepository`; `IIdentityProvisioningService` |
| **Infrastructure** | `PatientDbContext` (EF Core), `PatientRepository`, `IdentityProvisioningClient` (HTTP to IdentityServer), `AuditInterceptor`, `OutboxProcessor` |
| **API** | `PatientsEndpoints` (Minimal API), `HealthCheckEndpoints`, JWT middleware wiring, DI registration |

**Domain — Patient Aggregate**:
```csharp
public sealed class Patient : AggregateRoot
{
    public PatientId Id { get; private set; }
    public FullName FullName { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    public Email ContactEmail { get; private set; }         // value object — enforces format
    public PhoneNumber PhoneNumber { get; private set; }
    public DateTimeOffset RegistrationDate { get; private set; }

    private Patient() { }  // EF Core

    public static Patient Register(string firstName, string lastName,
        DateOnly dateOfBirth, string email, string phone)
    {
        var patient = new Patient
        {
            Id = PatientId.New(),
            FullName = new FullName(firstName, lastName),
            DateOfBirth = dateOfBirth,
            ContactEmail = new Email(email),
            PhoneNumber = new PhoneNumber(phone),
            RegistrationDate = DateTimeOffset.UtcNow
        };
        patient.AddDomainEvent(new PatientRegisteredEvent(patient.Id, patient.ContactEmail));
        return patient;
    }

    public void UpdateProfile(string firstName, string lastName,
        string phone) { /* guards, raise ProfileUpdatedEvent */ }
}
```

---

### 2.2 ProviderService

| Layer | Key Contents |
|---|---|
| **Domain** | `Provider` (aggregate root), `AvailabilitySlot` (entity), `SlotStatus` (enum), `SlotId` / `ProviderId` (value objects), domain events: `SlotCreatedEvent`, `SlotStatusChangedEvent` |
| **Application** | `CreateProviderCommand`, `DefineAvailabilityCommand`, `CancelAvailabilityWindowCommand`, `GetProviderByIdQuery`, `SearchProvidersBySpecializationQuery`, `GetProviderSlotsQuery`; `IProviderRepository`; `ISlotRepository`; `ICacheService` |
| **Infrastructure** | `ProviderDbContext`, `ProviderRepository`, `SlotRepository`, `RedisCacheService`, `AuditInterceptor`, `OutboxProcessor`, `AppointmentBookedConsumer` (MassTransit) |
| **API** | `ProvidersEndpoints`, `SlotsEndpoints`, gRPC `ProviderGrpcService` |

**Domain — AvailabilitySlot entity** (owned by Provider aggregate):
```csharp
public sealed class AvailabilitySlot : AuditableEntity
{
    public SlotId Id { get; private set; }
    public ProviderId ProviderId { get; private set; }
    public DateTimeOffset StartTimeUtc { get; private set; }
    public DateTimeOffset EndTimeUtc { get; private set; }
    public int DurationMinutes { get; } = 30;
    public SlotStatus Status { get; private set; } = SlotStatus.Available;

    // EF Core rowversion for optimistic concurrency
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void MarkAsBooked()
    {
        Guard.Against.InvalidInput(Status, nameof(Status),
            s => s == SlotStatus.Available, "Only Available slots can be booked.");
        Status = SlotStatus.Booked;
    }

    public void Release()
    {
        Status = SlotStatus.Available;
    }
}

public enum SlotStatus { Available, Booked, Blocked }
```

---

### 2.3 AppointmentService

| Layer | Key Contents |
|---|---|
| **Domain** | `Appointment` (aggregate root), `AppointmentStatus` (enum, full state machine), `SagaCorrelationId` (value object), `AppointmentId` / `PatientId` / `ProviderId` / `SlotId` (value objects), domain events: `AppointmentBookedDomainEvent`, `AppointmentCancelledDomainEvent`, `AppointmentRescheduledDomainEvent` |
| **Application** | `BookAppointmentCommand`, `CancelAppointmentCommand`, `RescheduleAppointmentCommand`, `GetAppointmentByIdQuery`, `GetPatientAppointmentsQuery`; `IAppointmentRepository`; `IProviderSlotGrpcClient`; `IPatientGrpcClient` |
| **Infrastructure** | `AppointmentDbContext`, `AppointmentRepository`, `ProviderSlotGrpcClient`, `PatientGrpcClient`, `OutboxProcessor`, `OutboxPublisher`, `AuditInterceptor` |
| **API** | `AppointmentsEndpoints`, `HealthCheckEndpoints` |

**Domain — Appointment state machine**:
```
                     ┌───────────────────┐
                     │      Pending      │
                     └─────────┬─────────┘
                               │ (payment / sys confirm)
                     ┌─────────▼─────────┐
              ┌──────│    Confirmed      │──────┐
              │      └─────────┬─────────┘      │
         Cancel               │ (date arrives) Reschedule
              │      ┌─────────▼─────────┐      │ (→ new Appointment)
              │      │    Completed      │      │
              │      └───────────────────┘      │
    ┌─────────▼──────────────────────────────────▼───┐
    │                  Cancelled                      │
    └─────────────────────────────────────────────────┘
              │ (patient no-show)
    ┌─────────▼─────────┐
    │      NoShow       │
    └───────────────────┘
```

---

### 2.4 NotificationService

| Layer | Key Contents |
|---|---|
| **Domain** | `NotificationRecord` (entity), `NotificationStatus` (enum), `NotificationChannel` (enum), `NotificationTemplate` (value object) |
| **Application** | `ProcessAppointmentBookedCommand`, `ProcessAppointmentCancelledCommand`, `ProcessReminderCommand`; `INotificationRepository`; `IEmailDeliveryService` |
| **Infrastructure** | `NotificationDbContext`, `NotificationRepository`, `StubEmailDeliveryService` (production-ready interface), `AppointmentBookedConsumer`, `AppointmentCancelledConsumer`, `ReminderScheduler` (Hangfire or Quartz.NET) |
| **API** | `HealthCheckEndpoints` only — no external REST API |

---

## 3. Database Schema Design Per Service

All schema is defined using **EF Core code-first** with `IEntityTypeConfiguration<T>` Fluent API.
Migrations are generated per-service via `dotnet ef migrations add` and applied on startup through `dbContext.Database.MigrateAsync()`.
No raw SQL DDL is shipped — the database is fully owned by EF Core.

### 3.0 Shared — `OutboxMessage` Entity (per-service, same DbContext)

```csharp
// SharedKernel/HealthBooking.SharedKernel/Persistence/OutboxMessage.cs
public sealed class OutboxMessage
{
    public Guid   Id                  { get; init; } = Guid.NewGuid();
    public string EventType           { get; init; } = default!;
    public string SchemaVersion       { get; init; } = default!;
    public string Payload             { get; init; } = default!;
    public string DestinationExchange { get; init; } = default!;
    public DateTimeOffset CreatedAt   { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int    RetryCount          { get; set; }
    public string Status              { get; set; } = "Pending";   // Pending | Published | Failed
}

// SharedKernel/HealthBooking.SharedKernel/Persistence/OutboxMessageConfiguration.cs
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

### 3.1 PatientService — `healthbooking_patient` Database

#### Domain Entity

```csharp
// PatientService.Domain/Entities/Patient.cs
public sealed class Patient : AuditableEntity
{
    public Guid   Id               { get; private set; } = Guid.NewGuid();
    public string FirstName        { get; private set; } = default!;
    public string LastName         { get; private set; } = default!;
    public DateOnly DateOfBirth    { get; private set; }
    public string ContactEmail     { get; private set; } = default!;
    public string PhoneNumber      { get; private set; } = default!;
    public DateTimeOffset RegistrationDate { get; private set; }

    private Patient() { }   // EF Core constructor

    public static Patient Register(string firstName, string lastName,
        DateOnly dob, string email, string phone)
    {
        return new Patient
        {
            FirstName        = firstName,
            LastName         = lastName,
            DateOfBirth      = dob,
            ContactEmail     = email,
            PhoneNumber      = phone,
            RegistrationDate = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string firstName, string lastName, string phone)
    {
        FirstName   = firstName;
        LastName    = lastName;
        PhoneNumber = phone;
        AddDomainEvent(new PatientProfileUpdatedEvent(Id));
    }
}
```

#### EF Core Fluent Configuration

```csharp
// PatientService.Infrastructure/Persistence/Configurations/PatientConfiguration.cs
public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
               .HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.LastName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.DateOfBirth).HasColumnType("date").IsRequired();
        builder.Property(p => p.ContactEmail).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PhoneNumber).HasMaxLength(30).IsRequired();
        builder.Property(p => p.RegistrationDate)
               .HasDefaultValueSql("SYSDATETIMEOFFSET()").IsRequired();

        // Audit columns — populated exclusively by AuditInterceptor
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedAt);
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(p => p.ContactEmail)
               .IsUnique()
               .HasDatabaseName("UQ_Patients_Email");
    }
}
```

#### DbContext

```csharp
// PatientService.Infrastructure/Persistence/PatientDbContext.cs
public sealed class PatientDbContext(DbContextOptions<PatientDbContext> options)
    : DbContext(options)
{
    public DbSet<Patient>      Patients      => Set<Patient>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PatientConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
```

### 3.2 ProviderService — `healthbooking_provider` Database

#### Domain Entities

```csharp
// ProviderService.Domain/Entities/Provider.cs
public sealed class Provider : AuditableEntity
{
    public Guid   Id            { get; private set; } = Guid.NewGuid();
    public string FirstName     { get; private set; } = default!;
    public string LastName      { get; private set; } = default!;
    public string LicenseNumber { get; private set; } = default!;
    public string ContactEmail  { get; private set; } = default!;

    private readonly List<string> _specializations = [];
    public IReadOnlyList<string> Specializations => _specializations.AsReadOnly();

    private readonly List<AvailabilitySlot> _slots = [];
    public IReadOnlyList<AvailabilitySlot> Slots => _slots.AsReadOnly();

    private Provider() { }

    public static Provider Create(string firstName, string lastName,
        string licenseNumber, string contactEmail)
        => new() { FirstName = firstName, LastName = lastName,
                   LicenseNumber = licenseNumber, ContactEmail = contactEmail };

    public void AddSpecialization(string specialization)
    {
        if (!_specializations.Contains(specialization))
            _specializations.Add(specialization);
    }
}

// ProviderService.Domain/Entities/AvailabilitySlot.cs
public sealed class AvailabilitySlot : AuditableEntity
{
    public Guid   Id              { get; private set; } = Guid.NewGuid();
    public Guid   ProviderId      { get; private set; }
    public DateTimeOffset StartTimeUtc { get; private set; }
    public DateTimeOffset EndTimeUtc   { get; private set; }
    public int    DurationMinutes { get; private set; } = 30;
    public SlotStatus Status      { get; private set; } = SlotStatus.Available;
    public uint   RowVersion      { get; private set; }   // mapped to SQL rowversion

    private AvailabilitySlot() { }

    public static AvailabilitySlot Create(Guid providerId,
        DateTimeOffset start, DateTimeOffset end)
        => new() { ProviderId = providerId, StartTimeUtc = start, EndTimeUtc = end };

    public void MarkBooked()
    {
        if (Status != SlotStatus.Available)
            throw new DomainException("Slot is not available.");
        Status = SlotStatus.Booked;
        AddDomainEvent(new SlotBookedEvent(Id, ProviderId));
    }

    public void Release() => Status = SlotStatus.Available;
}

public enum SlotStatus { Available, Booked, Blocked }
```

#### EF Core Fluent Configurations

```csharp
// ProviderService.Infrastructure/Persistence/Configurations/ProviderConfiguration.cs
public sealed class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.ToTable("Providers");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.LastName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.LicenseNumber).HasMaxLength(100).IsRequired();
        builder.Property(p => p.ContactEmail).HasMaxLength(256).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(p => p.ModifiedAt);
        builder.Property(p => p.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(p => p.LicenseNumber)
               .IsUnique()
               .HasDatabaseName("UQ_Providers_License");

        // Specializations as owned collection stored in a separate table
        builder.OwnsMany(p => p.Specializations, spec =>
        {
            spec.ToTable("ProviderSpecializations");
            spec.WithOwner().HasForeignKey("ProviderId");
            spec.Property<string>("Value").HasColumnName("Specialization").HasMaxLength(100).IsRequired();
            spec.HasKey("ProviderId", "Value");
        });

        builder.HasMany(p => p.Slots)
               .WithOne()
               .HasForeignKey(s => s.ProviderId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

// ProviderService.Infrastructure/Persistence/Configurations/AvailabilitySlotConfiguration.cs
public sealed class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> builder)
    {
        builder.ToTable("AvailabilitySlots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(s => s.ProviderId).IsRequired();
        builder.Property(s => s.StartTimeUtc).IsRequired();
        builder.Property(s => s.EndTimeUtc).IsRequired();
        builder.Property(s => s.DurationMinutes).HasDefaultValue(30).IsRequired();
        builder.Property(s => s.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .HasDefaultValue(SlotStatus.Available)
               .IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(s => s.ModifiedAt);
        builder.Property(s => s.ModifiedBy).HasMaxLength(256);

        // Optimistic concurrency via SQL rowversion
        builder.Property(s => s.RowVersion)
               .IsRowVersion()
               .IsConcurrencyToken();

        builder.HasCheckConstraint("CK_Slots_Duration", "[DurationMinutes] = 30");

        // Filtered unique index — no overlapping slots per provider (non-blocked)
        builder.HasIndex(s => new { s.ProviderId, s.StartTimeUtc })
               .IsUnique()
               .HasFilter("[Status] != 'Blocked'")
               .HasDatabaseName("UX_Slots_Provider_Start");
    }
}
```

#### DbContext

```csharp
// ProviderService.Infrastructure/Persistence/ProviderDbContext.cs
public sealed class ProviderDbContext(DbContextOptions<ProviderDbContext> options)
    : DbContext(options)
{
    public DbSet<Provider>      Providers      => Set<Provider>();
    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProviderConfiguration());
        modelBuilder.ApplyConfiguration(new AvailabilitySlotConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
```

### 3.3 AppointmentService — `healthbooking_appointment` Database

#### Domain Entities

```csharp
// AppointmentService.Domain/Entities/Appointment.cs
public sealed class Appointment : AuditableEntity
{
    public Guid   Id                  { get; private set; } = Guid.NewGuid();
    public Guid   PatientId           { get; private set; }   // denormalized — no cross-DB FK
    public Guid   ProviderId          { get; private set; }   // denormalized
    public Guid   SlotId              { get; private set; }   // denormalized
    public DateTimeOffset ScheduledStartUtc { get; private set; }
    public DateTimeOffset ScheduledEndUtc   { get; private set; }
    public AppointmentStatus Status   { get; private set; } = AppointmentStatus.Pending;
    public string? CancellationReason { get; private set; }
    public Guid   SagaCorrelationId   { get; private set; }

    private Appointment() { }

    public static Appointment Book(Guid patientId, Guid providerId, Guid slotId,
        DateTimeOffset start, DateTimeOffset end, Guid sagaCorrelationId)
        => new()
        {
            PatientId = patientId, ProviderId = providerId, SlotId = slotId,
            ScheduledStartUtc = start, ScheduledEndUtc = end,
            SagaCorrelationId = sagaCorrelationId
        };

    public void Confirm()  { Status = AppointmentStatus.Confirmed; AddDomainEvent(new AppointmentConfirmedEvent(Id)); }
    public void Cancel(string reason) { Status = AppointmentStatus.Cancelled; CancellationReason = reason; }
}

public enum AppointmentStatus { Pending, Confirmed, Cancelled, Completed }

// AppointmentService.Domain/Entities/BookingIdempotencyKey.cs
public sealed class BookingIdempotencyKey
{
    public Guid Id            { get; init; }   // the client-supplied idempotency key
    public Guid AppointmentId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

#### EF Core Fluent Configurations

```csharp
// AppointmentService.Infrastructure/Persistence/Configurations/AppointmentConfiguration.cs
public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(a => a.PatientId).IsRequired();
        builder.Property(a => a.ProviderId).IsRequired();
        builder.Property(a => a.SlotId).IsRequired();
        builder.Property(a => a.ScheduledStartUtc).IsRequired();
        builder.Property(a => a.ScheduledEndUtc).IsRequired();
        builder.Property(a => a.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .HasDefaultValue(AppointmentStatus.Pending)
               .IsRequired();
        builder.Property(a => a.CancellationReason).HasMaxLength(500);
        builder.Property(a => a.SagaCorrelationId).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(a => a.ModifiedAt);
        builder.Property(a => a.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(a => a.PatientId)
               .HasDatabaseName("IX_Appointments_PatientId");
        builder.HasIndex(a => new { a.ProviderId, a.Status })
               .HasDatabaseName("IX_Appointments_ProviderId_Status");
        builder.HasIndex(a => a.SlotId)
               .HasDatabaseName("IX_Appointments_SlotId");
    }
}

// AppointmentService.Infrastructure/Persistence/Configurations/BookingIdempotencyKeyConfiguration.cs
public sealed class BookingIdempotencyKeyConfiguration : IEntityTypeConfiguration<BookingIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<BookingIdempotencyKey> builder)
    {
        builder.ToTable("BookingIdempotencyKeys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.AppointmentId).IsRequired();
        builder.Property(k => k.CreatedAt)
               .HasDefaultValueSql("SYSDATETIMEOFFSET()").IsRequired();
    }
}
```

#### DbContext

```csharp
// AppointmentService.Infrastructure/Persistence/AppointmentDbContext.cs
public sealed class AppointmentDbContext(DbContextOptions<AppointmentDbContext> options)
    : DbContext(options)
{
    public DbSet<Appointment>          Appointments          => Set<Appointment>();
    public DbSet<BookingIdempotencyKey> BookingIdempotencyKeys => Set<BookingIdempotencyKey>();
    public DbSet<OutboxMessage>        OutboxMessages        => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AppointmentConfiguration());
        modelBuilder.ApplyConfiguration(new BookingIdempotencyKeyConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
```

### 3.4 NotificationService — `healthbooking_notification` Database

#### Domain Entities

```csharp
// NotificationService.Domain/Entities/NotificationRecord.cs
public sealed class NotificationRecord
{
    public Guid   Id                  { get; private set; } = Guid.NewGuid();
    public Guid   AppointmentId       { get; private set; }
    public Guid   CorrelationId       { get; private set; }
    public Guid   RecipientPatientId  { get; private set; }
    public NotificationChannel Channel { get; private set; } = NotificationChannel.Email;
    public string TemplateType        { get; private set; } = default!;
    public NotificationStatus Status  { get; private set; } = NotificationStatus.Pending;
    public int    RetryCount          { get; private set; }
    public DateTimeOffset? LastAttemptedAt { get; private set; }
    public DateTimeOffset? DeliveredAt     { get; private set; }
    public string? FailureReason      { get; private set; }
    public DateTimeOffset CreatedAt   { get; private set; } = DateTimeOffset.UtcNow;

    private NotificationRecord() { }

    public static NotificationRecord Create(Guid appointmentId, Guid correlationId,
        Guid recipientPatientId, string templateType)
        => new() { AppointmentId = appointmentId, CorrelationId = correlationId,
                   RecipientPatientId = recipientPatientId, TemplateType = templateType };

    public void MarkDelivered()
    {
        Status = NotificationStatus.Delivered;
        DeliveredAt = DateTimeOffset.UtcNow;
    }

    public void RecordFailure(string reason)
    {
        RetryCount++;
        LastAttemptedAt = DateTimeOffset.UtcNow;
        FailureReason = reason;
        if (RetryCount >= 3) Status = NotificationStatus.Failed;
    }
}

public enum NotificationChannel { Email, Sms }
public enum NotificationStatus  { Pending, Delivered, Failed }

// NotificationService.Domain/Entities/ProcessedEvent.cs
public sealed class ProcessedEvent
{
    public Guid   MessageId   { get; init; }   // MassTransit message ID
    public Guid   RecordId    { get; init; }   // NotificationRecord.Id
    public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

#### EF Core Fluent Configurations

```csharp
// NotificationService.Infrastructure/Persistence/Configurations/NotificationRecordConfiguration.cs
public sealed class NotificationRecordConfiguration : IEntityTypeConfiguration<NotificationRecord>
{
    public void Configure(EntityTypeBuilder<NotificationRecord> builder)
    {
        builder.ToTable("NotificationRecords");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(n => n.AppointmentId).IsRequired();
        builder.Property(n => n.CorrelationId).IsRequired();
        builder.Property(n => n.RecipientPatientId).IsRequired();
        builder.Property(n => n.Channel)
               .HasConversion<string>().HasMaxLength(20)
               .HasDefaultValue(NotificationChannel.Email).IsRequired();
        builder.Property(n => n.TemplateType).HasMaxLength(100).IsRequired();
        builder.Property(n => n.Status)
               .HasConversion<string>().HasMaxLength(20)
               .HasDefaultValue(NotificationStatus.Pending).IsRequired();
        builder.Property(n => n.RetryCount).HasDefaultValue(0);
        builder.Property(n => n.LastAttemptedAt);
        builder.Property(n => n.DeliveredAt);
        builder.Property(n => n.FailureReason).HasMaxLength(500);
        builder.Property(n => n.CreatedAt).IsRequired();

        builder.HasIndex(n => n.CorrelationId)
               .HasDatabaseName("IX_Notifications_CorrelationId");
        builder.HasIndex(n => new { n.Status, n.LastAttemptedAt })
               .HasDatabaseName("IX_Notifications_Status_LastAttemptedAt");
    }
}

// NotificationService.Infrastructure/Persistence/Configurations/ProcessedEventConfiguration.cs
public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("ProcessedEvents");
        builder.HasKey(e => e.MessageId);
        builder.Property(e => e.RecordId).IsRequired();
        builder.Property(e => e.ProcessedAt)
               .HasDefaultValueSql("SYSDATETIMEOFFSET()").IsRequired();
    }
}
```

#### DbContext

```csharp
// NotificationService.Infrastructure/Persistence/NotificationDbContext.cs
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options)
{
    public DbSet<NotificationRecord> NotificationRecords => Set<NotificationRecord>();
    public DbSet<ProcessedEvent>     ProcessedEvents     => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new NotificationRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
    }
}
```

### 3.5 Migrations Strategy

```
# Per service — run from each Infrastructure project directory
dotnet ef migrations add InitialCreate \
    --project src/Services/PatientService/PatientService.Infrastructure \
    --startup-project src/Services/PatientService/PatientService.API

# Apply on startup (Program.cs)
await using var scope = app.Services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<PatientDbContext>();
await db.Database.MigrateAsync();
```

> All four services follow the same pattern. Migration files live in `Infrastructure/Persistence/Migrations/`.

---

## 4. API Contracts — REST Endpoints

All endpoints are exposed through the API Gateway at `http://localhost:5000`. Services listen on their own ports internally but are not reachable from outside the Docker network.

### 4.1 PatientService (`/api/patients`)

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/patients/register` | None | Register new patient; issues OIDC credentials |
| `GET` | `/api/patients/{id}` | Bearer (patient or admin) | Retrieve patient profile |
| `PUT` | `/api/patients/{id}` | Bearer (same patient) | Update demographic profile |
| `GET` | `/api/patients/me` | Bearer | Shortcut: retrieve self profile from JWT sub |

**POST /api/patients/register — Request**:
```json
{
  "firstName": "Jane",
  "lastName": "Smith",
  "dateOfBirth": "1990-06-15",
  "contactEmail": "jane.smith@example.com",
  "phoneNumber": "+1-555-0100"
}
```
**Response 201**:
```json
{
  "patientId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "message": "Registration successful.",
  "accessToken": "<JWT>"
}
```
**Response 409** (duplicate email):
```json
{ "type": "conflict", "detail": "Email address is already registered." }
```

### 4.2 ProviderService (`/api/providers`)

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/providers` | Bearer (admin) | Create provider profile |
| `GET` | `/api/providers/{id}` | Bearer | Get provider details |
| `GET` | `/api/providers/search` | Bearer | Search by specialization + date range |
| `POST` | `/api/providers/{id}/availability` | Bearer (admin/provider) | Define availability window |
| `DELETE` | `/api/providers/{id}/availability/{windowId}` | Bearer (admin/provider) | Cancel availability window |
| `GET` | `/api/providers/{id}/slots` | Bearer | List available slots (from cache) |

**GET /api/providers/search — Query Params**:
```
?specialization=Cardiology&from=2026-04-07T09:00:00Z&to=2026-04-07T17:00:00Z
```

**POST /api/providers/{id}/availability — Request**:
```json
{
  "startDateUtc": "2026-04-07T09:00:00Z",
  "endDateUtc": "2026-04-07T17:00:00Z",
  "recurrence": "Weekly",
  "daysOfWeek": ["Monday", "Tuesday", "Wednesday", "Thursday"]
}
```

### 4.3 AppointmentService (`/api/appointments`)

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/api/appointments` | Bearer (patient) | Book appointment |
| `GET` | `/api/appointments/{id}` | Bearer | Get appointment details |
| `GET` | `/api/appointments/me` | Bearer (patient) | Get own appointments |
| `DELETE` | `/api/appointments/{id}` | Bearer (patient/admin) | Cancel appointment |
| `PUT` | `/api/appointments/{id}/reschedule` | Bearer (patient) | Reschedule to new slot |

**POST /api/appointments — Request**:
```json
{
  "patientId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "slotId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "idempotencyKey": "8d1f4c3a-1234-5678-abcd-000000000001"
}
```
**Response 201**:
```json
{
  "appointmentId": "1b9d6bcd-bbfd-4b2d-9b5d-ab8dfbbd4bed",
  "referenceNumber": "APT-20260407-00042",
  "status": "Confirmed",
  "scheduledStartUtc": "2026-04-07T09:00:00Z",
  "scheduledEndUtc": "2026-04-07T09:30:00Z",
  "sagaCorrelationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
}
```
**Response 409** (slot taken concurrently):
```json
{ "type": "conflict", "detail": "The selected slot is no longer available." }
```

**PUT /api/appointments/{id}/reschedule — Request**:
```json
{ "newSlotId": "new-slot-guid" }
```

---

## 5. gRPC Contracts

gRPC is used for synchronous inter-service reads (AppointmentService → ProviderService; AppointmentService → PatientService). Contracts live in `HealthBooking.Contracts/Protos/`.

### 5.1 ProviderService gRPC

```protobuf
// protos/provider.proto
syntax = "proto3";
option csharp_namespace = "HealthBooking.Contracts.Grpc.Provider";

service ProviderGrpc {
  rpc GetSlotById (GetSlotRequest) returns (SlotResponse);
  rpc LockSlot (LockSlotRequest) returns (LockSlotResponse);
  rpc ReleaseSlot (ReleaseSlotRequest) returns (ReleaseSlotResponse);
}

message GetSlotRequest  { string slot_id = 1; }
message LockSlotRequest { string slot_id = 1; string correlation_id = 2; }
message LockSlotResponse {
  bool success = 1;
  string slot_id = 2;
  string conflict_reason = 3;   // populated on failure
}
message ReleaseSlotRequest { string slot_id = 1; string correlation_id = 2; }
message ReleaseSlotResponse { bool success = 1; }

message SlotResponse {
  string slot_id = 1;
  string provider_id = 2;
  string start_time_utc = 3;
  string end_time_utc = 4;
  string status = 5;
}
```

### 5.2 PatientService gRPC

```protobuf
// protos/patient.proto
syntax = "proto3";
option csharp_namespace = "HealthBooking.Contracts.Grpc.Patient";

service PatientGrpc {
  rpc GetPatientById (GetPatientRequest) returns (PatientResponse);
}

message GetPatientRequest { string patient_id = 1; }
message PatientResponse {
  string patient_id = 1;
  string full_name = 2;          // PII — masked in telemetry, not cached downstream
  string contact_email = 3;      // PII — masked in telemetry
  bool exists = 4;
}
```

> **Security note**: gRPC responses containing PII are consumed only within the AppointmentService to validate patient existence; they are never stored in AppointmentService's database beyond the foreign-key reference.

---

## 6. Message Broker Event Contracts — RabbitMQ / MassTransit

All events are defined in `HealthBooking.Contracts` and published through MassTransit. Exchange topology uses topic exchanges; each consumer declares its own queue with a dead-letter queue.

### 6.1 Event Contracts

```csharp
namespace HealthBooking.Contracts.Appointments.V1;

// Published by AppointmentService when a booking is confirmed
public record V1_AppointmentBookedEvent
{
    public Guid EventId { get; init; }
    public string EventType => "V1_AppointmentBooked";
    public string SchemaVersion => "1.0";
    public string SourceService => "AppointmentService";
    public Guid CorrelationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    // --- Payload ---
    public Guid AppointmentId { get; init; }
    public Guid PatientId { get; init; }
    public Guid ProviderId { get; init; }
    public Guid SlotId { get; init; }
    public DateTimeOffset ScheduledStartUtc { get; init; }
    public DateTimeOffset ScheduledEndUtc { get; init; }
    public string PatientEmail { get; init; } = string.Empty;  // needed for notification routing only
}

public record V1_AppointmentCancelledEvent
{
    public Guid EventId { get; init; }
    public string EventType => "V1_AppointmentCancelled";
    public string SchemaVersion => "1.0";
    public string SourceService => "AppointmentService";
    public Guid CorrelationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid AppointmentId { get; init; }
    public Guid PatientId { get; init; }
    public Guid SlotId { get; init; }
    public string CancellationReason { get; init; } = string.Empty;
    public string PatientEmail { get; init; } = string.Empty;
}

public record V1_AppointmentRescheduledEvent
{
    public Guid EventId { get; init; }
    public string EventType => "V1_AppointmentRescheduled";
    public string SchemaVersion => "1.0";
    public string SourceService => "AppointmentService";
    public Guid CorrelationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid AppointmentId { get; init; }
    public Guid PatientId { get; init; }
    public Guid OldSlotId { get; init; }
    public Guid NewSlotId { get; init; }
    public DateTimeOffset NewScheduledStartUtc { get; init; }
    public DateTimeOffset NewScheduledEndUtc { get; init; }
    public string PatientEmail { get; init; } = string.Empty;
}

// Published by ProviderService (or AppointmentService) when a slot is released
public record V1_SlotReleasedEvent
{
    public Guid EventId { get; init; }
    public string EventType => "V1_SlotReleased";
    public string SchemaVersion => "1.0";
    public string SourceService => "AppointmentService";
    public Guid CorrelationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid SlotId { get; init; }
    public Guid ProviderId { get; init; }
}
```

### 6.2 MassTransit Configuration (per service)

```csharp
// AppointmentService.Infrastructure — DI wiring
services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    // Add consumers — AppointmentService has no inbound consumers in Week 3
    // (slot status feedback consumed by ProviderService)

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(config["RabbitMq:Host"], "/", h =>
        {
            h.Username(config["RabbitMq:Username"]);
            h.Password(config["RabbitMq:Password"]);
        });
        cfg.ConfigureEndpoints(ctx);
        // Dead-letter queue: messages failing after 3 retries
        cfg.UseMessageRetry(r =>
            r.Exponential(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
        cfg.UseDeadLetterQueue();
    });
});
```

```csharp
// ProviderService.Infrastructure — inbound consumer
public sealed class AppointmentBookedConsumer : IConsumer<V1_AppointmentBookedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    // Constructor injection via IServiceScopeFactory (avoid scoped service in singleton)

    public async Task Consume(ConsumeContext<V1_AppointmentBookedEvent> context)
    {
        // Idempotency: check ProcessedEvents table by EventId
        // Mark slot as Booked via ISlotRepository
        // Invalidate Redis cache for provider slot list
    }
}
```

### 6.3 Exchange / Queue Topology

```
Exchange: appointment.booked.v1     (fanout)
  └── Queue: provider-service.slot-update             → ProviderService consumer
  └── Queue: notification-service.appointment-booked  → NotificationService consumer
  └── DLQ:  appointment.booked.v1.dead-letter

Exchange: appointment.cancelled.v1  (fanout)
  └── Queue: provider-service.slot-release            → ProviderService consumer
  └── Queue: notification-service.appointment-cancelled
  └── DLQ:  appointment.cancelled.v1.dead-letter

Exchange: appointment.rescheduled.v1 (fanout)
  └── Queue: notification-service.appointment-rescheduled
```

---

## 7. YARP API Gateway Configuration

The Gateway project (`HealthBooking.ApiGateway`) wraps YARP with JWT validation middleware and a rate limiter.

### 7.1 appsettings.json — Routes & Clusters

```json
{
  "ReverseProxy": {
    "Routes": {
      "patient-route": {
        "ClusterId": "patient-cluster",
        "AuthorizationPolicy": "JwtBearer",
        "Match": { "Path": "/api/patients/{**catch-all}" },
        "Transforms": [
          { "RequestHeader": "X-Internal-Service", "Set": "ApiGateway" }
        ]
      },
      "patient-register-route": {
        "ClusterId": "patient-cluster",
        "AuthorizationPolicy": "Anonymous",
        "Match": { "Path": "/api/patients/register", "Methods": ["POST"] }
      },
      "provider-route": {
        "ClusterId": "provider-cluster",
        "AuthorizationPolicy": "JwtBearer",
        "Match": { "Path": "/api/providers/{**catch-all}" }
      },
      "appointment-route": {
        "ClusterId": "appointment-cluster",
        "AuthorizationPolicy": "JwtBearer",
        "Match": { "Path": "/api/appointments/{**catch-all}" }
      }
    },
    "Clusters": {
      "patient-cluster": {
        "Destinations": {
          "patient-service": { "Address": "http://patient-service:5001/" }
        },
        "HealthCheck": {
          "Active": { "Enabled": true, "Interval": "00:00:10", "Path": "/health/ready" }
        }
      },
      "provider-cluster": {
        "Destinations": {
          "provider-service": { "Address": "http://provider-service:5002/" }
        },
        "HealthCheck": {
          "Active": { "Enabled": true, "Interval": "00:00:10", "Path": "/health/ready" }
        }
      },
      "appointment-cluster": {
        "Destinations": {
          "appointment-service": { "Address": "http://appointment-service:5003/" }
        },
        "HealthCheck": {
          "Active": { "Enabled": true, "Interval": "00:00:10", "Path": "/health/ready" }
        }
      }
    }
  }
}
```

### 7.2 Gateway Program.cs Wiring

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServer:Authority"];
        options.Audience = "healthbooking-api";
        options.RequireHttpsMetadata = false;   // dev only; TLS in staging
        options.TokenValidationParameters.ValidateIssuer = true;
    });

builder.Services.AddRateLimiter(options =>
{
    options.AddSlidingWindowLimiter("gateway-global", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 6;
        opt.PermitLimit = 300;
        opt.QueueLimit = 0;
    });
});

// Correlation ID propagation middleware
builder.Services.AddCorrelationId();

// YARP
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseCorrelationId();
app.MapReverseProxy();
```

### 7.3 Correlation ID Middleware

```csharp
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString();
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        context.Items["CorrelationId"] = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
```

---

## 8. IdentityServer OIDC Configuration

Duende IdentityServer runs as a separate container. Services use OIDC discovery (`/.well-known/openid-configuration`) exclusively — no hardcoded JWKS URLs.

### 8.1 Config.cs — Clients, Scopes, Resources

```csharp
public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email()
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("healthbooking-api", "HealthBooking Full API"),
        new ApiScope("patient:read"),
        new ApiScope("patient:write"),
        new ApiScope("provider:read"),
        new ApiScope("provider:write"),
        new ApiScope("appointment:read"),
        new ApiScope("appointment:write")
    ];

    public static IEnumerable<ApiResource> ApiResources =>
    [
        new ApiResource("healthbooking-api")
        {
            Scopes = { "healthbooking-api", "patient:read", "patient:write",
                       "provider:read", "provider:write", "appointment:read", "appointment:write" }
        }
    ];

    public static IEnumerable<Client> Clients =>
    [
        // Machine-to-machine: ApiGateway → services
        new Client
        {
            ClientId = "api-gateway",
            ClientSecrets = { new Secret("api-gateway-secret".Sha256()) },
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            AllowedScopes = { "healthbooking-api" }
        },
        // Interactive patient client (PKCE)
        new Client
        {
            ClientId = "patient-spa",
            AllowedGrantTypes = GrantTypes.Code,
            RequireClientSecret = false,
            RequirePkce = true,
            RedirectUris = { "http://localhost:3000/callback" },
            PostLogoutRedirectUris = { "http://localhost:3000" },
            AllowedScopes = { "openid", "profile", "email",
                              "patient:read", "patient:write", "appointment:read", "appointment:write" },
            AllowOfflineAccess = true
        },
        // Admin client
        new Client
        {
            ClientId = "admin-client",
            ClientSecrets = { new Secret("admin-secret".Sha256()) },
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            AllowedScopes = { "healthbooking-api", "provider:write" }
        }
    ];
}
```

### 8.2 JWT Validation Per Service

Every service independently validates the JWT (defense-in-depth; gateway is not the only trust boundary):

```csharp
// Applied in every service's Program.cs
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["IdentityServer:Authority"];
        options.Audience = "healthbooking-api";
        options.RequireHttpsMetadata = false;   // dev; true in production
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PatientWrite", p => p.RequireScope("patient:write"));
    options.AddPolicy("ProviderWrite", p => p.RequireScope("provider:write"));
    options.AddPolicy("AppointmentWrite", p => p.RequireScope("appointment:write"));
});
```

---

## 9. Redis Caching Strategy

### 9.1 Cache Keys & TTL

| Cache Entry | Key Pattern | TTL | Invalidated By |
|---|---|---|---|
| Provider availability slots | `provider:slots:{providerId}:{dateUtc}` | 60 s | `V1_AppointmentBookedEvent`, `V1_SlotReleasedEvent`, slot CRUD write |
| Provider profile | `provider:profile:{providerId}` | 300 s | Provider profile update |
| Provider search by specialization | `provider:search:{specialization}:{fromDate}:{toDate}` | 60 s | Any provider slot write |

### 9.2 Cache Abstraction (Application Layer)

The Application layer depends only on `IDistributedCache` — no direct StackExchange.Redis calls:

```csharp
// Application/Interfaces/ICacheService.cs
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPatternAsync(string pattern, CancellationToken ct = default);
}
```

```csharp
// Infrastructure/Caching/RedisCacheService.cs
public sealed class RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    : ICacheService
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var bytes = await cache.GetAsync(key, ct);
        return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await cache.SetAsync(key, bytes, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
        => await cache.RemoveAsync(key, ct);
}
```

### 9.3 Cache-Aside in Query Handler

```csharp
// ProviderService — GetProviderSlotsQueryHandler
public async Task<List<SlotDto>> Handle(GetProviderSlotsQuery request, CancellationToken ct)
{
    var cacheKey = $"provider:slots:{request.ProviderId}:{request.DateUtc:yyyyMMdd}";
    var cached = await _cache.GetAsync<List<SlotDto>>(cacheKey, ct);
    if (cached is not null)
        return cached;

    var slots = await _slotRepository.GetAvailableSlotsByProviderAndDateAsync(
        request.ProviderId, request.DateUtc, ct);
    var dtos = slots.Select(SlotDto.FromDomain).ToList();
    await _cache.SetAsync(cacheKey, dtos, TimeSpan.FromSeconds(60), ct);
    return dtos;
}
```

### 9.4 Cache Invalidation on Write Event

```csharp
// ProviderService — AppointmentBookedConsumer
public async Task Consume(ConsumeContext<V1_AppointmentBookedEvent> context)
{
    // ... update slot status in DB ...

    // Eager invalidation (not lazy)
    var cacheKey = $"provider:slots:{msg.ProviderId}:{msg.ScheduledStartUtc:yyyyMMdd}";
    await _cache.RemoveAsync(cacheKey);

    // Also invalidate search cache entries for this provider's specialization
    await _cache.RemoveByPatternAsync($"provider:search:*");
}
```

---

## 10. Outbox Pattern Implementation Design

### 10.1 Flow Diagram

```
┌─────────────────────────────────────────────────────────┐
│                  BookAppointmentHandler                  │
│                                                          │
│  BEGIN TRANSACTION                                       │
│    1. Insert Appointment record                          │
│    2. Insert OutboxMessage record (same DbContext)       │
│  COMMIT TRANSACTION   ← atomic                          │
└─────────────────────────────┬───────────────────────────┘
                              │
                   (background hosted service)
                              │
┌─────────────────────────────▼───────────────────────────┐
│                  OutboxProcessor (IHostedService)         │
│                                                          │
│  LOOP every 5 seconds:                                   │
│    1. SELECT TOP 50 FROM OutboxMessages WHERE Status='Pending' │
│       ORDER BY CreatedAt ASC                             │
│    2. For each record:                                   │
│       a. Deserialize Payload → typed event               │
│       b. Publish to RabbitMQ via MassTransit IBus        │
│       c. UPDATE OutboxMessage SET Status='Published',    │
│              PublishedAt=SYSDATETIMEOFFSET()             │
│       d. On failure: increment RetryCount                │
│              if RetryCount >= 3: Status='Failed'         │
└─────────────────────────────────────────────────────────┘
```

### 10.2 OutboxInterceptor — Auto-populate on SaveChanges

```csharp
// Infrastructure/Persistence/Interceptors/OutboxPublishingInterceptor.cs
public sealed class OutboxPublishingInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct)
    {
        var dbContext = eventData.Context!;

        // Collect domain events from all aggregate roots being tracked
        var aggregates = dbContext.ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                dbContext.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = domainEvent.EventType,
                    SchemaVersion = domainEvent.SchemaVersion,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    DestinationExchange = ResolveExchangeName(domainEvent.EventType),
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            aggregate.ClearDomainEvents();
        }

        return await base.SavingChangesAsync(eventData, result, ct);
    }

    private static string ResolveExchangeName(string eventType) => eventType switch
    {
        "V1_AppointmentBooked"    => "appointment.booked.v1",
        "V1_AppointmentCancelled" => "appointment.cancelled.v1",
        "V1_AppointmentRescheduled" => "appointment.rescheduled.v1",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null)
    };
}
```

### 10.3 OutboxProcessor Hosted Service

```csharp
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessBatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var messages = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var msg in messages)
        {
            try
            {
                var eventType = Type.GetType(msg.EventType)!;
                var payload = JsonSerializer.Deserialize(msg.Payload, eventType)!;
                var sendEndpoint = await bus.GetSendEndpoint(new Uri($"exchange:{msg.DestinationExchange}"));
                await sendEndpoint.Send(payload, eventType, ct);

                msg.Status = OutboxStatus.Published;
                msg.PublishedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                if (msg.RetryCount >= 3)
                    msg.Status = OutboxStatus.Failed;
                logger.LogError(ex, "Outbox publish failed for {MessageId} (attempt {Retry})",
                    msg.Id, msg.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
```

---

## 11. CQRS / MediatR Command & Query Structure

### 11.1 Command / Query Inventory Per Service

#### PatientService

| Type | Name | Handler |
|---|---|---|
| Command | `RegisterPatientCommand` | `RegisterPatientCommandHandler` |
| Command | `UpdatePatientProfileCommand` | `UpdatePatientProfileCommandHandler` |
| Query | `GetPatientByIdQuery` | `GetPatientByIdQueryHandler` |
| Query | `GetPatientByEmailQuery` | `GetPatientByEmailQueryHandler` |

#### ProviderService

| Type | Name | Handler |
|---|---|---|
| Command | `CreateProviderCommand` | `CreateProviderCommandHandler` |
| Command | `UpdateProviderProfileCommand` | `UpdateProviderProfileCommandHandler` |
| Command | `DefineAvailabilityCommand` | `DefineAvailabilityCommandHandler` |
| Command | `CancelAvailabilityWindowCommand` | `CancelAvailabilityWindowCommandHandler` |
| Query | `GetProviderByIdQuery` | `GetProviderByIdQueryHandler` |
| Query | `SearchProvidersBySpecializationQuery` | `SearchProvidersBySpecializationQueryHandler` |
| Query | `GetProviderSlotsQuery` | `GetProviderSlotsQueryHandler` |

#### AppointmentService

| Type | Name | Handler |
|---|---|---|
| Command | `BookAppointmentCommand` | `BookAppointmentCommandHandler` |
| Command | `CancelAppointmentCommand` | `CancelAppointmentCommandHandler` |
| Command | `RescheduleAppointmentCommand` | `RescheduleAppointmentCommandHandler` |
| Command | `MarkAppointmentCompletedCommand` | `MarkAppointmentCompletedCommandHandler` |
| Command | `MarkAppointmentNoShowCommand` | `MarkAppointmentNoShowCommandHandler` |
| Query | `GetAppointmentByIdQuery` | `GetAppointmentByIdQueryHandler` |
| Query | `GetPatientAppointmentsQuery` | `GetPatientAppointmentsQueryHandler` |

#### NotificationService

| Type | Name | Handler |
|---|---|---|
| Command | `ProcessAppointmentBookedNotificationCommand` | `ProcessBookedNotificationHandler` |
| Command | `ProcessAppointmentCancelledNotificationCommand` | `ProcessCancelledNotificationHandler` |
| Command | `ProcessAppointmentRescheduledNotificationCommand` | `ProcessRescheduledNotificationHandler` |
| Command | `SendReminderNotificationCommand` | `SendReminderNotificationHandler` |

### 11.2 Command Structure Example

```csharp
// AppointmentService.Application/Commands/BookAppointment/
//   BookAppointmentCommand.cs
public sealed record BookAppointmentCommand(
    Guid PatientId,
    Guid SlotId,
    Guid IdempotencyKey
) : IRequest<BookAppointmentResult>;

public sealed record BookAppointmentResult(
    Guid AppointmentId,
    string ReferenceNumber,
    string Status,
    DateTimeOffset ScheduledStartUtc,
    Guid SagaCorrelationId
);

//   BookAppointmentCommandValidator.cs  (FluentValidation via MediatR pipeline)
public sealed class BookAppointmentCommandValidator
    : AbstractValidator<BookAppointmentCommand>
{
    public BookAppointmentCommandValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.SlotId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}

//   BookAppointmentCommandHandler.cs
public sealed class BookAppointmentCommandHandler(
    IAppointmentRepository appointmentRepo,
    IProviderSlotGrpcClient slotClient,
    IPatientGrpcClient patientClient,
    ILogger<BookAppointmentCommandHandler> logger)
    : IRequestHandler<BookAppointmentCommand, BookAppointmentResult>
{
    public async Task<BookAppointmentResult> Handle(
        BookAppointmentCommand request, CancellationToken ct)
    {
        // 1. Check idempotency key (idempotent rebooking)
        var existing = await appointmentRepo.FindByIdempotencyKeyAsync(request.IdempotencyKey, ct);
        if (existing is not null)
            return BookAppointmentResult.FromExisting(existing);

        // 2. Verify patient exists (gRPC — read-only)
        var patient = await patientClient.GetPatientByIdAsync(request.PatientId, ct);
        if (!patient.Exists)
            throw new NotFoundException($"Patient {request.PatientId} not found.");

        // 3. Lock slot via gRPC (optimistic — uses rowversion check in ProviderService)
        var lockResult = await slotClient.LockSlotAsync(request.SlotId, request.IdempotencyKey, ct);
        if (!lockResult.Success)
            throw new ConflictException("The selected slot is no longer available.");

        // 4. Create appointment aggregate + outbox entry (same transaction in AppointmentDbContext)
        var appointment = Appointment.Book(request.PatientId, request.ProviderId,
            request.SlotId, lockResult.StartTimeUtc, lockResult.EndTimeUtc);

        await appointmentRepo.AddAsync(appointment, ct);
        // OutboxPublishingInterceptor collects domain events from aggregate automatically
        await appointmentRepo.SaveChangesAsync(ct);

        logger.LogInformation("Appointment {AppointmentId} booked for patient {PatientId}",
            appointment.Id, request.PatientId);

        return new BookAppointmentResult(appointment.Id.Value, appointment.ReferenceNumber,
            appointment.Status.ToString(), appointment.ScheduledStartUtc, appointment.SagaCorrelationId.Value);
    }
}
```

### 11.3 MediatR Pipeline Behaviors

```csharp
// Registration order matters
services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
});

// LoggingBehavior: logs command name, latency, correlation ID
// ValidationBehavior: FluentValidation; throws ValidationException on failure
// PerformanceBehavior: logs warning if handler exceeds 500 ms
```

---

## 12. EF Core Interceptors — Auditing

### 12.1 AuditInterceptor

The interceptor is registered globally per DbContext and automatically populates all four audit fields — manual assignment in handlers is a constitution violation.

```csharp
public sealed class AuditInterceptor(ICurrentUserService currentUser) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct)
    {
        var context = eventData.Context!;
        var now = DateTimeOffset.UtcNow;
        var actor = currentUser.UserId ?? "system";

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(AuditableEntity.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).CurrentValue = actor;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(AuditableEntity.ModifiedAt)).CurrentValue = now;
                    entry.Property(nameof(AuditableEntity.ModifiedBy)).CurrentValue = actor;
                    // Prevent accidental overwrite of immutable created fields
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                    break;
            }
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}
```

```csharp
// Infrastructure/Services/CurrentUserService.cs
public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    public string? UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
```

### 12.2 DbContext Registration

```csharp
// e.g. AppointmentDbContext
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder.AddInterceptors(
        _serviceProvider.GetRequiredService<AuditInterceptor>(),
        _serviceProvider.GetRequiredService<OutboxPublishingInterceptor>()
    );
}
```

---

## 13. Polly Resilience Policies

All HTTP and gRPC clients are registered via `IHttpClientFactory` with a shared `ResiliencePipeline` using `Polly.Extensions`.

### 13.1 Standard Resilience Pipeline

```csharp
// SharedKernel extension method — used by every service
public static IHttpClientBuilder AddHealthBookingResiliencePipeline(
    this IHttpClientBuilder builder, string clientName)
{
    return builder.AddResilienceHandler($"{clientName}-pipeline", pipeline =>
    {
        // Timeout: 10 seconds per attempt
        pipeline.AddTimeout(TimeSpan.FromSeconds(10));

        // Retry: max 3 attempts, exponential back-off 1s → 2s → 4s + ±500ms jitter
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            DelayGenerator = args => new ValueTask<TimeSpan?>(
                TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber))
                + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)))
        });

        // Circuit Breaker: 5 failures in 30 s window → 30 s open
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 5,
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = args =>
            {
                Log.Warning("Circuit breaker OPENED for {ClientName}: {Reason}",
                    clientName, args.Outcome.Exception?.Message);
                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                Log.Information("Circuit breaker CLOSED for {ClientName}", clientName);
                return ValueTask.CompletedTask;
            }
        });
    });
}
```

### 13.2 Client Registration (AppointmentService)

```csharp
builder.Services
    .AddGrpcClient<ProviderGrpc.ProviderGrpcClient>(options =>
        options.Address = new Uri(builder.Configuration["Grpc:ProviderService:Address"]!))
    .AddHealthBookingResiliencePipeline("provider-grpc");

builder.Services
    .AddGrpcClient<PatientGrpc.PatientGrpcClient>(options =>
        options.Address = new Uri(builder.Configuration["Grpc:PatientService:Address"]!))
    .AddHealthBookingResiliencePipeline("patient-grpc");
```

### 13.3 Circuit Breaker Logging in Telemetry

Circuit breaker state changes are automatically emitted by the `OnOpened`/`OnClosed` delegates above as structured Serilog entries. The callbacks receive the correlation ID via the `ILogger` ambient scope injected through `LoggingBehavior`.

---

## 14. OpenTelemetry + Jaeger Setup

### 14.1 Instrumentation Per Service

```csharp
// Shared extension: applied in every service's Program.cs
public static WebApplicationBuilder AddHealthBookingObservability(
    this WebApplicationBuilder builder, string serviceName)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(opts =>
            {
                opts.RecordException = true;
                // Mask PII from route values (patient IDs in path)
                opts.EnrichWithHttpRequest = (activity, request) =>
                {
                    activity.SetTag("http.correlation_id",
                        request.Headers["X-Correlation-Id"].ToString());
                };
            })
            .AddGrpcClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(opts =>
            {
                opts.SetDbStatementForText = false;  // prevent SQL (with PII params) from appearing
                opts.SetDbStatementForStoredProcedure = false;
            })
            .AddSource("MassTransit")   // MassTransit activity source
            .AddOtlpExporter(o =>
                o.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!)))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(o =>
                o.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!)));

    // Serilog
    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ServiceName", serviceName)
        .Destructure.ByTransforming<Patient>(p => new { p.Id })  // mask PII at log level
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.File(new JsonFormatter(),
            $"logs/{serviceName}-.json", rollingInterval: RollingInterval.Day));

    return builder;
}
```

### 14.2 Correlation ID Propagation in MassTransit

```csharp
// MassTransit automatically propagates OpenTelemetry W3C trace context.
// Additionally, embed CorrelationId in every message envelope:
cfg.ConfigureJsonSerializerOptions(opts => opts.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
cfg.UsePublishFilter(typeof(CorrelationIdPublishFilter<>), context);
cfg.UseConsumeFilter(typeof(CorrelationIdConsumeFilter<>), context);
```

### 14.3 PII Masking Policy

```csharp
// Serilog destructuring policy applied at bootstrap
LoggerConfiguration.Destructure.ByTransforming<PatientResponse>(p => new
{
    p.PatientId,
    ContactEmail = "***",
    FullName = "***"
});
```

---

## 15. Docker Compose Topology

```yaml
# docker-compose.yml
version: "3.9"

services:
  # ── Infrastructure ──────────────────────────────────
  sqlserver-patient:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: ${SQL_SA_PASSWORD}
      ACCEPT_EULA: "Y"
      MSSQL_DB: healthbooking_patient
    ports: ["1433:1433"]
    healthcheck:
      test: ["CMD", "/opt/mssql-tools/bin/sqlcmd", "-S", "localhost",
             "-U", "sa", "-P", "${SQL_SA_PASSWORD}", "-Q", "SELECT 1"]
      interval: 10s
      retries: 10

  sqlserver-provider:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: ${SQL_SA_PASSWORD}
      ACCEPT_EULA: "Y"
      MSSQL_DB: healthbooking_provider
    ports: ["1434:1433"]
    healthcheck: *sqlserver-healthcheck

  sqlserver-appointment:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: ${SQL_SA_PASSWORD}
      ACCEPT_EULA: "Y"
      MSSQL_DB: healthbooking_appointment
    ports: ["1435:1433"]
    healthcheck: *sqlserver-healthcheck

  sqlserver-notification:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: ${SQL_SA_PASSWORD}
      ACCEPT_EULA: "Y"
      MSSQL_DB: healthbooking_notification
    ports: ["1436:1433"]
    healthcheck: *sqlserver-healthcheck

  rabbitmq:
    image: rabbitmq:3-management
    environment:
      RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER}
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASS}
    ports:
      - "5672:5672"    # AMQP
      - "15672:15672"  # Management UI
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      retries: 10

  redis:
    image: redis:7-alpine
    ports: ["6379:6379"]
    command: redis-server --requirepass ${REDIS_PASSWORD}
    healthcheck:
      test: ["CMD", "redis-cli", "-a", "${REDIS_PASSWORD}", "ping"]
      interval: 10s
      retries: 5

  identity-server:
    build: ./src/IdentityServer/HealthBooking.IdentityServer
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__OperationalStore: ${IDENTITY_DB_CONN}
    ports: ["5005:8080"]
    depends_on:
      sqlserver-patient:
        condition: service_healthy
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:8080/.well-known/openid-configuration || exit 1"]
      interval: 10s
      retries: 10

  jaeger:
    image: jaegertracing/all-in-one:latest
    environment:
      COLLECTOR_OTLP_ENABLED: "true"
    ports:
      - "16686:16686"  # Jaeger UI
      - "4317:4317"    # OTLP gRPC
      - "4318:4318"    # OTLP HTTP
    healthcheck:
      test: ["CMD-SHELL", "wget -q -O- http://localhost:16686/api/services || exit 1"]
      interval: 15s
      retries: 5

  # ── Application Services ────────────────────────────
  patient-service:
    build: ./src/Services/PatientService/PatientService.API
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Patient: "Server=sqlserver-patient;Database=healthbooking_patient;User=sa;Password=${SQL_SA_PASSWORD};TrustServerCertificate=True"
      IdentityServer__Authority: "http://identity-server:8080"
      RabbitMq__Host: "rabbitmq"
      RabbitMq__Username: ${RABBITMQ_USER}
      RabbitMq__Password: ${RABBITMQ_PASS}
      Otlp__Endpoint: "http://jaeger:4317"
    ports: ["5001:8080"]
    depends_on:
      sqlserver-patient:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
      identity-server:
        condition: service_healthy
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:8080/health/ready || exit 1"]
      interval: 10s
      retries: 10

  provider-service:
    build: ./src/Services/ProviderService/ProviderService.API
    environment:
      ConnectionStrings__Provider: "Server=sqlserver-provider;..."
      Redis__ConnectionString: "redis:6379,password=${REDIS_PASSWORD}"
      IdentityServer__Authority: "http://identity-server:8080"
      RabbitMq__Host: "rabbitmq"
      RabbitMq__Username: ${RABBITMQ_USER}
      RabbitMq__Password: ${RABBITMQ_PASS}
      Otlp__Endpoint: "http://jaeger:4317"
    ports: ["5002:8080"]
    depends_on:
      sqlserver-provider:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
      redis:
        condition: service_healthy
      identity-server:
        condition: service_healthy

  appointment-service:
    build: ./src/Services/AppointmentService/AppointmentService.API
    environment:
      ConnectionStrings__Appointment: "Server=sqlserver-appointment;..."
      Grpc__ProviderService__Address: "http://provider-service:8081"
      Grpc__PatientService__Address: "http://patient-service:8081"
      IdentityServer__Authority: "http://identity-server:8080"
      RabbitMq__Host: "rabbitmq"
      RabbitMq__Username: ${RABBITMQ_USER}
      RabbitMq__Password: ${RABBITMQ_PASS}
      Otlp__Endpoint: "http://jaeger:4317"
    ports: ["5003:8080"]
    depends_on:
      sqlserver-appointment:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
      provider-service:
        condition: service_healthy
      patient-service:
        condition: service_healthy

  notification-service:
    build: ./src/Services/NotificationService/NotificationService.API
    environment:
      ConnectionStrings__Notification: "Server=sqlserver-notification;..."
      IdentityServer__Authority: "http://identity-server:8080"
      RabbitMq__Host: "rabbitmq"
      RabbitMq__Username: ${RABBITMQ_USER}
      RabbitMq__Password: ${RABBITMQ_PASS}
      Otlp__Endpoint: "http://jaeger:4317"
    ports: ["5004:8080"]
    depends_on:
      sqlserver-notification:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy

  api-gateway:
    build: ./src/ApiGateway/HealthBooking.ApiGateway
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      IdentityServer__Authority: "http://identity-server:8080"
      Otlp__Endpoint: "http://jaeger:4317"
    ports: ["5000:8080"]
    depends_on:
      patient-service:
        condition: service_healthy
      provider-service:
        condition: service_healthy
      appointment-service:
        condition: service_healthy
      notification-service:
        condition: service_healthy
      identity-server:
        condition: service_healthy
```

```
# .env.example
SQL_SA_PASSWORD=YourStrong!Passw0rd
RABBITMQ_USER=healthbooking
RABBITMQ_PASS=healthbooking_pass
REDIS_PASSWORD=redis_pass_local
IDENTITY_DB_CONN=Server=sqlserver-patient;Database=IdentityServer;User=sa;Password=...
```

---

## 16. Weekly Milestone Sequencing

### Week 1 — Foundation: Scaffolding, Identity & Gateway

**Goal**: A runnable environment. All service skeletons exist; Docker Compose stack reaches healthy state; JWT tokens can be issued and validated end-to-end.

| Task | Layer | Owner |
|---|---|---|
| Create solution file; add all projects with correct layer structure | Solution | All |
| Define `HealthBooking.SharedKernel`: `AuditableEntity`, `AggregateRoot`, `IDomainEvent`, `OutboxMessage` | SharedKernel | All |
| Define `HealthBooking.Contracts`: all `V1_*` event records and gRPC `.proto` files | Contracts | All |
| Configure Duende IdentityServer with clients, scopes, test seed users | IdentityServer | Dev 1 |
| Configure YARP ApiGateway with route table, JWT validation middleware, rate limiter | ApiGateway | Dev 2 |
| Write `docker-compose.yml` with all infrastructure containers + health checks | Infrastructure | Dev 3 |
| Add `.editorconfig`, `Directory.Build.props` (nullable, implicit usings), `dotnet format` CI step | Tooling | All |
| Auth smoke integration test: POST `/connect/token` → GET `/api/patients/me` → 401 without token | Tests | Dev 1 |
| Validate `docker compose up` cold-start ≤ 3 minutes in clean environment | CI | All |
| Commit `.env.example`; verify `.env` is gitignored; zero secrets hardcoded | Security | All |

**DoD Gate**: `docker compose up` → all `/health/ready` return 200. Auth smoke test passes in CI.

---

### Week 2 — Core Domain: Patient & Provider Services

**Goal**: PatientService and ProviderService are fully functional with CRUD, unit tests, and integration tests.

| Task | Layer | Detail |
|---|---|---|
| `Patient` aggregate: `Register()`, `UpdateProfile()` methods; `PatientId`, `Email`, `PhoneNumber` value objects | PatientService.Domain | |
| `RegisterPatientCommand` + handler; `UpdatePatientProfileCommand` + handler | PatientService.Application | |
| `GetPatientByIdQuery`, `GetPatientByEmailQuery` handlers | PatientService.Application | |
| `PatientDbContext` + `Patients` + `OutboxMessages` EF Core config; Code-First migration | PatientService.Infrastructure | |
| `AuditInterceptor` + `OutboxPublishingInterceptor` registered | PatientService.Infrastructure | |
| `IIdentityProvisioningService` → HTTP client to IdentityServer; Polly pipeline | PatientService.Infrastructure | |
| `PatientsEndpoints` Minimal API; `HealthCheckEndpoints` (`/health/live`, `/health/ready`) | PatientService.API | |
| PatientService gRPC endpoint for `GetPatientById` | PatientService.API | |
| `Provider` aggregate: `Create()`, profile update; `AvailabilitySlot` entity with rowversion | ProviderService.Domain | |
| `DefineAvailabilityCommand`: generates 30-min slot entities; overlap check via domain guard | ProviderService.Domain | |
| All query handlers with Redis `ICacheService` integration | ProviderService.Application | |
| `ProviderDbContext`, repos, `AuditInterceptor`, `OutboxPublishingInterceptor` | ProviderService.Infrastructure | |
| `ProvidersEndpoints`, `SlotsEndpoints`, gRPC `ProviderGrpcService` | ProviderService.API | |
| Unit tests (xUnit + Bogus + NSubstitute): all command/query handlers; ≥80% Domain+Application coverage | Both.UnitTests | |
| Integration tests (TestContainers): persistence round-trips, audit field population, API endpoint contracts | Both.IntegrationTests | |

**DoD Gate**: 80% coverage CI gate green. Integration tests pass with containerised SQL Server.

---

### Week 3 — Scheduling Engine: Booking, Concurrency & Outbox

**Goal**: AppointmentService: booking, double-booking prevention, cancellation, rescheduling, Outbox publishing.

| Task | Layer | Detail |
|---|---|---|
| `Appointment` aggregate: full state machine; `SagaCorrelationId`; all domain events | AppointmentService.Domain | |
| `BookAppointmentCommand` + handler: gRPC slot lock → create Appointment → outbox entry (atomic) | AppointmentService.Application | |
| Idempotency key check in `BookAppointmentCommandHandler` | AppointmentService.Application | |
| `CancelAppointmentCommand` + handler: ≥ 2-hour notice guard; emit cancellation event | AppointmentService.Application | |
| `RescheduleAppointmentCommand` + handler: atomic cancel-rebook in single transaction | AppointmentService.Application | |
| `AppointmentDbContext`: `Appointments`, `BookingIdempotencyKeys`, `OutboxMessages` EF config | AppointmentService.Infrastructure | |
| `ProviderSlotGrpcClient` + `PatientGrpcClient` with Polly resilience pipelines | AppointmentService.Infrastructure | |
| `OutboxProcessor` background service | AppointmentService.Infrastructure | |
| Booking saga documentation: sequence diagram + compensation paths (in `docs/booking-saga.md`) | Docs | |
| Concurrent booking integration test: two simultaneous `BookAppointmentCommand` → exactly one succeeds | AppointmentService.IntegrationTests | |
| Outbox publishing integration test: verify outbox record written in same transaction, event published to broker | AppointmentService.IntegrationTests | |
| All unit tests for booking, cancellation, rescheduling handlers; ≥80% coverage | AppointmentService.UnitTests | |

**DoD Gate**: Concurrent double-booking test passes. Outbox atomicity verified in integration test.

---

### Week 4 — Integration: Message Broker & Eventual Consistency

**Goal**: All four services connected via RabbitMQ. NotificationService consuming events. Provider slot status updated asynchronously.

| Task | Layer | Detail |
|---|---|---|
| `AppointmentBookedConsumer` in ProviderService: update slot to Booked; invalidate Redis cache | ProviderService.Infrastructure | |
| `SlotReleasedConsumer` in ProviderService: update slot to Available; invalidate cache | ProviderService.Infrastructure | |
| `AppointmentBookedConsumer` in NotificationService: idempotency check → create `NotificationRecord` → send email stub | NotificationService.Infrastructure | |
| `AppointmentCancelledConsumer` in NotificationService | NotificationService.Infrastructure | |
| `AppointmentRescheduledConsumer` in NotificationService | NotificationService.Infrastructure | |
| `ProcessedEvents` table idempotency guard in all notification consumers | NotificationService.Infrastructure | |
| Dead-letter queue configuration in MassTransit (3 retries → DLQ) | All services | |
| 24-hour reminder scheduler: Quartz.NET job → `SendReminderNotificationCommand` | NotificationService.Infrastructure | |
| Message contract backward-compatibility documentation in `docs/message-versioning.md` | Docs | |
| `NotificationDbContext`, repos | NotificationService.Infrastructure | |
| `NotificationService.API` with health checks only | NotificationService.API | |
| Integration tests: end-to-end event flow with TestContainers RabbitMQ; duplicate event → single notification | NotificationService.IntegrationTests | |
| Integration test: slot status eventually Booked after `V1_AppointmentBookedEvent`; cache invalidated | ProviderService.IntegrationTests | |

**DoD Gate**: Duplicate delivery test passes. Slot status consistency test passes. All notifications delivered within 60 s in test environment.

---

### Week 5 — Resilience & Caching: Circuit Breakers, Gateway, Redis

**Goal**: All outbound clients wrapped in Polly pipelines. API Gateway fully configured. Redis caching verified. Circuit breaker behavior tested.

| Task | Layer | Detail |
|---|---|---|
| Register all HTTP/gRPC clients with `AddHealthBookingResiliencePipeline()` extension in all services | All services | |
| Circuit breaker `OnOpened`/`OnClosed`/`OnHalfOpened` structured log entries with correlation ID | All services | |
| YARP Gateway: finalize all route definitions; rate limiter tuned to 300 req/min (sliding window) | ApiGateway | |
| YARP Gateway: active health checks for all clusters | ApiGateway | |
| `/health/live` + `/health/ready` endpoints: check SQL Server, RabbitMQ, Redis connectivity per service | All services | |
| Redis `ICacheService` end-to-end for provider slots: verify TTL expiry + write-event invalidation | ProviderService | |
| Integration test: simulate ProviderService unreachable → AppointmentService circuit opens → 503 returned | AppointmentService.IntegrationTests | |
| Cache TTL / invalidation integration test: write slot → cache miss → read cached → update slot → cache invalidated | ProviderService.IntegrationTests | |
| Review all Polly pipeline configurations; ensure jitter applied consistently | All services | |
| Verify `depends_on: condition: service_healthy` all wired in Docker Compose | Infrastructure | |

**DoD Gate**: Circuit breaker test passes. Cache invalidation test passes. All health probes green in `docker compose up`.

---

### Week 6 — Observability, Testing & Closure

**Goal**: Full distributed tracing. Complete integration test suite green in CI. Risk review. Final documentation.

| Task | Layer | Detail |
|---|---|---|
| `AddHealthBookingObservability()` applied in all services | All services | |
| EF Core instrumentation (without SQL statement capture) active | All services | |
| MassTransit OpenTelemetry source wired; consume/publish spans visible in Jaeger | All services | |
| Correlation ID propagation integration test: single booking flow → all spans linked by `X-Correlation-Id` | Integration | |
| PII masking integration test: capture log output → assert no email/name in plain text | All.IntegrationTests | |
| Full booking-flow end-to-end integration test: register → search → book → notify (Jaeger trace verifiable) | Integration | |
| Confirm 80% coverage CI gate passing across all four services on `main` | CI | |
| Risk Register reviewed: all High items mitigated or formally accepted with written rationale | Docs | |
| `README.md` complete: local setup steps, port map, service architecture overview, constitution link | Docs | |
| `docker compose up` cold-start verified ≤ 3 min in clean environment; SC-007 satisfied | Infrastructure | |
| Final milestone review: constitution compliance walk-through (all DoD checklists ticked) | Review | |

**DoD Gate**: All six weekly DoD checklists fully satisfied. CI green on `main`. Distributed trace end-to-end visible in Jaeger. Zero PII in logs confirmed by test.

---

## Appendix A — Booking Saga Choreography

```
Patient           ApiGateway          AppointmentSvc          ProviderSvc         NotificationSvc
  │                    │                      │                     │                      │
  │──POST /book───────►│                      │                     │                      │
  │                    │──JWT validate        │                     │                      │
  │                    │──route to Appt──────►│                     │                      │
  │                    │                      │──gRPC LockSlot─────►│                      │
  │                    │                      │◄── success ─────────│                      │
  │                    │                      │── INSERT Appointment │                      │
  │                    │                      │── INSERT OutboxMsg   │                      │
  │                    │                      │── COMMIT tx          │                      │
  │◄── 201 Confirmed ──│◄─ 201 Confirmed ─────│                     │                      │
  │                    │                      │                     │                      │
  │                    (OutboxProcessor fires 0–5 s later)          │                      │
  │                    │                      │──publish V1_ApptBooked────────────────────►│
  │                    │                      │                     │◄──consume─────────── │
  │                    │                      │                     │  mark slot Booked     │
  │                    │                      │                     │  invalidate cache     │
  │                    │                      │                     │                      │  consume
  │                    │                      │                     │              send email stub
  │                    │                      │                     │              record delivery
```

**Compensation path** (saga failure after slot lock, before DB commit):
- If `BookAppointmentCommand` handler throws after gRPC LockSlot but before DB commit → no `Appointment` row, no `OutboxMessage` written → `V1_AppointmentBooked` never published → ProviderService slot remains in "locked but not officially Booked" state.
- Resolution: gRPC `LockSlot` uses a timeout TTL (30 s). If no `AppointmentBooked` event arrives within TTL, ProviderService auto-releases the slot. This is documented in `docs/booking-saga.md`.

---

## Appendix B — Constitution Compliance Checklist

| Principle | This Plan's Coverage |
|---|---|
| I — Clean Architecture four-layer | ✅ Every service has Domain/Application/Infrastructure/API. Domain has zero external deps. |
| I — MediatR for all writes/reads | ✅ All mutation paths go through Command handlers; all reads through Query handlers. |
| I — EF Core interceptors for audit | ✅ `AuditInterceptor` is the only source of `CreatedAt/By/ModifiedAt/By` — no handler assignment. |
| II — Database-per-service | ✅ Four isolated SQL Server instances; no cross-DB joins; inter-service reads via gRPC or gateway. |
| II — Outbox Pattern for events | ✅ `OutboxPublishingInterceptor` + `OutboxProcessor` in every publishing service. |
| II — Choreography saga documented | ✅ Appendix A + `docs/booking-saga.md` deliverable in Week 3. |
| III — Async-first cross-service writes | ✅ All state-changing cross-service operations use `V1_*` events over RabbitMQ. |
| III — Versioned event contracts | ✅ `V1_*` naming in `HealthBooking.Contracts`; backward compatibility doc in Week 4. |
| III — Idempotent consumers | ✅ `ProcessedEvents` table in NotificationService; slot status guard in ProviderService. |
| III — Redis write-event invalidation | ✅ Cache is invalidated in consumer on every confirmed write — not lazily. |
| IV — xUnit + Bogus + NSubstitute | ✅ Specified in all test projects; Moq is explicitly excluded. |
| IV — 80% coverage gate | ✅ CI enforcement from Week 2 onwards; target is Domain + Application layers per service. |
| IV — TestContainers isolation | ✅ `IClassFixture` per test class; no shared state between classes. |
| V — Polly pipelines | ✅ `AddHealthBookingResiliencePipeline()` on every HTTP/gRPC client; retry + CB + timeout. |
| V — Health endpoints | ✅ `/health/live` + `/health/ready` on all services; Docker Compose `condition: service_healthy`. |
| V — OpenTelemetry + Jaeger | ✅ `AddHealthBookingObservability()` in every service; EF Core + MassTransit + HTTP instrumented. |
| V — Serilog JSON + Correlation ID | ✅ `ICurrentUserService` + `LoggingBehavior` + `CorrelationIdMiddleware` propagate in all entries. |
| VI — JWT at gateway + service boundary | ✅ YARP validates JWT; each service independently validates with `AddJwtBearer`. |
| VI — PII masking | ✅ Serilog destructuring policy + OTel attribute filtering; verified by Week 6 integration test. |
| VI — Secrets via env vars | ✅ `.env.example` committed; `.env` gitignored; zero hardcoded secrets. |
| VII — Docker Compose single command | ✅ Full topology in `docker-compose.yml`; `depends_on: service_healthy` chain ensures ordered startup. |
| VII — Weekly milestones self-contained | ✅ Each week delivers independently demonstrable increment; no forward dependencies. |
