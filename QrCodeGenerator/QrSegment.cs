using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace QrCodeGenerator;

public sealed class QrSegment
{
    internal const string ALPHANUMERIC_CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
    private static readonly SearchValues<char> _alphanumericCharsSearchValues = SearchValues.Create(ALPHANUMERIC_CHARSET);

    private readonly Mode _mode;
    private readonly BitBuffer _data;
    private readonly int _numChars;

    internal QrSegment(Mode md, int numCh, BitBuffer data)
    {
        Utils.CheckNull(md, nameof(md));

        if (numCh < 0)
            throw new ArgumentException("Invalid value");

        _mode = md;
        _numChars = numCh;
        _data = data;
    }

    public ReadOnlyBitBuffer Data => new(_data);
    public Mode Mode => _mode;
    public int NumChars => _numChars;

    public static int GetTotalBits(ReadOnlyMemory<QrSegment> segs, int version)
    {
        long result = 0;
        for (int i = 0; i < segs.Length; i++)
        {
            var seg = segs.Span[i];
            Utils.CheckNull(seg, nameof(seg));

            var ccbits = seg.Mode.NumCharCountBits(version);
            if (seg.NumChars >= (1 << ccbits))
                return -1;

            result += 4L + ccbits + seg.Data.Length;
            if (result > int.MaxValue)
                return -1;
        }

        return (int)result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNumeric(ReadOnlySpan<char> text)
    {
        return !text.ContainsAnyExceptInRange('0', '9');
    }

    public static QrSegment MakeNumeric(ReadOnlySpan<char> digits)
    {
        if (!IsNumeric(digits))
            throw new ArgumentException("String contains non-numeric characters");

        return MakeNumericCore(digits);
    }

    private static QrSegment MakeNumericCore(ReadOnlySpan<char> digits)
    {
        var bb = new BitBuffer();
        bb.AppendNumeric(digits);

        return new QrSegment(Mode.NUMERIC, digits.Length, bb);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAlphanumeric(ReadOnlySpan<char> text)
    {
        return !text.ContainsAnyExcept(_alphanumericCharsSearchValues);
    }

    public static QrSegment MakeAlphanumeric(ReadOnlySpan<char> text)
    {
        if (!IsAlphanumeric(text))
            throw new ArgumentException("String contains unencodable characters in alphanumeric mode");

        return MakeAlphanumericCore(text);
    }

    private static QrSegment MakeAlphanumericCore(ReadOnlySpan<char> text)
    {
        var bb = new BitBuffer();
        bb.AppendAlphanumeric(text);

        return new QrSegment(Mode.ALPHANUMERIC, text.Length, bb);
    }

    public static ReadOnlyMemory<QrSegment> MakeSegments(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return Array.Empty<QrSegment>();

        var result = new QrSegment[1];

        if (IsNumeric(text))
        {
            result[0] = MakeNumericCore(text);
        }
        else if (IsAlphanumeric(text))
        {
            result[0] = MakeAlphanumericCore(text);
        }
        else
        {
            byte[] pooledArray = null;
            var utf8Size = Encoding.UTF8.GetByteCount(text);

            Span<byte> buffer = utf8Size <= 256 ? stackalloc byte[256] : (pooledArray = ArrayPool<byte>.Shared.Rent(utf8Size));

            Encoding.UTF8.TryGetBytes(text, buffer, out var written);

            result[0] = MakeBytes(buffer.Slice(0, written));

            if (pooledArray != null)
                ArrayPool<byte>.Shared.Return(pooledArray);
        }

        return result;
    }

    public static QrSegment MakeEci(int assignVal)
    {
        var bb = new BitBuffer();
        if (assignVal < 0)
            throw new ArgumentException("ECI assignment value out of range");

        if (assignVal < (1 << 7))
        {
            bb.AppendBits(assignVal, 8);
        }
        else if (assignVal < (1 << 14))
        {
            bb.AppendBits(2, 2);
            bb.AppendBits(assignVal, 14);
        }
        else if (assignVal < 1_000_000)
        {
            bb.AppendBits(6, 3);
            bb.AppendBits(assignVal, 21);
        }
        else
        {
            throw new ArgumentException("ECI assignment value out of range");
        }

        return new QrSegment(Mode.ECI, 0, bb);
    }

    public static QrSegment MakeBytes(ReadOnlySpan<byte> data)
    {
        var bb = new BitBuffer();
        bb.AppendBytes(data);

        return new QrSegment(Mode.BYTE, data.Length, bb);
    }
}