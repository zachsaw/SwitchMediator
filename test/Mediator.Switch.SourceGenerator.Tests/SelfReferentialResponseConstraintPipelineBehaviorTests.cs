using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

public class SelfReferentialResponseConstraintPipelineBehaviorTests
{
    private static readonly ReferenceAssemblies ReferenceAssemblies = TestDefinitions.ReferenceAssemblies;

    [Fact]
    public async Task AppliesValuePipelineBehaviorWithSelfReferentialResponseConstraint()
    {
        var generatedCode = await GenerateAsync("""
            using Mediator.Switch;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Tests.SelfReferentialResponseConstraint;

            [SwitchMediator]
            public partial class TestMediator;

            public interface IErrorResultFactory<TSelf> where TSelf : IErrorResultFactory<TSelf>
            {
                static abstract TSelf CreateFromError(string error);
            }

            public readonly struct OneOfResult : IErrorResultFactory<OneOfResult>
            {
                public bool IsError { get; }
                public static OneOfResult CreateFromError(string error) => new(true);
                private OneOfResult(bool isError) => IsError = isError;
            }

            public interface IOneOfRequest<TResponse> : IRequest<TResponse>
                where TResponse : struct, IErrorResultFactory<TResponse>;

            public sealed class DeleteMenuItemCommand : IOneOfRequest<OneOfResult>;

            public sealed class DeleteMenuItemCommandHandler : IValueRequestHandler<DeleteMenuItemCommand, OneOfResult>
            {
                public ValueTask<OneOfResult> Handle(DeleteMenuItemCommand request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(new OneOfResult());
            }

            public sealed class UnhandledExceptionBehaviour<TRequest, TResponse> : IValuePipelineBehavior<TRequest, TResponse>
                where TRequest : class
                where TResponse : struct, IErrorResultFactory<TResponse>
            {
                public async ValueTask<TResponse> Handle(
                    TRequest request,
                    ValueRequestHandlerDelegate<TResponse> next,
                    CancellationToken cancellationToken)
                {
                    try
                    {
                        return await next(cancellationToken);
                    }
                    catch (Exception)
                    {
                        return TResponse.CreateFromError("error");
                    }
                }
            }
            """);

        Assert.Contains("UnhandledExceptionBehaviour<global::Tests.SelfReferentialResponseConstraint.DeleteMenuItemCommand, global::Tests.SelfReferentialResponseConstraint.OneOfResult>", generatedCode);
    }

    [Fact]
    public async Task AppliesValuePipelineBehaviorWithMixedResponseConstraints()
    {
        var generatedCode = await GenerateAsync("""
            using Mediator.Switch;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Tests.SelfReferentialResponseConstraint.Mixed;

            [SwitchMediator]
            public partial class TestMediator;

            public interface IOneOfResult
            {
                bool IsError { get; }
            }

            public interface IErrorResultFactory<TSelf> where TSelf : IErrorResultFactory<TSelf>
            {
                static abstract TSelf CreateFromError(string error);
            }

            public readonly struct OneOfResult : IOneOfResult, IErrorResultFactory<OneOfResult>
            {
                public bool IsError { get; }
                public static OneOfResult CreateFromError(string error) => new(true);
                private OneOfResult(bool isError) => IsError = isError;
            }

            public interface IOneOfRequest<TResponse> : IRequest<TResponse>
                where TResponse : struct, IOneOfResult, IErrorResultFactory<TResponse>;

            public sealed class DeleteMenuItemCommand : IOneOfRequest<OneOfResult>;

            public sealed class DeleteMenuItemCommandHandler : IValueRequestHandler<DeleteMenuItemCommand, OneOfResult>
            {
                public ValueTask<OneOfResult> Handle(DeleteMenuItemCommand request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(new OneOfResult());
            }

            public sealed class UnhandledExceptionBehaviour<TRequest, TResponse> : IValuePipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
                where TResponse : IOneOfResult, IErrorResultFactory<TResponse>
            {
                public async ValueTask<TResponse> Handle(
                    TRequest request,
                    ValueRequestHandlerDelegate<TResponse> next,
                    CancellationToken cancellationToken)
                {
                    try
                    {
                        return await next(cancellationToken);
                    }
                    catch (Exception)
                    {
                        return TResponse.CreateFromError("error");
                    }
                }
            }
            """);

        Assert.Contains("UnhandledExceptionBehaviour<global::Tests.SelfReferentialResponseConstraint.Mixed.DeleteMenuItemCommand, global::Tests.SelfReferentialResponseConstraint.Mixed.OneOfResult>", generatedCode);
    }

    private static async Task<string> GenerateAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));

        var references = new List<MetadataReference>(await ReferenceAssemblies.ResolveAsync(null, CancellationToken.None))
        {
            MetadataReference.CreateFromFile(TestDefinitions.MediatorAssembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: $"SelfReferentialConstraint_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new SwitchMediatorSourceGenerator()],
            parseOptions: (CSharpParseOptions)syntaxTree.Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var allErrors = diagnostics.Concat(outputCompilation.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(allErrors.Count == 0, string.Join(Environment.NewLine, allErrors));

        var generatedSource = driver.GetRunResult()
            .Results
            .SelectMany(result => result.GeneratedSources)
            .FirstOrDefault(sourceResult => sourceResult.HintName == "TestMediator.g.cs");

        Assert.True(generatedSource.HintName == "TestMediator.g.cs");
        return generatedSource.SourceText.ToString();
    }
}
