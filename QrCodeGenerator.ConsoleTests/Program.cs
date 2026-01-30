using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System;
using System.IO;

namespace QrCodeGenerator.ConsoleTests;

class Program
{
    static void Main(string[] args)
    {
        DoBasicDemo();
        DoVarietyDemo();
        DoSegmentDemo();
        DoMaskDemo();
    }

    // Creates a single QR Code, then writes it to a PNG file and an SVG file.
    private static void DoBasicDemo()
    {
        var text = "Hello, world!"; // User-supplied Unicode text
        var errCorLvl = Ecc.Low;  // Error correction level

        var qr = QrCode.EncodeText(text, errCorLvl);  // Make the QR Code symbol

        using var img = qr.ToImage(10, 4);           // Convert to bitmap image
        WritePng(img, "hello-world-QR.png");   // Write image to file

        var svg = qr.ToSvgString(4);                  // Convert to SVG XML code
        File.WriteAllText("hello-world-QR.svg", svg); // Write image to file

        var svg2 = qr.ToSvgUtf8String(4);                  // Convert to SVG XML code
        File.WriteAllBytes("hello-world-QR-u8.svg", svg2); // Write image to file

        using var file = File.OpenWrite("hello-world-QR-u8-stream.svg");
        qr.ToSvgUtf8Stream(4, file);
    }


    // Creates a variety of QR Codes that exercise different features of the library, and writes each one to file.
    private static void DoVarietyDemo()
    {
        QrCode qr;

        // Numeric mode encoding (3.33 bits per digit)
        qr = QrCode.EncodeText("314159265358979323846264338327950288419716939937510", Ecc.Medium);
        WritePng(qr.ToImage(13, 1), "pi-digits-QR.png");

        // Alphanumeric mode encoding (5.5 bits per character)
        qr = QrCode.EncodeText("DOLLAR-AMOUNT:$39.87 PERCENTAGE:100.00% OPERATIONS:+-*/", Ecc.High);
        WritePng(qr.ToImage(10, 2), "alphanumeric-QR.png");

        // Unicode text as UTF-8
        qr = QrCode.EncodeText("こんにちwa、世界！ αβγδ", Ecc.Quartitle);
        WritePng(qr.ToImage(10, 3), "unicode-QR.png");

        // Moderately large QR Code using longer text (from Lewis Carroll's Alice in Wonderland)
        qr = QrCode.EncodeText(
            "Alice was beginning to get very tired of sitting by her sister on the bank, "
            + "and of having nothing to do: once or twice she had peeped into the book her sister was reading, "
            + "but it had no pictures or conversations in it, 'and what is the use of a book,' thought Alice "
            + "'without pictures or conversations?' So she was considering in her own mind (as well as she could, "
            + "for the hot day made her feel very sleepy and stupid), whether the pleasure of making a "
            + "daisy-chain would be worth the trouble of getting up and picking the daisies, when suddenly "
            + "a White Rabbit with pink eyes ran close by her.", Ecc.High);
        WritePng(qr.ToImage(6, 10), "alice-wonderland-QR.png");
    }


