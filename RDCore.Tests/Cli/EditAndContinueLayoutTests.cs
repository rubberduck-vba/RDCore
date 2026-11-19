using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI;
using RDCore.Runtime.Support.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Platform.Components;
using RDCore.Runtime.Semantics;

namespace RDCore.Tests.Cli;

/// <summary>
/// An edit that changes what a procedure the program is in declares, or what a class whose objects it holds declares, applied to the program that waits: the
/// locals and the fields it holds are as a fresh run of the edited code would have them, from where it waits on - and a statement typed at the stop meanwhile
/// sees them as the edited code declares them.
/// </summary>
[TestClass]
public sealed class EditAndContinueLayoutTests
{
    // a cap on a hang, not a budget: each wait is on the event itself, which a loaded machine brings as late as it does.
    private static readonly TimeSpan SafetyCap = TimeSpan.FromMinutes(5);

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly string D = ModuleWorkspace.ClassModule("D",
        "Option Explicit",
        "Public Property Get Name() As String", "    Name = \"d\"", "End Property",
        "Private Sub Class_Terminate()", "    Debug.Print \"d:term\"", "End Sub");

    private static Task<ExecuteResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(SafetyCap);

    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
    }

    // a statement typed at the stop, in the activation the program waits in: what it printed, or why it did not run.
    private static async Task<string> AtTheStopAsync(EnvironmentSessionProvider provider, string statement)
    {
        var result = await new HostDebugExecuteHandler(provider, Substitute.For<IErrorDetailsRecorder>())
            .Handle(new HostDebugExecuteParams { FrameId = 0, Json = PlatformJson.Serialize(Statement(statement)) }, CancellationToken.None)
            .WaitAsync(SafetyCap);
        return result.Success ? $"at the stop: {string.Join("|", result.Output.Select(line => line.Trim()))}" : $"at the stop, not run: {result.Error}";
    }

    private static (string, ModuleType, string)[] All((string Name, string Source)[] classes, string program)
        => [.. classes.Select(c => (c.Name, ModuleType.ClassModule, c.Source)), ("Program", ModuleType.StdModule, program)];

    // the program run to its first stop; at each stop the step given is taken, and the program resumed. What it printed after its first stop, with a line for
    // each outcome that is not a stop or the end.
    private static async Task<List<string>> EditedAsync((string Name, string Source)[] classes, string program,
        params Func<EnvironmentSessionProvider, List<string>, Task>[] atStops)
    {
        var log = new List<string>();
        await ModuleWorkspace.InspectAsync(classes, program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            var current = stopped;
            for (var step = 0; current.Outcome == ExecutionOutcome.Suspended && step < 12; step++)
            {
                if (step < atStops.Length)
                {
                    await atStops[step](provider, log);
                }

                current = await Resume(provider);
                log.AddRange(current.Output.Select(line => line.Trim()));
                if (current.Outcome is not (ExecutionOutcome.Suspended or ExecutionOutcome.Completed))
                {
                    log.Add($"{current.Outcome}: {current.ErrorMessage}");
                }
            }
        }, debug: true);
        return log;
    }

    private static Func<EnvironmentSessionProvider, List<string>, Task> Reload((string Name, string Source)[] classes, string program)
        => async (provider, log) => log.AddRange((await ModuleWorkspace.ReloadAsync(provider, All(classes, program))).Select(error => $"load: {error}"));

    private static Func<EnvironmentSessionProvider, List<string>, Task> AtTheStop(string statement)
        => async (provider, log) => log.Add(await AtTheStopAsync(provider, statement));

    private static Func<EnvironmentSessionProvider, List<string>, Task> Then(params Func<EnvironmentSessionProvider, List<string>, Task>[] steps)
        => async (provider, log) =>
        {
            foreach (var step in steps)
            {
                await step(provider, log);
            }
        };

    private static Task<List<string>> FreshAsync((string Name, string Source)[] classes, string program) => EditedAsync(classes, program);

    // what the program printed, as one line to compare: a difference shows whole.
    private static void Same(IEnumerable<string> expected, IEnumerable<string> log)
    {
        var (wanted, printed) = (string.Join(" / ", expected), string.Join(" / ", log));
        Assert.AreEqual(wanted, printed, $"\r\nprinted: {printed}");
    }

    private static readonly string Holding = Lines(
        "Option Explicit",
        "Public Sub Main()",
        "    Inner",
        "    Debug.Print \"main:after\"",
        "End Sub",
        "Private Sub Inner()",
        "    Dim o As D",
        "    Set o = New D",
        "    Stop",
        "    Debug.Print \"inner:after\"",
        "    Stop",
        "    Debug.Print \"inner:end\"",
        "End Sub");

    // a local removed at one stop, and added back as another type at a later one, is a variable of its own: the object the removed one held still goes when
    // the procedure returns.
    [TestMethod]
    public async Task ALocalRemoved_ThenAddedBackAsAnotherType_AtALaterStop_IsAVariableOfItsOwn()
    {
        var removed = Holding.Replace("    Dim o As D\r\n    Set o = New D\r\n", string.Empty, StringComparison.Ordinal);
        var readded = removed.Replace("    Debug.Print \"inner:end\"\r\n", "    Dim o As Long\r\n    o = o + 1: Debug.Print \"o=\" & o\r\n", StringComparison.Ordinal);

        var log = await EditedAsync([("D", D)], Holding, Reload([("D", D)], removed), Reload([("D", D)], readded));

        Same(["inner:after", "o=1", "d:term", "main:after"], log);
    }

    private static readonly string Numbers = Lines(
        "Option Explicit",
        "Public Sub Main()",
        "    Inner",
        "End Sub",
        "Private Sub Inner()",
        "    Dim x As Long",
        "    x = 42",
        "    Stop",
        "    Debug.Print \"a\"",
        "    Stop",
        "    Debug.Print \"b\"",
        "End Sub");

    // a local removed at one stop and added back at a later one starts at its default, whatever type it is added back as.
    [TestMethod]
    [DataRow("Dim x As String", "x = x & \"s\": Debug.Print \"x=[\" & x & \"] \" & TypeName(x)", "x=[s] String", DisplayName = "as String")]
    [DataRow("Dim x As Long", "Debug.Print \"x=\" & x", "x=0", DisplayName = "as Long again")]
    [DataRow("Dim x As Object", "Debug.Print (x Is Nothing)", "True", DisplayName = "as Object")]
    public async Task ALocalRemoved_ThenAddedBack_AtALaterStop_StartsAtItsDefault(string declaration, string use, string expected)
    {
        var removed = Numbers.Replace("    Dim x As Long\r\n    x = 42\r\n", string.Empty, StringComparison.Ordinal);
        var readded = removed.Replace("    Debug.Print \"b\"\r\n", $"    {declaration}\r\n    {use}\r\n", StringComparison.Ordinal);

        var log = await EditedAsync([], Numbers, Reload([], removed), Reload([], readded));

        Same(["a", expected], log);
    }

    private static readonly string CalledTwice = Lines(
        "Option Explicit",
        "Public Sub Main()",
        "    Inner",
        "    Inner",
        "End Sub",
        "Private Sub Inner()",
        "    Dim x As Long",
        "    x = x + 42",
        "    Stop",
        "    Debug.Print \"a\"",
        "    Stop",
        "    Debug.Print \"b\"",
        "End Sub");

    // a local removed at one stop and added back as a Static at a later one is the procedure's Static, not the cell the removed local held.
    [TestMethod]
    public async Task ALocalRemoved_ThenAddedBackAsAStatic_AtALaterStop_IsTheProceduresStatic()
    {
        var removed = CalledTwice.Replace("    Dim x As Long\r\n    x = x + 42\r\n", string.Empty, StringComparison.Ordinal);
        var readded = removed.Replace("    Debug.Print \"b\"\r\n", "    Static x As Long\r\n    x = x + 1: Debug.Print \"b x=\" & x\r\n", StringComparison.Ordinal);

        var log = await EditedAsync([], CalledTwice, Reload([], removed), Reload([], readded));

        Same(["a", "b x=1", "a", "b x=2"], log);
    }

    // a Dim of a procedure the program is in, made a Static, is an edit the program can't go on with, as in MS-VBA: run again, the procedure has a Static.
    [TestMethod]
    public async Task ALocalMadeAStatic_InAProcedureTheProgramIsIn_IsNotApplied()
    {
        var edited = CalledTwice.Replace("    Dim x As Long\r\n", "    Static x As Long\r\n", StringComparison.Ordinal);

        var log = await EditedAsync([], CalledTwice, Reload([], edited));

        Same([$"Refused: {string.Format(RDCore.Runtime.Support.Resources.Host_ChangedWhileWaiting_Resume, $"Program.Inner ({RDCore.Runtime.Support.Resources.Host_Edit_LocalOfAnActiveProcedure})")}"], log);
    }

    // a local removed and added back before the program goes on was never removed from it: it goes on with its value.
    [TestMethod]
    public async Task ALocalRemoved_AndAddedBack_BeforeTheProgramGoesOn_KeepsItsValue()
    {
        var removed = Numbers.Replace("    Dim x As Long\r\n    x = 42\r\n", string.Empty, StringComparison.Ordinal);
        var used = Numbers.Replace("    Debug.Print \"a\"\r\n", "    Debug.Print \"a x=\" & x\r\n", StringComparison.Ordinal);

        var log = await EditedAsync([], Numbers, Then(Reload([], removed), AtTheStop("Debug.Print 1"), Reload([], used)));

        Same(["at the stop: 1", "a x=42", "b"], log);
    }

    // a local an edit added is one the statement typed at the stop can read, before the program goes on.
    [TestMethod]
    public async Task ALocalAdded_IsReadByAStatementTypedAtTheStop()
    {
        var edited = Numbers.Replace("    Stop\r\n    Debug.Print \"a\"\r\n", "    Dim w As Long\r\n    Stop\r\n    Debug.Print \"a w=\" & w\r\n", StringComparison.Ordinal);

        var log = await EditedAsync([], Numbers, Then(Reload([], edited), AtTheStop("w = 3"), AtTheStop("Debug.Print \"w=\" & w")));

        Same(["at the stop: ", "at the stop: w=3", "a w=3", "b"], log);
    }

    private static readonly string Tagged = ModuleWorkspace.ClassModule("N",
        "Option Explicit",
        "Public Tag As String",
        "Private Sub Class_Terminate()", "    Debug.Print \"n:term \" & Tag", "End Sub");

    // the locals of an activation an edit relaid let go of what they hold in the order the edited procedure declares them, as a fresh run's do.
    [TestMethod]
    public async Task TheLocalsOfARelaidActivation_LetGoOfTheirObjects_InTheOrderTheEditDeclaresThem()
    {
        var program = Lines(
            "Option Explicit",
            "Public Sub Main()",
            "    Inner",
            "    Debug.Print \"main:after\"",
            "End Sub",
            "Private Sub Inner()",
            "    Dim o1 As N",
            "    Set o1 = New N: o1.Tag = \"1\"",
            "    Stop",
            "    Debug.Print \"inner:after\"",
            "End Sub");
        var edited = program.Replace("    Dim o1 As N\r\n", "    Dim o0 As N\r\n    Dim o1 As N\r\n", StringComparison.Ordinal)
            .Replace("    Debug.Print \"inner:after\"\r\n", "    Set o0 = New N: o0.Tag = \"0\"\r\n    Debug.Print \"inner:after\"\r\n", StringComparison.Ordinal);

        var log = await EditedAsync([("N", Tagged)], program, Reload([("N", Tagged)], edited));

        Same(await FreshAsync([("N", Tagged)], edited), log);
    }

    private static readonly string Named = ModuleWorkspace.ClassModule("C2",
        "Option Explicit",
        "Public Name As String",
        "Public Function Ext() As String",
        "    Ext = Name",
        "End Function");

    private static readonly string NamedEdited = Named.Replace("Public Name As String\r\n", "Public Name As String\r\nPrivate mN As Long\r\n", StringComparison.Ordinal)
        .Replace("    Ext = Name\r\n", "    mN = mN + 1: Ext = Name & mN\r\n", StringComparison.Ordinal);

    private static readonly string Keeper = ModuleWorkspace.ClassModule("Holder",
        "Option Explicit",
        "Public Function Keep(ByVal x As C2) As C2",
        "    Static k As C2",
        "    If Not x Is Nothing Then Set k = x",
        "    Set Keep = k",
        "End Function");

    // objects of the class held every way a program holds one: a module variable, a Collection, an array, a Static of another class, a local.
    private static readonly string Holders = Lines(
        "Option Explicit",
        "Public V As C2",
        "Public Col As Collection",
        "Public Arr(1) As C2",
        "Public H As Holder",
        "Public Sub Main()",
        "    Dim local1 As C2",
        "    Set V = Make(\"v\")",
        "    Set Col = New Collection",
        "    Col.Add Make(\"c\")",
        "    Set Arr(1) = Make(\"a\")",
        "    Set H = New Holder",
        "    H.Keep Make(\"s\")",
        "    Set local1 = Make(\"l\")",
        "    Stop",
        "    Debug.Print V.Ext & \" \" & Col(1).Ext & \" \" & Arr(1).Ext & \" \" & H.Keep(Nothing).Ext & \" \" & local1.Ext & \" \" & Make(\"n\").Ext",
        "    Debug.Print V.Ext",
        "End Sub",
        "Private Function Make(ByVal n As String) As C2",
        "    Set Make = New C2",
        "    Make.Name = n",
        "End Function");

    // a field added to a class, every module loaded again: each live object has it, at its default, wherever the program holds it.
    [TestMethod]
    public async Task AFieldAdded_ToObjectsHeldEveryWay_EveryModuleLoadedAgain()
    {
        var log = await EditedAsync([("C2", Named), ("Holder", Keeper)], Holders, Reload([("C2", NamedEdited), ("Holder", Keeper)], Holders));

        Same(["v1 c1 a1 s1 l1 n1", "v2"], log);
    }

    // a method added to the class, every module loaded again: an array of its objects the program holds is of the class it was.
    [TestMethod]
    public async Task AMethodAdded_ToAClassAnArrayHoldsObjectsOf_EveryModuleLoadedAgain()
    {
        var methodOnly = Named.Replace("End Function\r\n", "End Function\r\nPublic Function Other() As Long\r\nEnd Function\r\n", StringComparison.Ordinal);

        var log = await EditedAsync([("C2", Named), ("Holder", Keeper)], Holders, Reload([("C2", methodOnly), ("Holder", Keeper)], Holders));

        Same(["v c a s l n", "v"], log);
    }

    // the field a class was given is one a statement typed at the stop can read, before the program goes on.
    [TestMethod]
    public async Task AFieldAdded_IsReadByAStatementTypedAtTheStop()
    {
        var log = await EditedAsync([("C2", Named), ("Holder", Keeper)], Holders,
            Then(Reload([("C2", NamedEdited), ("Holder", Keeper)], Holders), AtTheStop("Debug.Print V.Ext")));

        Same(["at the stop: v1", "v2 c1 a1 s1 l1 n1", "v3"], log);
    }
}
