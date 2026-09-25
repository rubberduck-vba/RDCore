using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
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
/// produces. A name declared in several conditional-compilation branches arrives as one descriptor
/// carrying every branch in <see cref="SymbolDescriptor.Definitions"/>; the host defines the single
/// symbol and leaves live/dead branch selection to a later pass.
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

    /// <summary>
    /// Whether a descriptor replaces an already-defined symbol of the same identity rather than being
    /// skipped.
    /// </summary>
    /// <remarks>
    /// The one-shot workspace sync defines each module once, and first-definition-wins is what
    /// collapses a name declared in several conditional-compilation branches. A module the user is
    /// editing is the other case: it is defined again every time it is run, and the newest definition
    /// is the one that is true — skipping it would leave the session running yesterday's symbols, with
    /// whatever locals and declared types they had.
    /// </remarks>
    public bool Replace { get; init; }
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

    /// <summary>
    /// The number of already-defined symbols that were replaced, when
    /// <see cref="DefineSymbolsParams.Replace"/> was set.
    /// </summary>
    public int Replaced { get; init; }

    /// <summary>
    /// The number of duplicate descriptors that were collapsed into an already-reconstructed symbol
    /// rather than defined separately — a member declared in more than one conditional-compilation
    /// branch reaches the session as one symbol with multiple <see cref="SymbolDescriptor.Definitions"/>.
    /// </summary>
    public int MergedDefinitions { get; init; }
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
    /// The source span of the whole declaration — the primary site (the first branch) when the
    /// member has multiple <see cref="Definitions"/>.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The source span to select when navigating to the symbol (typically its name token) — the
    /// primary site when the member has multiple <see cref="Definitions"/>.
    /// </summary>
    public SourceRange SelectionRange { get; init; }

    /// <summary>
    /// The declaration sites of this member, in source order — populated only when the same name is
    /// declared in more than one conditional-compilation branch. Empty for the common
    /// single-declaration case, in which <see cref="Range"/>/<see cref="SelectionRange"/> are the
    /// sole site. A consumer that does not support multi-branch symbols can ignore this and use
    /// <see cref="Range"/>.
    /// </summary>
    public ImmutableArray<DefinitionDescriptor> Definitions { get; init; } = [];

    /// <summary>
    /// Parameters, for the procedure, function, property and event kinds.
    /// </summary>
    public ImmutableArray<ParameterDescriptor> Parameters { get; init; } = [];

    /// <summary>
    /// The procedure-extent variables the member declares — one per <c>Dim</c> or <c>Static</c> — for
    /// the procedure, function and property kinds.
    /// </summary>
    /// <remarks>
    /// A local is not a member of the module and is never resolvable from outside its procedure, so it
    /// rides on its own procedure's descriptor rather than appearing beside it. It has to travel at
    /// all because the procedure symbol is what an invocation allocates frame storage from
    /// (<strong>MS-VBAL §5.4.3</strong> step 4): a procedure reconstructed without its locals has a
    /// body that cannot assign to any of them.
    /// </remarks>
    public ImmutableArray<LocalDescriptor> Locals { get; init; } = [];

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
/// A transport-friendly projection of one <c>SymbolDefinition</c> — a single declaration site of a
/// member declared in more than one conditional-compilation branch.
/// </summary>
public record class DefinitionDescriptor
{
    /// <summary>
    /// The full source span of this declaration site.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The source span to select when navigating to this site (typically its name token).
    /// </summary>
    public SourceRange SelectionRange { get; init; }

    /// <summary>
    /// Whether this site's conditional-compilation branch compiles.
    /// <see cref="DefinitionState.Unknown"/> until a precompiler-evaluation pass resolves it.
    /// </summary>
    public DefinitionState State { get; init; } = DefinitionState.Unknown;
}

/// <summary>
/// A transport-friendly projection of a <c>VBLocalVariableSymbol</c> — one <c>Dim</c> or <c>Static</c>
/// of a procedure.
/// </summary>
public record class LocalDescriptor
{
    /// <summary>
    /// The variable's identifier name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The declared type's name, or <c>null</c> — resolved host-side like a member's.
    /// </summary>
    public string? DeclaredTypeName { get; init; }

    /// <summary>
    /// Whether the declaration carries the <c>Static</c> token (<strong>MS-VBAL §5.4.3.1</strong>):
    /// module-extent storage that outlives one activation, not procedure-extent.
    /// </summary>
    public bool IsStatic { get; init; }

    /// <summary>
    /// How the variable entered the procedure scope — a real declaration, or an implicit one.
    /// </summary>
    public LocalDeclarationKind DeclaredBy { get; init; } = LocalDeclarationKind.Dim;

    /// <summary>
    /// The source span of the declaration.
    /// </summary>
    public SourceRange Range { get; init; }

    /// <summary>
    /// The source span to select when navigating to the variable.
    /// </summary>
    public SourceRange SelectionRange { get; init; }
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
