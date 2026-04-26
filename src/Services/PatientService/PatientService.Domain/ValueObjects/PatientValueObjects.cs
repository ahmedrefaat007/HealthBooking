namespace PatientService.Domain.ValueObjects;

/*
 * PatientId
 * ---------
 * Strongly-typed wrapper around Guid to uniquely identify a Patient aggregate.
 *
 * WHO USES IT:
 *   Patient entity, IPatientRepository, command/query handlers.
 *
 * WHY THIS APPROACH:
 *   Prevents accidental substitution of a ProviderId where a PatientId is expected;
 *   makes domain method signatures self-documenting and type-safe.
 */
public sealed record PatientId(Guid Value)
{
    public static PatientId New() => new(Guid.NewGuid());
    public static PatientId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

/*
 * Email
 * -----
 * Value object encapsulating a validated, lower-cased e-mail address.
 *
 * WHO USES IT:
 *   Patient.Register(), IPatientRepository.GetByEmailAsync(),
 *   RegisterPatientCommandValidator, PatientDto.
 *
 * WHY THIS APPROACH:
 *   Validation is encoded in the constructor so any holder of an Email record
 *   is guaranteed a non-null, structurally valid, normalised address.
 */
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

/*
 * PhoneNumber
 * -----------
 * Value object validating a 7–15 digit phone number (E.164-compatible).
 *
 * WHO USES IT:
 *   Patient.Register(), Patient.UpdateProfile().
 *
 * WHY THIS APPROACH:
 *   Centralises phone validation; non-digit characters are allowed in input
 *   (spaces, dashes) but only digits are counted toward the length constraint,
 *   matching real-world user input flexibility.
 */
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

/*
 * FullName
 * --------
 * Value object combining FirstName and LastName.  Both must be non-empty.
 *
 * WHO USES IT:
 *   Patient.Register(), Patient.UpdateProfile(), PatientDto display.
 *
 * WHY THIS APPROACH:
 *   A composite record enforces that first and last name are always provided
 *   together, preventing a half-named patient from being persisted.
 */
public sealed record FullName
{
    public string FirstName { get; }
    public string LastName { get; }

    public FullName(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new ArgumentException("First name cannot be empty.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Last name cannot be empty.", nameof(lastName));

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
    }

    public string Full => $"{FirstName} {LastName}";
    public override string ToString() => Full;
}
