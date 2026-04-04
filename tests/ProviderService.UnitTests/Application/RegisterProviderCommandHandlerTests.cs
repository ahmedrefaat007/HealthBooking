using FluentAssertions;
using NSubstitute;
using ProviderService.Application.Commands.RegisterProvider;
using ProviderService.Application.Interfaces;
using ProviderService.Domain.Entities;
using Xunit;

namespace ProviderService.UnitTests.Application;

public sealed class RegisterProviderCommandHandlerTests
{
    private readonly IProviderRepository _repo = Substitute.For<IProviderRepository>();
    private readonly RegisterProviderCommandHandler _sut;

    public RegisterProviderCommandHandlerTests() =>
        _sut = new RegisterProviderCommandHandler(_repo);

    [Fact]
    public async Task Handle_NewProvider_ReturnsProviderId()
    {
        _repo.ExistsByLicenseAsync("LIC001", default).Returns(false);

        var result = await _sut.Handle(
            new RegisterProviderCommand("John", "Doe", "Cardiology", "LIC001"), default);

        result.ProviderId.Should().NotBeEmpty();
        await _repo.Received(1).AddAsync(Arg.Any<Provider>(), default);
        await _repo.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task Handle_DuplicateLicense_Throws()
    {
        _repo.ExistsByLicenseAsync("LIC002", default).Returns(true);

        var act = () => _sut.Handle(
            new RegisterProviderCommand("Jane", "Doe", "Cardiology", "LIC002"), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*LIC002*");
    }
}
