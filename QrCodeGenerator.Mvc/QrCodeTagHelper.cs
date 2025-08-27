using Microsoft.AspNetCore.Razor.TagHelpers;
using SixLabors.ImageSharp.Formats.Png;
using System;
using System.IO;

namespace QrCodeGenerator.Mvc;

[HtmlTargetElement("qrcode", Attributes = "data,border", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,scale,border", TagStructure = TagStructure.WithoutEndTag)]
public class QrCodeTagHelper : TagHelper
{
    private readonly QrCodeConfiguration _configuration;

    public QrCodeTagHelper(QrCodeConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HtmlAttributeName("data")]
    public string Data { get; set; }

    [HtmlAttributeName("scale")]
    public int Scale { get; set; }

    [HtmlAttributeName("border")]
    public int Border { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Data) || Border < 0)
        {
            output.SuppressOutput();
            return;
        }

        var qrCode = QrCode.EncodeText(Data, _configuration.ErrorCorrectionLevel);
        if (_configuration.Format == QrCodeFormat.Svg)
        {
            var svg = qrCode.ToSvgString(Border);
            output.Content.AppendHtml(svg);
        }
        else
        {
            if (Scale <= 0)
            {
                output.SuppressOutput();
                return;
            }

            var bitmap = qrCode.ToImage(Scale, Border);
            using var ms = new MemoryStream();
            bitmap.Save(ms, new PngEncoder());
            var base64 = Convert.ToBase64String(ms.ToArray());
            output.Content.AppendHtml($"<img src=\"data:image/png;base64,{base64}\"/>");
        }

        output.TagName = null;
    }
}