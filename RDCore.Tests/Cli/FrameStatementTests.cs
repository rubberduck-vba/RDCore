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
/// A statement can be run in any activation of a program that waits, as if it were written in the procedure that activation is of: what it assigns is what the program
/// goes on with.
/// </summary>
[TestClass]
public sealed class FrameStatementTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly string Source = Program(
        "Public Total As Long",
        "Public Sub Main()",
        "    Dim n As Long",
        "    n = 7",
        "    Helper n",
        "    Debug.Print \"main \" & n",
        "End Sub",
        "Private Sub Helper(ByVal k As Long)",
        "    Dim local As Long",
        "    local = 3",
        "    Stop",
        "    Debug.Print \"helper \" & local & \" \" & k & \" \" & Total",
        "End Sub",
        "Public Sub Stopper()",
        "    Stop",
        "End Sub");

    private static Task DebugAsync(Func<EnvironmentSessionProvider, Task> test)
        => ModuleWorkspace.InspectAsync([], Source, (provider, _, _) => test(provider), debug: true);

    // how the language server turns text into the statement the host runs: the one statement of a procedure written for the purpose.
    private static SyntaxNode Statement(string text)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Execute.bas"), $"Public Sub E()\r\n{text}\r\nEnd Sub\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Single().Children.OfType<StatementNode>().Single();
    }

    private static Task<HostDebugEvaluateResult> Execute(EnvironmentSessionProvider provider, int frame, string statement)
        => new HostDebugExecuteHandler(provider, Substitute.For<IVerboseMessageBuilder>())
            .Handle(new HostDebugExecuteParams { FrameId = frame, Json = PlatformJson.Serialize(Statement(statement)) }, CancellationToken.None).WaitAsync(Patience);

    private static async Task<string> ValueOf(EnvironmentSessionProvider provider, int frame, string name)
    {
        var variables = provider.Execution.Variables(frame, HostVariableScope.Locals, 0).Variables.ToDictionary(variable => variable.Name);
        return variables[name].Value;
    }

    private static async Task<ExecuteSessionResult> ResumeAsync(EnvironmentSessionProvider provider)
        => await new HostDebugResumeHandler(provider, NullLogger<HostDebugResumeHandler>.Instance)
            .Handle(new HostDebugResumeParams(), CancellationToken.None).WaitAsync(Patience);

    [TestMethod]
    public async Task AnAssignment_ToALocalOfTheInnermostActivation_IsWhatTheProgramGoesOnWith()
        => await DebugAsync(async provider =>
        {
            var done = await Execute(provider, 0, "local = 99");

            Assert.IsTrue(done.Success, done.Error);
            Assert.AreEqual("99", await ValueOf(provider, 0, "local"));
            var resumed = await ResumeAsync(provider);
            Assert.IsTrue(resumed.Output.Any(line => line.Contains("helper 99 7")), string.Join("|", resumed.Output));
        });

    [TestMethod]
    public async Task AnAssignment_ToALocalOfACallingActivation_ChangesThatActivation()
        => await DebugAsync(async provider =>
        {
            var done = await Execute(provider, 1, "n = 100");

            Assert.IsTrue(done.Success, done.Error);
            Assert.AreEqual("100", await ValueOf(provider, 1, "n"));
            Assert.AreEqual("7", await ValueOf(provider, 0, "k"), "the parameter was passed by value");
            var resumed = await ResumeAsync(provider);
            Assert.IsTrue(resumed.Output.Any(line => line.Contains("main 100")), string.Join("|", resumed.Output));
        });

    [TestMethod]
    public async Task AnAssignment_ToAVariableOfTheModule_IsSeenByTheProgram()
        => await DebugAsync(async provider =>
        {
            _ = await Execute(provider, 0, "Total = 42");

            var resumed = await ResumeAsync(provider);
            Assert.IsTrue(resumed.Output.Any(line => line.Contains("helper 3 7 42")), string.Join("|", resumed.Output));
        });

    [TestMethod]
    public async Task AStatement_ThatPrints_HasItsOutputApartFromTheProgram()
        => await DebugAsync(async provider =>
        {
            var done = await Execute(provider, 0, "Debug.Print \"hello\"");

            Assert.IsTrue(done.Success, done.Error);
            CollectionAssert.AreEqual(new[] { "hello" }, done.Output.Select(line => line.Trim()).ToArray());
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
        });

    [TestMethod]
    public async Task AStatement_ThatRaisesAnError_Fails_AndTheProgramStillWaits()
        => await DebugAsync(async provider =>
        {
            var done = await Execute(provider, 0, "local = 1 \\ 0");

            Assert.IsFalse(done.Success);
            StringAssert.Contains(done.Error, "Division by zero");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
            Assert.AreEqual("3", await ValueOf(provider, 0, "local"), "the assignment did not happen");
        });

    [TestMethod]
    public async Task AStatement_ThatCallsSomethingThatStops_IsStopped_AndNotTheProgram()
        => await DebugAsync(async provider =>
        {
            var done = await Execute(provider, 0, "Stopper");

            Assert.IsFalse(done.Success);
            StringAssert.Contains(done.Error, "Stop");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
            Assert.AreEqual(2, provider.Session.CallStack.Depth);
        });

    [TestMethod]
    public async Task AStatement_InAnActivationThatIsNotOnTheStack_Fails()
        => await DebugAsync(async provider =>
        {
            var done = await Execute(provider, 5, "local = 1");

            Assert.IsFalse(done.Success);
        });

    [TestMethod]
    public async Task AStatement_SpellingALocalOtherwise_IsAboutThatLocal()
        => await DebugAsync(async provider =>
        {
            // MS-VBAL 3.3.5: a name is the same name in any case.
            var done = await Execute(provider, 0, "Debug.Print LOCAL");

            Assert.IsTrue(done.Success, done.Error);
            CollectionAssert.AreEqual(new[] { "3" }, done.Output.Select(line => line.Trim()).ToArray());
        });

    [TestMethod]
    public async Task AStatement_WithANameTheProcedureDoesNotSee_DeclaresItAnEmptyVariant()
        => await DebugAsync(async provider =>
        {
            // MS-VBAL 5.6.10: with no Option Explicit, the name declares a variable, as it would in the procedure's code: ?x prints Empty, which is no text.
            var done = await Execute(provider, 0, "Debug.Print nowhere");

            Assert.IsTrue(done.Success, done.Error);
            CollectionAssert.AreEqual(new[] { string.Empty }, done.Output.Select(line => line.Trim()).ToArray());
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
        });

    [TestMethod]
    public async Task AVariableAStatementDeclared_IsALocalOfTheActivation_ThatTheNextStatementSees()
        => await DebugAsync(async provider =>
        {
            // in VBA and VB6 the variable is a local of the procedure (ImplicitDeclarationScope.Procedure): of this activation, and not of its caller.
            _ = await Execute(provider, 0, "fresh = 41 + 1");

            var read = await Execute(provider, 0, "Debug.Print FRESH");

            CollectionAssert.AreEqual(new[] { "42" }, read.Output.Select(line => line.Trim()).ToArray());
            Assert.AreEqual("42", await ValueOf(provider, 0, "fresh"));
            Assert.AreEqual(ExecutionOutcome.Completed, (await ResumeAsync(provider)).Outcome, "the program goes on with a local it did not declare");
        });

    [TestMethod]
    public async Task AStatement_WithANameTheProcedureDoesNotSee_IsRefused_InAModuleThatSaysOptionExplicit()
        => await ModuleWorkspace.InspectAsync([], "Option Explicit\r\n" + Source, async (provider, _, _) =>
        {
            var done = await Execute(provider, 0, "Debug.Print nowhere");

            Assert.IsFalse(done.Success);
            Assert.AreEqual(string.Format(RDCore.CLI.Resources.Host_VariableNotDefined, "nowhere"), done.Error);
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
        }, debug: true);

    [TestMethod]
    public async Task InBasic_AStatement_WithANameTheProcedureDoesNotSee_DeclaresAVariableOfTheModule()
        => await ModuleWorkspace.InspectAsync([], Source, async (provider, _, _) =>
        {
            // a BASIC shell's variables are the module's (ImplicitDeclarationScope.Module), and a statement typed at a stop declares one as one typed at the prompt does.
            var done = await Execute(provider, 0, "kept = 7");

            Assert.IsTrue(done.Success, done.Error);
            Assert.IsFalse(provider.Execution.Variables(0, HostVariableScope.Locals, 0).Variables.Any(variable => variable.Name == "kept"), "not a local of the activation");
            Assert.IsTrue(provider.Execution.Variables(0, HostVariableScope.Module, 0).Variables.Any(variable => variable.Name == "kept"), "a variable of the module");
            var read = await Execute(provider, 1, "Debug.Print KEPT");
            CollectionAssert.AreEqual(new[] { "7" }, read.Output.Select(line => line.Trim()).ToArray(), "the caller's activation sees it too: it is the module's");
        }, debug: true, language: SupportedLanguages.BASIC);

    [TestMethod]
    public async Task AnAssignment_ToAFunctionOfTheLibrary_IsRefusedAsTheProceduresOwnCodeWouldBe_AndDoesNotRun()
        => await DebugAsync(async provider =>
        {
            // Shell is VBA.Shell: the name matches, so it declares nothing (MS-VBAL 5.6.10), and a function is not a variable to assign (MS-VBAL 5.4.3.8).
            var done = await Execute(provider, 0, "Shell = 7");

            Assert.IsFalse(done.Success);
            StringAssert.Contains(done.Error, "Shell");
            Assert.AreEqual(ProgramState.Suspended, provider.Execution.State);
        });

    [TestMethod]
    public async Task ALocalNamedLikeAFunctionOfTheLibrary_ShadowsIt()
        => await ModuleWorkspace.InspectAsync([], Program(
            "Public Sub Main()",
            "    Dim shell As Long",
            "    shell = 5",
            "    Stop",
            "End Sub"), async (provider, _, _) =>
        {
            var assigned = await Execute(provider, 0, "Shell = 6");
            var read = await Execute(provider, 0, "Debug.Print SHELL");

            Assert.IsTrue(assigned.Success, assigned.Error);
            CollectionAssert.AreEqual(new[] { "6" }, read.Output.Select(line => line.Trim()).ToArray());
        }, debug: true);

    [TestMethod]
    public async Task AnEnd_RunInTheProgram_EndsIt_AndSaysSo()
        => await DebugAsync(async provider =>
        {
            var done = await Execute(provider, 0, "End");

            Assert.IsTrue(done.EndedTheProgram);
            Assert.AreEqual(ProgramState.Idle, provider.Execution.State);
        });

    [TestMethod]
    public async Task TheProgram_GoesOnAfterStatementsWereRunInIt()
        => await DebugAsync(async provider =>
        {
            _ = await Execute(provider, 0, "local = local + 1");
            _ = await Execute(provider, 1, "n = n + 1");

            var resumed = await ResumeAsync(provider);

            Assert.AreEqual(ExecutionOutcome.Completed, resumed.Outcome, resumed.ErrorMessage);
        });
}
