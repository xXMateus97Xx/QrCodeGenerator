using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace QrCodeGenerator;

public partial class QrCode
{
    private static sbyte[][] ECC_CODEWORDS_PER_BLOCK = {
                    //0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40    Error correction level
        new sbyte[] {-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28},  // Medium
        new sbyte[] {-1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},  // Low
        new sbyte[] {-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},  // High
        new sbyte[] {-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},  // Quartile
    };

    private static sbyte[][] NUM_ERROR_CORRECTION_BLOCKS = {
                    //0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40    Error correction level
        new sbyte[] {-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5,  5,  8,  9,  9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49},  // Medium
        new sbyte[] {-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4,  4,  4,  4,  4,  6,  6,  6,  6,  7,  8,  8,  9,  9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25},  // Low
        new sbyte[] {-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81},  // High
        new sbyte[] {-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8,  8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68},  // Quartile
    };

    private static ReadOnlySpan<byte> SVG_UTF8_HEADER => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"u8;
    private static ReadOnlySpan<byte> SVG_UTF8_HEADER2 => "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n"u8;
    private static ReadOnlySpan<byte> SVG_UTF8_SVG => "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" viewBox=\"0 0 "u8;
    private static ReadOnlySpan<byte> SVG_UTF8_SVG2 => "\" stroke=\"none\">\n"u8;
    private static ReadOnlySpan<byte> SVG_UTF8_RECT => "\t<rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\"/>\n"u8;
    private static ReadOnlySpan<byte> SVG_UTF8_PATH => "\t<path d=\""u8;
    private static ReadOnlySpan<byte> SVG_UTF8_SPACE => " "u8;
    private static ReadOnlySpan<byte> SVG_UTF8_CELL => "M"u8;
    private static ReadOnlySpan<byte> SVG_UTF8_CELL2 => ","u8;
    private static ReadOnlySpan<byte> SVG_UTF8_CELL3 => "h1v1h-1z"u8;
    private static ReadOnlySpan<byte> SVG_UTF8_END_PATH => "\" fill=\"#000000\"/>\n"u8;
    private static ReadOnlySpan<byte> SVG_UTF8_END_SVG => "</svg>\n"u8;

    public const int MIN_VERSION = 1;
    public const int MAX_VERSION = 40;

    const int PENALTY_N1 = 3;
    const int PENALTY_N2 = 3;
    const int PENALTY_N3 = 40;
    const int PENALTY_N4 = 10;

