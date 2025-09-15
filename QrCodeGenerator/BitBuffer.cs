using System;
using System.Buffers.Binary;
using System.Collections;

namespace QrCodeGenerator;

public readonly struct BitBuffer
{
    private readonly BitArray _data;

    public BitBuffer()
    {
        _data = new BitArray(0);
    }

    public BitBuffer(BitBuffer bitBuffer)
    {
        _data = new BitArray(bitBuffer._data);
    }

    public int Length => _data.Length;

    public int GetBit(int index)
    {
        if (index < 0 || index >= Length)
            throw new IndexOutOfRangeException();

        return _data[index] ? 1 : 0;
    }

    public void AppendData(BitBuffer bb)
    {
        var data = _data;
        if (int.MaxValue - data.Length < bb.Length)
            throw new ArithmeticException("Maximum length reached");

        var position = data.Length;
        data.Length += bb.Length;

        for (var i = 0; i < bb.Length; i++, position++)
            data.Set(position, bb._data[i]);
    }

    public void AppendAlphanumeric(ReadOnlySpan<char> text)
    {
        var data = _data;
        var position = data.Length;
        var len = text.Length;
        var size = (len / 2) * 11 + (len % 2 * 6);

        data.Length += size;

        int i;
        for (i = 0; i <= text.Length - 2; i += 2)
        {
            var val = QrSegment.ALPHANUMERIC_CHARSET.IndexOf(text[i]) * 45;
            val += QrSegment.ALPHANUMERIC_CHARSET.IndexOf(text[i + 1]);

            for (var p = 10; p >= 0; p--, position++)
            {
                var bit = QrCode.GetBit(val, p);
                data.Set(position, bit);
            }
        }

        if (i < text.Length)
        {
            var val = QrSegment.ALPHANUMERIC_CHARSET.IndexOf(text[i]);
            for (var p = 5; p >= 0; p--, position++)
            {
                var bit = QrCode.GetBit(val, p);
                data.Set(position, bit);
            }
        }
    }

    public void AppendBits(int val, int len)
    {
        if (len < 0 || len > 31 || val >> len != 0)
            throw new ArgumentException("Value out of range");

        var data = _data;
        if (int.MaxValue - data.Length < len)
            throw new ArithmeticException("Maximum length reached");

        var position = data.Length;
        data.Length += len;

        for (var i = len - 1; i >= 0; i--, position++)  // Append bit by bit
        {
            var bit = QrCode.GetBit(val, i);
            data.Set(position, bit);
        }
    }

    public void AppendNumeric(ReadOnlySpan<char> digits)
    {
        var data = _data;
        var position = data.Length;
        var len = digits.Length;
        var calculatedLength = (len / 3) * 10;
        var lastBytes = 0;
        if (len % 3 > 0)
        {
            lastBytes = ((len - ((len - 1) - ((len - 1) % 3))) * 3 + 1);
            calculatedLength += lastBytes;
        }

        data.Length += calculatedLength;

        while (digits.Length >= 3)
        {
            var val = ushort.Parse(digits.Slice(0, 3));
            digits = digits.Slice(3);

            for (var i = 9; i >= 0; i--, position++)
            {
                var bit = QrCode.GetBit(val, i);
                data.Set(position, bit);
            }
        }

        if (digits.Length > 0)
        {
            var val = ushort.Parse(digits);
            for (var i = lastBytes - 1; i >= 0; i--, position++)
            {
                var bit = QrCode.GetBit(val, i);
                data.Set(position, bit);
            }
        }
    }

    public void AppendBytes(ReadOnlySpan<byte> bytes)
    {
        var data = _data;

        var position = data.Length;
        data.Length += bytes.Length * 8;

        while (bytes.Length >= sizeof(ulong))
        {
            var val = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(0, sizeof(ulong)));
            bytes = bytes.Slice(sizeof(ulong));

            for (var i = sizeof(ulong) * 8 - 1; i >= 0; i--, position++)
            {
                var bit = QrCode.GetBit(val, i);
                data.Set(position, bit);
            }
        }

        if (bytes.Length >= sizeof(uint))
        {
            var val = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(0, sizeof(uint)));
            bytes = bytes.Slice(sizeof(uint));

            for (var i = sizeof(uint) * 8 - 1; i >= 0; i--, position++)
            {
                var bit = QrCode.GetBit(val, i);
                data.Set(position, bit);
            }
        }

        if (bytes.Length >= sizeof(ushort))
        {
            var val = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(0, sizeof(ushort)));
            bytes = bytes.Slice(sizeof(ushort));

            for (var i = sizeof(ushort) * 8 - 1; i >= 0; i--, position++)
            {
                var bit = QrCode.GetBit(val, i);
                data.Set(position, bit);
            }
        }

        if (bytes.Length >= sizeof(byte))
        {
            var val = bytes[0];

            for (var i = sizeof(byte) * 8 - 1; i >= 0; i--, position++)
            {
                var bit = QrCode.GetBit(val, i);
                data.Set(position, bit);
            }
        }
    }
}
