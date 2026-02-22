namespace Opossum.BenchmarkTests;

/// <summary>
/// Shared configuration for all Opossum benchmarks
/// </summary>
public class OpossumBenchmarkConfig : ManualConfig
{
    public OpossumBenchmarkConfig()
    {
        // Job configuration - will run on the current runtime (.NET 10)
        AddJob(Job.Default
            .WithPlatform(Platform.X64)
            .WithJit(Jit.RyuJit));

        // Diagnosers - Memory allocations and ETW (Windows only)
        AddDiagnoser(MemoryDiagnoser.Default);

        if (OperatingSystem.IsWindows())
        {
            // ETW profiler for detailed performance analysis on Windows
            // Requires admin rights
            // AddDiagnoser(new EtwProfiler());
        }

        // Columns to display
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);
        AddColumn(StatisticColumn.P95); // 95th percentile
        AddColumn(BaselineRatioColumn.RatioMean);

        // Exporters - Generate reports in multiple formats
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(CsvExporter.Default);
        AddExporter(HtmlExporter.Default);

        // Summary style
        WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default
            .WithRatioStyle(BenchmarkDotNet.Columns.RatioStyle.Trend));
    }
}

/// <summary>
/// Fast config for quick validation runs during development
/// </summary>
public class FastBenchmarkConfig : ManualConfig
{
    public FastBenchmarkConfig()
    {
        AddJob(Job.Dry); // Minimal iterations, uses current runtime


        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(StatisticColumn.Mean);
        AddExporter(MarkdownExporter.GitHub);
    }
}
