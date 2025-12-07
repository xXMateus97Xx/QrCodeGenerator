using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;

namespace QrCodeGenerator.Benchmark;

[Config(typeof(DisableSIMDConfig))]
public class ReedSolomonBenchmarks
{
    private class DisableSIMDConfig : ManualConfig
    {
        public DisableSIMDConfig()
        {
            AddJob(Job.Default.WithRuntime(CoreRuntime.Core10_0)//.WithToolchain(InProcessNoEmitToolchain.Instance)
                .WithId("SIMD enabled"));
            AddJob(Job.Default.WithRuntime(CoreRuntime.Core10_0)//.WithToolchain(InProcessNoEmitToolchain.Instance)
                .WithEnvironmentVariables([
                    new EnvironmentVariable("DOTNET_EnableHWIntrinsic", "0"),
                ])
                .WithId("SIMD disabled"));
        }
    }

    [Params(10, 100, 250)]
    public int Size { get; set; }

    private byte[] _array;

    [GlobalSetup]
    public void Setup()
    {
        _array = new byte[Size];
    }

    [Benchmark]
    public void ReedSolomonComputeDivisor()
    {
        ReedSolomon.ReedSolomonComputeDivisor(_array);
    }
}
