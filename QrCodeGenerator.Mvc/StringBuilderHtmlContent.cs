using Microsoft.AspNetCore.Html;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

namespace QrCodeGenerator.Mvc;

class StringBuilderHtmlContent(StringBuilder sb) : IHtmlContent
{
    private readonly StringBuilder _sb = sb;

    public void WriteTo(TextWriter writer, HtmlEncoder encoder)
    {
        writer.Write(_sb);
    }
}
