namespace Opossum.BenchmarkTests;

/// <summary>
/// Entry point for Opossum benchmarks
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Run benchmarks using BenchmarkSwitcher for flexibility
        // This allows filtering by class name or method name from command line
        var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
        switcher.Run(args);
    }
}
