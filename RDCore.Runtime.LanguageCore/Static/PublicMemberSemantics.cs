using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;
using RDCore.SDK;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.2.3.1, §5.2.3.3, §5.3.1, §5.3.1.5
// as MS-VBA does, not all as MS-VBAL says: a procedural module's public members may use its private types, and a field of its Public Type a private Enum or type.
// MS-VBA also accepts, as RD-VBA does, a procedural module's Public Type as a parameter of a class's public procedure or event, which §5.3.1.5 refuses.
public static class PublicMemberSemantics
{
    public static ImmutableArray<VBCompileErrorInfo> Evaluate(Symbol module, IReadOnlyList<VBTypeMemberSymbol> members, ISymbolResolver resolver)
    {
        var errors = ImmutableArray.CreateBuilder<VBCompileErrorInfo>();
        var isObjectModule = module is VBClassModuleSymbol;

        foreach (var member in members)
        {
            if (member is VBEnumConstMemberSymbol or VBUserDefinedTypeFieldSymbol || !IsPublic(member))
            {
                continue;
            }

            if (member is VBUserDefinedTypeMemberSymbol or VBEnumMemberSymbol)
            {
                if (isObjectModule && member is VBUserDefinedTypeMemberSymbol)
                {
                    errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.PublicUserDefinedTypeInObjectModule, LocationOf(member),
                        string.Format(Exceptions.VBCompileError_PublicUserDefinedTypeInObjectModule_Verbose, member.Name)));
                }

                continue;
            }

            if (isObjectModule && IsNotAllowedInAnObjectModule(member, module, resolver))
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.NotAllowedAsPublicMemberOfObjectModule, LocationOf(member),
                    string.Format(Exceptions.VBCompileError_NotAllowedAsPublicMemberOfObjectModule_Verbose, member.Name)));
                continue;
            }

            if (PrivateTypeIn(member, module, isObjectModule, resolver) is { } found)
            {
                errors.Add(VBCompileErrorInfo.For(VBCompileErrorId.PrivateTypeInPublicMember, new SourceLocation(member.ParentUri, found.Range),
                    string.Format(Exceptions.VBCompileError_PrivateTypeInPublicMember_Verbose, member.Name, found.Type.Name)));
            }
        }

        return errors.ToImmutable();
    }

    private static SourceLocation LocationOf(VBTypeMemberSymbol member) => new(member.ParentUri, member.SelectionRange);

    // a variable or a constant with no modifier is private (Dim, Const); everything else a module declares with none is public.
    private static bool IsPublic(VBTypeMemberSymbol member) => member.AccessModifier switch
    {
        AccessModifier.Public or AccessModifier.Global => true,
        AccessModifier.Implicit => member is not (VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol or VBConstantMemberSymbol),
        _ => false,
    };

    private static bool IsNotAllowedInAnObjectModule(VBTypeMemberSymbol member, Symbol module, ISymbolResolver resolver) => member switch
    {
        VBConstantMemberSymbol or VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol => true,
        _ when member.AccessModifier is AccessModifier.Global => true,
        VBModuleFieldVariableMemberSymbol or VBInstanceFieldVariableMemberSymbol => ((ITypedSymbol)member).ResolvedType is VBFixedStringType or VBArrayType
            || TypeSymbolOf(((ITypedSymbol)member).ResolvedType, module, resolver) is VBUserDefinedTypeMemberSymbol,
        _ => false,
    };

    // the first of the member's declared types that is a private Enum, or in an object module a private user-defined type, and where it is declared:
    // the member's own type (a variable's, a function's or a property's result), then each parameter's.
    private static (VBType Type, SourceRange Range)? PrivateTypeIn(VBTypeMemberSymbol member, Symbol module, bool isObjectModule, ISymbolResolver resolver)
    {
        if (member is ITypedSymbol { ResolvedType: var own } && IsPrivateType(own, module, isObjectModule, resolver))
        {
            return (own, member.SelectionRange);
        }

        foreach (var parameter in DeclarationStaticSemanticsEvaluator.ParametersOf(member))
        {
            if (IsPrivateType(parameter.ResolvedType, module, isObjectModule, resolver))
            {
                return (parameter.ResolvedType, parameter.SelectionRange);
            }
        }

        return null;
    }

    private static bool IsPrivateType(VBType type, Symbol module, bool isObjectModule, ISymbolResolver resolver) => TypeSymbolOf(type, module, resolver) switch
    {
        VBEnumMemberSymbol { AccessModifier: AccessModifier.Private } => true,
        VBUserDefinedTypeMemberSymbol { AccessModifier: AccessModifier.Private } => isObjectModule,
        _ => false,
    };

    // the declaration of the user-defined type or the Enum a declared type is, an array's element's included; a name that did not resolve where the
    // declaration was read is resolved again as seen from the module. Null for any other type, and for a name that still does not resolve.
    private static Symbol? TypeSymbolOf(VBType type, Symbol module, ISymbolResolver resolver) => type switch
    {
        VBArrayType array => TypeSymbolOf(array.ItemType, module, resolver),
        VBUserDefinedType record => record.Symbol,
        VBEnumType enumeration => enumeration.Symbol,
        VBUnresolvedType { DeclaredName: var name } => Resolve(name, module, resolver),
        _ => null,
    };

    private static Symbol? Resolve(string name, Symbol module, ISymbolResolver resolver)
    {
        var split = name.LastIndexOf('.');
        var (qualifier, typeName) = split < 0 ? ((string?)null, name) : (name[..split], name[(split + 1)..]);
        return VBProjectSymbol.ResolveQualifiedType(resolver, qualifier, typeName, module.Uri) is { IsResolved: true, Symbol: var symbol } ? symbol : null;
    }
}
