using RDCore.SDK.Model;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Source;
using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// Calling a standard-library member from VBA source. Its code is not the workspace's, so no instruction
/// list exists for it: the symbol carries the declaration it was read off, and an
/// <see cref="IExternalDispatcher"/> reaches whatever implements it.
/// </summary>
[TestClass]
public sealed class StdLibDispatchTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    // the executing procedure has to really be a member of a module of the workspace, or the scope it resolves
    // names from has no project tier above it - and a standard module's members reach exactly that tier, which
    // is what makes `Erl` a name at all. The symbols derive their own Uris from their parents, so the
    // procedure's is taken from it rather than assembled here, where it could differ by a separator and
    // silently resolve nothing.
    private static (VBStandardModuleSymbol Module, VBProcedureMemberSymbol Procedure) Workspace()
    {
        var module = new VBStandardModuleSymbol(Root, Root, "TestModule1");
        return (module, new VBProcedureMemberSymbol(
            Root, module.Uri, "TestMethod1", ScopeKind.Module, SymbolKindExt.Procedure,
            VBVoidType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public));
    }

    /// <summary>Runs a procedure body against a session that has the standard library, capturing its output.</summary>
    private static IReadOnlyList<string> Run(params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (module, procedure) = Workspace();
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            [],
            [new StdLibSymbolProvider(Root), new Provider([module, procedure])],
            output);

        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IVerboseMessageBuilder>());

        var procedureUri = procedure.Uri;
        var nodeId = new SyntaxNodeId(procedureUri.AbsolutePath, [1]);
        var frame = session.Symbols.CreateFrame(
            nodeId, new StaticSymbol(procedure.Name, SymbolKindExt.Procedure, VBVoidType.TypeInfo));
        session.CallStack.TryPush(frame);

        var source = $"Sub Foo()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var lowering = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(lowering.Errors, string.Join("; ", lowering.Errors.Select(error => error.Verbose)));

        pipeline.Executor.Run(session, frame, lowering.InstructionList, new RuntimeEvaluationContext(procedureUri));
        return output.Lines;
    }

    [TestMethod]
    public void AStandardLibraryFunction_IsCalledFromSource()
    {
        // the first one that can be: Erl reads the session's own error state and needs nothing else.
        var output = Run("10 On Error Resume Next", "20 Error 11", "30 Debug.Print Erl");

        Assert.HasCount(1, output);
        Assert.Contains("3", output[0], "the document line of the faulting statement");
    }

    [TestMethod]
    public void AStandardLibraryFunction_ReportsZeroBeforeAnyError()
    {
        var output = Run("10 Debug.Print Erl");

        Assert.Contains("0", output[0]);
    }

    [TestMethod]
    public void AMemberNothingImplementsYet_IsARunTimeError_NotAnInternalOne()
    {
        // most of the library, today. The symbol resolves and the call is well-formed; the platform has not
        // got the code, which is a thing a program can trap rather than a gap in the interpreter.
        var output = Run(
            "10 On Error Resume Next",
            "20 Debug.Print IsNumeric(42)",
            "30 Debug.Print \"trapped\"");

        Assert.Contains("trapped", output[^1]);
    }
}
