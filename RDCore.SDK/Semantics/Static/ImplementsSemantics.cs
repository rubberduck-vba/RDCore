using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The static semantics of the <c>Implements</c> directives of a class module (<strong>MS-VBAL §5.2.4.2</strong>) and of
/// the implemented name declarations that follow from them (<strong>§5.3.1.9</strong>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><strong>The directive.</strong> The class it names must exist, and cannot be the class of the module that has the
/// directive, nor a class another directive of the module names. A class whose public variables or methods have an
/// underscore in their names cannot be an interface class, and no interface's implemented interface name prefix
/// (<c>InterfaceName_</c>) can begin another's. All of that is <see cref="VBCompileErrorId.InvalidImplementsDirective"/>,
/// a class that does not exist is <see cref="VBCompileErrorId.UserDefinedTypeNotDefined"/>.</item>
/// <item><strong>What it requires.</strong> An implemented name declaration for each public method of the interface class,
/// of the same kind, and for each public variable the property accessors its type calls for: a <c>Property Get</c> and a
/// <c>Property Let</c>, a <c>Property Set</c> where the variable is an <c>Object</c> or a class, and all three where it is a
/// <c>Variant</c> (<see cref="VBCompileErrorId.InterfaceMemberNotImplemented"/>).</item>
/// <item><strong>What it forbids.</strong> A procedure named <c>InterfaceName_MemberName</c> is the implementation of that
/// member, and is invalid when it is not the kind of declaration the member needs, when its parameters are not equivalent
/// to the member's, or when its type is not the member's (<see cref="VBCompileErrorId.InvalidImplementedMember"/>).</item>
/// </list>
/// A member the language implements on every class module's behalf, and gives an implementation of its own
/// (<see cref="SymbolProperties.DefaultImplementation"/>), is not required of anything: this checks the interfaces a module
/// names, and <see cref="ClassLifecycleInterface"/> is not one of them.
/// <para>
/// An <c>Implements</c> directive in an extensible module (<c>Attribute VB_Extensible = True</c>,
/// <see cref="SymbolProperties.Extensible"/>) is invalid, whatever the directive names, and nothing else of the module's
/// directives is checked then.
/// </para>
/// <para>
/// 🚧 TODO the directive's own location is not on the symbol, so what is reported of a directive is located at the module.
/// </para>
/// </remarks>
public static class ImplementsSemantics
{
    /// <summary>
    /// Checks the <c>Implements</c> directives of <paramref name="module"/> and what they require of it.
    /// </summary>
    /// <param name="module">The class module, with its members.</param>
    /// <param name="resolver">What finds the classes the directives name, as they are now.</param>
    /// <returns>Every error found, the directives' first, then the members'; empty when they are valid.</returns>
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(VBClassModuleSymbol module, ISymbolResolver resolver)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();

