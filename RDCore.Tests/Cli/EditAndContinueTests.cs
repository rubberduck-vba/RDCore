using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Services.VerboseMessages;
using RDCore.SDK.Workspace;

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
    private const string LinesMoved = "the lines of a procedure the program is in, moved by an edit above it, are not followed yet";

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

    // declarations added at the top of the module, above the procedures the program is in, and used: applied once the lines of an active procedure follow the edit.
    [TestMethod]
    [Ignore(LinesMoved)]
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
    [DataRow(new[] { "Program|Replace|a = a + 1|    a = a + 5" }, "the code of a procedure the program is in", DisplayName = "the code of the procedure the program is in")]
    [DataRow(new[] { "Program|Replace|    Callee 5|    Callee 6" }, "the code of a procedure the program is in", DisplayName = "the call a caller is in the middle of")]
    [DataRow(new[] { "Program|InsertAfter|Private Const K |Private Const K2 As Long = 2" }, "the lines of a procedure the program is in moved",
        DisplayName = "a line added above the procedures the program is in")]
    public async Task AnEditThatIsNotApplied_IsRefused_AndTheProgramStillWaits(string[] edits, string reason)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, reason);
    }

    [TestMethod]
    public async Task AnEditOfAPropertyLetTheProgramIsIn_IsSeenAsAnEditOfAProcedureTheProgramIsIn()
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

        Assert.AreEqual(ExecutionOutcome.Refused, resumed!.Outcome, string.Join(" | ", resumed.Output));
        StringAssert.Contains(resumed.ErrorMessage, "Value");
        StringAssert.Contains(resumed.ErrorMessage, "the code of a procedure the program is in");
    }

    // the edits of the procedures the program is in, and of their layout: applied once the program follows them. Each is what MS-VBA does with it.
    [TestMethod]
    [Ignore("the code of a procedure the program is in is not applied to it yet")]
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

    private static readonly string Child = ModuleWorkspace.ClassModule("Child",
        "Private Sub Class_Terminate()", "    Debug.Print \"child gone\"", "End Sub");

    private static string Owner(bool adopts, params string[] fields) => ModuleWorkspace.ClassModule("Owner",
        [.. fields,
        "Public Sub Adopt()", .. (adopts ? new[] { "    Set mChild = New Child" } : []), "End Sub",
        "Private Sub Class_Terminate()", "    Debug.Print \"owner gone\"", "End Sub"]);

    private static readonly string Owning = string.Join("\r\n",
        "Option Explicit",
        "Public Obj As Owner",
        "Public Sub Main()",
        "    Set Obj = New Owner",
        "    Obj.Adopt",
        "    Stop",
        "End Sub") + "\r\n";

    private static readonly string[] Holding = ["Private mChild As Object"];

    // the fields of an object edited while it lives, with what is done with it at the stop or after: the program waits on, refusing to go on.
    private static async Task<(ExecuteSessionResult Refused, T After)> OwnerEditedAsync<T>(string[] fields, Func<EnvironmentSessionProvider,
        Func<Task<ExecuteSessionResult>>, Task<T>> then)
    {
        ExecuteSessionResult? refused = null;
        T after = default!;
        await ModuleWorkspace.InspectAsync([("Child", Child), ("Owner", Owner(true, Holding))], Owning, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadOnlyAsync(provider, ["Owner"], ("Child", ModuleType.ClassModule, Child),
                ("Owner", ModuleType.ClassModule, Owner(fields.Contains(Holding[0]), fields)), ("Program", ModuleType.StdModule, Owning));
            refused = await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
            after = await then(provider, runAgain);
        }, debug: true);

        return (refused!, after);
    }

    // an object is let go of with the fields it was created with: a field added to its class while it lives has no storage on it, and a field removed from
    // its class still holds the object it was set to, which goes when it does.
    [TestMethod]
    [DataRow(new[] { "Private mChild As Object", "Private mOther As Object" }, DisplayName = "a field added to the class of an object the program holds")]
    [DataRow(new[] { "Private mKept As Object" }, DisplayName = "the field that holds an object removed from its class")]
    public async Task AnObjectOfAClassWhoseFieldsWereEdited_LetGoOfAtTheStop_LetsGoOfWhatItHolds(string[] fields)
    {
        var (refused, released) = await OwnerEditedAsync(fields, (provider, _) => new HostDebugExecuteHandler(provider, Substitute.For<IVerboseMessageBuilder>())
            .Handle(new HostDebugExecuteParams { FrameId = 0, Json = PlatformJson.Serialize(Statement("Set Obj = Nothing")) }, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.AreEqual(ExecutionOutcome.Refused, refused.Outcome, "the object's layout is not applied to it");
        Assert.IsTrue(released.Success, released.Error);
        CollectionAssert.AreEqual(new[] { "owner gone", "child gone" }, released.Output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    [DataRow(new[] { "Private mChild As Object", "Private mOther As Object" }, DisplayName = "a field added to the class of an object the program holds")]
    [DataRow(new[] { "Private mKept As Object" }, DisplayName = "the field that holds an object removed from its class")]
    public async Task AnObjectOfAClassWhoseFieldsWereEdited_IsLetGoOf_WhenTheProgramIsRunAgain(string[] fields)
    {
        var (_, again) = await OwnerEditedAsync(fields, (_, runAgain) => runAgain().WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.AreEqual(ExecutionOutcome.Suspended, again.Outcome, again.ErrorMessage);
    }

    // how the language server turns text into the statement the host runs at a stop: the one statement of a procedure written for the purpose.
    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
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
