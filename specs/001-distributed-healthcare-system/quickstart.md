# Quick-Start: Implementing All Gap Remediation Changes

**Date**: 2026-04-04 | **Branch**: `001-distributed-healthcare-system`  
**Prerequisite**: `docker compose up -d` running; all services healthy.

---

## Step 1 — Add `ScheduledStartUtc` to Appointment Entity

```csharp
// src/Services/AppointmentService/AppointmentService.Domain/Entities/Appointment.cs

// 1. Add property:
public DateTimeOffset ScheduledStartUtc { get; private set; }

// 2. Update Book() factory to accept and store it:
public static Appointment Book(Guid patientId, Guid slotId,
    string patientName, DateTimeOffset scheduledStart)
{
    // ...existing null checks...
    var a = new Appointment
    {
        Id                = Guid.NewGuid(),
        PatientId         = patientId,
        SlotId            = slotId,
        PatientName       = patientName.Trim(),
        ScheduledStartUtc = scheduledStart,    // NEW
        Status            = AppointmentStatus.Booked
    };
    a.AddDomainEvent(new AppointmentBookedEvent(a.Id, patientId, slotId, DateTimeOffset.UtcNow));
    return a;
}
```

Update `PersistAppointmentActivity.cs` to pass `slotInfo.StartTimeUtc` to `Appointment.Book(...)`.

---

## Step 2 — Add Domain Methods + Events

```csharp
// In Appointment.cs:
public void Confirm()
{
    if (Status != AppointmentStatus.Booked)
        throw new InvalidOperationException($"Cannot confirm appointment with status {Status}.");
    Status = AppointmentStatus.Confirmed;
    AddDomainEvent(new AppointmentConfirmedDomainEvent(Id, PatientId, DateTimeOffset.UtcNow));
}

public void MarkNoShow()
{
    if (Status != AppointmentStatus.Confirmed)
        throw new InvalidOperationException($"Cannot mark no-show for status {Status}.");
    Status = AppointmentStatus.NoShow;
    AddDomainEvent(new AppointmentNoShowDomainEvent(Id, PatientId, DateTimeOffset.UtcNow));
}

public void Reschedule(Guid newSlotId, DateTimeOffset newStart)
{
    if (Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed)
        throw new InvalidOperationException($"Cannot reschedule appointment with status {Status}.");
    var oldSlotId = SlotId;
    SlotId = newSlotId;
    ScheduledStartUtc = newStart;
    AddDomainEvent(new AppointmentRescheduledDomainEvent(
        Id, PatientId, oldSlotId, newSlotId, newStart, DateTimeOffset.UtcNow));
}
```

```csharp
// In AppointmentEvents.cs — add 3 new records:
public sealed record AppointmentRescheduledDomainEvent(
    Guid AppointmentId, Guid PatientId,
    Guid OldSlotId, Guid NewSlotId,
    DateTimeOffset NewStartUtc, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentConfirmedDomainEvent(
    Guid AppointmentId, Guid PatientId,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AppointmentNoShowDomainEvent(
    Guid AppointmentId, Guid PatientId,
    DateTimeOffset OccurredAt) : IDomainEvent;
```

---

## Step 3 — EF Core Configuration + Migration

```csharp
// In AppointmentConfiguration.cs — add inside Configure():
builder.Property(a => a.ScheduledStartUtc).IsRequired();

builder.HasIndex(a => a.SlotId)
    .IsUnique()
    .HasDatabaseName("UQ_Appointments_SlotId");
```

```bash
# Generate migration (run from repo root):
dotnet ef migrations add AddScheduledStartAndUniqueSlotId \
  --project src/Services/AppointmentService/AppointmentService.Infrastructure \
  --startup-project src/Services/AppointmentService/AppointmentService.API
```

---

## Step 4 — RescheduleAppointmentCommand

