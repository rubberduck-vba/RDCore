namespace RDCore.Tests.Cli;

/// <summary>
/// A Function declares a local of its own name, its result variable, and a Property Get returns its value the same way (<strong>MS-VBAL §5.3.1</strong>): in
/// its body the name is bound in the procedure, before an Enum of the name its module declares (<c>Property Get PlayerType() As PlayerType</c>) is a candidate.
/// Each program runs the same with the Enum as without it.
/// </summary>
[TestClass]
public sealed class FunctionResultVariableTests
{
    private const string Namesake = "Public Enum F\r\n    Zero\r\n    One\r\nEnd Enum";

    // what Program.Main prints, with a class Calc of `members`, after an Enum of the function's name or not.
    private static async Task<string> RunAsync(bool withEnum, string main, params string[] members)
    {
        var calc = ModuleWorkspace.ClassModule("Calc", [.. withEnum ? new[] { Namesake } : [], "Option Explicit", .. members]);
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n    Dim c As New Calc\r\n{main}\r\nEnd Sub\r\n";
        return string.Join(" ", await ModuleWorkspace.RunAsync([("Calc", calc)], program));
    }

    private static async Task AssertRunsAsWithoutTheEnumAsync(string expected, string main, params string[] members)
    {
        Assert.AreEqual(expected, await RunAsync(withEnum: false, main, members), "without the Enum");
        Assert.AreEqual(expected, await RunAsync(withEnum: true, main, members), "with an Enum of the function's name");
    }

    [TestMethod]
    public Task AFunctionThatCallsItself_ByItsName()
        => AssertRunsAsWithoutTheEnumAsync("10", "    Debug.Print c.F(4)",
            "Public Function F(ByVal n As Long) As Long", "    If n <= 0 Then F = 0 Else F = n + F(n - 1)", "End Function");

    [TestMethod]
    public Task AFunctionThatCallsItself_WithCall()
        => AssertRunsAsWithoutTheEnumAsync("3", "    Debug.Print c.F(3)",
            "Private Calls As Long",
            "Public Function F(ByVal n As Long) As Long", "    Calls = Calls + 1", "    If n > 1 Then Call F(n - 1)", "    F = Calls", "End Function");

    [TestMethod]
    public Task AFunctionThatReturnsAnArray_AssignsItByItsName()
        => AssertRunsAsWithoutTheEnumAsync("7  8", "    Dim a() As Long\r\n    a = c.F()\r\n    Debug.Print a(0); a(1)",
            "Public Function F() As Long()", "    Dim values(1) As Long", "    values(0) = 7", "    values(1) = 8", "    F = values", "End Function");

    [TestMethod]
    public async Task AFunctionThatReturnsAnArray_CallsItselfWithF0_WithOrWithoutTheEnum()
    {
        // in the body, F(0) is a call of F with one argument, which F takes none of (as without the Enum), and not an element of the result variable.
        string[] members = ["Public Function F() As Long()", "    Dim values(0) As Long", "    values(0) = 7", "    F = values", "    Debug.Print F(0)", "End Function"];
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\n";

        var without = await ModuleWorkspace.LoadErrorsAsync([("Calc", ModuleWorkspace.ClassModule("Calc", ["Option Explicit", .. members]))], program);
        var with = await ModuleWorkspace.LoadErrorsAsync([("Calc", ModuleWorkspace.ClassModule("Calc", [Namesake, "Option Explicit", .. members]))], program);

        StringAssert.Contains(Assert.ContainsSingle(without), RDCore.SDK.Exceptions.VBCompileError_WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        CollectionAssert.AreEqual(without, with);
    }

    [TestMethod]
    public Task AFunctionThatReturnsAnObject_SetsItByItsName_AndUsesIt()
        => AssertRunsAsWithoutTheEnumAsync("2", "    Debug.Print c.F().Count",
            "Public Function F() As Collection", "    Set F = New Collection", "    F.Add 1", "    F.Add 2", "End Function");

    [TestMethod]
    public Task AFunctionThatReturnsAnObject_IsSetToAVariable()
        => AssertRunsAsWithoutTheEnumAsync("2", "    Dim col As Collection\r\n    Set col = c.F()\r\n    Debug.Print col.Count",
            "Public Function F() As Collection", "    Set F = New Collection", "    F.Add 1", "    F.Add 2", "End Function");

    [TestMethod]
    public Task APropertyGet_AssignsItsResultByItsName()
        => AssertRunsAsWithoutTheEnumAsync("1", "    Debug.Print c.F",
            "Public Property Get F() As Long", "    F = 1", "End Property");
}
