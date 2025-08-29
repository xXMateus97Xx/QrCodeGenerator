using System;

namespace QrCodeGenerator;

public class Mode
{
    public static readonly Mode NUMERIC = new Mode(0x1, new int[] { 10, 12, 14 });
    public static readonly Mode ALPHANUMERIC = new Mode(0x2, new int[] { 9, 11, 13 });
    public static readonly Mode BYTE = new Mode(0x4, new int[] { 8, 16, 16 });
    public static readonly Mode KANJI = new Mode(0x8, new int[] { 8, 10, 12 });
    public static readonly Mode ECI = new Mode(0x7, new int[] { 0, 0, 0 });

    public static readonly ReadOnlyMemory<Mode> All = new[] { Mode.BYTE, Mode.ALPHANUMERIC, Mode.NUMERIC, Mode.KANJI };

    private readonly int _modeBits;
    private readonly int[] _numBitsCharCount;

    private Mode(int modeBits, int[] numBitsCharCount)
    {
        _modeBits = modeBits;
        _numBitsCharCount = numBitsCharCount;
    }

    public int ModeBits => _modeBits;

    public int NumCharCountBits(int ver) => _numBitsCharCount[(int)(Math.Floor((ver + 7) / 17d))];
}