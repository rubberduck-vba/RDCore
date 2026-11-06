using RDCore.Runtime.Compiled;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A field of a standard module of a scalar type, compiled (IR lowering S2.6c): held in the cell the session holds it in, found where the interpreter finds
/// it, whichever module names it and whatever activation is current, and read and written there by compiled code as a variable held in a cell is. What the
/// interpreter or another procedure does of the field, it does in that cell too. A program runs the same either way.
/// </summary>
[TestClass]
public sealed class CompiledModuleFieldTests
{
    private static async Task<(string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled)> RunAsync(string program, VBExecutionEngine engine,
        IReadOnlyList<(string Name, string Source)> modules, IReadOnlyList<(string Name, string Source)> classes)
    {
        (string Observed, IReadOnlyDictionary<string, CompilationResult> Compiled) made = default;
        using var running = CompiledExecution.Running(engine);
        await ModuleWorkspace.InspectAsync(classes, program, (provider, run, _) =>
        {
            made = (Observed(run), CompiledExecutionTests.ByName(provider));
            return Task.CompletedTask;
        }, engine: engine, modules: modules);
        return made;
    }

    // the program run both ways: what it printed, the error it raised and where, alike; and each procedure named compiled, run to its end in compiled code
    // with as many statements stepped as recoveries says.
    internal static async Task AssertAlikeAsync(string program, (string Name, long Recoveries)[] compiled, IReadOnlyList<(string Name, string Source)>? modules = null,
        IReadOnlyList<(string Name, string Source)>? classes = null)
    {
        CompiledExecutionTests.RequirePayloads();
        var interpreted = await RunAsync(program, VBExecutionEngine.Interpreter, modules ?? [], classes ?? []);
        var run = await RunAsync(program, VBExecutionEngine.Compiled, modules ?? [], classes ?? []);
        Assert.AreEqual(interpreted.Observed, run.Observed);

        foreach (var (name, recoveries) in compiled)
        {
            Assert.IsTrue(run.Compiled.TryGetValue(name, out var result), $"{name} was not called");
            Assert.IsNotNull(result.Body, $"{name}: {string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}"))}");
            if (!CompiledExecution.HandsOverAtRandom)
            {
                Assert.AreEqual(0, result.Body.WalkBacks, $"{name}'s walk-backs");
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

    // a field private to its module, of the module whose procedure names it.
    [TestMethod]
    public async Task APrivateFieldOfTheModule_IsKeptFromCallToCall()
        => await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted(3); Counted(4); Counted(0)",
            "End Sub"), [("Counted", 0)], modules: Store(
                "Public Function Counted(ByVal N As Long) As Long",
                "    Dim I As Long",
                "    For I = 1 To N",
                "        mCount = mCount + 1",
                "    Next",
                "    Counted = mCount",
                "End Function"));

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
}