    // Creates QR Codes with manually specified segments for better compactness.
    private static void DoSegmentDemo()
    {
        QrCode qr;
        QrSegment[] segs;

        // Illustration "silver"
        var silver0 = "THE SQUARE ROOT OF 2 IS 1.";
        var silver1 = "41421356237309504880168872420969807856967187537694807317667973799";
        qr = QrCode.EncodeText(silver0 + silver1, Ecc.Low);
        WritePng(qr.ToImage(10, 3), "sqrt2-monolithic-QR.png");

        segs = [
            QrSegment.MakeAlphanumeric(silver0),
            QrSegment.MakeNumeric(silver1)
        ];
        qr = QrCode.EncodeSegments(segs, Ecc.Low);
        WritePng(qr.ToImage(10, 3), "sqrt2-segmented-QR.png");

        // Illustration "golden"
        var golden0 = "Golden ratio φ = 1.";
        var golden0u8 = "Golden ratio φ = 1."u8;
        var golden1 = "6180339887498948482045868343656381177203091798057628621354486227052604628189024497072072041893911374";
        var golden2 = "......";
        qr = QrCode.EncodeText(golden0 + golden1 + golden2, Ecc.Low);
        WritePng(qr.ToImage(8, 5), "phi-monolithic-QR.png");

        segs = [
            QrSegment.MakeBytes(golden0u8),
            QrSegment.MakeNumeric(golden1),
            QrSegment.MakeAlphanumeric(golden2)
        ];
        qr = QrCode.EncodeSegments(segs, Ecc.Low);
        WritePng(qr.ToImage(8, 5), "phi-segmented-QR.png");

        // Illustration "Madoka": kanji, kana, Cyrillic, full-width Latin, Greek characters
        var madoka = "「魔法少女まどか☆マギカ」って、　ИАИ　ｄｅｓｕ　κα？";
        qr = QrCode.EncodeText(madoka, Ecc.Low);
        WritePng(qr.ToImage(9, 4), "madoka-utf8-QR.png");

        segs = [QrSegmentAdvanced.MakeKanji(madoka)];
        qr = QrCode.EncodeSegments(segs, Ecc.Low);
        WritePng(qr.ToImage(9, 4), "madoka-kanji-QR.png");
    }

    // Creates QR Codes with the same size and contents but different mask patterns.
    private static void DoMaskDemo()
    {
        QrCode qr;
        ReadOnlyMemory<QrSegment> segs;

        // Project Nayuki URL
        segs = QrSegment.MakeSegments("https://www.nayuki.io/");
        qr = QrCode.EncodeSegments(segs, Ecc.High, QrCode.MIN_VERSION, QrCode.MAX_VERSION, -1, true);  // Automatic mask
        WritePng(qr.ToImage(8, 6), "project-nayuki-automask-QR.png");
        qr = QrCode.EncodeSegments(segs, Ecc.High, QrCode.MIN_VERSION, QrCode.MAX_VERSION, 3, true);  // Force mask 3
        WritePng(qr.ToImage(8, 6), "project-nayuki-mask3-QR.png");

        // Chinese text as UTF-8
        segs = QrSegment.MakeSegments("維基百科（Wikipedia，聆聽i/ˌwɪkᵻˈpiːdi.ə/）是一個自由內容、公開編輯且多語言的網路百科全書協作計畫");
        qr = QrCode.EncodeSegments(segs, Ecc.Medium, QrCode.MIN_VERSION, QrCode.MAX_VERSION, 0, true);  // Force mask 0
        WritePng(qr.ToImage(10, 3), "unicode-mask0-QR.png");
        qr = QrCode.EncodeSegments(segs, Ecc.Medium, QrCode.MIN_VERSION, QrCode.MAX_VERSION, 1, true);  // Force mask 1
        WritePng(qr.ToImage(10, 3), "unicode-mask1-QR.png");
        qr = QrCode.EncodeSegments(segs, Ecc.Medium, QrCode.MIN_VERSION, QrCode.MAX_VERSION, 5, true);  // Force mask 5
        WritePng(qr.ToImage(10, 3), "unicode-mask5-QR.png");
        qr = QrCode.EncodeSegments(segs, Ecc.Medium, QrCode.MIN_VERSION, QrCode.MAX_VERSION, 7, true);  // Force mask 7
        WritePng(qr.ToImage(10, 3), "unicode-mask7-QR.png");

        var text = "Hello, world!";
        segs = QrSegment.MakeSegments(text);
        for (int i = 0; i < 8; i++)
        {
            qr = QrCode.EncodeSegments(segs, Ecc.Low, QrCode.MIN_VERSION, QrCode.MAX_VERSION, i, true);  // Force all masks
            WritePng(qr.ToImage(10, 3), $"hw-mask{i}-QR.png");
        }
    }

    private static void WritePng(Image img, string filepath)
    {
        img.Save(filepath, new PngEncoder());
    }
}
