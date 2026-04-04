using FluentAssertions;
using PatientService.Domain.Entities;
using PatientService.Domain.Events;
using Xunit;

namespace PatientService.UnitTests.Domain;

public sealed class PatientAggregateTests
{
    private static Patient BuildPatient(string email = "alice@example.com") =>
        Patient.Register("Alice", "Smith", email, "+447911000001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)));

    [Fact]
    public void Register_ValidArgs_SetsPropertiesAndRaisesEvent()
    {
        var patient = BuildPatient();

        patient.FirstName.Should().Be("Alice");
        patient.LastName.Should().Be("Smith");
        patient.ContactEmail.Should().Be("alice@example.com");
        patient.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PatientRegisteredEvent>();
    }

    [Fact]
    public void Register_EmptyFirstName_Throws()
    {
        var act = () => Patient.Register("", "Smith", "a@b.com", "+1234567", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_InvalidEmail_Throws()
    {
        var act = () => Patient.Register("Alice", "Smith", "not-an-email", "+1234567", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateProfile_RaisesProfileUpdatedEvent_AndClearsOldEvents()
    {
        var patient = BuildPatient();
        patient.ClearDomainEvents();

        patient.UpdateProfile("Bob", "Jones", "+447911999999");

        patient.FirstName.Should().Be("Bob");
        patient.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PatientProfileUpdatedEvent>();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var patient = BuildPatient();
        patient.ClearDomainEvents();
        patient.DomainEvents.Should().BeEmpty();
    }
}
