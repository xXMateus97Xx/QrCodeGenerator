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
    public static Vector256<short> Mod(Vector256<short> a, short b)
    {
        var bVec = Vector256.Create(b);
        return a - ((a / bVec) * bVec);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Mod(Vector128<short> a, short b)
    {
        var bVec = Vector128.Create(b);
        return a - ((a / bVec) * bVec);
    }
}