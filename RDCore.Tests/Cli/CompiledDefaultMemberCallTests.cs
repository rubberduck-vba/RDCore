using static RDCore.Tests.Cli.CompiledErrorHandlingTests;
using static RDCore.Tests.Cli.CompiledVariantParameterOfAnotherTypeTests;

namespace RDCore.Tests.Cli;

/// <summary>
/// The default member of an object of a class of the workspace, called by an index expression on an object variable (<c>F(K)</c>,
/// <strong>MS-VBAL §5.6.13</strong>): compiled code calls the member the class marks <c>VB_UserMemId = 0</c> on the object, as the interpreter calls it.
/// An object of another class, reached through the class as an interface, is the interpreter's to call.
/// </summary>
[TestClass]
public sealed class CompiledDefaultMemberCallTests
{
    private static string Program(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // values by key, read by the default member, which returns a Variant; and a count by number, which returns a Long.
    private static (string Name, string Source)[] Classes() =>
    [
        ("Flags", ModuleWorkspace.ClassModule("Flags",
            "Option Explicit",
            "Private m As Collection",
            "Private Sub Class_Initialize()",
            "    Set m = New Collection",
            "End Sub",
            "Public Sub Add(ByVal Value As Variant, ByVal Key As String)",
            "    m.Add Value, Key",
            "End Sub",
            "Public Property Get Item(Key As Variant) As Variant",
            "Attribute Item.VB_UserMemId = 0",
            "    Item = m(Key)",
            "End Property")),
        ("Squares", ModuleWorkspace.ClassModule("Squares",
            "Option Explicit",
            "Public Function Value(ByVal N As Long) As Long",
            "Attribute Value.VB_UserMemId = 0",
            "    Value = N * N",
            "End Function")),
    ];

    // a default member that returns a Variant, the condition of an If, given a Variant and a String by reference.
    [TestMethod]
    public async Task TheDefaultMemberInACondition_IsCalledByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Counted()",
            "End Sub",
            "Private Function Counted() As String",
            "    Dim F As Flags, I As Long, K As Variant, J As String, T As String",
            "    Set F = New Flags",
            "    F.Add True, \"a\"",
            "    F.Add False, \"b\"",
            "    For I = 1 To 3",
            "        K = \"a\"",
            "        J = \"b\"",
            "        If F(K) Then T = T & \"i\"",
            "        If F(J) Then T = T & \"x\" Else T = T & \"d\"",
            "    Next",
            "    Counted = T",
            "End Function"), classes: Classes(), expected: "ididid");
        AssertNoneStepped(compiled, "Counted");
    }

    // a default member that returns a Long, an operand of an operation, and assigned whole.
    [TestMethod]
    public async Task TheDefaultMemberInAnOperation_IsCalledByCompiledCode()
    {
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Debug.Print Summed()",
            "End Sub",
            "Private Function Summed() As Long",
            "    Dim S As Squares, I As Long, T As Long, L As Long",
            "    Set S = New Squares",
            "    For I = 1 To 4",
            "        T = T + S(I)",
            "        L = S(I)",
            "    Next",
            "    Summed = T + L",
            "End Function"), classes: Classes(), expected: "46");
        AssertNoneStepped(compiled, "Summed");
    }

    // an object of a class that implements the declared class as an interface: compiled code gives the call to the interpreter, and the program runs the
    // same either way.
    [TestMethod]
    public async Task TheDefaultMemberOfAnObjectOfAnotherClass_IsCalledByTheInterpreter()
    {
        var classes = Classes().Append(("Cubes", ModuleWorkspace.ClassModule("Cubes",
            "Option Explicit",
            "Implements Squares",
            "Private Function Squares_Value(ByVal N As Long) As Long",
            "    Squares_Value = N * N * N",
            "End Function"))).ToArray();
        var compiled = await AssertAlikeAsync(Program(
            "Public Sub Main()",
            "    Dim S As Squares",
            "    Set S = New Squares",
            "    Debug.Print Summed(S);",
            "    Set S = New Cubes",
            "    Debug.Print Summed(S)",
            "End Sub",
            "Private Function Summed(ByVal S As Squares) As Long",
            "    Dim I As Long, T As Long",
            "    For I = 1 To 3",
            "        T = T + S(I)",
            "    Next",
            "    Summed = T",
            "End Function"), classes: classes);
        Assert.IsTrue(compiled.TryGetValue("Summed", out var result), "Summed was not called");
        Assert.IsNotNull(result.Body, string.Join("; ", result.Reasons.Select(reason => $"{reason.Kind} {reason.Detail}")));
    }
}
