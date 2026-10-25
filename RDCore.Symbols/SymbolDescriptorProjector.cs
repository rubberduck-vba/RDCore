using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;

namespace RDCore.Symbols;

/// <summary>
/// Projects the flat <see cref="Symbol"/> stream from <see cref="SyntaxTreeSymbolProvider"/> onto the
/// <see cref="SymbolDescriptor"/> tree carried over <c>rdcore/host/symbols/define</c>: top-level
/// members parent to the module, and the members the provider yields separately (<c>Enum</c>
/// constants, user-defined-<c>Type</c> fields) are nested under their owner.
/// </summary>
/// <remarks>
/// A declared type reaches the descriptor as a name (<see cref="SymbolDescriptor.DeclaredTypeName"/>), which the host resolves again in its
/// own session (TypeNameOf): an intrinsic type, a class of the workspace and a type of the standard library (VBA) by their own name, a type of
/// any other referenced library with the library's name, a user-defined type or an Enum of another module of the workspace with that module's
/// name, and a name that did not resolve as it was written. A type that is not known has no name to carry, and the descriptor's is <c>null</c>.
/// </remarks>
public static class SymbolDescriptorProjector
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
            builder.Add(Describe(symbol, kind, childrenByParent[symbol.Uri.ToString()], moduleKey));
        }
        return builder.ToImmutable();
    }

    private static SymbolDescriptor Describe(Symbol symbol, SymbolDescriptorKind kind, IEnumerable<Symbol> children, string moduleKey)
    {
        var accessible = symbol as AccessibleTypedSymbol;

        return new SymbolDescriptor
        {
            Name = symbol.Name,
            Kind = kind,
            AccessModifier = accessible?.AccessModifier ?? RDCore.SDK.Model.AccessModifier.Implicit,
            Scope = symbol.ScopeKind,
            DeclaredTypeName = DeclaredTypeNameOf(accessible, moduleKey),
            Array = ArrayOf(accessible?.ResolvedType, symbol),
            FixedLength = FixedLengthOf(accessible?.ResolvedType),
            Range = accessible?.Range ?? default,
            SelectionRange = accessible?.SelectionRange ?? default,
            Definitions = DefinitionsOf(symbol),
            Parameters = ParametersOf(symbol, moduleKey),
            Locals = LocalsOf(symbol, children, moduleKey),
            Constants = ConstantsOf(symbol, children, moduleKey),
            IsWithEvents = symbol.GetProperty(SymbolProperties.WithEvents),
            IsDesignerControl = symbol.GetProperty(SymbolProperties.DesignerControl),
            IsAutoInstantiated = symbol.GetProperty(SymbolProperties.AutoInstantiated),
            UserMemId = symbol.TryGetProperty(SymbolProperties.UserMemId, out var userMemId) ? userMemId : null,
            Members = [.. children.Where(IsNestableMember).Select(child => Describe(child, KindOf(child)!.Value, [], moduleKey))],
            EnumValue = (symbol as VBEnumConstMemberSymbol)?.DeclaredValue,
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

    private static string? DeclaredTypeNameOf(AccessibleTypedSymbol? symbol, string moduleKey)
        => symbol is null ? null : TypeNameOf(symbol.ResolvedType, moduleKey);

    // the name a declared type travels as: the type's own, and for an array its element's - the kind of array and how it is
    // sized are the array descriptor's. A type that is not known has no name to carry, and one that did not resolve carries the
    // name it was written with, which the host resolves again once it has the whole workspace. The module being projected is
    // moduleKey (see OfAnotherModule).
    private static string? TypeNameOf(VBType? type, string moduleKey) => type switch
    {
        VBUnresolvedType unresolved => unresolved.DeclaredName,
        null or VBUnknownType or VBVoidType => null,
        VBArrayType array => array.ItemType is VBVoidType ? null : TypeNameOf(array.ItemType, moduleKey),
        _ when OfAnotherModule(type, moduleKey) is { } qualified => qualified,
        // a name means what the references make it mean, which is not the same in every process that reads it: two libraries can declare a class of
        // one name, and the one the declaration named is the one that travels (MS-VBAL 5.6.12).
        VBClassType { Symbol: { } symbol } => Qualified(symbol, type.Name),
        VBEnumType { Symbol: { } symbol } => Qualified(symbol, type.Name),
        _ => type.Name,
    };

    // the length a fixed-length string travels with, the name String having no room for it - an array's, of its elements.
    private static int? FixedLengthOf(VBType? type) => type switch
    {
        VBFixedStringType fixedString => fixedString.Length,
        VBArrayType array => FixedLengthOf(array.ItemType),
        _ => null,
    };

    // a type of a referenced library is named for its library, and the standard library's by its own. A class of the workspace travels by its own
    // name too; a user-defined type or an Enum of another module of the workspace is OfAnotherModule's.
    private static string Qualified(Symbol symbol, string name)
        => symbol.GetProperty(SymbolProperties.Library) is { Length: > 0 } library && library != StdLibSymbolProvider.LibraryName ? $"{library}.{name}" : name;

    // a user-defined type or an Enum that another module of the workspace declares travels with the name of that module, and the host resolves it within
    // that module (MS-VBAL 5.6.12). By its bare name the host would bind it again, from the module being defined, and get whatever ranks first there under
    // that name: a procedural module of the project, a library's class, or another module's type of that name. A type of the module being projected keeps
    // its bare name, which the host binds to the module's own type first, a Private one included.
    private static string? OfAnotherModule(VBType type, string moduleKey) => type switch
    {
        VBUserDefinedType { Symbol: { } symbol } => OfAnotherModule(symbol, moduleKey),
        VBEnumType { Symbol: { } symbol } => OfAnotherModule(symbol, moduleKey),
        _ => null,
    };

    // a symbol of the workspace carries no library, and the module that declares a type is its parent: the fragment of the workspace's root.
    private static string? OfAnotherModule(Symbol symbol, string moduleKey)
        => symbol.GetProperty(SymbolProperties.Library) is null
            && symbol.ParentUri.ToString() != moduleKey
            && Uri.UnescapeDataString(symbol.ParentUri.Fragment.TrimStart('#')) is { Length: > 0 } module
                ? $"{module}.{symbol.Name}"
                : null;

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
    private static ImmutableArray<LocalDescriptor> LocalsOf(Symbol symbol, IEnumerable<Symbol> children, string moduleKey)
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
                DeclaredTypeName = TypeNameOf(local.ResolvedType, moduleKey),
                Array = ArrayOf(local.ResolvedType, local),
                FixedLength = FixedLengthOf(local.ResolvedType),
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
    private static ImmutableArray<ConstantDescriptor> ConstantsOf(Symbol symbol, IEnumerable<Symbol> children, string moduleKey)
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
                // the name travels as a variable's does: as it was written when it did not resolve, so that the host can say it is not a type, and a
                // referenced library's type with the library's name, as another module's Enum has its module's, so that the host resolves that type and not
                // what the bare name binds first.
                DeclaredTypeName = TypeNameOf(type, moduleKey),
                Value = value,
                Range = (constant as WorkspaceSymbol)?.Range ?? default,
                SelectionRange = (constant as WorkspaceSymbol)?.SelectionRange ?? default,
            });
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<ParameterDescriptor> ParametersOf(Symbol symbol, string moduleKey)
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
                    : TypeNameOf(parameter.ResolvedType, moduleKey),
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
