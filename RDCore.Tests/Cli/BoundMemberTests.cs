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
        ("Named", ClassModule("Named",
            "Public Name As String",
            "Public Items As Variant")),
        ("INamed", ClassModule("INamed",
            "Public Name As String")),
        ("Labelled", ClassModule("Labelled",
            "Implements INamed",
            "Private Property Get INamed_Name() As String", "    INamed_Name = \"label\"", "End Property",
            "Private Property Let INamed_Name(ByVal Value As String)", "End Property")),
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

    // MS-VBAL 5.6.13: an object a variable holds, indexed, is a call of its default member - c(1) is c.Item(1) - and an array a variable holds, its element.
    [TestMethod]
    public async Task AVariableIndexed_IsTheDefaultMemberOfItsObject_OrAnElementOfItsArray()
        => CollectionAssert.AreEqual(new[] { "b", "2", "30", "9" }, await RunMainAsync(
            "Dim c As New Collection, a(2) As Long, v As Variant",
            "c.Add \"a\"", "c.Add \"b\"",
            "a(2) = 30", "v = Array(1, 2)",
            "Debug.Print c(2)",
            "Debug.Print v(1)",
            "Debug.Print a(2)",
            "On Error Resume Next", "Debug.Print a(3)", "Debug.Print Err.Number"));

    // an object declared Object or held by a Variant is late-bound: the member is the one its class has by the name, found from the value.
    [TestMethod]
    public async Task AMemberOfALateBoundObject_IsTheOneItsClassHasByThatName()
        => CollectionAssert.AreEqual(new[] { "2", "14", "1", "2" }, await RunMainAsync(
            "Dim o As Object, v As Variant, k As Object",
            "Set o = New Counter", "o.Increment", "o.Increment",
            "Debug.Print o.Value",
            "Debug.Print o.Twice(7)",
            "Set v = New Counter", "v.Increment",
            "Debug.Print v.Value",
            "Set k = New Collection", "k.Add 1", "k.Add 2",
            "Debug.Print k.Count"));

    // MS-VBA: a member of an Object that is Nothing is error 91, and of a Variant that holds no object error 424 (recorded in Excel).
    [TestMethod]
    public async Task AMemberOfALateBoundOwnerThatHoldsNoObject_IsError91Or424()
        => CollectionAssert.AreEqual(new[] { "91", "424" }, await RunMainAsync(
            "Dim o As Object, v As Variant",
            "On Error Resume Next",
            "o.Increment", "Debug.Print Err.Number", "Err.Clear",
            "v = 5", "v.Increment", "Debug.Print Err.Number"));

    // a public variable of a class is the storage of the object: read, and indexed by what follows it in parentheses.
    [TestMethod]
    public async Task APublicVariableOfAClass_IsRead_AndIndexed()
        => CollectionAssert.AreEqual(new[] { "Ducky", "20", "Ducky!" }, await RunMainAsync(
            "Dim n As New Named",
            "n.Name = \"Ducky\"", "n.Items = Array(10, 20, 30)",
            "Debug.Print n.Name",
            "Debug.Print n.Items(1)",
            "Debug.Print n.Name & \"!\""));

    // MS-VBAL 5.3.1.9: a public variable of an interface is implemented by properties: reading it through the interface is the Property Get.
    [TestMethod]
    public async Task APublicVariableOfAnInterface_IsThePropertyGetThatImplementsIt()
        => CollectionAssert.AreEqual(new[] { "label" }, await RunMainAsync(
            "Dim n As INamed",
            "Set n = New Labelled",
            "Debug.Print n.Name"));

    // MS-VBAL 5.6.7: TypeOf … Is is True of an object of the class, or of a class that implements it; of Nothing, it is error 91 (MS-VBA too, Excel-verified,
    // oracle\s29 T09).
    [TestMethod]
    public async Task TypeOfIs_TestsTheClassOfTheObject_AndTheInterfacesItImplements()
        => CollectionAssert.AreEqual(new[] { "True", "False", "True", "True", "False", "91" }, await RunMainAsync(
            "Dim c As New Counter, i As ICounter, o As Object",
            "Set i = New ByTen",
            "Debug.Print TypeOf c Is Counter",
            "Debug.Print TypeOf c Is ICounter",
            "Debug.Print TypeOf i Is ICounter",
            "Debug.Print TypeOf i Is ByTen",
            "Debug.Print TypeOf i Is ByOne",
            "On Error Resume Next",
            "Debug.Print TypeOf o Is Counter",
            "Debug.Print Err.Number"));

    // an object variable declared As New is never Nothing when it is referred to: the reference creates it.
    [TestMethod]
    public async Task AMemberOfAnObjectDeclaredAsNew_CreatesTheObject()
        => CollectionAssert.AreEqual(new[] { "1" }, await RunMainAsync(
            "Dim c As New Counter",
            "c.Increment",
            "Debug.Print c.Value"));
}
