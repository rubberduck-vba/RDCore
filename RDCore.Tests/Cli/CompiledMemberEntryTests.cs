using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Services.VerboseMessages;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A member of a class compiled code calls on an object, entered without a frame: its activation holds <c>Me</c> as the invoker
/// holds it, by a cell of its own, and finds the fields of <c>Me</c> on the object; the frame it is given once something is to see it is the one the invoker
/// would have made. What the program does (which object a call is made on, when an object terminates, what the interpreter, an error or a debugger sees)
/// is what it does when the interpreter makes the call.
/// </summary>
[TestClass]
public sealed class CompiledMemberEntryTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    // Walk calls Relay, a procedure of the module that calls Walk on another object, which it holds as Second: two activations of Walk on the stack, the one
    // below given its frame by the call of Relay, each on its own object - and, a call deeper, two on the same object. Down and Up call each other on Me,
    // which the interpreter does. Each folds its object's fields into what it returns, and the object says what it held when it terminates.
    private static readonly (string Name, string Source)[] Nodes =
    [
        ("Node", ModuleWorkspace.ClassModule("Node", "Option Explicit",
            "Public Id As Long",
            "Private mTrace As Long",
            "Public Function Walk(ByVal N As Long) As Long",
            "    mTrace = mTrace * 10 + N",
            "    If N > 0 Then Relay N - 1",
            "    Walk = Id * 1000 + mTrace",
            "End Function",
            "Public Function Down(ByVal N As Long) As Long",
            "    mTrace = mTrace * 3 + N",
            "    If N > 0 Then Down = Up(N - 1) * 2 + N Else Down = mTrace",
            "End Function",
            "Public Function Up(ByVal N As Long) As Long",
            "    Up = Down(N) + Id",
            "End Function",
            "Private Sub Class_Terminate()",
            "    Debug.Print \"gone\"; Id; mTrace",
            "End Sub")),
    ];

    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine,
        IReadOnlyList<(string Name, string Source)> classes)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes, program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine);
        return made;
    }

    // the program run both ways: the compiled run is the interpreted one - what it printed, in order, the error it raised, where, and its stack trace.
    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> AssertAlikeAsync(string program,
        IReadOnlyList<(string Name, string Source)> classes)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, classes);
        var compiled = await RunAsync(program, VBExecutionEngine.Compiled, classes);
        Assert.AreEqual(interpreted.Observed, compiled.Observed);
        return compiled;
    }

    // the body of the procedure named, compiled.
    private static CompiledBody Compiled(IReadOnlyDictionary<string, CompilationResult> compiled, string name)
    {
        Assert.IsTrue(compiled.TryGetValue(name, out var result), $"{name} was not called");
        return result.Body ?? throw new AssertFailedException($"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
    }

    // what the caller called directly, through each member's typed entry, and through a binding; and how many activations of the member were given a frame
    // after it was entered - by a call of the interpreter's, a statement it ran, an object terminating - none of the others. Under the random walk-back any
    // activation may be handed to the interpreter, which makes the calls after.
    private static void AssertEntered(IReadOnlyDictionary<string, CompilationResult> compiled, string caller, long direct, string member, long framesMade)
    {
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        var called = Compiled(compiled, caller);
        Assert.AreEqual((direct, 0L), (called.DirectCalls, called.IndirectCalls), $"{caller}'s calls made directly, and through a binding");
        Assert.AreEqual(framesMade, Compiled(compiled, member).FramesMade, $"the activations of {member} given a frame after they were entered");
    }

    // the lines the program printed, in order: each named one after the one before.
    private static void AssertInOrder(string observed, params string[] lines)
    {
        var at = 0;
        foreach (var line in lines)
        {
            var found = observed.IndexOf(line, at, StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, found, $"'{line}' after what came before it, in: {observed}");
            at = found + line.Length;
        }
    }

    // each activation of Walk holds its own object: the one below the call of Relay is still on the first object when the calls above it - on the second
    // object, twice - return, and lets go of it then. The second terminates when the module lets go of it; the first when its variable goes.
    [TestMethod]
    public async Task EachActivationOfAMember_HoldsItsOwnObject_OnTheSameObjectAndOnAnother()
    {
        var (observed, compiled) = await AssertAlikeAsync(Program(
            "Public Second As Node",
            "Public Sub Main()",
            "    Debug.Print Run()",
            "    Debug.Print \"after\"",
            "End Sub",
            "Private Function Run() As Long",
            "    Dim A As Node",
            "    Set A = New Node",
            "    A.Id = 1",
            "    Set Second = New Node",
            "    Second.Id = 2",
            "    Run = A.Walk(2)",
            "    Set Second = Nothing",
            "    Debug.Print \"released\"",
            "End Function",
            "Public Sub Relay(ByVal N As Long)",
            "    Dim B As Node",
            "    Dim V As Long",
            "    Set B = Second",
            "    V = B.Walk(N)",
            "    Debug.Print \"relayed\"; N; V",
            "End Sub"), Nodes);

        AssertInOrder(observed, "relayed 0", "relayed 1", "gone 2", "released", "gone 1", "1002", "after");
        AssertEntered(compiled, "Run", direct: 1, "Walk", framesMade: 2);
        AssertEntered(compiled, "Relay", direct: 2, "Walk", framesMade: 2);
    }

    // Down calls Up on Me, which calls Down on Me again: the interpreter makes those calls, each with its frame, above the activation compiled code entered
    // without one, which holds the object until it returns. The object terminates when its variable lets go of it, and not before.
    [TestMethod]
    public async Task AMemberCallingBackIntoItsObject_HoldsTheObject_UntilItReturns()
    {
        var (observed, compiled) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bounced()",
            "End Sub",
            "Private Function Bounced() As Long",
            "    Dim A As Node",
            "    Set A = New Node",
            "    A.Id = 5",
            "    Bounced = A.Down(3)",
            "    Set A = Nothing",
            "    Debug.Print \"released\"",
            "End Function",
            "Public Sub Relay(ByVal N As Long)",
            "End Sub"), Nodes);

        AssertInOrder(observed, "gone 5", "released");
        AssertEntered(compiled, "Bounced", direct: 1, "Down", framesMade: 1);
    }

    // Through's object is the module's Keeper, by reference: Drop lets go of Keeper, which was the object's last reference but Me. The object lives until Drop
    // returns, and terminates then - before Through goes on.
    [TestMethod]
    public async Task AnObjectWhoseLastReferenceGoesInAMember_TerminatesWhenTheMemberReturns()
    {
        var (observed, compiled) = await AssertAlikeAsync(Program(
            "Public Keeper As Kept",
            "Public Sub Main()",
            "    Set Keeper = New Kept",
            "    Keeper.Id = 7",
            "    Through Keeper",
            "    Debug.Print \"after\"; Keeper Is Nothing",
            "End Sub",
            "Private Sub Through(ByRef T As Kept)",
            "    T.Drop 3",
            "    Debug.Print \"returned\"; T Is Nothing",
            "End Sub"), [("Kept", ModuleWorkspace.ClassModule("Kept", "Option Explicit",
                "Public Id As Long",
                "Public Sub Drop(ByVal N As Long)",
                "    Id = Id * 10 + N",
                "    Set Keeper = Nothing",
                "    Id = Id * 10 + 1",
                "    Debug.Print \"still here\"; Id",
                "End Sub",
                "Private Sub Class_Terminate()",
                "    Debug.Print \"gone\"; Id",
                "End Sub"))]);

        AssertInOrder(observed, "still here", "gone", "returned", "after");
        AssertEntered(compiled, "Through", direct: 1, "Drop", framesMade: 1);
    }

    // each pass makes a new object, and the one before terminates - its fields freed - as it is made: the new one's fields are its own, and start empty.
    [TestMethod]
    public async Task ANewObject_MadeAsTheOneBeforeTerminates_HasFieldsOfItsOwn()
    {
        var (observed, compiled) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Fresh()",
            "End Sub",
            "Private Function Fresh() As Long",
            "    Dim T As Tally",
            "    Dim I As Long",
            "    Dim V As Long",
            "    For I = 1 To 4",
            "        Set T = New Tally",
            "        T.Add I",
            "        T.Add 1",
            "        V = T.Total()",
            "        Fresh = Fresh * 100 + V",
            "    Next",
            "End Function"), Tallies);

        StringAssert.Contains(observed, "| 4071013 |");
        AssertEntered(compiled, "Fresh", direct: 12, "Add", framesMade: 0);
    }

    // two objects of one class, called in turn without anything allocated between the calls: each call finds the fields of the object it is made on.
    [TestMethod]
    public async Task TwoObjectsOfOneClass_CalledInTurn_EachHaveTheirOwnFields()
    {
        var (_, compiled) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print InTurn()",
            "End Sub",
            "Private Function InTurn() As Long",
            "    Dim A As Tally",
            "    Dim B As Tally",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set A = New Tally",
            "    Set B = New Tally",
            "    For I = 1 To 4",
            "        A.Add I",
            "        B.Add I + 1",
            "        V = A.Total()",
            "        InTurn = InTurn * 7 + V",
            "        V = B.Twice(I)",
            "        InTurn = InTurn * 3 + V",
            "    Next",
            "End Function"), Tallies);

        AssertEntered(compiled, "InTurn", direct: 16, "Add", framesMade: 0);
    }

    // what the caller called through a binding, the object given as Me (CompiledActivation.CallMember) - each a call of the member named, which is not
    // compiled - and nothing directly.
    private static void AssertCalledThroughBinding(IReadOnlyDictionary<string, CompilationResult> compiled, string caller, long indirect, string member)
    {
        Assert.IsTrue(compiled.TryGetValue(member, out var result) && result.Body is null, $"{member} is compiled, or was not called");
        if (CompiledExecution.HandsOverAtRandom)
        {
            return;
        }

        var called = Compiled(compiled, caller);
        Assert.AreEqual((0L, indirect), (called.DirectCalls, called.IndirectCalls), $"{caller}'s calls made directly, and through a binding");
    }

    // a member whose one statement is a CallByName, late-bound and so the interpreter's, which leaves it nothing compiled code does: it is not compiled,
    // and compiled code calls it through its binding, on the object the variable refers to, as the interpreter does. The member it calls by name, on Me,
    // works on the object's field.
    private static string LateBound(string name) => string.Join("\r\n",
        $"Private Function {name}(ByVal N As Long) As Long",
        $"    {name} = CallByName(Me, \"Raised\", VbMethod, N)",
        "End Function",
        "Public Function Raised(ByVal N As Long) As Long",
        "    mTotal = mTotal * 3 + N",
        "    If N > 2 Then Raised = mTotal + 1000 Else Raised = mTotal",
        "End Function");

    // the member that is not compiled is called through its binding each time, and its fields are the object's, from call to call.
    [TestMethod]
    public async Task AMemberThatIsNotCompiled_IsCalledThroughItsBinding_OnTheObject()
    {
        var (observed, compiled) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bound()",
            "End Sub",
            "Private Function Bound() As Long",
            "    Dim J As Jumper",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set J = New Jumper",
            "    For I = 1 To 4",
            "        V = J.Scaled(I)",
            "        Bound = Bound * 3 + V",
            "    Next",
            "End Function"), [("Jumper", ModuleWorkspace.ClassModule("Jumper", "Option Explicit",
                "Private mTotal As Long",
                LateBound("Scaled").Replace("Private Function Scaled", "Public Function Scaled")))]);

        StringAssert.Contains(observed, "| 4184 |");
        AssertCalledThroughBinding(compiled, "Bound", indirect: 4, "Scaled");
    }

    // a variable of an interface, the member implementing it not compiled: the call is the implementing member's, through its binding.
    [TestMethod]
    public async Task AnImplementingMemberThatIsNotCompiled_IsCalledThroughItsBinding_OnTheObject()
    {
        var (observed, compiled) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Bound()",
            "End Sub",
            "Private Function Bound() As Long",
            "    Dim S As IShape",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set S = New Square",
            "    For I = 1 To 4",
            "        V = S.Area(I)",
            "        Bound = Bound * 3 + V",
            "    Next",
            "End Function"), [
                ("IShape", ModuleWorkspace.ClassModule("IShape", "Option Explicit",
                    "Public Function Area(ByVal N As Long) As Long",
                    "End Function")),
                ("Square", ModuleWorkspace.ClassModule("Square", "Option Explicit",
                    "Implements IShape",
                    "Private mTotal As Long",
                    LateBound("IShape_Area")))]);

        StringAssert.Contains(observed, "| 4184 |");
        AssertCalledThroughBinding(compiled, "Bound", indirect: 4, "IShape_Area");
    }

    [TestMethod]
    public async Task AnObjectAMemberWasCalledOn_IsCollected_OnceNothingHoldsIt()
    {
        CompiledExecutionTests.RequirePayloads();
        // the second run lets go of the object, then stops: the program is still on the stack - with what its compiled code keeps for its next call.
        var program = Program(
            "Private Kept As Tally",
            "Public Sub Main()",
            "    Dim Local As Tally",
            "    If Kept Is Nothing Then",
            "        Set Kept = New Tally",
            "        Debug.Print Once(Kept)",
            "    Else",
            "        Set Local = Kept",
            "        Set Kept = Nothing",
            "        Debug.Print Once(Local)",
            "        Set Local = Nothing",
            "        Debug.Print \"released\"",
            "        Stop",
            "    End If",
            "End Sub",
            "Private Function Once(ByVal T As Tally) As Long",
            "    T.Add 5",
            "    Once = T.Total()",
            "End Function");

        // compiled only: what the interpreter keeps is not in question, and anything the test itself held the object by would fail the row, not pass it.
        using var running = CompiledExecution.Running(VBExecutionEngine.Compiled);
        await ModuleWorkspace.InspectAsync(Tallies, program, async (provider, first, runAgain) =>
        {
            Assert.AreEqual("Completed | 5", string.Join(" | ", first.Outcome, string.Join(" ; ", first.Output.Select(line => line.Trim()))), first.ErrorMessage);
            var kept = WeakInstanceOf(provider, "Kept");
            var second = await runAgain();
            Assert.AreEqual("Interrupted | 20 ; released", string.Join(" | ", second.Outcome, string.Join(" ; ", second.Output.Select(line => line.Trim()))),
                second.ErrorMessage);
            Assert.IsGreaterThan(0, provider.Session.CallStack.Depth, "the program is stopped, not ended");

            // the session, every compiled body in it, and the program's stack are still alive: only what held the object let go of it.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.IsFalse(kept.IsAlive, "the object is held after the program let go of it");
            // the second run's: each run compiles for its own scopes.
            AssertEntered(CompiledExecutionTests.ByName(provider), "Once", direct: 2, "Add", framesMade: 0);
        }, engine: VBExecutionEngine.Compiled);
    }

    // the object the module variable named holds, which the test does not hold: made out of the caller's frame, so that nothing of it is left there.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference WeakInstanceOf(EnvironmentSessionProvider provider, string name)
    {
        var symbols = provider.Session.Symbols;
        Assert.IsTrue(symbols.TryGetModule("Program", out var module));
        var field = symbols.MembersOf(module.Uri).OfType<RDCore.SDK.Model.Symbols.VBProject.VBModuleFieldVariableMemberSymbol>().Single(member => member.Name == name);
        Assert.IsTrue(ObjectOperations.TryRead(symbols.Resolver.GetValue(field), out var id), $"{name} holds no object");
        Assert.IsTrue(symbols.TryGetInstance(id, out var instance), $"{name} holds an object the session has not");
        return new WeakReference(instance);
    }

    private static readonly (string Name, string Source)[] Tallies =
    [
        ("Tally", ModuleWorkspace.ClassModule("Tally", "Option Explicit",
            "Private mTotal As Long",
            "Public Sub Add(ByVal N As Long)",
            "    mTotal = mTotal * 3 + N",
            "End Sub",
            "Public Function Total() As Long",
            "    Total = mTotal",
            "End Function",
            "Public Function Twice(ByVal N As Long) As Long",
            "    Twice = N * 2 + mTotal",
            "End Function")),
    ];

    // the bytes of a field written by the interpreter, at the field's address, in the middle of a member: compiled code reads the field after it as it now is.
    [TestMethod]
    public async Task AFieldWrittenAtItsAddressInAMember_IsReadByCompiledCodeAfter()
    {
        var (_, compiled) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Poked()",
            "End Sub",
            "Private Function Poked() As Long",
            "    Dim T As Writer",
            "    Dim I As Long",
            "    Dim V As Long",
            "    Set T = New Writer",
            "    For I = 1 To 3",
            "        V = T.Bump(I)",
            "        Poked = Poked * 10 + V Mod 10",
            "    Next",
            "End Function"), [("Writer", ModuleWorkspace.ClassModule("Writer", "Option Explicit",
                "Private Declare PtrSafe Sub CopyMemory Lib \"kernel32\" Alias \"RtlMoveMemory\" (Destination As Any, Source As Any, ByVal Length As LongPtr)",
                "Private mTotal As Long",
                "Public Function Bump(ByVal N As Long) As Long",
                "    Dim V As Long",
                "    mTotal = mTotal * 10 + N",
                "    V = mTotal + 3",
                "    CopyMemory mTotal, V, 4",
                "    mTotal = mTotal * 2",
                "    Bump = mTotal",
                "End Function"))]);

        AssertEntered(compiled, "Poked", direct: 3, "Bump", framesMade: 3);
    }

    // an error a member does not handle is its caller's, where the call is: the stack it is raised on has the member's activation, made as the interpreter
    // makes it, and the handler is the caller's.
    [TestMethod]
    public async Task AnErrorAMemberDoesNotHandle_IsRaisedOnTheStackTheInterpreterHas()
    {
        var (observed, compiled) = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Divided()",
            "    Debug.Print Unhandled()",
            "End Sub",
            "Private Function Divided() As Long",
            "    Dim T As Divider",
            "    Dim I As Long",
            "    On Error GoTo Failed",
            "    Set T = New Divider",
            "    For I = 2 To 0 Step -1",
            "        T.Divide I",
            "    Next",
            "    Exit Function",
            "Failed:",
            "    Divided = T.Total() * 100 + Err.Number",
            "End Function",
            "Private Function Unhandled() As Long",
            "    Dim T As Divider",
            "    Set T = New Divider",
            "    T.Divide 0",
            "End Function"), [("Divider", ModuleWorkspace.ClassModule("Divider", "Option Explicit",
                "Private mTotal As Long",
                "Public Sub Divide(ByVal N As Long)",
                "    mTotal = mTotal + 12 \\ N",
                "End Sub",
                "Public Function Total() As Long",
                "    Total = mTotal",
                "End Function",
                "Private Sub Class_Terminate()",
                "    Debug.Print \"gone\"; mTotal",
                "End Sub"))]);

        StringAssert.Contains(observed, "Divide@");
        AssertEntered(compiled, "Divided", direct: 3, "Divide", framesMade: 2);
    }

    // a Stop in a member, with no debugger: the program is stopped where the interpreter stops it.
    [TestMethod]
    public async Task AStopInAMember_StopsTheProgramWhereTheInterpreterStopsIt()
        => _ = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim T As Tally",
            "    Set T = New Tally",
            "    Debug.Print Stopped(T)",
            "End Sub",
            "Private Function Stopped(ByVal T As Tally) As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        T.Add I",
            "    Next",
            "    Stopped = T.Total()",
            "End Function"), [("Tally", ModuleWorkspace.ClassModule("Tally", "Option Explicit",
                "Private mTotal As Long",
                "Public Sub Add(ByVal N As Long)",
                "    mTotal = mTotal * 3 + N",
                "    If N = 2 Then Stop",
                "End Sub",
                "Public Function Total() As Long",
                "    Total = mTotal",
                "End Function"))]);

    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
    }

    // Counted calls Add on a Counter, which waits at its Stop in the second call, under a debugger: a call compiled code makes of a member under a debugger
    // is made with its frame, as the invoker makes it. The debugger sees the stack, and the fields of Me, as the interpreter has them; it assigns a field in
    // the member's frame, or the class is edited to have another field; and the program goes on with what was assigned, to its end.
    private static readonly string Counting = Program(
        "Public Sub Main()",
        "    Debug.Print Counted()",
        "End Sub",
        "Private Function Counted() As Long",
        "    Dim C As Counter",
        "    Dim I As Long",
        "    Set C = New Counter",
        "    For I = 1 To 3",
        "        C.Add I",
        "    Next",
        "    Counted = C.Total()",
        "End Function");

    private static string Counter(params string[] fields) => ModuleWorkspace.ClassModule("Counter", ["Option Explicit",
        "Private mTotal As Long",
        .. fields,
        "Public Sub Add(ByVal N As Long)",
        "    mTotal = mTotal * 3 + N",
        "    If N = 2 Then Stop",
        "    mTotal = mTotal + 1",
        "End Sub",
        "Public Function Total() As Long",
        "    Total = mTotal",
        "End Function"]);

    private static async Task<string> WaitedInMemberAsync(VBExecutionEngine engine, string? statement, string? edited, long direct)
    {
        var observed = string.Empty;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([("Counter", Counter())], Counting, async (provider, first, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                var stack = provider.Execution.Stack().Frames;
                var add = stack.Single(frame => frame.Procedure == "Add").Id;
                observed = string.Join(" < ", stack.Select(frame => $"{frame.Procedure}@{frame.Line}")) + " | "
                    + string.Join(", ", provider.Execution.Variables(add, DebugVariableScope.Locals, 0).Variables.Select(variable => $"{variable.Name}={variable.Value}"));
                if (statement is not null)
                {
                    var made = await new HostDebugExecuteHandler(provider, Substitute.For<IVerboseMessageBuilder>())
                        .Handle(new HostDebugExecuteParams { FrameId = add, Json = PlatformJson.Serialize(Statement(statement)) }, CancellationToken.None).WaitAsync(Patience);
                    Assert.IsTrue(made.Success, made.Error);
                }

                if (edited is not null)
                {
                    Assert.IsEmpty(await ModuleWorkspace.ReloadOnlyAsync(provider, ["Counter"], ("Counter", ModuleType.ClassModule, edited),
                        ("Program", ModuleType.StdModule, Counting)));
                }

                observed += " | " + Observed(await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                    .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience));
                if (engine is VBExecutionEngine.Compiled && !CompiledExecution.HandsOverAtRandom)
                {
                    Assert.AreEqual(0L, BodyOf(provider, "Add").FramesMade, "Add is entered with its frame under a debugger");
                    var counted = BodyOf(provider, "Counted");
                    Assert.AreEqual((direct, 0L), (counted.DirectCalls, counted.IndirectCalls), "Counted's calls made directly, and through a binding, under a debugger");
                }
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

    // Counted makes its 3 calls of Add and its call of Total directly: 4. When the class is edited, the symbols are defined again while Add waits, and the
    // interpreter goes on with Counted's activation once Add returns: the 2 calls of Add before it are the ones made directly.
    [TestMethod]
    [DataRow(null, null, 4L, DisplayName = "the program resumed")]
    [DataRow("mTotal = 100", null, 4L, DisplayName = "a field of Me assigned in the member's frame")]
    [DataRow(null, "Private mExtra As Long", 2L, DisplayName = "a field added to the class")]
    public async Task AMemberWaitedInUnderADebugger_IsWhatTheInterpreterHas(string? statement, string? field, long direct)
    {
        CompiledExecutionTests.RequirePayloads();
        var edited = field is null ? null : Counter(field);
        var interpreted = await WaitedInMemberAsync(VBExecutionEngine.Interpreter, statement, edited, direct);
        var compiled = await WaitedInMemberAsync(VBExecutionEngine.Compiled, statement, edited, direct);
        Assert.AreEqual(interpreted, compiled);
    }
}
