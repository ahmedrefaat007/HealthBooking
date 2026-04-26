using AppointmentService.Application.Interfaces;
using AppointmentService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AppointmentService.Infrastructure.Persistence.Repositories;

/*
 * AppointmentRepository
 * ---------------------
 * EF Core implementation of IAppointmentRepository.
 *
 * WHO USES IT:
 *   All command and query handlers that need to read or persist appointments.
 *
 * WHY THIS APPROACH:
 *   GetByPatientIdAsync orders by the shadow-property CreatedAt (set by AuditInterceptor)
 *   so the patient's appointment history is always sorted most-recent-first without
 *   adding a public property to the domain entity.
 */
public sealed class AppointmentRepository(AppointmentDbContext context)
    : IAppointmentRepository
{
    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<IReadOnlyList<Appointment>> GetByPatientIdAsync(
        Guid patientId, CancellationToken ct = default) =>
        context.Appointments
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => EF.Property<DateTimeOffset>(a, "CreatedAt"))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<Appointment>)t.Result, ct);

    public async Task AddAsync(Appointment appointment, CancellationToken ct = default) =>
        await context.Appointments.AddAsync(appointment, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
