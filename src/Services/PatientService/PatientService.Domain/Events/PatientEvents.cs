namespace PatientService.Domain.Events;

public sealed record PatientRegisteredEvent(
    Guid PatientId,
    string FirstName,
    string LastName,
    string ContactEmail,
    DateTimeOffset OccurredAt) : HealthBooking.SharedKernel.Domain.IDomainEvent;

public sealed record PatientProfileUpdatedEvent(
    Guid PatientId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    DateTimeOffset OccurredAt) : HealthBooking.SharedKernel.Domain.IDomainEvent;
