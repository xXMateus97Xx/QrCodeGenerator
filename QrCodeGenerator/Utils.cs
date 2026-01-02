using System.Runtime.CompilerServices;

namespace QrCodeGenerator;

public static class Utils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEven(this int n) => (n & 1) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SimpleAbs(this int n) => unchecked(n >= 0 ? n : -n);
}