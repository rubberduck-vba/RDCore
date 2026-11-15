using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// An argument list written after a function that returns an array is the call's (observed in Excel). MS-VBA gives it to the function,
/// with too many arguments for one that takes fewer - though <strong>MS-VBAL §5.6.13</strong> lets the arguments a function that takes none cannot accept index
/// what it returns. The result is indexed by a second argument list: <c>F()(1)</c>.
/// </summary>
[TestClass]
public sealed class ArrayResultArgumentListTests
{
    // the function that takes none (F) and the one that takes one (G), and the case's statements in a procedure that prints what they did.
    private static string Program(params string[] body)
        => string.Join("\r\n",
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Private Function F() As Long()",
            "    Dim a(2) As Long",
            "    a(1) = 7",
            "    F = a",
            "End Function",
            "Private Function G(n As Long) As Long()",
            "    Dim a(2) As Long",
            "    a(0) = n * 10",
            "    G = a",
            "End Function",
            "Public Sub Main()",
            "    Dim x As Long",
            "    " + string.Join("\r\n    ", body),
            "    Debug.Print x",
            "End Sub") + "\r\n";

    private const string WrongNumberOfArguments = "Wrong number of arguments or invalid property assignment";

    // MS-VBA, Compile error: Wrong number of arguments or invalid property assignment.
    [TestMethod]
    public async Task AnArgument_ToAFunctionThatTakesNone_AndReturnsAnArray_IsWrongNumberOfArguments()
        => StringAssert.Contains(string.Join(" / ", await LoadErrorsAsync([], [], Program("x = F(1)"))), WrongNumberOfArguments);

    // MS-VBA, Compile error: Wrong number of arguments or invalid property assignment, not a type mismatch of the array assigned.
    [TestMethod]
    public async Task OneArgumentTooMany_ToAFunctionThatReturnsAnArray_IsWrongNumberOfArguments()
    {
        var errors = string.Join(" / ", await LoadErrorsAsync([], [], Program("x = G(1, 0)")));

        StringAssert.Contains(errors, WrongNumberOfArguments);
        Assert.DoesNotContain("Type mismatch", errors);
    }

    // A02, A03, A05: an empty argument list is the call itself, and the result is indexed by the next; the function's result is an array UBound takes.
    [TestMethod]
    [DataRow("x = F()(1)", "7", DisplayName = "A02 the result of a function that takes none, indexed")]
    [DataRow("x = G(1)(0)", "10", DisplayName = "A03 the result of a function that takes one, indexed")]
    [DataRow("x = UBound(F)", "2", DisplayName = "A05 the bound of the result of a function that takes none")]
    public async Task TheResultOfAFunctionThatReturnsAnArray_IsIndexedByTheNextArgumentList(string statement, string printed)
        => CollectionAssert.AreEqual(new[] { printed }, await RunAsync([], [], Program(statement)));
}
