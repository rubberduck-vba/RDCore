using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.6.10</strong>: a member of a class module named bare in the module's own code is a member of the current instance - the call is made
/// on <c>Me</c>, whatever form it takes: a <c>Sub</c> named alone or with <c>Call</c>, a <c>Function</c> given arguments, a <c>Property Get</c> read by its
/// name, a <c>Property Let</c> assigned by it.
/// </summary>
[TestClass]
public sealed class ClassSiblingCallTests
{
    private static readonly (string Name, string Source) Box = ("Box", ClassModule("Box",
        "Option Explicit",
        "Private mSize As Long",
        "Private mLog As String",
        "Private Sub Note(ByVal s As String)", "    mLog = mLog & s", "End Sub",
        "Private Function Twice(ByVal x As Long) As Long", "    Twice = 2 * x", "End Function",
        "Private Property Get IsEmptyBox() As Boolean", "    IsEmptyBox = mSize = 0", "End Property",
        "Public Property Get Size() As Long", "    Size = mSize", "End Property",
        "Public Property Let Size(ByVal value As Long)", "    mSize = value", "    Note \"let\"", "End Property",
        "Public Property Get Log() As String", "    Log = mLog", "End Property",
        "Public Sub Grow(ByVal by As Long)", "    Note \"grow\"", "    Call Note(\",\")", "    Size = Size + Twice(by)", "End Sub",
        "Public Function Describe() As String", "    If IsEmptyBox Then Describe = \"empty\" Else Describe = \"size \" & Size", "End Function",
        "Public Sub Bump(ByRef n As Long)", "    Increment n", "End Sub",
        "Private Sub Increment(ByRef n As Long)", "    n = n + 1", "End Sub",
        "Public Function IsEven(ByVal n As Long) As Boolean", "    If n = 0 Then IsEven = True Else IsEven = IsOdd(n - 1)", "End Function",
        "Public Function IsOdd(ByVal n As Long) As Boolean", "    If n = 0 Then IsOdd = False Else IsOdd = IsEven(n - 1)", "End Function",
        "Private Sub Class_Initialize()", "    Note \"init;\"", "End Sub"));

    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync([Box],
            "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n" + string.Join("\r\n", body) + "\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task EachFormOfACallOfASibling_IsMadeOnTheCurrentInstance()
        => CollectionAssert.AreEqual(new[] { "empty", "size 6", "init;grow,let" }, await RunAsync(
            "Dim b As New Box",
            "Debug.Print b.Describe",
            "b.Grow 3",
            "Debug.Print b.Describe",
            "Debug.Print b.Log"));

    // the current instance is the object the call was made on: two objects, two states.
    [TestMethod]
    public async Task ASiblingCalledOnOneObject_LeavesAnotherAsItWas()
        => CollectionAssert.AreEqual(new[] { "size 2", "empty" }, await RunAsync(
            "Dim a As New Box, b As New Box",
            "a.Grow 1",
            "Debug.Print a.Describe",
            "Debug.Print b.Describe"));

    [TestMethod]
    public async Task AVariablePassedOnToASiblingByReference_IsTheCallersVariable()
        => CollectionAssert.AreEqual(new[] { "2" }, await RunAsync(
            "Dim b As New Box, n As Long",
            "n = 1",
            "b.Bump n",
            "Debug.Print n"));

    [TestMethod]
    public async Task SiblingsCanCallEachOther()
        => CollectionAssert.AreEqual(new[] { "True", "False" }, await RunAsync(
            "Dim b As New Box",
            "Debug.Print b.IsEven(10)",
            "Debug.Print b.IsEven(7)"));
}