        // MS-VBAL §5.2.4.2: "An <implements-directive> cannot occur within an extension module." What a host's document modules
        // are is extensible, and a directive there would extend the very module the host reaches into.
        if (module.GetProperty(SymbolProperties.Extensible) && !module.ImplementedInterfaceNames.IsEmpty)
        {
            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidImplementsDirective, LocationOfDirective(module, 0),
                $"'{module.Name}' is an extensible module (Attribute VB_Extensible), which cannot have an Implements directive (MS-VBAL §5.2.4.2)."));
            return errors.ToImmutable();
        }

        var interfaces = CheckDirectives(module, resolver, errors);

        foreach (var implemented in interfaces)
        {
            foreach (var member in InterfaceMembersOf(implemented))
            {
                CheckMember(module, implemented, member, errors);
            }
        }

        return errors.ToImmutable();
    }

    private enum Shape { Sub, Function, PropertyGet, PropertyLet, PropertySet }

    private static SourceLocation LocationOf(VBTypeMemberSymbol member) => new(member.ParentUri, member.SelectionRange);

    private static SourceLocation LocationOf(VBClassModuleSymbol module) => new(module.Uri, SourceRange.Empty);

    // where the directive that names the interface is written, which a symbol that was not read from source does not know.
    private static SourceLocation LocationOfDirective(VBClassModuleSymbol module, int index)
        => index >= 0 && index < module.ImplementedInterfaceRanges.Length && module.ImplementedInterfaceRanges.Length == module.ImplementedInterfaceNames.Length
            ? new(module.Uri, module.ImplementedInterfaceRanges[index])
            : LocationOf(module);

    private static SourceLocation LocationOfDirective(VBClassModuleSymbol module, VBClassModuleSymbol implemented)
        => LocationOfDirective(module, module.ImplementedInterfaceNames.ToList().FindIndex(
            name => string.Equals(name, implemented.Name, StringComparison.OrdinalIgnoreCase)));

    // Property Let, Property Set and Property Get derive from the subroutine's and the function's symbols, and are the
    // kinds of declaration they derive from only by inheritance.
    private static Shape? ShapeOf(VBTypeMemberSymbol member) => member switch
    {
        VBPropertyGetMemberSymbol => Shape.PropertyGet,
        VBPropertyLetMemberSymbol => Shape.PropertyLet,
        VBPropertySetMemberSymbol => Shape.PropertySet,
        VBFunctionMemberSymbol => Shape.Function,
        VBProcedureMemberSymbol => Shape.Sub,
        _ => null,
    };

    private static bool IsPropertyAccessor(Shape shape) => shape is Shape.PropertyGet or Shape.PropertyLet or Shape.PropertySet;

    private static bool IsVariable(VBTypeMemberSymbol member) => member.Kind == SymbolKindExt.Field;

    // "public variable or method": a procedure, function or property accessor that is public (as one is unless it says
    // otherwise), and a variable that is declared Public. Events, constants and types are neither.
    private static IEnumerable<VBTypeMemberSymbol> InterfaceMembersOf(VBClassModuleSymbol interfaceClass)
        => interfaceClass.Members.Where(member => ShapeOf(member) is not null
            ? member.AccessModifier is AccessModifier.Public or AccessModifier.Implicit
            : IsVariable(member) && member.AccessModifier is AccessModifier.Public);

    private static List<VBClassModuleSymbol> CheckDirectives(
        VBClassModuleSymbol module, ISymbolResolver resolver, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        var valid = new List<VBClassModuleSymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < module.ImplementedInterfaceNames.Length; index++)
        {
            var name = module.ImplementedInterfaceNames[index];
            if (resolver.ResolveType(name, ScopeKind.Global, StaticSymbol.GlobalUri).Symbol is not VBClassModuleSymbol named)
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.UserDefinedTypeNotDefined, LocationOfDirective(module, index),
                    $"'Implements {name}' names no class (MS-VBAL §5.2.4.2)."));
                continue;
            }

            // a Uri's fragment is where a symbol's identity lives, and Uri equality ignores it.
            if (named.Uri.AbsoluteUri == module.Uri.AbsoluteUri)
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidImplementsDirective, LocationOfDirective(module, index),
                    $"'Implements {name}': the interface class cannot be the class of the module that has the directive (MS-VBAL §5.2.4.2)."));
                continue;
            }

            if (!seen.Add(named.Uri.AbsoluteUri))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidImplementsDirective, LocationOfDirective(module, index),
                    $"'{name}' is the interface class of more than one Implements directive of this module (MS-VBAL §5.2.4.2)."));
                continue;
            }

            if (InterfaceMembersOf(named).FirstOrDefault(member => member.Name.Contains('_')) is { } underscored)
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidImplementsDirective, LocationOfDirective(module, index),
                    $"'{name}' cannot be an interface class: its public member '{underscored.Name}' has an underscore in its name (MS-VBAL §5.2.4.2)."));
                continue;
            }

            valid.Add(named);
        }

        // the prefix an interface's members are implemented under is its name and an underscore: none may begin another's.
        for (var i = 0; i < valid.Count; i++)
        {
            for (var j = 0; j < valid.Count; j++)
            {
                if (i != j && $"{valid[j].Name}_".StartsWith($"{valid[i].Name}_", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidImplementsDirective, LocationOfDirective(module, valid[j]),
                        $"the implemented interface name prefix '{valid[i].Name}_' begins the prefix '{valid[j].Name}_' of another interface (MS-VBAL §5.2.4.2)."));
                }
            }
        }

        return valid;
    }

    private static void CheckMember(
        VBClassModuleSymbol module, VBClassModuleSymbol implemented, VBTypeMemberSymbol member, ImmutableArray<VBCompileErrorInfo>.Builder errors)
    {
        var name = $"{implemented.Name}_{member.Name}";
        var candidates = module.Members
            .Where(candidate => ShapeOf(candidate) is not null && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var provided = new HashSet<Shape>();
        foreach (var candidate in candidates)
        {
            if (Incompatibility(implemented, member, candidate) is { } reason)
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InvalidImplementedMember, LocationOf(candidate),
                    $"'{candidate.Name}' implements '{implemented.Name}.{member.Name}', and {reason} (MS-VBAL §5.3.1.9)."));
            }
            else
            {
                provided.Add(ShapeOf(candidate)!.Value);
            }
        }

        // a method that has a declaration, valid or not, is not missing one: what is wrong with it is reported above.
        var required = RequiredShapes(member);
        if (!IsVariable(member) && candidates.Length > 0)
        {
            return;
        }

        foreach (var shape in required.Where(shape => !provided.Contains(shape)))
        {
            if (IsVariable(member) && candidates.Any(candidate => ShapeOf(candidate) == shape))
            {
                continue;
            }

            errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.InterfaceMemberNotImplemented, LocationOfDirective(module, implemented),
                $"'{module.Name}' implements '{implemented.Name}' and needs to implement '{implemented.Name}.{member.Name}' with a {Describe(shape)} named '{name}' (MS-VBAL §5.2.4.2)."));
        }
    }

    // the accessors a public variable is implemented with depend on its type; a method is implemented by its own kind.
    private static Shape[] RequiredShapes(VBTypeMemberSymbol member)
    {
        if (!IsVariable(member))
        {
            return [ShapeOf(member)!.Value];
        }

        return member.ResolvedType switch
        {
            VBVariantType => [Shape.PropertyGet, Shape.PropertyLet, Shape.PropertySet],
            VBObjectType or VBClassType => [Shape.PropertyGet, Shape.PropertySet],
            _ => [Shape.PropertyGet, Shape.PropertyLet],
        };
    }

    private static string Describe(Shape shape) => shape switch
    {
        Shape.Sub => "Sub",
        Shape.Function => "Function",
        Shape.PropertyGet => "Property Get",
        Shape.PropertyLet => "Property Let",
        _ => "Property Set",
    };

    // MS-VBAL §5.3.1.9: why the candidate is not the implemented name declaration of the member, or null when it is.
    private static string? Incompatibility(VBClassModuleSymbol implemented, VBTypeMemberSymbol member, VBTypeMemberSymbol candidate)
    {
        var shape = ShapeOf(candidate)!.Value;

        if (IsVariable(member))
        {
            if (!IsPropertyAccessor(shape))
            {
                return $"'{member.Name}' is a variable, which is implemented by property declarations, and this is a {Describe(shape)}";
            }

            // the type a property carries: what a Get returns, and what the value parameter of a Let or a Set takes.
            var type = shape == Shape.PropertyGet
                ? candidate.ResolvedType
                : ParameterLists.Of(candidate) is { Length: > 0 } parameters ? parameters[^1].ResolvedType : null;
            return type is not null && !ParameterLists.SameType(type, member.ResolvedType)
                ? $"its type, {type.Name}, is not the interface member's, {member.ResolvedType.Name}"
                : null;
        }

        if (shape != ShapeOf(member))
        {
            return $"'{member.Name}' is a {Describe(ShapeOf(member)!.Value)}, and this is a {Describe(shape)}";
        }

        if (ParameterLists.Incompatibility(ParameterLists.Of(candidate), ParameterLists.Of(member), optionalsAndDefaults: true) is { } reason)
        {
            return $"its parameter list is not equivalent to the interface member's: {reason}";
        }

        return shape is Shape.Function or Shape.PropertyGet && !ParameterLists.SameType(candidate.ResolvedType, member.ResolvedType)
            ? $"its type, {candidate.ResolvedType.Name}, is not the interface member's, {member.ResolvedType.Name}"
            : null;
    }
}
