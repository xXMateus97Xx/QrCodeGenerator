// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Running;
using QrCodeGenerator.Benchmark;

BenchmarkSwitcher.FromTypes([
    typeof(QrCodeEncodeEccLowBenchmarks),
    typeof(QrCodeEncodeEccMediumBenchmarks),
    typeof(QrCodeEncodeEccHighBenchmarks),
    typeof(QrCodeRenderImageBenchmarks)
    ]).Run();

Console.ReadLine();
