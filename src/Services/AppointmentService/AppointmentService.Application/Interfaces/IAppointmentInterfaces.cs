using AppointmentService.Domain.Entities;

namespace AppointmentService.Application.Interfaces;

/*
 * IAppointmentRepository
 * ----------------------
 * Persistence abstraction for Appointment aggregates.
 *
 * WHO USES IT:
 *   All command handlers, query handlers, and AppointmentsEndpoints
 *   idempotency pre-check.
 */
public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Appointment>> GetByPatientIdAsync(Guid patientId, CancellationToken ct = default);
    Task AddAsync(Appointment appointment, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IPatientGrpcClient
{
    Task<PatientInfo?> GetPatientByIdAsync(Guid patientId, CancellationToken ct = default);
}

public interface IProviderSlotGrpcClient
{
    Task<SlotInfo?> GetSlotByIdAsync(Guid slotId, CancellationToken ct = default);
    Task<bool> LockSlotAsync(Guid slotId, Guid appointmentId, CancellationToken ct = default);
    Task<bool> ReleaseSlotAsync(Guid slotId, CancellationToken ct = default);
}

public interface IIdempotencyRepository
{
    Task<BookingIdempotencyKey?> FindAsync(string key, CancellationToken ct = default);
    Task AddAsync(BookingIdempotencyKey key, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface ICurrentUserService
{
    string UserId { get; }
}

// ── DTOs used across application interfaces ──────────────────────────────────

public sealed record PatientInfo(
    Guid PatientId,
    string FullName,
    string ContactEmail);

public sealed record SlotInfo(
    Guid SlotId,
    Guid ProviderId,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset EndTimeUtc,
    string Status);
