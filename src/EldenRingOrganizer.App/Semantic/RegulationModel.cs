using System.Globalization;
using System.Security.Cryptography;

namespace EldenRingOrganizer.Semantic;

public sealed class FileFingerprint
{
    public required string Path { get; init; }
    public required long Length { get; init; }
    public required long LastWriteUtcTicks { get; init; }
    public required string Sha256 { get; init; }

    public static FileFingerprint Create(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        using var stream = File.OpenRead(fullPath);
        var hash = Convert.ToHexString(SHA256.HashData(stream));

        return new FileFingerprint
        {
            Path = fullPath,
            Length = info.Length,
            LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
            Sha256 = hash
        };
    }

    public bool SameContent(FileFingerprint other)
    {
        return Length == other.Length &&
               string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}

public enum RegulationValueKind
{
    Null,
    String,
    Boolean,
    SignedInteger,
    UnsignedInteger,
    FloatingPoint,
    Bytes,
    Other
}

public sealed class RegulationValue : IEquatable<RegulationValue>
{
    public required string TypeName { get; init; }
    public required RegulationValueKind Kind { get; init; }
    public string? Data { get; init; }

    public static RegulationValue FromObject(object? value, Type declaredType)
    {
        if (value is null)
        {
            return new RegulationValue
            {
                TypeName = declaredType.FullName ?? declaredType.Name,
                Kind = RegulationValueKind.Null
            };
        }

        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        var typeName = type.FullName ?? type.Name;

        return value switch
        {
            string text => Create(typeName, RegulationValueKind.String, text),
            bool boolean => Create(typeName, RegulationValueKind.Boolean, boolean ? "true" : "false"),
            sbyte number => Create(typeName, RegulationValueKind.SignedInteger, number.ToString(CultureInfo.InvariantCulture)),
            short number => Create(typeName, RegulationValueKind.SignedInteger, number.ToString(CultureInfo.InvariantCulture)),
            int number => Create(typeName, RegulationValueKind.SignedInteger, number.ToString(CultureInfo.InvariantCulture)),
            long number => Create(typeName, RegulationValueKind.SignedInteger, number.ToString(CultureInfo.InvariantCulture)),
            byte number => Create(typeName, RegulationValueKind.UnsignedInteger, number.ToString(CultureInfo.InvariantCulture)),
            ushort number => Create(typeName, RegulationValueKind.UnsignedInteger, number.ToString(CultureInfo.InvariantCulture)),
            uint number => Create(typeName, RegulationValueKind.UnsignedInteger, number.ToString(CultureInfo.InvariantCulture)),
            ulong number => Create(typeName, RegulationValueKind.UnsignedInteger, number.ToString(CultureInfo.InvariantCulture)),
            float number => Create(typeName, RegulationValueKind.FloatingPoint, number.ToString("R", CultureInfo.InvariantCulture)),
            double number => Create(typeName, RegulationValueKind.FloatingPoint, number.ToString("R", CultureInfo.InvariantCulture)),
            byte[] bytes => Create(typeName, RegulationValueKind.Bytes, Convert.ToBase64String(bytes)),
            IFormattable formattable => Create(typeName, RegulationValueKind.Other, formattable.ToString(null, CultureInfo.InvariantCulture)),
            _ => Create(typeName, RegulationValueKind.Other, value.ToString())
        };
    }

    private static RegulationValue Create(string typeName, RegulationValueKind kind, string? data)
    {
        return new RegulationValue
        {
            TypeName = typeName,
            Kind = kind,
            Data = data
        };
    }

    public string Format()
    {
        if (Kind == RegulationValueKind.Null)
        {
            return "—";
        }

        if (Kind == RegulationValueKind.Bytes)
        {
            if (string.IsNullOrEmpty(Data))
            {
                return "";
            }

            var bytes = Convert.FromBase64String(Data);
            return bytes.Length <= 32
                ? Convert.ToHexString(bytes)
                : $"{Convert.ToHexString(bytes.AsSpan(0, 32))}… ({bytes.Length} bytes)";
        }

        return Data ?? "";
    }

    public bool Equals(RegulationValue? other)
    {
        return other is not null &&
               Kind == other.Kind &&
               string.Equals(TypeName, other.TypeName, StringComparison.Ordinal) &&
               string.Equals(Data, other.Data, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is RegulationValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TypeName, Kind, Data);
    }
}

public sealed class RegulationDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string SourceName { get; init; }
    public required FileFingerprint Source { get; init; }
    public required ulong RegulationVersion { get; init; }
    public required string RegulationVersionDisplay { get; init; }
    public required List<RegulationParam> Params { get; init; }
}

public sealed class RegulationParam
{
    public required string Name { get; init; }
    public string? ParamType { get; init; }
    public required List<RegulationRow> Rows { get; init; }
}

public sealed class RegulationRow
{
    public required int Id { get; init; }
    public string? Name { get; init; }
    public required int RowIndex { get; init; }
    public required int Occurrence { get; init; }
    public required List<RegulationField> Fields { get; init; }
}

public sealed class RegulationField
{
    public required string InternalName { get; init; }
    public required string FieldType { get; init; }
    public required int FieldIndex { get; init; }
    public required RegulationValue Value { get; init; }
}

public enum RegulationRowChangeKind
{
    Added,
    Modified,
    Removed
}

public sealed class RegulationDelta
{
    public required FileFingerprint ModSource { get; init; }
    public required FileFingerprint VanillaSource { get; init; }
    public required ulong ModRegulationVersion { get; init; }
    public required ulong VanillaRegulationVersion { get; init; }
    public required List<RegulationParamDelta> Params { get; init; }
}

public sealed class RegulationParamDelta
{
    public required string Name { get; init; }
    public string? ParamType { get; init; }
    public required List<RegulationRowDelta> Rows { get; init; }
}

public sealed class RegulationRowDelta
{
    public required int Id { get; init; }
    public required int Occurrence { get; init; }
    public int? ModRowIndex { get; init; }
    public int? VanillaRowIndex { get; init; }
    public string? ModName { get; init; }
    public string? VanillaName { get; init; }
    public required RegulationRowChangeKind Kind { get; init; }
    public required List<RegulationFieldDelta> Fields { get; init; }
}

public sealed class RegulationFieldDelta
{
    public required string InternalName { get; init; }
    public required string FieldType { get; init; }
    public RegulationValue? VanillaValue { get; init; }
    public RegulationValue? ModValue { get; init; }
}

public sealed class RegulationIndex
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required DateTime CreatedAtUtc { get; init; }
    public required RegulationDocument Document { get; init; }
    public required RegulationDelta Delta { get; init; }
}
