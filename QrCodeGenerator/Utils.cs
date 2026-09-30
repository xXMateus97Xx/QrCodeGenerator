using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace QrCodeGenerator;

public static class Utils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEven(this int n) => (n & 1) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SimpleAbs(this int n) => unchecked(n >= 0 ? n : -n);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> Mod(Vector256<short> a, short b, float multiplier)
    {
        return a - (Div(a, multiplier) * b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Mod(Vector128<short> a, short b, float multiplier)
    {
        return a - (Div(a, multiplier) * b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> Mod3(Vector256<short> a)
    {
        var v = Div3(a);
        return a - (v * 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Mod3(Vector128<short> a)
    {
        var v = Div3(a);
        return a - (v * 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> Div3(Vector256<short> a)
    {
        var (lower, upper) = Vector256.Widen(a);
        lower *= 0x5556;
        lower >>= 16;
        upper *= 0x5556;
        upper >>= 16;
        return Vector256.Narrow(lower, upper);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Div3(Vector128<short> a)
    {
        var (lower, upper) = Vector128.Widen(a);
        lower *= 0x5556;
        lower >>= 16;
        upper *= 0x5556;
        upper >>= 16;
        return Vector128.Narrow(lower, upper);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> Div(Vector256<short> a, float mul)
    {
        var (lower, upper) = Vector256.Widen(a);
        lower = Vector256.ConvertToInt32(Vector256.ConvertToSingle(lower) * mul);
        upper = Vector256.ConvertToInt32(Vector256.ConvertToSingle(upper) * mul);

        return Vector256.Narrow(lower, upper);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Div(Vector128<short> a, float mul)
    {
        var (lower, upper) = Vector128.Widen(a);
        lower = Vector128.ConvertToInt32(Vector128.ConvertToSingle(lower) * mul);
        upper = Vector128.ConvertToInt32(Vector128.ConvertToSingle(upper) * mul);

        return Vector128.Narrow(lower, upper);
    }
}