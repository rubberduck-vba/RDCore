using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.3.1.11</strong>: a variable of a class, or an array of one, given to a <c>ByRef</c> parameter of that type is the parameter, whichever order the modules of
/// the workspace were defined in. A module whose declarations are bound before the class module is composed names the class as it was then, and one bound after
/// names it as composed: the same class, whose variable the callee assigns.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.3.1.11 Procedure Invocation Argument Processing")]
public sealed class ByRefClassArgumentDefinitionOrderTests
{
    // defined in this order: the caller's declarations are bound before Ship is composed, the callee's after.
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Caller", ModuleWorkspace.ClassModule("Caller",
            "Option Explicit",
            "Public Function Play() As String",
            "    Dim hit As Ship",
            "    Dim grid As New Grid",
            "    grid.TryHit hit",
            "    If hit Is Nothing Then Play = \"Nothing\" Else Play = hit.Name",
            "End Function",
            "Public Function Fleet() As String",
            "    Dim ships() As Ship",
            "    Dim grid As New Grid",
            "    grid.Launch ships",
            "    Fleet = ships(0).Name",
            "End Function")),
        ("Ship", ModuleWorkspace.ClassModule("Ship",
            "Option Explicit",
            "Public Name As String")),
        ("Grid", ModuleWorkspace.ClassModule("Grid",
            "Option Explicit",
            "Public Sub TryHit(Optional ByRef hitShip As Ship)",
            "    Set hitShip = New Ship",
            "    hitShip.Name = \"Cruiser\"",
            "End Sub",
            "Public Sub Launch(ByRef ships() As Ship)",
            "    ReDim ships(0)",
            "    Set ships(0) = New Ship",
            "    ships(0).Name = \"Frigate\"",
            "End Sub")),
    ];

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task AnObjectVariableOrArrayPassedByReference_IsTheParameter_WhenItsModuleWasBoundBeforeTheClassWasComposed(VBExecutionEngine engine)
    {
        var printed = await ModuleWorkspace.RunAsync(Classes, ModuleWorkspace.StandardModule("Program",
            "Option Explicit",
            "Public Sub Main()",
            "    Dim c As New Caller",
            "    Debug.Print c.Play()",
            "    Debug.Print c.Fleet()",
            "End Sub"), engine);

        CollectionAssert.AreEqual(new[] { "Cruiser", "Frigate" }, printed, string.Join(" | ", printed));
    }
}