```csharp
// src/.../Commands/RescheduleAppointment/RescheduleAppointmentCommand.cs
public sealed record RescheduleAppointmentCommand(
    Guid   AppointmentId,
    Guid   NewSlotId,
    string CallerUserId) : IRequest;

public sealed class RescheduleValidator : AbstractValidator<RescheduleAppointmentCommand>
{
    public RescheduleValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.NewSlotId).NotEmpty();
        RuleFor(x => x.CallerUserId).NotEmpty();
    }
}

public sealed class RescheduleAppointmentCommandHandler(
    IAppointmentRepository appointments,
    IProviderSlotGrpcClient slotClient,
    IPublishEndpoint        publish) : IRequestHandler<RescheduleAppointmentCommand>
{
    public async Task Handle(RescheduleAppointmentCommand request, CancellationToken ct)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, ct)
            ?? throw new InvalidOperationException($"Appointment {request.AppointmentId} not found.");

        if (appointment.PatientId.ToString() != request.CallerUserId)
            throw new UnauthorizedAccessException("Not authorised to reschedule this appointment.");

        var newSlot = await slotClient.GetSlotByIdAsync(request.NewSlotId, ct)
            ?? throw new InvalidOperationException($"Slot {request.NewSlotId} not found.");

        if (newSlot.Status != "Available")
            throw new InvalidOperationException($"Slot {request.NewSlotId} is not available.");

        var oldSlotId = appointment.SlotId;

        // Atomic slot swap: release old, lock new
        await slotClient.ReleaseSlotAsync(oldSlotId, ct);
        var locked = await slotClient.LockSlotAsync(request.NewSlotId, appointment.Id, ct);
        if (!locked)
        {
            // Compensate: re-lock old slot
            await slotClient.LockSlotAsync(oldSlotId, appointment.Id, ct);
            throw new InvalidOperationException("New slot could not be locked (concurrent booking).");
        }

        // Domain mutation + raises AppointmentRescheduledDomainEvent
        appointment.Reschedule(request.NewSlotId, newSlot.StartTimeUtc);

        // SaveChanges writes Appointment update + OutboxMessage in one transaction
        await appointments.SaveChangesAsync(ct);
    }
}
```

---

## Step 5 — ConfirmAppointmentCommand + MarkNoShowCommand

```csharp
// Pattern identical to CancelAppointmentCommand, just different domain method called
// ConfirmAppointmentCommand: appointment.Confirm() → SaveChanges
// MarkNoShowCommand:         appointment.MarkNoShow() → SaveChanges
// No gRPC side effects needed for these two
```

---

## Step 6 — New AppointmentService Endpoints

```csharp
// In AppointmentsEndpoints.cs, inside MapAppointmentEndpoints():

group.MapPut("/{id:guid}/reschedule", async (
    Guid id, RescheduleRequest body, ISender sender, HttpContext http, CancellationToken ct) =>
{
    var callerId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(callerId)) return Results.Unauthorized();
    await sender.Send(new RescheduleAppointmentCommand(id, body.NewSlotId, callerId), ct);
    return Results.Ok();
}).WithName("RescheduleAppointment").RequireAuthorization();

group.MapPost("/{id:guid}/confirm", async (
    Guid id, ISender sender, HttpContext http, CancellationToken ct) =>
{
    var callerId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(callerId)) return Results.Unauthorized();
    await sender.Send(new ConfirmAppointmentCommand(id, callerId), ct);
    return Results.NoContent();
}).WithName("ConfirmAppointment").RequireAuthorization();

group.MapPost("/{id:guid}/no-show", async (
    Guid id, ISender sender, HttpContext http, CancellationToken ct) =>
{
    var callerId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(callerId)) return Results.Unauthorized();
    await sender.Send(new MarkNoShowCommand(id, callerId), ct);
    return Results.NoContent();
}).WithName("MarkNoShow").RequireAuthorization();

// Add request record at bottom of file:
public sealed record RescheduleRequest(Guid NewSlotId);
```

---

## Step 7 — Cancellation Notice Window

