using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// The stack trace of a run-time error locates every activation: the one the error was raised in at the statement or the expression that faulted, and each
/// of its callers at the call expression it was suspended at. Each program is run by the interpreter and compiled, and is traced alike.
/// </summary>
[TestClass]
public sealed class StackTraceLocationTests
{
    private static readonly VBExecutionEngine[] Engines = [VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled];

    private static readonly (string Name, string Source)[] NoClasses = [];

    // each activation as procedure@line:character, zero-based, then the lines of the GoSubs it had not returned from.
    private static string[] Frames(ExecuteResult result)
        => [.. result.StackTrace.Select(frame => $"{frame.Procedure}@{frame.Line}:{frame.Character}" + string.Concat(frame.ReturnLines.Select(line => $" from {line}")))];

    // the program run with each engine, the procedures named compiled in the compiled run.
    private static async Task<IReadOnlyList<(VBExecutionEngine Engine, ExecuteResult Result)>> RunAsync(
        string program, (string Name, string Source)[] classes, params string[] compiled)
    {
        CompiledExecutionTests.RequirePayloads();
        var runs = new List<(VBExecutionEngine, ExecuteResult)>();
        foreach (var engine in Engines)
        {
            using var running = CompiledExecution.Running(engine);
            await ModuleWorkspace.InspectAsync(classes, program, (provider, result, _) =>
            {
                if (engine is VBExecutionEngine.Compiled)
                {
                    var results = CompiledExecutionTests.ByName(provider);
                    foreach (var procedure in compiled)
                    {
                        Assert.IsNotNull(results.TryGetValue(procedure, out var outcome) ? outcome.Body : null,
                            $"{procedure} is not compiled: {string.Join("; ", results.Select(pair => $"{pair.Key} {string.Join(", ", pair.Value.Reasons)}"))}");
                    }
                }

                runs.Add((engine, result));
                return Task.CompletedTask;
            }, engine: engine);
        }

        return runs;
    }

