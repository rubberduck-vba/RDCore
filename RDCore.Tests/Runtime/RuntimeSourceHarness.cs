using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
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
    /// <param name="symbols">The symbols the source refers to, defined into the session alongside the module
    /// and procedure that enclose the body — there is no declaration pass here, so a variable the body
    /// assigns has to be one of these, declared against <see cref="ModuleUri"/>.</param>
    /// <param name="body">The statements, one per line.</param>
    public static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome) Run(
        IFileSystem? fileSystem, IEnumerable<Symbol> symbols, params string[] body)
    {
        // resolution walks the scope tree, so the module and the procedure have to be in it as symbols and
        // not only as a call frame: a name resolved from a procedure Uri no node exists for resolves to
        // nothing, whatever is defined at module scope.
        var workspace = TestUri.WorkspaceRoot();
        var module = new VBStandardModuleSymbol(workspace, workspace, ModuleName);
        var procedure = new VBProcedureMemberSymbol(
            workspace, module.Uri, ProcedureName, ScopeKind.Module, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [],
            [new Provider([module, procedure, .. symbols])],
            output: null, fileSystem: fileSystem);

        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IVerboseMessageBuilder>());

        var procedureUri = procedure.Uri;
        var nodeId = new SyntaxNodeId(procedureUri.AbsolutePath, [1]);
        var frame = session.Symbols.CreateFrame(nodeId, new StaticSymbol(ProcedureName, SymbolKindExt.Procedure, VBVoidType.TypeInfo));
        session.CallStack.TryPush(frame);

        var source = $"Sub {ProcedureName}()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var lowering = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(lowering.Errors, string.Join("; ", lowering.Errors.Select(error => error.Verbose)));

        var outcome = pipeline.Executor.Run(session, frame, lowering.InstructionList, new RuntimeEvaluationContext(procedureUri));
        return (session, outcome);
    }
}
