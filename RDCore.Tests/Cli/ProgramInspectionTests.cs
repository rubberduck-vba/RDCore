using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.Tests.Cli;

/// <summary>
/// A program that waits can be looked at: its activations, innermost first, and the variables of each - the parameters and locals of the procedure, and the
/// variables of its module - with the parts of an array or a user-defined type for whoever asks.
/// </summary>
[TestClass]
public sealed class ProgramInspectionTests
{
    private static readonly (string Name, string Source)[] NoClasses = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static Task DebugAsync(string program, Func<EnvironmentSessionProvider, Task> test)
        => ModuleWorkspace.InspectAsync(NoClasses, program, (provider, _, _) => test(provider), cancelAfter: null, debug: true);

    private static readonly string Source = Program(
        "Private Type TPoint",
        "    X As Long",
        "    Y As Long",
        "End Type",
        "Public Total As Long",
        "Public Title As String",
        "Public Sub Main()",
        "    Dim n As Long, s As String, d As Date, v As Variant, flag As Boolean",
        "    Dim arr(1 To 3) As Long, grid(1 To 2, 0 To 1) As Integer",
        "    Dim p As TPoint, o As Object",
        "    Total = 5",
        "    p.X = 4",
        "    Title = \"hi\"",
        "    n = 7",
        "    s = \"a\"\"b\"",
        "    d = #1/2/2000#",
        "    v = 2.5",
        "    flag = True",
        "    arr(2) = 9",
        "    grid(2, 1) = 4",
        "    Helper n",
        "End Sub",
        "Private Sub Helper(ByVal k As Long)",
        "    Dim local As Integer",
        "    local = 3",
        "    Stop",
        "End Sub");

