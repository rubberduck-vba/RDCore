using RDCore.Runtime.Compiled;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// The stack trace of a run-time error locates every activation: the one the error was raised in at the statement or the expression that faulted, and each
/// of its callers at the statement it was suspended at, the one that made the call. Each program is run by the interpreter and compiled, and is traced alike.
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
                new[] { "  at CalculateGrossMargin (18:28)", "  at ProcessOrders (14:5) from (11:5)", "  at Main (3:5)" },
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
                new[] { $"Inner@15:{result.ErrorCharacter}", "Middle@11:4", "Outer@6:4", "Main@2:4" },
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
            "    Fail d",
            "    Return",
            "End Sub",
            "Private Sub Fail(ByVal d As Long)",
            "    Dim r As Long",
            "    r = 1 \\ d",
            "End Sub");

        foreach (var (engine, result) in await RunAsync(program, NoClasses, "Main", "Caller", "Fail"))
        {
            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, engine.ToString());
            CollectionAssert.AreEqual(
                new[] { $"Fail@15:{result.ErrorCharacter}", "Caller@10:4 from 7 from 4", "Main@1:4" },
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
}
