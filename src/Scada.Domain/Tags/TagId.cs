namespace Scada.Domain.Tags;

public readonly record struct TagId
{
    public TagId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A tag identifier must be positive.");
        }

        _value = value;
    }

    private readonly long _value;

    public long Value => IsValid
        ? _value
        : throw new InvalidOperationException("A default tag identifier is invalid and cannot be used.");

    public bool IsValid => _value > 0;

    public long RequireValid() => IsValid
        ? Value
        : throw new InvalidOperationException("A default tag identifier is invalid and cannot be used.");
}
