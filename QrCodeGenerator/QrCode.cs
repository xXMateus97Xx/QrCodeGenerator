using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace QrCodeGenerator;

public partial class QrCode
{
    private readonly int _version;
    private readonly int _size;
    private readonly Ecc _errorCorrectionLevel;
    private readonly int _mask;
    private readonly bool[,] _modules;

    public QrCode(int ver, Ecc ecl, ReadOnlySpan<byte> dataCodewords, int msk)
    {
        if (ver < MIN_VERSION || ver > MAX_VERSION)
            throw new ArgumentException("Version value out of range");
        if (msk < -1 || msk > 7)
            throw new ArgumentException("Mask value out of range");

        _version = ver;
        _size = ver * 4 + 17;
        _errorCorrectionLevel = ecl;
        _modules = new bool[_size, _size];
        var isFunction = new bool[_size, _size];

        DrawFunctionPatterns(isFunction);
        var rawCodewords = GetNumRawDataModules(ver) / 8;
        byte[] pooledArray = null;
        Span<byte> allCodewords = rawCodewords <= 512 ? stackalloc byte[512] : (pooledArray = ArrayPool<byte>.Shared.Rent(rawCodewords));
        allCodewords = allCodewords.Slice(0, rawCodewords);
        AddEccAndInterleave(dataCodewords, allCodewords);
        DrawCodewords(allCodewords, isFunction);
        _mask = HandleConstructorMasking(msk, isFunction);
        if (pooledArray != null)
            ArrayPool<byte>.Shared.Return(pooledArray);
    }

