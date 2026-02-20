using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace QrCodeGenerator;

public partial class QrCode
{
    private void ApplyMask(int msk, ref ModuleState ptr)
    {
        var size = _size;

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

    private void ApplyMaskFast(int msk, ref ModuleState ptr)
    {
        if (msk < 0 || msk > 7)
            throw new ArgumentException("Mask value out of range");

        if (!Vector256.IsHardwareAccelerated && !Vector128.IsHardwareAccelerated)
        {
            ApplyMask(msk, ref ptr);
            return;
        }

        var size = _size;
        var version = (size - 17) / 4;
        var sizeShort = (short)size;
        var versionMultipler = GetVersionMultiplier(version);

        ref var current = ref Unsafe.As<ModuleState, byte>(ref ptr);
        ref var end = ref Unsafe.Add(ref current, size * size);
        short pos = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            Vector256<short> idx;
#if NET9_0_OR_GREATER
            idx = Vector256<short>.Indices;
#else
            idx = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
#endif

            var isFunction = Vector256.Create((short)ModuleState.IsFunction);
            var module = Vector256.Create((byte)ModuleState.Module);
            var moduleReverse = ~module;
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref current, Vector256<byte>.Count), ref end))
            {
                var allModules = Vector256.LoadUnsafe(ref current);
                var (modules, modules2) = Vector256.Widen(allModules.AsSByte());

                var posV = Vector256.Create(pos) + idx;
                var y = Utils.Div(posV, versionMultipler);
                var x = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply = Vector256.Equals(modules & isFunction, Vector256<short>.Zero);
                apply = CalculateMask(msk, x, y, apply);

                posV = Vector256.Create((short)(pos + (short)Vector256<short>.Count)) + idx;
                var y2 = Utils.Div(posV, versionMultipler);
                var x2 = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply2 = Vector256.Equals(modules2 & isFunction, Vector256<short>.Zero);
                apply2 = CalculateMask(msk, x2, y2, apply2);

                var finalApply = Vector256.Narrow(apply, apply2).AsByte();
                finalApply ^= Vector256.Equals(allModules & module, module);

                var toAdd = Vector256.ConditionalSelect(finalApply, Vector256<byte>.Zero, module);
                var toRemove = Vector256.ConditionalSelect(finalApply, moduleReverse, Vector256<byte>.AllBitsSet);

                allModules |= toAdd;
                allModules &= toRemove;

                allModules.StoreUnsafe(ref current);

                var mask = finalApply.ExtractMostSignificantBits();
                var xb = Vector256.Narrow(x, x2).AsByte();
                var yb = Vector256.Narrow(y, y2).AsByte();
                ApplyReverseMask(ref ptr, xb, yb, mask);

                current = ref Unsafe.Add(ref current, Vector256<byte>.Count);
                pos += (short)Vector256<byte>.Count;
            }
        }

        if (Vector128.IsHardwareAccelerated && Unsafe.IsAddressLessThan(ref Unsafe.Add(ref current, Vector128<byte>.Count), ref end))
        {
            Vector128<short> idx;
#if NET9_0_OR_GREATER
            idx = Vector128<short>.Indices;
#else
            idx = Vector128.Create(0, 1, 2, 3, 4, 5, 6, 7);
#endif

            var isFunction = Vector128.Create((short)ModuleState.IsFunction);
            var module = Vector128.Create((byte)ModuleState.Module);
            var moduleReverse = ~module;
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref current, Vector128<byte>.Count), ref end))
            {
                var allModules = Vector128.LoadUnsafe(ref current).AsByte();
                var (modules, modules2) = Vector128.Widen(allModules.AsSByte());

                var posV = Vector128.Create(pos) + idx;
                var y = Utils.Div(posV, versionMultipler);
                var x = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply = Vector128.Equals(modules & isFunction, Vector128<short>.Zero);
                apply = CalculateMask(msk, x, y, apply);

                posV = Vector128.Create((short)(pos + (short)Vector128<short>.Count)) + idx;
                var y2 = Utils.Div(posV, versionMultipler);
                var x2 = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply2 = Vector128.Equals(modules2 & isFunction, Vector128<short>.Zero);
                apply2 = CalculateMask(msk, x2, y2, apply2);

                var finalApply = Vector128.Narrow(apply, apply2).AsByte();
                finalApply ^= Vector128.Equals(allModules & module, module);

                var toAdd = Vector128.ConditionalSelect(finalApply, Vector128<byte>.Zero, module);
                var toRemove = Vector128.ConditionalSelect(finalApply, moduleReverse, Vector128<byte>.AllBitsSet);

                allModules |= toAdd;
                allModules &= toRemove;
                allModules.StoreUnsafe(ref current);

                var mask = finalApply.ExtractMostSignificantBits();
                var xb = Vector128.Narrow(x, x2).AsByte();
                var yb = Vector128.Narrow(y, y2).AsByte();
                ApplyReverseMask(ref ptr, xb, yb, mask);

                current = ref Unsafe.Add(ref current, Vector128<byte>.Count);
                pos += (short)Vector128<byte>.Count;
            }
        }

        while (Unsafe.IsAddressLessThan(ref current, ref end))
        {
            var y = pos / size;
            var x = pos % size;
            var currentModule = Unsafe.As<byte, ModuleState>(ref current);
            var apply = CalculateMask(msk, x, y, currentModule);

            SetMask(x, y, apply, ref ptr, size);

            current = ref Unsafe.Add(ref current, 1);
            pos++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyReverseMask(ref ModuleState ptr, Vector256<byte> x, Vector256<byte> y, uint mask)
    {
        var size = _size;
        for (var i = 0; i < Vector256<byte>.Count; i++)
        {
            int xs = x[i],
                ys = y[i];
            var isSet = GetBit(mask, i);
            ref var p = ref Unsafe.Add(ref ptr, xs * size + ys);
            if (isSet)
                p |= ModuleState.Reversed;
            else
                p &= ~ModuleState.Reversed;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyReverseMask(ref ModuleState ptr, Vector128<byte> x, Vector128<byte> y, uint mask)
    {
        var size = _size;
        for (var i = 0; i < Vector128<byte>.Count; i++)
        {
            int xs = x[i],
                ys = y[i];
            var isSet = GetBit(mask, i);
            ref var p = ref Unsafe.Add(ref ptr, xs * size + ys);
            if (isSet)
                p |= ModuleState.Reversed;
            else
                p &= ~ModuleState.Reversed;
        }
    }

    private static bool CalculateMask(int msk, int x, int y, ModuleState currentModule)
    {
        bool apply;
        if (msk == 0)
            apply = (x + y).IsEven() && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 1)
            apply = y.IsEven() && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 2)
            apply = x % 3 == 0 && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 3)
            apply = (x + y) % 3 == 0 && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 4)
            apply = (x / 3 + y / 2).IsEven() && !currentModule.HasFlag(ModuleState.IsFunction);
        else if (msk == 5)
            apply = !currentModule.HasFlag(ModuleState.IsFunction) && ((x * y) & 1) + x * y % 3 == 0;
        else if (msk == 6)
            apply = !currentModule.HasFlag(ModuleState.IsFunction) && ((x * y & 1) + x * y % 3).IsEven();
        else
            apply = !currentModule.HasFlag(ModuleState.IsFunction) && (((x + y) & 1) + x * y % 3).IsEven();
        return apply;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> CalculateMask(int msk, Vector128<short> x, Vector128<short> y, Vector128<short> apply)
    {
        var one = Vector128<short>.One;
        Vector128<short> r;
        if (msk == 0)
            r = (x + y) & one;
        else if (msk == 1)
            r = y & one;
        else if (msk == 2)
            r = Utils.Mod3(x);
        else if (msk == 3)
            r = Utils.Mod3(x + y);
        else if (msk == 4)
            r = (Utils.Div3(x) + (y >> 1)) & one;
        else
        {
            var m = x * y;
            var modM = Utils.Mod3(m);
            if (msk == 5)
                r = (m & one) + modM;
            else if (msk == 6)
                r = ((x * y & one) + modM) & one;
            else
                r = (((x + y) & one) + modM) & one;
        }
        return apply & Vector128.Equals(r, Vector128<short>.Zero);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<short> CalculateMask(int msk, Vector256<short> x, Vector256<short> y, Vector256<short> apply)
    {
        var one = Vector256<short>.One;
        Vector256<short> r;
        if (msk == 0)
            r = (x + y) & one;
        else if (msk == 1)
            r = y & one;
        else if (msk == 2)
            r = Utils.Mod3(x);
        else if (msk == 3)
            r = Utils.Mod3(x + y);
        else if (msk == 4)
            r = (Utils.Div3(x) + (y >> 1)) & one;
        else
        {
            var m = x * y;
            var modM = Utils.Mod3(m);
            if (msk == 5)
                r = (m & one) + modM;
            else if (msk == 6)
                r = ((x * y & one) + modM) & one;
            else
                r = (((x + y) & one) + modM) & one;
        }
        return apply & Vector256.Equals(r, Vector256<short>.Zero);
    }

    private static void SetMask(int x, int y, bool apply, ref ModuleState ptr, int size)
    {
        ref var p = ref Unsafe.Add(ref ptr, y * size + x);
        if (apply ^ p.HasFlag(ModuleState.Module))
        {
            p |= ModuleState.Module;
            Unsafe.Add(ref ptr, x * size + y) |= ModuleState.Reversed;
        }
        else
        {
            p &= ~ModuleState.Module;
            Unsafe.Add(ref ptr, x * size + y) &= ~ModuleState.Reversed;
        }
    }

    private int GetPenaltyScoreFast(ref ModuleState ptr, int currentScore)
    {
        var result = 0;
        var size = _size;

        Span<ModuleState> modules = stackalloc ModuleState[MAX_VERSION * 4 + 17];
        Span<ModuleState> modules2 = stackalloc ModuleState[MAX_VERSION * 4 + 17];
        Span<ModuleState> reversed = stackalloc ModuleState[MAX_VERSION * 4 + 17];
        Span<ModuleState> reversed2 = stackalloc ModuleState[MAX_VERSION * 4 + 17];
        modules = modules.Slice(0, size);
        modules2 = modules2.Slice(0, size);
        reversed = reversed.Slice(0, size);
        reversed2 = reversed2.Slice(0, size);

        var black = 0;

        black += ExtractFlagsAndSumBlack(ref ptr, modules, reversed);
        ptr = ref Unsafe.Add(ref ptr, size);

        for (int i = 1; i < size; i++)
        {
            black += ExtractFlagsAndSumBlack(ref ptr, modules2, reversed2);

            result += FindSequentialPatternFast(modules, ModuleState.Module);
            result += FindBadPattern(modules, ModuleState.Module);

            result += FindSequentialPatternFast(reversed, ModuleState.Reversed);
            result += FindBadPattern(reversed, ModuleState.Reversed);

            result += FindSquarePattern(modules, modules2);

            if (result >= currentScore)
                return -1;

            ptr = ref Unsafe.Add(ref ptr, size);

            var pivot = modules;
            modules = modules2;
            modules2 = pivot;

            pivot = reversed;
            reversed = reversed2;
            reversed2 = pivot;
        }

        result += FindSequentialPatternFast(modules, ModuleState.Module);
        result += FindBadPattern(modules, ModuleState.Module);

        result += FindSequentialPatternFast(reversed, ModuleState.Reversed);
        result += FindBadPattern(reversed, ModuleState.Reversed);

        var total = size * size;

        var k = ((black * 20 - total * 10).SimpleAbs() + total - 1) / total - 1;
        result += k * PENALTY_N4;

        return result < currentScore ? result : -1;
    }

    private static int FindSquarePattern(ReadOnlySpan<ModuleState> line1, ReadOnlySpan<ModuleState> line2)
    {
        var result = 0;

        ref var l1ptr = ref Unsafe.As<ModuleState, byte>(ref MemoryMarshal.GetReference(line1));
        ref var l1end = ref Unsafe.Add(ref l1ptr, line1.Length);
        ref var l2ptr = ref Unsafe.As<ModuleState, byte>(ref MemoryMarshal.GetReference(line2));
        ref var l2end = ref Unsafe.Add(ref l2ptr, line2.Length);

        if (Vector256.IsHardwareAccelerated && line1.Length >= Vector256<byte>.Count)
        {
            var mvec = Vector256.Create((byte)ModuleState.Module);
            var lvec = Vector256.Create((byte)ModuleState.None);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref l1ptr, Vector256<byte>.Count), ref l1end))
            {
                var vec1 = Vector256.LoadUnsafe(ref l1ptr);
                var vec2 = Vector256.LoadUnsafe(ref l2ptr);

                var eq = Vector256.Equals(vec1, mvec) & Vector256.Equals(vec2, mvec);
                var mask = eq.ExtractMostSignificantBits();

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                eq = Vector256.Equals(vec1, lvec) & Vector256.Equals(vec2, lvec);
                mask = eq.ExtractMostSignificantBits();

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                l1ptr = ref Unsafe.Add(ref l1ptr, Vector256<byte>.Count - 1);
                l2ptr = ref Unsafe.Add(ref l2ptr, Vector256<byte>.Count - 1);
            }

            if (Unsafe.IsAddressLessThan(ref l1ptr, ref l1end))
            {
                var vec1 = Vector256.LoadUnsafe(ref Unsafe.Subtract(ref l1end, Vector256<byte>.Count));
                var vec2 = Vector256.LoadUnsafe(ref Unsafe.Subtract(ref l2end, Vector256<byte>.Count));

                var eq = Vector256.Equals(vec1, mvec) & Vector256.Equals(vec2, mvec);
                var mask = eq.ExtractMostSignificantBits();

                mask >>= Vector256<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref l1ptr, ref l1end);

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                eq = Vector256.Equals(vec1, lvec) & Vector256.Equals(vec2, lvec);
                mask = eq.ExtractMostSignificantBits();

                mask >>= Vector256<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref l1ptr, ref l1end);

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }
            }
        }
        else if (Vector128.IsHardwareAccelerated && line1.Length >= Vector128<byte>.Count)
        {
            var mvec = Vector128.Create((byte)ModuleState.Module);
            var lvec = Vector128.Create((byte)ModuleState.None);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref l1ptr, Vector128<byte>.Count), ref l1end))
            {
                var vec1 = Vector128.LoadUnsafe(ref l1ptr);
                var vec2 = Vector128.LoadUnsafe(ref l2ptr);

                var eq = Vector128.Equals(vec1, mvec) & Vector128.Equals(vec2, mvec);
                var mask = eq.ExtractMostSignificantBits();

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                eq = Vector128.Equals(vec1, lvec) & Vector128.Equals(vec2, lvec);
                mask = eq.ExtractMostSignificantBits();

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                l1ptr = ref Unsafe.Add(ref l1ptr, Vector128<byte>.Count - 1);
                l2ptr = ref Unsafe.Add(ref l2ptr, Vector128<byte>.Count - 1);
            }

            if (Unsafe.IsAddressLessThan(ref l1ptr, ref l1end))
            {
                var vec1 = Vector128.LoadUnsafe(ref Unsafe.Subtract(ref l1end, Vector128<byte>.Count));
                var vec2 = Vector128.LoadUnsafe(ref Unsafe.Subtract(ref l2end, Vector128<byte>.Count));

                var eq = Vector128.Equals(vec1, mvec) & Vector128.Equals(vec2, mvec);
                var mask = eq.ExtractMostSignificantBits();

                mask >>= Vector128<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref l1ptr, ref l1end);

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }

                eq = Vector128.Equals(vec1, lvec) & Vector128.Equals(vec2, lvec);
                mask = eq.ExtractMostSignificantBits();

                mask >>= Vector128<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref l1ptr, ref l1end);

                while (mask > 1)
                {
                    mask >>= BitOperations.TrailingZeroCount(mask);
                    if ((mask & 0b11) == 0b11)
                        result += PENALTY_N2;
                    mask >>= 1;
                }
            }
        }
        else
        {
            var p1 = l1ptr;
            var p2 = l2ptr;
            l1ptr = ref Unsafe.Add(ref l1ptr, 1);
            l2ptr = ref Unsafe.Add(ref l2ptr, 1);
            while (Unsafe.IsAddressLessThan(ref l1ptr, ref l1end))
            {
                if (p1 == p2 && l1ptr == l2ptr && p1 == l1ptr)
                    result += PENALTY_N2;

                p1 = l1ptr;
                p2 = l2ptr;

                l1ptr = ref Unsafe.Add(ref l1ptr, 1);
                l2ptr = ref Unsafe.Add(ref l2ptr, 1);
            }
        }

        return result;
    }

    private static int FindSequentialPatternFast(ReadOnlySpan<ModuleState> modules, ModuleState flag)
    {
        var result = 0;

        ref var ptr = ref Unsafe.As<ModuleState, byte>(ref MemoryMarshal.GetReference(modules));
        ref var end = ref Unsafe.Add(ref ptr, modules.Length);

        if (Vector256.IsHardwareAccelerated && modules.Length >= Vector256<byte>.Count)
        {
            var flagv = Vector256.Create((byte)flag);
            var turn = false; //false - check white, true - check black
            var lastCheck = 0;

            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector256<byte>.Count), ref end))
            {
                var vec = Vector256.LoadUnsafe(ref ptr);

                var eq = Vector256.Equals(vec, flagv);

                var mask = eq.ExtractMostSignificantBits();

                var pos = 0;

                if (lastCheck > 0)
                {
                    if (turn)
                        mask = ~mask;
                    var step = BitOperations.TrailingZeroCount(mask);
                    step = Math.Min(step, Vector256<byte>.Count - pos);
                    pos += step;
                    mask >>= step;

                    var fullStep = lastCheck + step;

                    if (lastCheck >= 5)
                        result += step;
                    else if (fullStep >= 5)
                        result += 3 + (fullStep - 5);

                    lastCheck = pos == Vector256<byte>.Count ? step : 0;
                }

                while (pos < Vector256<byte>.Count)
                {
                    var step = BitOperations.TrailingZeroCount(mask);
                    if (step == 0)
                    {
                        turn = !turn;
                        mask = ~mask;
                        step = BitOperations.TrailingZeroCount(mask);
                    }
                    step = Math.Min(step, Vector256<byte>.Count - pos);
                    pos += step;
                    mask >>= step;

                    if (pos == Vector256<byte>.Count)
                        lastCheck = step;

                    if (step >= 5)
                        result += 3 + (step - 5);
                }

                ptr = ref Unsafe.Add(ref ptr, Vector256<byte>.Count);
            }

            if (Unsafe.IsAddressLessThan(ref ptr, ref end))
            {
                var vec = Vector256.LoadUnsafe(ref Unsafe.Subtract(ref end, Vector256<byte>.Count));

                var eq = Vector256.Equals(vec, flagv);

                var mask = eq.ExtractMostSignificantBits();

                var pos = Vector256<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref ptr, ref end);
                mask >>= pos;

                if (lastCheck > 0)
                {
                    if (turn)
                        mask = ~mask;
                    var step = BitOperations.TrailingZeroCount(mask);
                    step = Math.Min(step, Vector256<byte>.Count - pos);
                    pos += step;
                    mask >>= step;

                    var fullStep = lastCheck + step;

                    if (lastCheck >= 5)
                        result += step;
                    else if (fullStep >= 5)
                        result += 3 + (fullStep - 5);
                }

                while (pos < Vector256<byte>.Count)
                {
                    var step = BitOperations.TrailingZeroCount(mask);
                    if (step == 0)
                    {
                        mask = ~mask;
                        step = BitOperations.TrailingZeroCount(mask);
                    }
                    step = Math.Min(step, Vector256<byte>.Count - pos);
                    pos += step;
                    mask >>= step;

                    if (step >= 5)
                        result += 3 + (step - 5);
                }
            }
        }
        else if (Vector128.IsHardwareAccelerated && modules.Length >= Vector128<byte>.Count)
        {
            var flagv = Vector128.Create((byte)flag);
            var turn = false; //false - check white, true - check black
            var lastCheck = 0;

            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector128<byte>.Count), ref end))
            {
                var vec = Vector128.LoadUnsafe(ref ptr);

                var eq = Vector128.Equals(vec, flagv);

                var mask = eq.ExtractMostSignificantBits();

                var pos = 0;

                if (lastCheck > 0)
                {
                    if (turn)
                        mask = ~mask;
                    var step = BitOperations.TrailingZeroCount(mask);
                    step = Math.Min(step, Vector128<byte>.Count - pos);
                    pos += step;
                    mask >>= step;

                    var fullStep = lastCheck + step;

                    if (lastCheck >= 5)
                        result += step;
                    else if (fullStep >= 5)
                        result += 3 + (fullStep - 5);

                    lastCheck = pos == Vector128<byte>.Count ? step : 0;
                }

                while (pos < Vector128<byte>.Count)
                {
                    var step = BitOperations.TrailingZeroCount(mask);
                    if (step == 0)
                    {
                        turn = !turn;
                        mask = ~mask;
                        step = BitOperations.TrailingZeroCount(mask);
                    }
                    step = Math.Min(step, Vector128<byte>.Count - pos);
                    pos += step;
                    mask >>= step;

                    if (pos == Vector128<byte>.Count)
                        lastCheck = step;

                    if (step >= 5)
                        result += 3 + (step - 5);
                }

                ptr = ref Unsafe.Add(ref ptr, Vector128<byte>.Count);
            }

            if (Unsafe.IsAddressLessThan(ref ptr, ref end))
            {
                var vec = Vector128.LoadUnsafe(ref Unsafe.Subtract(ref end, Vector128<byte>.Count));

                var eq = Vector128.Equals(vec, flagv);

                var mask = eq.ExtractMostSignificantBits();

                var pos = Vector128<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref ptr, ref end);
                mask >>= pos;

                if (lastCheck > 0)
                {
                    if (turn)
                        mask = ~mask;
                    var step = BitOperations.TrailingZeroCount(mask);
                    step = Math.Min(step, Vector128<byte>.Count - pos);
                    pos += step;
                    mask >>= step;

                    var fullStep = lastCheck + step;

                    if (lastCheck >= 5)
                        result += step;
                    else if (fullStep >= 5)
                        result += 3 + (fullStep - 5);
                }

                while (pos < Vector128<byte>.Count)
                {
                    var step = BitOperations.TrailingZeroCount(mask);
                    if (step == 0)
                    {
                        mask = ~mask;
                        step = BitOperations.TrailingZeroCount(mask);
                    }
                    step = Math.Min(step, Vector128<byte>.Count - pos);
                    pos += step;
                    mask >>= step;

                    if (step >= 5)
                        result += 3 + (step - 5);
                }
            }
        }
        else
        {
            return FindSequentialPattern(modules, flag);
        }

        return result;
    }

    private static int FindSequentialPattern(ReadOnlySpan<ModuleState> modules, ModuleState flag)
    {
        var result = 0;

        ReadOnlySpan<ModuleState> seq = flag == ModuleState.Module ?
            [ModuleState.Module, ModuleState.Module, ModuleState.Module, ModuleState.Module, ModuleState.Module] :
            [ModuleState.Reversed, ModuleState.Reversed, ModuleState.Reversed, ModuleState.Reversed, ModuleState.Reversed];
        const ModuleState light = ModuleState.None;
        ReadOnlySpan<ModuleState> lightSeq = [light, light, light, light, light];

        while (modules.Length >= seq.Length)
        {
            var length = modules.CommonPrefixLength(seq);
            if (length == 5)
            {
                result += PENALTY_N1;
                while (length > 0)
                {
                    modules = modules.Slice(length);
                    length = modules.CommonPrefixLength(seq);
                    result += length;
                }
            }

            if (length == 0)
            {
                length = modules.CommonPrefixLength(lightSeq);
                if (length == 5)
                {
                    result += PENALTY_N1;
                    while (length > 0)
                    {
                        modules = modules.Slice(length);
                        length = modules.CommonPrefixLength(lightSeq);
                        result += length;
                    }
                }
            }

            if (modules.Length >= length)
                modules = modules.Slice(length);
        }

        return result;
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindBadPattern(ReadOnlySpan<ModuleState> modules, ModuleState dark)
    {
        var result = 0;
        var idx = 0;

        const ModuleState light = ModuleState.None;
        ReadOnlySpan<ModuleState> pattern = dark == ModuleState.Module ?
            [ModuleState.Module, light, ModuleState.Module, ModuleState.Module, ModuleState.Module, light, ModuleState.Module] :
            [ModuleState.Reversed, light, ModuleState.Reversed, ModuleState.Reversed, ModuleState.Reversed, light, ModuleState.Reversed];
        ReadOnlySpan<ModuleState> lightPattern = [light, light, light, light];

        while (idx + pattern.Length <= modules.Length)
        {
            var length = modules.Slice(idx).CommonPrefixLength(pattern);
            if (length == pattern.Length)
            {
                var nextStep = 6;
                if (idx + pattern.Length + lightPattern.Length < modules.Length)
                {
                    var pLength = modules.Slice(idx + pattern.Length).CommonPrefixLength(lightPattern);

                    if (pLength == lightPattern.Length)
                    {
                        result += PENALTY_N3;
                        nextStep = pattern.Length + pattern.Length;
                    }
                }

                if (idx - lightPattern.Length >= 0)
                {
                    var pLength = modules.Slice(idx - lightPattern.Length).CommonPrefixLength(lightPattern);

                    if (pLength == lightPattern.Length)
                        result += PENALTY_N3;
                }

                idx += nextStep;
            }
            else
            {
                idx += length switch
                {
                    2 or 3 or 4 or 6 => length + 1,
                    1 or 5 => length,
                    _ => 1,
                };
            }
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ExtractFlagsAndSumBlack(ref ModuleState mptr, Span<ModuleState> moduleDestiny, Span<ModuleState> reverseDestiny)
    {
        var count = 0;
        ref var mdptr = ref MemoryMarshal.GetReference(moduleDestiny);
        ref var rdptr = ref MemoryMarshal.GetReference(reverseDestiny);
        if (Vector256.IsHardwareAccelerated || Vector128.IsHardwareAccelerated)
        {
            ref var ptr = ref Unsafe.As<ModuleState, byte>(ref mptr);
            ref var end = ref Unsafe.Add(ref ptr, moduleDestiny.Length);
            ref var modulePtr = ref Unsafe.As<ModuleState, byte>(ref mdptr);
            ref var moduleEndPtr = ref Unsafe.Add(ref modulePtr, moduleDestiny.Length);
            ref var reversePtr = ref Unsafe.As<ModuleState, byte>(ref rdptr);
            ref var reverseEndPtr = ref Unsafe.Add(ref reversePtr, reverseDestiny.Length);

            if (Vector256.IsHardwareAccelerated && moduleDestiny.Length >= Vector256<byte>.Count)
            {
                var module = Vector256.Create((byte)ModuleState.Module);
                var reversed = Vector256.Create((byte)ModuleState.Reversed);
                while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector256<byte>.Count), ref end))
                {
                    var vec = Vector256.LoadUnsafe(ref ptr);
                    var mVec = vec & module;
                    var rVec = vec & reversed;

                    mVec.StoreUnsafe(ref modulePtr);
                    rVec.StoreUnsafe(ref reversePtr);

                    var mask = Vector256.Equals(mVec, module).ExtractMostSignificantBits();
                    count += BitOperations.PopCount(mask);

                    ptr = ref Unsafe.Add(ref ptr, Vector256<byte>.Count);
                    modulePtr = ref Unsafe.Add(ref modulePtr, Vector256<byte>.Count);
                    reversePtr = ref Unsafe.Add(ref reversePtr, Vector256<byte>.Count);
                }

                if (Unsafe.IsAddressLessThan(ref ptr, ref end))
                {
                    var vec = Vector256.LoadUnsafe(ref Unsafe.Subtract(ref end, Vector256<byte>.Count));
                    var mVec = vec & module;
                    var rVec = vec & reversed;

                    mVec.StoreUnsafe(ref Unsafe.Subtract(ref moduleEndPtr, Vector256<byte>.Count));
                    rVec.StoreUnsafe(ref Unsafe.Subtract(ref reverseEndPtr, Vector256<byte>.Count));

                    var mask = Vector256.Equals(mVec, module).ExtractMostSignificantBits();
                    count += BitOperations.PopCount(mask >> (Vector256<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref ptr, ref end)));
                }
            }
            else if (Vector128.IsHardwareAccelerated && moduleDestiny.Length >= Vector128<byte>.Count)
            {
                var module = Vector128.Create((byte)ModuleState.Module);
                var reversed = Vector128.Create((byte)ModuleState.Reversed);
                while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector128<byte>.Count), ref end))
                {
                    var vec = Vector128.LoadUnsafe(ref ptr);
                    var mVec = vec & module;
                    var rVec = vec & reversed;

                    mVec.StoreUnsafe(ref modulePtr);
                    rVec.StoreUnsafe(ref reversePtr);

                    var mask = Vector128.Equals(mVec, module).ExtractMostSignificantBits();
                    count += BitOperations.PopCount(mask);

                    ptr = ref Unsafe.Add(ref ptr, Vector128<byte>.Count);
                    modulePtr = ref Unsafe.Add(ref modulePtr, Vector128<byte>.Count);
                    reversePtr = ref Unsafe.Add(ref reversePtr, Vector128<byte>.Count);
                }

                if (Unsafe.IsAddressLessThan(ref ptr, ref end))
                {
                    var vec = Vector128.LoadUnsafe(ref Unsafe.Subtract(ref end, Vector128<byte>.Count));
                    var mVec = vec & module;
                    var rVec = vec & reversed;

                    mVec.StoreUnsafe(ref Unsafe.Subtract(ref moduleEndPtr, Vector128<byte>.Count));
                    rVec.StoreUnsafe(ref Unsafe.Subtract(ref reverseEndPtr, Vector128<byte>.Count));

                    var mask = Vector128.Equals(mVec, module).ExtractMostSignificantBits();
                    count += BitOperations.PopCount(mask >> (Vector128<byte>.Count - (int)(nuint)Unsafe.ByteOffset(ref ptr, ref end)));
                }
            }
        }
        else
        {
            for (var i = 0; i < moduleDestiny.Length; i++)
            {
                var mi = Unsafe.Add(ref mptr, i);
                var m = mi & ModuleState.Module;
                Unsafe.Add(ref mdptr, i) = m;
                Unsafe.Add(ref rdptr, i) = mi & ModuleState.Reversed;
                if (m == ModuleState.Module)
                    count++;
            }
        }

        return count;
    }

    private int GetPenaltyScore(ref ModuleState ptr, int currentScore)
    {
        if (Vector128.IsHardwareAccelerated || Vector256.IsHardwareAccelerated)
            return GetPenaltyScoreFast(ref ptr, currentScore);

        var result = 0;
        var size = _size;

        Span<int> history = size * 2 <= 256 ? stackalloc int[256] : new int[size * 2];

        ref var xPtr = ref MemoryMarshal.GetReference(history);
        ref var yPtr = ref Unsafe.Add(ref xPtr, size);

        for (int y = 0; y < size; y++)
        {
            xPtr = 0;
            yPtr = 0;

            PenaltyState xState = new() { RunHistory = ref xPtr }, yState = new() { RunHistory = ref yPtr };

            for (int x = 0; x < size; x++)
            {
                var mod = Unsafe.Add(ref ptr, y * size + x);
                xState.Current = mod.HasFlag(ModuleState.Module);
                yState.Current = mod.HasFlag(ModuleState.Reversed);

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
            result += FinderPenaltyTerminateAndCount(ref xState) * PENALTY_N3;
            result += FinderPenaltyTerminateAndCount(ref yState) * PENALTY_N3;

            if (result >= currentScore)
                return -1;
        }

        if (result >= currentScore)
            return -1;

        var black = CountModules(ref ptr);

        var total = size * size;
        var k = ((black * 20 - total * 10).SimpleAbs() + total - 1) / total - 1;
        result += k * PENALTY_N4;

        return result < currentScore ? result : -1;
    }

    private int PenaltyIteration(ref PenaltyState state)
    {
        if (state.Current == state.RunColor)
        {
            state.RunCordinate++;

            if (state.RunCordinate < 5)
                return 0;

            if (state.RunCordinate == 5)
                return PENALTY_N1;

            return 1;
        }

        var result = 0;

        FinderPenaltyAddHistory(ref state);
        if (!state.RunColor)
            result = FinderPenaltyCountPatterns(ref state.RunHistory, state.HistoryPosition) * PENALTY_N3;
        state.RunColor = state.Current;
        state.RunCordinate = 1;
        return result;
    }

    private int CountModules(ref ModuleState modulesPtr)
    {
        var size = _size;
        ref var ptr = ref Unsafe.As<ModuleState, byte>(ref modulesPtr);
        ref var end = ref Unsafe.Add(ref ptr, size * size);
        var result = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            var black = Vector256.Create((byte)ModuleState.Module);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector256<byte>.Count), ref end))
            {
                var vec = Vector256.LoadUnsafe(ref ptr);
                vec &= black;
                var isBlack = Vector256.Equals(vec, black);
                var mask = isBlack.ExtractMostSignificantBits();
                result += BitOperations.PopCount(mask);
                ptr = ref Unsafe.Add(ref ptr, Vector256<byte>.Count);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            var black = Vector128.Create((byte)ModuleState.Module);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref ptr, Vector128<byte>.Count), ref end))
            {
                var vec = Vector128.LoadUnsafe(ref ptr);
                vec &= black;
                var isBlack = Vector128.Equals(vec, black);
                var mask = isBlack.ExtractMostSignificantBits();
                result += BitOperations.PopCount(mask);
                ptr = ref Unsafe.Add(ref ptr, Vector128<byte>.Count);
            }
        }

        while (Unsafe.IsAddressLessThan(ref ptr, ref end))
        {
            if (((ModuleState)ptr).HasFlag(ModuleState.Module))
                result++;

            ptr = ref Unsafe.Add(ref ptr, 1);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinderPenaltyAddHistory(ref PenaltyState state)
    {
        var currentRunLength = state.RunCordinate;

        var position = Math.Min(state.HistoryPosition - 1, 0);

        if (Unsafe.Add(ref state.RunHistory, position) == 0)
            currentRunLength += _size;

        Unsafe.Add(ref state.RunHistory, state.HistoryPosition) = currentRunLength;
        state.HistoryPosition++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FinderPenaltyCountPatterns(ref int runHistory, nuint position)
    {
        var n = Unsafe.Add(ref runHistory, position - 2);

        bool core = n > 0;
        if (!core)
            return 0;

        var n6 = Unsafe.Add(ref runHistory, position - 7);
        var n0 = Unsafe.Add(ref runHistory, position - 1);

        if (Vector128.IsHardwareAccelerated)
        {
            var hstVec = Vector128.LoadUnsafe(ref runHistory, position - 6);
            var nVec = Vector128.Create(n) * Vector128.Create(1, 1, 3, 1);
            core = hstVec == nVec;
        }
        else
        {
            core = Unsafe.Add(ref runHistory, position - 3) == n &&
            Unsafe.Add(ref runHistory, position - 4) == n * 3 &&
            Unsafe.Add(ref runHistory, position - 5) == n &&
            Unsafe.Add(ref runHistory, position - 6) == n;
        }

        return (core && n6 >= n && n0 >= n * 4 ? 1 : 0)
            + (core && n0 >= n && n6 >= n * 4 ? 1 : 0);
    }

    private int FinderPenaltyTerminateAndCount(ref PenaltyState state)
    {
        if (state.RunColor)
        {
            FinderPenaltyAddHistory(ref state);
            state.RunCordinate = 0;
        }
        state.RunCordinate += _size;
        FinderPenaltyAddHistory(ref state);
        return FinderPenaltyCountPatterns(ref state.RunHistory, state.HistoryPosition);
    }
}
