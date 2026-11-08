using MediatR;
using OmniSharp.Extensions.JsonRpc;
using RDCore.SDK.Client;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Platform.Protocol;
using System.Collections.Immutable;

namespace RDCore.SDK.Platform.Components;

/// <summary>
/// Request for <c>rdcore/host/symbols/define</c>: the language server sends the environment host the
/// member symbols one workspace module declares, as descriptors. The host reconstructs a runtime
/// <see cref="Symbol"/> from each, resolves its declared type name against its own session, and
/// defines it in the session symbol table.
/// </summary>
/// <remarks>
/// Module symbols are <em>not</em> carried here: the host already has them from the <c>.rdproj</c>.
/// The descriptors are the transport form of what the language server's <c>SyntaxTreeSymbolProvider</c>
/// produces. A name declared in several conditional-compilation branches arrives as one descriptor
/// carrying every branch in <see cref="SymbolDescriptor.Definitions"/>; the host defines the single
/// symbol and leaves live/dead branch selection to a later pass.
/// <para>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </para>
/// </remarks>
[Method(RDCoreComponentProtocol.DefineSymbols, Direction.ClientToServer)]
public record class DefineSymbolsParams : IRequest, IRequest<DefineSymbolsResult>
{
    /// <summary>
    /// The workspace root the module and its symbols are addressed under. Matches the root the host
    /// composed its session with, so reconstructed symbol <c>Uri</c>s line up with the module symbol.
    /// </summary>
    public Uri? WorkspaceRoot { get; init; }

    /// <summary>
    /// The <c>Uri</c> of the module these symbols belong to. Informational: the host re-derives the
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
    /// <remarks>
    /// Not on the wire as such: a descriptor carries expressions (<see cref="ConstantDescriptor.Value"/>, <see cref="ParameterDescriptor.DefaultValue"/>,
    /// <see cref="ArrayBoundDescriptor"/>), which the transport's serializer cannot write - and a request it cannot write is never sent, nor ever answered.
    /// They travel as <see cref="SymbolsJson"/>.
    /// </remarks>
    [Newtonsoft.Json.JsonIgnore]
    public ImmutableArray<SymbolDescriptor> Symbols { get; init; } = [];

    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of <see cref="Symbols"/> (see <see cref="PlatformJson"/>): the form they take on the wire.
    /// </summary>
    /// <remarks>
    /// An AST node is polymorphic, and the literal one holds a value whose type holds a value of that type; <c>Newtonsoft.Json</c> refuses the first and loops on
    /// the second. Setting it sets <see cref="Symbols"/>.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SymbolsJson
    {
        get => PlatformJson.Serialize(Symbols);
        init => Symbols = value.Length == 0 ? [] : PlatformJson.Deserialize<ImmutableArray<SymbolDescriptor>>(value);
    }

    /// <summary>
    /// The module's own <c>Option</c> directives (<strong>MS-VBAL §5.2.1</strong>), which the host
    /// applies to its module symbol.
    /// </summary>
    /// <remarks>
    /// These are run-time dials, not only static ones: <c>Option Compare</c> decides how the relational
    /// operators compare <c>String</c> values in the module's code, and <c>Option Base</c> decides what
    /// an array dimension declared without a lower bound means. Both are read off the activation's own
    /// call-stack frame, which gets them from the module symbol the procedure is declared in.
    /// </remarks>
    public ModuleDirectives Directives { get; init; } = ModuleDirectives.None;

    /// <summary>
    /// The names of the interfaces the module's <c>Implements</c> directives name
    /// (<strong>MS-VBAL §5.2.4.2</strong>), as written and in source order; empty for a module that has none, and for one that
    /// is not a class module.
    /// </summary>
    /// <remarks>
    /// The host composes the class from them once its members are defined
    /// (<see cref="RDCore.SDK.Runtime.Abstract.Execution.ISessionSymbols.TryComposeClassModule"/>), resolving each to the class it
    /// names among the class modules it has: an object of the class is then an object that implements the interface, which is
    /// what a call through a variable declared as the interface is dispatched on (<strong>§5.3.1.9</strong>), and what a
    /// <c>TypeOf ... Is</c> and a <c>Set</c> to such a variable are decided by.
    /// </remarks>
    public ImmutableArray<string> ImplementedInterfaceNames { get; init; } = [];

    /// <summary>
    /// Where each of <see cref="ImplementedInterfaceNames"/> is written in the module: the range of its <c>Implements</c> directive,
    /// one for each name and in the same order, or empty when they are not known.
    /// </summary>
    public ImmutableArray<SourceRange> ImplementedInterfaceRanges { get; init; } = [];

    /// <summary>
    /// The <see cref="System.Text.Json"/> representation of the module's <see cref="RDCore.SDK.Model.AST.ModuleParseResult"/>
    /// (see <see cref="PlatformJson"/>), or empty when the module is defined for its symbols alone.
    /// </summary>
    /// <remarks>
    /// What gives the module's procedures code. A module the host is asked to run carries its parse result with that request
    /// and is loaded then; a module of the workspace is never run as such, but its procedures are called by the ones that
    /// are, and its class members by the objects that are made of it. The AST is polymorphic, which is why it rides a string
    /// and not the transport's own serializer.
    /// </remarks>
    public string ParseResultJson { get; init; } = string.Empty;

