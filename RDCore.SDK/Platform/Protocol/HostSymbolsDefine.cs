using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Platform.Protocol;

/// <summary>
/// Request for <c>rdcore/host/symbols/define</c>: the language server sends the environment host the
/// member symbols one workspace module declares, as descriptors. The host reconstructs a runtime
/// <see cref="Symbol"/> from each, resolves its declared type name against its own session, and
/// defines it in the session symbol table.
/// </summary>
/// <remarks>
/// Module symbols are <em>not</em> carried here — the host already has them from the <c>.rdproj</c>.
/// The descriptors are the transport form of what the language server's <c>SyntaxTreeSymbolProvider</c>
/// produces, so both live and dead conditional-compilation branches may be present; the host defines
/// what it is given and leaves branch selection to a later pass.
/// </remarks>
[Method(RDCorePlatformProtocol.DefineSymbols, Direction.ClientToServer)]
public record class DefineSymbolsParams : IRequest, IRequest<DefineSymbolsResult>
{
    /// <summary>
    /// The workspace root the module and its symbols are addressed under. Matches the root the host
    /// composed its session with, so reconstructed symbol <c>Uri</c>s line up with the module symbol.
    /// </summary>
    public Uri? WorkspaceRoot { get; init; }

    /// <summary>
    /// The <c>Uri</c> of the module these symbols belong to. Informational — the host re-derives the
    /// parent module symbol from <see cref="WorkspaceRoot"/> and <see cref="ModuleName"/>.
    /// </summary>
    public Uri? ModuleUri { get; init; }

    /// <summary>
    /// The module's name. The member symbols attach under the module symbol of this name in the session.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// The member symbol descriptors, in declaration order.
    /// </summary>
    public ImmutableArray<SymbolDescriptor> Symbols { get; init; } = [];
}

/// <summary>
/// Result of an <c>rdcore/host/symbols/define</c> request.
/// </summary>
public record class DefineSymbolsResult
{
    /// <summary>
    /// The number of symbols added to the session symbol table.
    /// </summary>
    public int Defined { get; init; }

    /// <summary>
    /// Names that were already defined in the target scope and were therefore skipped.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>
    /// Distinct declared type names the host could not resolve. The owning symbols were still
    /// defined, with <c>VBUnknownType</c>; a later resolver pass can bind them.
    /// </summary>
    public IReadOnlyList<string> UnresolvedTypeNames { get; init; } = [];
}

/// <summary>
/// A transport-friendly projection of one declared member <see cref="Symbol"/>. Declared types
/// travel as names, not resolved <c>VBType</c>s — the environment host is the resolution authority.
/// </summary>
public record class SymbolDescriptor
{
    /// <summary>
    /// The member's identifier name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Which kind of member symbol to reconstruct.
    /// </summary>
    public SymbolDescriptorKind Kind { get; init; }

    /// <summary>
    /// The declared access modifier; <see cref="AccessModifier.Implicit"/> when none was written.
    /// </summary>
    public AccessModifier AccessModifier { get; init; } = AccessModifier.Implicit;

    /// <summary>
    /// The allocation scope the symbol is defined in.
    /// </summary>
    public ScopeKind Scope { get; init; } = ScopeKind.Instance;

    /// <summary>
    /// The declared type's name — from an <c>As</c> clause or a type-declaration character — or
    /// <c>null</c> when there is none, or the type reference is not a simple name (a qualified name
    /// or an array definition needs a later semantic pass).
    /// </summary>
    public string? DeclaredTypeName { get; init; }

    /// <summary>
    /// The source span of the whole declaration.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The source span to select when navigating to the symbol (typically its name token).
    /// </summary>
    public SourceRange SelectionRange { get; init; }

    /// <summary>
    /// Parameters, for the procedure, function, property and event kinds.
    /// </summary>
    public ImmutableArray<ParameterDescriptor> Parameters { get; init; } = [];

