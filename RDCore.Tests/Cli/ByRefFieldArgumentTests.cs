namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.11</strong> a public variable of an object, named as a member of it (<c>b.Count</c>, <c>Me.Count</c>), given to a <c>ByRef</c>
/// parameter: MS-VBA passes a copy, which what the callee assigns to does not write back. Named by itself in its own class, it is the variable, as a module's
/// variable is however it is named. Each expectation is what MS-VBA prints for the same code, observed in Excel.
/// </summary>
/// <remarks>
/// MS-VBAL §5.6.12 classifies <c>b.Count</c> as a variable, which §5.3.1.11 would pass by reference: RD-VBA does as MS-VBA does, and warns (RDC11026, RD-VBAL
/// §5.3.1.11).
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class ByRefFieldArgumentTests
{
    private static readonly (string Name, string Source)[] Box =
    [
        ("Box", ModuleWorkspace.ClassModule("Box",
            "Option Explicit",
            "Public Count As Long",
            "Public Loose As Variant",
            "Public Sub BumpMine()", "    Bump Me.Count", "End Sub",
            "Public Sub BumpOwn()", "    Bump Count", "End Sub")),
    ];

    private static readonly (string Name, string Source)[] Globals = [("Globals", ModuleWorkspace.StandardModule("Globals", "Option Explicit", "Public Counter As Long"))];

    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync(Box, Globals, string.Join("\r\n",
        [
            "Attribute VB_Name = \"Program\"",
            "Option Explicit",
            "Public Sub Bump(ByRef n As Long)", "    n = n + 1", "End Sub",
            "Public Sub Bump2(ByRef n As Integer)", "    n = n + 1", "End Sub",
            "Public Sub Set8(ByRef v As Variant)", "    v = 8", "End Sub",
            "Public Sub Main()",
            "    Dim b As Box",
            "    Set b = New Box",
            .. body,
            "End Sub",
        ]));

    [TestMethod]
    public async Task APublicVariableOfAnObject_PassedByRef_IsACopy()
        => CollectionAssert.AreEqual(new[] { "1" },
            await RunAsync("b.Count = 1", "Bump b.Count", "Debug.Print b.Count"));

    [TestMethod]
    public async Task APublicVariableOfItsOwnObject_NamedThroughMe_PassedByRef_IsACopy()
        => CollectionAssert.AreEqual(new[] { "0" },
            await RunAsync("b.BumpMine", "Debug.Print b.Count"));

    [TestMethod]
    public async Task APublicVariableOfItsOwnObject_NamedByItself_PassedByRef_IsTheVariable()
        => CollectionAssert.AreEqual(new[] { "2" },
            await RunAsync("b.BumpOwn", "b.BumpOwn", "Debug.Print b.Count"));

    [TestMethod]
    public async Task AModulesVariable_NamedThroughItsModule_PassedByRef_IsTheVariable()
        => CollectionAssert.AreEqual(new[] { "1", "2" },
            await RunAsync("Bump Globals.Counter", "Debug.Print Globals.Counter", "Bump Counter", "Debug.Print Counter"));

    [TestMethod]
    public async Task APublicVariableInAWithBlock_PassedByRef_IsACopy()
        => CollectionAssert.AreEqual(new[] { "5" },
            await RunAsync("b.Count = 5", "With b", "Bump .Count", "End With", "Debug.Print b.Count"));

    [TestMethod]
    public async Task APublicVariableOfAnObjectBoundLate_PassedByRef_IsACopy()
        => CollectionAssert.AreEqual(new[] { "5" },
            await RunAsync("Dim o As Object", "Set o = b", "o.Count = 5", "Bump o.Count", "Debug.Print o.Count"));

    [TestMethod]
    public async Task APublicVariableOfAnObject_PassedToAVariantParameter_IsACopy()
        => CollectionAssert.AreEqual(new[] { "1" },
            await RunAsync("b.Count = 1", "Set8 b.Count", "Debug.Print b.Count"));

    [TestMethod]
    public async Task APublicVariableOfAnotherType_PassedByRef_IsACopy()
        => CollectionAssert.AreEqual(new[] { "1" },
            await RunAsync("b.Count = 1", "Bump2 b.Count", "Debug.Print b.Count"));

    [TestMethod]
    public async Task APublicVariableInParentheses_PassedByRef_IsACopy()
        => CollectionAssert.AreEqual(new[] { "1" },
            await RunAsync("b.Count = 1", "Bump (b.Count)", "Debug.Print b.Count"));
}
