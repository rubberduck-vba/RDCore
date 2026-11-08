using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL 5.6.13.1</strong> An empty argument list of an array, which was not given one already, is the array itself: <c>f()</c> is <c>f</c>, the same
/// array, of the same declared type.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13.1 Index Expressions")]
public sealed class EmptyArgumentListTests
{
    private static string Program(params string[] body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Public Sub Main()",
        "    Dim f(2) As Long, a() As Long",
        "    " + string.Join("\r\n    ", body),
        "End Sub",
        "Private Sub Fill(ByRef values() As Long)",
        "    values(0) = 9",
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow(new[] { "Debug.Print UBound(f())" }, "2", DisplayName = "the bounds of a fixed-size array")]
    [DataRow(new[] { "ReDim a(3)", "Debug.Print UBound(a())" }, "3", DisplayName = "the bounds of a dynamic array")]
    [DataRow(new[] { "f(1) = 7", "Dim v As Variant", "v = f()", "Debug.Print v(1)" }, "7", DisplayName = "assigned: a copy of the array")]
    [DataRow(new[] { "Fill f()", "Debug.Print f(0)" }, "9", DisplayName = "passed by reference: the array itself")]
    public async Task AnArrayWithAnEmptyArgumentList_IsTheArray(string[] body, string printed)
    {
        var output = await RunAsync([], Program(body));

        CollectionAssert.AreEqual(new[] { printed }, output);
    }

    // an array is no value Debug.Print writes, written `f` or `f()`: MS-VBA refuses it when it compiles (oracle\s53 E02, E03, F02, F03).
    [TestMethod]
    [DataRow("f", DisplayName = "f")]
    [DataRow("f()", DisplayName = "f()")]
    [DataRow("a()", DisplayName = "a()")]
    [DataRow("a", DisplayName = "a")]
    public async Task AnArrayPrinted_WithOrWithoutTheEmptyArgumentList_IsATypeMismatch_WhenItCompiles(string printed)
    {
        var errors = await LoadErrorsAsync([], Program($"Debug.Print {printed}"));

        StringAssert.StartsWith(Assert.ContainsSingle(errors), "Type mismatch");
    }

    // `Erase f()` erases `f`: a dynamic array loses its dimensions (9 for its bounds after), a fixed-size one its values (oracle\s53 E06, F06).
    [TestMethod]
    [DataRow(new[] { "ReDim a(2)", "Erase a()", "On Error Resume Next", "Debug.Print UBound(a)", "Debug.Print Err.Number" }, new[] { "9" }, DisplayName = "dynamic")]
    [DataRow(new[] { "f(1) = 7", "Erase f()", "Debug.Print f(1)", "Debug.Print UBound(f)" }, new[] { "0", "2" }, DisplayName = "fixed")]
    public async Task AnArrayErased_WithTheEmptyArgumentList_IsTheArrayErased(string[] body, string[] printed)
    {
        var output = await RunAsync([], Program(body));

        CollectionAssert.AreEqual(printed, output);
    }

    // `f()()` compiles in MS-VBA, as the array, and raises 9 whenever it runs (oracle\s53 E07, E08, F07, F08); RDC11025 warns of it.
    [TestMethod]
    [DataRow("Debug.Print UBound(f()())", DisplayName = "its bounds, a fixed-size array")]
    [DataRow("Dim v As Variant: v = f()()", DisplayName = "assigned, a fixed-size array")]
    [DataRow("ReDim a(2): Debug.Print UBound(a()())", DisplayName = "its bounds, a dynamic array")]
    public async Task AnArrayGivenASecondEmptyArgumentList_Compiles_AndIsASubscriptOutOfRange(string statement)
    {
        var output = await RunAsync([], Program("On Error Resume Next", statement, "Debug.Print \"err \" & Err.Number"));

        CollectionAssert.AreEqual(new[] { "err 9" }, output);
    }
}
