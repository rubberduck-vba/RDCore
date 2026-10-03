using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using System.Collections.Immutable;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Reconstructs runtime member <see cref="Symbol"/>s from the descriptors carried by a
/// <see cref="DefineSymbolsParams"/>. The inverse of the language server's descriptor projection;
/// one trivial method per <see cref="SymbolDescriptorKind"/>, mirroring the language server's
/// <c>SymbolBuilder</c>. Declared types are bound through a caller-supplied resolver so the same
/// call path binds real types once the environment host composes a project/library resolver — a
/// name the resolver returns <c>null</c> for stays <see cref="VBUnknownType"/>.
/// </summary>
public static class SymbolDescriptorReader
{
    /// <summary>
    /// Reconstructs every member symbol in the request, including the nested members
    /// (<c>Enum</c> constants, user-defined-<c>Type</c> fields) parented to their owner rather than
    /// the module.
    /// </summary>
    /// <param name="request">The define-symbols request.</param>
    /// <param name="resolveType">
    /// Resolves a declared type name to a <see cref="VBType"/>, or returns <c>null</c> when it cannot.
    /// </param>
    /// <exception cref="ArgumentException"><see cref="DefineSymbolsParams.WorkspaceRoot"/> is not set.</exception>
    public static IEnumerable<Symbol> Read(DefineSymbolsParams request, Func<string, VBType?> resolveType)
    {
        var workspaceRoot = request.WorkspaceRoot
            ?? throw new ArgumentException("A WorkspaceRoot is required to address the reconstructed symbols.", nameof(request));

        // re-derive the module symbol uri the same way ProjectSymbolProvider does (parent + name fragment),
        // so member uris line up with the module symbol the host already composed.
        var moduleUri = ChildUri(workspaceRoot, request.ModuleName);

        foreach (var descriptor in request.Symbols)
        {
            // the primary symbol is always yielded first, ahead of any nested members (enum
            // constants, udt fields) that carry their own descriptors — so only the first carries
            // the descriptor's multi-branch Definitions.
            var isPrimary = true;
            foreach (var symbol in Read(descriptor, workspaceRoot, moduleUri, resolveType))
            {
                yield return isPrimary ? WithUserMemId(WithDefinitions(symbol, descriptor), descriptor) : symbol;
                isPrimary = false;
            }
        }
    }

    // the id the member's module gave it (VB_UserMemId): what marks a class's default member and its enumeration member.
    private static Symbol WithUserMemId(Symbol symbol, SymbolDescriptor descriptor)
        => descriptor.UserMemId is { } userMemId ? symbol.With(SymbolProperties.UserMemId, userMemId) : symbol;

    // a member declared in more than one conditional-compilation branch arrives as one descriptor
    // carrying every site; rebuild them onto the reconstructed symbol. Range/SelectionRange stay the
    // primary site the ctor already set.
    private static Symbol WithDefinitions(Symbol symbol, SymbolDescriptor descriptor)
    {
        if (descriptor.Definitions.Length <= 1 || symbol is not WorkspaceSymbol bound)
        {
            return symbol;
        }

        return bound with
        {
            Definitions = [.. descriptor.Definitions.Select(definition =>
                new SymbolDefinition(definition.Range, definition.SelectionRange, definition.State))],
        };
    }

