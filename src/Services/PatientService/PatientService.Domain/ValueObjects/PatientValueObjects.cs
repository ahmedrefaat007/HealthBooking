namespace PatientService.Domain.ValueObjects;

public sealed record PatientId(Guid Value)
{
    public static PatientId New() => new(Guid.NewGuid());
    public static PatientId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public sealed record Email
{
    public string Value { get; }

    public Email(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email cannot be empty.", nameof(value));

        if (!value.Contains('@') || !value.Contains('.'))
            throw new ArgumentException($"'{value}' is not a valid email address.", nameof(value));

        Value = value.Trim().ToLowerInvariant();
    }

    public override string ToString() => Value;
}

public sealed record PhoneNumber
{
    public string Value { get; }

    public PhoneNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Phone number cannot be empty.", nameof(value));

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length < 7 || digits.Length > 15)
            throw new ArgumentException(
                $"Phone number '{value}' must contain 7–15 digits (E.164).", nameof(value));

        Value = value.Trim();
    }

    public override string ToString() => Value;
}

public sealed record FullName
{
    public string FirstName { get; }
    public string LastName  { get; }

    public FullName(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name cannot be empty.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name cannot be empty.", nameof(lastName));

        FirstName = firstName.Trim();
        LastName  = lastName.Trim();
    }

    public string Full => $"{FirstName} {LastName}";
    public override string ToString() => Full;
}
