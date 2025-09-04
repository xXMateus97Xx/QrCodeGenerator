using Microsoft.AspNetCore.Html;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

namespace QrCodeGenerator.Mvc;

class StringBuilderHtmlContent : IHtmlContent
{
    private readonly StringBuilder _sb;

    public StringBuilderHtmlContent(StringBuilder sb)
    {
        _sb = sb;
    }

    public void WriteTo(TextWriter writer, HtmlEncoder encoder)
    {
        writer.Write(_sb);
    }
}
