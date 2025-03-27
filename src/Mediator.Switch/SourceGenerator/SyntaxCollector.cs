using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mediator.Switch.SourceGenerator;

public class SyntaxCollector : ISyntaxReceiver
{
    public List<ClassDeclarationSyntax> Classes { get; } = new();

    public void OnVisitSyntaxNode(SyntaxNode node)
    {
        if (node is ClassDeclarationSyntax classDeclaration)
        {
            Classes.Add(classDeclaration);
        }
    }
}