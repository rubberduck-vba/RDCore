using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Hosting;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Cli;

/// <summary>
/// An instance of a class of the workspace, returned by a procedure invoked by name (<see cref="IProgramMachine"/>): a JSON object of its public members,
/// each read as <c>CallByName</c> reads it, in the order the class declares them.
/// </summary>
[TestClass]
public sealed class JsonInstancesTests
{
    // a cap on a hang, not a budget: each wait is on the invocation itself.
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private static readonly (string Name, string Source)[] Classes =
    [
        ("Order", ModuleWorkspace.ClassModule("Order",
            "Public Id As Long",
            "Private mCustomer As Customer",
            "Dim mHidden As Long",
            "Public Tags As Variant",
            "Public Note As String",
            "Public Property Get Customer() As Customer",
            "Set Customer = mCustomer",
            "End Property",
            "Public Property Set Customer(ByVal value As Customer)",
            "Set mCustomer = value",
            "End Property",
            "Public Property Get Total() As Currency",
            "Total = 12.5",
            "End Property",
            "Public Property Let WriteOnly(ByVal value As Long)",
            "End Property",
            "Public Property Get Line(ByVal index As Long) As Long",
            "Line = index",
            "End Property",
            "Friend Property Get Internal() As Long",
            "Internal = 1",
            "End Property",
            "Private Property Get Concealed() As Long",
            "Concealed = 1",
            "End Property",
            "Public Function Method() As Long",
            "Method = 1",
            "End Function",
            "Public Sub Act()",
            "End Sub")),
        ("Customer", ModuleWorkspace.ClassModule("Customer",
            "Public Name As String",
            "Public Order As Order")),
        ("Faulty", ModuleWorkspace.ClassModule("Faulty",
            "Public Property Get Broken() As Long",
            "Err.Raise 1234, , \"broken\"",
            "End Property")),
        ("Ending", ModuleWorkspace.ClassModule("Ending",
            "Public Property Get Gone() As Long",
            "End",
            "End Property")),
    ];

    private static readonly string Program = string.Join("\r\n",
        "Public Sub Main()",
        "End Sub",
        "Public Function MakeOrder() As Order",
        "Dim o As New Order",
        "o.Id = 7",
        "Set o.Customer = New Customer",
        "o.Customer.Name = \"duck\"",
        "o.Tags = Array(\"a\", \"b\")",
        "Set MakeOrder = o",
        "End Function",
        "Public Function Twice() As Variant",
        "Dim c As New Customer",
        "c.Name = \"x\"",
        "Twice = Array(c, c)",
        "End Function",
        "Public Function Chain() As Customer",
        "Dim c As New Customer",
        "Set c.Order = New Order",
        "Set c.Order.Customer = New Customer",
        "Set Chain = c",
        "End Function",
        "Public Function Cycle() As Customer",
        "Dim c As New Customer",
        "Set c.Order = New Order",
        "Set c.Order.Customer = c",
        "Set Cycle = c",
        "End Function",
        "Public Function MakeFaulty() As Faulty",
        "Set MakeFaulty = New Faulty",
        "End Function",
        "Public Function MakeEnding() As Ending",
        "Set MakeEnding = New Ending",
        "End Function",
        "");

    private static Task WithMachineAsync(Func<IProgramMachine, Task> act)
        => ModuleWorkspace.InspectAsync(Classes, Program, async (provider, first, _) =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, first.Outcome, first.ErrorMessage);
            await using var machine = new ProgramMachine(provider, NSubstitute.Substitute.For<IErrorDetailsRecorder>(), ownsSession: false);
            await act(machine);
        });

    private static Task<ProgramInvocationResult> Invoke(IProgramMachine machine, string procedure)
        => machine.InvokeAsync(new ProgramInvocation("Program", procedure, new Dictionary<string, JsonNode?>()), default).WaitAsync(Patience);

    private static void AssertReturned(string expected, ProgramInvocationResult result)
    {
        Assert.AreEqual(ExecutionOutcome.Completed, result.Outcome, result.ErrorMessage);
        Assert.AreEqual(expected, result.Value?.ToJsonString() ?? "null");
    }

    // a public variable and a Property Get that takes no argument, in the order declared and spelled as declared; a nested instance is an object too.
    // A Dim or Private variable, a Let with no Get, a Get that takes an argument, a Friend or Private Get, a Function and a Sub are not members of it.
    [TestMethod]
    public Task AnInstance_IsAnObjectOfItsPublicVariablesAndArgumentlessPropertyGets_InTheOrderDeclared()
        => WithMachineAsync(async machine
            => AssertReturned("""{"Id":7,"Tags":["a","b"],"Note":"","Customer":{"Name":"duck","Order":null},"Total":12.5}""",
                await Invoke(machine, "MakeOrder")));

    // an instance held twice is written twice, as a Dictionary held twice is.
    [TestMethod]
    public Task AnInstanceHeldTwice_IsWrittenTwice()
        => WithMachineAsync(async machine
            => AssertReturned("""[{"Name":"x","Order":null},{"Name":"x","Order":null}]""", await Invoke(machine, "Twice")));

    // an instance that holds itself, through another, has no JSON value: 13, after the procedure has run. The same chain with no way back converts, so
    // the 13 is the loop's.
    [TestMethod]
    public Task AnInstanceThatHoldsItself_IsATypeMismatch()
        => WithMachineAsync(async machine =>
        {
            var chain = await Invoke(machine, "Chain");
            Assert.AreEqual(ExecutionOutcome.Completed, chain.Outcome, "premise: the chain with no way back converts; " + chain.ErrorMessage);

            var result = await Invoke(machine, "Cycle");

            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
            Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, result.ErrorNumber, result.ErrorMessage);
        });

    // a Property Get that raises an error ends the invocation in that error, as the program raised it: its own number, and a program's error code.
    [TestMethod]
    public Task APropertyGetThatRaises_EndsTheInvocationInItsError()
        => WithMachineAsync(async machine =>
        {
            var result = await Invoke(machine, "MakeFaulty");

            Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
            Assert.AreEqual(1234, result.ErrorNumber, result.ErrorMessage);
            StringAssert.StartsWith(result.ErrorCode, "VBA", "an error the program raised has a program's code");
        });

    // an End in a Property Get ends the invocation as an End in the procedure does.
    [TestMethod]
    public Task AnEndInAPropertyGet_EndsTheInvocationAsHalted()
        => WithMachineAsync(async machine =>
            Assert.AreEqual(ExecutionOutcome.Halted, (await Invoke(machine, "MakeEnding")).Outcome));
}
