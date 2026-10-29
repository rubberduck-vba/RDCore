using RDCore.CLI.Host;
using RDCore.Runtime.Compiled;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Reflection;
using System.Reflection.Emit;

namespace RDCore.Tests.Cli;

/// <summary>
/// The compiled engine against the interpreter: a program runs the same either way - what it prints, the error it raises and
/// where - and the procedures compiled code does are compiled, and the others say why they are not.
/// </summary>
[TestClass]
public sealed class CompiledExecutionTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private sealed record class Run(ExecuteSessionResult Result, IReadOnlyDictionary<string, CompilationResult> Compiled);

    // compiled code works on payloads, which the suite is also run without: what it does is not to be seen then, as every procedure is refused.
    internal static void RequirePayloads()
    {
        if (!UnboxedEvaluation.IsEnabled)
        {
            Assert.Inconclusive("compiled code needs unboxed values (RDCORE_UNBOXED=0 refuses every procedure)");
        }
    }

    // what compiled code does without payloads is to be seen only where the suite is run without them.
    private static void RequireBoxedValues()
    {
        if (UnboxedEvaluation.IsEnabled)
        {
            Assert.Inconclusive("values are held unboxed: what compiled code does without them is seen with RDCORE_UNBOXED=0");
        }
    }

    private static async Task<Run> RunAsync(string program, VBExecutionEngine engine, Func<EnvironmentSessionProvider, bool>? cancelWhen = null,
        (string Name, string Source)[]? classes = null)
    {
        ExecuteSessionResult? result = null;
        IReadOnlyDictionary<string, CompilationResult> compiled = new Dictionary<string, CompilationResult>();

        // whatever engine the suite runs every session with (RDCORE_ENGINE): the comparison is of the two.
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes ?? NoClasses, program, (provider, run, _) =>
        {
            result = run;
            compiled = provider.Image.Compiler.Results.ToDictionary(entry => entry.Procedure.Name, entry => entry.Result);
            return Task.CompletedTask;
        }, cancelWhen, engine: engine);

        return new Run(result!, compiled);
    }

    // what a run of a program is to the person who runs it, which is the same whatever engine runs it.
    private static string Observed(ExecuteSessionResult result)
        => $"{result.Outcome} | {string.Join(" ; ", result.Output.Select(line => line.Trim()))} | {result.ErrorNumber} {result.ErrorCode} {result.ErrorMessage} "
            + $"@{result.ErrorLine}:{result.ErrorCharacter} Erl {result.ErrorLineNumber} | {string.Join(" < ", result.StackTrace.Select(frame => $"{frame.Procedure}@{frame.Line}:{frame.Character}"))}";

    // the program run both ways: the compiled run is the interpreted one, and the procedures named were compiled and ran in compiled code to their end.
    private static Task<Run> AssertAlikeAsync(string program, params string[] compiled) => AssertAlikeAsync(program, walksBack: false, compiled);

    // the same, of procedures whose activation compiled code hands to the interpreter: what fails, or what no payload operation does.
    private static Task<Run> AssertAlikeWalkingBackAsync(string program, params string[] compiled) => AssertAlikeAsync(program, walksBack: true, compiled);

    private static async Task<Run> AssertAlikeAsync(string program, bool walksBack, string[] compiled)
    {
        RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var run = await RunAsync(program, VBExecutionEngine.Compiled);

        Assert.AreEqual(Observed(interpreted.Result), Observed(run.Result));
        foreach (var name in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (walksBack)
            {
                Assert.IsGreaterThan(0L, result.Body.WalkBacks, $"{name} ran to its end in compiled code");
            }
            // under the random walk-back every activation may be handed to the interpreter, which is what that run is for.
            else if (!CompiledExecution.WalksBackAtRandom)
            {
                Assert.AreEqual(0L, result.Body.WalkBacks, $"{name} was handed to the interpreter");
            }
        }

        Assert.IsEmpty(interpreted.Compiled, "the interpreter compiles nothing");
        return run;
    }

    [TestMethod]
    public async Task TheReferenceLoop_IsCompiled_AndSumsAsTheInterpreterDoes()
    {
        await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Total()",
            "End Sub",
            "Private Function Total() As Double",
            "    Dim I As Long",
            "    Dim S As Double",
            "    For I = 1 To 100000",
            "        S = S + I",
            "    Next",
            "    Total = S",
            "End Function"), "Total");
    }

    [TestMethod]
    public async Task TheOperatorsOfScalars_GiveWhatTheInterpreterGives()
    {
        await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Extended(), Wide(), Mixed(7, 3), Logical(12, 10), Compared(2.5, 3), Negated(5), Currency(3), Dated(), Single(), Precedence(2, 3)",
            "End Sub",
            "Private Function Extended() As Double",
            "    Extended = (0.1 + 0.2) - 0.3",
            "End Function",
            "Private Function Wide() As Double",
            "    Dim D As Double",
            "    D = 1E+308",
            "    Wide = D * 10 / 10",
            "End Function",
            "Private Function Mixed(ByVal A As Long, ByVal B As Integer) As Long",
            "    Mixed = A \\ B + A Mod B * 100 - A * B",
            "End Function",
            "Private Function Logical(ByVal A As Long, ByVal B As Long) As Long",
            "    Logical = (A And B) + (A Or B) * 100 + (A Xor B) * 10000 + Not A + (A Eqv B) + (A Imp B)",
            "End Function",
            "Private Function Compared(ByVal D As Double, ByVal L As Long) As Boolean",
            "    Compared = D < L And D * 2 > L And Not D = L",
            "End Function",
            "Private Function Negated(ByVal L As Long) As Long",
            "    Negated = -L - -(-L)",
            "End Function",
            "Private Function Currency(ByVal L As Long) As Currency",
            "    Dim C As Currency",
            "    C = L",
            "    Currency = C + C * 2",
            "End Function",
            "Private Function Dated() As Date",
            "    Dim D As Date",
            "    D = #1/1/2000#",
            "    Dated = D + 1.5",
            "End Function",
            "Private Function Single() As Single",
            "    Dim S As Single",
            "    S = 16777216",
            "    Single = S + 1",
            "End Function",
            "Private Function Precedence(ByVal A As Byte, ByVal B As Byte) As Integer",
            "    Precedence = (A + B) * (B - A) + A",
            "End Function"), "Extended", "Wide", "Mixed", "Logical", "Compared", "Negated", "Currency", "Dated", "Single", "Precedence");
    }

    [TestMethod]
    public async Task TheLoopsAndTheBranches_GoWhereTheInterpretersGo()
    {
        await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Loops(10), Branches(5), Branches(-5), Branches(0), Halves(), Downwards()",
            "End Sub",
            "Private Function Loops(ByVal N As Long) As Long",
            "    Dim I As Long, J As Long, S As Long",
            "    Do While I < N",
            "        I = I + 1",
            "        If I = 7 Then Exit Do",
            "        S = S + I",
            "    Loop",
            "    Do Until J >= N",
            "        J = J + 2",
            "    Loop",
            "    Do",
            "        J = J - 1",
            "    Loop While J > 3",
            "    Do",
            "        J = J + 3",
            "    Loop Until J > 20",
            "    While J > 15",
            "        J = J - 4",
            "    Wend",
            "    For I = 1 To 100",
            "        If I > 4 Then Exit For",
            "        S = S + I * 1000",
            "    Next",
            "    Loops = S + J * 1000000 + I * 100000000",
            "End Function",
            "Private Function Branches(ByVal N As Long) As Long",
            "    If N > 0 Then",
            "        Branches = 1",
            "    ElseIf N < 0 Then",
            "        Branches = -1",
            "        Exit Function",
            "    Else",
            "        Branches = 100",
            "    End If",
            "    Branches = Branches * 2",
            "End Function",
            "Private Function Halves() As Double",
            "    Dim D As Double, S As Double",
            "    For D = 0 To 3 Step 0.5",
            "        S = S + D",
            "    Next",
            "    Halves = S + D",
            "End Function",
            "Private Function Downwards() As Long",
            "    Dim I As Integer, S As Long",
            "    For I = 10 To 1 Step -3",
            "        S = S * 10 + I",
            "    Next",
            "    Downwards = S + I",
            "End Function"), "Loops", "Branches", "Halves", "Downwards");
    }

    [TestMethod]
    public async Task AnOverflowInACompiledLoop_IsRaisedWhereTheInterpreterRaisesIt()
    {
        var run = await AssertAlikeWalkingBackAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Count()",
            "End Sub",
            "Private Function Count() As Long",
            "    Dim I As Integer",
            "    For I = 32760 To 32767",
            "        Count = Count + 1",
            "    Next",
            "End Function"), "Count");

        Assert.AreEqual(6, run.Result.ErrorNumber);
    }

    [TestMethod]
    public async Task ADivisionByZero_IsRaisedAsTheInterpreterRaisesIt()
    {
        var run = await AssertAlikeWalkingBackAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Quotient(7, 0)",
            "End Sub",
            "Private Function Quotient(ByVal A As Long, ByVal B As Long) As Long",
            "    Quotient = A \\ B",
            "End Function"), "Quotient");

        Assert.AreEqual(11, run.Result.ErrorNumber);
    }

    [TestMethod]
    public async Task AConversionThatRounds_IsTheInterpretersToMake_AndTheLoopGoesOn()
    {
        // a Double variable to a Long rounds to even, which no payload operation does: the interpreter is handed the activation at that Let, and runs the
        // rest of it.
        await AssertAlikeWalkingBackAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded()",
            "End Sub",
            "Private Function Rounded() As Long",
            "    Dim I As Long, L As Long, M As Long, D As Double",
            "    For I = 1 To 5",
            "        D = I + 0.5",
            "        M = D",
            "        L = L * 10 + M",
            "    Next",
            "    Rounded = L",
            "End Function"), "Rounded");
    }

    [TestMethod]
    public async Task AnExtendedResultStoredInALong_RoundsToEven_AsTheInterpreterRoundsIt()
    {
        // what an arithmetic operation of a Double computes is stored in its destination by compiled code, rounded as the interpreter rounds it.
        await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Rounded()",
            "End Sub",
            "Private Function Rounded() As Long",
            "    Dim I As Long, L As Long, D As Double",
            "    For I = 1 To 5",
            "        D = I + 0.5",
            "        L = L * 10 + D",
            "    Next",
            "    Rounded = L",
            "End Function"), "Rounded");
    }

    // a compiled procedure calls a procedure of the workspace whose parameters are scalars passed by value: a call statement, and a function's result
    // assigned whole.
    [TestMethod]
    public async Task ACallOfAProcedure_FromCompiledCode_IsMadeAsTheInterpreterMakesIt()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(10)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long, t As Long, total As Long",
            "    For i = 1 To n",
            "        t = Twice(i)",
            "        total = total + t",
            "        Touch i, 2.5",
            "    Next",
            "    Outer = total",
            "End Function",
            "Private Function Twice(ByVal x As Long) As Long",
            "    Twice = x * 2",
            "End Function",
            "Private Sub Touch(ByVal x As Long, ByVal y As Double)",
            "    Dim z As Double",
            "    z = x + y",
            "End Sub"), "Outer", "Twice", "Touch");

    // a function returning an object, called as a statement: what it returns is let go of when the statement is over, which compiled code holds nothing
    // of - the caller is not compiled, and says why.
    [TestMethod]
    public async Task AFunctionReturningAnObject_CalledAsAStatement_IsNotCalledFromCompiledCode()
    {
        RequirePayloads();
        var program = Program(
            "Public Sub Main()",
            "    Outer",
            "    Debug.Print \"after outer\"",
            "End Sub",
            "Private Sub Outer()",
            "    Dim i As Long",
            "    For i = 1 To 2",
            "        MakeOne i",
            "        Report i",
            "    Next",
            "End Sub",
            "Private Function MakeOne(ByVal x As Long) As Class1",
            "    Set MakeOne = New Class1",
            "    MakeOne.Id = x",
            "End Function",
            "Private Sub Report(ByVal x As Long)",
            "    Debug.Print \"report \" & x",
            "End Sub");
        (string Name, string Source)[] classes =
            [("Class1", ModuleWorkspace.ClassModule("Class1", "Public Id As Long", "Private Sub Class_Terminate()", "    Debug.Print \"terminate \" & Id", "End Sub"))];

        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, classes: classes);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, classes: classes);

        Assert.AreEqual(Observed(interpreted.Result), Observed(run.Result));
        CollectionAssert.AreEqual(new[] { "terminate 1", "report 1", "terminate 2", "report 2", "after outer" }, run.Result.Output.Select(line => line.Trim()).ToArray());
        var reason = Assert.ContainsSingle(run.Compiled["Outer"].Reasons);
        Assert.AreEqual((IneligibilityKind.Call, "MakeOne"), (reason.Kind, reason.Detail));
    }

    // an error raised in the callee reaches the compiled caller at its call, and the interpreter gives it the handler a caller of it has: the same error,
    // line and stack.
    [TestMethod]
    public async Task AnErrorRaisedInTheCallee_OfACompiledCaller_IsCaughtAsTheInterpreterCatchesIt()
        => await AssertAlikeWalkingBackAsync(Program(
            "Public Sub Main()",
            "    On Error GoTo Caught",
            "    Debug.Print Outer(2000000000)",
            "    Exit Sub",
            "Caught:",
            "    Debug.Print \"caught \" & Err.Number & \" at \" & Erl",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim t As Long",
            "    t = Twice(n)",
            "    Outer = t",
            "End Function",
            "Private Function Twice(ByVal x As Long) As Long",
            "    Twice = x * 2",
            "End Function"), "Outer");

    [TestMethod]
    public async Task AnErrorRaisedInTheCallee_OfACompiledCaller_ThatNothingCatches_EndsTheProgramAsTheInterpreterEndsIt()
        => await AssertAlikeWalkingBackAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(2000000000)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim t As Long",
            "    t = Twice(n)",
            "    Outer = t",
            "End Function",
            "Private Function Twice(ByVal x As Long) As Long",
            "    Twice = x * 2",
            "End Function"), "Outer");

    [TestMethod]
    public async Task AnEndInTheCallee_OfACompiledCaller_EndsTheProgramAsTheInterpreterEndsIt()
        => await AssertAlikeWalkingBackAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Outer(3)",
            "    Debug.Print \"never\"",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim i As Long",
            "    For i = 1 To n",
            "        Finish i",
            "    Next",
            "    Outer = n",
            "End Function",
            "Private Sub Finish(ByVal i As Long)",
            "    Debug.Print \"finish \" & i",
            "    If i = 2 Then End",
            "End Sub"), "Outer");

    [TestMethod]
    public async Task ADoubleStoredInALong_ThatDoesNotFit_IsAnOverflow_AsTheInterpreterRaisesIt()
    {
        // compiled code runs the statements before the store, and hands the activation over at the store, whose error the interpreter raises.
        string[] lines =
        [
            "Public Sub Main()",
            "    Debug.Print Stored(3000000000#)",
            "End Sub",
            "Private Function Stored(ByVal D As Double) As Long",
            "    Dim L As Long, I As Long",
            "    For I = 1 To 3",
            "        L = L + I",
            "    Next",
            "    L = D * 1",
            "    Stored = L",
            "End Function",
        ];
        var run = await AssertAlikeWalkingBackAsync(Program(lines), "Stored");

        Assert.AreEqual(6, run.Result.ErrorNumber);

        // under the random walk-back the activation may be handed over before it gets to the store, and the interpreter raises the error all the same.
        if (!CompiledExecution.WalksBackAtRandom)
        {
            Assert.AreEqual(1L, run.Compiled["Stored"].Body!.WalkBacks);
            Assert.AreEqual(Array.IndexOf(lines, "    L = D * 1"), run.Compiled["Stored"].Body!.LastWalkedBackAt?.Range.Start.Line, "handed over at the store, not before it");
        }
    }

    [TestMethod]
    public void AFailureOfTheCompiler_IsAReasonTheProcedureRunsInTheInterpreter_AndIsReported()
    {
        // a procedure whose parameters were never set is no procedure a session defines, and makes the analysis fail.
        RequirePayloads();
        var root = new Uri("file://rdcore-test");
        var procedure = new SDK.Model.Symbols.VBProject.VBProcedureMemberSymbol(root, root, "Broken", SDK.Model.Symbols.Abstract.ScopeKind.Module,
            SDK.Model.Source.SymbolKindExt.Procedure, SDK.Model.Types.Complex.VBVoidType.TypeInfo, default, default, SDK.Model.AccessModifier.Public) { Parameters = default };
        var list = SDK.Semantics.Instructions.InstructionListLowering.Lower(new SDK.Model.AST.Statements.StatementBlock([]),
            SDK.Semantics.Instructions.InstructionLoweringOptions.Default, SDK.Model.AST.Declarations.MemberKind.Procedure).InstructionList;
        var compiler = new ProcedureCompiler();
        Exception? reported = null;
        compiler.Refused += (_, exception) => reported = exception;

        var result = compiler.Compile(NSubstitute.Substitute.For<IRuntimeSession>(), null!, procedure, list);

        Assert.IsNull(result.Body);
        var reason = Assert.ContainsSingle(result.Reasons);
        Assert.AreEqual(IneligibilityKind.Refused, reason.Kind);
        Assert.IsNotNull(reported);
        Assert.StartsWith(reported.GetType().Name, reason.Detail);
    }

    [TestMethod]
    public void AHandlerOfAFailureThatFails_FailsNoCall_AndTheRefusalStands()
    {
        RequirePayloads();
        var root = new Uri("file://rdcore-test");
        var procedure = new SDK.Model.Symbols.VBProject.VBProcedureMemberSymbol(root, root, "Broken", SDK.Model.Symbols.Abstract.ScopeKind.Module,
            SDK.Model.Source.SymbolKindExt.Procedure, SDK.Model.Types.Complex.VBVoidType.TypeInfo, default, default, SDK.Model.AccessModifier.Public) { Parameters = default };
        var list = SDK.Semantics.Instructions.InstructionListLowering.Lower(new SDK.Model.AST.Statements.StatementBlock([]),
            SDK.Semantics.Instructions.InstructionLoweringOptions.Default, SDK.Model.AST.Declarations.MemberKind.Procedure).InstructionList;
        var compiler = new ProcedureCompiler();
        var told = 0;
        compiler.Refused += (_, _) =>
        {
            told++;
            throw new InvalidOperationException("the handler fails");
        };
        var session = NSubstitute.Substitute.For<IRuntimeSession>();

        var first = compiler.Compile(session, null!, procedure, list);
        var again = compiler.Compile(session, null!, procedure, list);

        Assert.IsNull(first.Body);
        Assert.AreSame(first, again, "the refusal is the procedure's result, compiled once");
        Assert.AreEqual(1, told);
    }

    [TestMethod]
    public async Task ACompiledLoopThatNeverEnds_IsStoppedAsTheInterpreterStopsIt()
    {
        var program = Program(
            "Public Sub Main()",
            "    Forever",
            "End Sub",
            "Private Sub Forever()",
            "    Dim I As Long",
            "    Do",
            "        I = (I + 1) Mod 1000",
            "    Loop",
            "End Sub");
        RequirePayloads();

        // cancelled once the program is in Forever: its activation on the stack, under Main's.
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, provider => provider.Session.CallStack.Depth >= 2);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled, provider => provider.Session.CallStack.Depth >= 2);

        Assert.AreEqual(interpreted.Result.Outcome, compiled.Result.Outcome);
        Assert.IsNotNull(compiled.Compiled["Forever"].Body);
    }

    [TestMethod]
    public async Task AProcedureCompiledCodeDoesNotDo_RunsInTheInterpreter_AndSaysWhy()
    {
        var run = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Guarded(0)",
            "End Sub",
            "Private Function Guarded(ByVal N As Long) As Long",
            "    On Error Resume Next",
            "    Guarded = 10 \\ N",
            "    Guarded = Guarded + 1",
            "End Function"));

        var guarded = run.Compiled["Guarded"];
        Assert.IsNull(guarded.Body);
        Assert.AreEqual(IneligibilityKind.Instruction, guarded.Reasons.Single().Kind);
        Assert.AreEqual(nameof(SDK.Semantics.Instructions.InstructionKind.OnErrorResumeNext), guarded.Reasons.Single().Detail);
    }

    [TestMethod]
    public async Task WhereValuesAreHeldBoxed_NoProcedureIsCompiled_AndEachSaysWhy()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Total()",
            "End Sub",
            "Private Function Total() As Long",
            "    Dim i As Long",
            "    For i = 1 To 10",
            "        Total = Total + i",
            "    Next",
            "End Function");
        RequireBoxedValues();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);

        var run = await RunAsync(program, VBExecutionEngine.Compiled);

        Assert.AreEqual(Observed(interpreted.Result), Observed(run.Result));
        foreach (var (name, result) in run.Compiled)
        {
            Assert.IsNull(result.Body, name);
            Assert.AreEqual(IneligibilityKind.BoxedEvaluation, result.Reasons.Single().Kind, name);
        }

        Assert.IsTrue(run.Compiled.ContainsKey("Total"), "Total was not called");
    }

    [TestMethod]
    public async Task WhereValuesAreHeldBoxed_TheStrictModeFailsTheCall()
    {
        RequireBoxedValues();
        using var strictly = CompiledExecution.Strictly();
        var run = await RunAsync(Program(
            "Public Sub Main()",
            "    Debug.Print 1 + 2",
            "End Sub"), VBExecutionEngine.Compiled);

        // the call fails as an internal error, rather than run in the interpreter.
        Assert.AreEqual(ExecutionOutcome.NotImplemented, run.Result.Outcome, Observed(run.Result));
        Assert.IsEmpty(run.Result.Output);
        Assert.AreEqual(IneligibilityKind.BoxedEvaluation, Assert.ContainsSingle(run.Compiled["Main"].Reasons).Kind);
    }

    [TestMethod]
    public async Task UnderTheStrictMode_AProcedureThatIsNotCompiled_FailsItsCall()
    {
        RequirePayloads();
        using var strictly = CompiledExecution.Strictly();
        var run = await RunAsync(Program(
            "Public Sub Main()",
            "    On Error Resume Next",
            "End Sub"), VBExecutionEngine.Compiled);

        Assert.AreEqual(ExecutionOutcome.NotImplemented, run.Result.Outcome, Observed(run.Result));
        Assert.AreEqual(IneligibilityKind.Instruction, Assert.ContainsSingle(run.Compiled["Main"].Reasons).Kind);
    }

    [TestMethod]
    public async Task CompiledCode_ReferencesNothingButTheSupportNamespace()
    {
        RequirePayloads();
        var run = await RunAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Everything(3, 4.5)",
            "End Sub",
            "Private Function Everything(ByVal L As Long, ByVal D As Double) As Double",
            "    Dim I As Integer, S As Double, C As Currency, B As Boolean",
            "    For I = 1 To 10 Step 2",
            "        S = S + L * D - I / 3",
            "        C = C + I",
            "        B = Not B Or I > L",
            "        If S > 100 Then Exit For",
            "    Next",
            "    Do While S > 1",
            "        S = S / 2",
            "    Loop",
            "    Everything = S + C - (L \\ 2) + (L Mod 2)",
            "End Function"), VBExecutionEngine.Compiled);

        var method = run.Compiled["Everything"].Body!.Run.Method;
        var references = MemberReferencesOf(method).ToList();
        Assert.IsNotEmpty(references);
        foreach (var member in references)
        {
            Assert.AreEqual(typeof(CompiledActivation).Namespace, member.DeclaringType?.Namespace, member.ToString());
        }
    }

    // every member an IL instruction of the method names: what it calls, and the fields it reads and writes.
    private static IEnumerable<MemberInfo> MemberReferencesOf(MethodInfo method)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var position = 0; position < il.Length;)
        {
            var opCode = il[position] == 0xFE ? TwoByteOpCodes[il[position + 1]] : OneByteOpCodes[il[position]];
            position += opCode.Size;
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok)
            {
                yield return method.Module.ResolveMember(BitConverter.ToInt32(il, position))!;
            }

            position += OperandSize(opCode, il, position);
        }
    }

    private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

    static CompiledExecutionTests()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var opCode = (OpCode)field.GetValue(null)!;
            if (opCode.Size == 1)
            {
                OneByteOpCodes[(ushort)opCode.Value] = opCode;
            }
            else
            {
                TwoByteOpCodes[(ushort)opCode.Value & 0xFF] = opCode;
            }
        }
    }

    private static int OperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
        _ => 4,
    };
}
