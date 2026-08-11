namespace Scada.Domain.Tags;

public enum TagValueKind : byte
{
    Boolean = 0,
    Signed16 = 1,
    Signed32 = 2,
    Signed64 = 3,
    Real32 = 4,
    Real64 = 5,
    Text = 6,
    Enumeration = 7,
}

public readonly struct TagValue : IEquatable<TagValue>
{
    private readonly bool boolValue;
    private readonly short int16Value;
    private readonly int int32Value;
    private readonly long int64Value;
    private readonly float float32Value;
    private readonly double float64Value;
    private readonly string? stringValue;

    private TagValue(TagValueKind kind)
    {
        Kind = kind;
    }

    private TagValue(bool value)
        : this(TagValueKind.Boolean) => boolValue = value;

    private TagValue(short value)
        : this(TagValueKind.Signed16) => int16Value = value;

    private TagValue(int value)
        : this(TagValueKind.Signed32) => int32Value = value;

    private TagValue(long value)
        : this(TagValueKind.Signed64) => int64Value = value;

    private TagValue(float value)
        : this(TagValueKind.Real32) => float32Value = value;

    private TagValue(double value)
        : this(TagValueKind.Real64) => float64Value = value;

    private TagValue(TagValueKind kind, string value)
        : this(kind) => stringValue = value;

    public TagValueKind Kind { get; }

    public static TagValue FromBool(bool value) => new(value);

    public static TagValue FromInt16(short value) => new(value);

    public static TagValue FromInt32(int value) => new(value);

    public static TagValue FromInt64(long value) => new(value);

    public static TagValue FromFloat32(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A floating-point tag value must be finite.");
        }

        return new TagValue(value);
    }

    public static TagValue FromFloat64(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A floating-point tag value must be finite.");
        }

        return new TagValue(value);
    }

    public static TagValue FromString(string value) => new(TagValueKind.Text, value ?? throw new ArgumentNullException(nameof(value)));

    public static TagValue FromEnum(string value) => new(TagValueKind.Enumeration, value ?? throw new ArgumentNullException(nameof(value)));

    public bool AsBool() => Kind == TagValueKind.Boolean ? boolValue : ThrowWrongKind<bool>();

    public short AsInt16() => Kind == TagValueKind.Signed16 ? int16Value : ThrowWrongKind<short>();

    public int AsInt32() => Kind == TagValueKind.Signed32 ? int32Value : ThrowWrongKind<int>();

    public long AsInt64() => Kind == TagValueKind.Signed64 ? int64Value : ThrowWrongKind<long>();

    public float AsFloat32() => Kind == TagValueKind.Real32 ? float32Value : ThrowWrongKind<float>();

    public double AsFloat64() => Kind == TagValueKind.Real64 ? float64Value : ThrowWrongKind<double>();

    public string AsString() => Kind == TagValueKind.Text ? stringValue! : ThrowWrongKind<string>();

    public string AsEnum() => Kind == TagValueKind.Enumeration ? stringValue! : ThrowWrongKind<string>();

    public object ToWireValue() => Kind switch
    {
        TagValueKind.Boolean => boolValue,
        TagValueKind.Signed16 => int16Value,
        TagValueKind.Signed32 => int32Value,
        TagValueKind.Signed64 => int64Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TagValueKind.Real32 => float32Value,
        TagValueKind.Real64 => float64Value,
        TagValueKind.Text or TagValueKind.Enumeration => stringValue!,
        _ => throw new InvalidOperationException("Tag value has no recognized kind."),
    };

    public bool Equals(TagValue other) =>
        Kind == other.Kind &&
        Kind switch
        {
            TagValueKind.Boolean => boolValue == other.boolValue,
            TagValueKind.Signed16 => int16Value == other.int16Value,
            TagValueKind.Signed32 => int32Value == other.int32Value,
            TagValueKind.Signed64 => int64Value == other.int64Value,
            TagValueKind.Real32 => float32Value.Equals(other.float32Value),
            TagValueKind.Real64 => float64Value.Equals(other.float64Value),
            TagValueKind.Text or TagValueKind.Enumeration => stringValue == other.stringValue,
            _ => false,
        };

    public override bool Equals(object? obj) => obj is TagValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, ToWireValue());

    public static bool operator ==(TagValue left, TagValue right) => left.Equals(right);

    public static bool operator !=(TagValue left, TagValue right) => !left.Equals(right);

    private T ThrowWrongKind<T>() => throw new InvalidOperationException($"Tag value kind is {Kind}, not {typeof(T).Name}.");
}