```csharp
// In appsettings.json (AppointmentService.API):
"Appointment": {
  "CancellationNoticeHours": 2
}

// In CancelAppointmentCommandHandler.cs:
public sealed class CancelAppointmentCommandHandler(
    IAppointmentRepository  appointments,
    IProviderSlotGrpcClient slotClient,
    IConfiguration          config) : IRequestHandler<CancelAppointmentCommand>
{
    public async Task Handle(CancelAppointmentCommand request, CancellationToken ct)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, ct)
            ?? throw new InvalidOperationException($"Appointment {request.AppointmentId} not found.");

        if (appointment.PatientId.ToString() != request.CallerUserId)
            throw new UnauthorizedAccessException("Not authorised to cancel this appointment.");

        // Notice window check (uses ScheduledStartUtc added in Step 1)
        var noticeHours = config.GetValue("Appointment:CancellationNoticeHours", 2);
        var noticeCutoff = appointment.ScheduledStartUtc.AddHours(-noticeHours);
        if (DateTimeOffset.UtcNow > noticeCutoff)
            throw new InvalidOperationException(
                $"Appointments must be cancelled at least {noticeHours}h before the scheduled time.");

        appointment.Cancel(request.Reason);
        await slotClient.ReleaseSlotAsync(appointment.SlotId, ct);
        await appointments.SaveChangesAsync(ct);
    }
}
```

---

## Step 8 — OutboxPublishingInterceptor: Map Domain Event → Contract

```csharp
// In OutboxPublishingInterceptor.cs, the interceptor serialises IDomainEvent into OutboxMessage.
// The OutboxProcessor publishes to exchange named after the type.
// The contract mapping is done in the OutboxProcessor when it calls publish.Publish(payload).
// AppointmentRescheduledDomainEvent must map to V1_AppointmentRescheduledEvent.
// Add domain-to-contract mapper or configure MassTransit publish type mapping.

// Option (simplest): In OutboxProcessor, switch on EventType to publish the right contract type:
// AppointmentRescheduledDomainEvent → V1_AppointmentRescheduledEvent
// OR: rename domain event to match contract type (convention-based)
```

---

## Step 9 — ProviderService: Add MassTransit + NuGet

```bash
# Add NuGet to ProviderService.Infrastructure:
dotnet add src/Services/ProviderService/ProviderService.Infrastructure/ProviderService.Infrastructure.csproj \
  package MassTransit.RabbitMQ

# Add health check to ProviderService.API:
dotnet add src/Services/ProviderService/ProviderService.API/ProviderService.API.csproj \
  package AspNetCore.HealthChecks.Rabbitmq
```

```csharp
// AppointmentBookedConsumer.cs:
public sealed class AppointmentBookedConsumer(
    ISlotRepository slotRepository,
    ICacheService   cache) : IConsumer<V1_AppointmentBookedEvent>
{
    public async Task Consume(ConsumeContext<V1_AppointmentBookedEvent> context)
    {
        var msg  = context.Message;
        var slot = await slotRepository.GetByIdAsync(msg.SlotId, context.CancellationToken);
        if (slot is null || slot.Status == SlotStatus.Booked) return; // idempotent

        slot.Book();
        await slotRepository.SaveChangesAsync(context.CancellationToken);
        await cache.RemoveAsync($"provider:slots:{msg.ProviderId}:{slot.Date:yyyyMMdd}");
    }
}

// SlotReleasedConsumer.cs:
public sealed class SlotReleasedConsumer(
    ISlotRepository slotRepository,
    ICacheService   cache) : IConsumer<V1_SlotReleasedEvent>
{
    public async Task Consume(ConsumeContext<V1_SlotReleasedEvent> context)
    {
        var msg  = context.Message;
        var slot = await slotRepository.GetByIdAsync(msg.SlotId, context.CancellationToken);
        if (slot is null || slot.Status == SlotStatus.Available) return; // idempotent

        slot.Release();
        await slotRepository.SaveChangesAsync(context.CancellationToken);
        await cache.RemoveAsync($"provider:slots:{msg.ProviderId}:{slot.Date:yyyyMMdd}");
    }
}
```

```csharp
// In ProviderService.API/Program.cs — add MassTransit block:
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AppointmentBookedConsumer>();
    x.AddConsumer<SlotReleasedConsumer>();
    x.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.Host(builder.Configuration.GetConnectionString("RabbitMq"));
        rmq.UseMessageRetry(r => r.Exponential(3,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
        rmq.ConfigureEndpoints(ctx);
    });
});
```

---

## Step 10 — Global MassTransit Retry (AppointmentService + NotificationService)

