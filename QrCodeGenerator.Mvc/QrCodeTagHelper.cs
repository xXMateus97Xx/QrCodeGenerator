using Microsoft.AspNetCore.Razor.TagHelpers;
using SixLabors.ImageSharp.Formats.Png;
using System;
using System.IO;
using System.Text;

namespace QrCodeGenerator.Mvc;

[HtmlTargetElement("qrcode", Attributes = "data", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,border", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,border,ecc", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,scale", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,scale,ecc", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,scale,border", TagStructure = TagStructure.WithoutEndTag)]
[HtmlTargetElement("qrcode", Attributes = "data,scale,border,ecc", TagStructure = TagStructure.WithoutEndTag)]
public class QrCodeTagHelper(QrCodeConfiguration configuration) : TagHelper
{
    private readonly QrCodeConfiguration _configuration = configuration;

    [HtmlAttributeName("data")]
    public string Data { get; set; }

    [HtmlAttributeName("scale")]
    public int Scale { get; set; }

    [HtmlAttributeName("border")]
    public int Border { get; set; }

    [HtmlAttributeName("ecc")]
    public Ecc? ErrorCorrectionLevel { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var data = Data;
        if (string.IsNullOrWhiteSpace(data))
        {
            output.SuppressOutput();
            return;
        }

        var border = Math.Max(Border, 0);

        var qrCode = QrCode.EncodeText(data, ErrorCorrectionLevel ?? _configuration.ErrorCorrectionLevel);
        if (_configuration.Format == QrCodeFormat.Svg)
        {
            var sb = new StringBuilder();
            qrCode.ToSvgStringBuilder(border, sb);
            output.Content.AppendHtml(new StringBuilderHtmlContent(sb));
        }
        else
        {
            var scale = Math.Max(Scale, 1);

            using var bitmap = qrCode.ToImage(scale, border);
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