    /// <summary>
    /// Members parented to this descriptor rather than the module: <c>Enum</c> constants and
    /// user-defined-<c>Type</c> fields.
    /// </summary>
    public ImmutableArray<SymbolDescriptor> Members { get; init; } = [];

    /// <summary>
    /// <c>Declare</c>-statement metadata; <c>null</c> unless <see cref="Kind"/> is
    /// <see cref="SymbolDescriptorKind.ExternalProcedure"/> or <see cref="SymbolDescriptorKind.ExternalFunction"/>.
    /// </summary>
    public ExternalDescriptor? External { get; init; }
}

/// <summary>
/// A transport-friendly projection of a <c>VBParameterSymbol</c>.
/// </summary>
public record class ParameterDescriptor
{
    /// <summary>
    /// The parameter's identifier name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// How an argument is passed to the parameter.
    /// </summary>
    public ParameterKind ParameterKind { get; init; } = ParameterKind.ImplicitByRef;

    /// <summary>
    /// Whether the parameter carries an <c>Optional</c> token.
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>
    /// Whether the parameter is a <c>ParamArray</c>.
    /// </summary>
    public bool IsParamArray { get; init; }

    /// <summary>
    /// The declared type's name, or <c>null</c> — resolved host-side like a member's.
    /// </summary>
    public string? DeclaredTypeName { get; init; }

    /// <summary>
    /// The source span of the parameter declaration.
    /// </summary>
    public SourceRange Range { get; init; }
}

/// <summary>
/// The <c>Declare</c>-statement metadata of an external member.
/// </summary>
public record class ExternalDescriptor
{
    /// <summary>
    /// Whether the declaration carries the <c>PtrSafe</c> token.
    /// </summary>
    public bool IsPtrSafe { get; init; }

    /// <summary>
    /// The library name from the <c>Lib "…"</c> clause.
    /// </summary>
    public string Library { get; init; } = string.Empty;

    /// <summary>
    /// The exported name from the <c>Alias "…"</c> clause, or <c>null</c>.
    /// </summary>
    public string? Alias { get; init; }
}

/// <summary>
/// The member-symbol kinds a <see cref="SymbolDescriptor"/> can carry. Mirrors the subset of
/// <see cref="MemberKind"/> the language server's AST symbol provider emits, split so <c>Property</c>
/// accessors and <c>Enum</c>/<c>Type</c> members each map to one runtime symbol type.
/// </summary>
public enum SymbolDescriptorKind
{
    /// <summary>
    /// A <c>Sub</c> procedure.
    /// </summary>
    Procedure,

    /// <summary>
    /// A <c>Function</c> procedure.
    /// </summary>
    Function,

    /// <summary>
    /// A <c>Property Get</c> accessor.
    /// </summary>
    PropertyGet,

    /// <summary>
    /// A <c>Property Let</c> accessor.
    /// </summary>
    PropertyLet,

    /// <summary>
    /// A <c>Property Set</c> accessor.
    /// </summary>
    PropertySet,

    /// <summary>
    /// A <c>Declare Sub</c>.
    /// </summary>
    ExternalProcedure,

    /// <summary>
    /// A <c>Declare Function</c>.
    /// </summary>
    ExternalFunction,

    /// <summary>
    /// An <c>Event</c> declaration.
    /// </summary>
    Event,

    /// <summary>
    /// A <c>Type … End Type</c> declaration.
    /// </summary>
    UserDefinedType,

    /// <summary>
    /// A field of a user-defined <c>Type</c>.
    /// </summary>
    UserDefinedTypeField,

    /// <summary>
    /// An <c>Enum … End Enum</c> declaration.
    /// </summary>
    Enum,

    /// <summary>
    /// A member of an <c>Enum</c>.
    /// </summary>
    EnumMember,

    /// <summary>
    /// A module-scoped variable (<c>Dim</c>/<c>Private</c>/<c>Public</c> field).
    /// </summary>
    ModuleField,

    /// <summary>
    /// A module-scoped <c>Const</c>.
    /// </summary>
    ModuleConstant,
}
