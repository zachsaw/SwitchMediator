namespace Mediator.Switch.Benchmark;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Starting Mediator Benchmarks...");
        BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        Console.WriteLine("Mediator Benchmarks finished.");
    }
}