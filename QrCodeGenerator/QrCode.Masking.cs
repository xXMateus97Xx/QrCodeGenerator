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
        if (msk < 0 || msk > 7)
            throw new ArgumentException("Mask value out of range");

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
            var module = Vector256.Create((short)ModuleState.Module);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref current, Vector256<byte>.Count), ref end))
            {
                var (modules, modules2) = Vector256.Widen(Vector256.LoadUnsafe(ref current).AsSByte());

                var posV = Vector256.Create(pos) + idx;
                var y = Utils.Div(posV, versionMultipler);
                var x = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply = ~Vector256.Equals(modules & isFunction, isFunction);
                apply = CalculateMask(msk, x, y, apply) ^ Vector256.Equals(modules & module, module);

                var toAdd = Vector256.ConditionalSelect(apply, Vector256<short>.Zero, module);
                var toRemove = Vector256.ConditionalSelect(apply, ~module, Vector256<short>.AllBitsSet);

                modules |= toAdd;
                modules &= toRemove;

                var mask = apply.ExtractMostSignificantBits();

                posV = Vector256.Create((short)(pos + (short)Vector256<short>.Count)) + idx;
                var y2 = Utils.Div(posV, versionMultipler);
                var x2 = Utils.Mod(posV, sizeShort, versionMultipler);

                apply = ~Vector256.Equals(modules2 & isFunction, isFunction);
                apply = CalculateMask(msk, x2, y2, apply) ^ Vector256.Equals(modules2 & module, module);

                toAdd = Vector256.ConditionalSelect(apply, Vector256<short>.Zero, module);
                toRemove = Vector256.ConditionalSelect(apply, ~module, Vector256<short>.AllBitsSet);

                modules2 |= toAdd;
                modules2 &= toRemove;

                var b = Vector256.Narrow(modules, modules2).AsByte();
                b.StoreUnsafe(ref current);

                ApplyReverseMask(ref ptr, x, y, mask);
                mask = apply.ExtractMostSignificantBits();
                ApplyReverseMask(ref ptr, x2, y2, mask);

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
            var module = Vector128.Create((short)ModuleState.Module);
            while (Unsafe.IsAddressLessThan(ref Unsafe.Add(ref current, Vector128<byte>.Count), ref end))
            {
                var (modules, modules2) = Vector128.Widen(Vector128.LoadUnsafe(ref current).AsSByte());

                var posV = Vector128.Create(pos) + idx;
                var y = Utils.Div(posV, versionMultipler);
                var x = Utils.Mod(posV, sizeShort, versionMultipler);

                var apply = ~Vector128.Equals(modules & isFunction, isFunction);

                apply = CalculateMask(msk, x, y, apply);
                apply ^= Vector128.Equals(modules & module, module);

                var toAdd = Vector128.ConditionalSelect(apply, Vector128<short>.Zero, module);
                var toRemove = Vector128.ConditionalSelect(apply, ~module, Vector128<short>.AllBitsSet);

                modules |= toAdd;
                modules &= toRemove;

                var mask = apply.ExtractMostSignificantBits();

                posV = Vector128.Create((short)(pos + (short)Vector128<short>.Count)) + idx;
                var y2 = Utils.Div(posV, versionMultipler);
                var x2 = Utils.Mod(posV, sizeShort, versionMultipler);

                apply = ~Vector128.Equals(modules2 & isFunction, isFunction);
                apply = CalculateMask(msk, x2, y2, apply);
                apply ^= Vector128.Equals(modules2 & module, module);

                toAdd = Vector128.ConditionalSelect(apply, Vector128<short>.Zero, module);
                toRemove = Vector128.ConditionalSelect(apply, ~module, Vector128<short>.AllBitsSet);

                modules2 |= toAdd;
                modules2 &= toRemove;

                var b = Vector128.Narrow(modules, modules2).AsByte();
                b.StoreUnsafe(ref current);

                ApplyReverseMask(ref ptr, x, y, mask);
                mask = apply.ExtractMostSignificantBits();
                ApplyReverseMask(ref ptr, x2, y2, mask);

                current = ref Unsafe.Add(ref current, Vector128<byte>.Count);
                pos += (short)Vector128<byte>.Count;
            }
        }

        while (Unsafe.IsAddressLessThan(ref current, ref end))
        {
            var y = pos / size;
            var x = pos % size;
            ref var currentModule = ref Unsafe.As<byte, ModuleState>(ref current);
            bool apply;
            apply = CalculateMask(msk, x, y, currentModule);

            SetMask(x, y, apply, ref ptr, size);

            current = ref Unsafe.Add(ref current, 1);
            pos++;
        }
    }

    private void ApplyReverseMask(ref ModuleState ptr, Vector256<short> x, Vector256<short> y, uint mask)
    {
        var size = _size;
        for (var i = 0; i < Vector256<short>.Count; i++)
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

    private void ApplyReverseMask(ref ModuleState ptr, Vector128<short> x, Vector128<short> y, uint mask)
    {
        var size = _size;
        for (var i = 0; i < Vector128<short>.Count; i++)
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

    private int GetPenaltyScore(ref ModuleState ptr, int currentScore)
    {
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
