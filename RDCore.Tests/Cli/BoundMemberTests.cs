using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A member of an object declared as a class, bound to the member of the class (IR lowering S0.10), is the member MS-VBAL 5.6.12 and 5.6.13 read and call:
/// the class's own, or - of an object declared as an interface its class implements - the one implementing it (MS-VBAL 5.3.1.9), whichever class the object
/// is an instance of each time the expression is evaluated.
/// </summary>
[TestClass]
public sealed class BoundMemberTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("Counter", ClassModule("Counter",
            "Private mValue As Long",
            "Public Sub Increment(Optional ByVal By As Long = 1)", "    mValue = mValue + By", "End Sub",
            "Public Property Get Value() As Long", "    Value = mValue", "End Property",
            "Public Function Twice(ByVal N As Long) As Long", "    Twice = 2 * N", "End Function",
            "Public Sub Double(ByRef N As Long)", "    N = 2 * N", "End Sub",
            "Public Function Doubled() As Long", "    Doubled = 2 * Me.Value", "End Function",
            "Public Sub Fail()", "    Err.Raise 5", "End Sub",
            "Public Function Items() As Variant", "    Items = Array(10, 20, 30)", "End Function")),
        ("ICounter", ClassModule("ICounter",
            "Public Sub Increment()", "End Sub",
            "Public Property Get Value() As Long", "End Property")),
        ("ByOne", ClassModule("ByOne",
            "Implements ICounter",
            "Private mValue As Long",
            "Private Sub ICounter_Increment()", "    mValue = mValue + 1", "End Sub",
            "Private Property Get ICounter_Value() As Long", "    ICounter_Value = mValue", "End Property")),
        ("ByTen", ClassModule("ByTen",
            "Implements ICounter",
            "Private mValue As Long",
            "Private Sub ICounter_Increment()", "    mValue = mValue + 10", "End Sub",
            "Private Property Get ICounter_Value() As Long", "    ICounter_Value = mValue", "End Property")),
    ];

    private static Task<string[]> RunMainAsync(params string[] body)
        => RunAsync(Classes, $"Attribute VB_Name = \"Program\"\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task AMemberOfAClass_IsReadAndCalled()
        => CollectionAssert.AreEqual(new[] { "3", "14", "15", "6" }, await RunMainAsync(
            "Dim c As Counter", "Set c = New Counter", "Dim I As Long",
            "For I = 1 To 3", "    c.Increment", "Next",
            "Debug.Print c.Value",
            "Debug.Print c.Twice(7)",
            "Debug.Print c.Twice(7) + 1",
            "Debug.Print c.Doubled"));

    [TestMethod]
    public async Task AMemberCalled_WithArguments_PassesThemAsItsParametersSay()
        => CollectionAssert.AreEqual(new[] { "5", "8" }, await RunMainAsync(
            "Dim c As New Counter",
            "c.Increment 2", "c.Increment By:=3",
            "Debug.Print c.Value",
            "Dim N As Long", "N = 4", "c.Double N",
            "Debug.Print N"));

    // MS-VBAL 5.3.1.9: through the interface, the member is the one the object's class implements it with - a different one for each class, at the same
    // expression.
    [TestMethod]
    public async Task AMemberOfAnInterface_IsTheImplementationOfTheObjectsClass_WhicheverItIs()
        => CollectionAssert.AreEqual(new[] { "1", "10", "2", "20" }, await RunMainAsync(
            "Dim counters(1) As ICounter",
            "Set counters(0) = New ByOne", "Set counters(1) = New ByTen",
            "Dim Pass As Long, I As Long, c As ICounter",
            "For Pass = 1 To 2",
            "    For I = 0 To 1",
            "        Set c = counters(I)",
            "        c.Increment",
            "        Debug.Print c.Value",
            "    Next",
            "Next"));

    [TestMethod]
    public async Task AMemberOfNothing_IsError91()
        => CollectionAssert.AreEqual(new[] { "91", "91" }, await RunMainAsync(
            "Dim c As Counter",
            "On Error Resume Next",
            "c.Increment", "Debug.Print Err.Number", "Err.Clear",
            "Debug.Print c.Value", "Debug.Print Err.Number"));

    [TestMethod]
    public async Task AnErrorRaisedByAMember_IsTheCallersToHandle()
        => CollectionAssert.AreEqual(new[] { "5" }, await RunMainAsync(
            "Dim c As New Counter",
            "On Error Resume Next",
            "c.Fail",
            "Debug.Print Err.Number"));

    // a function that declares no parameter is not given what follows it in parentheses: they index what it returns.
    [TestMethod]
    public async Task AFunctionThatTakesNoArgument_GivenSome_IsIndexed()
        => CollectionAssert.AreEqual(new[] { "20" }, await RunMainAsync(
            "Dim c As New Counter",
            "Debug.Print c.Items(1)"));

    // MS-VBAL 5.6.15: a member written with no object before the dot is a member of the target of the enclosing With block - the innermost one.
    [TestMethod]
    public async Task AMemberOfTheTargetOfAWithBlock_IsReadAndCalled()
        => CollectionAssert.AreEqual(new[] { "3", "14", "1", "3" }, await RunMainAsync(
            "Dim c As New Counter, d As New Counter",
            "With c",
            "    .Increment", "    .Increment 2",
            "    Debug.Print .Value",
            "    Debug.Print .Twice(7)",
            "    With d",
            "        .Increment",
            "        Debug.Print .Value",
            "    End With",
            "    Debug.Print .Value",
            "End With"));

    [TestMethod]
    public async Task AMemberOfTheTargetOfAWithBlock_DeclaredAsAnInterface_IsItsImplementation()
        => CollectionAssert.AreEqual(new[] { "10" }, await RunMainAsync(
            "Dim c As ICounter",
            "Set c = New ByTen",
            "With c",
            "    .Increment",
            "    Debug.Print .Value",
            "End With"));

    [TestMethod]
    public async Task AMemberOfTheTargetOfAWithBlock_ThatIsNothing_IsError91()
        => CollectionAssert.AreEqual(new[] { "91" }, await RunMainAsync(
            "Dim c As Counter",
            "On Error Resume Next",
            "With c",
            "    .Increment",
            "End With",
            "Debug.Print Err.Number"));

    // MS-VBAL 5.6.9.9: TypeOf … Is is True of an object of the class, or of a class that implements it; False of Nothing.
    [TestMethod]
    public async Task TypeOfIs_TestsTheClassOfTheObject_AndTheInterfacesItImplements()
        => CollectionAssert.AreEqual(new[] { "True", "False", "True", "True", "False", "False" }, await RunMainAsync(
            "Dim c As New Counter, i As ICounter, o As Object",
            "Set i = New ByTen",
            "Debug.Print TypeOf c Is Counter",
            "Debug.Print TypeOf c Is ICounter",
            "Debug.Print TypeOf i Is ICounter",
            "Debug.Print TypeOf i Is ByTen",
            "Debug.Print TypeOf i Is ByOne",
            "Debug.Print TypeOf o Is Counter"));

    // an object variable declared As New is never Nothing when it is referred to: the reference creates it.
    [TestMethod]
    public async Task AMemberOfAnObjectDeclaredAsNew_CreatesTheObject()
        => CollectionAssert.AreEqual(new[] { "1" }, await RunMainAsync(
            "Dim c As New Counter",
            "c.Increment",
            "Debug.Print c.Value"));
}
