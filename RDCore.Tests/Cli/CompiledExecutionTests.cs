using RDCore.CLI.Host;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Reflection;
using System.Reflection.Emit;

namespace RDCore.Tests.Cli;

/// <summary>
/// The compiled engine (IR lowering stage 2, S2.1) against the interpreter: a program runs the same either way - what it prints, the error it raises and
/// where - and the procedures compiled code does are compiled, and the others say why they are not.
/// </summary>
[TestClass]
public sealed class CompiledExecutionTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private sealed record class Run(ExecuteSessionResult Result, IReadOnlyDictionary<string, CompilationResult> Compiled);

    private static async Task<Run> RunAsync(string program, VBExecutionEngine engine, TimeSpan? cancelAfter = null)
    {
        ExecuteSessionResult? result = null;
        IReadOnlyDictionary<string, CompilationResult> compiled = new Dictionary<string, CompilationResult>();

        // whatever engine the suite runs every session with (RDCORE_ENGINE): the comparison is of the two.
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(NoClasses, program, (provider, run, _) =>
        {
            result = run;
            compiled = provider.Image.Compiler.Results.ToDictionary(entry => entry.Procedure.Name, entry => entry.Result);
            return Task.CompletedTask;
        }, cancelAfter, engine: engine);

        return new Run(result!, compiled);
    }

    // what a run of a program is to the person who runs it, which is the same whatever engine runs it.
    private static string Observed(ExecuteSessionResult result)
        => $"{result.Outcome} | {string.Join(" ; ", result.Output.Select(line => line.Trim()))} | {result.ErrorNumber} {result.ErrorCode} {result.ErrorMessage} "
            + $"@{result.ErrorLine}:{result.ErrorCharacter} Erl {result.ErrorLineNumber} | {string.Join(" < ", result.StackTrace.Select(frame => $"{frame.Procedure}@{frame.Line}:{frame.Character}"))}";

    // the program run both ways: the compiled run is the interpreted one, and the procedures named were compiled.
    private static async Task<Run> AssertAlikeAsync(string program, params string[] compiled)
    {
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter);
        var run = await RunAsync(program, VBExecutionEngine.Compiled);

        Assert.AreEqual(Observed(interpreted.Result), Observed(run.Result));
        foreach (var name in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
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
        var run = await AssertAlikeAsync(Program(
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
        var run = await AssertAlikeAsync(Program(
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
        // 2.5 to a Long rounds to even, which no payload operation does: the interpreter is handed the activation at that Let, and runs the rest of it.
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

        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, TimeSpan.FromMilliseconds(300));
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled, TimeSpan.FromMilliseconds(300));

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
    public async Task UnderTheStrictMode_AProcedureThatIsNotCompiled_FailsItsCall()
    {
        using var strictly = CompiledExecution.Strictly();
        var run = await RunAsync(Program(
            "Public Sub Main()",
            "    On Error Resume Next",
            "End Sub"), VBExecutionEngine.Compiled);

        Assert.AreNotEqual(ExecutionOutcome.Completed, run.Result.Outcome);
    }

    [TestMethod]
    public async Task CompiledCode_ReferencesNothingButTheSupportNamespace()
    {
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
