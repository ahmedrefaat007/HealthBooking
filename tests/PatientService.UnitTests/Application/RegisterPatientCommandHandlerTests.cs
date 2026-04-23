using Bogus;
using FluentAssertions;
using NSubstitute;
using PatientService.Application.Commands.RegisterPatient;
using PatientService.Application.Interfaces;
using PatientService.Domain.Entities;
using Xunit;

namespace PatientService.UnitTests.Application;

public sealed class RegisterPatientCommandHandlerTests
{
    private readonly IPatientRepository _repo = Substitute.For<IPatientRepository>();
    private readonly IIdentityProvisioningService _identity = Substitute.For<IIdentityProvisioningService>();
    private readonly RegisterPatientCommandHandler _sut;
    private static readonly Faker _faker = new();

    public RegisterPatientCommandHandlerTests()
    {
        _sut = new RegisterPatientCommandHandler(_repo, _identity);
    }

    private static RegisterPatientCommand ValidCommand() => new(
        FirstName: _faker.Name.FirstName(),
        LastName: _faker.Name.LastName(),
        Email: _faker.Internet.Email(),
        PhoneNumber: "+447911123456",
        DateOfBirth: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30)));

    [Fact]
    public async Task Handle_NewPatient_ReturnsPatientId()
    {
        // Arrange
        var cmd = ValidCommand();
        _repo.ExistsByEmailAsync(cmd.Email, default).Returns(false);

        // Act
        var result = await _sut.Handle(cmd, default);

        // Assert
        result.PatientId.Should().NotBeEmpty();
        await _repo.Received(1).AddAsync(Arg.Is<Patient>(p => p.ContactEmail == cmd.Email.ToLowerInvariant()), default);
        await _repo.Received(1).SaveChangesAsync(default);
        await _identity.Received(1).ProvisionUserAsync(result.PatientId, cmd.Email.ToLowerInvariant(), default);
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ThrowsInvalidOperationException()
    {
        // Arrange
        var cmd = ValidCommand();
        _repo.ExistsByEmailAsync(cmd.Email, default).Returns(true);

        // Act
        var act = () => _sut.Handle(cmd, default);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{cmd.Email}*");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Patient>(), default);
    }
}
