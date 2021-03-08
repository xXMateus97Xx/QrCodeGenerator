using System;

namespace QrCodeGenerator
{
    public static class Utils
    {
        public static void CheckNull(object obj, string paramName)
        {
            if (obj is null)
                throw new ArgumentNullException(paramName);
        }

        public static bool IsEven(this int n) => (n & 1) == 0;
    }
}