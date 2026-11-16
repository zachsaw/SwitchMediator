using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

/// <summary>
/// A contextual behavior hands the request to its <c>next</c> delegate, so whatever request it passes on is the
/// one the rest of the pipeline and the handler receive. That must hold whichever way the pipeline is built:
/// composed once (dictionary caches), per call (<c>Fields</c>, <c>None</c>), or per call because a classic
/// behavior applies too.
/// </summary>
public class ContextualPipelineRequestTests
{
    [Theory]
    [InlineData("LazyDictionary", false)]
    [InlineData("Dictionary", false)]
    [InlineData("Fields", false)]
    [InlineData("None", false)]
    [InlineData("LazyDictionary", true)]
    [InlineData("Dictionary", true)]
    [InlineData("Fields", true)]
    [InlineData("None", true)]
    public async Task HandlerReceivesTheRequestAContextualBehaviorPassesOn(string handlerCache, bool withClassicBehavior)
    {
        var source = """
            using Mediator.Switch;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Tests.ContextualRequest;

            [SwitchMediator(HandlerCache = HandlerCache.HANDLER_CACHE)]
            public partial class TestMediator;

            public interface IScalable<TSelf>
            {
                TSelf Scaled();
            }

            public sealed record Ping(int Value) : IRequest<string>, IScalable<Ping>
            {
                public Ping Scaled() => new(Value * 100);
            }

            public sealed class PingHandler : IValueRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken cancellationToken = default) =>
                    new("handled " + request.Value);
            }

            [PipelineBehaviorOrder(1)]
            public sealed class Scaling<TRequest, TResponse> : IValueContextualPipelineBehavior<TRequest, TResponse>
                where TRequest : notnull, IScalable<TRequest>
            {
                public ValueTask<TResponse> Handle(TRequest request, ValueContextualRequestHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken = default) =>
                    next(request.Scaled(), cancellationToken);
            }

            #if WITH_CLASSIC_BEHAVIOR
            [PipelineBehaviorOrder(2)]
            public sealed class Classic<TRequest, TResponse> : IValuePipelineBehavior<TRequest, TResponse>
                where TRequest : notnull
            {
                public ValueTask<TResponse> Handle(TRequest request, ValueRequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default) =>
                    next(cancellationToken);
            }
            #endif

            public sealed class Services : ISwitchMediatorServiceProvider
            {
                public T Get<T>() where T : notnull => (T)Create(typeof(T));

                private static object Create(Type type) =>
                    type == typeof(PingHandler) ? new PingHandler() :
                    type == typeof(Scaling<Ping, string>) ? new Scaling<Ping, string>() :
            #if WITH_CLASSIC_BEHAVIOR
                    type == typeof(Classic<Ping, string>) ? new Classic<Ping, string>() :
            #endif
                    throw new InvalidOperationException($"No service for {type}.");
            }

            public static class Probe
            {
                public static async Task<string> Run()
                {
                    var mediator = new TestMediator(new Services());
                    var value = await ((IValueSender)mediator).Send(new Ping(1));
                    var task = await ((ISender)mediator).Send(new Ping(2));
                    return value + ", " + task;
                }
            }
            """.Replace("HANDLER_CACHE", handlerCache);

        var result = await RunAsync(source, withClassicBehavior ? ["WITH_CLASSIC_BEHAVIOR"] : []);

        Assert.Equal("handled 100, handled 200", result);
    }

    private static async Task<string> RunAsync(string source, string[] preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbols);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = new List<MetadataReference>(await TestDefinitions.ReferenceAssemblies.ResolveAsync(null, CancellationToken.None))
        {
            MetadataReference.CreateFromFile(TestDefinitions.MediatorAssembly.Location)
        };
        var compilation = CSharpCompilation.Create(
            assemblyName: $"ContextualRequest_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new SwitchMediatorSourceGenerator()],
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        using var image = new MemoryStream();
        var emitted = outputCompilation.Emit(image);
        var errors = diagnostics.Concat(emitted.Diagnostics).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(emitted.Success && errors.Count == 0, string.Join(Environment.NewLine, errors));

        image.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var run = assembly.GetType("Tests.ContextualRequest.Probe")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return await (Task<string>)run.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
