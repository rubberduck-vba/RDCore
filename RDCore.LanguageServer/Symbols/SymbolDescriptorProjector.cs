using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Protocol;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Projects the flat <see cref="Symbol"/> stream from <see cref="SyntaxTreeSymbolProvider"/> onto the
/// <see cref="SymbolDescriptor"/> tree carried over <c>rdcore/host/symbols/define</c>: top-level
/// members parent to the module, and the members the provider yields separately (<c>Enum</c>
/// constants, user-defined-<c>Type</c> fields) are nested under their owner.
/// </summary>
/// <remarks>
/// A declared type reaches the descriptor as a name only when it resolved — an intrinsic today. A
/// project or library type is <see cref="VBUnknownType"/> on both sides for now, so its name is not
/// carried (it would be redundant); the descriptor's <see cref="SymbolDescriptor.DeclaredTypeName"/>
/// is <c>null</c>.
/// </remarks>
internal static class SymbolDescriptorProjector
{
    public static ImmutableArray<SymbolDescriptor> Project(IEnumerable<Symbol> symbols, Uri moduleUri)
    {
        // Uri.Equals ignores the fragment, but symbol parentage lives entirely in the fragment
        // (workspace#Module.Member), so compare by the full string instead.
        var moduleKey = moduleUri.ToString();
        var all = symbols as IReadOnlyCollection<Symbol> ?? [.. symbols];
        var childrenByParent = all.Where(s => s.ParentUri.ToString() != moduleKey).ToLookup(s => s.ParentUri.ToString());

        var builder = ImmutableArray.CreateBuilder<SymbolDescriptor>();
        foreach (var symbol in all.Where(s => s.ParentUri.ToString() == moduleKey))
        {
            builder.Add(Describe(symbol, KindOf(symbol), childrenByParent[symbol.Uri.ToString()]));
        }
        return builder.ToImmutable();
    }

    private static SymbolDescriptor Describe(Symbol symbol, SymbolDescriptorKind kind, IEnumerable<Symbol> children)
    {
        var accessible = symbol as AccessibleTypedSymbol;

        return new SymbolDescriptor
        {
            Name = symbol.Name,
            Kind = kind,
            AccessModifier = accessible?.AccessModifier ?? RDCore.SDK.Model.AccessModifier.Implicit,
            Scope = symbol.ScopeKind,
            DeclaredTypeName = DeclaredTypeNameOf(accessible),
            Range = accessible?.Range ?? default,
            SelectionRange = accessible?.SelectionRange ?? default,
            Definitions = DefinitionsOf(symbol),
            Parameters = ParametersOf(symbol),
            Members = [.. children.Select(child => Describe(child, KindOf(child), []))],
            External = ExternalOf(symbol),
        };
    }

    // carried only for a multi-branch symbol; the common single-declaration descriptor stays lean and
    // consumers read Range/SelectionRange.
    private static ImmutableArray<DefinitionDescriptor> DefinitionsOf(Symbol symbol)
        => symbol is BoundSymbol { Definitions.IsDefaultOrEmpty: false } bound
            ? [.. bound.Definitions.Select(definition => new DefinitionDescriptor
            {
                Range = definition.Range,
                SelectionRange = definition.SelectionRange,
                State = definition.State,
            })]
            : [];

    private static SymbolDescriptorKind KindOf(Symbol symbol) => symbol switch
    {
        // most specific first: externals subclass Function/Procedure, and Property Let/Set subclass Procedure.
        VBExternalFunctionMemberSymbol => SymbolDescriptorKind.ExternalFunction,
        VBExternalSubMemberSymbol => SymbolDescriptorKind.ExternalProcedure,
        VBPropertyGetMemberSymbol => SymbolDescriptorKind.PropertyGet,
        VBPropertyLetMemberSymbol => SymbolDescriptorKind.PropertyLet,
        VBPropertySetMemberSymbol => SymbolDescriptorKind.PropertySet,
        VBFunctionMemberSymbol => SymbolDescriptorKind.Function,
        VBProcedureMemberSymbol => SymbolDescriptorKind.Procedure,
        VBEventMemberSymbol => SymbolDescriptorKind.Event,
        VBUserDefinedTypeMemberSymbol => SymbolDescriptorKind.UserDefinedType,
        VBEnumMemberSymbol => SymbolDescriptorKind.Enum,
        VBEnumConstMemberSymbol => SymbolDescriptorKind.EnumMember,
        VBConstantMemberSymbol => SymbolDescriptorKind.ModuleConstant,
        VBUserDefinedTypeFieldSymbol => SymbolDescriptorKind.UserDefinedTypeField,
        VBModuleFieldVariableMemberSymbol => SymbolDescriptorKind.ModuleField,
        _ => throw new NotSupportedException($"'{symbol.GetType().Name}' has no mapped {nameof(SymbolDescriptorKind)}."),
    };

    private static string? DeclaredTypeNameOf(AccessibleTypedSymbol? symbol)
        => symbol is null || symbol.ResolvedType is VBUnknownType or VBVoidType
            ? null
            : symbol.ResolvedType.Name;

    private static ImmutableArray<ParameterDescriptor> ParametersOf(Symbol symbol)
    {
        var parameters = symbol switch
        {
            VBReturningMemberSymbol returning => returning.Parameters,
            VBProcedureMemberSymbol procedure => procedure.Parameters,
            VBEventMemberSymbol @event => @event.Parameters,
            _ => [],
        };
        if (parameters.IsDefaultOrEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<ParameterDescriptor>(parameters.Length);
        foreach (var parameter in parameters)
        {
            builder.Add(new ParameterDescriptor
            {
                Name = parameter.Name,
                ParameterKind = parameter.ParameterKind,
                IsOptional = parameter.IsOptional,
                IsParamArray = parameter is ParamArrayParameterSymbol,
                DeclaredTypeName = parameter.ResolvedType is VBUnknownType or VBVoidType ? null : parameter.ResolvedType.Name,
                Range = parameter.Range,
            });
        }
        return builder.ToImmutable();
    }

    private static ExternalDescriptor? ExternalOf(Symbol symbol) => symbol switch
    {
        VBExternalFunctionMemberSymbol external => new()
        {
            IsPtrSafe = external.IsPtrSafe,
            Library = external.Lib,
            Alias = external.Alias,
        },
        VBExternalSubMemberSymbol external => new()
        {
            IsPtrSafe = external.IsPtrSafe,
            Library = external.Lib,
            Alias = external.Alias,
        },
        _ => null,
    };
}
