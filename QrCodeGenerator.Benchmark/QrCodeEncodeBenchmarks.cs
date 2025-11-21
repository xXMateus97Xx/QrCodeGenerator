using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace QrCodeGenerator.Benchmark;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class QrCodeEncodeEccLowBenchmarks
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
        "維基百科（Wikipedia，聆聽i/ˌwɪkᵻˈpiːdi.ə/）是一個自由內容、公開編輯且多語言的網路百科全書協作計畫",
        "DOLLAR-AMOUNT:$39.87 PERCENTAGE:100.00% OPERATIONS:+-*/"
        )]
    public string Text { get; set; }

    [Benchmark]
    public QrCode EncodeTextLow()
    {
        var qr = QrCode.EncodeText(Text, Ecc.Low);
        return qr;
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class QrCodeEncodeEccMediumBenchmarks
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
        "維基百科（Wikipedia，聆聽i/ˌwɪkᵻˈpiːdi.ə/）是一個自由內容、公開編輯且多語言的網路百科全書協作計畫",
        "DOLLAR-AMOUNT:$39.87 PERCENTAGE:100.00% OPERATIONS:+-*/"
        )]
    public string Text { get; set; }

    [Benchmark]
    public QrCode EncodeTextMedium()
    {
        var qr = QrCode.EncodeText(Text, Ecc.Medium);
        return qr;
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class QrCodeEncodeEccHighBenchmarks
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
        "維基百科（Wikipedia，聆聽i/ˌwɪkᵻˈpiːdi.ə/）是一個自由內容、公開編輯且多語言的網路百科全書協作計畫",
        "DOLLAR-AMOUNT:$39.87 PERCENTAGE:100.00% OPERATIONS:+-*/"
        )]
    public string Text { get; set; }

    [Benchmark]
    public QrCode EncodeTextHigh()
    {
        var qr = QrCode.EncodeText(Text, Ecc.High);
        return qr;
    }
}