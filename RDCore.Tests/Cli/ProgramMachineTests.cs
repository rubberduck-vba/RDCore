using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Hosting;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Cli;

/// <summary>
/// A machine of a program (<see cref="IProgramMachine"/>): a procedure invoked by name, with its arguments by their parameters' names and its value as JSON;
/// what ends an invocation otherwise; one invocation at a time; and a reset that a token cuts short, in a <c>Class_Terminate</c> too.
/// </summary>
[TestClass]
public sealed class ProgramMachineTests
{
    // a cap on a hang, not a budget: each wait is on the invocation itself.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private static readonly (string Name, string Source)[] Classes =
    [
        ("Looping", ModuleWorkspace.ClassModule("Looping",
            "Private Sub Class_Terminate()",
            "Do",
            "Loop",
            "End Sub")),
    ];

    private static readonly string Program = string.Join("\r\n",
        "Public Count As Long",
        "Public Held As Object",
        "Public Enum Shade",
        "Light = 1",
        "Dark = 2",
        "End Enum",
        "Public Sub Main()",
        "End Sub",
        "Public Function Add(ByVal A As Long, ByVal B As Long) As Long",
        "Add = A + B",
        "End Function",
        "Public Sub Bump()",
        "Count = Count + 1",
        "End Sub",
        "Public Function Current() As Long",
        "Current = Count",
        "End Function",
        "Public Function Defaulted(Optional ByVal N As Long = 7, Optional V As Variant) As String",
        "Defaulted = N & \" \" & IsMissing(V)",
        "End Function",
        "Public Function Fails() As Long",
        "Fails = 1 / 0",
        "End Function",
        "Public Sub Ends()",
        "End",
        "End Sub",
        "Public Sub Forever()",
        "Do",
        "Loop",
        "End Sub",
        "Public Sub WriteForever(ByVal Path As String)",
        "Open Path For Output As #1",
        "Do",
        "Loop",
        "End Sub",
        "Public Function Record() As Object",
        "Set Record = CreateObject(\"Scripting.Dictionary\")",
        "Record.Add \"name\", \"x\"",
        "Record.Add \"items\", Array(1, 2)",
        "End Function",
        "Public Function Files() As Object",
        "Set Files = CreateObject(\"Scripting.FileSystemObject\")",
        "End Function",
        "Public Function ShadeOf(ByVal S As Shade) As Long",
        "ShadeOf = S * 10",
        "End Function",
        "Private Function Hidden() As Long",
        "Hidden = 1",
        "End Function",
        "Public Sub Hold()",
        "Set Held = New Looping",
        "End Sub",
        "");

