using FluentAssertions;
using PatientService.Domain.ValueObjects;
using Xunit;

namespace PatientService.UnitTests.Domain;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("user.name+tag@sub.domain.org")]
    public void Email_ValidFormat_CreatesSuccessfully(string raw)
    {
        var email = new Email(raw);
        email.Value.Should().Be(raw.ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("notanemail")]
    [InlineData("missing@")]
    public void Email_InvalidFormat_Throws(string raw)
    {
        var act = () => new Email(raw);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("+14155552671")]
    [InlineData("+447911123456")]
    [InlineData("07911123456")]    // local format — digits 11, within 7-15
    public void PhoneNumber_ValidDigitCount_CreatesSuccessfully(string raw)
    {
        var phone = new PhoneNumber(raw);
        phone.Value.Should().Be(raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+1")]            // too short (1 digit)
    public void PhoneNumber_Invalid_Throws(string raw)
    {
        var act = () => new PhoneNumber(raw);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FullName_EmptyFirstName_Throws()
    {
        var act = () => new FullName("", "Smith");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FullName_Valid_DisplayNameCombines()
    {
        var name = new FullName("Alice", "Smith");
        name.Full.Should().Be("Alice Smith");
    }
}
