using RDCore.Runtime.Compiled;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Components;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.CompiledDirectCallTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// A <c>MsgBox</c> is the interpreter's: the call waits on whoever answers the dialog, so compiled code would gain nothing by making it, and a call that
/// shows a dialog is never made twice. A <c>Stop</c> and an <c>End</c> are the interpreter's too: they suspend the program or end it. A procedure that does
/// nothing else is run by the interpreter, saying why; one that does more is compiled, the interpreter making only those instructions.
/// </summary>
[TestClass]
public sealed class CompiledDialogTests
{
    // a prompt whose answer an If tests, with a Stop and an End in the branch the answer takes: nothing in it would gain from compiled code.
    private static readonly string[] Prompt =
    [
        "    Const message As String = \"Not implemented.\" & vbNewLine & \"Will you implement it?\"",
        "    If MsgBox(message, vbInformation + vbYesNo, \"Battleship\") = vbYes Then",
        "        Stop",
        "        End",
        "    End If",
    ];

    // the program run by the interpreter, then by compiled code, every MsgBox answered with OK: what it printed alike, and the procedures compiled code was asked for.
    private static async Task<IReadOnlyDictionary<string, CompilationResult>> RunBothWaysAsync(string program)
    {
        CompiledExecutionTests.RequirePayloads();
        string? interpreted = null;
        using (CompiledExecution.Running(VBExecutionEngine.Interpreter))
        {
            await ModuleWorkspace.InspectAsync([], program, (_, run, _) =>
            {
                interpreted = Observed(run);
                return Task.CompletedTask;
            }, engine: VBExecutionEngine.Interpreter, interaction: new AnswersOk());
        }

        IReadOnlyDictionary<string, CompilationResult> compiled = new Dictionary<string, CompilationResult>();
        using (CompiledExecution.Running(VBExecutionEngine.Compiled))
        {
            await ModuleWorkspace.InspectAsync([], program, (provider, run, _) =>
            {
                Assert.AreEqual(interpreted, Observed(run));
                compiled = CompiledExecutionTests.ByName(provider);
                return Task.CompletedTask;
            }, engine: VBExecutionEngine.Compiled, interaction: new AnswersOk());
        }

        return compiled;
    }

    private static string Said(IEnumerable<Ineligibility> reasons) => string.Join("; ", reasons.Select(reason => $"{reason.Kind} {reason.Detail}"));

    [TestMethod]
    public async Task AProcedureThatOnlyPrompts_ThenStopsOrEnds_IsRunByTheInterpreter_SayingWhy()
    {
        var compiled = await RunBothWaysAsync(Program([
            "Public Sub Main()",
            "    PlayOtherInterface",
            "    Debug.Print \"answered\"",
            "End Sub",
            "Public Sub PlayOtherInterface()",
            .. Prompt,
            "End Sub"]));

        var prompting = compiled["PlayOtherInterface"];
        Assert.IsNull(prompting.Body, Said(prompting.Stepped));
        var said = prompting.Reasons.Select(reason => (reason.Kind, reason.Detail)).ToList();
        CollectionAssert.IsSubsetOf(
            new[] { (IneligibilityKind.Call, "MsgBox"), (IneligibilityKind.Instruction, "Break"), (IneligibilityKind.Instruction, "Halt") }, said, Said(prompting.Reasons));
    }

    [TestMethod]
    public async Task AProcedureThatPromptsAndComputes_IsCompiled_TheInterpreterMakingThePromptAlone()
    {
        var compiled = await RunBothWaysAsync(Program([
            "Public Sub Main()",
            "    Debug.Print Counted(1000)",
            "End Sub",
            "Public Function Counted(ByVal N As Long) As Long",
            .. Prompt,
            "    Dim I As Long",
            "    For I = 1 To N",
            "        Counted = (Counted + I) Mod 7919",
            "    Next",
            "End Function"]));

        var counting = compiled["Counted"];
        Assert.IsNotNull(counting.Body, Said(counting.Reasons));
        Assert.IsTrue(counting.Stepped.Any(reason => reason is { Kind: IneligibilityKind.Call, Detail: "MsgBox" }), Said(counting.Stepped));
    }
}
