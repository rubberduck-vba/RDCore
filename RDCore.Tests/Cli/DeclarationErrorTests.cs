namespace RDCore.Tests.Cli;

/// <summary>
/// A module is valid when what it declares is, as well as every procedure of it: a module whose procedures are all fine is not loaded when it declares a
/// name twice, or an interface it does not implement.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2 Module Structure")]
public sealed class DeclarationErrorTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\nPublic Sub Main()\r\nDebug.Print \"ran\"\r\nEnd Sub\r\n";

    private static readonly (string, string) IShape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Function Area() As Double", "End Function"));

    [TestMethod]
    public async Task AValidModule_Loads()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([], Program("Public Total As Long")));

    [TestMethod]
    public async Task ANameDeclaredTwice_InTheScopeOfAModule_IsADuplicateDeclaration()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program("Public Total As Long", "Public Sub total()", "End Sub"));

        Assert.HasCount(1, errors);
        StringAssert.Contains(errors[0], "declared more than once");
    }

    [TestMethod]
    public async Task AModule_ThatNamesOneDefinedAfterIt_IsLoaded_ForTheCodeIsCheckedOnceEveryModuleIsDefined()
    {
        // A comes before B in the workspace, and uses it.
        var a = ("A", ModuleWorkspace.ClassModule("A", "Public Function Make() As B", "Set Make = New B", "End Function"));
        var b = ("B", ModuleWorkspace.ClassModule("B", "Public Size As Long"));

        CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([a, b], Program()));
        CollectionAssert.AreEqual(new[] { "ran" }, await ModuleWorkspace.RunAsync([a, b], Program()));
    }

    [TestMethod]
    public async Task TheAccessorsOfAProperty_AreTheOneDeclarationOfIt()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync(
            [("Box", ModuleWorkspace.ClassModule("Box", "Private mV As Long",
                "Public Property Get V() As Long", "V = mV", "End Property",
                "Public Property Let V(ByVal n As Long)", "mV = n", "End Property"))],
            Program()));

    [TestMethod]
    public async Task AClass_ThatDoesNotImplementWhatItsInterfaceDeclares_IsNotLoaded()
    {
        var square = ("Square", ModuleWorkspace.ClassModule("Square", "Implements IShape"));

        var errors = await ModuleWorkspace.LoadErrorsAsync([IShape, square], Program());

        Assert.IsNotEmpty(errors);
    }

    [TestMethod]
    public async Task AClass_ThatImplementsWhatItsInterfaceDeclares_Loads()
    {
        var square = ("Square", ModuleWorkspace.ClassModule("Square", "Implements IShape",
            "Private Function IShape_Area() As Double", "IShape_Area = 4", "End Function"));

        CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([IShape, square], Program()));
    }
}
