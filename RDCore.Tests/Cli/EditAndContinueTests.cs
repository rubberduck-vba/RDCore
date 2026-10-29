using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// An edit made while a program waits at a <c>Stop</c>: applied when the program goes on, or refused with what it is. The program, its edits and what MS-VBA
/// does with each are those of the Excel grid edit-and-continue was measured with. Where RD-VBA applies an edit MS-VBA resets the project for, the test says
/// so. Each edit reaches the host as the language server sends it: the edited module is defined and loaded again, and the others are as they were.
/// </summary>
[TestClass]
[TestCategory("Edit and continue")]
public sealed class EditAndContinueTests
{
    private static readonly string Class = ModuleWorkspace.ClassModule("C",
        "Option Explicit",
        "Private mValue As Long",
        "",
        "Public Sub Bump()",
        "    mValue = mValue + 1",
        "End Sub",
        "",
        "Public Property Get Value() As Long",
        "    Value = mValue * 10",
        "End Property");

    private static readonly string Program = Lines(
        "Option Explicit",
        "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long",
        "Private Const K As Long = 1",
        "Private Const OtherK As Long = 1",
        "Private Const N As Long = 3",
        "Public Type T",
        "    X As Long",
        "End Type",
        "Public Enum E",
        "    E1 = 1",
        "End Enum",
        "Public G As Long",
        "Public H As Long",
        "Public Obj As C",
        "Private arr(1 To N) As Long",
        "",
        "Public Sub Main()",
        "    G = 1",
        "    arr(1) = 7",
        "    Set Obj = New C",
        "    Obj.Bump",
        "    Debug.Print \"main:start\"",
        "    Callee 5",
        "    Debug.Print \"main:after G=\" & G & \" K=\" & K & \" obj=\" & Obj.Value",
        "    Other",
        "    Debug.Print \"main:end\"",
        "End Sub",
        "",
        "Public Sub Callee(ByVal p As Long)",
        "    Static S As Long",
        "    Dim a As Long",
        "    Dim z As Long",
        "    Dim tt As T",
        "    S = S + 1",
        "    a = 10",
        "    G = G + 1",
        "    Debug.Print \"callee:before a=\" & a",
        "    Stop",
        "    Debug.Print \"callee:after-stop a=\" & a",
        "    a = a + 1",
        "    GoTo Done",
        "    Debug.Print \"callee:skipped\"",
        "Done:",
        "    Debug.Print \"callee:done a=\" & a & \" S=\" & S & \" G=\" & G & \" E1=\" & E1",
        "End Sub",
        "",
        "Public Sub Other()",
        "    Debug.Print \"other:1 obj=\" & Obj.Value & \" k=\" & OtherK & \" ub=\" & UBound(arr)",
        "End Sub");

    // what the program prints after the Stop, as MS-VBA prints it with no edit.
    private static readonly string[] AsItWas =
        ["callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10 k=1 ub=3", "main:end"];

    // as it was, with the line Other prints in place of its own.
    private static string[] OtherPrints(params string[] lines) => [.. AsItWas[..3], .. lines, AsItWas[4]];

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // an edit of a line: Replace, InsertAfter, InsertBefore or Delete the first line that contains Find; In is the class's, "C", or the program's.
    private static string Edited(string source, string[] edits, string module)
    {
        var lines = source.Split("\r\n").ToList();
        foreach (var edit in edits)
        {
            var parts = edit.Split('|');
            if (parts[0] != module)
            {
                continue;
            }

            var index = lines.FindIndex(line => line.Contains(parts[2], StringComparison.Ordinal));
            Assert.IsGreaterThanOrEqualTo(0, index, $"no line contains {parts[2]}");
            switch (parts[1])
            {
                case "Replace":
                    lines[index] = parts[3];
                    break;
                case "InsertAfter":
                    lines.InsertRange(index + 1, parts[3].Split("\\n"));
                    break;
                case "InsertBefore":
                    lines.InsertRange(index, parts[3].Split("\\n"));
                    break;
                case "Delete":
                    lines.RemoveAt(index);
                    break;
            }
        }

        return string.Join("\r\n", lines);
    }

