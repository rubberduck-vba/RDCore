using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A session has one error state, which is what makes <strong>MS-VBAL §6.1.3.2</strong>'s <c>Err</c>
/// singleton possible: "the most recent run-time error". Distinct from an activation's own
/// <c>ActiveError</c>, which answers whether that frame may <c>Resume</c> — this answers what went
/// wrong in the session, and outlives the frame that raised it.
/// </summary>
[TestClass]
public sealed class SessionErrorStateTests
{
    private static readonly Uri ProcedureUri = TestUri.TestSubProcUri();
    private static readonly SyntaxNodeId NodeId = new(ProcedureUri.AbsolutePath, [1]);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    /// <summary>Runs a procedure body and returns the session it ran in.</summary>
    private static IRuntimeSession Run(params string[] body) => Run(VBErlLineNumbering.DocumentLine, body);

    /// <summary>Runs a procedure body in an environment that counts Erl's line the given way.</summary>
    private static IRuntimeSession Run(VBErlLineNumbering numbering, params string[] body)
    {
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, ErlLineNumbering: numbering), new Provider([]));

        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IVerboseMessageBuilder>());

        var frame = session.Symbols.CreateFrame(NodeId, new StaticSymbol("Foo", SymbolKindExt.Procedure, VBVoidType.TypeInfo));
        session.CallStack.TryPush(frame);

        pipeline.Executor.Run(session, frame, Lower(body), new RuntimeEvaluationContext(ProcedureUri));
        return session;
    }

    private static InstructionList Lower(params string[] procedureBody)
    {
        var source = $"Sub Foo()\r\n{string.Join("\r\n", procedureBody)}\r\nEnd Sub\r\n";
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Mod1.bas"), source);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var member = parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single();
        var result = InstructionListLowering.Lower(new StatementBlock([.. member.Children]));
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => error.Verbose)));
        return result.InstructionList;
    }

    [TestMethod]
    public void ANewSession_HasNoError()
    {
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider([]));

        Assert.IsFalse(session.Errors.HasError);
        Assert.IsNull(session.Errors.Current);
    }

    [TestMethod]
    public void AnUnhandledError_IsTheSessionsCurrentError()
    {
        // Err is set by the error being raised, not by anything handling it.
        var session = Run("10 Error 11");

        Assert.IsTrue(session.Errors.HasError);
        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, session.Errors.Current!.ErrorId);
    }

    [TestMethod]
    public void AnErrorCaughtByAHandler_IsStillTheSessionsCurrentError()
    {
        // which is the whole point: the handler body is where source reads Err.Number.
        var session = Run(
            "10 On Error GoTo 30",
            "20 Error 11",
            "30 Debug.Print 1");

        Assert.IsTrue(session.Errors.HasError);
        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, session.Errors.Current!.ErrorId);
    }

    [TestMethod]
    public void AnOnErrorStatement_ClearsIt()
    {
        // MS-VBAL 6.1.3.2.1 lists On Error among the statements that clear the Err object.
        var session = Run(
            "10 On Error Resume Next",
            "20 Error 11",
            "30 On Error GoTo 0");

        Assert.IsFalse(session.Errors.HasError);
    }

    [TestMethod]
    public void AResumeStatement_ClearsIt()
    {
        var session = Run(
            "10 On Error GoTo 40",
            "20 Error 11",
            "30 Exit Sub",
            "40 Resume 30");

        Assert.IsFalse(session.Errors.HasError);
    }

    [TestMethod]
    public void AnExitProcedureStatement_ClearsIt()
    {
        // ...and falling off the end of a procedure does not, which is why the clear is on the
        // statement rather than on the activation ending.
        var session = Run(
            "10 On Error Resume Next",
            "20 Error 11",
            "30 Exit Sub");

        Assert.IsFalse(session.Errors.HasError);
    }

    [TestMethod]
    public void FallingOffTheEndOfAProcedure_LeavesItSet()
    {
        var session = Run(
            "10 On Error Resume Next",
            "20 Error 11");

        Assert.IsTrue(session.Errors.HasError);
    }

    [TestMethod]
    public void AnErrorStatement_RaisesAnApplicationError()
    {
        // RD-VBAL §2.6.3: an application error is one "explicitly raised from workspace source code with
        // Error or Err.Raise", and it carries the VBA family rather than VBR. The family follows *who
        // raised it*, not what the number means - so `Error 11` is VBA00011, even though 11 is the code
        // MS-VBA gives division by zero.
        var session = Run("10 Error 11");

        Assert.IsInstanceOfType<VBApplicationErrorInfo>(session.Errors.Current);
        Assert.AreEqual("VBA00011", session.Errors.Current!.ToDiagnosticCode());
    }

    [TestMethod]
    public void AnErrorTheRuntimeSemanticsReport_IsNotAnApplicationError()
    {
        // the other half of the same rule: a division by zero the evaluator actually hit is VBR00011.
        var session = Run("10 Debug.Print 1 / 0");

        Assert.IsInstanceOfType<VBRuntimeErrorInfo>(session.Errors.Current);
        Assert.AreEqual("VBR00011", session.Errors.Current!.ToDiagnosticCode());
    }

    [TestMethod]
    public void AnError_RecordsTheLineItWasRaisedAt()
    {
        // 🎯 what Erl reports - RD-VBAL's own, undocumented in MS-VBAL and hidden in MS-VBA. The body is
        // wrapped in a Sub, so the faulting statement is on the second line of the document, counted from 1.
        var session = Run("10 Error 11");

        Assert.AreEqual(2L, session.Errors.LineNumber);
    }

    [TestMethod]
    public void AnUnnumberedStatement_IsStillLocated()
    {
        // the whole point of counting document lines: nothing has to number its lines to be locatable.
        var session = Run("On Error Resume Next", "Error 11");

        Assert.AreEqual(3L, session.Errors.LineNumber);
    }

    [TestMethod]
    public void WithMSVBACompatibleNumbering_TheLineIsTheLastLabel()
    {
        var session = Run(VBErlLineNumbering.LineLabel, "10 Error 11");

        Assert.AreEqual(10L, session.Errors.LineNumber);
    }

    [TestMethod]
    public void WithMSVBACompatibleNumbering_AnUnnumberedStatement_ReportsAStaleLabel()
    {
        // this is the behaviour that makes MS-VBA's Erl worth a dial rather than a faithful copy: the fault
        // is two statements past line 10 and it is reported as line 10 regardless.
        var session = Run(VBErlLineNumbering.LineLabel, "10 On Error Resume Next", "Error 11");

        Assert.AreEqual(10L, session.Errors.LineNumber);
    }

    [TestMethod]
    public void WithMSVBACompatibleNumbering_AProgramThatNumbersNothing_ReportsZero()
    {
        // ...and the same behaviour says every error in unnumbered code happened at line 0.
        var session = Run(VBErlLineNumbering.LineLabel, "Error 11");

        Assert.AreEqual(0L, session.Errors.LineNumber);
    }

    [TestMethod]
    public void ClearingTheError_ClearsItsLineNumber()
    {
        var session = Run("10 On Error Resume Next", "20 Error 11", "30 On Error GoTo 0");

        Assert.AreEqual(0L, session.Errors.LineNumber);
    }

    [TestMethod]
    public void ASecondError_ReplacesTheFirst()
    {
        var session = Run(
            "10 On Error Resume Next",
            "20 Error 11",
            "30 Error 9");

        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, session.Errors.Current!.ErrorId);
    }
}
