using NSubstitute;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.Semantics;
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
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;
using System.IO.Abstractions;

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
        => Run(fileSystem, symbols, output, standardLibrary, arrange, ModuleDirectives.None, body);

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
    {
        // resolution walks the scope tree, so the module and the procedure have to be in it as symbols and
        // not only as a call frame: a name resolved from a procedure Uri no node exists for resolves to
        // nothing, whatever is defined at module scope.
        var workspace = TestUri.WorkspaceRoot();
        var module = new VBStandardModuleSymbol(workspace, workspace, ModuleName);

        var source = $"Sub {ProcedureName}()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

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
            ? [new StdLibSymbolProvider(workspace), new Provider([module, .. reconstructed, .. symbols])]
            : [new Provider([module, .. reconstructed, .. symbols])];

        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [], providers,
            output: output, fileSystem: fileSystem);

        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IVerboseMessageBuilder>());

        var procedureUri = procedure.Uri;
        var nodeId = new SyntaxNodeId(procedureUri.AbsolutePath, [1]);
        var frame = session.Symbols.CreateFrame(
            nodeId, new StaticSymbol(ProcedureName, SymbolKindExt.Procedure, VBVoidType.TypeInfo), directives);
        session.CallStack.TryPush(frame);
        RuntimeProcedureInvoker.HoistLocals(session, (CallStackFrame)frame, RuntimeProcedureInvoker.GetLocals(procedure));
        arrange?.Invoke(session);

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var lowering = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(lowering.Errors, string.Join("; ", lowering.Errors.Select(error => error.Verbose)));

        var outcome = pipeline.Executor.Run(session, frame, lowering.InstructionList, new RuntimeEvaluationContext(procedureUri));
        return (session, outcome);
    }
}
