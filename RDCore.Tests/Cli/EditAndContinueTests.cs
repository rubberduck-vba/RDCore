using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// An edit made while a program waits at a <c>Stop</c>: applied when the program goes on, or refused with what it is (plan-edit-and-continue §2). The program,
/// its edits and what MS-VBA does with each are those of the Excel grid the plan was measured with; where RD-VBA applies an edit MS-VBA resets the project for,
/// the test says so.
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
        "Public Type T",
        "    X As Long",
        "End Type",
        "Public Enum E",
        "    E1 = 1",
        "End Enum",
        "Public G As Long",
        "Public H As Long",
        "Public Obj As C",
        "",
        "Public Sub Main()",
        "    G = 1",
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
        "    Debug.Print \"other:1 obj=\" & Obj.Value",
        "End Sub");

    // what the program prints after the Stop, as MS-VBA prints it with no edit.
    private static readonly string[] AsItWas = ["callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10", "main:end"];

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

    // the program run to its Stop, the edits made as the analysis pass makes them, and the program resumed.
    private static async Task<ExecuteSessionResult> EditedAtTheStopAsync(params string[] edits)
    {
        ExecuteSessionResult? resumed = null;
        await ModuleWorkspace.InspectAsync([("C", Class)], Program, async (provider, stopped, runAgain) =>
        {
            Assert.AreEqual(ExecutionOutcome.Suspended, stopped.Outcome, stopped.ErrorMessage);
            _ = await ModuleWorkspace.ReloadAsync(provider,
                ("C", ModuleType.ClassModule, Edited(Class, edits, "C")), ("Program", ModuleType.StdModule, Edited(Program, edits, "Program")));
            resumed = await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
        }, debug: true);

        return resumed!;
    }

    private static string[] Output(ExecuteSessionResult result) => [.. result.Output.Select(line => line.Trim())];

    [TestMethod]
    public async Task NoEdit_TheProgramGoesOnAsItWas()
    {
        var resumed = await EditedAtTheStopAsync();

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(AsItWas, Output(resumed));
    }

    // what MS-VBA applies and goes on with, and RD-VBA does in this slice: every edit that leaves the program's frames alone.
    [TestMethod]
    [DataRow(new[] { "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:EDITED obj=\" & Obj.Value" },
        new[] { "callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:EDITED obj=10", "main:end" },
        DisplayName = "a statement of a procedure the program is not in")]
    [DataRow(new[] { "Program|InsertAfter|Private Const K|Private Const K2 As Long = 2" }, null, DisplayName = "a Const added")]
    [DataRow(new[] { "Program|InsertAfter|Private Declare PtrSafe|Private Declare PtrSafe Function GetCurrentProcessId Lib \"kernel32\" () As Long" }, null,
        DisplayName = "a Declare added")]
    [DataRow(new[] { "Program|InsertAfter|Public H As Long|Private J As Long" }, null, DisplayName = "a Private module variable added")]
    [DataRow(new[] { "C|Replace|Value = mValue * 10|    Value = mValue * 100" },
        new[] { "callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=100", "other:1 obj=100", "main:end" },
        DisplayName = "a method of a class whose object the program holds")]
    public async Task AnEditMsVbaApplies_IsApplied(string[] edits, string[]? expected)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(expected ?? AsItWas, Output(resumed));
    }

    // what MS-VBA resets the project for, and RD-VBA applies: the program reads it afresh where it next uses it.
    [TestMethod]
    [DataRow(new[] { "Program|InsertAfter|Public H As Long|Public J As Long" }, null, DisplayName = "a Public module variable added")]
    [DataRow(new[] { "Program|Delete|Public H As Long" }, null, DisplayName = "a module variable nothing uses removed")]
    [DataRow(new[] { "Program|Replace|Private Const K|Private Const K As Long = 2" },
        new[] { "callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=2 obj=10", "other:1 obj=10", "main:end" },
        DisplayName = "a Const revalued, read where it is next used")]
    [DataRow(new[] { "Program|InsertAfter|End Type|Public Type T2\\n    Y As Long\\nEnd Type" }, null, DisplayName = "a Type added")]
    [DataRow(new[] { "Program|InsertAfter|End Enum|Public Enum E2\\n    F1 = 1\\nEnd Enum" }, null, DisplayName = "an Enum added")]
    [DataRow(new[] { "Program|InsertAfter|E1 = 1|    E2m = 2" }, null, DisplayName = "an Enum member added")]
    [DataRow(new[] { "Program|Replace|E1 = 1|    E1 = 3" },
        new[] { "callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=3", "main:after G=2 K=1 obj=10", "other:1 obj=10", "main:end" },
        DisplayName = "an Enum member revalued, read where it is next used")]
    [DataRow(new[] { "Program|InsertAfter|Debug.Print \"other:1|End Sub\\n\\nPublic Sub Added()\\n    Debug.Print \"added\"" }, null, DisplayName = "a procedure added")]
    [DataRow(new[] { "Program|Replace|Public Sub Other()|Public Sub Other(Optional ByVal q As Long)" }, null,
        DisplayName = "the signature of a procedure the program is not in")]
    [DataRow(new[] { "Program|Replace|Private Declare PtrSafe|Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As LongPtr" }, null,
        DisplayName = "a Declare changed")]
    [DataRow(new[] { "Program|InsertBefore|Private Declare PtrSafe|Option Compare Text" }, null, DisplayName = "an Option added")]
    [DataRow(new[] { "C|InsertAfter|End Property|\\nPublic Sub Extra()\\nEnd Sub" }, null, DisplayName = "a method added to a class whose object the program holds")]
    public async Task AnEditMsVbaResetsFor_ThatCanBeAppliedCorrectly_IsApplied(string[] edits, string[]? expected)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(expected ?? AsItWas, Output(resumed));
    }

    // what can't be applied correctly: the program waits as it was, and picks the edit up when it is run again.
    [TestMethod]
    [DataRow(new[] { "Program|Replace|Dim a As Long|    Dim a As Integer" }, "a local of a procedure the program is in, given another type",
        DisplayName = "a local of a procedure the program is in, retyped")]
    [DataRow(new[] { "Program|Replace|Public G As Long|Public G As Integer" }, "a variable or a field that holds a value", DisplayName = "a module variable that holds a value, retyped")]
    [DataRow(new[] { "Program|InsertAfter|    X As Long|    Z As Long" }, "a variable or a field that holds a value", DisplayName = "a field added to a Type a local holds")]
    [DataRow(new[] { "Program|Replace|Public Sub Callee(ByVal p As Long)|Public Sub Callee(ByVal p As Long, Optional ByVal q As Long)" },
        "the signature of a procedure the program is in", DisplayName = "the signature of the procedure the program is in")]
    [DataRow(new[] { "Program|Replace|Static S As Long|    Static S As Integer" }, "a variable or a field that holds a value", DisplayName = "a Static retyped")]
    [DataRow(new[] { "Program|Replace|Debug.Print \"other:1|    Debug.Print \"other:1 obj=\" & Undeclared" }, "does not compile",
        DisplayName = "an edit that makes the code not compile")]
    public async Task AnEditThatCannotBeAppliedCorrectly_IsRefused_AndTheProgramStillWaits(string[] edits, string reason)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome, resumed.ErrorMessage);
        StringAssert.Contains(resumed.ErrorMessage, reason);
    }

    // the edits of the procedures the program is in, and of their layout: applied in later slices (S2, S3). Each is what MS-VBA does with it, where it goes on.
    [TestMethod]
    [Ignore("Edit and continue S2: the body of a procedure the program is in, its Pc remapped")]
    [DataRow(new[] { "Program|Replace|a = 10|    a = 20" }, null, DisplayName = "a statement above the yellow line")]
    [DataRow(new[] { "Program|Replace|Debug.Print \"callee:after-stop|    Debug.Print \"callee:after-stop-EDITED a=\" & a" },
        new[] { "callee:after-stop-EDITED a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10", "main:end" },
        DisplayName = "the statement after the yellow line")]
    [DataRow(new[] { "Program|Replace|a = a + 1|    a = a + 5" },
        new[] { "callee:after-stop a=10", "callee:done a=15 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10", "main:end" },
        DisplayName = "a statement further down")]
    [DataRow(new[] { "Program|Delete|    Stop" }, null, DisplayName = "the yellow line deleted")]
    [DataRow(new[] { "Program|InsertAfter|    Stop|    Debug.Print \"callee:inserted-below\"" },
        new[] { "callee:inserted-below", "callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10", "main:end" },
        DisplayName = "a line inserted right below the yellow line")]
    [DataRow(new[] { "Program|Replace|Debug.Print \"main:after|    Debug.Print \"main:after-EDITED G=\" & G & \" K=\" & K & \" obj=\" & Obj.Value" },
        new[] { "callee:after-stop a=10", "callee:done a=11 S=1 G=2 E1=1", "main:after-EDITED G=2 K=1 obj=10", "other:1 obj=10", "main:end" },
        DisplayName = "a statement of a caller, below its call")]
    [DataRow(new[] { "Program|Replace|Done:|Done2:", "Program|Replace|GoTo Done|    GoTo Done2" }, null, DisplayName = "a label renamed with its GoTo")]
    public async Task AnEditOfAProcedureTheProgramIsIn_IsApplied_AsMsVbaAppliesIt(string[] edits, string[]? expected)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(expected ?? AsItWas, Output(resumed));
    }

    [TestMethod]
    [Ignore("Edit and continue S3: the layout of a frame the program is in, and the fields of live objects")]
    [DataRow(new[] { "Program|InsertAfter|Dim tt As T|    Dim b As Long", "Program|Replace|Debug.Print \"callee:after-stop|    b = 7: Debug.Print \"callee:after-stop b=\" & b" },
        new[] { "callee:after-stop b=7", "callee:done a=11 S=1 G=2 E1=1", "main:after G=2 K=1 obj=10", "other:1 obj=10", "main:end" },
        DisplayName = "a local added to the procedure the program is in, and used")]
    [DataRow(new[] { "Program|InsertAfter|Public Sub Main()|    Dim m As Long" }, null, DisplayName = "a local added to a caller")]
    [DataRow(new[] { "C|InsertAfter|Private mValue As Long|Private mOther As Long" }, null, DisplayName = "a field added to a class whose object the program holds")]
    public async Task AnEditOfTheLayoutOfAFrame_IsApplied_AsMsVbaAppliesIt(string[] edits, string[]? expected)
    {
        var resumed = await EditedAtTheStopAsync(edits);

        Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        CollectionAssert.AreEqual(expected ?? AsItWas, Output(resumed));
    }

    [TestMethod]
    public async Task AnEditOfAProcedureTheProgramIsIn_IsRefusedForNow_NamedSo()
    {
        // S1: the program is not resumed on it, and the refusal says that it is the code of a procedure the program is in, which is not applied yet.
        var resumed = await EditedAtTheStopAsync("Program|Replace|a = a + 1|    a = a + 5");

        Assert.AreEqual(ExecutionOutcome.Refused, resumed.Outcome);
        StringAssert.Contains(resumed.ErrorMessage, "Callee");
        StringAssert.Contains(resumed.ErrorMessage, "not applied to a program that waits yet");
    }
}
