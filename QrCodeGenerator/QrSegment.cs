using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QrCodeGenerator;

public sealed class QrSegment
{
    public const string ALPHANUMERIC_CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    private readonly Mode _mode;
    private readonly int _numChars;
    private readonly BitBuffer _data;

    public QrSegment(Mode md, int numCh, BitBuffer data)
        : this(md, numCh, data, true)
    {
    }

    internal QrSegment(Mode md, int numCh, BitBuffer data, bool cloneData)
    {
        Utils.CheckNull(md, nameof(md));
        Utils.CheckNull(data, nameof(data));

        if (numCh < 0)
            throw new ArgumentException("Invalid value");

        _mode = md;
        _numChars = numCh;
        _data = cloneData ? (BitBuffer)data.Clone() : data;
    }

    public BitBuffer Data => (BitBuffer)_data.Clone();
    public Mode Mode => _mode;
    public int NumChars => _numChars;

    public static int GetTotalBits(List<QrSegment> segs, int version)
    {
        Utils.CheckNull(segs, nameof(segs));

        long result = 0;
        foreach (var seg in segs)
        {
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

    public static QrSegment MakeNumeric(string digits)
    {
        Utils.CheckNull(digits, nameof(digits));
        if (digits.Any(c => !char.IsDigit(c)))
            throw new ArgumentException("String contains non-numeric characters");

        var bb = new BitBuffer();
        for (int i = 0; i < digits.Length;)
        {
            var n = Math.Min(digits.Length - i, 3);
            bb.AppendBits(int.Parse(digits.AsSpan().Slice(i, n)), n * 3 + 1);
            i += n;
        }
        return new QrSegment(Mode.NUMERIC, digits.Length, bb, false);
    }

    public static QrSegment MakeAlphanumeric(string text)
    {
        Utils.CheckNull(text, nameof(text));

        if (text.Any(c => !ALPHANUMERIC_CHARSET.Contains(c)))
            throw new ArgumentException("String contains unencodable characters in alphanumeric mode");

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

    public static List<QrSegment> MakeSegments(string text)
    {
        Utils.CheckNull(text, nameof(text));

        var result = new List<QrSegment>();
        if (text == string.Empty)
            return result;

        if (text.All(c => char.IsDigit(c)))
        {
            result.Add(MakeNumeric(text));
        }
        else if (text.All(c => ALPHANUMERIC_CHARSET.Contains(c)))
        {
            result.Add(MakeAlphanumeric(text));
        }
        else
        {
            byte[] pooledArray = null;
            var utf8Size = Encoding.UTF8.GetByteCount(text);

            Span<byte> buffer = utf8Size <= 256 ? stackalloc byte[256] : (pooledArray = ArrayPool<byte>.Shared.Rent(utf8Size));

            Encoding.UTF8.TryGetBytes(text, buffer, out var written);

            result.Add(MakeBytes(buffer.Slice(0, written)));

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
        foreach (var b in data)
            bb.AppendBits(b & 0xFF, 8);

        return new QrSegment(Mode.BYTE, data.Length, bb, false);
    }
}