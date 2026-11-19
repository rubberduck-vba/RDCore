using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.2.4.2</strong>: a variable declared as the class an <c>Implements</c> directive names has the members of that class, and a call of
/// one runs the procedure of the object's class that implements it. A member of the interface named through such a variable, a parameter, a field, the
/// element variable of a <c>For Each</c> or a <c>With</c> block is found, compiles, and runs. A project that references Excel, whose <c>Shape</c> class
/// has no such member, means the same (<strong>MS-VBAL §5.6.10</strong>): a variable of the procedure or the module named <c>Shape</c> is that variable,
/// and a class of the project named <c>Shape</c> is that class.
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

    // the project's own class named as Excel's is.
    private static readonly (string Name, string Source) Shape = ("Shape", ClassModule("Shape",
        "Option Explicit",
        "Implements IShape",
        "Public Function Describe() As String",
        "    Describe = \"own\"",
        "End Function",
        "Private Property Get IShape_Area() As Double",
        "    IShape_Area = 1",
        "End Property",
        "Private Function IShape_Describe() As String",
        "    IShape_Describe = \"Shape\"",
        "End Function"));

    // the description of Excel that ships, read as a project that references it reads it; no Excel is started.
    private static WorkspaceLibraries Excel
        => new(["Excel"], new DirectoryLibrarySource(new FileSystem(), ShippedLibrariesTests.Symbols), AllowAutomation: false);

    private static string Program(string declarations, string body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations.Replace(" ;; ", "\r\n")}\r\n"
            + $"Public Sub Main()\r\n{body.Replace(" ;; ", "\r\n")}\r\nEnd Sub\r\n";

    private const string Made = "Dim Disc As New CircleShape ;; Disc.Radius = 2";

    public static IEnumerable<object[]> Namings =>
    [
        ["a local variable", "", Made + " ;; Dim Shape As IShape ;; Set Shape = Disc ;; Debug.Print Shape.Describe & \": \" & Shape.Area"],
        ["a parameter", "Private Sub Report(ByVal Shape As IShape) ;; Debug.Print Shape.Describe & \": \" & Shape.Area ;; End Sub", Made + " ;; Report Disc"],
        ["a field of the module", "Private Shape As IShape", Made + " ;; Set Shape = Disc ;; Debug.Print Shape.Describe & \": \" & Shape.Area"],
        ["the element variable of a For Each", "", Made + " ;; Dim Shapes As New Collection, Shape As IShape ;; Shapes.Add Disc ;; For Each Shape In Shapes"
            + " ;; Debug.Print Shape.Describe & \": \" & Shape.Area ;; Next"],
        ["a With block", "", Made + " ;; Dim Shape As IShape ;; Set Shape = Disc ;; With Shape ;; Debug.Print .Describe & \": \" & .Area ;; End With"],
    ];

    public static string NamingOf(System.Reflection.MethodInfo method, object[] row) => $"{method.Name} ({row[0]})";

    [TestMethod]
    [DynamicData(nameof(Namings), DynamicDataDisplayName = nameof(NamingOf))]
    public async Task AMemberOfTheInterface_NamedThroughAVariableOfIt_RunsTheImplementation(string naming, string declarations, string body)
    {
        var program = Program(declarations, body);
        var errors = await LoadErrorsAsync([IShape, CircleShape], program);
        Assert.IsEmpty(errors, $"{naming}: {string.Join(" | ", errors)}");

        CollectionAssert.AreEqual(new[] { "Circle: 12" }, await RunAsync([IShape, CircleShape], program), naming);
    }

    [TestMethod]
    [DynamicData(nameof(Namings), DynamicDataDisplayName = nameof(NamingOf))]
    public async Task InAProjectThatReferencesExcel_AVariableNamedShape_IsTheVariable_AndNotExcelsClass(string naming, string declarations, string body)
    {
        var program = Program(declarations, body);
        var errors = await LoadErrorsAsync([IShape, CircleShape], program, Excel);
        Assert.IsEmpty(errors, $"{naming}: {string.Join(" | ", errors)}");

        CollectionAssert.AreEqual(new[] { "Circle: 12" }, await RunAsync([IShape, CircleShape], program, Excel), naming);
    }

    [TestMethod]
    [DataRow("Dim s As Shape ;; Set s = New Shape ;; Debug.Print s.Describe", "own", DisplayName = "declared as the class")]
    [DataRow("Dim s As IShape ;; Set s = New Shape ;; Debug.Print s.Describe", "Shape", DisplayName = "made as the class, through the interface")]
    public async Task InAProjectThatReferencesExcel_ItsOwnClassNamedShape_IsItsOwn(string body, string expected)
    {
        var program = Program(string.Empty, body);
        var errors = await LoadErrorsAsync([IShape, Shape], program, Excel);
        Assert.IsEmpty(errors, string.Join(" | ", errors));

        CollectionAssert.AreEqual(new[] { expected }, await RunAsync([IShape, Shape], program, Excel));
    }
}
