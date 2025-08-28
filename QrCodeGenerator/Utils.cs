using System;

namespace QrCodeGenerator;

public static class Utils
{
    public static void CheckNull<T>(T obj, string paramName) where T : class
    {
        if (obj is null)
            throw new ArgumentNullException(paramName);
    }

    public static bool IsEven(this int n) => (n & 1) == 0;
}