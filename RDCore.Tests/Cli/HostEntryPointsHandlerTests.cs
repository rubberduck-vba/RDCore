using RDCore.SDK.Workspace;
using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// <c>rdcore/host/entryPoints</c> over a workspace the host has defined as the platform defines it: where a program starts when nothing names the module, a
/// <c>Sub Main</c> of a standard module that the project can call, which takes no argument or one <c>ParamArray</c>, as the session defined it - the live
/// branch of a <c>#If</c> block's, under the project's constants.
/// </summary>
[TestClass]
public sealed class HostEntryPointsHandlerTests
{
    [TestMethod]
    [DataRow("Public Sub Main()", DisplayName = "Public Sub Main()")]
    [DataRow("Sub Main()", DisplayName = "Sub Main(), Public by default")]
    [DataRow("Global Sub Main()", DisplayName = "Global Sub Main()")]
    [DataRow("Public Static Sub Main()", DisplayName = "Public Static Sub Main()")]
    [DataRow("Static Sub Main()", DisplayName = "Static Sub Main()")]
    [DataRow("Public Sub main()", DisplayName = "the name in another case")]
    [DataRow("Public Sub Main(ParamArray Args())", DisplayName = "Sub Main(ParamArray Args())")]
    [DataRow("Public Sub Main(ParamArray Args() As Variant)", DisplayName = "Sub Main(ParamArray Args() As Variant)")]
    [DataRow("Private Sub Helper()\r\nEnd Sub\r\nPublic Sub Main()", DisplayName = "among other procedures")]
    public async Task ASubMainTheProjectCanCall_ThatTakesNoArgumentOrAParamArray_IsWhereTheProgramStarts(string header)
        => CollectionAssert.AreEqual(new[] { "Program" }, await EntryPointsAsync(StandardModule("Program", header, "End Sub")));

    [TestMethod]
    [DataRow("Private Sub Main()\r\nEnd Sub", DisplayName = "Private Sub Main()")]
    [DataRow("Public Sub Main(ByVal Count As Long)\r\nEnd Sub", DisplayName = "a parameter")]
    [DataRow("Public Sub Main(Optional ByVal Count As Long)\r\nEnd Sub", DisplayName = "an optional parameter")]
    [DataRow("Public Sub Main(ByVal Name As String, ParamArray Args())\r\nEnd Sub", DisplayName = "a parameter before the ParamArray")]
    [DataRow("Public Function Main()\r\nEnd Function", DisplayName = "a Function")]
    [DataRow("Public Property Get Main()\r\nEnd Property", DisplayName = "a Property Get")]
    [DataRow("Public Property Let Main(ByVal Value As Long)\r\nEnd Property", DisplayName = "a Property Let")]
    [DataRow("Public Declare PtrSafe Sub Main Lib \"user32\" Alias \"MessageBeep\" ()", DisplayName = "a Declare Sub")]
    [DataRow("Public Sub Start()\r\nEnd Sub", DisplayName = "another name")]
    [DataRow("Public Main As Long", DisplayName = "a variable")]
    public async Task AnythingElse_IsNot(string declaration)
        => Assert.IsEmpty(await EntryPointsAsync(StandardModule("Program", declaration)));

    // the session gives a class's procedures its object as their first parameter (Me): a Sub Main of a class, a form or a document is kept out by that, besides
    // its module's kind, which ProgramEntryPointTests tells apart.
    [TestMethod]
    public async Task ASubMainOfAClass_IsNot()
        => Assert.IsEmpty(await EntryPointsAsync(StandardModule("Program"), classes: [("Helper", ClassModule("Helper", "Public Sub Main()", "End Sub"))]));

    [TestMethod]
    public async Task ASubMainOfADocument_IsNot()
        => Assert.IsEmpty(await EntryPointsAsync(StandardModule("Program"), libraries: DocumentModuleTests.Excel,
            documents: [("GameSheet", "Option Explicit\r\nPublic Sub Main()\r\nEnd Sub\r\n", SDK.Workspace.DocClassType.ExcelWorksheet)]));

