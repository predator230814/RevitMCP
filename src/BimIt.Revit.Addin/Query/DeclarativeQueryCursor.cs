using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BimIt.Revit.Addin.Query;

/// <summary>
/// Stable identifiers of the ADR-0023 declarative query sources that mint cursors.
/// A source identifier is signed into every cursor and prefixed to every query fingerprint,
/// so a cursor minted for one source never validates for another.
/// It is internal and never appears in MCP output.
/// </summary>
internal static class DeclarativeQuerySources
{
    /// <summary>CAP-0014 <c>revit_query_family_types</c>.</summary>
    public const string FamilyTypes = "family_types";

    public const int MaxLength = 64;

    public static bool IsValid(string? source)
    {
        if (source is null || source.Length is < 1 or > MaxLength)
        {
            return false;
        }

        foreach (var character in source)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    public static void EnsureValid(string? source)
    {
        if (!IsValid(source))
        {
            throw new ArgumentException("A declarative query source identifier is required.", nameof(source));
        }
    }
}

/// <summary>
/// Builds the 32-byte query fingerprint of a declarative query source.
/// The source identifier is always the first field. Every field is length-prefixed
/// or marked absent, so field values cannot run into each other.
/// Only the fields that define the matched set belong here, never the page size.
/// </summary>
internal sealed class DeclarativeQueryFingerprintBuilder
{
    private readonly StringBuilder _text = new();

    public DeclarativeQueryFingerprintBuilder(string source)
    {
        DeclarativeQuerySources.EnsureValid(source);
        Value(source);
    }

    /// <summary>Appends an optional value; <see langword="null"/> is distinct from every string.</summary>
    public DeclarativeQueryFingerprintBuilder Value(string? value)
    {
        if (value is null)
        {
            _text.Append('-');
        }
        else
        {
            AppendText(value);
        }

        _text.Append('\n');
        return this;
    }

    /// <summary>Appends an optional set of values, compared as an ordinal set (order-insensitive).</summary>
    public DeclarativeQueryFingerprintBuilder Set(IEnumerable<string>? values)
    {
        if (values is null)
        {
            _text.Append('-');
            _text.Append('\n');
            return this;
        }

        var ordered = values.OrderBy(static value => value, StringComparer.Ordinal).ToList();
        _text.Append(ordered.Count.ToString(CultureInfo.InvariantCulture));
        _text.Append('#');
        foreach (var value in ordered)
        {
            AppendText(value);
            _text.Append(';');
        }

        _text.Append('\n');
        return this;
    }

    public byte[] Compute()
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(_text.ToString()));
    }

    private void AppendText(string value)
    {
        _text.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        _text.Append(':');
        _text.Append(value);
    }
}

internal readonly record struct DeclarativeQueryCursorPayload(
    string Source,
    string InstanceId,
    string DocumentId,
    byte[] Fingerprint,
    long Revision,
    int Offset);

/// <summary>
/// Stateless HMAC-SHA256 cursor shared by the declarative query sources.
/// The signing key lives only for this process and is not serialized.
/// The signed body carries the source identifier, so a cursor of one source
/// never opens as a cursor of another, even under the same key.
/// This type covers only cursor mechanics. Filtering, ordering, summaries,
/// and the source's own error messages stay with each source.
/// </summary>
internal sealed class DeclarativeQueryCursorCodec
{
    public const int MaxCursorLength = 2048;

    public const int FingerprintLength = 32;

    /// <summary>Body format version. Version 1 was the CAP-0014-only body without a source.</summary>
    internal const byte Version = 2;

    private readonly byte[] _key;

    public DeclarativeQueryCursorCodec()
        : this(RandomNumberGenerator.GetBytes(32))
    {
    }

    internal DeclarativeQueryCursorCodec(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new ArgumentException("A 32-byte cursor key is required.", nameof(key));
        }

