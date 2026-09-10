using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Turns a declaration AST node into the bound <see cref="Symbol"/> it declares. One trivial method
/// per node kind, mirroring <c>DeclarationNodeBuilder</c> on the parsing side. Declared types are
/// resolved through <see cref="ISymbolResolver"/> so the same call path binds real types once a
/// resolver with intrinsic/library/project symbols is available; a type reference that doesn't
/// resolve stays <see cref="VBUnknownType"/>.
/// </summary>
/// <remarks>
/// <paramref name="memberScope"/> is the allocation scope every module-level member is defined in —
/// <see cref="ScopeKind.Module"/> for a standard module, <see cref="ScopeKind.Instance"/> for a
/// class module. Parameters are <see cref="ScopeKind.Local"/> and user-defined-type fields are
/// <see cref="ScopeKind.Instance"/> (reached through an instance of the type), regardless.
/// </remarks>
internal sealed class SymbolBuilder(Uri workspaceRoot, Uri moduleUri, ScopeKind memberScope, ISymbolResolver resolver)
{
    // MS-VBAL 3.3.2 type-declaration characters name a reserved type the resolver can bind.
    private static readonly ImmutableDictionary<string, string> _typeHintNames = new Dictionary<string, string>
    {
        ["%"] = VBTypeNames.VBInteger,
        ["&"] = VBTypeNames.VBLong,
        ["^"] = VBTypeNames.VBLongLong,
        ["!"] = VBTypeNames.VBSingle,
        ["#"] = VBTypeNames.VBDouble,
        ["@"] = VBTypeNames.VBCurrency,
        ["$"] = VBTypeNames.VBString,
    }.ToImmutableDictionary();

    public Symbol BuildProcedure(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBProcedureMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildFunction(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBFunctionMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Function,
            VBUnknownType.TypeInfo, range, range, node.AccessModifier);
        return symbol with
        {
            ResolvedType = ReturnType(node, symbol.Uri),
            Parameters = BuildParameters(node, symbol.Uri),
        };
    }

    public Symbol BuildPropertyGet(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBPropertyGetMemberSymbol(
            workspaceRoot, moduleUri, memberScope, node.Name, range, range, node.AccessModifier);
        return symbol with
        {
            ResolvedType = ReturnType(node, symbol.Uri),
            Parameters = BuildParameters(node, symbol.Uri),
        };
    }

    public Symbol BuildPropertyLet(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBPropertyLetMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Property,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildPropertySet(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBPropertySetMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Property,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildExternal(ExternalMemberDeclarationNode node)
    {
        var range = RangeOf(node);
        if (node.MemberKind == MemberKind.ExternalFunction)
        {
            var function = new VBExternalFunctionMemberSymbol(
                workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Function,
                VBUnknownType.TypeInfo, range, range, node.AccessModifier, node.IsPtrSafe, node.Library, node.Alias);
            return function with
            {
                ResolvedType = ReturnType(node, function.Uri),
                Parameters = BuildParameters(node, function.Uri),
            };
        }

        var procedure = new VBExternalSubMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, range, range, node.AccessModifier, node.IsPtrSafe, node.Library, node.Alias);
        return procedure with { Parameters = BuildParameters(node, procedure.Uri) };
    }

    public Symbol BuildEvent(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBEventMemberSymbol(workspaceRoot, moduleUri, node.Name, memberScope, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildUserDefinedType(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        return new VBUserDefinedTypeMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, range, range, node.AccessModifier);
    }

    public Symbol BuildEnum(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        return new VBEnumMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, range, range, node.AccessModifier);
    }

    // Enum members parent to the enum symbol, not the module.
    public IEnumerable<Symbol> BuildEnumMembers(MemberDeclarationNode enumNode, Uri enumUri)
    {
        foreach (var member in enumNode.Children.OfType<ConstantDeclarationNode>())
        {
            var range = RangeOf(member);
            yield return new VBEnumConstMemberSymbol(
                workspaceRoot, enumUri, member.Name, memberScope, SymbolKindExt.EnumMember, range, range);
        }
    }

    public Symbol BuildModuleField(VariableDeclarationNode node)
    {
        var range = RangeOf(node);
        var type = DeclaredType(AsTypeOf(node), node.TypeHint, moduleUri);
        return new VBModuleFieldVariableMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, type, range, range, node.AccessModifier);
    }

