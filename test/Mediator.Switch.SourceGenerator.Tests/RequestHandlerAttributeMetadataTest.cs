using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

public class RequestHandlerAttributeMetadataTest
{
    [Fact]
    public void AttributeAppliedInReferencedAssemblyHasNoApplicationSyntaxReference_AndAnalyzerStillFindsHandler()
    {
        const string referencedProjectCode =
            """
            using Mediator.Switch;
            using System.Threading;
            using System.Threading.Tasks;

            namespace ReferencedProject;

            [RequestHandler(typeof(ExternalHandler))]
            public class ExternalRequest : IRequest<string>;

            public class ExternalHandler : IRequestHandler<ExternalRequest, string>
            {
                public Task<string> Handle(ExternalRequest request, CancellationToken cancellationToken = default) => Task.FromResult("ok");
            }
            """;

        var refTree = CSharpSyntaxTree.ParseText(referencedProjectCode);

        var loadedRefs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Distinct(new MetadataReferenceComparer())
            .ToList();

        var refComp = CSharpCompilation.Create("ReferencedProjectAssembly",
            [refTree],
            // Ensure mediator assembly is referenced when compiling the referenced project
            loadedRefs.Concat([MetadataReference.CreateFromFile(TestDefinitions.MediatorAssembly.Location)]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = refComp.Emit(ms);
        if (!emitResult.Success)
        {
            var diag = string.Join("\n", emitResult.Diagnostics.Select(d => d.ToString()));
            throw new Xunit.Sdk.XunitException("Failed to compile referenced project in test. Diagnostics:\n" + diag);
        }

        ms.Seek(0, SeekOrigin.Begin);
        var metadataRef = MetadataReference.CreateFromStream(ms);

        // Create a main compilation that references the emitted assembly (so attribute applications are from metadata).
        var mainComp = CSharpCompilation.Create("MainComp",
            [],
            loadedRefs.Concat([metadataRef]).ToArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Inspect the attribute on the request type and assert the ApplicationSyntaxReference is null (metadata)
        var requestBSymbol = mainComp.GetTypeByMetadataName("ReferencedProject.ExternalRequest");
        Assert.NotNull(requestBSymbol);

        var attr = requestBSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Mediator.Switch.RequestHandlerAttribute");

        Assert.NotNull(attr);
        Assert.Null(attr.ApplicationSyntaxReference);

        // Now run the SemanticAnalyzer and ensure it still finds the handler
        var analyzer = new SemanticAnalyzer(mainComp);
        var analysis = analyzer.Analyze([], CancellationToken.None);

        var found = analysis.Handlers.Any(h => h.TRequest.ToDisplayString().Contains("ExternalRequest"));
        if (!found)
        {
            var handlersList = string.Join("\n", analysis.Handlers.Select(h => h.TRequest.ToDisplayString()));
            throw new Xunit.Sdk.XunitException("SemanticAnalyzer did not find expected handler for ExternalRequest. Handlers found:\n" + handlersList);
        }
    }

    private class MetadataReferenceComparer : IEqualityComparer<MetadataReference>
    {
        public bool Equals(MetadataReference? x, MetadataReference? y)
        {
            if (x == null || y == null) return x == y;
            return string.Equals((x as PortableExecutableReference)?.FilePath, (y as PortableExecutableReference)?.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(MetadataReference obj)
        {
            return ((obj as PortableExecutableReference)?.FilePath ?? string.Empty).GetHashCode();
        }
    }
}

