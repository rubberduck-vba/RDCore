using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Runtime.Semantics;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Cli;

/// <summary>
/// A parameter passed by reference, and a variable compiled code passes by reference, is held in a cell: the parameter is a second name
/// for its caller's variable (MS-VBAL §5.3.1.11) whichever engine runs either, and what the interpreter, a debugger or a frame made later sees of it is what
/// compiled code wrote.
/// </summary>
[TestClass]
public sealed class CompiledByRefCallTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    // the program run to its end with engine: what it did, and what measure reads of the bodies compiled for it.
    private static async Task<(string Observed, long Measured)> RunAsync(string program, VBExecutionEngine engine, IReadOnlyList<(string Name, string Source)> classes,
        Func<EnvironmentSessionProvider, long>? measure)
    {
        (string Observed, long Measured) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes, program, (provider, run, _) =>
        {
            made = (Observed(run), engine is VBExecutionEngine.Compiled && measure is not null ? measure(provider) : 0);
            return Task.CompletedTask;
        }, engine: engine);
        return made;
    }

    // the program run both ways: the compiled run is the interpreted one.
    private static async Task<(string Observed, long Measured)> AssertAlikeAsync(string program, Func<EnvironmentSessionProvider, long>? measure = null,
        IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, classes ?? [], null);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled, classes ?? [], measure);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        return compiled;
    }

    private static Func<EnvironmentSessionProvider, long> Direct(string caller) => provider => BodyOf(provider, caller).DirectCalls;

    // a variable passed by reference from compiled code to compiled code is the parameter, both ways: the callee reads what the caller assigned, and the
    // caller what the callee did - with no frame made, in the one activation of the callee.
    [TestMethod]
    public async Task AVariablePassedByReference_IsTheParameter_WhenCompiledCodeCallsCompiledCode()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Count(100)",
            "End Sub",
            "Private Function Count(ByVal n As Long) As Long",
            "    Dim i As Long, s As Long",
            "    s = 5",
            "    For i = 1 To n",
            "        Increment s",
            "    Next",
            "    Count = s",
            "End Function",
            "Private Sub Increment(ByRef x As Long)",
            "    x = x + 1",
            "End Sub");
        var (observed, direct) = await AssertAlikeAsync(program, Direct("Count"));

        StringAssert.Contains(observed, "| 105 |");
        AssertCalls(100, (direct, 0));
        if (!CompiledExecution.HandsOverAtRandom)
        {
            var (frames, activations) = (0L, 0L);
            await RunAsync(program, VBExecutionEngine.Compiled, [], provider =>
            {
                (frames, activations) = (BodyOf(provider, "Increment").FramesMade, BodyOf(provider, "Increment").ActivationsMade);
                return 0;
            });
            Assert.AreEqual((0L, 1L), (frames, activations));
        }
    }

    // a variable passed to two parameters by reference is both (MS-VBAL §5.3.1.11): what is written through one is read through the other.
    [TestMethod]
    public async Task AVariablePassedTwice_IsBothParameters()
    {
        var (observed, direct) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            "Private Function Go() As Long",
            "    Dim n As Long",
            "    n = 3",
            "    Twice n, n",
            "    Go = n",
            "End Function",
            "Private Sub Twice(ByRef x As Long, ByRef y As Long)",
            "    x = x + 1",
            "    y = y * 2",
            "End Sub"), Direct("Go"));

        StringAssert.Contains(observed, "| 8 |");
        AssertCalls(1, (direct, 0));
    }

    // an argument that is no variable - in parentheses, an operation - is a copy the callee is given, which the caller's variable is not (MS-VBAL §5.3.1.11).
    [TestMethod]
    public async Task AnExpressionPassedByReference_IsACopyOfItsValue()
    {
        var (observed, direct) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            "Private Function Go() As Long",
            "    Dim n As Long",
            "    n = 3",
            "    Increment (n)",
            "    Increment n + 0",
            "    Go = n",
            "End Function",
            "Private Sub Increment(ByRef x As Long)",
            "    x = x + 1",
            "End Sub"), Direct("Go"));

        StringAssert.Contains(observed, "| 3 |");
        AssertCalls(2, (direct, 0));
    }

    // a parameter passed by reference, passed on by reference, is still the first caller's variable.
    [TestMethod]
    public async Task AParameterPassedByReference_PassedOn_IsStillTheCallersVariable()
    {
        var (observed, direct) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            "Private Function Go() As Long",
            "    Dim n As Long",
            "    n = 1",
            "    First n",
            "    Go = n",
            "End Function",
            "Private Sub First(ByRef x As Long)",
            "    x = x * 10",
            "    Second x",
            "End Sub",
            "Private Sub Second(ByRef y As Long)",
            "    y = y + 2",
            "End Sub"), provider => BodyOf(provider, "Go").DirectCalls + BodyOf(provider, "First").DirectCalls);

        StringAssert.Contains(observed, "| 12 |");
        AssertCalls(2, (direct, 0));
    }

    // a recursion that passes a variable on by reference: each activation, made again in its place of the stack, is given the cell of its own call.
    [TestMethod]
    public async Task ARecursion_PassingAVariableOnByReference_AddsIntoTheFirstCallersVariable()
    {
        var program = Program(
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            "Private Function Go() As Long",
            "    Dim total As Long",
            "    Add 10, total",
            "    Add 4, total",
            "    Go = total",
            "End Function",
            "Private Sub Add(ByVal n As Long, ByRef total As Long)",
            "    If n > 0 Then",
            "        total = total + n",
            "        Add n - 1, total",
            "    End If",
            "End Sub");
        var (observed, direct) = await AssertAlikeAsync(program, provider => BodyOf(provider, "Go").DirectCalls + BodyOf(provider, "Add").DirectCalls);

        StringAssert.Contains(observed, "| 65 |");
        AssertCalls(2 + 10 + 4, (direct, 0));
        if (!CompiledExecution.HandsOverAtRandom)
        {
            var activations = (await RunAsync(program, VBExecutionEngine.Compiled, [], provider => BodyOf(provider, "Add").ActivationsMade)).Measured;
            Assert.IsGreaterThanOrEqualTo(1L, activations, "Add is called directly");
            Assert.IsLessThanOrEqualTo(11L, activations, "as many as the recursion goes deep");
        }
    }

    // a callee handed to the interpreter - an operation compiled code declines, a Double let-coerced to a Long, which rounds - goes on writing its caller's
    // variable, which the caller reads when the call returns: the parameter's cell is the variable's in both engines.
    [TestMethod]
    public async Task ACalleeHandedToTheInterpreter_GoesOnWritingTheCallersVariable()
    {
        var (observed, walkedBack) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            "Private Function Go() As Long",
            "    Dim n As Long",
            "    n = 5",
            "    Halve n",
            "    Go = n * 10",
            "End Function",
            "Private Sub Halve(ByRef x As Long)",
            "    Dim d As Double",
            "    d = x / 2",
            "    x = d",
            "    x = x + 1",
            "End Sub"), provider => BodyOf(provider, "Halve").WalkBacks);

        // 5 / 2 = 2.5, rounded to even: 2; then 3.
        StringAssert.Contains(observed, "| 30 |");
        Assert.IsGreaterThanOrEqualTo(1L, walkedBack);
    }

    // an error raised after a callee wrote its parameter leaves the caller's variable written: an interpreted caller's, given to compiled code by reference,
    // which passes it on directly.
    [TestMethod]
    public async Task AnErrorAfterTheCalleeWroteTheVariable_LeavesItWritten()
    {
        var (observed, direct) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    On Error Resume Next",
            "    Dim n As Long",
            "    n = 1",
            "    First n",
            "    Debug.Print n; Err.Number",
            "End Sub",
            "Private Sub First(ByRef x As Long)",
            "    Second x",
            "End Sub",
            "Private Sub Second(ByRef y As Long)",
            "    y = 7",
            "    y = y \\ 0",
            "End Sub"), Direct("First"));

        StringAssert.Contains(observed, "| 7  11 |");
        AssertCalls(1, (direct, 0));
    }

    // a field of an object passed by reference, and Me's field: the compiled callee is given what the interpreter passes - a cell of its own holding a copy,
    // which it runs compiled on, making its call directly, or the field's address, which it is handed to the interpreter at. Either way its body is entered
    // for each call.
    [TestMethod]
    public async Task AFieldOfAnObjectPassedByReference_IsWhatTheInterpreterPasses_TheCompiledCalleeIsGiven()
    {
        var classes = new[]
        {
            ("C", ModuleWorkspace.ClassModule("C",
                "Option Explicit",
                "Public Field As Long",
                "",
                "Public Sub Bump()",
                "    Increment Me.Field",
                "End Sub")),
        };
        var (observed, entered) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim o As C",
            "    Set o = New C",
            "    o.Field = 1",
            "    Increment o.Field",
            "    o.Bump",
            "    Debug.Print o.Field",
            "End Sub",
            "Public Sub Increment(ByRef x As Long)",
            "    Add x, 1",
            "End Sub",
            "Private Sub Add(ByRef x As Long, ByVal n As Long)",
            "    x = x + n",
            "End Sub"), provider => BodyOf(provider, "Increment").DirectCalls + BodyOf(provider, "Increment").WalkBacks, classes);

        StringAssert.StartsWith(observed, "Completed |");
        if (CompiledExecution.HandsOverAtRandom)
        {
            Assert.IsGreaterThanOrEqualTo(2L, entered);
        }
        else
        {
            Assert.AreEqual(2L, entered, "Increment's compiled body is entered for each call");
        }
    }

    // an element of an array passed by reference is the element: a compiled callee given it is handed to the interpreter, which reads it where it is.
    [TestMethod]
    public async Task AnElementPassedByReference_IsTheElement()
    {
        var (observed, walkedBack) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim a(1 To 3) As Long",
            "    a(2) = 5",
            "    Increment a(2)",
            "    Debug.Print a(2)",
            "End Sub",
            "Public Sub Increment(ByRef x As Long)",
            "    x = x + 1",
            "End Sub"), provider => BodyOf(provider, "Increment").WalkBacks);

        StringAssert.Contains(observed, "| 6 |");
        Assert.IsGreaterThanOrEqualTo(1L, walkedBack, "Increment is compiled, and handed to the interpreter");
    }

    // Go passes Touch a value by reference twice, and its variable once, in each of count turns; Touch takes the address of its parameter, after the
    // statements given, if any.
    private static string Touching(string kept) => Program(
        "Public Sub Main()",
        "    Debug.Print Go(100)",
        "End Sub",
        "Private Function Go(ByVal count As Long) As Long",
        "    Dim i As Long, n As Long",
        "    For i = 1 To count",
        "        Touch (n)",
        "        Touch n + 1",
        "        Touch n",
        "    Next",
        "    Go = n",
        "End Function",
        "Private Sub Touch(ByRef x As Long)",
        kept,
        "    Dim p As LongPtr",
        "    p = VarPtr(x)",
        "    x = x + 1",
        "End Sub");

    // a value passed by reference to a callee the interpreter runs is a value, as the interpreter passes it: the callee's cell is its own, and is let go of
    // with its frame, even after the callee took its address. What is still allocated once the program ended is what the interpreter leaves. Touch counts
    // a For loop that never turns with its parameter: a loop counted with a variable held in a cell is not compiled, so the interpreter runs Touch.
    [TestMethod]
    public async Task AValuePassedByReference_ToAnInterpretedCallee_IsLetGoOfWithTheCallee()
    {
        var program = Touching("    For x = x To x - 1\r\n    Next");
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await AllocatedAfterAsync(program, VBExecutionEngine.Interpreter);
        var compiled = await AllocatedAfterAsync(program, VBExecutionEngine.Compiled);

        StringAssert.Contains(compiled.Observed, "| 100 |");
        Assert.AreEqual((false, "ForCounter x"), compiled.Touch, "Touch is not compiled, for its For loop");
        Assert.AreEqual((interpreted.Observed, interpreted.Allocated), (compiled.Observed, compiled.Allocated));
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(300L, compiled.Indirect, "Go is compiled, and calls Touch through the invoker");
        }
    }

    // a value passed by reference to a compiled callee that compiled code enters through its typed entry is a value too: the cell compiled code gives it is
    // the callee's own, and is let go of with the callee, even after the callee took its address. What is still allocated once the program ended is what
    // the interpreter leaves.
    [TestMethod]
    public async Task AValuePassedByReference_ToACompiledCallee_IsLetGoOfWithTheCallee()
    {
        var program = Touching(string.Empty);
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await AllocatedAfterAsync(program, VBExecutionEngine.Interpreter);
        var compiled = await AllocatedAfterAsync(program, VBExecutionEngine.Compiled);

        StringAssert.Contains(compiled.Observed, "| 100 |");
        Assert.IsTrue(compiled.Touch.IsCompiled, $"Touch: {compiled.Touch.Reasons}");
        Assert.AreEqual((interpreted.Observed, interpreted.Allocated), (compiled.Observed, compiled.Allocated));
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0L, compiled.Indirect, "Go calls Touch through its typed entry");
        }
    }

    // the program run to its end with engine: what it did, the bytes of the session's memory still allocated, the calls Go made through the invoker, and
    // whether Touch was compiled (and why not).
    private static async Task<(string Observed, int Allocated, long Indirect, (bool IsCompiled, string Reasons) Touch)> AllocatedAfterAsync(string program,
        VBExecutionEngine engine)
    {
        (string Observed, int Allocated, long Indirect, (bool, string) Touch) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
        {
            var touch = engine is VBExecutionEngine.Compiled ? CompiledExecutionTests.ByName(provider).GetValueOrDefault("Touch") : null;
            made = (Observed(run), provider.Session.Memory.Info.AllocatedBytes, engine is VBExecutionEngine.Compiled ? BodyOf(provider, "Go").IndirectCalls : 0,
                (touch?.Body is not null, string.Join("; ", touch?.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}") ?? [])));
            return Task.CompletedTask;
        }, engine: engine);
        return made;
    }

    // compiled code passes its variable by reference to a callee it does not compile as the interpreter does: the cell, which the callee's parameter is.
    [TestMethod]
    public async Task ACompiledCaller_PassesItsVariable_ToAnInterpretedCallee()
    {
        var (observed, indirect) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Count(10)",
            "End Sub",
            "Private Function Count(ByVal n As Long) As Long",
            "    Dim i As Long, s As Long",
            "    For i = 1 To n",
            "        Bump s",
            "    Next",
            "    Count = s",
            "End Function",
            "Private Sub Bump(ByRef x As Long)",
            "    Dim t As String",
            "    t = \"x\"",
            "    x = x + Len(t)",
            "End Sub"), provider => BodyOf(provider, "Count").IndirectCalls);

        StringAssert.Contains(observed, "| 10 |");
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(10L, indirect);
        }
    }

    // a For loop that counts with a variable it passes by reference is the interpreter's: compiled code counts in a local, which is no cell.
    [TestMethod]
    public async Task AForLoopCountingWithAVariablePassedByReference_IsNotCompiled()
    {
        CompiledExecutionTests.RequirePayloads();
        IReadOnlyList<Ineligibility> reasons = [];
        using var running = CompiledExecution.Running(VBExecutionEngine.Compiled);
        await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Debug.Print Count()",
            "End Sub",
            "Private Function Count() As Long",
            "    Dim i As Long, s As Long",
            "    For i = 1 To 6",
            "        Increment i",
            "        s = s + 1",
            "    Next",
            "    Count = s",
            "End Function",
            "Private Sub Increment(ByRef x As Long)",
            "    x = x + 1",
            "End Sub"), (provider, run, _) =>
        {
            StringAssert.Contains(Observed(run), "| 3 |");
            reasons = provider.Image.Compiler.Results.Single(result => result.Procedure.Name == "Count").Result.Reasons;
            return Task.CompletedTask;
        }, engine: VBExecutionEngine.Compiled);

        var reason = Assert.ContainsSingle(reasons);
        Assert.AreEqual((IneligibilityKind.ForCounter, "i"), (reason.Kind, reason.Detail));
    }

    // how the language server turns text into the statement the host runs: the one statement of a procedure written for the purpose.
    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
    }

    private static readonly string Waiting = Program(
        "Public Sub Main()",
        "    Debug.Print Go()",
        "End Sub",
        "Private Function Go() As Long",
        "    Dim n As Long",
        "    n = 1",
        "    Bump n",
        "    Go = n",
        "End Function",
        "Private Sub Bump(ByRef x As Long)",
        "    x = x + 1",
        "    Pause 0",
        "    x = x + 10",
        "End Sub",
        "Private Sub Pause(ByVal z As Long)",
        "    Stop",
        "End Sub");

    // the program waits below a compiled callee given a compiled caller's variable by reference: the debugger sees the variable written in both activations,
    // and what it assigns the parameter is what both go on with.
    private static async Task<string> WaitedBelowBumpAsync(VBExecutionEngine engine)
    {
        var observed = string.Empty;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Waiting, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stack = provider.Execution.Stack().Frames;
                string ValueIn(string procedure, string name)
                    => provider.Execution.Variables(stack.Single(frame => frame.Procedure == procedure).Id, DebugVariableScope.Locals, 0).Variables
                        .Single(variable => variable.Name == name).Value;

                observed = $"x={ValueIn("Bump", "x")}, n={ValueIn("Go", "n")}";
                var assigned = await new HostDebugExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>())
                    .Handle(new HostDebugExecuteParams { FrameId = stack.Single(frame => frame.Procedure == "Bump").Id, Json = PlatformJson.Serialize(Statement("x = 50")) },
                        CancellationToken.None).WaitAsync(Patience);
                Assert.IsTrue(assigned.Success, assigned.Error);
                observed += $" | n={ValueIn("Go", "n")}";
                if (engine is VBExecutionEngine.Compiled && !CompiledExecution.HandsOverAtRandom)
                {
                    Assert.AreEqual(1L, BodyOf(provider, "Go").DirectCalls, "Go calls Bump through its typed entry");
                }

                observed += " | " + Observed(await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                    .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience));
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
                }
            }
        }, debug: true, engine: engine);
        return observed;
    }

    // a byte POKEd at the address of a parameter passed by reference, while the program waits in the callee, is written into the caller's variable: the
    // address is the variable's (VarPtr), and so is what is bound there.
    private static async Task<string> PokedBelowFillAsync(VBExecutionEngine engine)
    {
        var observed = string.Empty;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            "Private Function Go() As Long",
            "    Dim n As Long",
            "    n = 1",
            "    Fill n",
            "    Go = n",
            "End Function",
            "Private Sub Fill(ByRef x As Long)",
            "    Dim p As LongPtr",
            "    p = VarPtr(x)",
            "    Stop",
            "End Sub"), async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var fill = provider.Execution.Stack().Frames.Single(frame => frame.Procedure == "Fill").Id;
                var address = int.Parse(provider.Execution.Variables(fill, DebugVariableScope.Locals, 0).Variables.Single(variable => variable.Name == "p").Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                var poked = await new HostPokeHandler(provider).Handle(new HostPokeParams { Address = address, Value = 42 }, CancellationToken.None).WaitAsync(Patience);
                Assert.IsTrue(poked.IsWritten);
                observed = Observed(await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                    .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience));
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
                }
            }
        }, debug: true, engine: engine);
        return observed;
    }

    // a byte POKEd at the address of a variable of a compiled caller - given it by a VarPtr the interpreter ran - while the program waits in a callee compiled
    // code called: the byte is written into the variable's cell at the address, which compiled code reads back after the call and goes on with.
    private static async Task<(string Observed, long WalkBacks)> PokedAboveWaitAsync(VBExecutionEngine engine)
    {
        (string Observed, long WalkBacks) observed = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Debug.Print Go()",
            "End Sub",
            "Private Function Go() As Long",
            "    Dim n As Long, p As LongLong, i As Long",
            "    n = 1",
            "    p = VarPtr(n)",
            "    Wait p",
            "    For i = 1 To 2",
            "        Go = Go + n",
            "    Next",
            "End Function",
            "Private Sub Wait(ByVal p As LongLong)",
            "    Stop",
            "End Sub"), async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var wait = provider.Execution.Stack().Frames.Single(frame => frame.Procedure == "Wait").Id;
                var address = int.Parse(provider.Execution.Variables(wait, DebugVariableScope.Locals, 0).Variables.Single(variable => variable.Name == "p").Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                var poked = await new HostPokeHandler(provider).Handle(new HostPokeParams { Address = address, Value = 42 }, CancellationToken.None).WaitAsync(Patience);
                Assert.IsTrue(poked.IsWritten);
                observed.Observed = Observed(await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                    .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience));
                observed.WalkBacks = provider.Image.Compiler.Results.SingleOrDefault(entry => entry.Procedure.Name == "Go").Result?.Body?.WalkBacks ?? -1;
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
                }
            }
        }, debug: true, engine: engine);
        return observed;
    }

    [TestMethod]
    public async Task AByteWrittenAtTheAddressOfACompiledVariable_WhileTheProgramWaitsInACallee_IsReadByCompiledCodeAfterTheCall()
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await PokedAboveWaitAsync(VBExecutionEngine.Interpreter);
        var compiled = await PokedAboveWaitAsync(VBExecutionEngine.Compiled);

        StringAssert.Contains(interpreted.Observed, "| 84 |");
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        if (!CompiledExecution.HandsOverAtRandom)
        {
            Assert.AreEqual(0, compiled.WalkBacks, "Go's walk-backs");
        }
    }

    [TestMethod]
    public async Task AByteWrittenAtTheAddressOfAParameterPassedByReference_IsWrittenIntoTheCallersVariable()
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await PokedBelowFillAsync(VBExecutionEngine.Interpreter);
        var compiled = await PokedBelowFillAsync(VBExecutionEngine.Compiled);

        StringAssert.Contains(interpreted, "| 42 |");
        Assert.AreEqual(interpreted, compiled);
    }

    [TestMethod]
    public async Task AParameterPassedByReference_IsTheCallersVariable_ToADebugger()
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await WaitedBelowBumpAsync(VBExecutionEngine.Interpreter);
        var compiled = await WaitedBelowBumpAsync(VBExecutionEngine.Compiled);

        Assert.AreEqual(interpreted, compiled);
        StringAssert.Contains(compiled, "x=2, n=2 | n=50");
        StringAssert.Contains(compiled, "| 60 |");
    }
}