    // the program of the report: the error is raised in a function called from a GoSub of its caller, and the handler of Main prints the trace.
    [TestMethod]
    public async Task ACallerInAGoSub_IsLocatedAtTheCall_ThenAtTheGoSub()
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            "    On Error GoTo CleanFail",
            "    ProcessOrders",
            "CleanExit:",
            "    Exit Sub",
            "CleanFail:",
            "    Debug.Print Err.StackTrace",
            "    Resume CleanExit",
            "End Sub",
            "Private Sub ProcessOrders()",
            "    GoSub ProcessFirstOrder",
            "    Exit Sub",
            "ProcessFirstOrder:",
            "    Debug.Print CalculateGrossMargin(42, 0)",
            "    Return",
            "End Sub",
            "Private Function CalculateGrossMargin(ByVal Revenue As Double, ByVal Cost As Double) As Double",
            "    CalculateGrossMargin = (Revenue - Cost) / Cost",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, NoClasses, "CalculateGrossMargin"))
        {
            Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, $"{engine}: {result.ErrorMessage}");
            CollectionAssert.AreEqual(
                new[] { "  at CalculateGrossMargin (18:28)", "  at ProcessOrders (14:17) from (11:5)", "  at Main (3:5)" },
                string.Join("\r\n", result.Output).Split("\r\n"),
                engine.ToString());
        }
    }

    [TestMethod]
    public async Task EveryCallerOfAThreeLevelChain_IsLocatedAtItsCall()
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            "    Dim r As Long",
            "    r = Outer(3)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Dim u As Long",
            "    u = Middle(n)",
            "    Outer = u",
            "End Function",
            "Private Function Middle(ByVal n As Long) As Long",
            "    Dim r As Long",
            "    r = Inner(n)",
            "    Middle = r + 1",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    Inner = 10 \\ (x - 3)",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, NoClasses, "Main", "Outer", "Middle", "Inner"))
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
            CollectionAssert.AreEqual(
                new[] { $"Inner@15:{result.ErrorCharacter}", "Middle@11:8", "Outer@6:8", "Main@2:8" },
                Frames(result),
                engine.ToString());
        }
    }

    [TestMethod]
    public async Task ACallerInAGoSubNestedTwoDeep_IsLocatedAtTheCall_ThenAtEachGoSub_InnermostFirst()
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            "    Caller 0",
            "End Sub",
            "Private Sub Caller(ByVal d As Long)",
            "    GoSub First",
            "    Exit Sub",
            "First:",
            "    GoSub Second",
            "    Return",
            "Second:",
            "    d = Fail(d)",
            "    Return",
            "End Sub",
            "Private Function Fail(ByVal d As Long) As Long",
            "    Dim r As Long",
            "    r = 1 \\ d",
            "    Fail = r",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, NoClasses, "Main", "Caller", "Fail"))
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
            CollectionAssert.AreEqual(
                new[] { $"Fail@15:{result.ErrorCharacter}", "Caller@10:8 from 7 from 4", "Main@1:4" },
                Frames(result),
                engine.ToString());
        }
    }

    // an error raised in Main itself, after the calls it made returned: Main is the one activation, located where the error was raised - at the expression
    // that faulted, not at the start of its statement, nor at a call it made.
    [TestMethod]
    public async Task AnErrorRaisedInMain_IsLocatedWhereItWasRaised()
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            "    Dim r As Long",
            "    Dim a As Long, b As Long",
            "    a = Twice(2)",
            "    b = Twice(1)",
            "    r = a \\ (b - 2)",
            "End Sub",
            "Private Function Twice(ByVal x As Long) As Long",
            "    Twice = x * 2",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, NoClasses, "Main", "Twice"))
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
            Assert.AreEqual(5, result.ErrorLine, engine.ToString());
            Assert.AreNotEqual(4, result.ErrorCharacter, $"{engine}: the expression that faulted, not the start of its statement");
            CollectionAssert.AreEqual(new[] { $"Main@5:{result.ErrorCharacter}" }, Frames(result), engine.ToString());
        }
    }

    // a call in the argument of another: the caller is located at the call that raised, the inner one while it runs, and the outer one once it returned.
    [TestMethod]
    [DataRow(0, "Inner", 22, DisplayName = "the inner call raises")]
    [DataRow(5, "Outer", 16, DisplayName = "the outer call raises")]
    public async Task ACallInTheArgumentOfAnother_LocatesItsCallerAtTheCallThatRaised(int argument, string raising, int character)
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            $"    Debug.Print Outer(Inner({argument}))",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Long",
            "    Outer = 10 \\ (n - 2)",
            "End Function",
            "Private Function Inner(ByVal x As Long) As Long",
            "    Inner = 10 \\ x",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, NoClasses))
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
            CollectionAssert.AreEqual(
                new[] { $"{raising}@{result.ErrorLine}:{result.ErrorCharacter}", $"Main@1:{character}" },
                Frames(result),
                engine.ToString());
        }
    }

    [TestMethod]
    public async Task ACallInAnIfCondition_LocatesItsCallerAtTheCall()
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            "    If Check(0) > 0 Then Debug.Print \"never\"",
            "End Sub",
            "Private Function Check(ByVal x As Long) As Long",
            "    Check = 1 \\ x",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, NoClasses))
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
            CollectionAssert.AreEqual(new[] { $"Check@4:{result.ErrorCharacter}", "Main@1:7" }, Frames(result), engine.ToString());
        }
    }

    private static readonly (string Name, string Source)[] Divider =
    [
        ("Divider", ModuleWorkspace.ClassModule("Divider",
            "Public Function M(ByVal x As Long) As Long",
            "    M = 1 \\ x",
            "End Function")),
    ];

    // a call of a member of an object: its caller is located at the call, which begins with the object it is made on.
    [TestMethod]
    public async Task AMemberCall_LocatesItsCallerAtTheCall()
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            "    Debug.Print Through(0)",
            "End Sub",
            "Private Function Through(ByVal x As Long) As Long",
            "    Dim o As Divider",
            "    Set o = New Divider",
            "    Through = o.M(x)",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, Divider, "Through"))
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
            CollectionAssert.AreEqual(
                new[] { $"M@{result.ErrorLine}:{result.ErrorCharacter}", "Through@6:14", "Main@1:16" },
                Frames(result),
                engine.ToString());
        }
    }

    // a function result assigned by the statement that calls it, to a Variant of the activation or to its own Variant result: the caller is located at the
    // call, the value of the statement's expression, and not at the variable assigned it.
    [TestMethod]
    [DataRow("Variant", DisplayName = "of a Variant function")]
    [DataRow("Long", DisplayName = "of a Long function")]
    public async Task ACallWhoseResultIsAssignedToAVariant_LocatesItsCallerAtTheCall(string returned)
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            "    Dim v As Variant",
            "    v = Outer(0)",
            "End Sub",
            "Private Function Outer(ByVal n As Long) As Variant",
            "    Outer = Inner(n)",
            "End Function",
            $"Private Function Inner(ByVal x As Long) As {returned}",
            "    Inner = 10 \\ x",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, NoClasses, "Main", "Outer"))
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
            CollectionAssert.AreEqual(
                new[] { $"Inner@8:{result.ErrorCharacter}", "Outer@5:12", "Main@2:8" },
                Frames(result),
                engine.ToString());
        }
    }

    // an object whose Class_Terminate raises an error and prints the trace of it.
    private static readonly (string Name, string Source)[] Raising =
    [
        ("Raising", ModuleWorkspace.ClassModule("Raising",
            "Private Sub Class_Terminate()",
            "    On Error GoTo Handler",
            "    Err.Raise 5",
            "    Exit Sub",
            "Handler:",
            "    Debug.Print Err.StackTrace",
            "End Sub")),
    ];

    // an object let go of where the activation that holds it ends - in a variable of its own, or in the collection a function returned to a For Each, which
    // is held until then however the loop ends: the activation is located where it then is, and not at the last call it made. That is nowhere, past the end
    // of its body, which an Exit Sub goes to through its End Sub.
    [TestMethod]
    public async Task AnActivationThatLetsGoOfAnObjectWhoseTerminateRaises_IsLocatedWhereItLetsGoOfIt()
    {
        var program = CompiledDirectCallTests.Program(
            "Public Sub Main()",
            "    Ends 0",
            "    Ends 1",
            "    Loops 0",
            "    Loops 1",
            "End Sub",
            "Private Sub Ends(ByVal early As Long)",
            "    Dim n As Long",
            "    Dim o As Raising",
            "    Set o = New Raising",
            "    n = Twice(early)",
            "    If early = 1 Then Exit Sub",
            "    n = n + 1",
            "End Sub",
            "Private Sub Loops(ByVal leave As Long)",
            "    Dim n As Long, v As Variant",
            "    For Each v In Made()",
            "        Set v = Nothing",
            "        n = Twice(1)",
            "        If leave = 1 Then Exit For",
            "    Next",
            "    n = n + 1",
            "End Sub",
            "Private Function Made() As Collection",
            "    Set Made = New Collection",
            "    Made.Add New Raising",
            "End Function",
            "Private Function Twice(ByVal x As Long) As Long",
            "    Twice = x * 2",
            "End Function");

        foreach (var (engine, result) in await RunAsync(program, Raising, "Ends", "Loops"))
        {
            Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, $"{engine}: {result.ErrorMessage}");
            CollectionAssert.AreEqual(
                new[]
                {
                    "  at Class_Terminate (8:5)", "  at Ends", "  at Main (2:5)",
                    "  at Class_Terminate (8:5)", "  at Ends", "  at Main (3:5)",
                    "  at Class_Terminate (8:5)", "  at Loops", "  at Main (4:5)",
                    "  at Class_Terminate (8:5)", "  at Loops", "  at Main (5:5)",
                },
                string.Join("\r\n", result.Output).Split("\r\n"),
                $"{engine}: {string.Join(" / ", result.Output.Select(line => line.Replace("\r\n", " + ")))}");
        }
    }
}
