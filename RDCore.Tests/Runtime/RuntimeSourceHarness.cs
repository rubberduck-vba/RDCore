using NSubstitute;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.Tests.Semantics.Runtime;
using System.IO.Abstractions;
using RDCore.Symbols;

namespace RDCore.Tests.Runtime;

/// <summary>
/// Runs the body of a <c>Sub</c> written as source through the real chain — parse, lower, execute — against
/// a session the caller composed the surroundings of.
/// </summary>
/// <remarks>
/// Statements that do something to the world outside the interpreter (the file statements especially) are
/// only worth testing end-to-end: a hand-built AST proves the semantics run but not that the parser produces
/// the nodes they read, and those two have disagreed before. Shared so that each suite exercising a
/// statement family does it the same way rather than growing its own harness.
/// </remarks>
internal static class RuntimeSourceHarness
{
    // whatever TestUri names them, so that the procedure's Uri is the one the scope tree has a node for.
    private const string ModuleName = "TestModule1";
    private const string ProcedureName = "TestMethod1";

    private sealed class Provider(IEnumerable<Symbol> symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    /// <summary>
    /// The module the body runs in — the scope a variable a test defines has to belong to for the body to
    /// resolve it by name.
    /// </summary>
    public static Uri ModuleUri => TestUri.TestModuleUri();

    /// <summary>
    /// The rest of the surroundings a caller can compose the session of, beyond the symbols the body refers to:
    /// the environment it runs under, the other procedures of its module, and what reaches the members that
    /// are not the workspace's.
    /// </summary>
    /// <remarks>
    /// A record rather than more parameters, so that each suite names only what it needs and a new fact about
    /// the surroundings gets a property here rather than another overload of <see cref="Run(IFileSystem?, IEnumerable{Symbol}, string[])"/>.
    /// </remarks>
    public sealed record class Surroundings
    {
        /// <summary>
        /// The environment the session runs under: 64-bit, the current culture, Windows-1252, and no
        /// <c>Option Compare Database</c>, unless a test says otherwise.
        /// </summary>
        public IRuntimeEnvironmentProfile Environment { get; init; } = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false);

        /// <summary>
        /// The module dials the body runs under - <c>Option Base</c> and the rest, which ride on the executing
        /// frame rather than on anything the body can say. The module symbol carries them too, so a procedure of
        /// <see cref="Procedures"/> runs under the same ones: a callee's frame takes its declaring module's.
        /// </summary>
        public ModuleDirectives Directives { get; init; } = ModuleDirectives.None;

        /// <summary>
        /// Other procedures the body can call, each with its own body. A procedure's symbol has to be parented
        /// to <see cref="ModuleUri"/> for the body to resolve it by name, and its body is parsed and lowered the
        /// way the body under test is — the statements, one per line, with no declaration line of its own. The
        /// declaration pass runs over the body under test only: a procedure here has the locals its symbol
        /// carries, and no others.
        /// </summary>
        public IReadOnlyList<(VBTypeMemberSymbol Symbol, string[] Body)> Procedures { get; init; } = [];

        /// <summary>
        /// Stands something in front of the dispatcher an external call reaches: it is given the pipeline's
        /// real dispatcher, and whatever it returns is what the body's calls go through — a
        /// <see cref="RecordingDispatcher"/>, to see which calls were made and with what.
        /// </summary>
        public Func<IExternalDispatcher, IExternalDispatcher>? Dispatcher { get; init; }

        /// <summary>
        /// What the body reaches outside the platform - the dialogs, the desktop, the settings - or <c>null</c> for
        /// none of it. Ignored by an analysis, which reaches none.
        /// </summary>
        public RDCore.External.ExternalWorld? Outside { get; init; }

        /// <summary>
        /// Told of every conversion and operation the body evaluates, or <c>null</c> to run it unobserved.
        /// </summary>
        public IAnalysisObserver? Observer { get; init; }

        /// <summary>
        /// Whether the body is analyzed rather than run: in a session that shares the declared symbols of the composed one
        /// and holds the run-time state of its own (<see cref="RuntimeSessionComposer.ComposeForAnalysis"/>). The session returned is that one.
        /// </summary>
        public bool Analyze { get; init; }
    }

    /// <summary>
    /// Records every external call that reaches it, then lets the dispatcher it stands in front of run it.
    /// </summary>
    /// <remarks>
    /// What a call arrives with is only observable here: by the time a member runs, its arguments are the
    /// member's own parameters, and a member that is not written yet never runs at all.
    /// </remarks>
    /// <param name="inner">The dispatcher that really runs the call.</param>
    public sealed class RecordingDispatcher(IExternalDispatcher inner) : IExternalDispatcher
    {
        /// <summary>
        /// Every call that reached this dispatcher, in the order it did.
        /// </summary>
        public List<ExternalCallRequest> Requests { get; } = [];

