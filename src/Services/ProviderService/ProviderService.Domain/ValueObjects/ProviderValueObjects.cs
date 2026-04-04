namespace ProviderService.Domain.ValueObjects;

public sealed record ProviderId(Guid Value)
{
    public static ProviderId New() => new(Guid.NewGuid());
    public static ProviderId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

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

public sealed record SlotId(Guid Value)
{
    public static SlotId New() => new(Guid.NewGuid());
    public static SlotId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
