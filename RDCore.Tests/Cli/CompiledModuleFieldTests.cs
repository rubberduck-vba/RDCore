using Microsoft.Extensions.Logging.Abstractions;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Runtime.Compiled;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;
using RDCore.SDK.Platform.Components;

namespace RDCore.Tests.Cli;

/// <summary>
/// A field of a standard module of a scalar type, compiled: held in the cell the session holds it in, found where the interpreter finds
/// it, whichever module names it and whatever activation is current, and read and written there by compiled code as a variable held in a cell is. What the
/// interpreter or another procedure does of the field, it does in that cell too. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledModuleFieldTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    // the program run with engine, then once again after each of between, in the same session: what each run did, in order.
    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine,
        IReadOnlyList<(string Name, string Source)> modules, IReadOnlyList<(string Name, string Source)> classes, IReadOnlyList<Func<EnvironmentSessionProvider, Task>> between)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes, program, async (provider, run, again) =>
        {
            var observed = new List<string> { Observed(run) };
            foreach (var step in between)
            {
                await step(provider);
                observed.Add(Observed(await again()));
            }

            made = (string.Join(" || ", observed), CompiledExecutionTests.ByName(provider));
        }, engine: engine, modules: modules);
        return made;
    }

    // the program run both ways: what it printed, the error it raised and where, alike - in each run, when it is run again after each of between; and each
    // procedure named compiled, run in compiled code with as many statements stepped as recoveries says, over every run - to its end, unless walkBacks says
    // it was handed to the interpreter that many times.
    internal static async Task AssertAlikeAsync(string program, (string Name, long Recoveries)[] compiled, IReadOnlyList<(string Name, string Source)>? modules = null,
        IReadOnlyList<(string Name, string Source)>? classes = null, IReadOnlyList<Func<EnvironmentSessionProvider, Task>>? between = null, long walkBacks = 0)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, modules ?? [], classes ?? [], between ?? []);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, modules ?? [], classes ?? [], between ?? []);
        Assert.AreEqual(interpreted.Observed, run.Observed);

        foreach (var (name, recoveries) in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(walkBacks, result.Body.WalkBacks, $"{name}'s walk-backs");
                Assert.AreEqual(recoveries, result.Body.Recoveries, $"{name}'s recoveries");
            }
        }
    }

    private static (string Name, string Source)[] Store(params string[] members)
        => [("Store", ModuleWorkspace.StandardModule("Store", ["Option Explicit", "Public Total As Long", "Public Half As Double", "Private mCount As Integer", .. members]))];

    // what one module's compiled code writes in a field of another, the other's compiled code reads, and the interpreter too.
    [TestMethod]
    public async Task AFieldWrittenInOneModule_IsReadInAnother()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Fill 4",
            "    Debug.Print Read(); Total",
            "    Fill 2",
            "    Debug.Print Read(); Total; Half",
            "End Sub",
            "Private Function Read() As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Read = Read + Total",
            "    Next",
            "End Function"), [("Fill", 0), ("Read", 0)], modules: Store(
                "Public Sub Fill(ByVal N As Long)",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Total = Total + I",
                "        Half = Half + I / 2",
                "    Next",
                "End Sub"));

    // a field private to its module, of the module whose procedure names it: another module's field of the same name is that module's own.
    [TestMethod]
    public async Task APrivateFieldOfTheModule_IsKeptFromCallToCall()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted(3); Tens(2); Counted(4); Tens(1); Counted(0)",
            "End Sub"), [("Counted", 0), ("Tens", 0)], modules: [.. Store(
                "Public Function Counted(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        mCount = mCount + 1",
                "    Next",
                "    Counted = mCount",
                "End Function"), ("Other", ModuleWorkspace.StandardModule("Other", ["Option Explicit", "Private mCount As Integer",
                "Public Function Tens(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        mCount = mCount + 10",
                "    Next",
                "    Tens = mCount",
                "End Function"]))]);

    // a member of a class entered without a frame, on an object, reads the field the session holds, not one of its object's.
    [TestMethod]
    public async Task AFieldOfAStandardModule_IsTheSessions_InAMemberOfAClass()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim C As New Adder",
            "    Fill 3",
            "    Debug.Print C.Added(2); Total",
            "    Debug.Print C.Added(1); Total",
            "End Sub"), [("Added", 0), ("Fill", 0)], modules: Store(
                "Public Sub Fill(ByVal N As Long)",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Total = Total + I",
                "    Next",
                "End Sub"), classes: [("Adder", ModuleWorkspace.ClassModule("Adder", "Option Explicit", "Private mOwn As Long",
                "Public Function Added(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        Total = Total + 10",
                "        mOwn = mOwn + 1",
                "    Next",
                "    Added = mOwn * 1000 + Total",
                "End Function"))]);

    // what the interpreter writes in the field at a statement it runs alone is what compiled code reads after it, and the other way round.
    [TestMethod]
    public async Task AStatementTheInterpreterRuns_ReadsAndWritesTheFieldsCell()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Shown 3",
            "End Sub",
            "Private Sub Shown(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Total = Total + I",
            "        Debug.Print Total",
            "        Total = Total * 2",
            "    Next",
            "End Sub"), [("Shown", 3)], modules: Store());

    // a field passed by reference is passed as its cell: what the callee assigns its parameter, it assigns the field.
    [TestMethod]
    public async Task AFieldPassedByReference_IsAssignedByTheCallee()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Total = 1",
            "    Doubled 4",
            "    Debug.Print Total",
            "End Sub",
            "Private Sub Doubled(ByVal N As Long)",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Twice Total",
            "    Next",
            "End Sub",
            "Private Sub Twice(ByRef X As Long)",
            "    X = X * 2",
            "End Sub"), [("Doubled", 0), ("Twice", 0)], modules: Store());

    // the bytes of the field written at its address between two of compiled code's statements, by a procedure of the module it is a field of: what compiled
    // code reads after it is what the field now holds.
    [TestMethod]
    public async Task AFieldWrittenAtItsAddress_IsReadByCompiledCodeAfter()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed(4)",
            "End Sub",
            "Private Function Summed(ByVal N As Long) As Long",
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Total = Total + I",
            "        Poke Total * 3",
            "        Summed = Summed * 100 + Total Mod 100",
            "    Next",
            "End Function"), [("Summed", 0)], modules: Store(
                $"Private Declare PtrSafe Sub CopyMemory Lib \"{CompiledMemberEntryTests.CLibrary}\" Alias \"memmove\" (Destination As Any, Source As Any, ByVal Length As LongPtr)",
                "Public Sub Poke(ByVal V As Long)",
                "    CopyMemory Total, V, 4",
                "End Sub"));

    // the module that has the field edited between two runs - a field declared above it, which gives the module's fields their storage again: a procedure of
    // another module, whose compiled body is kept, finds the field's cell again, and reads and writes what the field now holds.
    [TestMethod]
    public async Task AFieldWhoseModuleIsEditedBetweenTwoRuns_IsFoundAgain()
    {
        string[] reading =
        [
            "Public Sub Main()",
            "    Fill 3",
            "    Debug.Print Read(); Total",
            "End Sub",
            "Private Function Read() As Long",
            "    Dim I As Long",
            "    For I = 1 To 3",
            "        Read = Read + Total",
            "        Total = Total + 1",
            "    Next",
            "End Function",
        ];
        string[] fill = ["Public Sub Fill(ByVal N As Long)", "    Total = Total + N", "End Sub"];
        var edited = ModuleWorkspace.StandardModule("Store", ["Option Explicit", "Public Extra As Long", "Public Total As Long", "Public Half As Double",
            "Private mCount As Integer", .. fill]);
        await AssertAlikeAsync(Program(reading), [("Read", 0)], modules: Store(fill), between:
        [
            async provider => _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Store"], ("Store", ModuleType.StdModule, edited),
                ("Program", ModuleType.StdModule, Program(reading))),
        ]);
    }

    private static readonly string Waiting = Program(
        "Public Sub Main()",
        "    Dim K As Long",
        "    For K = 1 To 3",
        "        Fill K",
        "        Debug.Print Read(); Total",
        "    Next",
        "End Sub",
        "Private Function Read() As Long",
        "    Dim I As Long",
        "    For I = 1 To 3",
        "        Read = Read + Total",
        "        Total = Total + 1",
        "    Next",
        "End Function");

    private static string Filling(params string[] fields) => ModuleWorkspace.StandardModule("Store", ["Option Explicit", .. fields, "Public Total As Long",
        "Public Sub Fill(ByVal N As Long)", "    Total = Total + N", "    If N = 2 Then Stop", "End Sub"]);

    // the program stopped in the module that has the field, the module edited while it waits - a field declared above it - and the program resumed: the
    // procedure of the other module, called again after, reads and writes what the field now holds.
    private static async Task<string> EditedWhileWaitingAsync(VBExecutionEngine engine)
    {
        var observed = string.Empty;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync([], Waiting, async (provider, first, again) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, first.Outcome, first.ErrorMessage);
            try
            {
                Assert.IsEmpty(await ModuleWorkspace.ReloadOnlyAsync(provider, ["Store"], ("Store", ModuleType.StdModule, Filling("Public Extra As Long")),
                    ("Program", ModuleType.StdModule, Waiting)));
                observed = Observed(first) + " || " + Observed(await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                    .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience));
                if (engine is VBExecutionEngine.Compiled)
                {
                    _ = BodyOf(provider, "Read");
                }
            }
            finally
            {
                if (provider.Execution.State is not ProgramState.Idle)
                {
                    _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);
                }
            }
        }, debug: true, engine: engine, modules: [("Store", Filling())]);
        return observed;
    }

    [TestMethod]
    public async Task AFieldWhoseModuleIsEditedWhileTheProgramWaits_IsWhatTheInterpreterHas()
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await EditedWhileWaitingAsync(VBExecutionEngine.Interpreter);
        var compiled = await EditedWhileWaitingAsync(VBExecutionEngine.Compiled);
        Assert.AreEqual(interpreted, compiled);
    }
}
