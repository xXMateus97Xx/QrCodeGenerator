using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace QrCodeGenerator;

public sealed class QrSegment
{
    public const string ALPHANUMERIC_CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    private readonly Mode _mode;
    private readonly BitBuffer _data;
    private readonly int _numChars;

    public QrSegment(Mode md, int numCh, BitBuffer data)
        : this(md, numCh, data, true)
    {
    }

    internal QrSegment(Mode md, int numCh, BitBuffer data, bool cloneData)
    {
        Utils.CheckNull(md, nameof(md));

        if (numCh < 0)
            throw new ArgumentException("Invalid value");

        _mode = md;
        _numChars = numCh;
        _data = cloneData ? new BitBuffer(data) : data;
    }

    public BitBuffer Data => new BitBuffer(_data);
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

    private static bool IsNumeric(ReadOnlySpan<char> text)
    {
        if (Vector256.IsHardwareAccelerated && text.Length >= Vector256<ushort>.Count)
        {
            var min = Vector256.Create((ushort)'0');
            var max = Vector256.Create((ushort)'9');
            while (text.Length >= Vector256<ushort>.Count)
            {
                var txt = MemoryMarshal.Cast<char, ushort>(text);
                var vec = Vector256.Create(txt);

                if (Vector256.LessThanAny(vec, min))
                    return false;

                if (Vector256.GreaterThanAny(vec, max))
                    return false;

                text = text.Slice(Vector256<ushort>.Count);
            }
        }

        if (Vector128.IsHardwareAccelerated && text.Length >= Vector128<ushort>.Count)
        {
            var min = Vector128.Create((ushort)'0');
            var max = Vector128.Create((ushort)'9');
            while (text.Length >= Vector128<ushort>.Count)
            {
                var txt = MemoryMarshal.Cast<char, ushort>(text);
                var vec = Vector128.Create(txt);

                if (Vector128.LessThanAny(vec, min))
                    return false;

                if (Vector128.GreaterThanAny(vec, max))
                    return false;

                text = text.Slice(Vector128<ushort>.Count);
            }
        }

        if (Vector64.IsHardwareAccelerated && text.Length >= Vector64<ushort>.Count)
        {
            var min = Vector64.Create((ushort)'0');
            var max = Vector64.Create((ushort)'9');
            while (text.Length >= Vector64<ushort>.Count)
            {
                var txt = MemoryMarshal.Cast<char, ushort>(text);
                var vec = Vector64.Create(txt);

                if (Vector64.LessThanAny(vec, min))
                    return false;

                if (Vector64.GreaterThanAny(vec, max))
                    return false;

                text = text.Slice(Vector64<ushort>.Count);
            }
        }

        for (int i = 0; i < text.Length; i++)
        {
            var c = (ushort)text[i];
            if (c < (ushort)'0' || c > (ushort)'9')
                return false;
        }

        return true;
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
        for (int i = 0; i < digits.Length;)
        {
            var n = Math.Min(digits.Length - i, 3);
            bb.AppendBits(int.Parse(digits.Slice(i, n)), n * 3 + 1);
            i += n;
        }
        return new QrSegment(Mode.NUMERIC, digits.Length, bb, false);
    }

    public static bool IsAlphanumeric(ReadOnlySpan<char> text)
    {
        for (int j = 0; j < text.Length; j++)
            if (!ALPHANUMERIC_CHARSET.Contains(text[j]))
                return false;

        return true;
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
        int i;
        for (i = 0; i <= text.Length - 2; i += 2)
        {
            var temp = ALPHANUMERIC_CHARSET.IndexOf(text[i]) * 45;
            temp += ALPHANUMERIC_CHARSET.IndexOf(text[i + 1]);
            bb.AppendBits(temp, 11);
        }

        if (i < text.Length)
            bb.AppendBits(ALPHANUMERIC_CHARSET.IndexOf(text[i]), 6);

        return new QrSegment(Mode.ALPHANUMERIC, text.Length, bb, false);
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
        {
            throw new ArgumentException("ECI assignment value out of range");
        }
        else if (assignVal < (1 << 7))
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

        return new QrSegment(Mode.ECI, 0, bb, false);
    }

    public static QrSegment MakeBytes(ReadOnlySpan<byte> data)
    {
        var bb = new BitBuffer();
        for (int i = 0; i < data.Length; i++)
            bb.AppendBits(data[i] & 0xFF, 8);

        return new QrSegment(Mode.BYTE, data.Length, bb, false);
    }
}