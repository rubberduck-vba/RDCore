using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A value that is not an object, given to a <c>ByVal</c> parameter declared as a class or as <c>Object</c>: the argument is Let-assigned to the
/// parameter (<strong>MS-VBAL §5.3.1.11</strong>), and the Let-coercion of a value other than an object to a class or <c>Object</c> raises error 424
/// (<strong>MS-VBAL §5.5.1.2.13</strong>), before the procedure is entered. A member reached late-bound, through an expression declared <c>Object</c>,
/// raises 13 instead, as the <c>IDispatch</c> the call goes through makes it. Each row is what VB6 does (observed in VB6, native code).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.13 Let-coercion to and from a class or Object or Nothing")]
public sealed class ByValObjectArgumentTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Thing", ClassModule("Thing", "Option Explicit", "Public X As Long",
            "Public Sub TakeObj(ByVal x As Object)", "    Debug.Print \"entered\"", "End Sub",
            "Public Sub TakeCol(ByVal x As Collection)", "    Debug.Print \"entered\"", "End Sub")),
    ];

    private static string Program(string statement) => string.Join("\r\n",
        "Attribute VB_Name = \"Program\"",
        "Option Explicit",
        "Private Sub TakeObj(ByVal x As Object)",
        "    Debug.Print \"entered\"",
        "End Sub",
        "Private Sub TakeCol(ByVal x As Collection)",
        "    Debug.Print \"entered\"",
        "End Sub",
        "Private Sub TakeThing(ByVal x As Thing)",
        "    Debug.Print \"entered\"",
        "End Sub",
        "Private Function Id(ByVal v As Variant) As Variant",
        "    Id = v",
        "End Function",
        "Public Sub Main()",
        "    Dim vStr As Variant, vLng As Variant, vEmpty As Variant, t As New Thing, o As Object",
        "    vStr = \"x\": vLng = 5: Set o = t",
        "    On Error Resume Next",
        $"    {statement}",
        "    Debug.Print Err.Number",
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow("TakeObj vStr", "424", DisplayName = "Object parameter, a Variant that holds a String")]
    [DataRow("TakeObj vLng", "424", DisplayName = "Object parameter, a Variant that holds a Long")]
    [DataRow("TakeObj Id(vLng)", "424", DisplayName = "Object parameter, a Variant a function returns")]
    [DataRow("TakeCol vStr", "424", DisplayName = "Collection parameter, a Variant that holds a String")]
    [DataRow("TakeCol vLng", "424", DisplayName = "Collection parameter, a Variant that holds a Long")]
    [DataRow("TakeCol vEmpty", "424", DisplayName = "Collection parameter, a Variant that holds Empty")]
    [DataRow("TakeThing vStr", "424", DisplayName = "class parameter, a Variant that holds a String")]
    [DataRow("TakeThing vLng", "424", DisplayName = "class parameter, a Variant that holds a Long")]
    [DataRow("t.TakeObj vStr", "424", DisplayName = "a method, early-bound, Object parameter, a Variant that holds a String")]
    [DataRow("o.TakeObj vStr", "13", DisplayName = "a method, late-bound, Object parameter, a Variant that holds a String")]
    [DataRow("o.TakeObj vLng", "13", DisplayName = "a method, late-bound, Object parameter, a Variant that holds a Long")]
    [DataRow("o.TakeCol vLng", "13", DisplayName = "a method, late-bound, Collection parameter, a Variant that holds a Long")]
    [DataRow("o.TakeCol vEmpty", "13", DisplayName = "a method, late-bound, Collection parameter, a Variant that holds Empty")]
    public async Task AValueOtherThanAnObject_GivenByValue_IsAnError_BeforeTheCall(string statement, string expected)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var output = await RunAsync(Classes, Program(statement), engine);

            Assert.AreEqual(expected, string.Join(" | ", output), engine.ToString());
        }
    }
}
