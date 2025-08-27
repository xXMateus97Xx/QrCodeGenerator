using Microsoft.AspNetCore.Razor.TagHelpers;
using SixLabors.ImageSharp.Formats.Png;
using System;
using System.IO;

namespace QrCodeGenerator.Mvc;

[HtmlTargetElement("qrcode", Attributes = "data", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,border", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,scale", TagStructure = TagStructure.WithoutEndTag)]
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
        if (string.IsNullOrWhiteSpace(Data))
        {
            output.SuppressOutput();
            return;
        }

        var border = Math.Max(Border, 0);

        var qrCode = QrCode.EncodeText(Data, _configuration.ErrorCorrectionLevel);
        if (_configuration.Format == QrCodeFormat.Svg)
        {
            var svg = qrCode.ToSvgString(border);
            output.Content.AppendHtml(svg);
        }
        else
        {
            var scale = Math.Max(Scale, 1);

            var bitmap = qrCode.ToImage(scale, border);
            using var ms = new MemoryStream();
            bitmap.Save(ms, new PngEncoder());

            var msLength = (int)ms.Length;

            var bytes = ms.GetBuffer();

            var base64Length = ((4 * msLength / 3) + 3) & ~3;

            const string openTag = "<img src=\"data:image/png;base64,";
            const string closeTag = "\"/>";

            var length = base64Length + openTag.Length + closeTag.Length;

            var tag = string.Create(length, bytes.AsMemory(0, msLength), (s, b) =>
            {
                openTag.CopyTo(s);

                Convert.TryToBase64Chars(b.Span, s.Slice(openTag.Length), out var written);

                closeTag.CopyTo(s.Slice(openTag.Length + written));
            });

            output.Content.AppendHtml(tag);
        }

        output.TagName = null;
    }
}