using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mediator.Switch.SourceGenerator.Exceptions;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

public class RequestHandlerAttributeSourceTest
{
    [Fact]
    public void AttributeAppliedInSource_HasApplicationSyntaxReference_AndAnalyzerUsesThatLocationForDiagnostics()
    {
        const string source =
            """
            using Mediator.Switch;
            using System.Threading;
            using System.Threading.Tasks;

            namespace InSourceProject
            {
                [RequestHandler(typeof(OtherHandler))]
                public class InRequest : IRequest<string> { }

                public class Handler : IRequestHandler<InRequest, string>
                {
                    public Task<string> Handle(InRequest request, CancellationToken cancellationToken = default) => Task.FromResult("ok");
                }

                public class OtherHandler { }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);

        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Distinct(new ReferenceEqualityComparer())
            .ToList();

        // Reference the mediator assembly so types like IRequest<> are resolved
        refs.Add(MetadataReference.CreateFromFile(TestDefinitions.MediatorAssembly.Location));

        var comp = CSharpCompilation.Create("InSourceComp",
            [tree],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Find the attribute syntax to compare its location later
        var root = tree.GetRoot();
        var attrSyntax = root.DescendantNodes().OfType<AttributeSyntax>()
            .First(a => a.Name.ToString().Contains("RequestHandler"));

        // Collect all type declarations to feed into the analyzer (simulating the syntax receiver)
        var typeDecls = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();

        var analyzer = new SemanticAnalyzer(comp);

        var ex = Assert.Throws<SourceGenerationException>(() =>
            analyzer.Analyze(typeDecls, CancellationToken.None)
        );

        Assert.NotNull(ex.Location);
        Assert.NotEqual(Location.None, ex.Location);
        Assert.Same(tree, ex.Location.SourceTree);
        Assert.Equal(attrSyntax.GetLocation().SourceSpan, ex.Location.SourceSpan);
    }

    private class ReferenceEqualityComparer : IEqualityComparer<MetadataReference>
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

