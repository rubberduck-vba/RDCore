using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.2.4.2</strong>: a variable declared as the class an <c>Implements</c> directive names has the members of that class, and a call of
/// one runs the procedure of the object's class that implements it. A member of the interface named through such a variable, a parameter or the element
/// variable of a <c>For Each</c> is found, compiles, and runs.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.4.2 Implements Directive")]
public sealed class InterfaceVariableMemberTests
{
    private static readonly (string Name, string Source) IShape = ("IShape", ClassModule("IShape",
        "Option Explicit",
        "Public Property Get Area() As Double",
        "End Property",
        "Public Function Describe() As String",
        "End Function"));

    private static readonly (string Name, string Source) CircleShape = ("CircleShape", ClassModule("CircleShape",
        "Option Explicit",
        "Implements IShape",
        "Public Radius As Double",
        "Private Property Get IShape_Area() As Double",
        "    IShape_Area = Radius * Radius * 3",
        "End Property",
        "Private Function IShape_Describe() As String",
        "    IShape_Describe = \"Circle\"",
        "End Function"));

    private static string Program(string declarations, string body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations.Replace(" ;; ", "\r\n")}\r\n"
            + $"Public Sub Main()\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";

    private const string Made = "Dim Disc As New CircleShape ;; Disc.Radius = 2";

    [TestMethod]
    [DataRow("", Made + " ;; Dim Shape As IShape ;; Set Shape = Disc ;; Debug.Print Shape.Describe & \": \" & Shape.Area",
        DisplayName = "a local variable")]
    [DataRow("Private Sub Report(ByVal Shape As IShape) ;; Debug.Print Shape.Describe & \": \" & Shape.Area ;; End Sub", Made + " ;; Report Disc",
        DisplayName = "a parameter")]
    [DataRow("Private Shape As IShape", Made + " ;; Set Shape = Disc ;; Debug.Print Shape.Describe & \": \" & Shape.Area",
        DisplayName = "a field of the module")]
    [DataRow("", Made + " ;; Dim Shapes As New Collection, Shape As IShape ;; Shapes.Add Disc ;; For Each Shape In Shapes"
        + " ;; Debug.Print Shape.Describe & \": \" & Shape.Area ;; Next", DisplayName = "the element variable of a For Each")]
    [DataRow("", Made + " ;; Dim Shape As IShape ;; Set Shape = Disc ;; With Shape ;; Debug.Print .Describe & \": \" & .Area ;; End With",
        DisplayName = "a With block")]
    public async Task AMemberOfTheInterface_NamedThroughAVariableOfIt_RunsTheImplementation(string declarations, string body)
    {
        var program = Program(declarations, body);
        var errors = await LoadErrorsAsync([IShape, CircleShape], program);
        Assert.IsEmpty(errors, string.Join(" | ", errors));

        CollectionAssert.AreEqual(new[] { "Circle: 12" }, await RunAsync([IShape, CircleShape], program));
    }
}