    const int MAX_ALIGN_PATTERN_POSITION = MAX_VERSION / 7 + 2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static sbyte GetCodewordPerBlock(Ecc ecl, int version)
    {
        ref var ptr = ref MemoryMarshal.GetReference<sbyte[]>(ECC_CODEWORDS_PER_BLOCK);
        ref var arr = ref MemoryMarshal.GetReference<sbyte>(Unsafe.Add(ref ptr, (int)ecl));
        return Unsafe.Add(ref arr, version);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static sbyte GetErrorCorrectionBlocks(Ecc ecl, int version)
    {
        ref var ptr = ref MemoryMarshal.GetReference<sbyte[]>(NUM_ERROR_CORRECTION_BLOCKS);
        ref var arr = ref MemoryMarshal.GetReference<sbyte>(Unsafe.Add(ref ptr, (int)ecl));
        return Unsafe.Add(ref arr, version);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBit(int x, int i) => ((x >> i) & 1) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBit(byte x, int i) => ((x >> i) & 1) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBit(ushort x, int i) => ((x >> i) & 1) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBit(uint x, int i) => ((x >> i) & 1) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBit(ulong x, int i) => ((x >> i) & 1) != 0;

    public static QrCode EncodeText(ReadOnlySpan<char> text, Ecc ecl)
    {
        var segs = QrSegment.MakeSegments(text);
        return EncodeSegments(segs, ecl);
    }

    public static QrCode EncodeSegments(ReadOnlyMemory<QrSegment> segs, Ecc ecl)
    {
        return EncodeSegments(segs, ecl, MIN_VERSION, MAX_VERSION, -1, true);
    }

    public static QrCode EncodeSegments(ReadOnlyMemory<QrSegment> segs, Ecc ecl, int minVersion, int maxVersion, int mask, bool boostEcl)
    {
        if (!(MIN_VERSION <= minVersion && minVersion <= maxVersion && maxVersion <= MAX_VERSION) || mask < -1 || mask > 7)
            throw new ArgumentException("Invalid value");

        int version, dataUsedBits;
        for (version = minVersion; ; version++)
        {
            var capacityBits = GetNumDataCodewords(version, ecl) * 8;
            dataUsedBits = QrSegment.GetTotalBits(segs, version);
            if (dataUsedBits != -1 && dataUsedBits <= capacityBits)
                break;

            if (version >= maxVersion)
            {
                var msg = "Segment too long";
                if (dataUsedBits != -1)
                    msg = string.Format("Data length = {0} bits, Max capacity = {1} bits", dataUsedBits, capacityBits);
                throw new DataTooLongException(msg);
            }
        }

        if (boostEcl)
        {
            if (ecl == Ecc.Low && dataUsedBits <= GetNumDataCodewords(version, Ecc.Medium) * 8)
                ecl = Ecc.Medium;

            if ((ecl == Ecc.Low || ecl == Ecc.Medium) && dataUsedBits <= GetNumDataCodewords(version, Ecc.Quartitle) * 8)
                ecl = Ecc.Quartitle;

            if ((ecl == Ecc.Low || ecl == Ecc.Medium || ecl == Ecc.Quartitle) && dataUsedBits <= GetNumDataCodewords(version, Ecc.High) * 8)
                ecl = Ecc.High;
        }

        var bb = new BitBuffer();
        for (var i = 0; i < segs.Length; i++)
        {
            var seg = segs.Span[i];
            var mode = seg.Mode;
            var bits = mode.ModeBits;
            var count = mode.NumCharCountBits(version);
            bits = bits << count;
            bits |= seg.NumChars;
            bb.AppendBits(bits, count + 4);
            bb.AppendData(seg.Data);
        }

        var dataCapacityBits = GetNumDataCodewords(version, ecl) * 8;

        var p = Math.Min(4, dataCapacityBits - bb.Length);
        bb.AppendBits(0UL, p + ((8 - (bb.Length + p) % 8) % 8));

        var missingPadding = dataCapacityBits - bb.Length;

        ulong paddingByte = 0xEC;
        while (missingPadding > 0)
        {
            var size = 0;
            ulong padding = 0;
            for (; size < sizeof(ulong) * 8 && size < missingPadding; size += sizeof(byte) * 8)
            {
                padding = padding << sizeof(byte) * 8;
                padding |= paddingByte;
                paddingByte ^= 0xEC ^ 0x11;
            }

            bb.AppendBits(padding, size);
            missingPadding -= size;
        }

        var dataCodewordsLength = bb.Length / 8;
        var dataCodewords = dataCodewordsLength <= 256 ? stackalloc byte[256] : new byte[dataCodewordsLength];
        ref var dataCodewordsPtr = ref MemoryMarshal.GetReference(dataCodewords);
        for (var i = 0; i < bb.Length; i++)
            Unsafe.Add(ref dataCodewordsPtr, i >> 3) = (byte)(Unsafe.Add(ref dataCodewordsPtr, i >> 3) | bb.GetBit(i) << (7 - (i & 7)));

        // Create the QR Code object
        return new QrCode(version, ecl, dataCodewords.Slice(0, dataCodewordsLength), mask);
    }

    public static int GetNumDataCodewords(int ver, Ecc ecl)
    {
        return GetNumRawDataModules(ver) / 8
            - GetCodewordPerBlock(ecl, ver)
            * GetErrorCorrectionBlocks(ecl, ver);
    }

    private static int GetNumRawDataModules(int ver)
    {
        if (ver < MIN_VERSION || ver > MAX_VERSION)
            throw new ArgumentException("Version number out of range");

        var size = ver * 4 + 17;
        var result = size * size;
        result -= 8 * 8 * 3;
        result -= 15 * 2 + 1;
        result -= (size - 16) * 2;

        if (ver >= 2)
        {
            var numAlign = ver / 7 + 2;
            result -= (numAlign - 1) * (numAlign - 1) * 25;
            result -= (numAlign - 2) * 2 * 20;

            if (ver >= 7)
                result -= 6 * 3 * 2;
        }

        return result;
    }

    private static void ReedSolomonComputeDivisor(Span<byte> result)
    {
        var degree = result.Length;
        if (degree < 1 || degree > 255)
            throw new ArgumentException("Degree out of range");

        result[degree - 1] = 1;

        var root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = (byte)ReedSolomonMultiply(result[j] & 0xFF, root);
                if (j + 1 < result.Length)
                    result[j] ^= result[j + 1];
            }
            root = ReedSolomonMultiply(root, 0x02);
        }
    }

    private static int ReedSolomonMultiply(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return z;
    }

    private static void ReedSolomonComputeRemainder(ReadOnlySpan<byte> data, ReadOnlySpan<byte> divisor, Span<byte> destiny)
    {
        ref var destinyPtr = ref MemoryMarshal.GetReference(destiny);
        ref var divisorPtr = ref MemoryMarshal.GetReference(divisor);
        for (int i = 0; i < data.Length; i++)
        {
            var b = data[i];
            var factor = (b ^ destiny[0]) & 0xFF;
            destiny.Slice(1).CopyTo(destiny);

            Unsafe.Add(ref destinyPtr, destiny.Length - 1) = 0;
            for (int j = 0; j < destiny.Length; j++)
                Unsafe.Add(ref destinyPtr, j) = (byte)(Unsafe.Add(ref destinyPtr, j) ^ ReedSolomonMultiply(Unsafe.Add(ref divisorPtr, j) & 0xFF, factor));
        }
    }
}