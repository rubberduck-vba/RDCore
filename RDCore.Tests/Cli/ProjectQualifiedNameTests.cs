namespace RDCore.Tests.Cli;

/// <summary>
/// The names of a project's own modules and types, qualified by the project's name (<strong>MS-VBAL §5.6.12</strong>): <c>Project1.Widget</c> is the class
/// <c>Widget</c> of the enclosing project, as <c>Excel.Range</c> is a library's. MVVM qualifies its own types everywhere (<c>MVVM.AppContext</c>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.12 Member Access Expressions")]
public sealed class ProjectQualifiedNameTests
{
    private static readonly (string Name, string Source) Widget = ("Widget", ModuleWorkspace.ClassModule("Widget",
        "Attribute VB_PredeclaredId = True",
        "Option Explicit",
        "Public Value As Long",
        "Public Enum WidgetKind",
        "    Plain = 1",
        "    Fancy = 2",
        "End Enum",
        "Public Function Create(ByVal value As Long) As Project1.Widget",
        "    Dim result As Project1.Widget",
        "    Set result = New Project1.Widget",
        "    result.Value = value",
        "    Set Create = result",
        "End Function"));

    private static readonly (string Name, string Source) Shapes = ("Shapes", ModuleWorkspace.StandardModule("Shapes",
        "Option Explicit",
        "Public Enum Colours",
        "    cRed = 1",
        "    cBlue = 2",
        "End Enum",
        "Public Type TPoint",
        "    X As Long",
        "    Y As Long",
        "End Type",
        "Public Function Twice(ByVal value As Long) As Long",
        "    Twice = value * 2",
        "End Function"));

    private static string Program(params string[] body)
        => ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. body, "End Sub"]);

    [TestMethod]
    public async Task TheProjectsOwnNames_AreQualifiedByItsName()
    {
        var printed = await ModuleWorkspace.RunAsync([Widget], [Shapes], Program(
            "Dim w As Project1.Widget",
            "Set w = Project1.Widget.Create(42)",
            "Debug.Print w.Value",
            "Debug.Print TypeName(w)",
            "Dim other As New Project1.Widget",
            "Debug.Print other.Value",
            "Debug.Print Project1.Shapes.Twice(21)",
            "Dim colour As Project1.Colours",
            "colour = Project1.Shapes.cBlue",
            "Debug.Print colour",
            "Dim p As Project1.TPoint",
            "p.X = 3",
            "Debug.Print p.X",
            "Dim kind As Project1.WidgetKind",
            "kind = Project1.WidgetKind.Fancy",
            "Debug.Print kind"), libraries: null);

        CollectionAssert.AreEqual(new[] { "42", "Widget", "0", "42", "2", "3", "2" }, printed, string.Join(" | ", printed));
    }

    // MS-VBA refuses a module named as its project ("Name conflicts with existing module, project, or object library"); the shell's own project is named as its
    // one module, which keeps the name.
    [TestMethod]
    public async Task AModuleNamedAsItsProject_KeepsTheName()
    {
        var printed = await ModuleWorkspace.RunAsync([], [("Project1", ModuleWorkspace.StandardModule("Project1", "Public Function Twice(ByVal value As Long) As Long",
            "    Twice = value * 2", "End Function"))], Program("Debug.Print Project1.Twice(4)"), libraries: null);

        CollectionAssert.AreEqual(new[] { "8" }, printed, string.Join(" | ", printed));
    }

    [TestMethod]
    public async Task ANameTheProjectDoesNotDeclare_IsNotOneOfIts()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([Widget], Program("Dim w As Project1.Gadget"));

        Assert.IsNotEmpty(errors, "the project declares no Gadget");
    }
}
