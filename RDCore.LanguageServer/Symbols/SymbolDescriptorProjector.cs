using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
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
            if (KindOf(symbol) is not { } kind)
            {
                // a symbol with no mapped top-level kind — e.g. a procedure-local wrongly parented to
                // the module itself, which a nameless-member Uri collision can produce — must not crash
                // the whole module's projection over one stray symbol.
                continue;
            }
            builder.Add(Describe(symbol, kind, childrenByParent[symbol.Uri.ToString()]));
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
            Array = ArrayOf(accessible?.ResolvedType, symbol),
            Range = accessible?.Range ?? default,
            SelectionRange = accessible?.SelectionRange ?? default,
            Definitions = DefinitionsOf(symbol),
            Parameters = ParametersOf(symbol),
            Locals = LocalsOf(symbol, children),
            Constants = ConstantsOf(symbol, children),
            IsWithEvents = symbol.GetProperty(SymbolProperties.WithEvents),
            IsAutoInstantiated = symbol.GetProperty(SymbolProperties.AutoInstantiated),
            UserMemId = symbol.TryGetProperty(SymbolProperties.UserMemId, out var userMemId) ? userMemId : null,
            Members = [.. children.Where(IsNestableMember).Select(child => Describe(child, KindOf(child)!.Value, []))],
            External = ExternalOf(symbol),
        };
    }

    // only Enum constants and UDT fields are meant to nest under their owner (see this class's own
    // remarks); a procedure's locals (VBLocalVariableSymbol/VBLocalConstantSymbol) share the same
    // childrenByParent lookup by virtue of their ParentUri, but were never meant to project into the
    // descriptor tree — KindOf has no arm for them, and none is wanted here.
    private static bool IsNestableMember(Symbol symbol) => symbol is VBEnumConstMemberSymbol or VBUserDefinedTypeFieldSymbol;

    // carried only for a multi-branch symbol; the common single-declaration descriptor stays lean and
    // consumers read Range/SelectionRange.
    private static ImmutableArray<DefinitionDescriptor> DefinitionsOf(Symbol symbol)
        => symbol is WorkspaceSymbol { Definitions.IsDefaultOrEmpty: false } bound
            ? [.. bound.Definitions.Select(definition => new DefinitionDescriptor
            {
                Range = definition.Range,
                SelectionRange = definition.SelectionRange,
                State = definition.State,
            })]
            : [];

    // null for a symbol kind this descriptor tree has no shape for (a procedure-local, for one) —
    // callers must treat that as "skip this symbol", not an error: a local can legitimately reach here
    // if it's ever wrongly parented to the module itself rather than to its owning procedure, and one
    // stray symbol must not crash the whole module's projection (adversarial review PRs #208-224,
    // item 7 — the top-level call site here wasn't covered by #209's guard on the child path below).
    private static SymbolDescriptorKind? KindOf(Symbol symbol) => symbol switch
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
        _ => null,
    };

    private static string? DeclaredTypeNameOf(AccessibleTypedSymbol? symbol)
        => symbol is null ? null : TypeNameOf(symbol.ResolvedType);

    // the name a declared type travels as: the type's own, and for an array its element's - the kind of array and how it is
    // sized are the array descriptor's. A type that is not known has no name to carry, and one that did not resolve carries the
    // name it was written with, which the host resolves again once it has the whole workspace.
    private static string? TypeNameOf(VBType? type) => type switch
    {
        VBUnresolvedType unresolved => unresolved.DeclaredName,
        null or VBUnknownType or VBVoidType => null,
        VBArrayType array => array.ItemType is VBVoidType ? null : TypeNameOf(array.ItemType),
        _ => type.Name,
    };

    // what makes the name above an array of it: fixed-size or resizable, and for a fixed-size one the bounds the symbol was
    // declared with (SymbolProperties.ArrayBounds).
    private static ArrayDescriptor? ArrayOf(VBType? type, Symbol symbol)
    {
        if (type is not VBArrayType array)
        {
            return null;
        }

        var bounds = symbol.TryGetProperty(SymbolProperties.ArrayBounds, out var declared) && !declared.IsDefault
            ? declared
            : [];
        return new ArrayDescriptor
        {
            IsFixedSize = array is VBFixedSizeArrayType,
            Bounds = [.. bounds.Select(bound => new ArrayBoundDescriptor { Lower = bound.LowerExpression, Upper = bound.UpperExpression })],
        };
    }

    // a procedure's Dim/Static variables. Parameters are not among them: they are their own descriptor
    // array, and VBParameterSymbol derives from VBLocalVariableSymbol, so they would otherwise be
    // projected twice and allocated twice on every activation.
    private static ImmutableArray<LocalDescriptor> LocalsOf(Symbol symbol, IEnumerable<Symbol> children)
    {
        var declared = symbol switch
        {
            VBReturningMemberSymbol returning => returning.Locals,
            VBProcedureMemberSymbol procedure => procedure.Locals,
            _ => ImmutableArray<BoundTypedSymbol>.Empty,
        };

        // a local reaches the projector two ways: on its own procedure's Locals, and as a child of it
        // in the flat symbol stream (by ParentUri). Which of the two a given provider populates is its
        // own business, so read both and let the name settle a duplicate.
        var locals = declared.OfType<VBLocalVariableSymbol>()
            .Concat(children.OfType<VBLocalVariableSymbol>())
            .Where(local => local is not VBParameterSymbol)
            .DistinctBy(local => local.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (locals.Length == 0)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<LocalDescriptor>(locals.Length);
        foreach (var local in locals)
        {
            builder.Add(new LocalDescriptor
            {
                Name = local.Name,
                DeclaredTypeName = TypeNameOf(local.ResolvedType),
                Array = ArrayOf(local.ResolvedType, local),
                IsStatic = local.IsStatic,
                IsAutoInstantiated = local.GetProperty(SymbolProperties.AutoInstantiated),
                DeclaredBy = local.DeclaredBy,
                Range = local.Range,
                SelectionRange = local.SelectionRange,
            });
        }
        return builder.ToImmutable();
    }

    // a procedure's Const declarations, and a module-level constant's own value. Neither has storage,
    // so neither belongs on Locals: what the host needs is the declaration's expression, to substitute
    // at each use site. A local Const never travelled at all before this, so a procedure that declared
    // one could not resolve its own name.
    private static ImmutableArray<ConstantDescriptor> ConstantsOf(Symbol symbol, IEnumerable<Symbol> children)
    {
        // a module-level constant is the descriptor, not a child of one; a local constant reaches here
        // the same two ways a local variable does (see LocalsOf).
        var declared = symbol switch
        {
            VBConstantMemberSymbol moduleConstant => [moduleConstant],
            VBReturningMemberSymbol returning => returning.Locals.OfType<Symbol>(),
            VBProcedureMemberSymbol procedure => procedure.Locals.OfType<Symbol>(),
            _ => [],
        };

        var constants = declared.Concat(symbol is VBConstantMemberSymbol ? [] : children)
            .Where(constant => constant is VBConstantMemberSymbol or VBLocalConstantSymbol)
            .DistinctBy(constant => constant.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (constants.Length == 0)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<ConstantDescriptor>(constants.Length);
        foreach (var constant in constants)
        {
            var (type, value) = constant switch
            {
                VBConstantMemberSymbol moduleConstant => (moduleConstant.ResolvedType, moduleConstant.Value),
                VBLocalConstantSymbol local => (local.ResolvedType, local.Value),
                _ => (VBUnknownType.TypeInfo, null),
            };

            builder.Add(new ConstantDescriptor
            {
                Name = constant.Name,
                DeclaredTypeName = type is VBUnknownType or VBVoidType ? null : type.Name,
                Value = value,
                Range = (constant as WorkspaceSymbol)?.Range ?? default,
                SelectionRange = (constant as WorkspaceSymbol)?.SelectionRange ?? default,
            });
        }
        return builder.ToImmutable();
    }

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
                DefaultValue = parameter.DefaultValue,
                // a ParamArray is its own kind of parameter, and what it holds is the host's to make.
                DeclaredTypeName = parameter is ParamArrayParameterSymbol
                    ? parameter.ResolvedType is VBUnknownType or VBVoidType ? null : parameter.ResolvedType.Name
                    : TypeNameOf(parameter.ResolvedType),
                Array = parameter is ParamArrayParameterSymbol ? null : ArrayOf(parameter.ResolvedType, parameter),
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
