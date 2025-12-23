using BenchmarkDotNet.Attributes;
using Mediator.Switch.Benchmark.Generated;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Benchmark;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[BenchmarkCategory("HandlerScaling")]
public class HandlerScalingBenchmarks
{
    [Params(25, 100, 600)] // Vary N
    public int N;

    // B is fixed for this class's build (e.g., B=0 or B=1)
    // We don't need a [Params] for B here.

    private IServiceProvider _mediatRProvider = null!;
    private IServiceProvider _switchMediatorProvider = null!;

    // Instances needed for these benchmarks
    private Ping1Request_MediatR _requestToSendMediatR = null!;
    private Notify1Event_MediatR _notificationToPublishMediatR = null!;
    private Ping1Request_Switch _requestToSendSwitch = null!;
    private Notify1Event_Switch _notificationToPublishSwitch = null!;

    private const string TargetNamespace = "Mediator.Switch.Benchmark.Generated";

    [GlobalSetup]
    public void GlobalSetup()
    {
        // This setup runs for each N, using code compiled with N handlers and a FIXED number of behaviors (e.g., 0 or 1)
        Console.WriteLine($"// HandlerScalingBenchmarks GlobalSetup running for N={N}");
        var handlerAssembly = typeof(Ping1RequestHandler_MediatR).Assembly;

        // --- MediatR Setup ---
        var mediatRServices = new ServiceCollection();
        mediatRServices.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(handlerAssembly); // Registers N handlers
            cfg.Lifetime = ServiceLifetime.Singleton;
            // Only register behaviors if they were compiled (e.g., if B=1 was used for build)
            // Find behavior type - assumes OpenBehavior1_MediatR exists if B>=1 was used for build
            var behaviorType = handlerAssembly.GetType($"{TargetNamespace}.OpenBehavior1_MediatR`2");
            if (behaviorType != null) {
                Console.WriteLine("// Registering MediatR Behavior 1 (if compiled)");
                cfg.AddOpenBehavior(behaviorType);
            }
        });
        _mediatRProvider = mediatRServices.BuildServiceProvider();

        // --- SwitchMediator Setup ---
        var switchMediatorServices = new ServiceCollection();
        switchMediatorServices.AddMediator<SwitchMediator>(op =>
        {
            // KnownTypes reflects N handlers and the fixed B behaviors compiled
            op.KnownTypes = SwitchMediator.KnownTypes;
            op.ServiceLifetime = ServiceLifetime.Singleton;
        });
        _switchMediatorProvider = switchMediatorServices.BuildServiceProvider();

        // --- Prepare instances ---
        _requestToSendMediatR = new Ping1Request_MediatR { Id = 1 };
        _notificationToPublishMediatR = new Notify1Event_MediatR { Message = "Benchmark MediatR" };
        _requestToSendSwitch = new Ping1Request_Switch { Id = 1 };
        _notificationToPublishSwitch = new Notify1Event_Switch { Message = "Benchmark Switch" };

        // --- Sanity Check ---
        // Note: Validation might fail Send if B=0 was used for build and pipelines are expected by default
        // Adjust validation or ensure B=1 is used for this build if pipelines run by default
        ValidateMediatR(_mediatRProvider);
        ValidateSwitchMediator(_switchMediatorProvider);
        Console.WriteLine($"// HandlerScalingBenchmarks GlobalSetup complete for N={N}");
    }

    // --- Startup Benchmarks (Depends on N and fixed B compiled) ---
    [BenchmarkCategory("Startup"), Benchmark]
    public IServiceProvider MediatR_Startup()
    {
        var services = new ServiceCollection();
        var handlerAssembly = typeof(Ping1RequestHandler_MediatR).Assembly;
        services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(handlerAssembly);
            cfg.Lifetime = ServiceLifetime.Singleton;
            var behaviorType = handlerAssembly.GetType($"{TargetNamespace}.OpenBehavior1_MediatR`2");
            if (behaviorType != null) cfg.AddOpenBehavior(behaviorType);
        });
        var sp = services.BuildServiceProvider();
        sp.Dispose(); // Teardown should be negligible but prevents garbage build up
        return sp;
    }

    [BenchmarkCategory("Startup"), Benchmark]
    public IServiceProvider SwitchMediator_Startup()
    {
        var services = new ServiceCollection();
        services.AddMediator<SwitchMediator>(op =>
        {
            op.KnownTypes = SwitchMediator.KnownTypes;
            op.ServiceLifetime = ServiceLifetime.Singleton;
        });
        var sp = services.BuildServiceProvider();
        sp.Dispose(); // Teardown should be negligible but prevents garbage build up
        return sp;
    }

    // --- Send (No Pipeline - Depends on N) ---
    [BenchmarkCategory("SendNoPipeline"), Benchmark]
    public Task<Pong1Response_MediatR> MediatR_Send()
    {
        var mediator = _mediatRProvider.GetRequiredService<MediatR.ISender>();
        return mediator.Send(_requestToSendMediatR);
    }

    [BenchmarkCategory("SendNoPipeline"), Benchmark]
    public Task<Pong1Response_Switch> SwitchMediator_Send()
    {
        var mediator = _switchMediatorProvider.GetRequiredService<ISender>();
        return mediator.Send(_requestToSendSwitch);
    }

    // --- Publish (Depends on N) ---
    [BenchmarkCategory("Publish"), Benchmark]
    public Task MediatR_Publish()
    {
        var mediator = _mediatRProvider.GetRequiredService<MediatR.IPublisher>();
        return mediator.Publish(_notificationToPublishMediatR);
    }

    [BenchmarkCategory("Publish"), Benchmark]
    public Task SwitchMediator_Publish()
    {
        var mediator = _switchMediatorProvider.GetRequiredService<IPublisher>();
        return mediator.Publish(_notificationToPublishSwitch);
    }

    // --- Validation Helpers ---
    private void ValidateMediatR(IServiceProvider provider)
    {
        const string name = "MediatR";
        try {
            var sender = provider.GetRequiredService<MediatR.ISender>();
            var publisher = provider.GetRequiredService<MediatR.IPublisher>();
            var sendTask = sender.Send(_requestToSendMediatR);
            _ = sendTask.GetAwaiter().GetResult();
            var publishTask = publisher.Publish(_notificationToPublishMediatR);
            publishTask.GetAwaiter().GetResult();
            Console.WriteLine($"// Validation PASSED for {name}");
        } catch (Exception ex) {
            Console.WriteLine($"// !!! Validation FAILED for {name}: {ex.Message} {ex.StackTrace}");
            throw new InvalidOperationException($"Validation failed for {name}", ex);
        }
    }
    
    private void ValidateSwitchMediator(IServiceProvider provider)
    {
        const string name = "SwitchMediator";
        try {
            var sender = provider.GetRequiredService<ISender>();
            var publisher = provider.GetRequiredService<IPublisher>();
            var sendTask = sender.Send(_requestToSendSwitch);
            _ = sendTask.GetAwaiter().GetResult();
            var publishTask = publisher.Publish(_notificationToPublishSwitch);
            publishTask.GetAwaiter().GetResult();
            Console.WriteLine($"// Validation PASSED for {name}");
        } catch (Exception ex) {
            Console.WriteLine($"// !!! Validation FAILED for {name}: {ex.Message} {ex.StackTrace}");
            throw new InvalidOperationException($"Validation failed for {name}", ex);
        }
    }
}