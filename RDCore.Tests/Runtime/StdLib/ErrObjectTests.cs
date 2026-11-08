using NSubstitute;
using RDCore.Parsing;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.3.2</strong>'s error object: the single <c>ErrObject</c> instance whose properties
/// "reflect and control the error state of the active VBA Environment". It holds nothing of its own —
/// every property is a view over the session's <see cref="ISessionErrorState"/>, which is what makes it a
/// singleton rather than something a session could have two of.
/// </summary>
[TestClass]
public sealed class ErrObjectTests
{
    private static readonly Uri ProcedureUri = TestUri.TestSubProcUri();
    private static readonly SyntaxNodeId NodeId = new(ProcedureUri.AbsolutePath, [1]);

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static IRuntimeSession Session(IRuntimeEnvironmentProfile? environment = null)
        => RuntimeSessionComposer.Compose(
            environment ?? new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider([]));

    /// <summary>Runs a procedure body in a frame named <paramref name="frameNames"/>' last entry.</summary>
    private static IRuntimeSession Run(string[] frameNames, params string[] body) => Run(null, frameNames, body);

    /// <summary>
    /// Runs a procedure body in a frame named <paramref name="frameNames"/>' last entry, in <paramref name="environment"/>.
    /// </summary>
    private static IRuntimeSession Run(IRuntimeEnvironmentProfile? environment, string[] frameNames, params string[] body)
    {
        var session = Session(environment);
        var pipeline = RuntimeExecutionPipeline.Create(
            session, new Dictionary<SemanticId, InstructionList>(), Substitute.For<IErrorDetailsRecorder>());

        ICallStackFrame? innermost = null;
        foreach (var name in frameNames)
        {
            innermost = session.Symbols.CreateFrame(NodeId, new StaticSymbol(name, SymbolKindExt.Procedure, VBVoidType.TypeInfo));
            session.CallStack.TryPush(innermost);
        }

        pipeline.Executor.Run(session, innermost!, Lower(body), new RuntimeEvaluationContext(ProcedureUri));
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

    private static string StringOf(RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult<VBStringValue> result)
        => (string)result.Result!.Handle.Value.BoxedValue!;

    private static int IntOf(RDCore.SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult<VBLongValue> result)
        => Convert.ToInt32(result.Result!.Handle.Value.BoxedValue);

    [TestMethod]
    public void WithNoError_NumberIsZero_AndDescriptionIsEmpty()
    {
        var err = new ErrObject(Session());

        Assert.AreEqual(0, IntOf(err.Number()));
        Assert.AreEqual(string.Empty, StringOf(err.Description()));
    }

    [TestMethod]
    public void AfterAnError_NumberAndDescriptionReportIt()
    {
        var err = new ErrObject(Run(["Foo"], "10 Error 11"));

        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, IntOf(err.Number()));
        Assert.AreEqual("Division by zero", StringOf(err.Description()));
    }

