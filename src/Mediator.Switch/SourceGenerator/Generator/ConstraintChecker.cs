using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class ConstraintChecker
{
    public static bool IsConstraintSatisfied(Compilation compilation, ITypeParameterSymbol? param, ITypeSymbol typeSymbol)
    {
        if (param == null) return true; // No constraints

        // 1. Check 'struct' constraint (non-nullable value type)
        // Note: Nullable<T> is a value type, but doesn't satisfy 'struct' constraint.
        if (param.HasValueTypeConstraint) // where T : struct
        {
            // Must be a value type AND not System.Nullable<T>
            if (!typeSymbol.IsValueType || typeSymbol.IsNullableValueType())
            {
                return false;
            }
        }

        // 2. Check 'class' constraint (reference type)
        if (param.HasReferenceTypeConstraint) // where T : class
        {
            // Includes interfaces, delegates, arrays, classes. Also allows nullable reference types (e.g., string?)
            if (!typeSymbol.IsReferenceType)
            {
                return false;
            }
        }

        // 3. Check 'notnull' constraint
        if (param.HasNotNullConstraint) // where T : notnull
        {
            // Must be a non-nullable value type OR a reference type not annotated as nullable.
            // This check relies on NullableAnnotation which is context-dependent.
            if (typeSymbol.IsNullableValueType() ||
                typeSymbol is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.Annotated }) // e.g., string? fails
            {
                return false;
            }
        }

        // 4. Check 'unmanaged' constraint
        if (param.HasUnmanagedTypeConstraint) // where T : unmanaged
        {
            if (!typeSymbol.IsUnmanagedType)
            {
                return false;
            }
        }

        // 5. Check 'new()' constraint (parameterless constructor)
        if (param.HasConstructorConstraint) // where T : new()
        {
            // Value types always satisfy this (implicit default constructor)
            // Reference types need an accessible parameterless constructor
            if (!typeSymbol.IsValueType)
            {
                // Check for accessible parameterless constructor on reference types
                var hasAccessibleParameterlessConstructor = typeSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Any(m => m.MethodKind == MethodKind.Constructor &&
                              m is { IsStatic: false, Parameters.IsEmpty: true } &&
                              // Check accessibility (Public or Protected within the same assembly context might be needed depending on strictness)
                              m.DeclaredAccessibility == Accessibility.Public); // Or check if accessible from current context

                if (!hasAccessibleParameterlessConstructor)
                {
                    return false;
                }
            }
        }

        // 6. Check Type constraints (Base class and Interfaces)
        var constraints = param.ConstraintTypes;
        foreach (var constraint in constraints) // No need to filter nulls, ConstraintTypes shouldn't contain them
        {
            if (!compilation.HasImplicitConversion(typeSymbol, constraint))
                return false;
        }

        // If we passed all checks, the constraints are satisfied
        return true;
    }
}