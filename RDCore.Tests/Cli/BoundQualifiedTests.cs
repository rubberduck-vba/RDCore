using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A name qualified by its module, its project or its enumeration, an index of a value that is no variable, and a member of any value, bound
/// (IR lowering S0.12b), evaluate to what MS-VBAL 5.6.12 and 5.6.13 say they do, and raise what they say they raise.
/// </summary>
[TestClass]
public sealed class BoundQualifiedTests
{
    private static readonly IReadOnlyList<(string Name, string Source)> Classes =
    [
        ("Point", ClassModule("Point",
            "Public X As Long",
            "Public Inner As Point")),
    ];

    private static readonly IReadOnlyList<(string Name, string Source)> Modules =
    [
        ("Globals", StandardModule("Globals",
            "Public Count As Long",
            "Public Items(2) As String",
            "Public Enum Color",
            "    Red = 1",
            "    Green = 2",
            "End Enum",
            "Public Function Twice(ByVal N As Long) As Long", "    Twice = 2 * N", "End Function",
            "Public Function Letters() As Variant", "    Letters = Array(\"a\", \"b\", \"c\")", "End Function")),
    ];

    private static Task<string[]> RunMainAsync(params string[] body)
        => RunAsync(Classes, Modules, $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", body)}\r\n");

    private static Task<string[]> RunMainBodyAsync(params string[] body)
        => RunMainAsync(["Public Sub Main()", .. body, "End Sub"]);

    // MS-VBAL 5.6.12: a member of a procedural module, named through it, is what the name means unqualified there - read, called, indexed.
    [TestMethod]
    public async Task AMemberOfAModule_NamedThroughIt_IsReadCalledAndIndexed()
        => CollectionAssert.AreEqual(new[] { "3", "14", "b", "c", "4" }, await RunMainBodyAsync(
            "Dim I As Long",
            "For I = 1 To 3", "    Globals.Count = Globals.Count + 1", "Next",
            "Debug.Print Globals.Count",
            "Debug.Print Globals.Twice(7)",
            "Globals.Items(1) = \"b\"", "Debug.Print Globals.Items(1)",
            "Debug.Print Globals.Letters()(2)",
            "Debug.Print VBA.Len(\"RD-V\")"));

    // MS-VBAL 5.6.12: a member of an enumeration named by its type is the constant; its module can qualify the enumeration.
    [TestMethod]
    public async Task AMemberOfAnEnumeration_NamedThroughIt_IsTheConstant()
        => CollectionAssert.AreEqual(new[] { "1", "2", "3" }, await RunMainBodyAsync(
            "Debug.Print Color.Red",
            "Debug.Print Globals.Color.Green",
            "Debug.Print Color.Red + Color.Green"));

    // MS-VBAL 5.6.13: what a call returns is indexed by what follows it - an element of the array it is.
    [TestMethod]
    public async Task WhatACallReturns_IsIndexed()
        => CollectionAssert.AreEqual(new[] { "c", "b", "9" }, await RunMainBodyAsync(
            "Debug.Print Letters()(2)",
            "Dim v As Variant", "v = Array(Array(\"a\", \"b\"), 1)",
            "Debug.Print v(0)(1)",
            "On Error Resume Next",
            "Debug.Print Letters()(3)", "Debug.Print Err.Number"));

    // a member of what an element, a member or a call is: the owner evaluated as the value it is, the member found in it.
    [TestMethod]
    public async Task AMemberOfAnyValue_IsFoundInIt()
        => CollectionAssert.AreEqual(new[] { "5", "7", "12", "91" }, await RunMainAsync(
            "Private Type Pair",
            "    X As Long",
            "End Type",
            "Private Function Made() As Point",
            "    Dim result As New Point",
            "    result.X = 12",
            "    Set Made = result",
            "End Function",
            "Public Sub Main()",
            "    Dim pairs(1) As Pair, p As New Point",
            "    pairs(1).X = 5",
            "    Debug.Print pairs(1).X",
            "    Set p.Inner = New Point", "    p.Inner.X = 7",
            "    Debug.Print p.Inner.X",
            "    Debug.Print Made.X",
            "    On Error Resume Next",
            "    Set p.Inner = Nothing",
            "    Debug.Print p.Inner.X", "    Debug.Print Err.Number",
            "End Sub"));

    // MS-VBAL 5.6.15: a member of the target of a With block declared Object or Variant is found in the object it holds.
    [TestMethod]
    public async Task AMemberOfALateBoundWithTarget_IsFoundInTheObject()
        => CollectionAssert.AreEqual(new[] { "2", "b", "438" }, await RunMainBodyAsync(
            "Dim o As Object",
            "Set o = New Collection", "o.Add \"a\"", "o.Add \"b\"",
            "With o",
            "    Debug.Print .Count",
            "    Debug.Print .Item(2)",
            "    On Error Resume Next",
            "    Debug.Print .Nothingness", "    Debug.Print Err.Number",
            "End With"));
}
