using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace QrCodeGenerator;

public partial class QrCode
{
    private readonly int _version;
    private readonly int _size;
    private readonly Ecc _errorCorrectionLevel;
    private readonly int _mask;
    private readonly ModuleState[,] _modules;

    private QrCode(int ver, Ecc ecl, ReadOnlySpan<byte> dataCodewords, int msk)
    {
        _version = ver;
        _size = ver * 4 + 17;
        _errorCorrectionLevel = ecl;
        _modules = new ModuleState[_size, _size];

        DrawFunctionPatterns();
        var rawCodewords = GetNumRawDataModules(ver) / 8;
        byte[] pooledArray = null;
        Span<byte> allCodewords = rawCodewords <= 512 ? stackalloc byte[512] : (pooledArray = ArrayPool<byte>.Shared.Rent(rawCodewords));
        allCodewords = allCodewords.Slice(0, rawCodewords);
        AddEccAndInterleave(dataCodewords, allCodewords);
        DrawCodewords(allCodewords);
        _mask = HandleConstructorMasking(msk);
        if (pooledArray != null)
            ArrayPool<byte>.Shared.Return(pooledArray);
    }

    public string ToSvgString(int border)
    {
        var sb = new StringBuilder(10 * _size * _size);
        ToSvgStringBuilder(border, sb);
        return sb.ToString();
    }