    public Symbol BuildConstant(ConstantDeclarationNode node)
    {
        var range = RangeOf(node);
        var type = DeclaredType(AsTypeOf(node), node.TypeHint, moduleUri);
        return new VBConstantMemberSymbol(
            workspaceRoot, moduleUri, node.Name, memberScope, type, range, range, node.AccessModifier);
    }

    // A UDT field parents to the enclosing user-defined-type symbol, not the module.
    public Symbol BuildUserDefinedTypeField(MemberDeclarationNode node, Uri userDefinedTypeUri)
    {
        var range = RangeOf(node);
        var type = DeclaredType(AsTypeOf(node), typeHint: null, userDefinedTypeUri);
        return new VBUserDefinedTypeFieldSymbol(
            workspaceRoot, userDefinedTypeUri, node.Name, type, range, range, node.AccessModifier);
    }

    private ImmutableArray<VBParameterSymbol> BuildParameters(MemberDeclarationNode member, Uri memberUri)
    {
        var parameters = member.Children.OfType<ParameterDeclarationNode>().ToArray();
        if (parameters.Length == 0)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<VBParameterSymbol>(parameters.Length);
        foreach (var parameter in parameters)
        {
            var range = RangeOf(parameter);
            builder.Add(parameter.IsParamArray
                ? new ParamArrayParameterSymbol(workspaceRoot, memberUri, parameter.Name, range, range, parameter.ParameterKind)
                : new VBParameterSymbol(
                    workspaceRoot, memberUri, parameter.Name, range, range, parameter.ParameterKind,
                    DeclaredType(AsTypeOf(parameter), typeHint: null, memberUri), parameter.IsOptional));
        }
        return builder.ToImmutable();
    }

    private VBType ReturnType(MemberDeclarationNode member, Uri memberUri)
        => DeclaredType(member.Children.OfType<AsTypeExpressionNode>().FirstOrDefault(), typeHint: null, memberUri);

    private static AsTypeExpressionNode? AsTypeOf(SyntaxNode node)
        => node.Children.OfType<AsTypeExpressionNode>().FirstOrDefault();

    // A declared type is a name the resolver binds from the given scope. A qualified name or an array
    // definition needs more than a name lookup, so it is left unresolved for a later semantic pass.
    private VBType DeclaredType(AsTypeExpressionNode? asType, string? typeHint, Uri handle)
    {
        string? typeName = null;
        if (asType is { QualifierName: null, IsArrayDef: false })
        {
            typeName = asType.TypeName;
        }
        else if (asType is null && typeHint is not null && _typeHintNames.TryGetValue(typeHint, out var hintName))
        {
            typeName = hintName;
        }

        if (typeName is null)
        {
            return VBUnknownType.TypeInfo;
        }

        return ResolveTypeName(typeName, handle);
    }

    // Binds a reserved/declared type name through the resolver; an unresolved name stays Unknown. A
    // resolved user-defined type or enum is a symbol carrying no VBType of its own, so build one.
    private VBType ResolveTypeName(string typeName, Uri handle)
        => resolver.Resolve(typeName, ScopeKind.Global, handle).Symbol switch
        {
            VBUserDefinedTypeMemberSymbol udt => new VBUserDefinedType(udt, udt.Members),
            VBEnumMemberSymbol enumType => new VBEnumType(enumType, members: null),
            BoundTypedSymbol bound => bound.ResolvedType,
            UnboundTypedSymbol unbound => unbound.ResolvedType,
            _ => VBUnknownType.TypeInfo,
        };