    /// <summary>
    /// The <see cref="PlatformJson"/> representation of the module's conditional-compilation directives
    /// (<see cref="RDCore.SDK.Model.AST.ModuleParseResult.PrecompilerTrivia"/>), or empty when the module has none or the caller does not send them.
    /// </summary>
    /// <remarks>
    /// What lets the host define the declarations of the live <c>#If</c> branches only (<strong>MS-VBAL §3.4</strong>): it evaluates the directives with the
    /// session's constants, and of a declaration written once per branch - a procedure, a <c>Declare</c>, a field of a <c>Type</c> - it defines the one of
    /// the live branch; one written only in dead branches is not defined. Without them, every declaration is defined as the first branch writes it.
    /// </remarks>
    public string PrecompilerTriviaJson { get; init; } = string.Empty;

    /// <summary>
    /// The <see cref="PrecompilerTriviaJson"/> of a module parsed: empty when it has no directive.
    /// </summary>
    /// <param name="parseResult">The module's parse.</param>
    public static string PrecompilerTriviaJsonOf(RDCore.SDK.Model.AST.ModuleParseResult parseResult)
        => parseResult.PrecompilerTrivia.IsDefaultOrEmpty
            ? string.Empty
            : TriviaJson.GetValue(parseResult, parse => PlatformJson.Serialize(parse.PrecompilerTrivia));

    // a parse is sent in every phase of a workspace's definition: its directives are serialized once.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RDCore.SDK.Model.AST.ModuleParseResult, string> TriviaJson = [];

    /// <summary>
    /// Whether a descriptor replaces an already-defined symbol of the same identity rather than being
    /// skipped.
    /// </summary>
    /// <remarks>
    /// The one-shot workspace sync defines each module once, and first-definition-wins is what
    /// collapses a name declared in several conditional-compilation branches. A module the user is
    /// editing is the other case: it is defined again every time it is run, and the newest definition
    /// is the one that is true: skipping it would leave the session running yesterday's symbols, with
    /// whatever locals and declared types they had.
    /// </remarks>
    public bool Replace { get; init; }

    /// <summary>
    /// Whether the module's symbols were defined by an earlier request, and this one is for its code alone: the host defines nothing, and loads the module's
    /// procedures from <see cref="ParseResultJson"/>.
    /// </summary>
    /// <remarks>
    /// A module's code is checked against what the whole workspace declares (<strong>RD-VBAL §5.0.1</strong>), and the host is told of the modules one at a
    /// time: a module that names one that comes after it cannot be checked until that one is defined. The language server defines every module first, and
    /// then sends the code of each. See <see cref="DeclareTypesOnly"/> for the three phases.
    /// </remarks>
    public bool CodeOnly { get; init; }

    /// <summary>
    /// Whether this request declares the module's types alone - its user-defined types with their fields, and its enumerations with their members - and
    /// nothing else is defined, composed or loaded.
    /// </summary>
    /// <remarks>
    /// A workspace is defined in three phases, each for every module before the next: its types are declared (this), then its declarations are bound
    /// (a request that is neither this nor <see cref="CodeOnly"/>), then its code is loaded (<see cref="CodeOnly"/>). A declaration is bound to a type that
    /// exists, whichever module declares it and in whichever order a project lists its modules: a class module exists as soon as the project is composed,
    /// and a user-defined type or an enumeration once this phase has declared it. A request that binds a module's declarations declares its types first, so
    /// that a module defined on its own (the shell's program) is right too; the types the declare phase gave the session are there already.
    /// </remarks>
    public bool DeclareTypesOnly { get; init; }

    /// <summary>
    /// Whether the module is the client's own, of the runtime session and not of the project (<see cref="SessionExecuteParams.SessionModule"/>): when the session
    /// has no module of <see cref="ModuleName"/>, the host defines it as a standard module at <see cref="WorkspaceRoot"/>, outside the project, before it defines the module's members,
    /// unless it is named after the project or a library the session references. Without it, the members of a module the session does not have are defined under
    /// no module, and the module is not found.
    /// </summary>
    public bool SessionModule { get; init; }
}

/// <summary>
/// Result of an <c>rdcore/host/symbols/define</c> request.
/// </summary>
/// <remarks>
/// Part of the component protocol (<see cref="RDCoreComponentProtocol"/>), outside the SDK's compatibility promise.
/// </remarks>
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
    /// The descriptions of the errors found lowering the module's procedures when <see cref="DefineSymbolsParams.ParseResultJson"/>
    /// was given. The module's symbols are defined all the same; its procedures have no code, and what had before is kept.
    /// </summary>
    public IReadOnlyList<string> CodeErrors { get; init; } = [];

    /// <summary>
    /// The number of already-defined symbols that were replaced, when
    /// <see cref="DefineSymbolsParams.Replace"/> was set.
    /// </summary>
    public int Replaced { get; init; }

    /// <summary>
    /// The number of duplicate descriptors that were collapsed into an already-reconstructed symbol
    /// rather than defined separately: a member declared in more than one conditional-compilation
    /// branch reaches the session as one symbol with multiple <see cref="SymbolDescriptor.Definitions"/>.
    /// </summary>
    public int MergedDefinitions { get; init; }
}