        /// <inheritdoc/>
        public RuntimeSemanticsEvaluationResult Invoke(ExternalCallRequest request, ISymbolResolver resolver)
        {
            Requests.Add(request);
            return inner.Invoke(request, resolver);
        }
    }

    /// <summary>
    /// Parses <paramref name="body"/> as the body of a <c>Sub</c>, lowers it, and runs it.
    /// </summary>
    /// <param name="fileSystem">The file system the session's file channels open against.</param>
    /// <param name="symbols">The module-level symbols the source refers to, defined into the session alongside
    /// the module and procedure that enclose the body. A variable the body declares itself (<c>Dim</c>, <c>Static</c>)
    /// is a procedure local, declared by the same pass the platform runs; one the body merely assigns is not
    /// declared at all (<strong>MS-VBAL §5.6.10</strong> is not modeled here), so it has to be one of these,
    /// declared against <see cref="ModuleUri"/>.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, params string[] body)
        => Run(fileSystem, symbols, output: null, standardLibrary: false, body);

    /// <summary>
    /// <inheritdoc cref="Run(IFileSystem?, IEnumerable{Symbol}, string[])" path="/summary"/>
    /// </summary>
    /// <param name="fileSystem">The file system the session's file channels open against.</param>
    /// <param name="symbols">The symbols the source refers to.</param>
    /// <param name="output">Where the body's <c>Debug.Print</c> output goes, or <c>null</c> to discard it.</param>
    /// <param name="standardLibrary">Whether the library's own symbols are defined too, which a body calling
    /// one of its functions needs.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, IRuntimeOutput? output, bool standardLibrary,
        params string[] body)
        => Run(fileSystem, symbols, output, standardLibrary, arrange: null, body);

    /// <summary>
    /// <inheritdoc cref="Run(IFileSystem?, IEnumerable{Symbol}, string[])" path="/summary"/>
    /// </summary>
    /// <param name="fileSystem">The file system the session's file channels open against.</param>
    /// <param name="symbols">The symbols the source refers to.</param>
    /// <param name="output">Where the body's <c>Debug.Print</c> output goes, or <c>null</c> to discard it.</param>
    /// <param name="standardLibrary">Whether the library's own symbols are defined too.</param>
    /// <param name="arrange">What to do to the composed session before the body runs - giving a variable a
    /// value source cannot build yet, such as an array with elements.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, IRuntimeOutput? output, bool standardLibrary,
        Action<IRuntimeSession>? arrange, params string[] body)
        => Run(fileSystem, symbols, output, standardLibrary, arrange, new Surroundings(), body);

    /// <summary>
    /// <inheritdoc cref="Run(IFileSystem?, IEnumerable{Symbol}, string[])" path="/summary"/>
    /// </summary>
    /// <param name="fileSystem">The file system the session's file channels open against.</param>
    /// <param name="symbols">The symbols the source refers to.</param>
    /// <param name="output">Where the body's <c>Debug.Print</c> output goes, or <c>null</c> to discard it.</param>
    /// <param name="standardLibrary">Whether the library's own symbols are defined too.</param>
    /// <param name="arrange">What to do to the composed session before the body runs.</param>
    /// <param name="directives">The module dials the body runs under - <c>Option Base</c> and the rest, which
    /// ride on the executing frame rather than on anything the body can say.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, IRuntimeOutput? output, bool standardLibrary,
        Action<IRuntimeSession>? arrange, ModuleDirectives directives, params string[] body)
        => Run(fileSystem, symbols, output, standardLibrary, arrange, new Surroundings { Directives = directives }, body);

    /// <summary>
    /// <inheritdoc cref="Run(IFileSystem?, IEnumerable{Symbol}, string[])" path="/summary"/>
    /// </summary>
    /// <param name="fileSystem">The file system the session's file channels open against.</param>
    /// <param name="symbols">The symbols the source refers to.</param>
    /// <param name="output">Where the body's <c>Debug.Print</c> output goes, or <c>null</c> to discard it.</param>
    /// <param name="standardLibrary">Whether the library's own symbols are defined too.</param>
    /// <param name="arrange">What to do to the composed session before the body runs.</param>
    /// <param name="directives">The module dials the body runs under.</param>
    /// <param name="observer">Told of every conversion and operation the body evaluates, or <c>null</c> to run it unobserved.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, IRuntimeOutput? output, bool standardLibrary,
        Action<IRuntimeSession>? arrange, ModuleDirectives directives, IAnalysisObserver? observer, params string[] body)
        => Run(fileSystem, symbols, output, standardLibrary, arrange, directives, observer, analyze: false, body);

    /// <summary>
    /// <inheritdoc cref="Run(IFileSystem?, IEnumerable{Symbol}, string[])" path="/summary"/>
    /// </summary>
    /// <param name="fileSystem">The file system the session's file channels open against.</param>
    /// <param name="symbols">The symbols the source refers to.</param>
    /// <param name="output">Where the body's <c>Debug.Print</c> output goes, or <c>null</c> to discard it.</param>
    /// <param name="standardLibrary">Whether the library's own symbols are defined too.</param>
    /// <param name="arrange">What to do to the composed session before the body runs.</param>
    /// <param name="directives">The module dials the body runs under.</param>
    /// <param name="observer">Told of every conversion and operation the body evaluates, or <c>null</c> to run it unobserved.</param>
    /// <param name="analyze">Whether the body is analyzed rather than run: in a session that shares the declared symbols of the composed one
    /// and holds the run-time state of its own (<see cref="RuntimeSessionComposer.ComposeForAnalysis"/>). The session returned is that one.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, IRuntimeOutput? output, bool standardLibrary,
        Action<IRuntimeSession>? arrange, ModuleDirectives directives, IAnalysisObserver? observer, bool analyze, params string[] body)
        => Run(fileSystem, symbols, output, standardLibrary, arrange,
            new Surroundings { Directives = directives, Observer = observer, Analyze = analyze }, body);

    /// <summary>
    /// <inheritdoc cref="Run(IFileSystem?, IEnumerable{Symbol}, string[])" path="/summary"/>
    /// </summary>
    /// <param name="fileSystem">The file system the session's file channels open against.</param>
    /// <param name="symbols">The symbols the source refers to.</param>
    /// <param name="output">Where the body's <c>Debug.Print</c> output goes, or <c>null</c> to discard it.</param>
    /// <param name="standardLibrary">Whether the library's own symbols are defined too.</param>
    /// <param name="arrange">What to do to the composed session before the body runs.</param>
    /// <param name="directives">The module dials the body runs under.</param>
    /// <param name="observer">Told of every conversion and operation the body evaluates, or <c>null</c> to run it unobserved.</param>
    /// <param name="analyze">Whether the body is analyzed rather than run.</param>
    /// <param name="outside">What the body reaches outside the platform - dialogs, desktop, settings - or <c>null</c> for none of it. Ignored by an analysis, which reaches none.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, IRuntimeOutput? output, bool standardLibrary,
        Action<IRuntimeSession>? arrange, ModuleDirectives directives, IAnalysisObserver? observer, bool analyze, RDCore.External.ExternalWorld? outside, params string[] body)
        => Run(fileSystem, symbols, output, standardLibrary, arrange,
            new Surroundings { Directives = directives, Observer = observer, Analyze = analyze, Outside = outside }, body);

    /// <summary>
    /// <inheritdoc cref="Run(IFileSystem?, IEnumerable{Symbol}, string[])" path="/summary"/>
    /// </summary>
    /// <param name="fileSystem">The file system the session's file channels open against.</param>
    /// <param name="symbols">The symbols the source refers to.</param>
    /// <param name="output">Where the body's <c>Debug.Print</c> output goes, or <c>null</c> to discard it.</param>
    /// <param name="standardLibrary">Whether the library's own symbols are defined too.</param>
    /// <param name="arrange">What to do to the composed session before the body runs.</param>
    /// <param name="surroundings">The environment, the module dials, the other procedures of the module, what
    /// stands in front of the dispatcher external calls reach, what the body reaches outside the platform, and
    /// whether the body is observed or analyzed.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, IRuntimeOutput? output, bool standardLibrary,
        Action<IRuntimeSession>? arrange, Surroundings surroundings, params string[] body)
    {
        // resolution walks the scope tree, so the module and the procedure have to be in it as symbols and
        // not only as a call frame: a name resolved from a procedure Uri no node exists for resolves to
        // nothing, whatever is defined at module scope.
        var workspace = TestUri.WorkspaceRoot();
        var module = new VBStandardModuleSymbol(workspace, workspace, ModuleName) { Directives = surroundings.Directives };

        var parse = Parse(body);

        // the procedure is what the platform makes of the source it was written in: the declaration pass builds
        // it and the variables its body declares, and the descriptors that carry it to the environment host put
        // those variables on it (SymbolDescriptorReader), which is where the runtime hoists them from. Only a
        // name the body declares is declared here - an undeclared one (MS-VBAL 5.6.10) needs a resolver that
        // knows everything else the session defines, which this does not build.
        var declared = new SyntaxTreeSymbolProvider(
            workspace, module.Uri, ModuleType.StdModule, parse, new IntrinsicSymbolResolver(), withImplicitDeclarations: false)
            .ProvideSymbols()
            // a variable the test supplies stands in for a module-level declaration the body's source does not
            // contain, so the pass cannot know it is one: a ReDim of it would declare a dynamic-array local of
            // the same name, shadowing the very variable the test is about. A name the body declares itself
            // (a Dim) is a local whatever else is called that.
            .Where(symbol => symbol is not VBLocalVariableSymbol { DeclaredBy: var by } local
                || !by.IsImplicit()
                || !symbols.Any(supplied => string.Equals(supplied.Name, local.Name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var reconstructed = SymbolDescriptorReader.Read(
            new DefineSymbolsParams
            {
                WorkspaceRoot = workspace,
                ModuleUri = module.Uri,
                ModuleName = ModuleName,
                Symbols = SymbolDescriptorProjector.Project(declared, module.Uri),
            },
            typeName => IntrinsicVBTypes.TryResolve(typeName, out var type) ? type : null).ToArray();
        var procedure = reconstructed.OfType<VBProcedureMemberSymbol>().Single(symbol => symbol.Name == ProcedureName);

        // the library's provider goes first, the way the platform composes it: a project always has these
        // symbols whether or not anything asked for them.
        ISymbolProvider[] providers = standardLibrary
            ? [new StdLibSymbolProvider(workspace), new Provider([module, .. reconstructed, .. surroundings.Procedures.Select(p => p.Symbol), .. symbols])]
            : [new Provider([module, .. reconstructed, .. surroundings.Procedures.Select(p => p.Symbol), .. symbols])];

        var session = RuntimeSessionComposer.Compose(
            surroundings.Environment, [], providers,
            output: output, fileSystem: fileSystem);

        if (surroundings.Analyze)
        {
            session = RuntimeSessionComposer.ComposeForAnalysis(session);
        }

        // the other procedures are found by the invoker the way any workspace procedure is: by their symbol's
        // SemanticId, among the bodies the pipeline was composed with.
        var bodies = surroundings.Procedures.ToDictionary(p => p.Symbol.SemanticId, p => Lower(Parse(p.Body)));
        var pipeline = surroundings.Analyze
            ? RuntimeExecutionPipeline.CreateForAnalysis(
                session, bodies, Substitute.For<IVerboseMessageBuilder>(), surroundings.Observer ?? new RecordingAnalysisObserver())
            : RuntimeExecutionPipeline.Create(
                session, bodies, Substitute.For<IVerboseMessageBuilder>(), observer: surroundings.Observer, outside: surroundings.Outside);

        // a call site reaches an external member through the evaluator's bindings, so that is where the spy goes.
        // A default member reached through a Let-coercion still goes straight to the real dispatcher: the object
        // coercion keeps bindings of its own.
        if (surroundings.Dispatcher is { } spy && pipeline.Expressions.Bindings is RuntimeCallableBindingFactory factory)
        {
            pipeline.Expressions.Bindings = factory with { External = spy(factory.External!) };
        }

        var procedureUri = procedure.Uri;
        var nodeId = new SyntaxNodeId(procedureUri.AbsolutePath, [1]);
        var frame = session.Symbols.CreateFrame(
            nodeId, new StaticSymbol(ProcedureName, SymbolKindExt.Procedure, VBVoidType.TypeInfo), surroundings.Directives);
        session.CallStack.TryPush(frame);
        RuntimeProcedureInvoker.HoistLocals(session, (CallStackFrame)frame, RuntimeProcedureInvoker.GetLocals(procedure));
        arrange?.Invoke(session);

        var outcome = pipeline.Executor.Run(session, frame, Lower(parse), new RuntimeEvaluationContext(procedureUri));
        return (session, outcome);
    }

    // the statements as the body of a Sub, parsed for real.
    private static ModuleParseResult Parse(string[] body)
    {
        var source = $"Sub {ProcedureName}()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse;
    }

    // the Sub's statements, lowered for real.
    private static InstructionList Lower(ModuleParseResult parse)
    {
        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var lowering = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(lowering.Errors, string.Join("; ", lowering.Errors.Select(error => error.Verbose)));
        return lowering.InstructionList;
    }
}
