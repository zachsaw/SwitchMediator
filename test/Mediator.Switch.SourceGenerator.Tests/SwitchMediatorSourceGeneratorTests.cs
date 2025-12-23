using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

public class SwitchMediatorSourceGeneratorTests : CSharpSourceGeneratorTest<SwitchMediatorSourceGenerator, DefaultVerifier>
{
    public SwitchMediatorSourceGeneratorTests()
    {
        TestState.AdditionalReferences.Add(TestDefinitions.MediatorAssembly);
        TestState.ReferenceAssemblies = TestDefinitions.ReferenceAssemblies;
    }

    [Theory]
    [InlineData("Basic")]
    [InlineData("BasicRecordType")]
    [InlineData("MultipleRequests")]
    [InlineData("Polymorphics")]
    [InlineData("Notifications")]
    [InlineData("NotificationPipeline")]
    [InlineData("NotificationPipelineConstrained")]
    [InlineData("BasicPipeline")]
    [InlineData("BasicPipelineNestedType")]
    [InlineData("BasicPipelineAdapted")]
    [InlineData("ConstrainedPipeline")]
    [InlineData("OrderedPipeline")]
    [InlineData("FullPipeline")]
    [InlineData("NoMessages")]
    [InlineData("GenericsIgnored")]
    [InlineData("AbstractsIgnored")]
    [InlineData("ReferencesMediator")]
    [InlineData("ReferencesSender")]
    [InlineData("ReferencesPublisher")]
    public async Task GeneratesSwitchMediatorCorrectly(string testCase)
    {
        var inputCode = await File.ReadAllTextAsync(Path.Combine("TestCases", testCase, "Input.cs"));
        var expectedOutput = await File.ReadAllTextAsync(Path.Combine("TestCases", testCase, "Expected.txt"));

        TestCode = inputCode;
        TestState.GeneratedSources.Add(
            (typeof(SwitchMediatorSourceGenerator), "SwitchMediator.g.cs", Normalize(expectedOutput))
        );
        await RunAsync();
    }

    private static string Normalize(string code) =>
        string.Join("\n",
            code.Replace("\r\n", "\n") // Normalize Windows endings
                .Split('\n')
                .Select(line => line.TrimEnd()));
}