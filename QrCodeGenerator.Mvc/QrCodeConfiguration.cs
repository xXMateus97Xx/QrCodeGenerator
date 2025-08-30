using Microsoft.Extensions.Configuration;
using System;

namespace QrCodeGenerator.Mvc;

public class QrCodeConfiguration
{
    public QrCodeConfiguration(IConfiguration configuration)
    {
        ErrorCorrectionLevel = ParseConfiguration<Ecc>(configuration, "qrCode:ecc", Ecc.Low);
        Format = ParseConfiguration<QrCodeFormat>(configuration, "qrCode:format", QrCodeFormat.Svg);
    }

    public QrCodeConfiguration()
    {
        ErrorCorrectionLevel = Ecc.Low;
        Format = QrCodeFormat.Svg;
    }

    public Ecc ErrorCorrectionLevel { get; init; }
    public QrCodeFormat Format { get; init; }

    private T ParseConfiguration<T>(IConfiguration configuration, string key, T @default = default) where T : struct
    {
        var configString = configuration[key];
        if (!string.IsNullOrWhiteSpace(configString))
            if (Enum.TryParse<T>(configString, out var val))
                return val;

        return @default;
    }
}