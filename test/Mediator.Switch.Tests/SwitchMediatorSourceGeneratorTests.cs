﻿using Mediator.Switch.SourceGenerator;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Mediator.Switch.Tests;

public class SwitchMediatorSourceGeneratorTests : CSharpSourceGeneratorTest<SwitchMediatorSourceGenerator, DefaultVerifier>
{
    public SwitchMediatorSourceGeneratorTests()
    {
        TestState.AdditionalReferences.Add(TestDefinitions.MediatorAssembly);
        TestState.ReferenceAssemblies = TestDefinitions.ReferenceAssemblies;
    }
    
    [Theory]
    [InlineData("Basic")]
    [InlineData("MultipleRequests")]
    [InlineData("PolymorphicRequests")]
    [InlineData("Notifications")]
    [InlineData("BasicPipeline")]
    [InlineData("BasicPipelineNestedType")]
    [InlineData("BasicPipelineAdapted")]
    [InlineData("ConstrainedPipeline")]
    [InlineData("OrderedPipeline")]
    [InlineData("FullPipeline")]
    [InlineData("NoMessages")]
    [InlineData("GenericsIgnored")]
    [InlineData("AbstractsIgnored")]
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
        string.Join(Environment.NewLine, 
            code.Replace("\r\n", "\n").TrimEnd().Split('\n')
                .Select(line => line.TrimEnd()));
}