    private static Dictionary<string, HostVariable> Named(HostDebugVariablesResult result) => result.Variables.ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);

    // ---- the stack ----

    [TestMethod]
    public async Task TheStack_IsTheActivationsOfTheProgram_InnermostFirst_EachWithItsPlace()
        => await DebugAsync(Source, provider =>
        {
            var stack = provider.Execution.Stack();

            CollectionAssert.AreEqual(new[] { "Helper", "Main" }, stack.Frames.Select(frame => frame.Procedure).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1 }, stack.Frames.Select(frame => frame.Id).ToArray());
            Assert.IsTrue(stack.Frames.All(frame => frame.Module == "Program"));
            Assert.AreEqual(25, stack.Frames[0].Line, "the Stop");
            Assert.AreEqual(20, stack.Frames[1].Line, "the call to Helper");
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheStack_SaysWhichGoSubsAnActivationIsInside_InnermostFirst()
        => await DebugAsync(Program(
            "Public Sub Main()",
            "    GoSub First",
            "    Exit Sub",
            "First:",
            "    GoSub Second",
            "    Return",
            "Second:",
            "    Stop",
            "    Return",
            "End Sub"), provider =>
        {
            var main = provider.Execution.Stack().Frames.Single();

            Assert.AreEqual(7, main.Line, "the Stop");
            CollectionAssert.AreEqual(new[] { 4, 1 }, main.ReturnLines.ToArray(), "from the GoSub Second, which the GoSub First called");
            return Task.CompletedTask;
        });

    private static readonly string Handled = Program(
        "Public Sub Main()",
        "    Dim z As Long",
        "    On Error GoTo Oops",
        "    z = 1 \\ z",
        "    Stop",
        "    Exit Sub",
        "Oops:",
        "    Stop",
        "    Resume Next",
        "End Sub");

    [TestMethod]
    public async Task TheStack_SaysWhichHandlerAnActivationIsRunning_UntilItResumes()
        => await DebugAsync(Handled, async provider =>
        {
            Assert.AreEqual("Oops", provider.Execution.Stack().Frames.Single().Handler, "stopped in the handler an error brought it to");

            var resumed = await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Suspended, resumed.Outcome, resumed.ErrorMessage);
            Assert.AreEqual(4, resumed.ErrorLine, "the Stop after the statement that failed");
            Assert.IsNull(provider.Execution.Stack().Frames.Single().Handler, "and no longer in the handler");
        });

    [TestMethod]
    public async Task TheStack_NamesTheHandlerByItsLineNumber_WhenThatIsWhatItIsLabelledWith()
        => await DebugAsync(Program(
            "Public Sub Main()",
            "    Dim z As Long",
            "    On Error GoTo 900",
            "    z = 1 \\ z",
            "    Exit Sub",
            "900 Stop",
            "End Sub"), provider =>
        {
            Assert.AreEqual("900", provider.Execution.Stack().Frames.Single().Handler);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheStack_SaysNothingOfAHandler_WhenAnErrorIsNotBeingHandled()
        => await DebugAsync(Program(
            "Public Sub Main()",
            "    On Error GoTo Oops",
            "    Stop",
            "    Exit Sub",
            "Oops:",
            "End Sub"), provider =>
        {
            Assert.IsNull(provider.Execution.Stack().Frames.Single().Handler, "the handler is armed, not running");
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheStack_SaysNothingOfAHandler_ForOnErrorResumeNext()
        => await DebugAsync(Program(
            "Public Sub Main()",
            "    Dim z As Long",
            "    On Error Resume Next",
            "    z = 1 \\ z",
            "    Stop",
            "End Sub"), provider =>
        {
            Assert.IsNull(provider.Execution.Stack().Frames.Single().Handler);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheStack_OfCodeThatUsesNoGoSub_HasNothingToSayOfHowItGotThere()
        => await DebugAsync(Source, provider =>
        {
            Assert.IsTrue(provider.Execution.Stack().Frames.All(frame => frame.ReturnLines.Count == 0));
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheStack_AfterAGoSubReturned_IsNoLongerInsideIt()
        => await DebugAsync(Program(
            "Public Sub Main()",
            "    GoSub First",
            "    Stop",
            "    Exit Sub",
            "First:",
            "    Return",
            "End Sub"), provider =>
        {
            Assert.IsEmpty(provider.Execution.Stack().Frames.Single().ReturnLines);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheVariables_AreInTheOrderTheyAreDeclaredIn_WhateverTheirNames()
        => await DebugAsync(Program(
            "Public Zebra As Long",
            "Public Apple As Long",
            "Public Sub Main()",
            "    Dim mango As Long, banana As Long",
            "    Dim cherry As Long",
            "    Stop",
            "End Sub"), provider =>
        {
            CollectionAssert.AreEqual(new[] { "Zebra", "Apple" }, provider.Execution.Variables(0, HostVariableScope.Module, 0).Variables.Select(variable => variable.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "mango", "banana", "cherry" }, provider.Execution.Variables(0, HostVariableScope.Locals, 0).Variables.Select(variable => variable.Name).ToArray());
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheParameters_ComeBeforeTheLocals()
        => await DebugAsync(Program(
            "Public Sub Main()",
            "    Helper 1, 2",
            "End Sub",
            "Private Sub Helper(ByVal zz As Long, ByVal aa As Long)",
            "    Dim first As Long",
            "    Stop",
            "End Sub"), provider =>
        {
            CollectionAssert.AreEqual(new[] { "zz", "aa", "first" }, provider.Execution.Variables(0, HostVariableScope.Locals, 0).Variables.Select(variable => variable.Name).ToArray());
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheStack_OfNoProgram_IsEmpty()
        => await DebugAsync(Program("Public Sub Main()", "End Sub"), provider =>
        {
            Assert.IsEmpty(provider.Execution.Stack().Frames);
            return Task.CompletedTask;
        });

    // ---- locals ----

    [TestMethod]
    public async Task TheLocals_AreTheParametersAndTheVariablesOfTheProcedure_WithTheirValuesAndTypes()
        => await DebugAsync(Source, provider =>
        {
            var locals = Named(provider.Execution.Variables(0, HostVariableScope.Locals, 0));

            Assert.AreEqual("7", locals["k"].Value);
            Assert.AreEqual("Long", locals["k"].Type);
            Assert.AreEqual("3", locals["local"].Value);
            Assert.AreEqual("Integer", locals["local"].Type);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheLocals_OfACallingActivation_AreItsOwn()
        => await DebugAsync(Source, provider =>
        {
            var locals = Named(provider.Execution.Variables(1, HostVariableScope.Locals, 0));

            Assert.AreEqual("7", locals["n"].Value);
            Assert.AreEqual("\"a\"\"b\"", locals["s"].Value, "a string is quoted, and its quotes are doubled");
            Assert.AreEqual("String", locals["s"].Type);
            Assert.AreEqual("#1/2/2000#", locals["d"].Value);
            Assert.AreEqual("Date", locals["d"].Type);
            Assert.AreEqual("2.5", locals["v"].Value);
            Assert.AreEqual("Variant/Double", locals["v"].Type);
            Assert.AreEqual("True", locals["flag"].Value);
            Assert.IsFalse(locals.ContainsKey("k"), "and not the parameter of the procedure it called");
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AnObject_ThatIsNothing_IsNothing()
        => await DebugAsync(Source, provider =>
        {
            var locals = Named(provider.Execution.Variables(1, HostVariableScope.Locals, 0));

            Assert.AreEqual("Nothing", locals["o"].Value);
            return Task.CompletedTask;
        });

    // ---- parts ----

    [TestMethod]
    public async Task AnArray_HasItsElementsForWhoeverAsks()
        => await DebugAsync(Source, provider =>
        {
            var arr = Named(provider.Execution.Variables(1, HostVariableScope.Locals, 0))["arr"];

            Assert.AreEqual("Long(1 To 3)", arr.Type);
            Assert.AreNotEqual(0, arr.Reference);

            var elements = provider.Execution.Variables(0, HostVariableScope.Locals, arr.Reference);

            CollectionAssert.AreEqual(new[] { "(1)", "(2)", "(3)" }, elements.Variables.Select(element => element.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "0", "9", "0" }, elements.Variables.Select(element => element.Value).ToArray());
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AnArrayOfSeveralDimensions_ListsItsElementsWithAllTheirSubscripts()
        => await DebugAsync(Source, provider =>
        {
            var grid = Named(provider.Execution.Variables(1, HostVariableScope.Locals, 0))["grid"];

            Assert.AreEqual("Integer(1 To 2, 0 To 1)", grid.Type);

            var elements = provider.Execution.Variables(0, HostVariableScope.Locals, grid.Reference);

            CollectionAssert.AreEqual(new[] { "(1, 0)", "(1, 1)", "(2, 0)", "(2, 1)" }, elements.Variables.Select(element => element.Name).ToArray());
            Assert.AreEqual("4", elements.Variables.Single(element => element.Name == "(2, 1)").Value);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task AUserDefinedType_HasItsFields()
        => await DebugAsync(Source, provider =>
        {
            var p = Named(provider.Execution.Variables(1, HostVariableScope.Locals, 0))["p"];

            Assert.AreEqual("TPoint", p.Type);

            var fields = provider.Execution.Variables(0, HostVariableScope.Locals, p.Reference);

            CollectionAssert.AreEqual(new[] { "X", "Y" }, fields.Variables.Select(field => field.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "4", "0" }, fields.Variables.Select(field => field.Value).ToArray());
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task APartOfAValue_ThatIsNoReferenceAnyMore_IsNothing()
        => await DebugAsync(Source, provider =>
        {
            Assert.IsEmpty(provider.Execution.Variables(0, HostVariableScope.Locals, 999).Variables);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheReferences_AreForgottenWhenTheProgramGoesOn()
        => await DebugAsync(Source.Replace("    Stop\r\nEnd Sub", "    Stop\r\n    Stop\r\nEnd Sub"), async provider =>
        {
            var arr = Named(provider.Execution.Variables(1, HostVariableScope.Locals, 0))["arr"];
            Assert.IsNotEmpty(provider.Execution.Variables(0, HostVariableScope.Locals, arr.Reference).Variables);

            var resumed = await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Suspended, resumed.Outcome, "at the second Stop");
            Assert.IsEmpty(provider.Execution.Variables(0, HostVariableScope.Locals, arr.Reference).Variables);
        });

    // ---- module ----

    [TestMethod]
    public async Task TheModuleVariables_AreThoseOfTheModuleTheProcedureIsDeclaredIn()
        => await DebugAsync(Source, provider =>
        {
            var module = Named(provider.Execution.Variables(0, HostVariableScope.Module, 0));

            Assert.AreEqual("5", module["Total"].Value);
            Assert.AreEqual("\"hi\"", module["Title"].Value);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheVariables_OfAnActivationThatIsNotOnTheStack_AreNone()
        => await DebugAsync(Source, provider =>
        {
            Assert.IsEmpty(provider.Execution.Variables(7, HostVariableScope.Locals, 0).Variables);
            Assert.IsEmpty(provider.Execution.Variables(-1, HostVariableScope.Locals, 0).Variables);
            return Task.CompletedTask;
        });

    [TestMethod]
    public async Task TheVariables_OfAProgramThatIsOver_AreNone()
        => await DebugAsync(Source, async provider =>
        {
            _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

            Assert.IsEmpty(provider.Execution.Variables(0, HostVariableScope.Locals, 0).Variables);
            Assert.IsEmpty(provider.Execution.Stack().Frames);
        });
}