    private static Task WithMachineAsync(Func<IProgramMachine, Task> act)
        => ModuleWorkspace.InspectAsync(Classes, Program, async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);
            await using var machine = new ProgramMachine(provider, NSubstitute.Substitute.For<IErrorDetailsRecorder>(), ownsSession: false);
            await act(machine);
        });

    private static Task<ProgramInvocationResult> Invoke(IProgramMachine machine, string procedure, string arguments = "{}", CancellationToken token = default)
        => machine.InvokeAsync(new ProgramInvocation("Program", procedure, Arguments(arguments)), token).WaitAsync(Patience);

    private static Dictionary<string, JsonNode?> Arguments(string json)
        => JsonNode.Parse(json)!.AsObject().ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.Ordinal);

    private static void AssertReturned(string expected, ProgramInvocationResult result)
    {
        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        Assert.IsTrue(result.HasValue, "a Function returns a value");
        Assert.AreEqual(expected, result.Value?.ToJsonString() ?? "null");
    }

    private static void AssertError(VBRuntimeErrorId expected, ProgramInvocationResult result)
    {
        Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
        Assert.AreEqual((int)expected, result.ErrorNumber, result.ErrorMessage);
    }

    // the module and the arguments are named as RD-VBA compares names, whatever the dictionary's comparer, in any order.
    [TestMethod]
    public Task AFunction_InvokedByName_ReturnsItsValue_ItsArgumentsMatchedToItsParametersByName()
        => WithMachineAsync(async machine =>
        {
            var invocation = new ProgramInvocation("program", "ADD", Arguments("""{"b":2,"A":40}"""));

            AssertReturned("42", await machine.InvokeAsync(invocation, default).WaitAsync(Patience));
        });

    // a Sub returns no value, and what it does is the machine's until a reset.
    [TestMethod]
    public Task ASub_CompletesWithNoValue_AndTheNextInvocationSeesWhatItDid()
        => WithMachineAsync(async machine =>
        {
            var bumped = await Invoke(machine, "Bump");
            await Invoke(machine, "Bump");

            Assert.AreEqual(ExecutionOutcome.Completed, bumped.Outcome, bumped.ErrorMessage);
            Assert.IsFalse(bumped.HasValue, "a Sub returns no value");
            AssertReturned("2", await Invoke(machine, "Current"));
        });

    // an Optional parameter given nothing is what a call that omits it passes: its default, or a missing Variant.
    [TestMethod]
    public Task AnOptionalParameterGivenNothing_IsWhatACallThatOmitsItPasses()
        => WithMachineAsync(async machine =>
        {
            AssertReturned("\"7 True\"", await Invoke(machine, "Defaulted"));
            AssertReturned("\"3 False\"", await Invoke(machine, "Defaulted", """{"n":3,"v":null}"""));
        });

    // the errors a call raises at its arguments, before the procedure runs: it does not run.
    [TestMethod]
    [DataRow("""{"A":1,"B":2,"C":3}""", VBRuntimeErrorId.NamedArgumentNotFound, DisplayName = "an argument no parameter has")]
    [DataRow("""{"A":1}""", VBRuntimeErrorId.ArgumentNotOptional, DisplayName = "a parameter that is not Optional, given nothing")]
    [DataRow("""{"A":"x","B":2}""", VBRuntimeErrorId.TypeMismatch, DisplayName = "a value of no parameter's type")]
    [DataRow("""{"A":null,"B":2}""", VBRuntimeErrorId.InvalidUseOfNull, DisplayName = "null for a Long")]
    [DataRow("""{"A":1,"a":2,"B":3}""", VBRuntimeErrorId.NamedArgumentNotFound, DisplayName = "one parameter named twice")]
    public Task AnArgumentThatDoesNotBind_IsARunTimeError_RaisedBeforeTheProcedureRuns(string arguments, VBRuntimeErrorId expected)
        => WithMachineAsync(async machine =>
        {
            await Invoke(machine, "Bump");
            AssertError(expected, await Invoke(machine, "Add", arguments));

            // an error at the arguments ends nothing: what the program did is still there.
            AssertReturned("1", await Invoke(machine, "Current"));
        });

    // MS-VBAL §5.2.3.4: an argument of an Enum parameter is a Long.
    [TestMethod]
    public Task AnEnumParameter_TakesANumber()
        => WithMachineAsync(async machine => AssertReturned("20", await Invoke(machine, "ShadeOf", """{"S":2}""")));

    // the value a Function returns is JSON, as JsonValues writes it; one that has none is error 13.
    [TestMethod]
    public Task AnObjectReturned_IsItsJsonValue_AndOneThatHasNoneIsATypeMismatch()
        => WithMachineAsync(async machine =>
        {
            AssertReturned("""{"name":"x","items":[1,2]}""", await Invoke(machine, "Record"));
            AssertError(VBRuntimeErrorId.TypeMismatch, await Invoke(machine, "Files"));
        });

    // a procedure the module does not declare as one of its public procedures, or a module the program does not have.
    [TestMethod]
    [DataRow("Program", "Nothing", DisplayName = "no such procedure")]
    [DataRow("Program", "Hidden", DisplayName = "a Private procedure")]
    [DataRow("Program", "Count", DisplayName = "a variable")]
    [DataRow("Elsewhere", "Add", DisplayName = "no such module")]
    [DataRow("Looping", "Class_Terminate", DisplayName = "a class module")]
    public Task AProcedureThatIsNotThere_IsNotFound(string module, string procedure)
        => WithMachineAsync(async machine =>
        {
            var result = await machine.InvokeAsync(new ProgramInvocation(module, procedure, new Dictionary<string, JsonNode?>()), default).WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.NotFound, result.Outcome, result.ErrorMessage);
            Assert.IsFalse(string.IsNullOrEmpty(result.ErrorMessage), "the result says what is not there");
        });

    // an error nothing handled is the program's run-time error, with its code; an End ends the program.
    [TestMethod]
    public Task AnUnhandledError_IsARunTimeError_AndAnEndHalts()
        => WithMachineAsync(async machine =>
        {
            var failed = await Invoke(machine, "Fails");
            var ended = await Invoke(machine, "Ends");

            AssertError(VBRuntimeErrorId.DivisionByZero, failed);
            Assert.IsFalse(string.IsNullOrEmpty(failed.ErrorCode), "the error has its diagnostic code");
            Assert.AreEqual(ExecutionOutcome.Halted, ended.Outcome, ended.ErrorMessage);
        });

    // the token stops the invocation between two instructions: a loop with no end is interrupted.
    [TestMethod]
    public Task TheToken_InterruptsTheInvocation()
        => WithMachineAsync(async machine =>
        {
            var interrupted = await Invoke(machine, "Forever", token: new CancellationToken(canceled: true));

            Assert.AreEqual(ExecutionOutcome.Interrupted, interrupted.Outcome, interrupted.ErrorMessage);
        });

    // one invocation at a time: another, or a reset, while one runs is refused, and the one that runs is not disturbed.
    [TestMethod]
    public Task AnInvocationOrAReset_WhileOneRuns_IsRefused()
        => WithMachineAsync(async machine =>
        {
            using var stop = new CancellationTokenSource();
            var running = machine.InvokeAsync(new ProgramInvocation("Program", "Forever", new Dictionary<string, JsonNode?>()), stop.Token);

            var refused = await Invoke(machine, "Current");
            var reset = await machine.ResetAsync(default).WaitAsync(Patience);
            await stop.CancelAsync();
            var interrupted = await running.WaitAsync(Patience);

            Assert.AreEqual(ExecutionOutcome.Refused, refused.Outcome, refused.ErrorMessage);
            Assert.IsFalse(reset, "a reset while an invocation runs");
            Assert.AreEqual(ExecutionOutcome.Interrupted, interrupted.Outcome, interrupted.ErrorMessage);
        });

    // a reset returns the machine to the state it was made in.
    [TestMethod]
    public Task AReset_ReturnsTheMachineToItsFirstState()
        => WithMachineAsync(async machine =>
        {
            await Invoke(machine, "Bump");
            await Invoke(machine, "Bump");

            Assert.IsTrue(await machine.ResetAsync(default).WaitAsync(Patience));
            AssertReturned("0", await Invoke(machine, "Current"));
        });

    // the token reaches the Terminate a reset runs: one that never ends is cut short, and the reset says so.
    [TestMethod]
    public Task TheToken_CutsShortATerminateTheResetRuns()
        => WithMachineAsync(async machine =>
        {
            var held = await Invoke(machine, "Hold");

            Assert.AreEqual(ExecutionOutcome.Completed, held.Outcome, held.ErrorMessage);
            Assert.IsFalse(await machine.ResetAsync(new CancellationToken(canceled: true)).WaitAsync(Patience));
        });

    [TestMethod]
    public Task ADisposedMachine_RefusesToBeUsed()
        => WithMachineAsync(async machine =>
        {
            await machine.DisposeAsync();

            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => Invoke(machine, "Current"));
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => machine.ResetAsync(default));
        });

    // a machine disposed while an invocation runs lets go of its session only once the invocation ends: the file the procedure opened is still open, and the
    // invocation ends as its token says. A dispose after that end, the machine's owner's, changes nothing.
    [TestMethod]
    public Task AMachineDisposedWhileItRuns_LetsGoOfItsSessionOnceTheInvocationEnds()
        => ModuleWorkspace.InspectAsync(Classes, Program, async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);
            var machine = new ProgramMachine(provider, NSubstitute.Substitute.For<IErrorDetailsRecorder>(), ownsSession: true);
            var path = Path.Combine(Path.GetTempPath(), $"rdcore-machine-{Guid.NewGuid():N}.txt");
            using var stop = new CancellationTokenSource();
            var running = machine.InvokeAsync(
                new ProgramInvocation("Program", "WriteForever", new Dictionary<string, JsonNode?> { ["Path"] = JsonValue.Create(path) }), stop.Token);
            try
            {
                // what is waited on is the file being open, not a time.
                while (!provider.Session.Files.Open.Any() && !running.IsCompleted)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                }

                Assert.IsFalse(running.IsCompleted, "the invocation runs until its token stops it");
                await machine.DisposeAsync();

                Assert.IsTrue(provider.Session.Files.Open.Any(), "the session of a machine disposed while it runs is let go of only once the invocation ends");
            }
            finally
            {
                await stop.CancelAsync();
            }

            var interrupted = await running.WaitAsync(Patience);
            Assert.AreEqual(ExecutionOutcome.Interrupted, interrupted.Outcome, interrupted.ErrorMessage);
            Assert.IsFalse(provider.Session.Files.Open.Any(), "the session is let go of once the invocation ends, and its files close with it");
            await machine.DisposeAsync();
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => Invoke(machine, "Current"));
            File.Delete(path);
        });
}
