using System;

namespace QrCodeGenerator;

public class DataTooLongException : ArgumentException
{
    public DataTooLongException()
    {
    }

    public DataTooLongException(string msg)
        : base(msg)
    {
    }
}