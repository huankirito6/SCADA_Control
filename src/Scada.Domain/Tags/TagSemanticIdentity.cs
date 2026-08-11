using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Scada.Domain.Tags;

public sealed record TagSemanticIdentity(string SourceBindingHash, string ValueMeaningHash, string PhysicalTargetDigest)
{
    public static TagSemanticIdentity Create(
        string endpoint,
        string unitOrNode,
        string address,
        string dataType,
        double scaling,
        string engineeringUnit,
        string physicalTargetDigest)
    {
        ValidateText(endpoint, nameof(endpoint));
        ValidateText(unitOrNode, nameof(unitOrNode));
        ValidateText(address, nameof(address));
        ValidateText(dataType, nameof(dataType));
        if (!double.IsFinite(scaling))
        {
            throw new ArgumentOutOfRangeException(nameof(scaling), scaling, "Scaling must be finite.");
        }

        ValidateText(engineeringUnit, nameof(engineeringUnit));
        ValidateText(physicalTargetDigest, nameof(physicalTargetDigest));

        return new TagSemanticIdentity(
            HashTuple(endpoint, unitOrNode, address),
            HashTuple(dataType, scaling.ToString("R", CultureInfo.InvariantCulture), engineeringUnit),
            physicalTargetDigest);
    }

    private static string HashTuple(params string[] fields)
    {
        using var bytes = new MemoryStream();
        var length = new byte[sizeof(int)];
        foreach (var field in fields)
        {
            var utf8 = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(length, utf8.Length);
            bytes.Write(length);
            bytes.Write(utf8);
        }

        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }

    private static void ValidateText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A semantic identity field must not be null, empty, or whitespace.", parameterName);
        }
    }
}