    [TestMethod]
    public async Task ASubMainOfAForm_IsNot()
        => Assert.IsEmpty(await EntryPointsAsync(StandardModule("Program"), libraries: new WorkspaceLibraries(["MSForms"], UnattendedRunTests.Shipped),
            forms: [UnattendedRunTests.ExampleView("Public Sub Main()\r\nEnd Sub\r\n")]));

    [TestMethod]
    public async Task AnEntryPointOfAnotherName_IsTheSubOfThatName()
    {
        var program = StandardModule("Program", "Public Sub Start()", "End Sub");

        CollectionAssert.AreEqual(new[] { "Program" }, await EntryPointsAsync(program, entryPoint: "Start"));
        Assert.IsEmpty(await EntryPointsAsync(program));
    }

    [TestMethod]
    public async Task EveryStandardModuleThatDeclaresOne_IsSaid()
        => CollectionAssert.AreEquivalent(new[] { "Other", "Program" }, await EntryPointsAsync(
            StandardModule("Program", "Public Sub Main()", "End Sub"), modules: [("Other", StandardModule("Other", "Sub Main(ParamArray Args())", "End Sub"))]));

    // VBA7 is True in the host's session: of the headers a Sub Main has in the branches of a #If block, the one under #If VBA7 is the declaration.
    [TestMethod]
    [DataRow("#If VBA7 Then\r\nPublic Sub Main()\r\n#Else\r\nPrivate Sub Main(ByVal Count As Long)\r\n#End If", true, DisplayName = "the live header is one")]
    [DataRow("#If VBA7 Then\r\nPrivate Sub Main(ByVal Count As Long)\r\n#Else\r\nPublic Sub Main()\r\n#End If", false, DisplayName = "only a dead header is one")]
    [DataRow("#If Not VBA7 Then\r\nPrivate Sub Main()\r\n#Else\r\nSub Main(ParamArray Args())\r\n#End If", true, DisplayName = "the live header is the #Else's")]
    public async Task ASubMainWhoseHeaderIsWrittenInEachBranchOfAnIf_IsWhereTheProgramStarts_WhenItsLiveHeaderIs(string headers, bool starts)
        => CollectionAssert.AreEqual(starts ? new[] { "Program" } : [], await EntryPointsAsync(StandardModule("Program", headers, "End Sub")));

    [TestMethod]
    public async Task ASubMainOnlyInADeadBranch_IsNot()
        => Assert.IsEmpty(await EntryPointsAsync(StandardModule("Program", "#If False Then", "Public Sub Main()", "End Sub", "#End If")));

    [TestMethod]
    public async Task ASubMainInADeadBranch_IsNot_WhileTheLiveOneElsewhereIs()
        => CollectionAssert.AreEqual(new[] { "Other" }, await EntryPointsAsync(
            StandardModule("Program", "#If False Then", "Public Sub Main()", "End Sub", "#End If"),
            modules: [("Other", StandardModule("Other", "Public Sub Main()", "End Sub"))]));

    [TestMethod]
    public async Task ASubMainWrittenWholeInEachBranch_IsTheLiveBranchs()
        => Assert.IsEmpty(await EntryPointsAsync(StandardModule("Program",
            "#If False Then", "Public Sub Main()", "End Sub", "#Else", "Public Sub Main(ByVal Count As Long)", "End Sub", "#End If")));

    // MS-VBAL §5.3.1.5: a ParamArray is declared "As Variant" or with no As clause. One of another type is no procedure the program can start with, nor one
    // any code can call: it is a compile error, as it is in MS-VBA.
    [TestMethod]
    [Ignore("the parser and the host accept a ParamArray declared As Long; MS-VBAL §5.3.1.5's grammar does not, and MS-VBA reports a compile error")]
    public async Task AParamArrayOfAnotherTypeThanVariant_DoesNotCompile()
        => Assert.IsNotEmpty(await LoadErrorsAsync([], StandardModule("Program", "Public Sub Main(ParamArray Args() As Long)", "End Sub")));
}