    private static IEnumerable<Symbol> Read(SymbolDescriptor node, Uri workspaceRoot, Uri parentUri, Func<string, VBType?> resolveType)
    {
        VBType Declared(string? typeName) => DeclaredType(typeName, node.Array, resolveType);
        ImmutableArray<VBParameterSymbol> Parameters(Uri memberUri) => ReadParameters(node, workspaceRoot, memberUri, resolveType);
        ImmutableArray<BoundTypedSymbol> Locals(Uri memberUri) => ReadLocals(node, workspaceRoot, memberUri, resolveType);

        switch (node.Kind)
        {
            case SymbolDescriptorKind.Procedure:
            {
                var symbol = new VBProcedureMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, SymbolKindExt.Procedure,
                    VBVoidType.TypeInfo, node.Range, node.SelectionRange, node.AccessModifier);
                yield return symbol with { Parameters = Parameters(symbol.Uri), Locals = Locals(symbol.Uri) };
                break;
            }
            case SymbolDescriptorKind.Function:
            {
                var symbol = new VBFunctionMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, SymbolKindExt.Function,
                    Declared(node.DeclaredTypeName), node.Range, node.SelectionRange, node.AccessModifier);
                yield return symbol with { Parameters = Parameters(symbol.Uri), Locals = Locals(symbol.Uri) };
                break;
            }
            case SymbolDescriptorKind.PropertyGet:
            {
                var symbol = new VBPropertyGetMemberSymbol(
                    workspaceRoot, parentUri, node.Scope, node.Name, node.Range, node.SelectionRange, node.AccessModifier);
                yield return symbol with
                {
                    ResolvedType = Declared(node.DeclaredTypeName),
                    Parameters = Parameters(symbol.Uri),
                    Locals = Locals(symbol.Uri),
                };
                break;
            }
            case SymbolDescriptorKind.PropertyLet:
            {
                var symbol = new VBPropertyLetMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, SymbolKindExt.Property,
                    VBVoidType.TypeInfo, node.Range, node.SelectionRange, node.AccessModifier);
                yield return symbol with { Parameters = Parameters(symbol.Uri) };
                break;
            }
            case SymbolDescriptorKind.PropertySet:
            {
                var symbol = new VBPropertySetMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, SymbolKindExt.Property,
                    VBVoidType.TypeInfo, node.Range, node.SelectionRange, node.AccessModifier);
                yield return symbol with { Parameters = Parameters(symbol.Uri) };
                break;
            }
            case SymbolDescriptorKind.ExternalProcedure:
            {
                var external = node.External ?? new ExternalDescriptor();
                var symbol = new VBExternalSubMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, SymbolKindExt.Procedure,
                    VBVoidType.TypeInfo, node.Range, node.SelectionRange, node.AccessModifier,
                    external.IsPtrSafe, external.Library, external.Alias);
                yield return symbol with { Parameters = Parameters(symbol.Uri) };
                break;
            }
            case SymbolDescriptorKind.ExternalFunction:
            {
                var external = node.External ?? new ExternalDescriptor();
                var symbol = new VBExternalFunctionMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, SymbolKindExt.Function,
                    Declared(node.DeclaredTypeName), node.Range, node.SelectionRange, node.AccessModifier,
                    external.IsPtrSafe, external.Library, external.Alias);
                yield return symbol with { Parameters = Parameters(symbol.Uri) };
                break;
            }
            case SymbolDescriptorKind.Event:
            {
                var evt = new VBEventMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, node.Range, node.SelectionRange, node.AccessModifier);
                yield return evt with { Parameters = Parameters(evt.Uri) };
                break;
            }

            case SymbolDescriptorKind.UserDefinedType:
            {
                var udt = new VBUserDefinedTypeMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, node.Range, node.SelectionRange, node.AccessModifier);
                yield return udt;
                foreach (var field in node.Members.Where(m => m.Kind == SymbolDescriptorKind.UserDefinedTypeField))
                {
                    yield return ReadUserDefinedTypeField(field, workspaceRoot, udt.Uri, resolveType);
                }
                break;
            }
            case SymbolDescriptorKind.UserDefinedTypeField:
                yield return ReadUserDefinedTypeField(node, workspaceRoot, parentUri, resolveType);
                break;

            case SymbolDescriptorKind.Enum:
            {
                var enumSymbol = new VBEnumMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, SymbolKindExt.Enum,
                    VBUnknownType.TypeInfo, node.Range, node.SelectionRange, node.AccessModifier);
                yield return enumSymbol;
                foreach (var member in node.Members.Where(m => m.Kind == SymbolDescriptorKind.EnumMember))
                {
                    yield return new VBEnumConstMemberSymbol(
                        workspaceRoot, enumSymbol.Uri, member.Name, member.Scope, SymbolKindExt.EnumMember,
                        member.Range, member.SelectionRange);
                }
                break;
            }
            case SymbolDescriptorKind.EnumMember:
                yield return new VBEnumConstMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, SymbolKindExt.EnumMember,
                    node.Range, node.SelectionRange);
                break;

            case SymbolDescriptorKind.ModuleField:
            {
                Symbol field = new VBModuleFieldVariableMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, Declared(node.DeclaredTypeName),
                    node.Range, node.SelectionRange, node.AccessModifier);
                field = WithArrayBounds(field, node.Array);
                field = node.IsWithEvents ? field.With(SymbolProperties.WithEvents, true) : field;
                yield return node.IsAutoInstantiated ? field.With(SymbolProperties.AutoInstantiated, true) : field;
                break;
            }

            case SymbolDescriptorKind.ModuleConstant:
                // the constant's own value is its single Constants entry - it has no storage to read one
                // back from, so the expression is what a use site substitutes.
                yield return new VBConstantMemberSymbol(
                    workspaceRoot, parentUri, node.Name, node.Scope, Declared(node.DeclaredTypeName),
                    node.Range, node.SelectionRange, node.AccessModifier,
                    node.Constants.FirstOrDefault()?.Value);
                break;
        }
    }

    // the declared type of a descriptor: the name resolved, which for an array is its element's and which the array descriptor
    // then makes an array of - fixed-size, or resizable (a resizable Byte array is its own type, RD-VBAL 2.4.1.3).
    private static VBType DeclaredType(string? typeName, ArrayDescriptor? array, Func<string, VBType?> resolveType)
    {
        var type = Resolve(typeName, resolveType);
        return array switch
        {
            null => type,
            { IsFixedSize: true } => new VBFixedSizeArrayType(type),
            _ => type is VBByteType ? VBResizableByteArrayType.TypeInfo : new VBResizableArrayType(type),
        };
    }

    // a name that does not resolve here, a module at a time, is not known yet and is not an error yet: it is kept as written, for whoever has the
    // whole workspace to resolve it again (VBUnresolvedType). No name is no declared type at all.
    private static VBType Resolve(string? typeName, Func<string, VBType?> resolveType)
        => typeName is null ? VBUnknownType.TypeInfo : resolveType(typeName) ?? new VBUnresolvedType(typeName);

    // the bounds a fixed-size array was declared with ride the symbol (SymbolProperties.ArrayBounds), as they do where the
    // declaration was read.
    private static Symbol WithArrayBounds(Symbol variable, ArrayDescriptor? array)
        => array is { IsFixedSize: true, Bounds.IsDefaultOrEmpty: false }
            ? variable.With(SymbolProperties.ArrayBounds, [.. array.Bounds.Select(bound =>
                new RDCore.SDK.Model.AST.Declarations.ArrayDimensionBound(null, string.Empty, bound.Lower, bound.Upper))])
            : variable;

    private static Symbol ReadUserDefinedTypeField(
        SymbolDescriptor field, Uri workspaceRoot, Uri userDefinedTypeUri, Func<string, VBType?> resolveType)
    {
        var type = Resolve(field.DeclaredTypeName, resolveType);

        return new VBUserDefinedTypeFieldSymbol(
            workspaceRoot, userDefinedTypeUri, field.Name, type, field.Range, field.SelectionRange, field.AccessModifier);
    }

    /// <summary>
    /// Reconstructs a procedure's <c>Dim</c>/<c>Static</c> variables and its <c>Const</c> declarations.
    /// An invocation allocates frame storage from the variables (<strong>MS-VBAL §5.4.3</strong> step 4),
    /// so a procedure reconstructed without them has a body that cannot assign to any of its own locals;
    /// a constant is allocated nothing and travels so that its name resolves at all.
    /// </summary>
    private static ImmutableArray<BoundTypedSymbol> ReadLocals(
        SymbolDescriptor member, Uri workspaceRoot, Uri memberUri, Func<string, VBType?> resolveType)
    {
        if (member.Locals.IsDefaultOrEmpty && member.Constants.IsDefaultOrEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<BoundTypedSymbol>(
            member.Locals.IsDefault ? 0 : member.Locals.Length);
        foreach (var constant in member.Constants.IsDefault ? [] : member.Constants)
        {
            builder.Add(new VBLocalConstantSymbol(
                workspaceRoot, memberUri, constant.Name, constant.Range, constant.SelectionRange,
                Resolve(constant.DeclaredTypeName, resolveType),
                constant.Value));
        }
        foreach (var local in member.Locals.IsDefault ? [] : member.Locals)
        {
            var variable = new VBLocalVariableSymbol(
                workspaceRoot, memberUri, local.Name, ScopeKind.Local, local.Range, local.SelectionRange,
                local.IsStatic,
                DeclaredType(local.DeclaredTypeName, local.Array, resolveType),
                local.DeclaredBy);
            var bounded = (BoundTypedSymbol)WithArrayBounds(variable, local.Array);
            builder.Add(local.IsAutoInstantiated ? (BoundTypedSymbol)bounded.With(SymbolProperties.AutoInstantiated, true) : bounded);
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<VBParameterSymbol> ReadParameters(
        SymbolDescriptor member, Uri workspaceRoot, Uri memberUri, Func<string, VBType?> resolveType)
    {
        if (member.Parameters.IsDefaultOrEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<VBParameterSymbol>(member.Parameters.Length);
        foreach (var parameter in member.Parameters)
        {
            builder.Add(parameter.IsParamArray
                ? new ParamArrayParameterSymbol(
                    workspaceRoot, memberUri, parameter.Name, parameter.Range, parameter.Range, parameter.ParameterKind)
                : new VBParameterSymbol(
                    workspaceRoot, memberUri, parameter.Name, parameter.Range, parameter.Range, parameter.ParameterKind,
                    DeclaredType(parameter.DeclaredTypeName, parameter.Array, resolveType),
                    parameter.IsOptional,
                    parameter.DefaultValue));
        }
        return builder.ToImmutable();
    }

    // mirrors Symbol.CreateUri: the parent uri with the child name as its fragment (dotted when nested).
    private static Uri ChildUri(Uri parent, string name)
    {
        var builder = new UriBuilder(parent)
        {
            Fragment = string.IsNullOrEmpty(parent.Fragment) ? name : $"{parent.Fragment.TrimStart('#')}.{name}",
        };
        return builder.Uri;
    }
}
