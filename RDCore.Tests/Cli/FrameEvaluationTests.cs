using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RDCore.CLI.Host;
using RDCore.CLI.Host.Handlers;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Cli;

/// <summary>
/// An expression can be evaluated in any activation of a program that waits, as if it were written in the procedure that activation is of - its locals, its parameters,
/// its module - and the program waits still, wherever it is, whatever the expression called.
/// </summary>
[TestClass]
public sealed class FrameEvaluationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly string Source = Program(
        "Public Total As Long",
        "Public Sub Main()",
        "    Dim n As Long, s As String, arr(1 To 3) As Long",
        "    Total = 5",
        "    n = 7",
        "    s = \"ab\"",
        "    arr(2) = 9",
        "    Helper n",
        "End Sub",
        "Private Sub Helper(ByVal k As Long)",
        "    Dim local As Integer",
        "    local = 3",
        "    Stop",
        "End Sub",
        "Public Function Twice(ByVal x As Long) As Long",
        "    Twice = x * 2",
        "End Function",
        "Public Function Noisy() As Long",
        "    Debug.Print \"from the call\"",
        "    Noisy = 1",
        "End Function",
        "Public Function Stopping() As Long",
        "    Stop",
        "    Stopping = 1",
        "End Function",
        "Public Function Ending() As Long",
        "    End",
        "End Function");

    private static Task DebugAsync(string program, Func<EnvironmentSessionProvider, Task> test)
        => ModuleWorkspace.InspectAsync([], program, (provider, _, _) => test(provider), cancelAfter: null, debug: true);

    // how the language server turns text into the tree the host evaluates: the right-hand side of an assignment written for the purpose.
    private static string Parsed(string expression)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Eval.bas"), $"Public Sub E()\r\n__e = {expression}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        var assignment = Descendants(parse.SyntaxTree!).OfType<AssignmentStatementNode>().Single();
        return PlatformJson.Serialize<ExpressionNode>(assignment.Value);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node) => node.Children.SelectMany(Descendants).Prepend(node);

    private static Task<HostDebugEvaluateResult> Evaluate(EnvironmentSessionProvider provider, int frame, string expression)
        => new HostDebugEvaluateHandler(provider, Substitute.For<IVerboseMessageBuilder>())
            .Handle(new HostDebugEvaluateParams { FrameId = frame, Json = Parsed(expression) }, CancellationToken.None).WaitAsync(Patience);

    // ---- names ----

    [TestMethod]
    public async Task AnExpression_SeesTheParametersAndLocalsOfTheActivationItIsEvaluatedIn()
        => await DebugAsync(Source, async provider =>
        {
            var sum = await Evaluate(provider, 0, "k + local");

            Assert.IsTrue(sum.Success, sum.Error);
            Assert.AreEqual("10", sum.Value);
            Assert.AreEqual("Long", sum.Type);
        });

    [TestMethod]
    public async Task AnExpression_EvaluatedInACallingActivation_SeesThatActivationsLocals()
        => await DebugAsync(Source, async provider =>
        {
            var doubled = await Evaluate(provider, 1, "n * 2");
            Assert.IsTrue(doubled.Success, doubled.Error);
            Assert.AreEqual("14", doubled.Value);

            var text = await Evaluate(provider, 1, "s & \"!\"");
            Assert.AreEqual("\"ab!\"", text.Value);
            Assert.AreEqual("String", text.Type);

            var element = await Evaluate(provider, 1, "arr(2)");
            Assert.AreEqual("9", element.Value);
        });

    [TestMethod]
    public async Task AnExpression_DoesNotSeeTheLocalsOfAnotherActivation()
        => await DebugAsync(Source, async provider =>
        {
            var fromTheInner = await Evaluate(provider, 0, "n");
            Assert.IsFalse(fromTheInner.Success, "n is a local of Main, and the innermost activation is Helper");

            var fromTheOuter = await Evaluate(provider, 1, "k");
            Assert.IsFalse(fromTheOuter.Success, "and k is a parameter of Helper");
        });

    [TestMethod]
    public async Task AnExpression_SeesTheVariablesOfTheModule_FromAnyActivation()
        => await DebugAsync(Source, async provider =>
        {
            Assert.AreEqual("6", (await Evaluate(provider, 0, "Total + 1")).Value);
            Assert.AreEqual("6", (await Evaluate(provider, 1, "Total + 1")).Value);
        });

    // ---- calls ----

    [TestMethod]
    public async Task AnExpression_CanCallAFunction_AndTheProgramWaitsWhereItDid()
        => await DebugAsync(Source, async provider =>
        {
            var called = await Evaluate(provider, 0, "Twice(k)");

            Assert.IsTrue(called.Success, called.Error);
            Assert.AreEqual("14", called.Value);
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
            Assert.AreEqual(2, provider.Session.CallStack.Depth, "what the call pushed is gone");
            Assert.AreEqual("Helper", provider.Execution.Stack().Frames[0].Procedure);
        });

    [TestMethod]
    public async Task AnExpression_ThatCallsAFunctionThatPrints_ReturnsWhatItPrinted_ApartFromTheProgram()
        => await DebugAsync(Source, async provider =>
        {
            var called = await Evaluate(provider, 0, "Noisy()");

            Assert.IsTrue(called.Success, called.Error);
            CollectionAssert.AreEqual(new[] { "from the call" }, called.Output.Select(line => line.Trim()).ToArray());
        });

    [TestMethod]
    public async Task AnExpression_ThatCallsAFunctionThatStops_IsStopped_AndNotTheProgram()
        => await DebugAsync(Source, async provider =>
        {
            var stopped = await Evaluate(provider, 0, "Stopping()");

            Assert.IsFalse(stopped.Success);
            StringAssert.Contains(stopped.Error, "Stop");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
            Assert.AreEqual(2, provider.Session.CallStack.Depth);
            Assert.IsNull(provider.Session.Halt.Pending);
        });

    [TestMethod]
    public async Task AnExpression_ThatCallsAFunctionThatEnds_EndsTheProgram()
        => await DebugAsync(Source, async provider =>
        {
            var ended = await Evaluate(provider, 0, "Ending()");

            Assert.IsFalse(ended.Success);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
            Assert.AreEqual(0, provider.Session.CallStack.Depth);
        });

    // ---- what is not a value ----

    [TestMethod]
    public async Task AnExpression_ThatRaisesAnError_HasNoValue_AndSaysWhy()
        => await DebugAsync(Source, async provider =>
        {
            var divided = await Evaluate(provider, 0, "1 \\ 0");

            Assert.IsFalse(divided.Success);
            StringAssert.Contains(divided.Error, "Division by zero");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
        });

    [TestMethod]
    public async Task AnExpression_ThatNamesSomethingThatIsNotDefined_HasNoValue()
        => await DebugAsync(Source, async provider =>
        {
            var undefined = await Evaluate(provider, 0, "Total + Nothingness");

            Assert.IsFalse(undefined.Success);
            StringAssert.Contains(undefined.Error, "Nothingness");
        });

    [TestMethod]
    public async Task AnExpression_InAnActivationThatIsNotOnTheStack_HasNoValue()
        => await DebugAsync(Source, async provider =>
        {
            Assert.IsFalse((await Evaluate(provider, 5, "Total")).Success);
            Assert.IsFalse((await Evaluate(provider, -1, "Total")).Success);
        });

    [TestMethod]
    public async Task AnExpression_WhenNoProgramWaits_HasNoValue()
        => await DebugAsync(Source, async provider =>
        {
            _ = await new HostDebugTerminateHandler(provider).Handle(new HostDebugTerminateParams(), CancellationToken.None).WaitAsync(Patience);

            var result = await Evaluate(provider, 0, "Total");

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Error, "no program");
        });

    // ---- afterwards ----

    [TestMethod]
    public async Task TheVariablesOfTheInnermostActivation_AreTheSame_AfterAnExpressionWasEvaluatedInAnother()
        => await DebugAsync(Source, async provider =>
        {
            _ = await Evaluate(provider, 1, "n");

            var locals = provider.Execution.Variables(0, HostVariableScope.Locals, 0).Variables.ToDictionary(variable => variable.Name);

            Assert.AreEqual("7", locals["k"].Value);
            Assert.AreEqual("3", locals["local"].Value);
        });

    [TestMethod]
    public async Task TheProgram_GoesOnAfterExpressionsWereEvaluatedInIt()
        => await DebugAsync(Source, async provider =>
        {
            _ = await Evaluate(provider, 1, "n * 2");
            _ = await Evaluate(provider, 0, "Twice(k)");

            var resumed = await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
                .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        });

    [TestMethod]
    public async Task TheValueOfAnArray_HasItsElementsForWhoeverAsks()
        => await DebugAsync(Source, async provider =>
        {
            var array = await Evaluate(provider, 1, "arr");

            Assert.IsTrue(array.Success, array.Error);
            Assert.AreEqual("Long(1 To 3)", array.Type);
            Assert.AreNotEqual(0, array.Reference);
            CollectionAssert.AreEqual(
                new[] { "0", "9", "0" },
                provider.Execution.Variables(0, HostVariableScope.Locals, array.Reference).Variables.Select(element => element.Value).ToArray());
        });
}
