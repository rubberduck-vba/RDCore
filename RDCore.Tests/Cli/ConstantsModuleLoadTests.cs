using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §6.1.2.2</strong> the constants of the <c>Constants</c> module are declared names of the project: a module that names one under
/// <c>Option Explicit</c> loads, and runs with its value.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.2 Constants Module")]
public sealed class ConstantsModuleLoadTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("vbCrLf", DisplayName = "unqualified")]
    [DataRow("VBA.vbTab", DisplayName = "qualified by its library")]
    [DataRow("Constants.vbTab", DisplayName = "qualified by its module")]
    public async Task AModuleUnderOptionExplicit_NamingALibraryConstant_Loads(string constant)
        // a name the library did not declare would be an undeclared variable here (or a member the library does not have), and the module would not load.
        => CollectionAssert.AreEqual(Array.Empty<string>(), await LoadErrorsAsync([], Program($"Debug.Print Len({constant})")));

    [TestMethod]
    public async Task AModuleUnderOptionExplicit_RunsWithTheConstantsValues()
        => CollectionAssert.AreEqual(new[] { "2", "1", "-2147221504" },
            await RunAsync([], Program("Debug.Print Len(vbCrLf)", "Debug.Print Len(VBA.vbTab)", "Debug.Print vbObjectError")));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
    public async Task AnotherModulesVariableNamedConstants_IsTheVariable_NotTheModule()
    {
        // another procedural module's member comes before a library's procedural module (MS-VBAL §5.6.10): Constants is the variable, whose Round the
        // module does not have.
        var widget = ("Widget", ClassModule("Widget", "Option Explicit", "Public Round As Long"));
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Constants As New Widget"));

        CollectionAssert.AreEqual(new[] { "3" }, await RunAsync([widget], [mod2], Program("Constants.Round = 3", "Debug.Print Constants.Round")));
    }

    private static string Symbols
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RDCore.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "the repository root, where RDCore.slnx is");
            return Path.Combine(directory.FullName, "Symbols");
        }
    }

    // the Excel library that ships (Symbols/Excel.json), whose Enum Constants has the member xlAll, -4104.
    private static WorkspaceLibraries Excel => new(["Excel"], new DirectoryLibrarySource(new FileSystem(), Symbols));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
    public async Task WithExcelReferenced_ConstantsIsThisModule_AndExcelsConstantsAreStillExcels()
        // a referenced project's procedural module comes before the members of a referenced project's modules (MS-VBAL §5.6.10), so Constants is this module
        // ahead of Excel's Enum Constants; Excel's constants are still Excel's, qualified by the library or alone.
        => CollectionAssert.AreEqual(new[] { "2", "-4104", "-4104", "-4104" }, await RunAsync([], Program(
            "Debug.Print Len(Constants.vbCrLf)", "Debug.Print Excel.Constants.xlAll", "Debug.Print xlAll", "Dim c As Excel.Constants", "c = xlAll", "Debug.Print c"), Excel));

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
    public async Task WithExcelReferenced_AConstantOfExcelsEnum_IsNotAMemberOfThisModule()
    {
        // without this module, Constants.xlAll was Excel's Enum's member and printed -4104.
        var errors = await LoadErrorsAsync([], Program("Debug.Print Constants.xlAll"), Excel);

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.Contains(errors[0], "Method or data member not found");
        StringAssert.Contains(errors[0], "xlAll");
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
    [DataRow(true, DisplayName = "with Excel referenced")]
    [DataRow(false, DisplayName = "with the standard library alone")]
    public async Task AsConstants_IsACompileError_TheModuleBeingNoType(bool excel)
    {
        // MS-VBAL §5.6.10's type binding context matches this module ahead of Excel's Enum Constants, and a procedural module is not a type (§5.6.16.7).
        // Without this module, the declaration was Excel's Enum, or with the standard library alone a type that was not defined.
        var program = Program("Dim c As Constants", "c = 1", "Debug.Print c");
        var errors = excel ? await LoadErrorsAsync([], program, Excel) : await LoadErrorsAsync([], program);

        Assert.HasCount(1, errors, string.Join("; ", errors));
        StringAssert.Contains(errors[0], string.Format(RDCore.SDK.Exceptions.VBCompileError_DeclaredTypeIsAProceduralModule_Verbose, "Constants"));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
    public async Task WithExcelReferenced_AsExcelConstants_IsExcelsEnum_AsAParameterAndAResult()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nDebug.Print F(xlAll)\r\nEnd Sub\r\n"
            + "Private Function F(ByVal c As Excel.Constants) As Excel.Constants\r\nF = c\r\nEnd Function\r\n";

        CollectionAssert.AreEqual(new[] { "-4104" }, await RunAsync([], program, Excel));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
    public async Task WithExcelReferenced_AsExcelConstants_IsExcelsEnum_AsALocalConstantsType()
    {
        // the constant's type travels to the host named for its library, as a variable's does: bare, the host would bind this module, which is no type. The
        // procedure is never called: a constant whose As clause names a type that is not built in stops the run when it is read, which is another matter.
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\nDebug.Print 1\r\nEnd Sub\r\n"
            + "Private Sub NeverCalled()\r\nConst c As Excel.Constants = 1\r\nDebug.Print c\r\nEnd Sub\r\n";

        CollectionAssert.AreEqual(new[] { "1" }, await RunAsync([], program, Excel));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
    [DataRow(true, DisplayName = "with Excel referenced")]
    [DataRow(false, DisplayName = "with the standard library alone")]
    public async Task AnotherModulesTypeNamedConstants_IsTheType_NotThisModule(bool excel)
    {
        // another module's accessible user-defined type comes before a referenced project's procedural module in the type binding context (MS-VBAL §5.6.10).
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Type Constants", "X As Long", "End Type"));

        CollectionAssert.AreEqual(new[] { "3" }, await RunAsync([], [mod2], Program("Dim x As Constants", "x.X = 3", "Debug.Print x.X"), excel ? Excel : null));
    }
}