    // the program run to its Stop, the edits sent as the language server sends them - only the edited modules defined and loaded again - and the program resumed.
    private static async Task<ExecuteSessionResult> EditedAtTheStopAsync(params string[] edits)
    {
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectAsync([("C", Class)], Program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            var (classSource, programSource) = (Edited(Class, edits, "C"), Edited(Program, edits, "Program"));
            string[] edited = [.. (classSource != Class ? ["C"] : Array.Empty<string>()), .. (programSource != Program ? ["Program"] : Array.Empty<string>())];
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, edited,
                ("C", ModuleType.ClassModule, classSource), ("Program", ModuleType.StdModule, programSource));
            resumed = await Resume(provider);
        }, debug: true);

        return resumed!;
    }

    private static Task<ExecuteSessionResult> Resume(EnvironmentSessionProvider provider)
        => new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));

    private static string[] Output(ExecuteSessionResult result) => [.. result.Output.Select(line => line.Trim())];

    [TestMethod]
    public async Task NoEdit_TheProgramGoesOnAsItWas()
    {
        var resumed = await EditedAtTheStopAsync();

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(AsItWas, Output(resumed));
    }

    [TestMethod]
    public async Task EveryModuleDefinedAgain_WithNoEdit_TheProgramGoesOnWithWhatItHeld()
    {
        // a class's symbol is a new one each time its module is defined: a variable of the class still holds the object it held.
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectAsync([("C", Class)], Program, async (provider, stopped, runAgain) =>
        {
            _ = await ModuleWorkspace.ReloadAsync(provider, ("C", ModuleType.ClassModule, Class), ("Program", ModuleType.StdModule, Program));
            resumed = await Resume(provider);
        }, debug: true);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed!.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(AsItWas, Output(resumed));
    }

    // what MS-VBA applies and goes on with, and RD-VBA does: the program runs the edited code where it next reaches it.
    [TestMethod]
    [DataRow(new[] { "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:EDITED obj=\" & Obj.Value" }, new[] { "other:EDITED obj=10" },
        DisplayName = "a statement of a procedure the program is not in")]
    [DataRow(new[] { "C|Replace|Value = mValue * 10|    Value = mValue * 100" }, null, DisplayName = "a method of a class whose object the program holds")]
    public async Task AnEditMsVbaApplies_IsApplied(string[] edits, string[]? other)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(other is null ? ["callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=100", "other:1 obj=100 k=1 ub=3", "main:end"]
            : OtherPrints(other), Output(resumed));
    }

    // what MS-VBA resets the project for, and RD-VBA applies: the program reads it afresh where it next uses it, and runs it.
    [TestMethod]
    [DataRow(new[] { "C|InsertAfter|End Property|\\nPublic Function Extra() As String\\n    Extra = \"extra\"\\nEnd Function",
        "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:\" & Obj.Extra" }, new[] { "other:extra" },
        DisplayName = "a method added to a class whose object the program holds, and called")]
    [DataRow(new[] { "C|InsertAfter|Option Explicit|Option Compare Text", "C|InsertAfter|End Property|\\nPublic Function Same() As Boolean\\n    Same = (\"a\" = \"A\")\\nEnd Function",
        "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:same=\" & Obj.Same" }, new[] { "other:same=True" },
        DisplayName = "Option Compare Text added to a module the program is not in, and a comparison it makes")]
    [DataRow(new[] { "Program|Replace|Private Const OtherK|Private Const OtherK As Long = 2" }, new[] { "other:1 obj=10 k=2 ub=3" },
        DisplayName = "a Const revalued that only a procedure the program is not in reads")]
    [DataRow(new[] { "Program|Replace|Public Sub Other()|Public Sub Other(Optional ByVal q As Long = 4)", "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:q=\" & q" },
        new[] { "other:q=4" }, DisplayName = "the signature of a procedure the program is not in, and the parameter it gained")]
    [DataRow(new[] { "Program|InsertAfter|Debug.Print \"other:1|End Sub\\n\\nPublic Sub Added()\\n    Debug.Print \"added\"", "Program|Replace|Debug.Print \"other:1|    Added" },
        new[] { "added" }, DisplayName = "a procedure added, and called")]
    [DataRow(new[] { "Program|Replace|Private Declare PtrSafe|Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As LongPtr" }, null,
        DisplayName = "a Declare changed (not called: its library is the machine's)")]
    public async Task AnEditMsVbaResetsFor_ThatCanBeAppliedCorrectly_IsApplied(string[] edits, string[]? other)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(other is null ? AsItWas : OtherPrints(other), Output(resumed));
    }

    // declarations added at the top of the module, above the procedures the program is in, and used: the procedures the program is in go on where they now are.
    [TestMethod]
    [DataRow(new[] { "Program|InsertAfter|Public H As Long|Public J As Long", "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:j=\" & J" }, new[] { "other:j=0" },
        DisplayName = "a Public module variable added, and read")]
    [DataRow(new[] { "Program|Delete|Public H As Long" }, null, DisplayName = "a module variable nothing uses removed")]
    [DataRow(new[] { "Program|InsertAfter|Private Const K |Private Const K2 As Long = 2", "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:k2=\" & K2" },
        new[] { "other:k2=2" }, DisplayName = "a Const added, and read")]
    [DataRow(new[] { "Program|InsertAfter|End Type|Public Type T2\\n    Y As Long\\nEnd Type",
        "Program|Replace|Debug.Print \"other:1|    Dim t2 As T2: t2.Y = 4: Debug.Print \"other:y=\" & t2.Y" }, new[] { "other:y=4" },
        DisplayName = "a Type added, and used")]
    [DataRow(new[] { "Program|InsertAfter|End Enum|Public Enum E2\\n    F1 = 1\\nEnd Enum", "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:f1=\" & F1" },
        new[] { "other:f1=1" }, DisplayName = "an Enum added, and read")]
    [DataRow(new[] { "Program|InsertAfter|E1 = 1|    E2m = 2", "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:e2m=\" & E2m" }, new[] { "other:e2m=2" },
        DisplayName = "an Enum member added, and read")]
    public async Task ADeclarationAddedAboveTheProcedureTheProgramIsIn_IsApplied(string[] edits, string[]? other)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(other is null ? AsItWas : OtherPrints(other), Output(resumed));
    }

    // what can't be applied correctly, or is not applied to a program that waits yet: the program waits as it was, and picks the edit up when it is run again.
    [TestMethod]
    [DataRow(new[] { "Program|Replace|Dim a As Long|    Dim a As Integer" }, "a local of a procedure the program is in, given another type",
        DisplayName = "a local of a procedure the program is in, retyped")]
    [DataRow(new[] { "Program|Replace|Public G As Long|Public G As Integer" }, "a variable or a field that holds a value", DisplayName = "a module variable that holds a value, retyped")]
    [DataRow(new[] { "Program|Replace|    X As Long|    X As Integer" }, "a variable or a field that holds a value", DisplayName = "a field of a Type a local holds, retyped")]
    [DataRow(new[] { "Program|Replace|Public Sub Callee(ByVal p As Long)|Public Sub Callee(ByVal p As Long, Optional ByVal q As Long)" },
        "the signature of a procedure the program is in", DisplayName = "the signature of the procedure the program is in")]
    [DataRow(new[] { "Program|Replace|Static S As Long|    Static S As Integer" }, "a variable or a field that holds a value", DisplayName = "a Static retyped")]
    [DataRow(new[] { "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:1 obj=\" & Undeclared" }, "does not compile",
        DisplayName = "an edit that makes the code not compile")]
    [DataRow(new[] { "Program|Replace|Private Const N |Private Const N As Long = 5" }, "a constant that sizes what a variable holds",
        DisplayName = "a Const that sizes an array the program holds, revalued")]
    [DataRow(new[] { "Program|Replace|Private Const K |Private Const K As Long = 2" }, "a constant a procedure the program is in already read",
        DisplayName = "a Const a procedure the program is in reads, revalued")]
    [DataRow(new[] { "Program|Replace|E1 = 1|    E1 = 3" }, "a constant a procedure the program is in already read",
        DisplayName = "an Enum member a procedure the program is in reads, revalued")]
    [DataRow(new[] { "Program|InsertBefore|Private Declare PtrSafe|Option Compare Text" }, "an option of the module the program is in",
        DisplayName = "an Option added to the module the program is in")]
    [DataRow(new[] { "Program|InsertAfter|    X As Long|    Z As Long" }, "a field added to a Type", DisplayName = "a field added to a Type a local holds")]
    [DataRow(new[] { "Program|Replace|    Callee 5|    Callee 6" }, "the call a procedure the program is in is in the middle of",
        DisplayName = "the call a caller is in the middle of")]
    public async Task AnEditThatIsNotApplied_IsRefused_AndTheProgramStillWaits(string[] edits, string reason)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, reason);
    }

    [TestMethod]
    public async Task AnEditOfAPropertyLetTheProgramIsIn_IsApplied_ToTheLet()
    {
        // a Property Get, Let and Set share a name and are three procedures: the program waits in the Let.
        var program = Lines(
            "Option Explicit",
            "Private mV As Long",
            "Public Property Let Value(ByVal v As Long)",
            "    mV = v",
            "    Stop",
            "    Debug.Print \"let:\" & mV",
            "End Property",
            "Public Property Get Value() As Long",
            "    Value = mV",
            "End Property",
            "Public Sub Main()",
            "    Value = 5",
            "    Debug.Print \"main:\" & Value",
            "End Sub");
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectAsync([], program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"],
                ("Program", ModuleType.StdModule, program.Replace("\"let:\" & mV", "\"let:EDITED \" & mV", StringComparison.Ordinal)));
            resumed = await Resume(provider);
        }, debug: true);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed!.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "let:EDITED 5", "main:5" }, Output(resumed));
    }

    // the edits of the procedures the program is in: each goes on in its edited code from where it is, as MS-VBA goes on.
    [TestMethod]
    [DataRow(new[] { "Program|Replace|a = 10|    a = 20" }, null, DisplayName = "a statement above the yellow line")]
    [DataRow(new[] { "Program|Replace|Debug.Print \"callee:after-stop|    Debug.Print \"callee:after-stop-EDITED a=\" & a" },
        new[] { "callee:after-stop-EDITED a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10 k=1 ub=3", "main:end" },
        DisplayName = "the statement after the yellow line")]
    [DataRow(new[] { "Program|Replace|a = a + 1|    a = a + 5" },
        new[] { "callee:after-stop a=10", "callee:done a=15 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10 k=1 ub=3", "main:end" },
        DisplayName = "a statement further down")]
    [DataRow(new[] { "Program|Delete|    Stop" }, null, DisplayName = "the yellow line deleted")]
    [DataRow(new[] { "Program|InsertAfter|    Stop|    Debug.Print \"callee:inserted-below\"" },
        new[] { "callee:inserted-below", "callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10 k=1 ub=3", "main:end" },
        DisplayName = "a line inserted right below the yellow line")]
    [DataRow(new[] { "Program|Replace|Debug.Print \"main:after|    Debug.Print \"main:after-EDITED G=\" & G & \" K=\" & K & \" obj=\" & Obj.Value" },
        new[] { "callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after-EDITED G=2 K=1 obj=10", "other:1 obj=10 k=1 ub=3", "main:end" },
        DisplayName = "a statement of a caller, below its call")]
    [DataRow(new[] { "Program|Replace|Done:|Done2:", "Program|Replace|GoTo Done|    GoTo Done2" }, null, DisplayName = "a label renamed with its GoTo")]
    [DataRow(new[] { "Program|InsertBefore|    Callee 5|    Debug.Print \"main:inserted\"" }, null,
        DisplayName = "a line added to a caller above its call, which it does not run")]
    public async Task AnEditOfAProcedureTheProgramIsIn_IsApplied_AsMsVbaAppliesIt(string[] edits, string[]? expected)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(expected ?? AsItWas, Output(resumed));
    }

    [TestMethod]
    [Ignore("a local added to or removed from a procedure the program is in, and a field added to an object it holds, are not applied to it yet")]
    [DataRow(new[] { "Program|InsertAfter|Dim tt As T|    Dim b As Long", "Program|Replace|Debug.Print \"callee:after-stop|    b = 7: Debug.Print \"callee:after-stop b=\" & b" },
        new[] { "callee:after-stop b=7", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10 k=1 ub=3", "main:end" },
        DisplayName = "a local added to the procedure the program is in, and used")]
    [DataRow(new[] { "Program|InsertAfter|Public Sub Main()|    Dim m As Long" }, null, DisplayName = "a local added to a caller")]
    [DataRow(new[] { "C|InsertAfter|Private mValue As Long|Private mOther As Long" }, null, DisplayName = "a field added to a class whose object the program holds")]
    public async Task AnEditOfTheLayoutOfAFrame_IsApplied_AsMsVbaAppliesIt(string[] edits, string[]? expected)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(expected ?? AsItWas, Output(resumed));
    }

    // a program of one module run to its Stop, edited into another, and resumed.
    private static async Task<ExecuteSessionResult> ProgramEditedAtItsStopAsync(string program, string edited)
    {
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectAsync([], program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", ModuleType.StdModule, edited));
            resumed = await Resume(provider);
        }, debug: true);

        return resumed!;
    }

    private static readonly string InALoop = Lines(
        "Option Explicit",
        "Public Sub Main()",
        "    Dim i As Long",
        "    For i = 1 To 2",
        "        Debug.Print \"loop:\" & i",
        "        If i = 1 Then Stop",
        "    Next",
        "    Debug.Print \"main:end\"",
        "End Sub");

    [TestMethod]
    public async Task AnEditInsideTheLoopTheProgramIsIn_IsApplied_AndTheLoopGoesOn()
    {
        var resumed = await ProgramEditedAtItsStopAsync(InALoop, InALoop.Replace("\"loop:\" & i", "\"edited:\" & i", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "edited:2", "main:end" }, Output(resumed));
    }

    [TestMethod]
    public async Task AnEditOfTheOpeningOfTheLoopTheProgramIsIn_IsRefused()
    {
        var resumed = await ProgramEditedAtItsStopAsync(InALoop, InALoop.Replace("For i = 1 To 2", "For i = 1 To 3", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "the opening of a block or a GoSub the program is in");
    }

    [TestMethod]
    public async Task AnEditOfACallerInTheMiddleOfACallItsConditionMakes_IsRefusedForNow()
    {
        var program = Lines(
            "Option Explicit",
            "Public Function Check() As Boolean",
            "    Stop",
            "    Check = True",
            "End Function",
            "Public Sub Main()",
            "    If Check() Then Debug.Print \"checked\"",
            "    Debug.Print \"main:end\"",
            "End Sub");

        var resumed = await ProgramEditedAtItsStopAsync(program, program.Replace("\"main:end\"", "\"main:edited\"", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "Program.Main");
        StringAssert.Contains(resumed.ErrorMessage, "in the middle of a call its condition or the header of its block makes");
    }

    [TestMethod]
    public async Task AnEditOfTheProcedureTheProgramIsIn_ThatIsOnTheStackTwice_IsAppliedToBoth()
    {
        var program = Lines(
            "Option Explicit",
            "Public Sub R(ByVal n As Long)",
            "    If n = 0 Then Stop",
            "    If n > 0 Then R n - 1",
            "    Debug.Print \"r:\" & n",
            "End Sub",
            "Public Sub Main()",
            "    R 1",
            "End Sub");

        var resumed = await ProgramEditedAtItsStopAsync(program, program.Replace("\"r:\" & n", "\"edited:\" & n", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "edited:0", "edited:1" }, Output(resumed));
    }

    private static readonly string InAWith = Lines(
        "Option Explicit",
        "Public Sub Main()",
        "    Dim c As New Collection",
        "    With c",
        "        .Add 1",
        "        Stop",
        "        .Add 2",
        "        Debug.Print \"count=\" & .Count",
        "    End With",
        "End Sub");

    [TestMethod]
    public async Task AnEditInsideTheWithTheProgramIsIn_IsApplied_WithItsTarget()
    {
        var resumed = await ProgramEditedAtItsStopAsync(InAWith, InAWith.Replace("\"count=\" & .Count", "\"edited=\" & .Count", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "edited=2" }, Output(resumed));
    }

    [TestMethod]
    public async Task AnEditOfTheTargetOfTheWithTheProgramIsIn_IsRefused()
    {
        var resumed = await ProgramEditedAtItsStopAsync(InAWith, InAWith.Replace("    With c", "    With New Collection", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "the opening of a block or a GoSub the program is in");
    }

    private static readonly string Handled = Lines(
        "Option Explicit",
        "Public Sub Main()",
        "    On Error GoTo Handler",
        "    Stop",
        "    Err.Raise 5",
        "    Exit Sub",
        "Handler:",
        "    Debug.Print \"handled \" & Err.Number",
        "End Sub");

    // the handler an On Error GoTo set is where its label now is: renamed with its On Error GoTo, it is the statement the label was on.
    [TestMethod]
    [DataRow("\"handled \" & Err.Number|\"edited \" & Err.Number", "edited 5", DisplayName = "the handler's code")]
    [DataRow("GoTo Handler|GoTo Renamed", "handled 5", DisplayName = "the handler's label, renamed with its On Error GoTo")]
    public async Task AnEditOfTheErrorHandlerOfTheProcedureTheProgramIsIn_IsApplied(string edit, string expected)
    {
        var (find, replace) = (edit.Split('|')[0], edit.Split('|')[1]);
        var edited = Handled.Replace(find, replace, StringComparison.Ordinal);
        if (find.StartsWith("GoTo", StringComparison.Ordinal))
        {
            edited = edited.Replace("Handler:", "Renamed:", StringComparison.Ordinal);
        }

        var resumed = await ProgramEditedAtItsStopAsync(Handled, edited);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { expected }, Output(resumed));
    }

    [TestMethod]
    public async Task AnEditOfTheProcedureTheProgramIsIn_InAGoSub_ReturnsWhereTheGoSubNowIs()
    {
        var program = Lines(
            "Option Explicit",
            "Public Sub Main()",
            "    GoSub Inner",
            "    Debug.Print \"back\"",
            "    Exit Sub",
            "Inner:",
            "    Stop",
            "    Debug.Print \"in\"",
            "    Return",
            "End Sub");
        var edited = program.Replace("    GoSub Inner", "    Debug.Print \"above\"\r\n    GoSub Inner", StringComparison.Ordinal)
            .Replace("\"back\"", "\"edited back\"", StringComparison.Ordinal);

        var resumed = await ProgramEditedAtItsStopAsync(program, edited);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "in", "edited back" }, Output(resumed));
    }

    // the program, and a module beside it, run to the program's Stop; the module edited into another, alone, as the language server sends it; and resumed.
    private static async Task<ExecuteSessionResult> OtherModuleEditedAtTheStopAsync(string other, string edited, string program)
    {
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectWithModulesAsync([("Other", other)], program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Other"], ("Other", ModuleType.StdModule, edited), ("Program", ModuleType.StdModule, program));
            resumed = await Resume(provider);
        }, debug: true);

        return resumed!;
    }

    [TestMethod]
    public async Task AConstThatSizesAnArrayThroughAnotherConst_Revalued_IsRefused()
    {
        var program = Lines(
            "Option Explicit",
            "Private Const N As Long = 3",
            "Private Const M As Long = N + 1",
            "Private arr(1 To M) As Long",
            "Public Sub Main()",
            "    arr(4) = 7",
            "    Stop",
            "    Debug.Print UBound(arr)",
            "End Sub");

        var resumed = await ProgramEditedAtItsStopAsync(program, program.Replace("N As Long = 3", "N As Long = 5", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "a constant that sizes what a variable holds");
    }

    [TestMethod]
    public async Task AConstThatSizesAFixedLengthStringOfAnotherModule_Revalued_IsRefused()
    {
        var other = Lines("Option Explicit", "Public Const N As Long = 3");
        var program = Lines(
            "Option Explicit",
            "Private s As String * N",
            "Public Sub Main()",
            "    s = \"abcdef\"",
            "    Stop",
            "    Debug.Print \"len=\" & Len(s) & \" N=\" & N",
            "End Sub");

        var resumed = await OtherModuleEditedAtTheStopAsync(other, other.Replace("= 3", "= 5", StringComparison.Ordinal), program);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "a constant that sizes what a variable holds");
    }

    // the name of a Const in a string, and a local of the same name, are no read of it: the procedure the program is in does not read the one revalued.
    [TestMethod]
    public async Task AConst_ThatTheProcedureTheProgramIsInOnlySpellsOrShadows_Revalued_IsApplied()
    {
        var program = Lines(
            "Option Explicit",
            "Private Const K As Long = 1",
            "Public Sub Other()",
            "    Debug.Print \"other k=\" & K",
            "End Sub",
            "Public Sub Main()",
            "    Dim K As Long",
            "    K = 5",
            "    Stop",
            "    Debug.Print \"K\" & K",
            "    Other",
            "End Sub");

        var resumed = await ProgramEditedAtItsStopAsync(program, program.Replace("K As Long = 1", "K As Long = 2", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "K5", "other k=2" }, Output(resumed));
    }

    // an array declared with no lower bound has the one the Option Base of its module gave it; one made after the edit, the edited one.
    [TestMethod]
    [DataRow("Public arr(3) As Long", null, DisplayName = "a module that holds an array declared with no lower bound")]
    [DataRow("Public Total As Long", "lb=1", DisplayName = "a module that holds none")]
    public async Task OptionBase_OfAModuleTheProgramIsNotIn_Changed(string holds, string? expected)
    {
        var other = Lines(
            "Option Explicit",
            holds,
            "Public Function Lb() As Long",
            "    Dim a(3) As Long",
            "    Lb = LBound(a)",
            "End Function");
        var program = Lines(
            "Option Explicit",
            "Public Sub Main()",
            "    Stop",
            "    Debug.Print \"lb=\" & Other.Lb()",
            "End Sub");

        var resumed = await OtherModuleEditedAtTheStopAsync(other, other.Replace("Option Explicit", "Option Explicit\r\nOption Base 1", StringComparison.Ordinal), program);

        if (expected is null)
        {
            Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
            StringAssert.Contains(resumed.ErrorMessage, "the Option Base of a module whose arrays hold values");
        }
        else
        {
            Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
            CollectionAssert.AreEqual(new[] { expected }, Output(resumed));
        }
    }

    // a variable retyped while the program waits is defined again, and what it held is gone: taking the edit back gives it another variable of the type
    // it had, not the value. Only running the program again gives the value back.
    [TestMethod]
    public async Task AModuleVariableRetypedWhileTheProgramWaits_ThenTheEditTakenBack_IsStillRefused_UntilTheProgramIsRunAgain()
    {
        var retyped = Edited(Program, ["Program|Replace|Public G As Long|Public G As Integer"], "Program");
        ExecuteSessionResult? whileRetyped = null, takenBack = null, again = null;
        await ModuleWorkspace.InspectAsync([("C", Class)], Program, async (provider, stopped, runAgain) =>
        {
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("C", ModuleType.ClassModule, Class), ("Program", ModuleType.StdModule, retyped));
            whileRetyped = await Resume(provider);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("C", ModuleType.ClassModule, Class), ("Program", ModuleType.StdModule, Program));
            takenBack = await Resume(provider);
            again = await runAgain();
        }, debug: true);

        Assert.AreEqual(ExecutionOutcome.Refused, whileRetyped!.Outcome, whileRetyped.ErrorMessage);
        Assert.AreEqual(ExecutionOutcome.Refused, takenBack!.Outcome, takenBack.ErrorMessage);
        StringAssert.Contains(takenBack.ErrorMessage, "Program.G (a variable whose value an edit reset");
        Assert.AreEqual(ExecutionOutcome.Suspended, again!.Outcome, again.ErrorMessage);
    }

    // the object a variable of a class held, retyped, is gone from it: the program can't go on without it, and its Class_Terminate does not run while the
    // program waits - what is let go of by a program that is reset runs none, as an End runs none - and the object is discarded when the program is run again.
    [TestMethod]
    public async Task AVariableOfAClassRetypedWhileTheProgramWaits_LosesItsObject_AndIsStillRefusedOnceTakenBack()
    {
        var owned = ModuleWorkspace.ClassModule("D",
            "Option Explicit",
            "Private Sub Class_Terminate()",
            "    Terminated = Terminated + 1",
            "End Sub");
        var program = Lines(
            "Option Explicit",
            "Public Terminated As Long",
            "Public Held As D",
            "Public Sub Main()",
            "    Set Held = New D",
            "    Stop",
            "    Debug.Print \"main:end\"",
            "End Sub");
        string? whileWaiting = null;
        ExecuteSessionResult? takenBack = null;
        await ModuleWorkspace.InspectAsync([("D", owned)], program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("D", ModuleType.ClassModule, owned),
                ("Program", ModuleType.StdModule, program.Replace("Held As D", "Held As Object", StringComparison.Ordinal)));
            whileWaiting = provider.Execution.Variables(0, HostVariableScope.Module, 0).Variables.Single(variable => variable.Name == "Terminated").Value;
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("D", ModuleType.ClassModule, owned), ("Program", ModuleType.StdModule, program));
            takenBack = await Resume(provider);
        }, debug: true);

        Assert.AreEqual("0", whileWaiting);
        Assert.AreEqual(ExecutionOutcome.Refused, takenBack!.Outcome, takenBack.ErrorMessage);
        StringAssert.Contains(takenBack.ErrorMessage, "Program.Held (a variable whose value an edit reset");
    }

    // a program of one module run to its Stop, made to wait where `before` makes it, edited into another, and resumed.
    private static async Task<ExecuteSessionResult> EditedWhereItWaitsAsync(string program, string edited, Func<EnvironmentSessionProvider, Task<ExecuteSessionResult>> before)
    {
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectAsync([], program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            var waits = await before(provider);
            Assert.AreEqual(ExecutionOutcome.Suspended, waits.Outcome, waits.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", ModuleType.StdModule, edited));
            resumed = await Resume(provider);
        }, debug: true);

        return resumed!;
    }

    private static Task<ExecuteSessionResult> AtTheNextErrorAsync(EnvironmentSessionProvider provider)
    {
        provider.Execution.SetErrorBreak(ErrorBreakMode.All);
        return Resume(provider);
    }

    // the code alone does not say which of two identical statements an edit removed: what the program goes on from is then refused, not guessed.
    [TestMethod]
    public async Task OneOfTwoIdenticalStatementsRemoved_AboveTheStopTheProgramWaitsAt_IsRefusedForNow()
    {
        var program = Lines("Option Explicit", "Public Sub Main()", "    Dim x As Long",
            "    x = x + 1", "    Stop", "    x = x + 1", "    Stop", "    x = x + 1", "    Debug.Print \"x=\" & x", "End Sub");
        var edited = Lines("Option Explicit", "Public Sub Main()", "    Dim x As Long",
            "    x = x + 1", "    Stop", "    x = x + 1", "    Debug.Print \"x=\" & x", "End Sub");

        var resumed = await EditedWhereItWaitsAsync(program, edited, Resume);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "can't be placed for certain");
    }

    [TestMethod]
    public async Task OneOfTwoIdenticalStatementsRemoved_AtTheBreakpointTheProgramWaitsAt_IsRefusedForNow()
    {
        var program = Lines("Option Explicit", "Public Sub Main()", "    Dim x As Long", "    Stop",
            "    x = x + 1", "    Debug.Print \"x=\" & x", "    x = x + 1", "    Debug.Print \"x=\" & x", "End Sub");
        var edited = Lines("Option Explicit", "Public Sub Main()", "    Dim x As Long", "    Stop",
            "    x = x + 1", "    Debug.Print \"x=\" & x", "End Sub");

        var resumed = await EditedWhereItWaitsAsync(program, edited, async provider =>
        {
            _ = await SetBreakpoints(provider, 6);
            return await Resume(provider);
        });

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "can't be placed for certain");
    }

    [TestMethod]
    public async Task OneOfTwoIdenticalCalls_TheOneACallerIsInTheMiddleOf_Removed_IsRefusedForNow()
    {
        var program = Lines("Option Explicit", "Public Sub Callee()", "    Static n As Long", "    n = n + 1", "    If n = 1 Then Stop", "    Debug.Print \"callee \" & n",
            "End Sub", "Public Sub Main()", "    Callee", "    Debug.Print \"between\"", "    Callee", "End Sub");

        var resumed = await ProgramEditedAtItsStopAsync(program, program.Replace("    Callee\r\n    Debug.Print \"between\"\r\n", "", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "Program.Main");
        StringAssert.Contains(resumed.ErrorMessage, "can't be placed for certain");
    }

    // a program that waits at an error goes on from the statement that raised it, as it now is: caught there by its handler, with the lines above it moved.
    [TestMethod]
    [DataRow("    On Error Resume Next", "", new[] { "b", "c" }, DisplayName = "On Error Resume Next, a line removed above it")]
    [DataRow("    On Error Resume Next", "    Debug.Print \"x2\"\r\n", new[] { "b", "c" }, DisplayName = "On Error Resume Next, a line added above it")]
    [DataRow("    On Error GoTo H", "", new[] { "h", "b" }, DisplayName = "On Error GoTo and Resume Next, a line removed above it")]
    public async Task AnEditAboveTheStatementTheProgramWaitsAtAnErrorOf_IsApplied_AndTheErrorIsCaughtThere(string handler, string replacement, string[] expected)
    {
        string[] after = handler.Contains("GoTo", StringComparison.Ordinal) ? ["    Debug.Print \"b\""] : ["    Debug.Print \"b\"", "    Debug.Print \"c\""];
        var program = Lines(["Option Explicit", "Public Sub Main()", "    Stop", handler, "    Debug.Print \"x\"", "    Err.Raise 5", .. after, "    Exit Sub",
            "H:", "    Debug.Print \"h\"", "    Resume Next", "End Sub"]);
        var edited = program.Replace("    Debug.Print \"x\"\r\n", replacement.Length == 0 ? "" : "    Debug.Print \"x\"\r\n" + replacement, StringComparison.Ordinal);

        var resumed = await EditedWhereItWaitsAsync(program, edited, AtTheNextErrorAsync);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(expected, Output(resumed));
    }

    [TestMethod]
    public async Task TheStatementTheProgramWaitsAtAnErrorOf_Edited_IsRefused()
    {
        var program = Lines("Option Explicit", "Public Sub Main()", "    Stop", "    On Error Resume Next", "    Err.Raise 5", "    Debug.Print \"b\"", "End Sub");

        var resumed = await EditedWhereItWaitsAsync(program, program.Replace("Err.Raise 5", "Err.Raise 6", StringComparison.Ordinal), AtTheNextErrorAsync);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, "stopped at an error of");
    }

    // what a block the program has left behind kept is of no use to it: the opening of that block, edited, is applied.
    [TestMethod]
    [DataRow("    For i = 1 To 2|        Debug.Print i|    Next", "For i = 1 To 2|For i = 1 To 3", DisplayName = "a For")]
    [DataRow("    For Each v In c|        Debug.Print v|    Next", "For Each v In c|For Each v In (c)", DisplayName = "a For Each")]
    [DataRow("    Select Case s|        Case 1|            Debug.Print \"one\"|    End Select", "Select Case s|Select Case s + 0", DisplayName = "a Select Case")]
    [DataRow("    With c|        .Add 2|    End With", "    With c|    With (c)", DisplayName = "a With")]
    public async Task TheOpeningOfABlockTheProgramHasLeft_Edited_IsApplied(string block, string edit)
    {
        var program = Lines(["Option Explicit", "Public Sub Main()", "    Dim i As Long", "    Dim s As Long", "    Dim v As Variant", "    Dim c As New Collection",
            "    c.Add 1", "    s = 1", .. block.Split('|'), "    Stop", "    Debug.Print \"end\"", "End Sub"]);
        var (find, replace) = (edit.Split('|')[0], edit.Split('|')[1]);

        var resumed = await ProgramEditedAtItsStopAsync(program, program.Replace(find, replace, StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "end" }, Output(resumed));
    }

    // an error On Error Resume Next went past is gone by: the statement that raised it, edited, is applied.
    [TestMethod]
    public async Task TheStatementAnErrorWasSkippedAt_Edited_IsApplied()
    {
        var program = Lines("Option Explicit", "Public Sub Main()", "    On Error Resume Next", "    Err.Raise 5", "    Stop", "    Debug.Print \"err=\" & Err.Number", "End Sub");

        var resumed = await ProgramEditedAtItsStopAsync(program, program.Replace("Err.Raise 5", "Err.Raise 6", StringComparison.Ordinal));

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "err=5" }, Output(resumed));
    }

    // the blocks the program is in, with a line added above them: what each keeps moves with it.
    [TestMethod]
    public async Task AnIfInAForInAWith_TheProgramIsIn_WithALineAddedAboveThem_GoesOnWithWhatTheyKeep()
    {
        var program = Lines("Option Explicit", "Public Sub Main()", "    Dim c As New Collection", "    Dim i As Long", "    With c", "        For i = 1 To 2",
            "            If i >= 1 Then", "                .Add i", "                If i = 1 Then Stop", "                Debug.Print \"n:\" & .Count", "            End If",
            "        Next", "    End With", "    Debug.Print \"end\"", "End Sub");
        var edited = program.Replace("\"n:\"", "\"m:\"", StringComparison.Ordinal)
            .Replace("    Dim i As Long\r\n", "    Dim i As Long\r\n    Debug.Print \"top\"\r\n", StringComparison.Ordinal);

        var resumed = await ProgramEditedAtItsStopAsync(program, edited);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "m:1", "m:2", "end" }, Output(resumed));
    }

    [TestMethod]
    public async Task TheHandlerTheProgramIsIn_WithALineAddedAboveTheStatementThatRaised_ResumesNextAfterIt()
    {
        var program = Lines("Option Explicit", "Public Sub Main()", "    On Error GoTo H", "    Err.Raise 5", "    Debug.Print \"after \" & Err.Number", "    Exit Sub", "H:",
            "    Debug.Print \"in h \" & Err.Number", "    Stop", "    Debug.Print \"h2\"", "    Resume Next", "End Sub");
        var edited = program.Replace("\"h2\"", "\"h2-edited \" & Err.Number", StringComparison.Ordinal)
            .Replace("    On Error GoTo H\r\n", "    Debug.Print \"top\"\r\n    On Error GoTo H\r\n", StringComparison.Ordinal);

        var resumed = await ProgramEditedAtItsStopAsync(program, edited);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "h2-edited 5", "after 0" }, Output(resumed));
    }

    private static Task<HostDebugBreakpointsResult> SetBreakpoints(EnvironmentSessionProvider provider, params int[] lines)
        => new HostDebugBreakpointsHandler(provider).Handle(new HostDebugBreakpointsParams
        {
            ModuleName = "Program",
            Breakpoints = [.. lines.Select(line => new HostBreakpointLine { Line = line })],
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));

    // what Visual Studio does: the program waits before the yellow line, and the line it waits before, edited, is the line it runs.
    [TestMethod]
    public async Task TheLineABreakpointWaitsBefore_Edited_RunsEdited()
    {
        var program = Lines(
            "Option Explicit",
            "Public Sub Main()",
            "    Dim x As Long",
            "    Stop",
            "    x = 1",
            "    Debug.Print \"x=\" & x",
            "End Sub");
        ExecuteSessionResult? hit = null, resumed = null;
        await ModuleWorkspace.InspectAsync([], program, async (provider, stopped, runAgain) =>
        {
            _ = await SetBreakpoints(provider, 4);
            hit = await Resume(provider);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("Program", ModuleType.StdModule, program.Replace("x = 1", "x = 2", StringComparison.Ordinal)));
            resumed = await Resume(provider);
        }, debug: true);

        Assert.AreEqual((ExecutionOutcome.Suspended, 4), (hit!.Outcome, hit.ErrorLine), hit.ErrorMessage);
        Assert.AreEqual(ExecutionOutcome.Completed, resumed!.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "x=2" }, Output(resumed));
    }

    // a client sends the breakpoints of the module again with its edit, at the lines they are now on.
    [TestMethod]
    public async Task ABreakpoint_OnALineAnEditMoved_IsWaitedAtWhereTheLineNowIs()
    {
        var edited = Edited(Program, ["Program|InsertAfter|Private Const K |Private Const K2 As Long = 2"], "Program");
        var done = edited.Split("\r\n").ToList().FindIndex(line => line.Contains("Debug.Print \"callee:done", StringComparison.Ordinal));
        ExecuteSessionResult? hit = null, resumed = null;
        await ModuleWorkspace.InspectAsync([("C", Class)], Program, async (provider, stopped, runAgain) =>
        {
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("C", ModuleType.ClassModule, Class), ("Program", ModuleType.StdModule, edited));
            _ = await SetBreakpoints(provider, done);
            hit = await Resume(provider);
            resumed = await Resume(provider);
        }, debug: true);

        Assert.AreEqual((ExecutionOutcome.Suspended, done), (hit!.Outcome, hit.ErrorLine), hit.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "callee:after-stop a=10" }, Output(hit));
        Assert.AreEqual(ExecutionOutcome.Completed, resumed!.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(AsItWas[1..], Output(resumed));
    }

    // an edit that changes no code of the procedure, only where its lines are, is followed as any other.
    [TestMethod]
    [DataRow("Program|InsertAfter|Private Const K |Private Const K2 As Long = 2", DisplayName = "a declaration added above the procedure the program is in")]
    [DataRow("Program|InsertBefore|    Stop|    ' a comment above the yellow line", DisplayName = "a comment added inside the procedure the program is in")]
    public async Task AGoto_AfterTheLinesOfTheProcedureTheProgramIsInMoved_IsALineOfTheEditedCode(string edit)
    {
        var edited = Edited(Program, [edit], "Program");
        var done = edited.Split("\r\n").ToList().FindIndex(line => line.Contains("Debug.Print \"callee:done", StringComparison.Ordinal));
        HostDebugGotoResult? moved = null;
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectAsync([("C", Class)], Program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("C", ModuleType.ClassModule, Class), ("Program", ModuleType.StdModule, edited));
            moved = provider.Execution.Goto(done, null);
            resumed = await Resume(provider);
        }, debug: true);

        Assert.IsTrue(moved!.Moved, moved.Reason);
        Assert.AreEqual(done, moved.Line);
        Assert.AreEqual(ExecutionOutcome.Completed, resumed!.Outcome, resumed.ErrorMessage);
        string[] expected = ["callee:done a=10 S=1 G=2 E1=1", .. AsItWas[2..]];
        CollectionAssert.AreEqual(expected, Output(resumed));
    }

    // what a client asks after each edit it sends, to mark it as it is made: only what is not applied, each with what it is, why, and where it now is.
    [TestMethod]
    public async Task TheEditStatus_ListsWhatIsNotApplied_WithWhereItIs()
    {
        HostDebugEditStatusResult? clean = null, edited = null;
        await ModuleWorkspace.InspectAsync([("C", Class)], Program, async (provider, stopped, runAgain) =>
        {
            clean = provider.Execution.EditStatus();
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Program"], ("C", ModuleType.ClassModule, Class), ("Program", ModuleType.StdModule,
                Edited(Program, ["Program|Replace|Public Sub Callee(ByVal p As Long)|Public Sub Callee(ByVal p As Long, Optional ByVal q As Long)",
                    "Program|Replace|Private Const OtherK|Private Const OtherK As Long = 2"], "Program")));
            edited = provider.Execution.EditStatus();
        }, debug: true);

        Assert.IsTrue(clean!.Suspended);
        Assert.IsEmpty(clean.Edits);

        // the Const revalued applies, and is not listed; the signature of the procedure the program is in can't be applied.
        Assert.IsTrue(edited!.Suspended);
        var edit = Assert.ContainsSingle(edited.Edits);
        Assert.AreEqual(("Program.Callee", HostDebugEditKind.Restart), (edit.Name, edit.Kind));
        StringAssert.Contains(edit.Reason, "the signature of a procedure the program is in");
        StringAssert.EndsWith(edit.ModuleUri!.AbsoluteUri, "#Program");
        Assert.AreEqual(Program.Split("\r\n").ToList().FindIndex(line => line.StartsWith("Public Sub Callee", StringComparison.Ordinal)), edit.Range!.Value.Start.Line);
    }

    [TestMethod]
    public async Task TheEditStatus_OfNoProgramThatWaits_IsNotSuspended()
    {
        HostDebugEditStatusResult? status = null;
        await ModuleWorkspace.InspectAsync([("C", Class)], Program.Replace("    Stop\r\n", string.Empty), (provider, _, _) =>
        {
            status = provider.Execution.EditStatus();
            return Task.CompletedTask;
        }, debug: true);

        Assert.IsFalse(status!.Suspended);
        Assert.IsEmpty(status.Edits);
    }
}
