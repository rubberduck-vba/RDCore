using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.Symbols;

namespace RDCore.Tests.Symbols;

/// <summary>
/// The modules a document is the text of, as the language server and the CLI define them: the file's own, then each block's, each with its own part of the
/// parse, named and addressed by the name its text gives it.
/// </summary>
[TestClass]
public sealed class ParsedModulesTests
{
    private static readonly Uri Root = new("file:///c:/rdcore-parsed-modules/");
    private static readonly Uri File = new("file:///c:/rdcore-parsed-modules/Backup.bas");

    private const string Blocks = "Attribute VB_Name = \"Backup\"\r\nPublic Sub Run()\r\nEnd Sub\r\n"
        + "Class Account\r\nPublic Sub Deposit()\r\nEnd Sub\r\nEnd Class\r\n"
        + "Module Helpers\r\nPublic Function Twice()\r\nEnd Function\r\nEnd Module\r\n";

    // a module file, in a workspace that allows its blocks.
    private static ModuleParseResult Parse(string source) => FileLevelStatementsOn.Parser().Parse(File, source);

    private static string[] Members(ParsedModule module)
        => [.. module.Parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().Select(member => member.Name)];

    [TestMethod]
    public void AFileOfBlocks_IsItsOwnModuleThenEachBlocks_EachWithItsOwnPartOfTheParse()
    {
        var modules = ParsedModules.Of(Root, File, Parse(Blocks), "File", ModuleType.StdModule);

        CollectionAssert.AreEqual(new[] { "Backup", "Account", "Helpers" }, modules.Select(module => module.Name).ToArray());
        CollectionAssert.AreEqual(new[] { ModuleType.StdModule, ModuleType.ClassModule, ModuleType.StdModule },
            modules.Select(module => module.Kind).ToArray());
        CollectionAssert.AreEqual(modules.Select(module => SymbolUri.Module(Root, module.Name)).ToArray(), modules.Select(module => module.Uri).ToArray());
        CollectionAssert.AreEqual(new[] { File, File, File }, modules.Select(module => module.Document).ToArray());
        CollectionAssert.AreEqual(new SourceRange?[] { null, new SourceRange(3, 0, 6, 9), new SourceRange(7, 0, 10, 10) },
            modules.Select(module => module.Range).ToArray());
        CollectionAssert.AreEqual(new[] { "Run" }, Members(modules[0]));
        CollectionAssert.AreEqual(new[] { "Deposit" }, Members(modules[1]));
        CollectionAssert.AreEqual(new[] { "Twice" }, Members(modules[2]));
    }

    // the file's own module is named by its attribute, or else by its file, and is of the kind its file says; a file with no block is that module alone.
    [TestMethod]
    [DataRow("Attribute VB_Name = \"Backup\"\r\nPublic Sub Run()\r\nEnd Sub\r\n", "Backup", DisplayName = "an attribute names it")]
    [DataRow("Public Sub Run()\r\nEnd Sub\r\n", "File", DisplayName = "no attribute: its file names it")]
    public void AFileOfNoBlock_IsOneModule_NamedByItsTextOrElseByItsFile(string source, string name)
    {
        var parse = Parse(source);

        var module = ParsedModules.Of(Root, File, parse, "File", ModuleType.ClassModule).Single();

        Assert.AreEqual(new ParsedModule(SymbolUri.Module(Root, name), name, ModuleType.ClassModule, parse) { Document = File }, module);
    }

    [TestMethod]
    public void TheNamesOfADocument_AreItsOwnModulesThenItsBlocks_AndItsFilesWhenItHasNoParse()
    {
        CollectionAssert.AreEqual(new[] { "Backup", "Account", "Helpers" }, ParsedModules.NamesOf(Parse(Blocks), "File").ToArray());
        CollectionAssert.AreEqual(new[] { "File" }, ParsedModules.NamesOf(Parse("Public Sub Run()\r\nEnd Sub\r\n"), "File").ToArray());
        CollectionAssert.AreEqual(new[] { "File" }, ParsedModules.NamesOf(null, "File").ToArray());
    }
}
