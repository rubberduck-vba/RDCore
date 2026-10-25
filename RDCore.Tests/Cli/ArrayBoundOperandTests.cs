using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §3.3.5.2</strong> What <c>LBound</c> and <c>UBound</c> ask the bounds of is an array, written as a name, a member or a call: MS-VBA refuses
/// any other expression, and any declared type that never holds an array, when it compiles.
/// </summary>
/// <remarks>
/// Each expected outcome is what MS-VBA did with the same code, in Excel (64-bit, en-US, oracle\s29 U cases).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 3.3.5.2 Reserved Identifiers")]
public sealed class ArrayBoundOperandTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
        [("Counter", ClassModule("Counter", "Public Value As Long"))];

    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
        [("Globals", StandardModule("Globals", "Public Enum Color", "    Red = 1", "End Enum",
            "Public Function NoArgs() As Variant", "    NoArgs = Array(10, 20, 30)", "End Function"))];

    private static string Program(params string[] body) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Private Type Pair", "    X As Long", "End Type",
        "Public Sub Main()",
        "    " + string.Join("\r\n    ", body),
        "End Sub",
        string.Empty);

    private static async Task AssertError(string expected, params string[] body)
    {
        var errors = await LoadErrorsAsync(Classes, Modules, Program(body));

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.StartsWith(errors[0], expected);
    }

    [TestMethod]
    [DataRow("Debug.Print UBound(5)")]
    [DataRow("Debug.Print UBound(\"abc\")")]
    [DataRow("Debug.Print UBound(True)")]
    [DataRow("Debug.Print UBound(Nothing)")]
    [DataRow("Debug.Print UBound(Empty)")]
    [DataRow("Dim n As Long: Debug.Print UBound(n + 1)")]
    [DataRow("Dim a(2) As Long: Debug.Print UBound((a))")]
    public Task WhatIsNoNameMemberOrCall_IsASyntaxError(string statement)
        => AssertError("Syntax error", statement);

    [TestMethod]
    [DataRow("Dim c As Collection: Debug.Print UBound(c)")]
    [DataRow("Dim c As Collection: Debug.Print LBound(c)")]
    [DataRow("Dim o As Object: Debug.Print UBound(o)")]
    [DataRow("Dim c As Counter: Debug.Print UBound(c)")]
    [DataRow("Dim q As Pair: Debug.Print UBound(q)")]
    [DataRow("Debug.Print UBound(Strings)")]
    [DataRow("Debug.Print UBound(VBA)")]
    [DataRow("Debug.Print UBound(Color)")]
    public Task WhatNeverHoldsAnArray_IsExpectedArray(string statement)
        => AssertError("Expected array", statement);

    [TestMethod]
    [DataRow("Dim a(2) As Long: Debug.Print UBound(a, 1)", "2")]
    [DataRow("Dim v As Variant: v = Array(1, 2): Debug.Print UBound(v)", "1")]
    [DataRow("Debug.Print UBound(NoArgs())", "2")]
    [DataRow("Debug.Print UBound(Array(1, 2, 3))", "2")]
    public async Task AnArray_OrWhatMayHoldOne_IsMeasured(string statement, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await RunAsync(Classes, Modules, Program(statement)));

    [TestMethod]
    public async Task AVariantThatHoldsNoArray_IsError13()
        => CollectionAssert.AreEqual(new[] { "13" }, await RunAsync(Classes, Modules, Program(
            "Dim v As Variant", "On Error Resume Next", "Debug.Print UBound(v)", "Debug.Print Err.Number")));
}
