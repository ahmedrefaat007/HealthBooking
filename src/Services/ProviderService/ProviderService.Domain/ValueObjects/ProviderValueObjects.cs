namespace ProviderService.Domain.ValueObjects;

/*
 * ProviderId
 * ----------
 * Strongly-typed Guid wrapper for Provider aggregate identity.
 * Prevents passing a patient or slot ID where a provider ID is expected.
 */
public sealed record ProviderId(Guid Value)
{
    public static ProviderId New() => new(Guid.NewGuid());
    public static ProviderId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

/*
 * SpecialtyName
 * -------------
 * Value object enforcing non-empty and max-100-char specialty string.
 * Used in Provider.Register() to validate the specialty field.
 */
public sealed record SpecialtyName
{
    public string Value { get; }

    public SpecialtyName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Specialty name cannot be empty.", nameof(value));
        if (value.Trim().Length > 100)
            throw new ArgumentException("Specialty name must not exceed 100 characters.", nameof(value));

        Value = value.Trim();
    }

    public override string ToString() => Value;
}

/*
 * SlotId
 * ------
 * Strongly-typed Guid wrapper for AvailabilitySlot identity.
 * Used in gRPC contracts and saga state to reference slots without
 * accidental confusion with Provider or Appointment IDs.
 */
public sealed record SlotId(Guid Value)
{
    public static SlotId New() => new(Guid.NewGuid());
    public static SlotId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
