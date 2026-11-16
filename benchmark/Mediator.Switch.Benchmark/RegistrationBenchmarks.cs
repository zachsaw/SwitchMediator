using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Mediator.Switch.Benchmark.Generated;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Benchmark;

// Today's Microsoft DI registration (type registrations from KnownTypes) against generated registrations
// (factories), on the JIT and under Native AOT, for the handlers this build was generated with.
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[SimpleJob(RuntimeMoniker.NativeAot10_0)]
[BenchmarkCategory("Registration")]
public class RegistrationBenchmarks
{
    private IServiceProvider _extension = null!;
    private IServiceProvider _generated = null!;
    private readonly Ping1Request_Switch _request = new() { Id = 1 };
    private readonly Notify1Event_Switch _notification = new() { Message = "Registration" };

    [GlobalSetup]
    public void Setup()
    {
        _extension = Extension().BuildServiceProvider();
        _generated = Generated().BuildServiceProvider();
    }

    // Today's extension: type registrations from KnownTypes.
    private static IServiceCollection Extension() =>
        new ServiceCollection().AddMediatorFromKnownTypes<SwitchMediator>(SwitchMediator.KnownTypes, ServiceLifetime.Scoped);

    // The extension with generated registrations: factories that call constructors.
    private static IServiceCollection Generated() =>
        new ServiceCollection().AddMediator<SwitchMediator>(op => op.ServiceLifetime = ServiceLifetime.Scoped);

    // A new container, its first scope and first resolve, one send and one publish.
    [BenchmarkCategory("FirstSend"), Benchmark(Baseline = true)]
    public Task FirstSend_Extension() => FirstSend(Extension());

    [BenchmarkCategory("FirstSend"), Benchmark]
    public Task FirstSend_Generated() => FirstSend(Generated());

    private async Task FirstSend(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        await mediator.Send(_request);
        await mediator.Publish(_notification);
    }

    // Steady state: one scope per operation, as a web request sees it.
    [BenchmarkCategory("ScopeSend"), Benchmark(Baseline = true)]
    public Task<Pong1Response_Switch> ScopeSend_Extension() => ScopeSend(_extension);

    [BenchmarkCategory("ScopeSend"), Benchmark]
    public Task<Pong1Response_Switch> ScopeSend_Generated() => ScopeSend(_generated);

    private async Task<Pong1Response_Switch> ScopeSend(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(_request);
    }
}
