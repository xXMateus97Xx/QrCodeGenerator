# QrCodeGenerator
QrCode generator and helpers for MVC

The QrCode library is a port of https://github.com/nayuki/QR-Code-generator java implementation.


#Using QRCodeGenerator Library

```C#
using QrCodeGenerator;
using SixLabors.ImageSharp;

string text = "Hello, world!";
Ecc errCorLvl = Ecc.Low;

QrCode qr = QrCode.EncodeText(text, errCorLvl);

Image img = qr.ToImage(scale: 10, border: 4) //Get Image

string svg = qr.ToSvgString(border: 4); //or a svg xml string
```

Visit QrCodeGenerator.ConsoleTests for more examples

#Using MVC helper

- Add new keys to AppSettings

```json
"qrCode": {
  "ecc": "Low",
  "format": "Svg"
}
```

Format can be Svg or Png.

Ecc can be Low, Medium, Quartitle or High.

- Register the service on startup.cs

```C#
using QrCodeGenerator.Mvc;

services.AddQrCodeTagHelper(Configuration);
```

- Add TagHelper on _ViewImports.cshtml

```Razor
@addTagHelper *, QrCodeGenerator.Mvc
```

- Use the TagHelper when necessary
```Razor
<qrcode data="@Model.QrCodeContent" scale="10" border="5"/>
```

The parameter scale must be passed if Png is the defined format.
