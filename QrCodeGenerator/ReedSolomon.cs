using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace QrCodeGenerator;

public static class ReedSolomon
{
    public static void ReedSolomonComputeDivisor(Span<byte> result)
    {
        var degree = result.Length;
        if (degree < 1 || degree > 255)
            throw new ArgumentException("Degree out of range");

        result[degree - 1] = 1;

        if (Vector128.IsHardwareAccelerated || Vector256.IsHardwareAccelerated)
        {
            ReedSolomonComputeDivisorFast(result);
            return;
        }

        var root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = ReedSolomonMultiply(result[j], root);
                if (j + 1 < result.Length)
                    result[j] ^= result[j + 1];
            }

            root = ReedSolomonMultiply(root, 0x02);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReedSolomonComputeDivisorFast(Span<byte> result)
    {
        var degree = result.Length;

        short root = 1;
        for (int i = 0; i < degree; i++)
        {
            var copy = result;

            if (Vector256.IsHardwareAccelerated && copy.Length >= Vector256<short>.Count)
            {
                var rootVec = Vector256.Create(root);

                while (copy.Length >= Vector256<short>.Count)
                {
                    var v = Vector256.Create(copy[0], copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7],
                        copy[8], copy[9], copy[10], copy[11], copy[12], copy[13], copy[14], copy[15]);
                    v = ReedSolomonMultiply(v, rootVec);

                    Vector256<short> v2;
                    if (copy.Length > Vector256<short>.Count)
                        v2 = Vector256.Create(copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7], copy[8],
                                copy[9], copy[10], copy[11], copy[12], copy[13], copy[14], copy[15], copy[16]);
                    else
                        v2 = Vector256.Create(copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7],
                            copy[8], copy[9], copy[10], copy[11], copy[12], copy[13], copy[14], copy[15], 0);

                    v ^= v2;
                    var byteV = v.AsByte();

                    copy[0] = byteV[0];
                    copy[1] = byteV[2];
                    copy[2] = byteV[4];
                    copy[3] = byteV[6];
                    copy[4] = byteV[8];
                    copy[5] = byteV[10];
                    copy[6] = byteV[12];
                    copy[7] = byteV[14];
                    copy[8] = byteV[16];
                    copy[9] = byteV[18];
                    copy[10] = byteV[20];
                    copy[11] = byteV[22];
                    copy[12] = byteV[24];
                    copy[13] = byteV[26];
                    copy[14] = byteV[28];
                    copy[15] = byteV[30];

                    copy = copy.Slice(Vector256<short>.Count);
                }
            }

            if (Vector128.IsHardwareAccelerated && copy.Length >= Vector128<short>.Count)
            {
                var rootVec = Vector128.Create(root);

                while (copy.Length >= Vector128<short>.Count)
                {
                    var v = Vector128.Create(copy[0], copy[1], copy[2], copy[3],
                        copy[4], copy[5], copy[6], copy[7]);
                    v = ReedSolomonMultiply(v, rootVec);

                    Vector128<short> v2;
                    if (copy.Length > Vector128<short>.Count)
                        v2 = Vector128.Create(copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7], copy[8]);
                    else
                        v2 = Vector128.Create(copy[1], copy[2], copy[3], copy[4], copy[5], copy[6], copy[7], 0);

                    v ^= v2;
                    var byteV = v.AsByte();

                    copy[0] = byteV[0];
                    copy[1] = byteV[2];
                    copy[2] = byteV[4];
                    copy[3] = byteV[6];
                    copy[4] = byteV[8];
                    copy[5] = byteV[10];
                    copy[6] = byteV[12];
                    copy[7] = byteV[14];

                    copy = copy.Slice(Vector128<short>.Count);
                }
            }

            if (copy.Length > 0)
            {
                for (var j = 0; j < copy.Length; j++)
                {
                    copy[j] = ReedSolomonMultiply(copy[j], root);
                    if (j + 1 < copy.Length)
                        copy[j] ^= copy[j + 1];
                }
            }

            root = ReedSolomonMultiply(root, 0x02);
        }
    }

    private static Vector128<short> ReedSolomonMultiply(Vector128<short> x, Vector128<short> y)
    {
        if (x == Vector128<short>.Zero)
            return Vector128<short>.Zero;

        var z = Vector128<short>.Zero;
        var one = Vector128<short>.One;
        z ^= ((y >> 7) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 6) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 5) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 4) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 3) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 2) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 1) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= (y & one) * x;

        return z;
    }

    private static Vector256<short> ReedSolomonMultiply(Vector256<short> x, Vector256<short> y)
    {
        if (x == Vector256<short>.Zero)
            return Vector256<short>.Zero;

        var z = Vector256<short>.Zero;
        var one = Vector256<short>.One;
        z ^= ((y >> 7) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 6) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 5) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 4) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 3) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 2) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 1) & one) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= (y & one) * x;

        return z;
    }

    private static byte ReedSolomonMultiply(int x, int y)
    {
        if (x == 0) return 0;

        var z = 0;
        z ^= ((y >> 7) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 6) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 5) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 4) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 3) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 2) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= ((y >> 1) & 1) * x;

        z = (z << 1) ^ ((z >> 7) * 0x11D);
        z ^= (y & 1) * x;

        return (byte)z;
    }

    public static void ReedSolomonComputeRemainder(ReadOnlySpan<byte> data, ReadOnlySpan<byte> divisor, Span<byte> destiny)
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