    public void ToSvgStringBuilder(int border, StringBuilder sb)
    {
        if (border < 0)
            throw new ArgumentException("Border must be non-negative");

        var size = _size;
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
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
                if (modules[x, y].HasFlag(ModuleState.Reversed))
                {
                    if (x != 0 || y != 0)
                        sb.Append(' ');
                    sb.AppendFormat("M{0},{1}h1v1h-1z", x + border, y + border);
                }
            }
        }

        sb.Append("\" fill=\"#000000\"/>\n")
          .Append("</svg>\n");
    }

    public void ToSvgUtf8Stream(int border, Stream destiny)
    {
        if (border < 0)
            throw new ArgumentException("Border must be non-negative");

        destiny.Write(SVG_UTF8_HEADER);
        destiny.Write(SVG_UTF8_HEADER2);
        destiny.Write(SVG_UTF8_SVG);

        var size = _size;
        var s = size + border * 2;

        Span<byte> format = stackalloc byte[64];

        Utf8Formatter.TryFormat(s, format, out var written);
        destiny.Write(format.Slice(0, written));

        destiny.Write(SVG_UTF8_SPACE);

        Utf8Formatter.TryFormat(s, format, out written);
        destiny.Write(format.Slice(0, written));

        destiny.Write(SVG_UTF8_SVG2);
        destiny.Write(SVG_UTF8_RECT);
        destiny.Write(SVG_UTF8_PATH);

        var modules = _modules;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (modules[x, y].HasFlag(ModuleState.Reversed))
                {
                    if (x != 0 || y != 0)
                        destiny.Write(SVG_UTF8_SPACE);

                    destiny.Write(SVG_UTF8_CELL);

                    Utf8Formatter.TryFormat(x + border, format, out written);
                    destiny.Write(format.Slice(0, written));

                    destiny.Write(SVG_UTF8_CELL2);

                    Utf8Formatter.TryFormat(y + border, format, out written);
                    destiny.Write(format.Slice(0, written));

                    destiny.Write(SVG_UTF8_CELL3);
                }
            }
        }

        destiny.Write(SVG_UTF8_END_PATH);
        destiny.Write(SVG_UTF8_END_SVG);
    }

    public byte[] ToSvgUtf8String(int border)
    {
        if (border < 0)
            throw new ArgumentException("Border must be non-negative");

        var size = _size;
        var s = size + border * 2;
        var sStringSize = (((int)Math.Floor(Math.Log10(s))) + 1) * 2;
        var estimatedSize = SVG_UTF8_HEADER.Length +
            SVG_UTF8_HEADER2.Length +
            SVG_UTF8_SVG.Length + sStringSize +
            SVG_UTF8_SVG2.Length +
            SVG_UTF8_RECT.Length +
            SVG_UTF8_PATH.Length +
            ((SVG_UTF8_CELL.Length + SVG_UTF8_CELL2.Length + SVG_UTF8_CELL3.Length + sStringSize) * size * size) +
            SVG_UTF8_END_PATH.Length +
            SVG_UTF8_END_SVG.Length;

        var arr = ArrayPool<byte>.Shared.Rent(estimatedSize);

        var arrSpan = arr.AsSpan();
        var pos = 0;

        SVG_UTF8_HEADER.CopyTo(arrSpan);
        arrSpan = arrSpan.Slice(SVG_UTF8_HEADER.Length);
        pos += SVG_UTF8_HEADER.Length;

        SVG_UTF8_HEADER2.CopyTo(arrSpan);
        arrSpan = arrSpan.Slice(SVG_UTF8_HEADER2.Length);
        pos += SVG_UTF8_HEADER2.Length;

        SVG_UTF8_SVG.CopyTo(arrSpan);
        arrSpan = arrSpan.Slice(SVG_UTF8_SVG.Length);
        pos += SVG_UTF8_SVG.Length;

        Utf8Formatter.TryFormat(s, arrSpan, out var written);
        arrSpan = arrSpan.Slice(written);
        pos += written;

        SVG_UTF8_SPACE.CopyTo(arrSpan);
        arrSpan = arrSpan.Slice(SVG_UTF8_SPACE.Length);
        pos += SVG_UTF8_SPACE.Length;

        Utf8Formatter.TryFormat(s, arrSpan, out written);
        arrSpan = arrSpan.Slice(written);
        pos += written;

        SVG_UTF8_SVG2.CopyTo(arrSpan);
        arrSpan = arrSpan.Slice(SVG_UTF8_SVG2.Length);
        pos += SVG_UTF8_SVG2.Length;

        SVG_UTF8_RECT.CopyTo(arrSpan);
        arrSpan = arrSpan.Slice(SVG_UTF8_RECT.Length);
        pos += SVG_UTF8_RECT.Length;

        SVG_UTF8_PATH.CopyTo(arrSpan);
        arrSpan = arrSpan.Slice(SVG_UTF8_PATH.Length);
        pos += SVG_UTF8_PATH.Length;

        var modules = _modules;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (modules[x, y].HasFlag(ModuleState.Reversed))
                {
                    if (x != 0 || y != 0)
                    {
                        SVG_UTF8_SPACE.CopyTo(arrSpan);
                        arrSpan = arrSpan.Slice(SVG_UTF8_SPACE.Length);
                        pos += SVG_UTF8_SPACE.Length;
                    }

                    SVG_UTF8_CELL.CopyTo(arrSpan);
                    arrSpan = arrSpan.Slice(SVG_UTF8_CELL.Length);
                    pos += SVG_UTF8_CELL.Length;

                    Utf8Formatter.TryFormat(x + border, arrSpan, out written);
                    arrSpan = arrSpan.Slice(written);
                    pos += written;

                    SVG_UTF8_CELL2.CopyTo(arrSpan);
                    arrSpan = arrSpan.Slice(SVG_UTF8_CELL2.Length);
                    pos += SVG_UTF8_CELL2.Length;

                    Utf8Formatter.TryFormat(y + border, arrSpan, out written);
                    arrSpan = arrSpan.Slice(written);
                    pos += written;

                    SVG_UTF8_CELL3.CopyTo(arrSpan);
                    arrSpan = arrSpan.Slice(SVG_UTF8_CELL3.Length);
                    pos += SVG_UTF8_CELL3.Length;
                }
            }
        }

        SVG_UTF8_END_PATH.CopyTo(arrSpan);
        arrSpan = arrSpan.Slice(SVG_UTF8_END_PATH.Length);
        pos += SVG_UTF8_END_PATH.Length;

        SVG_UTF8_END_SVG.CopyTo(arrSpan);
        pos += SVG_UTF8_END_SVG.Length;

        var bytes = new byte[pos];

        arr.AsSpan(0, pos).CopyTo(bytes);

        ArrayPool<byte>.Shared.Return(arr);

        return bytes;
    }

    private static bool GetModule(int x, int y, int size, ModuleState[,] modules) => 0 <= x && x < size && 0 <= y && y < size && modules[x, y].HasFlag(ModuleState.Reversed);

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
            var row = result.DangerousGetPixelRowMemory(y);
            ref var ptr = ref MemoryMarshal.GetReference(row.Span);
            for (var x = 0; x < result.Width; x++)
            {
                var color = GetModule(x / scale - border, y / scale - border, thisSize, modules);
                if (color)
                    Unsafe.Add(ref ptr, x) = black;
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
        var numBlocks = GetErrorCorrectionBlocks(ecl, version);
        var blockEccLen = GetCodewordPerBlock(ecl, version);
        var rawCodewords = result.Length;
        var numShortBlocks = numBlocks - rawCodewords % numBlocks;
        var shortBlockLen = rawCodewords / numBlocks;

        // Split data into blocks and append ECC to each block
        var blocks = new byte[numBlocks][];
        ref var blocksPtr = ref MemoryMarshal.GetReference(blocks);
        Span<byte> rsDiv = stackalloc byte[MAX_ECC_CODEWORKS_PER_BLOCK];
        rsDiv = rsDiv.Slice(0, blockEccLen);
        ReedSolomon.ReedSolomonComputeDivisor(rsDiv);

        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            var datLength = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);

            var block = new byte[shortBlockLen + 1];
            var dat = data.Slice(k).Slice(0, datLength);
            dat.CopyTo(block);

            ReedSolomon.ReedSolomonComputeRemainder(dat, rsDiv, block.AsSpan(block.Length - blockEccLen));

            k += datLength;
            Unsafe.Add(ref blocksPtr, i) = block;
        }

        // Interleave (not concatenate) the bytes from every block into a single sequence
        ref var resultPtr = ref MemoryMarshal.GetReference(result);
        for (int i = 0, k = 0; i < blocks[0].Length; i++)
        {
            for (int j = 0; j < blocks.Length; j++)
            {
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                {
                    ref var block = ref Unsafe.Add(ref blocksPtr, j);
                    ref var item = ref MemoryMarshal.GetReference(block);
                    item = Unsafe.Add(ref item, i);
                    Unsafe.Add(ref resultPtr, k) = item;
                    k++;
                }
            }
        }
    }

    private void DrawFunctionPatterns()
    {
        var modules = _modules;
        var size = _size;
        ref var ptr = ref Unsafe.As<byte, ModuleState>(ref MemoryMarshal.GetArrayDataReference(modules));

        for (var i = 0; i < size; i++)
        {
            var even = i.IsEven();
            SetFunctionModule(6, i, even, ref ptr, size);
            SetFunctionModule(i, 6, even, ref ptr, size);
        }

        // Draw 3 finder patterns (all corners except bottom right; overwrites some timing modules)
        DrawFinderPattern(3, 3);
        DrawFinderPattern(size - 4, 3);
        DrawFinderPattern(3, size - 4);

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
                    DrawAlignmentPattern(alignPatPos[i], alignPatPos[j]);
            }
        }

        // Draw configuration data
        DrawFormatBits(0);  // Dummy mask value; overwritten later in the constructor
        DrawVersion();
    }

    private void DrawCodewords(ReadOnlySpan<byte> data)
    {
        if (data.Length != GetNumRawDataModules(_version) / 8)
            throw new ArgumentException();

        var i = 0;

        var modules = _modules;
        var size = _size;
        ref var modulePtr = ref Unsafe.As<byte, ModuleState>(ref MemoryMarshal.GetArrayDataReference(modules));
        ref var dataPtr = ref MemoryMarshal.GetReference(data);

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
                    if (!Unsafe.Add(ref modulePtr, y * size + x).HasFlag(ModuleState.IsFunction) && i < data.Length * 8)
                    {
                        var bit = GetBit(Unsafe.Add(ref dataPtr, i >> 3), 7 - (i & 7));
                        if (bit)
                        {
                            Unsafe.Add(ref modulePtr, y * size + x) |= ModuleState.Module;
                            Unsafe.Add(ref modulePtr, x * size + y) |= ModuleState.Reversed;
                        }
                        else
                        {
                            Unsafe.Add(ref modulePtr, y * size + x) &= ~ModuleState.Module;
                            Unsafe.Add(ref modulePtr, x * size + y) &= ~ModuleState.Reversed;
                        }
                        i++;
                    }
                }
            }
        }
    }

    private int HandleConstructorMasking(int msk)
    {
        if (msk == -1)
        {
            int minPenalty = int.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                ApplyMask(i);
                DrawFormatBits(i);
                int penalty = GetPenaltyScore();
                if (penalty < minPenalty)
                {
                    msk = i;
                    minPenalty = penalty;
                }
                ApplyMask(i);
            }
        }

        ApplyMask(msk);
        DrawFormatBits(msk);
        return msk;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetFunctionModule(int x, int y, bool isBlack, ref ModuleState ptr, int size)
    {
        if (isBlack)
        {
            Unsafe.Add(ref ptr, x * size + y) |= ModuleState.Reversed;
            Unsafe.Add(ref ptr, y * size + x) |= ModuleState.Module;
        }
        else
        {
            Unsafe.Add(ref ptr, x * size + y) &= ~ModuleState.Reversed;
            Unsafe.Add(ref ptr, y * size + x) &= ~ModuleState.Module;
        }
        Unsafe.Add(ref ptr, y * size + x) |= ModuleState.IsFunction;
    }

    private void DrawFinderPattern(int x, int y)
    {
        var modules = _modules;
        var size = _size;
        ref var ptr = ref Unsafe.As<byte, ModuleState>(ref MemoryMarshal.GetArrayDataReference(modules));

        var minY = Math.Max(-y, -4);
        var maxY = Math.Min((size - y - 1).SimpleAbs(), 4);

        var minX = Math.Max(-x, -4);
        var maxX = Math.Min((size - x - 1).SimpleAbs(), 4);

        for (var dy = minY; dy <= maxY; dy++)
        {
            for (var dx = minX; dx <= maxX; dx++)
            {
                var dist = Math.Max(dx.SimpleAbs(), dy.SimpleAbs());
                int xx = x + dx, yy = y + dy;
                SetFunctionModule(xx, yy, dist != 2 && dist != 4, ref ptr, size);
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
        ref var ptr = ref MemoryMarshal.GetReference(result);
        for (int i = result.Length - 1, pos = size - 7; i >= 1; i--, pos -= step)
            Unsafe.Add(ref ptr, i) = pos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetAlignmentPatternPositionsLength()
    {
        var version = _version;
        if (version == 1)
            return 0;

        return version / 7 + 2;
    }

    private void DrawAlignmentPattern(int x, int y)
    {
        var modules = _modules;
        var size = _size;
        ref var ptr = ref Unsafe.As<byte, ModuleState>(ref MemoryMarshal.GetArrayDataReference(modules));

        SetFunctionModule(x - 2, y - 2, true, ref ptr, size);
        SetFunctionModule(x - 1, y - 2, true, ref ptr, size);
        SetFunctionModule(x + 0, y - 2, true, ref ptr, size);
        SetFunctionModule(x + 1, y - 2, true, ref ptr, size);
        SetFunctionModule(x + 2, y - 2, true, ref ptr, size);
        SetFunctionModule(x - 2, y - 1, true, ref ptr, size);
        SetFunctionModule(x - 1, y - 1, false, ref ptr, size);
        SetFunctionModule(x + 0, y - 1, false, ref ptr, size);
        SetFunctionModule(x + 1, y - 1, false, ref ptr, size);
        SetFunctionModule(x + 2, y - 1, true, ref ptr, size);
        SetFunctionModule(x - 2, y + 0, true, ref ptr, size);
        SetFunctionModule(x - 1, y + 0, false, ref ptr, size);
        SetFunctionModule(x + 0, y + 0, true, ref ptr, size);
        SetFunctionModule(x + 1, y + 0, false, ref ptr, size);
        SetFunctionModule(x + 2, y + 0, true, ref ptr, size);
        SetFunctionModule(x - 2, y + 1, true, ref ptr, size);
        SetFunctionModule(x - 1, y + 1, false, ref ptr, size);
        SetFunctionModule(x + 0, y + 1, false, ref ptr, size);
        SetFunctionModule(x + 1, y + 1, false, ref ptr, size);
        SetFunctionModule(x + 2, y + 1, true, ref ptr, size);
        SetFunctionModule(x - 2, y + 2, true, ref ptr, size);
        SetFunctionModule(x - 1, y + 2, true, ref ptr, size);
        SetFunctionModule(x + 0, y + 2, true, ref ptr, size);
        SetFunctionModule(x + 1, y + 2, true, ref ptr, size);
        SetFunctionModule(x + 2, y + 2, true, ref ptr, size);
    }

    private void DrawFormatBits(int msk)
    {
        var data = (int)_errorCorrectionLevel << 3 | msk;
        var rem = data;
        for (var i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = (data << 10 | rem) ^ 0x5412;

        var modules = _modules;
        var size = _size;
        ref var ptr = ref Unsafe.As<byte, ModuleState>(ref MemoryMarshal.GetArrayDataReference(modules));

        SetFunctionModule(8, 0, GetBit(bits, 0), ref ptr, size);
        SetFunctionModule(8, 1, GetBit(bits, 1), ref ptr, size);
        SetFunctionModule(8, 2, GetBit(bits, 2), ref ptr, size);
        SetFunctionModule(8, 3, GetBit(bits, 3), ref ptr, size);
        SetFunctionModule(8, 4, GetBit(bits, 4), ref ptr, size);
        SetFunctionModule(8, 5, GetBit(bits, 5), ref ptr, size);

        SetFunctionModule(8, 7, GetBit(bits, 6), ref ptr, size);
        SetFunctionModule(8, 8, GetBit(bits, 7), ref ptr, size);
        SetFunctionModule(7, 8, GetBit(bits, 8), ref ptr, size);

        SetFunctionModule(5, 8, GetBit(bits, 9), ref ptr, size);
        SetFunctionModule(4, 8, GetBit(bits, 10), ref ptr, size);
        SetFunctionModule(3, 8, GetBit(bits, 11), ref ptr, size);
        SetFunctionModule(2, 8, GetBit(bits, 12), ref ptr, size);
        SetFunctionModule(1, 8, GetBit(bits, 13), ref ptr, size);
        SetFunctionModule(0, 8, GetBit(bits, 14), ref ptr, size);

        SetFunctionModule(size - 1 - 0, 8, GetBit(bits, 0), ref ptr, size);
        SetFunctionModule(size - 1 - 1, 8, GetBit(bits, 1), ref ptr, size);
        SetFunctionModule(size - 1 - 2, 8, GetBit(bits, 2), ref ptr, size);
        SetFunctionModule(size - 1 - 3, 8, GetBit(bits, 3), ref ptr, size);
        SetFunctionModule(size - 1 - 4, 8, GetBit(bits, 4), ref ptr, size);
        SetFunctionModule(size - 1 - 5, 8, GetBit(bits, 5), ref ptr, size);
        SetFunctionModule(size - 1 - 6, 8, GetBit(bits, 6), ref ptr, size);
        SetFunctionModule(size - 1 - 7, 8, GetBit(bits, 7), ref ptr, size);

        SetFunctionModule(8, size - 15 + 8, GetBit(bits, 8), ref ptr, size);
        SetFunctionModule(8, size - 15 + 9, GetBit(bits, 9), ref ptr, size);
        SetFunctionModule(8, size - 15 + 10, GetBit(bits, 10), ref ptr, size);
        SetFunctionModule(8, size - 15 + 11, GetBit(bits, 11), ref ptr, size);
        SetFunctionModule(8, size - 15 + 12, GetBit(bits, 12), ref ptr, size);
        SetFunctionModule(8, size - 15 + 13, GetBit(bits, 13), ref ptr, size);
        SetFunctionModule(8, size - 15 + 14, GetBit(bits, 14), ref ptr, size);

        SetFunctionModule(8, size - 8, true, ref ptr, size);
    }

    private void DrawVersion()
    {
        var version = _version;
        if (version < 7)
            return;

        var rem = version;
        for (var i = 0; i < 12; i++)
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        var bits = version << 12 | rem;

        var modules = _modules;
        ref var ptr = ref Unsafe.As<byte, ModuleState>(ref MemoryMarshal.GetArrayDataReference(modules));

        var size = _size;
        // Draw two copies
        for (var i = 0; i < 18; i++)
        {
            var bit = GetBit(bits, i);
            var a = size - 11 + i % 3;
            var b = i / 3;
            SetFunctionModule(a, b, bit, ref ptr, size);
            SetFunctionModule(b, a, bit, ref ptr, size);
        }
    }

    private void ApplyMask(int msk)
    {
        if (msk < 0 || msk > 7)
            throw new ArgumentException("Mask value out of range");

        var size = _size;
        var modules = _modules;
        ref var ptr = ref Unsafe.As<byte, ModuleState>(ref MemoryMarshal.GetArrayDataReference(modules));

        if (msk == 0)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = (x + y).IsEven() && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 1)
        {
            for (var y = 0; y < size; y++)
            {
                var isEven = y.IsEven();
                for (var x = 0; x < size; x++)
                {
                    var apply = isEven && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 2)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = x % 3 == 0 && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 3)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = (x + y) % 3 == 0 && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 4)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = (x / 3 + y / 2).IsEven() && !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction);
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 5)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction) && ((x * y) & 1) + x * y % 3 == 0;
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else if (msk == 6)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction) && ((x * y & 1) + x * y % 3).IsEven();
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
        else
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var apply = !Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.IsFunction) && (((x + y) & 1) + x * y % 3).IsEven();
                    SetMask(x, y, apply, ref ptr, size);
                }
            }
        }
    }

    private static void SetMask(int x, int y, bool apply, ref ModuleState ptr, int size)
    {
        if (apply ^ Unsafe.Add(ref ptr, y * size + x).HasFlag(ModuleState.Module))
        {
            Unsafe.Add(ref ptr, y * size + x) |= ModuleState.Module;
            Unsafe.Add(ref ptr, x * size + y) |= ModuleState.Reversed;
        }
        else
        {
            Unsafe.Add(ref ptr, y * size + x) &= ~ModuleState.Module;
            Unsafe.Add(ref ptr, x * size + y) &= ~ModuleState.Reversed;
        }
    }

    private int GetPenaltyScore()
    {
        var result = 0;

        //Only first 7 positions are used, allocated only for aligment purposes
        Span<int> runHistoryX = stackalloc int[16];
        Span<int> runHistoryY = stackalloc int[16];

        var size = _size;
        var modules = _modules;
        ref var ptr = ref Unsafe.As<byte, ModuleState>(ref MemoryMarshal.GetArrayDataReference(modules));

        for (int y = 0; y < size; y++)
        {
            runHistoryX.Clear();
            runHistoryY.Clear();

            PenaltyState xState = new() { RunHistory = runHistoryX }, yState = new() { RunHistory = runHistoryY };

            for (int x = 0; x < size; x++)
            {
                var mod = Unsafe.Add(ref ptr, x * size + y);
                xState.Current = mod.HasFlag(ModuleState.Reversed);
                yState.Current = mod.HasFlag(ModuleState.Module);

                result += PenaltyIteration(ref xState);
                result += PenaltyIteration(ref yState);

                if (x < size - 1 && y < size - 1)
                {
                    if (xState.Current == Unsafe.Add(ref ptr, y * size + x + 1).HasFlag(ModuleState.Module) &&
                        xState.Current == Unsafe.Add(ref ptr, (y + 1) * size + x).HasFlag(ModuleState.Module) &&
                        xState.Current == Unsafe.Add(ref ptr, (y + 1) * size + x + 1).HasFlag(ModuleState.Module))
                        result += PENALTY_N2;
                }
            }
            result += FinderPenaltyTerminateAndCount(xState.RunColor, xState.RunCordinate, runHistoryX) * PENALTY_N3;
            result += FinderPenaltyTerminateAndCount(yState.RunColor, yState.RunCordinate, runHistoryY) * PENALTY_N3;
        }

        var black = CountModules();

        var total = size * size;
        var k = ((black * 20 - total * 10).SimpleAbs() + total - 1) / total - 1;
        result += k * PENALTY_N4;

        return result;
    }

    private int PenaltyIteration(ref PenaltyState state)
    {
        if (state.Current == state.RunColor)
        {
            state.RunCordinate++;
            if (state.RunCordinate == 5)
                return PENALTY_N1;
            else if (state.RunCordinate > 5)
                return 1;

            return 0;
        }

        var result = 0;

        FinderPenaltyAddHistory(state.RunCordinate, state.RunHistory);
        if (!state.RunColor)
            result = FinderPenaltyCountPatterns(state.RunHistory) * PENALTY_N3;
        state.RunColor = state.Current;
        state.RunCordinate = 1;
        return result;
    }

    private int CountModules()
    {
        var modules = _modules;
        var span = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetArrayDataReference(modules), modules.Length);
        var result = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            var black = Vector256.Create((byte)ModuleState.Module);
            while (span.Length >= Vector256<byte>.Count)
            {
                var vec = Vector256.Create(span.Slice(0, Vector256<byte>.Count));
                vec &= black;
                var isBlack = Vector256.Equals(vec, black);
                var mask = isBlack.ExtractMostSignificantBits();
                result += BitOperations.PopCount(mask);
                span = span.Slice(Vector256<byte>.Count);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            var black = Vector128.Create((byte)ModuleState.Module);
            while (span.Length >= Vector128<byte>.Count)
            {
                var vec = Vector128.Create(span.Slice(0, Vector128<byte>.Count));
                vec &= black;
                var isBlack = Vector128.Equals(vec, black);
                var mask = isBlack.ExtractMostSignificantBits();
                result += BitOperations.PopCount(mask);
                span = span.Slice(Vector128<byte>.Count);
            }
        }

        for (int i = 0; i < span.Length; i++)
        {
            var module = span[i];
            if (((ModuleState)module).HasFlag(ModuleState.Module))
                result++;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinderPenaltyAddHistory(int currentRunLength, Span<int> runHistory)
    {
        ref var ptr = ref MemoryMarshal.GetReference(runHistory);

        if (ptr == 0)
            currentRunLength += _size;

        if (Vector256.IsHardwareAccelerated)
        {
            var vec = Vector256.LoadUnsafe(ref ptr);
            vec.StoreUnsafe(ref ptr, 1);
        }
        else
        {
            var aux = runHistory.Slice(0, runHistory.Length - 1);
            var aux2 = runHistory.Slice(1);
            aux.CopyTo(aux2);
        }

        ptr = currentRunLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FinderPenaltyCountPatterns(ReadOnlySpan<int> runHistory)
    {
        ref var hstPtr = ref MemoryMarshal.GetReference(runHistory);
        var n = Unsafe.Add(ref hstPtr, 1);

        bool core = n > 0;
        if (core)
        {
            if (Vector128.IsHardwareAccelerated)
            {
                var hstVec = Vector128.LoadUnsafe(ref hstPtr, 2);
                var nVec = Vector128.Create(n) * Vector128.Create(1, 3, 1, 1);
                core = hstVec == nVec;
            }
            else
            {
                core = Unsafe.Add(ref hstPtr, 2) == n &&
                Unsafe.Add(ref hstPtr, 3) == n * 3 &&
                Unsafe.Add(ref hstPtr, 4) == n &&
                Unsafe.Add(ref hstPtr, 5) == n;
            }
        }

        var n6 = Unsafe.Add(ref hstPtr, 6);
        return (core && n6 >= n && hstPtr >= n * 4 ? 1 : 0)
            + (core && hstPtr >= n && n6 >= n * 4 ? 1 : 0);
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