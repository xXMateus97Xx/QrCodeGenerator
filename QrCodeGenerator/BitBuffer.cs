using System;
using System.Collections;

namespace QrCodeGenerator
{
    public class BitBuffer : ICloneable
    {
        private BitArray _data;

        public BitBuffer()
        {
            _data = new BitArray(0);
            Length = 0;
        }

        public int Length { get; private set; }

        public int GetBit(int index)
        {
            if (index < 0 || index >= Length)
                throw new IndexOutOfRangeException();

            return _data[index] ? 1 : 0;
        }

        public void AppendData(BitBuffer bb)
        {
            Utils.CheckNull(bb, nameof(bb));

            if (int.MaxValue - Length < bb.Length)
			    throw new ArithmeticException("Maximum length reached");

            _data.Length += bb.Length; 

            for (var i = 0; i < bb.Length; i++, Length++)
                _data.Set(Length, bb._data[i]);
        }

        public void AppendBits(int val, int len) 
        {
            if (len < 0 || len > 31 || val >> len != 0)
                throw new ArgumentException("Value out of range");

            if (int.MaxValue - Length < len)
                throw new ArithmeticException("Maximum length reached");

            _data.Length += len; 

            for (var i = len - 1; i >= 0; i--, Length++)  // Append bit by bit
            {
                var bit = QrCode.GetBit(val, i);
                _data.Set(Length, bit);
            }
        }

        public object Clone()
        {
            var clone = new BitBuffer();
            clone._data = new BitArray(_data);
            clone.Length = Length;
            return clone;
        }
    }
}
