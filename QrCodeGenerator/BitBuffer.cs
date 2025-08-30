using System;
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
}