    // Erl is the error state's line number: Err.Clear resets it unless the environment keeps it, as MS-VBA does. The error is on the procedure's third line.
    [TestMethod]
    [DataRow(null, 0L, DisplayName = "the default resets it")]
    [DataRow(VBErlAfterErrClear.Reset, 0L, DisplayName = "Reset")]
    [DataRow(VBErlAfterErrClear.Kept, 3L, DisplayName = "Kept, as MS-VBA does")]
    public void Clear_ResetsTheLineNumber_UnlessTheEnvironmentKeepsIt(VBErlAfterErrClear? setting, long expected)
    {
        var environment = setting is { } value
            ? new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, ErlAfterErrClear: value)
            : new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false);
        var session = Run(environment, ["Foo"], "10 On Error Resume Next", "20 Error 11");
        var err = new ErrObject(session);
        Assert.AreEqual(3L, session.Errors.LineNumber, "the line of the error, before it is cleared");

        err.Clear();

        Assert.AreEqual(0, IntOf(err.Number()));
        Assert.AreEqual(expected, session.Errors.LineNumber);
    }

    // only Err.Clear keeps it: a Resume, an On Error statement and leaving the procedure from an error handler reset it in MS-VBA too.
    [TestMethod]
    public void WhenTheEnvironmentKeepsTheLineNumber_AnOnErrorStatement_StillResetsIt()
    {
        var environment = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, ErlAfterErrClear: VBErlAfterErrClear.Kept);
        Assert.AreEqual(3L, Run(environment, ["Foo"], "10 On Error Resume Next", "20 Error 11").Errors.LineNumber, "the line of the error, before On Error");

        var session = Run(environment, ["Foo"], "10 On Error Resume Next", "20 Error 11", "30 On Error GoTo 0");

        Assert.AreEqual(0L, session.Errors.LineNumber);
    }

    [TestMethod]
    public void AMemberThatYieldsNoValue_YieldsVoid_NotEmpty()
    {
        // Void is the model's "not a real value" for a Sub or a Property Let, with an HRESULT under it.
        // Empty is something else entirely: a real Variant subtype that source can see (IsEmpty, VarType,
        // a Let-coercion to 0), and no assignment or Clear yields a value source can see anything about.
        var err = new ErrObject(Session());

        Assert.IsInstanceOfType<VBVoidValue>(err.Clear().Result);
        Assert.IsInstanceOfType<VBVoidValue>(err.Number(new VBLongValue(5)).Result);
        Assert.IsInstanceOfType<VBVoidValue>(err.Raise(new VBLongValue(513)).Result, "an error still yields no value");
    }


    [TestMethod]
    public void Clear_ResetsEveryProperty()
    {
        var err = new ErrObject(Run(["Foo"], "10 Error 11"));
        err.Source(new VBStringValue("Mod1.Foo"));

        err.Clear();

        Assert.AreEqual(0, IntOf(err.Number()));
        Assert.AreEqual(string.Empty, StringOf(err.Description()));
        Assert.AreEqual(string.Empty, StringOf(err.Source()));
        Assert.AreEqual(string.Empty, StringOf(err.StackTrace()));
    }

    [TestMethod]
    public void EveryPropertyThatSourceCanSet_ReadsBackWhatWasSet()
    {
        var err = new ErrObject(Session());

        err.Number(new VBLongValue(513));
        err.Description(new VBStringValue("something specific"));
        err.Source(new VBStringValue("MyProject.MyClass"));
        err.HelpFile(new VBStringValue("nowhere.chm"));
        err.HelpContext(new VBLongValue(42));

        Assert.AreEqual(513, IntOf(err.Number()));
        Assert.AreEqual("something specific", StringOf(err.Description()));
        Assert.AreEqual("MyProject.MyClass", StringOf(err.Source()));
        Assert.AreEqual("nowhere.chm", StringOf(err.HelpFile()));
        Assert.AreEqual(42, IntOf(err.HelpContext()));
    }

    [TestMethod]
    public void SettingNumber_MakesAnErrorCurrent()
    {
        // `Err.Number = 5` is how VBA source makes an error current without raising one, and
        // `Err.Number <> 0` is the whole of what "there is an error" means.
        var session = Session();
        new ErrObject(session).Number(new VBLongValue(5));

        Assert.IsTrue(session.Errors.HasError);
    }

    [TestMethod]
    public void LastDllError_IsReadOnly_AndZero()
    {
        // MS-VBAL 6.1.3.2.2.4: it is filled by a call into a DLL, and no Declare'd procedure can be
        // invoked yet - so it reads 0, and there is no accessor to set it with.
        Assert.AreEqual(0, IntOf(new ErrObject(Run(["Foo"], "10 Error 11")).LastDllError()));
    }

    [TestMethod]
    public void Raise_YieldsTheErrorRatherThanRecordingIt()
    {
        // the executor's own error interception is the one place every run-time error reaches the
        // session's error state; raising here as well would record it twice and capture the wrong stack.
        var session = Session();
        var err = new ErrObject(session);

        var result = err.Raise(new VBLongValue(513), description: new VBVariantValue(new VBStringValue("no good")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(513, result.ErrorInfo!.ErrorId);
        Assert.AreEqual("no good", result.ErrorInfo.Description);
        Assert.IsFalse(session.Errors.HasError, "recording it is the executor's job, not this one's");
    }

    [TestMethod]
    public void Raise_YieldsAnApplicationError_NotARuntimeSemanticsOne()
    {
        // RD-VBAL 2.6.3: Err.Raise is where workspace source raises its own error, so it carries the VBA
        // family. The family follows who raised it and nothing else - Err.Raise 11 is VBA00011, not the
        // VBR00011 the evaluator would report for a division by zero it actually hit.
        var result = new ErrObject(Session()).Raise(new VBLongValue((int)VBRuntimeErrorId.DivisionByZero));

        Assert.IsInstanceOfType<VBApplicationErrorInfo>(result.ErrorInfo);
        Assert.AreEqual("VBA00011", result.ErrorInfo!.ToDiagnosticCode());
    }

    [TestMethod]
    public void Raise_WithNoDescription_UsesTheStandardMessageForTheNumber()
    {
        // MS-VBAL 6.1.3.2.1.2: "If unspecified, the value in Number is examined... the String that would
        // be returned by the Error function is used as Description."
        var result = new ErrObject(Session()).Raise(new VBLongValue((int)VBRuntimeErrorId.SubscriptOutOfRange));

        Assert.AreEqual("Subscript out of range", result.ErrorInfo!.Description);
    }

    [TestMethod]
    public void Raise_WithANumberThatIsNoVBAError_UsesTheApplicationDefinedMessage()
    {
        // ..."If there is no VBA error corresponding to Number, the 'Application-defined or
        // object-defined error' message is used."
        var result = new ErrObject(Session()).Raise(new VBLongValue(60000));

        Assert.AreEqual("Application-defined or object-defined error", result.ErrorInfo!.Description);
    }

    [TestMethod]
    public void Raise_WithNoSource_KeepsWhatSourceWasAlreadySetTo()
    {
        // ..."If Raise is invoked without specifying some arguments, and the property settings of the Err
        // object contain values that have not been cleared, those values serve as the values for the new
        // error." An omitted argument reads the property; it does not reset it.
        var err = new ErrObject(Session());
        err.Source(new VBStringValue("MyProject.MyClass"));

        err.Raise(new VBLongValue(513));

        Assert.AreEqual("MyProject.MyClass", StringOf(err.Source()));
    }

    [TestMethod]
    public void StackTrace_NamesTheProcedureTheErrorWasRaisedIn()
    {
        var err = new ErrObject(Run(["Foo"], "10 Error 11"));

        Assert.Contains("Foo", StringOf(err.StackTrace()));
    }

    [TestMethod]
    public void StackTrace_OfAnActivationInsideGoSubs_SaysWhichGoSubsItCameFrom_InnermostFirst()
    {
        var module = new Uri("file:///c:/ws/Module1.bas");
        var frame = new VBStackTraceFrame("Main", new SourceLocation(module, new SourceRange(7, 4, 7, 20)),
            [new SourceLocation(module, new SourceRange(4, 4, 4, 16)), new SourceLocation(module, new SourceRange(1, 4, 1, 15))]);

        Assert.AreEqual("Main (8:5) from (5:5) from (2:5)", frame.ToString());
    }

    [TestMethod]
    public void StackTrace_ListsTheActivationsInnermostFirst()
    {
        // 🎯 RDCore's own, not MS-VBAL's: VBA can say what an error was but never where it came from.
        var err = new ErrObject(Run(["Main", "Middle", "Inner"], "10 Error 11"));

        var lines = StringOf(err.StackTrace()).Split("\r\n");

        Assert.AreEqual(3, lines.Length);
        Assert.Contains("Inner", lines[0]);
        Assert.Contains("Middle", lines[1]);
        Assert.Contains("Main", lines[2]);
    }

    [TestMethod]
    public void StackTrace_LocatesTheFaultingStatement_AndNoCallerWithoutABody()
    {
        // a caller is located by the statement its body is at: one pushed with no body is at none.
        var err = new ErrObject(Run(["Main", "Inner"], "10 Error 11"));

        var lines = StringOf(err.StackTrace()).Split("\r\n");

        Assert.Contains("(", lines[0], "the faulting statement's own line and column");
        Assert.DoesNotContain("(", lines[1]);
    }

    [TestMethod]
    public void StackTrace_IsEmptyWithNoError()
    {
        Assert.AreEqual(string.Empty, StringOf(new ErrObject(Session()).StackTrace()));
    }
}
