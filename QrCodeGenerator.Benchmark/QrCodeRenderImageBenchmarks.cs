using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp;
using System.Text;

namespace QrCodeGenerator.Benchmark;

[MemoryDiagnoser]
public class QrCodeRenderImageBenchmarks
{
    [Params("Hello, world!",
        "314159265358979323846264338327950288419716939937510",
        "Alice was beginning to get very tired of sitting by her sister on the bank, "
            + "and of having nothing to do: once or twice she had peeped into the book her sister was reading, "
            + "but it had no pictures or conversations in it, 'and what is the use of a book,' thought Alice "
            + "'without pictures or conversations?' So she was considering in her own mind (as well as she could, "
            + "for the hot day made her feel very sleepy and stupid), whether the pleasure of making a "
            + "daisy-chain would be worth the trouble of getting up and picking the daisies, when suddenly "
            + "a White Rabbit with pink eyes ran close by her.",
        "維基百科（Wikipedia，聆聽i/ˌwɪkᵻˈpiːdi.ə/）是一個自由內容、公開編輯且多語言的網路百科全書協作計畫"
        )]
    public string Text { get; set; }

    private QrCode _qrCode;
    private Image _image;

    [GlobalSetup]
    public void Setup()
    {
        _qrCode = QrCode.EncodeText(Text, Ecc.Low);
    }

    [IterationCleanup]
    public void CleanUp()
    {
        if (_image != null)
        {
            _image.Dispose();
            _image = null;
        }
    }

    [Benchmark]
    public StringBuilder ToSvgStringBuilder()
    {
        var sb = new StringBuilder();
        _qrCode.ToSvgStringBuilder(1, sb);
        return sb;
    }

    [Benchmark]
    public byte[] ToUtf8SvgString()
    {
        return _qrCode.ToUtf8SvgString(1);
    }

    [Benchmark]
    public int ToImage()
    {
        var img = _qrCode.ToImage(10, 1);
        _image = img;
        return img.Width;
    }
}
