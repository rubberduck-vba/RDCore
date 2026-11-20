using RDCore.SDK.Runtime.Abstract.Execution;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The name an <c>Implements</c> directive gives is a type expression of the class that has it (<strong>MS-VBAL §5.2.4.2</strong>, §5.6.16.7),
/// bound in the type binding context (§5.6.10): a class module of the enclosing project comes before any referenced project's. Excel's library
/// declares a hidden <c>IShape</c> interface, and a workspace that references Excel and has an <c>IShape</c> class of its own implements its own.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.4.2 Implements Directive")]
public sealed class ImplementsLibraryNamesakeTests
{
    private static (string Name, string Source)[] Shapes =>
    [
        ("IShape", ClassModule("IShape", "Option Explicit",
            "Public Function Describe() As String\r\nEnd Function", "Public Property Get Area() As Double\r\nEnd Property")),
        ("CircleShape", ClassModule("CircleShape", "Option Explicit", "Implements IShape",
            "Private Function IShape_Describe() As String\r\nIShape_Describe = \"circle\"\r\nEnd Function",
            "Private Property Get IShape_Area() As Double\r\nIShape_Area = 3\r\nEnd Property")),
    ];

    private static string Program(params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n"
            + "Private Sub Show(ByVal Shape As IShape)\r\nDebug.Print Shape.Describe\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter, "Debug.Print Shape.Describe", "circle", DisplayName = "A function, interpreted")]
    [DataRow(VBExecutionEngine.Compiled, "Debug.Print Shape.Describe", "circle", DisplayName = "A function, compiled")]
    [DataRow(VBExecutionEngine.Interpreter, "Debug.Print Shape.Area", "3", DisplayName = "A property, interpreted")]
    [DataRow(VBExecutionEngine.Compiled, "Debug.Print Shape.Area", "3", DisplayName = "A property, compiled")]
    [DataRow(VBExecutionEngine.Interpreter, "Show Shape", "circle", DisplayName = "Given to a parameter of the interface, interpreted")]
    [DataRow(VBExecutionEngine.Compiled, "Show Shape", "circle", DisplayName = "Given to a parameter of the interface, compiled")]
    [DataRow(VBExecutionEngine.Interpreter, "Debug.Print TypeOf Shape Is IShape", "True", DisplayName = "TypeOf, interpreted")]
    [DataRow(VBExecutionEngine.Compiled, "Debug.Print TypeOf Shape Is IShape", "True", DisplayName = "TypeOf, compiled")]
    public async Task WithExcelReferenced_AClassThatImplementsIShape_ImplementsTheWorkspacesIShape(VBExecutionEngine engine, string use, string expected)
    {
        // Set raised error 13: the class implemented Excel's IShape, and the variable is of the workspace's.
        var output = await RunAsync(Shapes, Program("Dim Shape As IShape", "Set Shape = New CircleShape", use), DocumentModuleTests.Excel, engine);

        CollectionAssert.AreEqual(new[] { expected }, output);
    }

    [TestMethod]
    [DataRow(VBExecutionEngine.Interpreter)]
    [DataRow(VBExecutionEngine.Compiled)]
    public async Task WithExcelReferenced_AnObjectOfTheClass_IsOfTheWorkspacesIShape(VBExecutionEngine engine)
    {
        var output = await RunAsync(Shapes, Program(
            "Dim Shapes As New Collection", "Shapes.Add New CircleShape", "Dim Item As Variant", "For Each Item In Shapes",
            "Debug.Print TypeOf Item Is IShape", "Dim Shape As IShape", "Set Shape = Item", "Debug.Print Shape.Describe", "Next"),
            DocumentModuleTests.Excel, engine);

        CollectionAssert.AreEqual(new[] { "True", "circle" }, output);
    }
}
