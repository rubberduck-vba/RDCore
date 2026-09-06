using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
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
/// Deferred, pending decisions the author owns: members are all <see cref="ScopeKind.Instance"/> and
/// <c>Declare</c>s <see cref="ScopeKind.External"/> regardless of the module kind (a runtime-model
/// call); and <c>Const Foo$</c> loses its type-declaration character because
/// <c>ConstantDeclarationNode</c> has no type-hint field (a parser change).
/// </remarks>
internal sealed class SymbolBuilder(Uri workspaceRoot, Uri moduleUri, ISymbolResolver resolver)
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
            workspaceRoot, moduleUri, node.Name, ScopeKind.Instance, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildFunction(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBFunctionMemberSymbol(
            workspaceRoot, moduleUri, node.Name, ScopeKind.Instance, SymbolKindExt.Function,
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
            workspaceRoot, moduleUri, ScopeKind.Instance, node.Name, range, range, node.AccessModifier);
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
            workspaceRoot, moduleUri, node.Name, ScopeKind.Instance, SymbolKindExt.Property,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildPropertySet(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBPropertySetMemberSymbol(
            workspaceRoot, moduleUri, node.Name, ScopeKind.Instance, SymbolKindExt.Property,
            VBVoidType.TypeInfo, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildExternal(ExternalMemberDeclarationNode node)
    {
        var range = RangeOf(node);
        if (node.MemberKind == MemberKind.ExternalFunction)
        {
            var function = new VBExternalFunctionMemberSymbol(
                workspaceRoot, moduleUri, node.Name, ScopeKind.External, SymbolKindExt.Function,
                VBUnknownType.TypeInfo, range, range, node.AccessModifier, node.IsPtrSafe, node.Library, node.Alias);
            return function with
            {
                ResolvedType = ReturnType(node, function.Uri),
                Parameters = BuildParameters(node, function.Uri),
            };
        }

        var procedure = new VBExternalSubMemberSymbol(
            workspaceRoot, moduleUri, node.Name, ScopeKind.External, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, range, range, node.AccessModifier, node.IsPtrSafe, node.Library, node.Alias);
        return procedure with { Parameters = BuildParameters(node, procedure.Uri) };
    }

    public Symbol BuildEvent(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        var symbol = new VBEventMemberSymbol(workspaceRoot, moduleUri, node.Name, range, range, node.AccessModifier);
        return symbol with { Parameters = BuildParameters(node, symbol.Uri) };
    }

    public Symbol BuildUserDefinedType(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        return new VBUserDefinedTypeMemberSymbol(
            workspaceRoot, moduleUri, node.Name, ScopeKind.Instance, range, range, node.AccessModifier);
    }

    public Symbol BuildEnum(MemberDeclarationNode node)
    {
        var range = RangeOf(node);
        return new VBEnumMemberSymbol(
            workspaceRoot, moduleUri, node.Name, ScopeKind.Instance, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, range, range, node.AccessModifier);
    }

    // Enum members parent to the enum symbol, not the module.
    public IEnumerable<Symbol> BuildEnumMembers(MemberDeclarationNode enumNode, Uri enumUri)
    {
        foreach (var member in enumNode.Children.OfType<ConstantDeclarationNode>())
        {
            var range = RangeOf(member);
            yield return new VBEnumConstMemberSymbol(
                workspaceRoot, enumUri, member.Name, ScopeKind.Instance, SymbolKindExt.EnumMember, range, range);
        }
    }

    public Symbol BuildModuleField(VariableDeclarationNode node)
    {
        var range = RangeOf(node);
        var type = DeclaredType(AsTypeOf(node), node.TypeHint, moduleUri);
        return new VBModuleFieldVariableMemberSymbol(
            workspaceRoot, moduleUri, node.Name, type, range, range, node.AccessModifier);
    }

    public Symbol BuildConstant(ConstantDeclarationNode node)
    {
        var range = RangeOf(node);
        // ConstantDeclarationNode carries no type-hint token; resolve from an As clause if present.
        var type = DeclaredType(AsTypeOf(node), typeHint: null, moduleUri);
        return new VBConstantMemberSymbol(
            workspaceRoot, moduleUri, node.Name, ScopeKind.Instance, type, range, range, node.AccessModifier);
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

        return resolver.Resolve(typeName, ScopeKind.Global, handle) switch
        {
            BoundTypedSymbol bound => bound.ResolvedType,
            UnboundTypedSymbol unbound => unbound.ResolvedType,
            _ => VBUnknownType.TypeInfo,
        };
    }

    private static SourceRange RangeOf(SyntaxNode node) => node.SourceLocation.Range;
}
