using System;
using System.Collections.Generic;

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

    public const int MIN_VERSION = 1;
    public const int MAX_VERSION = 40;

    const int PENALTY_N1 = 3;
    const int PENALTY_N2 = 3;
    const int PENALTY_N3 = 40;
    const int PENALTY_N4 = 10;

    public static bool GetBit(int x, int i) => ((x >> i) & 1) != 0;

    public static QrCode EncodeText(string text, Ecc ecl)
    {
        Utils.CheckNull(text, nameof(text));

        var segs = QrSegment.MakeSegments(text);
        return EncodeSegments(segs, ecl);
    }

    public static QrCode EncodeSegments(List<QrSegment> segs, Ecc ecl)
    {
        return EncodeSegments(segs, ecl, MIN_VERSION, MAX_VERSION, -1, true);
    }

    public static QrCode EncodeSegments(List<QrSegment> segs, Ecc ecl, int minVersion, int maxVersion, int mask, bool boostEcl)
    {
        Utils.CheckNull(segs, nameof(segs));

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

        foreach (var newEcl in new Ecc[] { Ecc.Low, Ecc.Medium, Ecc.Quartitle, Ecc.High })
        {
            if (boostEcl && dataUsedBits <= GetNumDataCodewords(version, newEcl) * 8)
                ecl = newEcl;
        }

        var bb = new BitBuffer();
        foreach (var seg in segs)
        {
            bb.AppendBits(seg.Mode.ModeBits, 4);
            bb.AppendBits(seg.NumChars, seg.Mode.NumCharCountBits(version));
            bb.AppendData(seg.Data);
        }

        var dataCapacityBits = GetNumDataCodewords(version, ecl) * 8;

        bb.AppendBits(0, Math.Min(4, dataCapacityBits - bb.Length));
        bb.AppendBits(0, (8 - bb.Length % 8) % 8);

        for (var padByte = 0xEC; bb.Length < dataCapacityBits; padByte ^= 0xEC ^ 0x11)
            bb.AppendBits(padByte, 8);

        var dataCodewords = new byte[bb.Length / 8];
        for (var i = 0; i < bb.Length; i++)
            dataCodewords[i >> 3] = (byte)(dataCodewords[i >> 3] | bb.GetBit(i) << (7 - (i & 7)));

        // Create the QR Code object
        return new QrCode(version, ecl, dataCodewords, mask);
    }

    public static int GetNumDataCodewords(int ver, Ecc ecl)
    {
        return GetNumRawDataModules(ver) / 8
            - ECC_CODEWORDS_PER_BLOCK[(int)ecl][ver]
            * NUM_ERROR_CORRECTION_BLOCKS[(int)ecl][ver];
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

    private static byte[] ReedSolomonComputeDivisor(int degree)
    {
        if (degree < 1 || degree > 255)
            throw new ArgumentException("Degree out of range");

        var result = new byte[degree];
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
        return result;
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

    private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
    {
        Utils.CheckNull(data, nameof(data));
        Utils.CheckNull(divisor, nameof(divisor));

        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = (b ^ result[0]) & 0xFF;
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[result.Length - 1] = 0;
            for (int i = 0; i < result.Length; i++)
                result[i] = (byte)(result[i] ^ ReedSolomonMultiply(divisor[i] & 0xFF, factor));
        }
        return result;
    }
}