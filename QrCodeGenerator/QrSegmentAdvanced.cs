using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace QrCodeGenerator;

public static class QrSegmentAdvanced
{
    private static readonly short[] UNICODE_TO_QR_KANJI = GC.AllocateUninitializedArray<short>(1 << 16);

    // Data derived from ftp://ftp.unicode.org/Public/MAPPINGS/OBSOLETE/EASTASIA/JIS/SHIFTJIS.TXT
    static QrSegmentAdvanced()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("QrCodeGenerator.kanji.txt");

        Array.Fill<short>(UNICODE_TO_QR_KANJI, -1);

        ref var unicodeToKanji = ref MemoryMarshal.GetReference<short>(UNICODE_TO_QR_KANJI);

        Span<byte> bytes = stackalloc byte[2];

        for (var i = 0; i < s.Length; i += 2)
        {
            s.Read(bytes);

            var c = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            if (c == 0xFFFF)
                continue;

            Unsafe.Add(ref unicodeToKanji, c) = (short)(i / 2);
        }
    }

    public static ReadOnlyMemory<QrSegment> MakeSegmentsOptimally(string text, Ecc ecl, int minVersion, int maxVersion)
    {
        Utils.CheckNull(text, nameof(text));

        if (!(QrCode.MIN_VERSION <= minVersion && minVersion <= maxVersion && maxVersion <= QrCode.MAX_VERSION))
            throw new ArgumentException("Invalid value");

        ReadOnlyMemory<QrSegment> segs = null;
        var codePoints = ToCodePoints(text);
        for (int version = minVersion; ; version++)
        {
            if (version == minVersion || version == 10 || version == 27)
                segs = MakeSegmentsOptimally(codePoints, version);

            var dataCapacityBits = QrCode.GetNumDataCodewords(version, ecl) * 8;
            var dataUsedBits = QrSegment.GetTotalBits(segs, version);
            if (dataUsedBits != -1 && dataUsedBits <= dataCapacityBits)
                return segs;
            if (version >= maxVersion)
            {
                var msg = "Segment too long";
                if (dataUsedBits != -1)
                    msg = string.Format("Data length = {0} bits, Max capacity = {1} bits", dataUsedBits, dataCapacityBits);
                throw new DataTooLongException(msg);
            }
        }
    }

    public static QrSegment MakeKanji(ReadOnlySpan<char> text)
    {
        if (!IsEncodableAsKanji(text))
            throw new ArgumentException("String contains non-kanji-mode characters");

        ref var unicodeToKanji = ref MemoryMarshal.GetReference<short>(UNICODE_TO_QR_KANJI);
        var bb = new BitBuffer();
        for (int i = 0; i < text.Length; i++)
        {
            var val = Unsafe.Add(ref unicodeToKanji, text[i]);
            bb.AppendBits(val, 13);
        }
        return new QrSegment(Mode.KANJI, text.Length, bb);
    }

    public static bool IsEncodableAsKanji(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
            if (!IsKanji(text[i]))
                return false;

        return true;
    }

    private static ReadOnlyMemory<QrSegment> MakeSegmentsOptimally(ReadOnlySpan<int> codePoints, int version)
    {
        if (codePoints.Length == 0)
            return ReadOnlyMemory<QrSegment>.Empty;
        Mode[] charModes = ComputeCharacterModes(codePoints, version);
        return SplitIntoSegments(codePoints, charModes);
    }

    private static Mode[] ComputeCharacterModes(ReadOnlySpan<int> codePoints, int version)
    {
        if (codePoints.Length == 0)
            throw new ArgumentException();

        var modeTypes = Mode.All.Span;
        var numModes = modeTypes.Length;

        Span<int> headCosts = stackalloc int[numModes];
        for (int i = 0; i < numModes; i++)
            headCosts[i] = (4 + modeTypes[i].NumCharCountBits(version)) * 6;

        var charModes = new Mode[codePoints.Length, numModes];

        Span<int> prevCosts = stackalloc int[numModes];
        headCosts.CopyTo(prevCosts);

        for (int i = 0; i < codePoints.Length; i++)
        {
            var c = codePoints[i];
            var curCosts = new int[numModes];
            {
                curCosts[0] = prevCosts[0] + CountUtf8Bytes(c) * 8 * 6;
                charModes[i, 0] = modeTypes[0];
            }

            if (QrSegment.ALPHANUMERIC_CHARSET.IndexOf((char)c) != -1)
            {
                curCosts[1] = prevCosts[1] + 33;
                charModes[i, 1] = modeTypes[1];
            }
            if ('0' <= c && c <= '9')
            {
                curCosts[2] = prevCosts[2] + 20;
                charModes[i, 2] = modeTypes[2];
            }
            if (IsKanji(c))
            {
                curCosts[3] = prevCosts[3] + 78;
                charModes[i, 3] = modeTypes[3];
            }

            for (int j = 0; j < numModes; j++)
            {
                for (int k = 0; k < numModes; k++)
                {
                    var newCost = (curCosts[k] + 5) / 6 * 6 + headCosts[j];
                    if (charModes[i, k] != null && (charModes[i, j] == null || newCost < curCosts[j]))
                    {
                        curCosts[j] = newCost;
                        charModes[i, j] = modeTypes[k];
                    }
                }
            }

            prevCosts = curCosts;
        }

        Mode curMode = null;
        for (int i = 0, minCost = 0; i < numModes; i++)
        {
            if (curMode == null || prevCosts[i] < minCost)
            {
                minCost = prevCosts[i];
                curMode = modeTypes[i];
            }
        }

        var result = new Mode[charModes.Length];
        for (var i = result.Length - 1; i >= 0; i--)
        {
            for (var j = 0; j < numModes; j++)
            {
                if (modeTypes[j] == curMode)
                {
                    curMode = charModes[i, j];
                    result[i] = curMode;
                    break;
                }
            }
        }
        return result;
    }

    private static ReadOnlyMemory<QrSegment> SplitIntoSegments(ReadOnlySpan<int> codePoints, Mode[] charModes)
    {
        if (codePoints.Length == 0)
            throw new ArgumentException();

        var result = new List<QrSegment>();

        // Accumulate run of modes
        var curMode = charModes[0];
        int start = 0;
        for (int i = 1; ; i++)
        {
            if (i < codePoints.Length && charModes[i] == curMode)
                continue;

            var s = FromCodePoint(codePoints, start, i - start);
            if (curMode == Mode.BYTE)
                result.Add(QrSegment.MakeBytes(Encoding.UTF8.GetBytes(s)));
            else if (curMode == Mode.NUMERIC)
                result.Add(QrSegment.MakeNumeric(s));
            else if (curMode == Mode.ALPHANUMERIC)
                result.Add(QrSegment.MakeAlphanumeric(s));
            else if (curMode == Mode.KANJI)
                result.Add(MakeKanji(s));
            else
                throw new ApplicationException();

            if (i >= codePoints.Length)
                return result.ToArray();
            curMode = charModes[i];
            start = i;
        }
    }

    private static int[] ToCodePoints(string s)
    {
        if (!s.IsNormalized())
            s = s.Normalize();

        var chars = new List<int>((s.Length * 3) / 2);

        var ee = StringInfo.GetTextElementEnumerator(s);

        while (ee.MoveNext())
        {
            var e = ee.GetTextElement();
            var c = char.ConvertToUtf32(e, 0);
            if (char.IsSurrogate((char)c))
                throw new ArgumentException("Invalid UTF-16 string");
            chars.Add(c);
        }

        return chars.ToArray();
    }

    private static string FromCodePoint(ReadOnlySpan<int> codePoints, int start, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
            sb.Append(char.ConvertFromUtf32(codePoints[i + start]));

        return sb.ToString();
    }

    private static int CountUtf8Bytes(int cp)
    {
        if (cp < 0)
            throw new ArgumentException("Invalid code point");
        else if (cp < 0x80)
            return 1;
        else if (cp < 0x800)
            return 2;
        else if (cp < 0x10000)
            return 3;
        else if (cp < 0x110000)
            return 4;
        else
            throw new ArgumentException("Invalid code point");
    }

    private static bool IsKanji(int c) => c < UNICODE_TO_QR_KANJI.Length && UNICODE_TO_QR_KANJI[c] != -1;
}