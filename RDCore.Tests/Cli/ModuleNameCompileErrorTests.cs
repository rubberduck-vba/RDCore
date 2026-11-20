using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A module's name is an <c>IDENTIFIER</c> (<strong>MS-VBAL §4.2</strong>, the <c>quoted-identifier</c> of its <c>VB_Name</c>), which no
/// <c>reserved-identifier</c> is (<strong>MS-VBAL §3.3.5.2</strong>): a module named <c>Circle</c> or <c>Dim</c> is
/// <see cref="RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.NotALegalObjectName"/>, at its <c>VB_Name</c>, and does not load. MS-VBA refuses to rename
/// or import a module so named (observed in Excel). A module that says no <c>VB_Name</c> is named by its file, by the same rule. A
/// <c>reserved-type-identifier</c> that is no <c>reserved-name</c> (<c>Long</c>) is an <c>IDENTIFIER</c>, and compiles. A module named as a library the
/// project references (<c>VBA</c>, <c>Excel</c>) is
/// <see cref="RDCore.Runtime.LanguageCore.Errors.VBCompileErrorId.NameConflictsWithModuleProjectOrLibrary"/>, and does not load either.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 4.2 Modules")]
public sealed class ModuleNameCompileErrorTests
{
    private const string NotALegalObjectName = "Not a legal object name";

    private static readonly string Program = StandardModule("Program", "Public Sub Main()", "End Sub");

    private static string[] Body => ["Public Sub Work()", "End Sub"];

    [TestMethod]
    [DataRow("Circle", DisplayName = "a special-form")]
    [DataRow("Dim", DisplayName = "a statement-keyword")]
    [DataRow("Len", DisplayName = "a reserved-name")]
    [DataRow("Date", DisplayName = "a reserved-name that is a reserved-type-identifier")]
    [DataRow("Me", DisplayName = "Me, a reserved-name")]
    [DataRow("True", DisplayName = "a literal-identifier")]
    [DataRow("Attribute", DisplayName = "a reserved-for-implementation-use")]
    [DataRow("VB_Name", DisplayName = "an attribute's name")]
    [DataRow("Decimal", DisplayName = "a future-reserved")]
    [DataRow("lbound", DisplayName = "a special-form, in lower case")]
    public async Task AStandardModuleNamedWithAReservedIdentifier_IsNotALegalObjectName(string name)
    {
        var errors = await LoadErrorsAsync([], [(name, StandardModule(name, Body))], Program);

        var error = Assert.ContainsSingle(errors, string.Join(" | ", errors));
        Assert.StartsWith(NotALegalObjectName, error);
        Assert.Contains($"'{name}'", error);
    }

    [TestMethod]
    public async Task AClassModuleNamedWithAReservedIdentifier_IsNotALegalObjectName()
    {
        var errors = await LoadErrorsAsync([("Circle", ClassModule("Circle", Body))], Program);

        Assert.StartsWith(NotALegalObjectName, Assert.ContainsSingle(errors, string.Join(" | ", errors)));
    }

    // the module says no VB_Name, and is named by its file.
    [TestMethod]
    public async Task AModuleNamedByItsFile_IsNamedByTheSameRule()
    {
        const string name = "Circle";
        var errors = await LoadErrorsAsync([], [(name, string.Join("\r\n", Body) + "\r\n")], Program);

        var error = Assert.ContainsSingle(errors, string.Join(" | ", errors));
        Assert.StartsWith(NotALegalObjectName, error);
        Assert.Contains($"'{name}'", error);
    }

    [TestMethod]
    [DataRow("Long", DisplayName = "a reserved-type-identifier that is no reserved-name")]
    [DataRow("Variant", DisplayName = "Variant, a reserved-type-identifier")]
    [DataRow("Local", DisplayName = "Local, which MS-VBA refuses and the specification does not reserve")]
    [DataRow("Name", DisplayName = "Name, which is no reserved-identifier")]
    [DataRow("Shapes", DisplayName = "a name")]
    public async Task AModuleNamedWithAnIdentifier_Loads(string name)
    {
        var errors = await LoadErrorsAsync([], [(name, StandardModule(name, Body))], Program);

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    // MS-VBAL §4.2: a module's name is not the project name of a project its project references, a library among them (§4.1). MS-VBA refuses to rename
    // a module so, case aside, and renames one it imports (Excel1); observed in Excel.
    [TestMethod]
    [DataRow("VBA", DisplayName = "the standard library")]
    [DataRow("vba", DisplayName = "the standard library, in lower case")]
    [DataRow("Excel", DisplayName = "a library the project references")]
    [DataRow("EXCEL", DisplayName = "a library the project references, in upper case")]
    public async Task AStandardModuleNamedAsAReferencedLibrary_ConflictsWithIt(string name)
    {
        var errors = await LoadErrorsAsync([], [(name, StandardModule(name, Body))], Program, Excel);

        var error = Assert.ContainsSingle(errors, string.Join(" | ", errors));
        Assert.StartsWith(NameConflicts, error);
        Assert.Contains($"'{name}'", error);
    }

    [TestMethod]
    public async Task AClassModuleNamedAsAReferencedLibrary_ConflictsWithIt()
    {
        var errors = await LoadErrorsAsync([("Excel", ClassModule("Excel", Body))], Program, Excel);

        Assert.StartsWith(NameConflicts, Assert.ContainsSingle(errors, string.Join(" | ", errors)));
    }

    [TestMethod]
    public async Task AModuleNamedByItsFile_AsAReferencedLibrary_ConflictsWithIt()
    {
        var errors = await LoadErrorsAsync([], [("Excel", string.Join("\r\n", Body) + "\r\n")], Program, Excel);

        Assert.StartsWith(NameConflicts, Assert.ContainsSingle(errors, string.Join(" | ", errors)));
    }

    // MS-VBA names a module as a library its project does not reference (Scripting).
    [TestMethod]
    public async Task AModuleNamedAsALibraryThatIsNotReferenced_Loads()
    {
        var errors = await LoadErrorsAsync([], [("Excel", StandardModule("Excel", Body))], Program);

        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    private const string NameConflicts = "Name conflicts with existing module, project, or object library";

    private static readonly WorkspaceLibraries Excel = new(["Excel"], new InMemoryLibrarySource(new LibraryDescription
    {
        Name = "Excel",
        Classes = [new ClassDescription { Name = "Range" }],
    }));
}