```csharp
// In both Program.cs files, update UsingRabbitMq block:
rmq.UseMessageRetry(r => r.Exponential(3,
    TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
```

---

## Step 11 — AppointmentRescheduledConsumer (NotificationService)

```csharp
// src/Services/NotificationService/NotificationService.Application/Consumers/
//   AppointmentRescheduledConsumer.cs

public sealed class AppointmentRescheduledConsumer(
    INotificationLogRepository repo,
    IPatientGrpcClient         patientClient,
    IEmailService              emailService,
    ILogger<AppointmentRescheduledConsumer> logger) : IConsumer<V1_AppointmentRescheduledEvent>
{
    public async Task Consume(ConsumeContext<V1_AppointmentRescheduledEvent> context)
    {
        var msg = context.Message;

        // Idempotency guard
        if (await repo.ExistsByCorrelationAndTypeAsync(
                msg.SagaCorrelationId, "AppointmentRescheduled", context.CancellationToken))
            return;

        PatientInfo? patient = null;
        try
        {
            patient = await patientClient.GetPatientByIdAsync(msg.PatientId, context.CancellationToken);
            var subject = "Appointment Rescheduled";
            var body    = $"Your appointment has been moved to {msg.NewStartUtc:f} (UTC).";
            await emailService.SendAsync(patient.ContactEmail, subject, body, context.CancellationToken);
            await repo.AddAsync(NotificationLog.Create(msg.AppointmentId, msg.SagaCorrelationId,
                "AppointmentRescheduled", patient.ContactEmail, subject, NotificationStatus.Sent));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reschedule notification failed for {AppointmentId}", msg.AppointmentId);
            var email = patient?.ContactEmail ?? "unknown";
            await repo.AddAsync(NotificationLog.CreateFailed(msg.AppointmentId, msg.SagaCorrelationId,
                "AppointmentRescheduled", email, ex.Message));
            throw; // allow MassTransit retry
        }
    }
}
```

```csharp
// Register in NotificationService Program.cs:
x.AddConsumer<AppointmentRescheduledConsumer>();  // ADD to existing block
```

---

## Step 12 — PII Masking (Serilog)

```csharp
// Add to each service's Program.cs Serilog configuration:
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Destructure.ByTransforming<PatientInfo>(p => new
    {
        p.PatientId,
        FullName     = "***",
        ContactEmail = "***"
    }));
```

---

## Step 13 — CI Coverage Enforcement

```yaml
# In .github/workflows/ci.yml, after the test step:
- name: Install ReportGenerator
  run: dotnet tool install --global dotnet-reportgenerator-globaltool

- name: Generate coverage summary
  run: |
    reportgenerator \
      -reports:**/coverage.cobertura.xml \
      -targetdir:coverage-report \
      -reporttypes:TextSummary

- name: Enforce 80% threshold
  shell: pwsh
  run: |
    $summary = Get-Content "coverage-report/Summary.txt" -Raw
    if ($summary -match 'Line coverage: (\d+\.?\d*)%') {
        $pct = [double]$Matches[1]
        if ($pct -lt 80.0) { exit 1 }
    }
```

---

## Verification

After all changes, verify each gap is closed:

| Gap | Verification Command / Check |
|---|---|
| C1 Reschedule | `PUT /api/appointments/{id}/reschedule` → 200; old slot Available; new slot Booked; notification email logged |
| C2 Unique SlotId | Attempt two bookings for same slot; second returns 409; DB shows one Appointment row |
| C3 ProviderService consumers | Book appointment → slot transitions Locked→Booked in ProviderService DB |
| C4 MassTransit retry | Kill NotificationService after 1st delivery attempt; restart; confirm message retried |
| C5 Reschedule notification | Reschedule appointment → notification log shows "AppointmentRescheduled" entry |
| C6 PII masking | Check logs: `@PatientInfo` objects show `***` for email/name |
| I1 State transitions | `POST /api/appointments/{id}/confirm` → 204; status = Confirmed |
| I3 Cancellation window | Cancel 1h before scheduled time → 400 response |
| I4 CI coverage | Push with <80% coverage → CI pipeline fails |
