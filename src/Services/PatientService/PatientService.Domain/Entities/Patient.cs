using HealthBooking.SharedKernel.Domain;
using PatientService.Domain.Events;
using PatientService.Domain.ValueObjects;

namespace PatientService.Domain.Entities;

/*
 * Patient
 * -------
 * The core DDD aggregate root for the PatientService.
 * Encapsulates all invariants and state transitions related to a healthcare patient.
 *
 * WHO USES IT:
 *   - RegisterPatientCommandHandler: creates new patients via Patient.Register().
 *   - UpdatePatientProfileCommandHandler: calls UpdateProfile().
 *   - PatientRepository: persists and retrieves Patient instances.
 *   - PatientGrpcService: serves patient data to AppointmentService and NotificationService.
 *
 * WHY THIS APPROACH:
 *   Aggregate root pattern ensures the Patient entity is always in a valid state:
 *   value objects (FullName, Email, PhoneNumber) validate on construction, and the
 *   factory method Register() is the only way to create a Patient, preventing
 *   partially-initialised instances.  Domain events emitted here trigger
 *   downstream workflows (identity provisioning, audit) via the outbox pattern.
 */
public sealed class Patient : AggregateRoot
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string ContactEmail { get; private set; } = default!;
    public string PhoneNumber { get; private set; } = default!;
    public DateOnly DateOfBirth { get; private set; }
    public DateTimeOffset RegistrationDate { get; private set; }

    /* EF Core requires a parameterless constructor for materialisation; kept private to prevent
     * callers from bypassing the Register() factory method. */
    private Patient() { }

    /*
     * Register
     * --------
     * Factory method — the only public way to create a Patient.
     * Validates all inputs through value objects, enforces that date-of-birth is
     * in the past, assigns a new GUID, and raises PatientRegisteredEvent.
     */
    public static Patient Register(
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        DateOnly dateOfBirth)
    {
        var name = new FullName(firstName, lastName);
        var mail = new Email(email);
        var phone = new PhoneNumber(phoneNumber);

        if (dateOfBirth >= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("Date of birth must be in the past.", nameof(dateOfBirth));

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = name.FirstName,
            LastName = name.LastName,
            ContactEmail = mail.Value,
            PhoneNumber = phone.Value,
            DateOfBirth = dateOfBirth,
            RegistrationDate = DateTimeOffset.UtcNow
        };

        patient.AddDomainEvent(new PatientRegisteredEvent(
            patient.Id,
            patient.FirstName,
            patient.LastName,
            patient.ContactEmail,
            DateTimeOffset.UtcNow));

        return patient;
    }

    /*
     * UpdateProfile
     * -------------
     * Updates mutable profile fields (name, phone).  Email and date-of-birth are
     * immutable after registration.  Raises PatientProfileUpdatedEvent so downstream
     * consumers can react (cache invalidation, audit log).
     */
    public void UpdateProfile(
        string firstName,
        string lastName,
        string phoneNumber)
    {
        var name = new FullName(firstName, lastName);
        var phone = new PhoneNumber(phoneNumber);

        FirstName = name.FirstName;
        LastName = name.LastName;
        PhoneNumber = phone.Value;

        AddDomainEvent(new PatientProfileUpdatedEvent(
            Id, FirstName, LastName, PhoneNumber, DateTimeOffset.UtcNow));
    }
}