    public String ToSvgString(int border)
    {
        if (border < 0)
            throw new ArgumentException("Border must be non-negative");

        var size = _size;

        var sb = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
            .Append("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n")
            .AppendFormat("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" viewBox=\"0 0 {0} {0}\" stroke=\"none\">\n",
                size + border * 2)
            .Append("\t<rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\"/>\n")
            .Append("\t<path d=\"");

        var modules = _modules;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (GetModule(x, y, size, modules))
                {
                    if (x != 0 || y != 0)
                        sb.Append(" ");
                    sb.AppendFormat("M{0},{1}h1v1h-1z", x + border, y + border);
                }
            }
        }
        return sb
            .Append("\" fill=\"#000000\"/>\n")
            .Append("</svg>\n")
            .ToString();
    }

    public static bool GetModule(int x, int y, int size, bool[,] modules) => 0 <= x && x < size && 0 <= y && y < size && modules[y, x];

    public Image ToImage(int scale, int border)
    {
        if (scale <= 0 || border < 0)
            throw new ArgumentException("Value out of range");

        var thisSize = _size;

        if (border > int.MaxValue / 2 || thisSize + border * 2L > int.MaxValue / scale)
            throw new ArgumentException("Scale or border too large");

        var size = (thisSize + border * 2) * scale;
        var modules = _modules;
        var result = new Image<Rgba32>(size, size, new Rgba32(255, 255, 255));
        var black = new Rgba32(0, 0, 0);
        for (var y = 0; y < result.Height; y++)
        {
            for (var x = 0; x < result.Width; x++)
            {
                var color = GetModule(x / scale - border, y / scale - border, thisSize, modules);
                if (color)
                    result[x, y] = black;
            }
        }
        return result;
    }

    private void AddEccAndInterleave(ReadOnlySpan<byte> data, Span<byte> result)
    {
        var ecl = _errorCorrectionLevel;
        var version = _version;

        if (data.Length != GetNumDataCodewords(version, ecl))
            throw new ArgumentException();

        // Calculate parameter numbers
        var numBlocks = NUM_ERROR_CORRECTION_BLOCKS[(int)ecl][version];
        var blockEccLen = ECC_CODEWORDS_PER_BLOCK[(int)ecl][version];
        var rawCodewords = result.Length;
        var numShortBlocks = numBlocks - rawCodewords % numBlocks;
        var shortBlockLen = rawCodewords / numBlocks;

        // Split data into blocks and append ECC to each block
        var blocks = new byte[numBlocks][];
        ref var blocksPtr = ref MemoryMarshal.GetReference<byte[]>(blocks);
        Span<byte> rsDiv = stackalloc byte[blockEccLen];
        ReedSolomonComputeDivisor(rsDiv);

        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            var datLength = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);

            var block = new byte[shortBlockLen + 1];
            var dat = data.Slice(k).Slice(0, datLength);
            dat.CopyTo(block);

            ReedSolomonComputeRemainder(dat, rsDiv, block.AsSpan(block.Length - blockEccLen));

            k += datLength;
            Unsafe.Add(ref blocksPtr, i) = block;
        }

        // Interleave (not concatenate) the bytes from every block into a single sequence
        ref var resultPtr = ref MemoryMarshal.GetReference<byte>(result);
        for (int i = 0, k = 0; i < blocks[0].Length; i++)
        {
            for (int j = 0; j < blocks.Length; j++)
            {
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                {
                    ref var block = ref Unsafe.Add(ref blocksPtr, j);
                    ref var item = ref MemoryMarshal.GetReference<byte>(block);
                    item = Unsafe.Add(ref item, i);
                    Unsafe.Add(ref resultPtr, k) = item;
                    k++;
                }
            }
        }
    }

    private void DrawFunctionPatterns(bool[,] isFunction)
    {
        var modules = _modules;
        var size = _size;

        for (var i = 0; i < size; i++)
        {
            SetFunctionModule(6, i, i % 2 == 0, modules, isFunction);
            SetFunctionModule(i, 6, i % 2 == 0, modules, isFunction);
        }

        // Draw 3 finder patterns (all corners except bottom right; overwrites some timing modules)
        DrawFinderPattern(3, 3, isFunction);
        DrawFinderPattern(size - 4, 3, isFunction);
        DrawFinderPattern(3, size - 4, isFunction);

        // Draw numerous alignment patterns
        Span<int> alignPatPos = stackalloc int[MAX_ALIGN_PATTERN_POSITION];
        alignPatPos = alignPatPos.Slice(0, GetAlignmentPatternPositionsLength());
        GetAlignmentPatternPositions(alignPatPos);
        var numAlign = alignPatPos.Length;
        for (var i = 0; i < numAlign; i++)
        {
            for (var j = 0; j < numAlign; j++)
            {
                // Don't draw on the three finder corners
                if (!(i == 0 && j == 0 || i == 0 && j == numAlign - 1 || i == numAlign - 1 && j == 0))
                    DrawAlignmentPattern(alignPatPos[i], alignPatPos[j], isFunction);
            }
        }

        // Draw configuration data
        DrawFormatBits(0, isFunction);  // Dummy mask value; overwritten later in the constructor
        DrawVersion(isFunction);
    }

    private void DrawCodewords(ReadOnlySpan<byte> data, bool[,] isFunction)
    {
        if (data.Length != GetNumRawDataModules(_version) / 8)
            throw new ArgumentException();

        var i = 0;

        var modules = _modules;
        var size = _size;

        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
                right = 5;

            for (var vert = 0; vert < size; vert++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vert : vert;
                    if (!isFunction[y, x] && i < data.Length * 8)
                    {
                        modules[y, x] = GetBit(data[i >> 3], 7 - (i & 7));
                        i++;
                    }
                }
            }
        }
    }

    private int HandleConstructorMasking(int msk, bool[,] isFunction)
    {
        if (msk == -1)
        {
            int minPenalty = int.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                ApplyMask(i, isFunction);
                DrawFormatBits(i, isFunction);
                int penalty = GetPenaltyScore();
                if (penalty < minPenalty)
                {
                    msk = i;
                    minPenalty = penalty;
                }
                ApplyMask(i, isFunction);
            }
        }

        ApplyMask(msk, isFunction);
        DrawFormatBits(msk, isFunction);
        return msk;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetFunctionModule(int x, int y, bool isBlack, bool[,] modules, bool[,] isFunction)
    {
        modules[y, x] = isBlack;
        isFunction[y, x] = true;
    }

    private void DrawFinderPattern(int x, int y, bool[,] isFunction)
    {
        var modules = _modules;
        var size = _size;

        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int xx = x + dx, yy = y + dy;
                if (0 <= xx && xx < size && 0 <= yy && yy < size)
                    SetFunctionModule(xx, yy, dist != 2 && dist != 4, modules, isFunction);
            }
        }
    }

    private void GetAlignmentPatternPositions(Span<int> result)
    {
        var numAlign = result.Length;
        if (numAlign == 0)
            return;

        var version = _version;

        int step;
        if (version == 32)
            step = 26;
        else
            step = (version * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;

        result[0] = 6;
        var size = _size;
        for (int i = result.Length - 1, pos = size - 7; i >= 1; i--, pos -= step)
            result[i] = pos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetAlignmentPatternPositionsLength()
    {
        var version = _version;
        if (version == 1)
            return 0;

        return version / 7 + 2;
    }

    private void DrawAlignmentPattern(int x, int y, bool[,] isFunction)
    {
        var modules = _modules;
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1, modules, isFunction);
    }

    private void DrawFormatBits(int msk, bool[,] isFunction)
    {
        var modules = _modules;

        var data = (int)_errorCorrectionLevel << 3 | msk;
        var rem = data;
        for (var i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = (data << 10 | rem) ^ 0x5412;

        for (var i = 0; i <= 5; i++)
            SetFunctionModule(8, i, GetBit(bits, i), modules, isFunction);
        SetFunctionModule(8, 7, GetBit(bits, 6), modules, isFunction);
        SetFunctionModule(8, 8, GetBit(bits, 7), modules, isFunction);
        SetFunctionModule(7, 8, GetBit(bits, 8), modules, isFunction);
        for (var i = 9; i < 15; i++)
            SetFunctionModule(14 - i, 8, GetBit(bits, i), modules, isFunction);

        var size = _size;
        for (var i = 0; i < 8; i++)
            SetFunctionModule(size - 1 - i, 8, GetBit(bits, i), modules, isFunction);
        for (var i = 8; i < 15; i++)
            SetFunctionModule(8, size - 15 + i, GetBit(bits, i), modules, isFunction);
        SetFunctionModule(8, size - 8, true, modules, isFunction);
    }

    private void DrawVersion(bool[,] isFunction)
    {
        var version = _version;
        if (version < 7)
            return;

        var rem = version;
        for (var i = 0; i < 12; i++)
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        var bits = version << 12 | rem;

        var modules = _modules;

        var size = _size;
        // Draw two copies
        for (var i = 0; i < 18; i++)
        {
            var bit = GetBit(bits, i);
            var a = size - 11 + i % 3;
            var b = i / 3;
            SetFunctionModule(a, b, bit, modules, isFunction);
            SetFunctionModule(b, a, bit, modules, isFunction);
        }
    }

    private void ApplyMask(int msk, bool[,] isFunction)
    {
        if (msk < 0 || msk > 7)
            throw new ArgumentException("Mask value out of range");

        var size = _size;
        var modules = _modules;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                bool invert;
                switch (msk)
                {
                    case 0: invert = (x + y).IsEven(); break;
                    case 1: invert = y.IsEven(); break;
                    case 2: invert = x % 3 == 0; break;
                    case 3: invert = (x + y) % 3 == 0; break;
                    case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                    case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                    case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                    case 7: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    default: invert = false; break;
                }
                modules[y, x] ^= invert & !isFunction[y, x];
            }
        }
    }

    private int GetPenaltyScore()
    {
        var result = 0;
        Span<int> runHistory = stackalloc int[7];

        var size = _size;
        var modules = _modules;

        for (int y = 0; y < size; y++)
        {
            var runColor = false;
            var runX = 0;
            runHistory.Fill(0);

            for (int x = 0; x < size; x++)
            {
                if (modules[y, x] == runColor)
                {
                    runX++;
                    if (runX == 5)
                        result += PENALTY_N1;
                    else if (runX > 5)
                        result++;
                }
                else
                {
                    FinderPenaltyAddHistory(runX, runHistory);
                    if (!runColor)
                        result += FinderPenaltyCountPatterns(runHistory) * PENALTY_N3;
                    runColor = modules[y, x];
                    runX = 1;
                }
            }
            result += FinderPenaltyTerminateAndCount(runColor, runX, runHistory) * PENALTY_N3;
        }

        for (var x = 0; x < size; x++)
        {
            var runColor = false;
            var runY = 0;
            runHistory.Fill(0);

            for (int y = 0; y < size; y++)
            {
                if (modules[y, x] == runColor)
                {
                    runY++;
                    if (runY == 5)
                        result += PENALTY_N1;
                    else if (runY > 5)
                        result++;
                }
                else
                {
                    FinderPenaltyAddHistory(runY, runHistory);
                    if (!runColor)
                        result += FinderPenaltyCountPatterns(runHistory) * PENALTY_N3;
                    runColor = modules[y, x];
                    runY = 1;
                }
            }
            result += FinderPenaltyTerminateAndCount(runColor, runY, runHistory) * PENALTY_N3;
        }

        for (var y = 0; y < size - 1; y++)
        {
            for (var x = 0; x < size - 1; x++)
            {
                var color = modules[y, x];
                if (color == modules[y, x + 1] &&
                    color == modules[y + 1, x] &&
                    color == modules[y + 1, x + 1])
                    result += PENALTY_N2;
            }
        }

        var black = 0;
        foreach (var color in modules)
        {
            if (color)
                black++;
        }

        var total = size * size;
        var k = (Math.Abs(black * 20 - total * 10) + total - 1) / total - 1;
        result += k * PENALTY_N4;

        return result;
    }

    private void FinderPenaltyAddHistory(int currentRunLength, Span<int> runHistory)
    {
        if (runHistory[0] == 0)
            currentRunLength += _size;

        var aux = runHistory.Slice(0, runHistory.Length - 1);
        var aux2 = runHistory.Slice(1);
        aux.CopyTo(aux2);
        runHistory[0] = currentRunLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FinderPenaltyCountPatterns(ReadOnlySpan<int> runHistory)
    {
        ref var hstPtr = ref MemoryMarshal.GetReference(runHistory);
        var n = Unsafe.Add(ref hstPtr, 1);
        var n6 = Unsafe.Add(ref hstPtr, 6);

        var core = n > 0 &&
            Unsafe.Add(ref hstPtr, 2) == n &&
            Unsafe.Add(ref hstPtr, 3) == n * 3 &&
            Unsafe.Add(ref hstPtr, 4) == n &&
            Unsafe.Add(ref hstPtr, 5) == n;
        return (core && hstPtr >= n * 4 && n6 >= n ? 1 : 0)
            + (core && n6 >= n * 4 && hstPtr >= n ? 1 : 0);
    }

    private int FinderPenaltyTerminateAndCount(bool currentRunColor, int currentRunLength, Span<int> runHistory)
    {
        if (currentRunColor)
        {
            FinderPenaltyAddHistory(currentRunLength, runHistory);
            currentRunLength = 0;
        }
        currentRunLength += _size;
        FinderPenaltyAddHistory(currentRunLength, runHistory);
        return FinderPenaltyCountPatterns(runHistory);
    }
}