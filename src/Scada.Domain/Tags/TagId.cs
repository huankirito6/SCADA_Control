namespace Scada.Domain.Tags;

public readonly record struct TagId
{
    public TagId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A tag identifier must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public bool IsValid => Value > 0;

    public long RequireValid() => IsValid
        ? Value
        : throw new InvalidOperationException("A default tag identifier is invalid and cannot be used.");
}
