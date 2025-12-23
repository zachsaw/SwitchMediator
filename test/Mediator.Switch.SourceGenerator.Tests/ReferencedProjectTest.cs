using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

public class ReferencedProjectTest
{
    [Fact]
    public void GeneratorFindsHandlersInReferencedCompilation()
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

        // Compile referenced assembly into metadata reference
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

        // Main compilation (empty source) that references mediator and the compiled referenced assembly
        var mainSource = CSharpSyntaxTree.ParseText("// main project with no handlers");

        var mainRefs = loadedRefs.ToList();
        mainRefs.Add(metadataRef);

        // Ensure Mediator assembly is present so analyzer can resolve Mediator.Switch symbols
        var mediatorAsm = TestDefinitions.MediatorAssembly;
        if (mainRefs.All(r => (r as PortableExecutableReference)?.FilePath != mediatorAsm.Location))
        {
            mainRefs.Add(MetadataReference.CreateFromFile(mediatorAsm.Location));
        }

        var mainComp = CSharpCompilation.Create("MainProject",
            [mainSource],
            mainRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Directly invoke the SemanticAnalyzer to verify that referenced assembly types are discovered.
        var analyzer = new SemanticAnalyzer(mainComp);
        var analysis = analyzer.Analyze([], CancellationToken.None);

        // Assert that at least one handler was discovered whose request type contains 'ExternalRequest'
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
