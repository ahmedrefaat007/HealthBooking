using HealthBooking.SharedKernel.Domain;
using PatientService.Domain.Events;
using PatientService.Domain.ValueObjects;

namespace PatientService.Domain.Entities;

public sealed class Patient : AggregateRoot
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public string ContactEmail { get; private set; } = default!;
    public string PhoneNumber { get; private set; } = default!;
    public DateOnly DateOfBirth { get; private set; }
    public DateTimeOffset RegistrationDate { get; private set; }

    // EF Core private constructor
    private Patient() { }

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
