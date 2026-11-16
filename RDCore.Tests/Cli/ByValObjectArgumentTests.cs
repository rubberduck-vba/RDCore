using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A value that is not an object, given to a <c>ByVal</c> parameter declared as a class or as <c>Object</c>: the argument is Let-assigned to the
/// parameter (<strong>MS-VBAL §5.3.1.11</strong>), and the Let-coercion of a value other than an object to a class or <c>Object</c> raises error 424
/// (<strong>MS-VBAL §5.5.1.2.13</strong>), before the procedure is entered. Each row is what VB6 does (observed in VB6, native code).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2.13 Let-coercion to and from a class or Object or Nothing")]
public sealed class ByValObjectArgumentTests
{
    private static readonly (string Name, string Source)[] Classes = [("Thing", ClassModule("Thing", "Option Explicit", "Public X As Long"))];

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
        "    Dim vStr As Variant, vLng As Variant, vEmpty As Variant, s As String",
        "    vStr = \"x\": vLng = 5: s = \"x\"",
        "    On Error Resume Next",
        $"    {statement}",
        "    Debug.Print Err.Number",
        "End Sub",
        string.Empty);

    [TestMethod]
    [DataRow("TakeObj vStr", DisplayName = "Object parameter, a Variant that holds a String")]
    [DataRow("TakeObj vLng", DisplayName = "Object parameter, a Variant that holds a Long")]
    [DataRow("TakeObj Id(vLng)", DisplayName = "Object parameter, a Variant a function returns")]
    [DataRow("TakeCol vStr", DisplayName = "Collection parameter, a Variant that holds a String")]
    [DataRow("TakeCol vLng", DisplayName = "Collection parameter, a Variant that holds a Long")]
    [DataRow("TakeCol vEmpty", DisplayName = "Collection parameter, a Variant that holds Empty")]
    [DataRow("TakeThing vStr", DisplayName = "class parameter, a Variant that holds a String")]
    [DataRow("TakeThing vLng", DisplayName = "class parameter, a Variant that holds a Long")]
    public async Task AValueOtherThanAnObject_GivenByValue_IsObjectRequired_BeforeTheCall(string statement)
    {
        foreach (var engine in new[] { VBExecutionEngine.Interpreter, VBExecutionEngine.Compiled })
        {
            var output = await RunAsync(Classes, Program(statement), engine);

            Assert.AreEqual("424", string.Join(" | ", output), engine.ToString());
        }
    }
}
