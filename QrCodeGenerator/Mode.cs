using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace QrCodeGenerator;

public enum Mode
{
    Numeric = 1,
    Alphanumeric = 2,
    Byte = 4,
    Kanji = 8,
    ECI = 7
}

public static class ModeExtensions
{
    private static readonly int[] CharCountBits =
        [
            -1, -1, -1,
            10, 12, 14,
             9, 11, 13,
            -1, -1, -1,
             8, 16, 16,
            -1, -1, -1,
            -1, -1, -1,
             0,  0,  0,
             8,  10, 12
        ];

    private static readonly ReadOnlyMemory<Mode> _all = new[] { Mode.Byte, Mode.Alphanumeric, Mode.Numeric, Mode.Kanji };

    public static int NumCharCountBits(this Mode mode, int ver)
    {
        var pos = (int)mode * 3 + (int)Math.Floor((ver + 7) / 17d);
        return Unsafe.Add(ref MemoryMarshal.GetReference(CharCountBits), pos);
    }

    extension(Mode)
    {
        public static ReadOnlyMemory<Mode> All => _all;
    }
}