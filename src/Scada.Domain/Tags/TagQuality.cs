namespace Scada.Domain.Tags;

public enum QualitySeverity : byte
{
    Good = 0,
    Uncertain = 1,
    Bad = 2,
}

[Flags]
public enum QualityReason : ushort
{
    None = 0,
    CommFail = 1,
    DeviceError = 2,
    ConfigError = 4,
    Stale = 8,
    LastKnown = 16,
    OutOfRange = 32,
    NotInitialized = 64,
    Simulated = 128,
    Forced = 256,
}

public readonly record struct TagQuality
{
    private const QualityReason DefinedReasons =
        QualityReason.CommFail |
        QualityReason.DeviceError |
        QualityReason.ConfigError |
        QualityReason.Stale |
        QualityReason.LastKnown |
        QualityReason.OutOfRange |
        QualityReason.NotInitialized |
        QualityReason.Simulated |
        QualityReason.Forced;

    public TagQuality(QualitySeverity severity, QualityReason reasons, uint? nativeStatus)
    {
        ValidateSeverity(severity, nameof(severity));
        if ((reasons & ~DefinedReasons) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reasons), reasons, "Quality reasons contain undefined bits.");
        }

        Severity = severity;
        Reasons = reasons;
        NativeStatus = nativeStatus;
    }

    public QualitySeverity Severity { get; }

    public QualityReason Reasons { get; }

    public uint? NativeStatus { get; }

    public static QualitySeverity WorstSeverity(QualitySeverity left, QualitySeverity right)
    {
        ValidateSeverity(left, nameof(left));
        ValidateSeverity(right, nameof(right));
        return left >= right ? left : right;
    }

    public static TagQuality Combine(TagQuality left, TagQuality right) =>
        new(WorstSeverity(left.Severity, right.Severity), left.Reasons | right.Reasons, null);

    private static void ValidateSeverity(QualitySeverity severity, string parameterName)
    {
        if (severity is not QualitySeverity.Good and not QualitySeverity.Uncertain and not QualitySeverity.Bad)
        {
            throw new ArgumentOutOfRangeException(parameterName, severity, "Quality severity is undefined.");
        }
    }
}
