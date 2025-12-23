using BenchmarkDotNet.Attributes;
using Mediator.Switch.Benchmark.Generated;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Benchmark;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[BenchmarkCategory("PipelineScaling")]
public class PipelineScalingBenchmarks
{
    // N is fixed for this class's build (e.g., N=100 or N=1000)
    // We don't need a [Params] for N here.

    [Params(0, 1, 5)] // Vary B (number of pipelines compiled)
    public int B;

    private IServiceProvider _mediatRProvider = null!;
    private IServiceProvider _switchMediatorProvider = null!;

    // Instances needed for these benchmarks
    private Ping1Request_MediatR _requestToSendMediatR = null!;
    private Ping1Request_Switch _requestToSendSwitch = null!;

    private const string TargetNamespace = "Mediator.Switch.Benchmark.Generated";

    [GlobalSetup]
    public void GlobalSetup()
    {
        // This setup runs for each B, using code compiled with a FIXED N and B behaviors
        Console.WriteLine($"// PipelineScalingBenchmarks GlobalSetup running for BehaviorCount={B}");
        var handlerAssembly = typeof(Ping1RequestHandler_MediatR).Assembly;

        // --- MediatR Setup ---
        var mediatRServices = new ServiceCollection();
        mediatRServices.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(handlerAssembly); // Registers fixed N handlers
            cfg.Lifetime = ServiceLifetime.Singleton;
            // Register B open behaviors (which were compiled for this run)
            for (var i = 1; i <= B; i++)
            {
                var typeName = $"{TargetNamespace}.OpenBehavior{i}_MediatR`2";
                var behaviorType = handlerAssembly.GetType(typeName);
                if (behaviorType == null) throw new InvalidOperationException($"MediatR Behavior type not found: {typeName}. Was code generated with at least B={B}?");
                cfg.AddOpenBehavior(behaviorType);
            }
        });
        _mediatRProvider = mediatRServices.BuildServiceProvider();

        // --- SwitchMediator Setup ---
        var switchMediatorServices = new ServiceCollection();
        switchMediatorServices.AddMediator<SwitchMediator>(op =>
        {
            // KnownTypes reflects fixed N handlers and B behaviors compiled
            op.KnownTypes = SwitchMediator.KnownTypes;
            op.ServiceLifetime = ServiceLifetime.Singleton;
        });
        _switchMediatorProvider = switchMediatorServices.BuildServiceProvider();

        // --- Prepare instances ---
        _requestToSendMediatR = new Ping1Request_MediatR { Id = 1 };
        _requestToSendSwitch = new Ping1Request_Switch { Id = 1 };

        // --- Sanity Check ---
        ValidateMediatR(_mediatRProvider);
        ValidateSwitchMediator(_switchMediatorProvider);
        Console.WriteLine($"// PipelineScalingBenchmarks GlobalSetup complete for BehaviorCount={B}");
    }

    // --- Send With Pipeline (Depends on B) ---
    [BenchmarkCategory("SendWithPipeline"), Benchmark]
    public Task<Pong1Response_MediatR> MediatR_Send_WithPipeline()
    {
        var mediator = _mediatRProvider.GetRequiredService<MediatR.ISender>();
        return mediator.Send(_requestToSendMediatR);
    }

    [BenchmarkCategory("SendWithPipeline"), Benchmark]
    public Task<Pong1Response_Switch> SwitchMediator_Send_WithPipeline()
    {
        var mediator = _switchMediatorProvider.GetRequiredService<ISender>();
        return mediator.Send(_requestToSendSwitch);
    }


    // --- Validation Helpers ---
    private void ValidateMediatR(IServiceProvider provider)
    {
        const string name = "MediatR";
        try {
            var sender = provider.GetRequiredService<MediatR.ISender>();
            var sendTask = sender.Send(_requestToSendMediatR);
            _ = sendTask.GetAwaiter().GetResult();
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
            var sendTask = sender.Send(_requestToSendSwitch);
            _ = sendTask.GetAwaiter().GetResult();
            Console.WriteLine($"// Validation PASSED for {name}");
        } catch (Exception ex) {
            Console.WriteLine($"// !!! Validation FAILED for {name}: {ex.Message} {ex.StackTrace}");
            throw new InvalidOperationException($"Validation failed for {name}", ex);
        }
    }
}