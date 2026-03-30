using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class ConstraintChecker
{
    public static bool IsConstraintSatisfied(
        Compilation compilation,
        ITypeParameterSymbol? param,
        ITypeSymbol typeSymbol,
        IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol>? substitutions = null)
    {
        if (param == null) return true; // No constraints

        // 1. Check 'struct' constraint (non-nullable value type)
        if (param.HasValueTypeConstraint) // where T : struct
        {
            if (!typeSymbol.IsValueType || typeSymbol.IsNullableValueType())
            {
                return false;
            }
        }

        // 2. Check 'class' constraint (reference type) - CORRECTED FOR NRTs
        if (param.HasReferenceTypeConstraint) // where T : class OR where T : class?
        {
            // Argument must fundamentally be a reference type.
            if (!typeSymbol.IsReferenceType)
            {
                return false;
            }

            // Check nullability compatibility:
            // Is the constraint explicitly nullable ('class?')
            var constraintAllowsNullable = param.ConstraintNullableAnnotations.Contains(NullableAnnotation.Annotated);
            // Is the argument type actually nullable (e.g., string?)
            var argumentIsNullable = typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;

            // If the argument IS nullable, but the constraint does NOT allow nullables (it's just 'class'), then it fails.
            if (argumentIsNullable && !constraintAllowsNullable)
            {
                return false;
            }
            // Otherwise (arg T + constraint class, arg T + constraint class?, arg T? + constraint class?), it's compatible.
        }

        // 3. Check 'notnull' constraint
        if (param.ConstraintNullableAnnotations.Contains(NullableAnnotation.NotAnnotated)) // where T : notnull
        {
            // Must be a non-nullable value type OR a reference type not annotated as nullable.
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
            if (!typeSymbol.IsValueType)
            {
                // Check for accessible public parameterless constructor on reference types
                var hasPublicParameterlessConstructor =
                    typeSymbol is INamedTypeSymbol namedTypeSymbol && // Ensure it's a named type to check constructors
                    !namedTypeSymbol.IsAbstract && // Abstract types cannot be instantiated
                    namedTypeSymbol.InstanceConstructors.Any(m =>
                        m.Parameters.IsEmpty &&
                        m.DeclaredAccessibility == Accessibility.Public); // Check for public parameterless

                if (!hasPublicParameterlessConstructor)
                {
                    return false;
                }
            }
            // Value types implicitly satisfy new()
        }

        // 6. Check Type constraints (Base class and Interfaces)
        foreach (var constraintType in param.ConstraintTypes)
        {
            var substitutedConstraintType = SubstituteTypeParameters(constraintType, substitutions);

            // Use compilation.ClassifyConversion for a more robust check than HasImplicitConversion
            var conversion = compilation.ClassifyConversion(typeSymbol, substitutedConstraintType);
            // Needs an identity, implicit reference, boxing, or implicit nullable conversion.
            // Unboxing/explicit reference conversions usually don't satisfy generic constraints.
            if (conversion is {IsIdentity: false, IsImplicit: false}) // Includes implicit reference, boxing, nullable value type to base type etc.
            {
                 // Add specific check for interface implementation if conversion fails (e.g., value type implementing interface)
                 if (!(substitutedConstraintType.TypeKind == TypeKind.Interface &&
                       typeSymbol.AllInterfaces.Contains(substitutedConstraintType, SymbolEqualityComparer.Default)))
                 {
                    return false;
                 }
            }
        }

        // If we passed all checks, the constraints are satisfied
        return true;
    }

    private static ITypeSymbol SubstituteTypeParameters(
        ITypeSymbol constraintType,
        IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol>? substitutions)
    {
        if (substitutions == null || substitutions.Count == 0)
        {
            return constraintType;
        }

        if (constraintType is ITypeParameterSymbol typeParameter &&
            substitutions.TryGetValue(typeParameter, out var replacement))
        {
            return replacement;
        }

        if (constraintType is not INamedTypeSymbol { IsGenericType: true } namedType)
        {
            return constraintType;
        }

        var substitutedTypeArguments = new ITypeSymbol[namedType.TypeArguments.Length];
        var changed = false;

        for (var i = 0; i < namedType.TypeArguments.Length; i++)
        {
            var originalTypeArgument = namedType.TypeArguments[i];
            var substitutedTypeArgument = SubstituteTypeParameters(originalTypeArgument, substitutions);
            substitutedTypeArguments[i] = substitutedTypeArgument;

            if (!SymbolEqualityComparer.Default.Equals(originalTypeArgument, substitutedTypeArgument))
            {
                changed = true;
            }
        }

        return changed
            ? namedType.OriginalDefinition.Construct(substitutedTypeArguments)
            : constraintType;
    }
}