    // Procedure-local Dim/Static/Const declarations, plus symbols a ReDim introduces. Locals parent
    // to the procedure symbol (MS-VBAL 5.4.3.1-3), mirroring how enum members / UDT fields parent to
    // their declaration. <paramref name="outerScopeNames"/> are the names already visible from
    // outside the body — the procedure's parameters and this module's fields/consts — against which
    // a ReDim target is a re-dimension rather than an implicit declaration.
    public IEnumerable<Symbol> BuildLocals(MemberDeclarationNode member, Uri procedureUri, IReadOnlySet<string> outerScopeNames)
    {
        var results = new List<Symbol>();
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // pass 1 — explicit declarations. order-independent: VBA hoists them, so a name declared
        // anywhere in the body is in scope for the whole procedure.
        foreach (var child in member.Children)
        {
            switch (child)
            {
                case VariableDeclarationNode local:
                    results.Add(BuildLocalVariable(local, procedureUri));
                    declared.Add(local.Name);
                    break;

                case ConstantDeclarationNode { ConstKind: ConstKind.Local } local:
                    results.Add(BuildLocalConstant(local, procedureUri));
                    declared.Add(local.Name);
                    break;
            }
        }

        // pass 2 — ReDim. an unqualified target that resolves to nothing implicitly declares a
        // dynamic-array local (MS-VBAL 5.4.3.3; legal under Option Explicit). NOTE cross-module
        // globals aren't visible here, so a ReDim of one is introduced until the resolver reconciles
        // it — the LocalDeclarationKind.ReDim marker is that pass's hook.
        foreach (var redim in member.Children.OfType<RedimDeclarationNode>())
        {
            if (redim.QualifierName is not null || outerScopeNames.Contains(redim.Name) || !declared.Add(redim.Name))
            {
                continue;
            }

            results.Add(BuildRedimLocal(redim, procedureUri));
        }

        return results;
    }

    public Symbol BuildLocalVariable(VariableDeclarationNode node, Uri procedureUri)
    {
        var range = RangeOf(node);
        var asType = AsTypeOf(node);
        var elementType = ArrayElementType(asType, node.TypeHint, procedureUri);
        var type = VariableType(elementType, asType, node.Children.OfType<ArrayBoundsNode>().FirstOrDefault());
        return new VBLocalVariableSymbol(
            workspaceRoot, procedureUri, node.Name, ScopeKind.Local, range, range,
            IsStatic: node.IsStatic, ResolvedType: type);
    }

    public Symbol BuildLocalConstant(ConstantDeclarationNode node, Uri procedureUri)
    {
        var range = RangeOf(node);
        var type = DeclaredType(AsTypeOf(node), node.TypeHint, procedureUri);
        return new VBLocalConstantSymbol(workspaceRoot, procedureUri, node.Name, range, range, type);
    }

    // A ReDim always targets a dynamic array (MS-VBAL 5.4.3.3); the new dimensions stay unresolved.
    public Symbol BuildRedimLocal(RedimDeclarationNode node, Uri procedureUri)
    {
        var range = RangeOf(node);
        var elementType = ArrayElementType(AsTypeOf(node), node.TypeHint, procedureUri);
        return new VBLocalVariableSymbol(
            workspaceRoot, procedureUri, node.Name, ScopeKind.Local, range, range,
            ResolvedType: ResizableArrayType(elementType), DeclaredBy: LocalDeclarationKind.ReDim);
    }

    // The element type for an array declaration also reads the name behind a trailing `()` on the
    // As-clause (`Dim x As Long()`), which DeclaredType leaves unresolved for the scalar case.
    private VBType ArrayElementType(AsTypeExpressionNode? asType, string? typeHint, Uri handle)
        => asType is { IsArrayDef: true, QualifierName: null }
            ? ResolveTypeName(asType.TypeName, handle)
            : DeclaredType(asType, typeHint, handle);

    // MS-VBAL 5.2.3.1 / RD-VBAL 2.5.2.1.2: an array-dim clause with bounds declares a fixed-size
    // array; an empty `()` clause, or a trailing `()` on the As-clause, declares a dynamic array —
    // a dynamic `Byte()` array binds the specialized VBResizableByteArrayType (RD-VBAL 2.4.1.3).
    // Bound evaluation and the Variant default for an omitted type are a later semantic pass's concern.
    private static VBType VariableType(VBType elementType, AsTypeExpressionNode? asType, ArrayBoundsNode? bounds)
    {
        if (bounds is { IsResizable: false })
        {
            return new VBFixedSizeArrayType(elementType);
        }

        if (bounds is not null || asType is { IsArrayDef: true })
        {
            return ResizableArrayType(elementType);
        }

        return elementType;
    }

    // RD-VBAL 2.4.1.3: a dynamic Byte() array binds the specialized VBResizableByteArrayType.
    private static VBType ResizableArrayType(VBType elementType)
        => elementType is VBByteType ? VBResizableByteArrayType.TypeInfo : new VBResizableArrayType(elementType);

    private static SourceRange RangeOf(SyntaxNode node) => node.SourceLocation.Range;
}