        _key = key.ToArray();
    }

    /// <summary>
    /// Mints a cursor. Returns <see langword="false"/> when the payload cannot be encoded
    /// within the cursor limits; the caller reports that in its own terms.
    /// </summary>
    public bool TryMint(DeclarativeQueryCursorPayload payload, out string token)
    {
        DeclarativeQuerySources.EnsureValid(payload.Source);
        ArgumentNullException.ThrowIfNull(payload.InstanceId);
        ArgumentNullException.ThrowIfNull(payload.DocumentId);
        if (payload.Offset < 0 || payload.Fingerprint is null || payload.Fingerprint.Length != FingerprintLength)
        {
            throw new ArgumentException("The cursor payload is not valid.", nameof(payload));
        }

        token = "";
        if (!TryEncode(payload, out var body))
        {
            return false;
        }

        var mac = HMACSHA256.HashData(_key, body);
        var minted = Base64Url.Encode(body) + "." + Base64Url.Encode(mac);
        if (minted.Length > MaxCursorLength)
        {
            return false;
        }

        token = minted;
        return true;
    }

    /// <summary>
    /// Opens a cursor issued by this codec for <paramref name="expectedSource"/>.
    /// Returns <see langword="false"/> for a malformed, forged, wrong-version, or other-source cursor.
    /// Instance, document, fingerprint, and revision checks stay with the caller, in its validation order.
    /// </summary>
    public bool TryOpen(string? token, string expectedSource, out DeclarativeQueryCursorPayload payload)
    {
        DeclarativeQuerySources.EnsureValid(expectedSource);
        payload = default;
        if (string.IsNullOrEmpty(token) || token.Length > MaxCursorLength)
        {
            return false;
        }

        var split = token.IndexOf('.');
        if (split <= 0 || split != token.LastIndexOf('.') || split == token.Length - 1)
        {
            return false;
        }

        if (!Base64Url.TryDecode(token[..split], out var body) || !Base64Url.TryDecode(token[(split + 1)..], out var mac))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_key, body);
        if (mac.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(mac, expected))
        {
            return false;
        }

        if (!TryDecode(body, out var decoded) || !string.Equals(decoded.Source, expectedSource, StringComparison.Ordinal))
        {
            return false;
        }

        payload = decoded;
        return true;
    }

    private static bool TryEncode(DeclarativeQueryCursorPayload payload, out byte[] buffer)
    {
        buffer = [];
        var source = Encoding.UTF8.GetBytes(payload.Source);
        var instance = Encoding.UTF8.GetBytes(payload.InstanceId);
        var document = Encoding.UTF8.GetBytes(payload.DocumentId);
        if (instance.Length > ushort.MaxValue || document.Length > ushort.MaxValue)
        {
            return false;
        }

        buffer = new byte[1 + 2 + source.Length + 2 + instance.Length + 2 + document.Length + FingerprintLength + 8 + 4];
        buffer[0] = Version;
        var offset = 1;
        WriteText(buffer, ref offset, source);
        WriteText(buffer, ref offset, instance);
        WriteText(buffer, ref offset, document);
        payload.Fingerprint.CopyTo(buffer.AsSpan(offset));
        offset += FingerprintLength;
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset), payload.Revision);
        offset += 8;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), payload.Offset);
        return true;
    }

    private static void WriteText(byte[] buffer, ref int offset, byte[] text)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), (ushort)text.Length);
        offset += 2;
        text.CopyTo(buffer.AsSpan(offset));
        offset += text.Length;
    }

    private static bool TryDecode(byte[] body, out DeclarativeQueryCursorPayload payload)
    {
        payload = default;
        if (body.Length < 1 + 2 + 2 + 2 + FingerprintLength + 8 + 4 || body[0] != Version)
        {
            return false;
        }

        var offset = 1;
        if (!TryReadText(body, ref offset, out var source)
            || !DeclarativeQuerySources.IsValid(source)
            || !TryReadText(body, ref offset, out var instanceId)
            || !TryReadText(body, ref offset, out var documentId))
        {
            return false;
        }

        if (body.Length - offset < FingerprintLength + 8 + 4)
        {
            return false;
        }

        var fingerprint = body.AsSpan(offset, FingerprintLength).ToArray();
        offset += FingerprintLength;
        var revision = BinaryPrimitives.ReadInt64BigEndian(body.AsSpan(offset));
        offset += 8;
        var rowOffset = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(offset));
        offset += 4;
        if (offset != body.Length || rowOffset < 0)
        {
            return false;
        }

        payload = new DeclarativeQueryCursorPayload(source, instanceId, documentId, fingerprint, revision, rowOffset);
        return true;
    }

    private static bool TryReadText(byte[] body, ref int offset, out string text)
    {
        text = "";
        if (body.Length - offset < 2)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(offset));
        offset += 2;
        if (body.Length - offset < length)
        {
            return false;
        }

        text = Encoding.UTF8.GetString(body, offset, length);
        offset += length;
        return true;
    }

    internal static class Base64Url
    {
        public static string Encode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static bool TryDecode(string text, out byte[] bytes)
        {
            bytes = [];
            if (text.Length == 0)
            {
                return false;
            }

            var padded = text.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
                case 0:
                    break;
                default:
                    return false;
            }

            try
            {
                bytes = Convert.FromBase64String(padded);
            }
            catch (FormatException)
            {
                bytes = [];
                return false;
            }

            // Only the canonical encoding is accepted: unused trailing bits must be zero,
            // so one cursor has exactly one textual form.
            if (!string.Equals(Encode(bytes), text, StringComparison.Ordinal))
            {
                bytes = [];
                return false;
            }

            return true;
        }
    }
}
