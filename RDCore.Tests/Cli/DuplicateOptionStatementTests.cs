using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics;
using RDCore.Runtime.LanguageCore.Static;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Cli;

/// <summary>
/// A module states each option directive at most once, whatever its value (MS-VBAL §5.2.1): a second statement of one is "Duplicate Option statement"
/// (VBC09387), as MS-VBA has it, and the module does not load.
/// </summary>
/// <remarks>
/// The tests that load a module run the rule as a program meets it, conditional compilation blocks included; the others ask the rule of a parsed module, for
/// where it reports the error.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.2.1 Option Directives")]
public sealed class DuplicateOptionStatementTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\nPublic Sub Main()\r\nDebug.Print \"ran\"\r\nEnd Sub\r\n";

    private static VBCompileErrorInfo[] Errors(string program)
    {
        var parsed = new ModuleParser().Parse(new Uri("file:///C:/Program.bas"), program);
        Assert.IsTrue(parsed.IsSuccess, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
        return [.. DeclarationStaticSemanticsEvaluator.CheckSyntax(parsed.SyntaxTree!, ConditionalCompilationBlocks.None)
            .Where(error => error.VBCompileErrorId == VBCompileErrorId.DuplicateOptionStatement)];
    }

    [TestMethod]
    public async Task AStandardModule_ThatStatesOptionExplicitTwice_DoesNotLoad()
    {
        var error = Assert.ContainsSingle(await ModuleWorkspace.LoadErrorsAsync([], Program("Option Explicit", "Option Explicit")));

        StringAssert.Contains(error, RDCore.Runtime.LanguageCore.Exceptions.VBCompileError_DuplicateOptionStatement);
    }

    [TestMethod]
    public async Task AClassModule_ThatStatesAnOptionTwice_DoesNotLoad()
    {
        var error = Assert.ContainsSingle(await ModuleWorkspace.LoadErrorsAsync(
            [("Box", ModuleWorkspace.ClassModule("Box", "Option Compare Text", "Option Explicit", "Option Compare Text", "Public Size As Long"))], Program()));

        StringAssert.Contains(error, RDCore.Runtime.LanguageCore.Exceptions.VBCompileError_DuplicateOptionStatement);
    }

    [TestMethod]
    public async Task EveryOption_StatedOnce_Loads()
        => CollectionAssert.AreEqual(new[] { "ran" }, await ModuleWorkspace.RunAsync([],
            Program("Option Explicit", "Option Base 1", "Option Compare Text", "Option Private Module")));

    [TestMethod]
    [DataRow("Option Explicit", "Option Explicit", DisplayName = "Option Explicit, twice")]
    [DataRow("Option Base 0", "Option Base 0", DisplayName = "Option Base, the same value")]
    [DataRow("Option Base 0", "Option Base 1", DisplayName = "Option Base, another value")]
    [DataRow("Option Compare Binary", "Option Compare Binary", DisplayName = "Option Compare, the same value")]
    [DataRow("Option Compare Text", "Option Compare Binary", DisplayName = "Option Compare, another value")]
    [DataRow("Option Compare Database", "Option Compare Text", DisplayName = "Option Compare Database, then another value")]
    [DataRow("Option Private Module", "Option Private Module", DisplayName = "Option Private Module, twice")]
    public void AnOptionStatedTwice_IsADuplicateOptionStatement_AtTheSecondStatement(string first, string second)
    {
        // line 0 is the attribute; another option between the two does not count as either.
        var error = Assert.ContainsSingle(Errors(Program(first, "Option Explicit" == first ? "Option Base 1" : "Option Explicit", second)));

        Assert.AreEqual(3, error.Location.Range.Start.Line);
    }

    // RD-VBAL §5.1: the parts of one name are one module, which states every part's options: an option that two parts state is stated twice. The block is
    // a part in a workspace that allows blocks in a module file.
    [TestMethod]
    public void AnOptionThatTwoPartsOfAModuleState_IsADuplicateOptionStatement_AtTheSecondPart()
    {
        var file = new Uri("file:///C:/Helpers.bas");
        var other = new Uri("file:///C:/More.bas");
        var parts = ModulePart.PartsOf(file, "Helpers", ModuleType.StdModule,
                FileLevelStatementsOn.Parser().Parse(file, "Attribute VB_Name = \"Helpers\"\r\nOption Explicit\r\nPublic Total As Long\r\n"))
            .Concat(ModulePart.PartsOf(other, "More", ModuleType.StdModule,
                FileLevelStatementsOn.Parser().Parse(other, "Attribute VB_Name = \"More\"\r\nPartial Module Helpers\r\nOption Explicit\r\nEnd Module\r\n")));
        var (modules, refused) = ModuleAssembly.Of(parts);
        Assert.IsEmpty(refused);
        var helpers = modules.Single(module => module.Name == "Helpers");

        var found = Assert.ContainsSingle(DeclarationStaticSemanticsEvaluator.CheckSyntax(helpers.Parse.SyntaxTree!, ConditionalCompilationBlocks.None)
            .Where(error => error.VBCompileErrorId == VBCompileErrorId.DuplicateOptionStatement));

        Assert.AreEqual(new SourcePosition(2, 0), found.Location.Range.Start);
        Assert.AreEqual(other, found.Location.Uri, "the second part's file");
    }

    [TestMethod]
    public void EachStatementAfterTheFirst_IsADuplicateOptionStatement()
    {
        var errors = Errors(Program("Option Base 1", "Option Base 1", "Public Total As Long", "Option Base 0"));

        CollectionAssert.AreEqual(new[] { 2, 4 }, errors.Select(error => error.Location.Range.Start.Line).ToArray());
    }

    [TestMethod]
    public async Task AnOptionStatedInEachBranchOfAConditionalCompilationBlock_IsStatedOnce_WhicheverBranchCompiles()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([], Program(
            "#If NothingDefinesThis Then", "Option Base 1", "#Else", "Option Base 0", "#End If", "Option Explicit")));

    // MS-VBAL §3.4: a branch that is not live is not part of the module, and neither is an option it states.
    // AnOptionStatedInABranch_AndOutsideTheBlock_IsStatedTwice is the twin of the first, with the branch live.
    [TestMethod]
    public async Task AnOptionStatedInADeadBranch_AndOutsideTheBlock_IsStatedOnce()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([], Program(
            "Option Explicit", "#If 0 Then", "Option Explicit", "#End If")));

    [TestMethod]
    public async Task AnOptionStatedTwiceInADeadBranch_IsNoStatementOfIt()
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([], Program(
            "#If 0 Then", "Option Base 1", "Option Base 1", "#End If", "Option Explicit")));

    [TestMethod]
    public async Task AnOptionStatedInABranch_AndOutsideTheBlock_IsStatedTwice()
    {
        var error = Assert.ContainsSingle(await ModuleWorkspace.LoadErrorsAsync([], Program(
            "Option Explicit", "#If 1 Then", "Option Explicit", "#End If")));

        StringAssert.Contains(error, RDCore.Runtime.LanguageCore.Exceptions.VBCompileError_DuplicateOptionStatement);
    }
}
