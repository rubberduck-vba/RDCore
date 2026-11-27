using RDCore.Runtime.LanguageCore.Errors;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Hosting;
using System.Text.Json.Nodes;

namespace RDCore.Tests.Cli;

/// <summary>
/// An instance of a class of the workspace, returned by a procedure invoked by name (<see cref="IProgramMachine"/>): a JSON object of its public members,
/// each read as <c>CallByName</c> reads it, in the order the class declares them. A JSON object given for a parameter of a class of the workspace: a
/// <c>New</c> object of it, each key giving its value to the member it names as <c>CallByName</c> gives one.
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
        ("Account", ModuleWorkspace.ClassModule("Account",
            "Public Id As Long",
            "Public Origin As String",
            "Public Owner As Customer",
            "Public Data As Variant",
            "Private mNick As String",
            "Private mSecret As Long",
            "Private Sub Class_Initialize()",
            "Origin = \"initialized\"",
            "End Sub",
            "Public Property Get Nick() As String",
            "Nick = mNick",
            "End Property",
            "Public Property Let Nick(ByVal value As String)",
            "mNick = \"~\" & value",
            "End Property",
            "Public Property Get Balance() As Currency",
            "Balance = 10",
            "End Property",
            "Friend Property Let Secret(ByVal value As Long)",
            "mSecret = value",
            "End Property",
            "Public Property Get SecretSeen() As Long",
            "SecretSeen = mSecret",
            "End Property",
            "Public Property Let Fails(ByVal value As Long)",
            "Err.Raise 1234, , \"fails\"",
            "End Property",
            "Public Sub Shut()",
            "Id = -1",
            "End Sub")),
        ("Doomed", ModuleWorkspace.ClassModule("Doomed",
            "Private Sub Class_Initialize()",
            "End",
            "End Sub")),
        ("Basket", ModuleWorkspace.ClassModule("Basket",
            "Private mItems() As Customer",
            "Public Property Get Items() As Customer()",
            "Items = mItems",
            "End Property",
            "Public Property Let Items(value() As Customer)",
            "mItems = value",
            "End Property")),
        ("Tracked", ModuleWorkspace.ClassModule("Tracked",
            "Public Id As Long",
            "Private Sub Class_Terminate()",
            "Terminated = Terminated + 1",
            "End Sub")),
    ];

    private static readonly string Program = string.Join("\r\n",
        "Public Terminated As Long",
        "Public Kept As Object",
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
        "Public Function Describe(ByVal account As Account) As String",
        "Describe = account.Id & \"|\" & account.Origin & \"|\" & account.Owner.Name & \"|\" & IsEmpty(account.Data) & \"|\" & account.Nick & \"|\" _",
        "    & account.Balance & \"|\" & account.SecretSeen",
        "End Function",
        "Public Function IsNone(ByVal account As Account) As Boolean",
        "IsNone = account Is Nothing",
        "End Function",
        "Public Sub Arrive(ByVal it As Doomed)",
        "End Sub",
        "Public Sub Take(ByVal it As Tracked)",
        "End Sub",
        "Public Sub Keep(ByVal it As Tracked)",
        "Set Kept = it",
        "End Sub",
        "Public Function Names(items() As Customer) As String",
        "Names = TypeName(items) & \"|\" & TypeName(items(0)) & \"|\" & UBound(items) & \"|\" & items(0).Name & \"|\" & items(1).Name",
        "End Function",
        "Public Function InTheBasket(ByVal basket As Basket) As String",
        "Dim items() As Customer",
        "items = basket.Items",
        "InTheBasket = TypeName(items) & \"|\" & UBound(items) & \"|\" & items(0).Name",
        "End Function",
        "Public Function Size(items() As Variant) As String",
        "Size = TypeName(items) & \"|\" & UBound(items) & \"|\" & items(1)",
        "End Function",
        "Public Sub TakeAll(items() As Tracked)",
        "End Sub",
        "Public Function TerminatedCount() As Long",
        "TerminatedCount = Terminated",
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

    // the procedure given its arguments as the properties of a JSON object.
    private static Task<ProgramInvocationResult> Invoke(IProgramMachine machine, string procedure, string arguments)
        => machine.InvokeAsync(new ProgramInvocation("Program", procedure, ArgumentsOf(arguments)), default).WaitAsync(Patience);

    private static Dictionary<string, JsonNode?> ArgumentsOf(string arguments)
        => JsonNode.Parse(arguments)!.AsObject().ToDictionary(property => property.Key, property => property.Value?.DeepClone());

    private static void AssertRaised(int expected, ProgramInvocationResult result)
    {
        Assert.AreEqual(ExecutionOutcome.RuntimeError, result.Outcome, result.ErrorMessage);
        Assert.AreEqual(expected, result.ErrorNumber, result.ErrorMessage);
    }

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

    // a JSON object for a parameter of a class is a New object of it: Class_Initialize runs (Origin), then each key gives its value to the member it names,
    // in any case, through a Property Let (Nick) or to a public variable (Id), a JSON object for a variable of a class (Owner). A member no key names keeps
    // its value (Data);
    // a key that names a read-only member (Balance), a Friend one (Secret), a method (Shut) or nothing is ignored.
    [TestMethod]
    public Task AJsonObject_IsANewInstance_EachKeyGivingItsValueToTheMemberItNames()
        => WithMachineAsync(async machine
            => AssertReturned("\"7|initialized|x|True|~duck|10|0\"", await Invoke(machine, "Describe",
                """{"account":{"id":7,"NICK":"duck","Balance":99,"Secret":5,"Shut":1,"Unknown":1,"owner":{"Name":"x"}}}""")));

    // two keys that name one member, in different cases, are 457, the error of a key given twice.
    [TestMethod]
    public Task TwoKeysNamingOneMember_AreAKeyGivenTwice()
        => WithMachineAsync(async machine
            => AssertRaised((int)VBRuntimeErrorId.KeyAlreadyAssociatedWithAnElementOfCollection,
                await Invoke(machine, "Describe", """{"account":{"Id":1,"id":2}}""")));

    // null is Nothing; a JSON value of any other kind is not an object of the class: 13.
    [TestMethod]
    [DataRow("5", DisplayName = "a number")]
    [DataRow("\"text\"", DisplayName = "a string")]
    [DataRow("[1]", DisplayName = "an array")]
    [DataRow("true", DisplayName = "a boolean")]
    public Task AnotherKindThanAnObject_IsATypeMismatch(string other)
        => WithMachineAsync(async machine =>
        {
            AssertReturned("true", await Invoke(machine, "IsNone", """{"account":null}"""));
            AssertRaised((int)VBRuntimeErrorId.TypeMismatch, await Invoke(machine, "Describe", $$"""{"account":{{other}}}"""));
        });

    // a Property Let that raises an error ends the invocation in that error, as the program raised it.
    [TestMethod]
    public Task APropertyLetThatRaises_EndsTheInvocationInItsError()
        => WithMachineAsync(async machine =>
        {
            var result = await Invoke(machine, "Describe", """{"account":{"Fails":1}}""");

            AssertRaised(1234, result);
            StringAssert.StartsWith(result.ErrorCode, "VBA", "an error the program raised has a program's code");
        });

    // an End in Class_Initialize ends the invocation as an End in the procedure does.
    [TestMethod]
    public Task AnEndInClassInitialize_EndsTheInvocationAsHalted()
        => WithMachineAsync(async machine =>
            Assert.AreEqual(ExecutionOutcome.Halted, (await Invoke(machine, "Arrive", """{"it":{}}""")).Outcome));

    // a JSON array for an array of a class is an array of exactly that class, each element a New object of it (TypeName of an array of objects is
    // Object() whatever its class: ArrayOfObjectsTests).
    [TestMethod]
    public Task AJsonArray_ForAnArrayOfAClass_IsAnArrayOfNewInstances()
        => WithMachineAsync(async machine
            => AssertReturned("\"Object()|Customer|1|a|b\"", await Invoke(machine, "Names", """{"items":[{"Name":"a"},{"name":"b"}]}""")));

    // a JSON array for an array parameter of Variants is a Variant() from 0.
    [TestMethod]
    public Task AJsonArray_ForAnArrayOfVariants_IsAVariantArray()
        => WithMachineAsync(async machine
            => AssertReturned("\"Variant()|1|x\"", await Invoke(machine, "Size", """{"items":[1,"x"]}""")));

    // a member that is an array is given one through its Property Let, which takes a dynamic array of exactly its element type.
    [TestMethod]
    public Task AnArrayMember_IsGivenAnArrayOfItsElementType()
        => WithMachineAsync(async machine
            => AssertReturned("\"Object()|0|duck\"", await Invoke(machine, "InTheBasket", """{"basket":{"Items":[{"Name":"duck"}]}}""")));

    // the objects of an array are let go of when the invocation is over, as one given by itself is.
    [TestMethod]
    public Task TheObjectsOfAnArray_AreLetGoOf_WhenTheInvocationIsOver()
        => WithMachineAsync(async machine =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, (await Invoke(machine, "TakeAll", """{"items":[{"Id":1},{"Id":2}]}""")).Outcome);
            AssertReturned("2", await Invoke(machine, "TerminatedCount"));
        });

    // the object is held until the invocation is over, then let go of as a New object a statement made is: one the program kept stays, and one it kept
    // nothing of is destroyed, its Class_Terminate running.
    [TestMethod]
    public Task TheObjectIsLetGoOf_WhenTheInvocationIsOver()
        => WithMachineAsync(async machine =>
        {
            Assert.AreEqual(ExecutionOutcome.Completed, (await Invoke(machine, "Keep", """{"it":{"Id":1}}""")).Outcome);
            AssertReturned("0", await Invoke(machine, "TerminatedCount"));

            Assert.AreEqual(ExecutionOutcome.Completed, (await Invoke(machine, "Take", """{"it":{"Id":2}}""")).Outcome);
            AssertReturned("1", await Invoke(machine, "TerminatedCount"));
        });
}
