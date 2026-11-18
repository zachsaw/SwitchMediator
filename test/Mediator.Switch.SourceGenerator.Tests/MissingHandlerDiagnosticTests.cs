using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

public class MissingHandlerDiagnosticTests
{
    private const string RequestWithoutHandler = """
        using Mediator.Switch;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Tests.MissingHandler;

        [SwitchMediator]
        public partial class TestMediator;

        public class Orphan : IRequest<string>;
        """;

    private const string RequestAllowingMissingHandler = """
        using Mediator.Switch;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Tests.AllowedMissingHandler;

        [SwitchMediator]
        public partial class TestMediator;

        [AllowMissingHandler]
        public class Orphan : IRequest<string>;
        """;

    private const string AbstractBaseWithoutHandler = """
        using Mediator.Switch;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Tests.AbstractMissingHandler;

        [SwitchMediator]
        public partial class TestMediator;

        public abstract class BaseRequest : IRequest<string>;

        public class ConcreteRequest : BaseRequest;
        """;

    private const string DerivedRequestHandledByTheBaseHandler = """
        using Mediator.Switch;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Tests.PolymorphicHandler;

        [SwitchMediator]
        public partial class TestMediator;

        public abstract class BaseRequest : IRequest<string>;

        public class DerivedRequest : BaseRequest;

        public class BaseRequestHandler : IRequestHandler<BaseRequest, string>
        {
            public Task<string> Handle(BaseRequest request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
        }
        """;

    [Fact]
    public async Task ReportsSMG003WhenARequestHasNoHandler()
    {
        var diagnostics = await GetGeneratorDiagnosticsAsync(RequestWithoutHandler);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "SMG003");
        Assert.Contains("Orphan", diagnostic.GetMessage());
    }

    [Fact]
    public async Task DoesNotReportWhenTheRequestAllowsAMissingHandler()
    {
        var diagnostics = await GetGeneratorDiagnosticsAsync(RequestAllowingMissingHandler);

        Assert.DoesNotContain(diagnostics, d => d.Id == "SMG003");
    }

    [Fact]
    public async Task DoesNotReportAbstractRequestsOrTheirDerivedRequestCoveredByABaseHandler()
    {
        var withoutHandler = await GetGeneratorDiagnosticsAsync(AbstractBaseWithoutHandler);
        Assert.Single(withoutHandler, d => d.Id == "SMG003");

        var handledByBase = await GetGeneratorDiagnosticsAsync(DerivedRequestHandledByTheBaseHandler);
        Assert.DoesNotContain(handledByBase, d => d.Id == "SMG003");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetGeneratorDiagnosticsAsync(string code)
    {
        var references = await TestDefinitions.ReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.Add(MetadataReference.CreateFromFile(TestDefinitions.MediatorAssembly.Location));

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "MissingHandlerTests",
            [CSharpSyntaxTree.ParseText(code, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new SwitchMediatorSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult().Diagnostics;
    }
}
