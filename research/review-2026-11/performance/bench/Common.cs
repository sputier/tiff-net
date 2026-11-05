using System.Diagnostics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Eleris.TiffNet;

namespace Bench;

public static class Data
{
    public const string Dir = @"C:\Users\sputi\AppData\Local\Temp\claude\ff452179a1069d23\ee3533e7-fa7a-49d0-87d1-cebbd8c83c9a\scratchpad\review-perf\data";
    public static string P(string name) => Path.Combine(Dir, name);

    public static float[] LoadNpyF32(string name) => LoadNpy<float>(name);
    public static ushort[] LoadNpyU16(string name) => LoadNpy<ushort>(name);

    // Minimal .npy reader (little endian, C order): skip header.
    public static T[] LoadNpy<T>(string name) where T : unmanaged
    {
        byte[] all = File.ReadAllBytes(P(name));
        int headerLen = BitConverter.ToUInt16(all, 8);
        int start = 10 + headerLen;
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>(all.AsSpan(start)).ToArray();
    }

    static TiffImage? _u16, _f32;
    public static TiffImage U16_24 => _u16 ??= TiffImage.Create<ushort>(6000, 4000, 1, LoadNpyU16("u16_24.npy"));
    public static TiffImage F32Rgb_24 => _f32 ??= TiffImage.Create<float>(6000, 4000, 3, LoadNpyF32("f32rgb_24.npy"));
    public static long Bytes(TiffImage i) => (long)i.Width * i.Height * i.Channels * (i.SampleType switch
    {
        TiffSampleType.UInt8 or TiffSampleType.Int8 => 1,
        TiffSampleType.UInt16 or TiffSampleType.Int16 => 2,
        TiffSampleType.UInt32 or TiffSampleType.Int32 or TiffSampleType.Float32 => 4,
        _ => 8,
    });
}

/// <summary>Few, long iterations: the operations take 50 ms - 3 s.</summary>
public class BigConfig : ManualConfig
{
    public BigConfig()
    {
        AddJob(Job.Default.WithStrategy(RunStrategy.Monitoring).WithLaunchCount(1).WithWarmupCount(2).WithIterationCount(7)
            .WithInvocationCount(1).WithUnrollFactor(1).WithId("big"));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.Median, StatisticColumn.Min);
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
        SummaryStyle = SummaryStyle.Default.WithMaxParameterColumnWidth(40);
    }
}

public class MicroConfig : ManualConfig
{
    public MicroConfig()
    {
        AddJob(Job.Default.WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(10).WithId("micro"));
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.Median);
    }
}
