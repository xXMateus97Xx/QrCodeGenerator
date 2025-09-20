using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System.Text;

namespace QrCodeGenerator.Benchmark;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
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
    private MemoryStream _stream;

    [GlobalSetup]
    public void Setup()
    {
        _qrCode = QrCode.EncodeText(Text, Ecc.Low);
        _stream = new MemoryStream();
    }

    [Benchmark]
    public StringBuilder ToSvgStringBuilder()
    {
        var sb = new StringBuilder();
        _qrCode.ToSvgStringBuilder(1, sb);
        return sb;
    }

    [Benchmark]
    public byte[] ToSvgUtf8String()
    {
        return _qrCode.ToSvgUtf8String(1);
    }

    [Benchmark]
    public void ToSvgUtf8Stream()
    {
        _stream.Position = 0;
        _qrCode.ToSvgUtf8Stream(1, _stream);
    }

    [Benchmark]
    public int ToImage()
    {
        using var img = _qrCode.ToImage(10, 1);
        return img.Width;
    }
}
