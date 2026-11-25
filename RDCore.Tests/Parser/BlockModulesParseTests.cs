using RDCore.Parsing;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Parser;

/// <summary>
/// A file's <c>Class Name ... End Class</c> and <c>Module Name ... End Module</c> blocks are modules of their own
/// (<see cref="ModuleParseResult.BlockModules"/>): each is read as a module is, its nodes keyed by its own address (<see cref="RDCoreDocument.BlockUri"/>)
/// and located where they are written in the file. The file's own module is what is outside every block.
/// </summary>
[TestClass]
public sealed class BlockModulesParseTests
{
    private static readonly Uri Script = new("file:///c:/ws/Backup.vbs");
    private static readonly Uri Module = new("file:///c:/ws/Module1.bas");

    private static ModuleParseResult Parse(Uri uri, params string[] lines) => new ModuleParser().Parse(uri, string.Join("\r\n", lines) + "\r\n");

    private static string Errors(ModuleParseResult parse)
        => string.Join(" | ", parse.SyntaxErrors.Select(error => $"{error.ErrorId} at {error.Location.Range.Start.Line}: {error.Verbose}"));

    private static string[] Members(ModuleNode module) => [.. module.Children.OfType<MemberDeclarationNode>().Select(member => member.Name)];

    private static IEnumerable<SyntaxNode> NodesOf(ModuleNode module) => [module, .. module.Descendants()];

    [TestMethod]
    public void EachBlock_IsAModuleOfItsOwn_AndTheFilesModuleIsWhatIsOutsideThem()
    {
        var parse = Parse(Module,
            "Public Sub Own()",
            "End Sub",
            "Class Account",
            "    Public Sub Deposit()",
            "    End Sub",
            "End Class",
            "Module Logger",
            "    Public Sub Log()",
            "    End Sub",
            "End Module");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        CollectionAssert.AreEqual(new[] { "Own" }, Members(parse.SyntaxTree!));
        Assert.HasCount(2, parse.BlockModules);

        var account = parse.BlockModules[0];
        Assert.AreEqual("Account", account.Name);
        Assert.AreEqual(ModuleType.ClassModule, account.Kind);
        Assert.AreEqual(RDCoreDocument.BlockUri(Module, "Account"), account.Uri);
        CollectionAssert.AreEqual(new[] { "Deposit" }, Members(account.SyntaxTree));
        Assert.AreEqual(new SourceRange(new(2, 0), new(5, 9)), account.Range, "from its header to its end");

        var logger = parse.BlockModules[1];
        Assert.AreEqual("Logger", logger.Name);
        Assert.AreEqual(ModuleType.StdModule, logger.Kind);
        CollectionAssert.AreEqual(new[] { "Log" }, Members(logger.SyntaxTree));
    }

    [TestMethod]
    public void ABlocksNodes_AreKeyedByItsAddress_AndLocatedWhereTheyAreWrittenInTheFile()
    {
        var parse = Parse(Module,
            "Public Sub Own()",
            "End Sub",
            "Module Logger",
            "    Public Sub Log()",
            "    End Sub",
            "End Module");

        var logger = parse.BlockModules.Single();
        Assert.IsTrue(NodesOf(logger.SyntaxTree).All(node => node.Identity.DocumentUri == logger.Uri.AbsolutePath), "keyed by the block's address");
        Assert.IsTrue(NodesOf(logger.SyntaxTree).All(node => node.SourceLocation.Uri == Module), "located in the file");
        Assert.IsTrue(NodesOf(parse.SyntaxTree!).All(node => node.Identity.DocumentUri == Module.AbsolutePath), "the file's own, keyed by the file");

        var log = logger.SyntaxTree.Children.OfType<MemberDeclarationNode>().Single();
        Assert.AreEqual(new SourcePosition(3, 4), log.SourceLocation.Range.Start, "the line it is on in the file");
        Assert.AreEqual(4, log.SourceLocation.Range.End.Line);
    }

    [TestMethod]
    public void NoTwoNodesOfAFile_ShareAnIdentity_DirectivesIncluded()
    {
        var parse = Parse(Module,
            "#Const Tracing = 1",
            "Public Sub Own()",
            "End Sub",
            "Module Logger",
            "    #Const Tracing = 1",
            "    Public Sub Log()",
            "    End Sub",
            "End Module");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        var nodes = NodesOf(parse.SyntaxTree!).Concat(parse.BlockModules.SelectMany(block => NodesOf(block.SyntaxTree)))
            .Concat(parse.PrecompilerTrivia.SelectMany(directive => (IEnumerable<SyntaxNode>)[directive, .. directive.Descendants()]))
            .ToList();
        Assert.HasCount(2, nodes.OfType<PrecompilerConstantDeclarationNode>());
        var shared = nodes.GroupBy(node => node.Identity).Where(group => group.Count() > 1).Select(group => group.Key.ToString()).ToList();
        Assert.IsEmpty(shared, string.Join(", ", shared));
    }

    [TestMethod]
    public void ABlockOfAScript_IsNoScript_ItReportsWhatTheSameLinesReportAsAModule()
    {
        string[] block = ["    count = 1", "    Public Sub Log()", "    End Sub"];
        var parse = Parse(Script, ["Log", "Module Logger", .. block, "End Module"]);
        var asAModule = Parse(Module, block);

        CollectionAssert.Contains(Members(parse.SyntaxTree!), ProgramEntryPoint.ScriptName, "the file is a script");
        var logger = parse.BlockModules.Single();
        CollectionAssert.DoesNotContain(Members(logger.SyntaxTree), ProgramEntryPoint.ScriptName, "its block is a module");
        Assert.IsNotEmpty(asAModule.SyntaxErrors, "a module refuses a statement outside its procedures");
        CollectionAssert.AreEqual(asAModule.SyntaxErrors.Select(error => (error.ErrorId, error.Location.Range.Start.Line + 2, error.Verbose)).ToArray(),
            parse.SyntaxErrors.Select(error => (error.ErrorId, error.Location.Range.Start.Line, error.Verbose)).ToArray(), Errors(parse));
        Assert.IsTrue(parse.SyntaxErrors.All(error => error.Location.Uri == Script), "the file's");
    }

    [TestMethod]
    public void ASyntaxErrorInABlock_IsTheFilesAtItsLine()
    {
        var parse = Parse(Module,
            "Class Account",
            "    Public Sub Deposit()",
            "        x = = 1",
            "    End Sub",
            "End Class");

        var error = parse.SyntaxErrors.Single();
        Assert.AreEqual(2, error.Location.Range.Start.Line, Errors(parse));
        Assert.AreEqual(Module, error.Location.Uri);
    }

    [TestMethod]
    public void AnEmptyBlock_IsAnEmptyModule()
    {
        var parse = Parse(Module, "Class Account", "End Class");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        var account = parse.BlockModules.Single();
        Assert.IsEmpty(account.SyntaxTree.Children);
        Assert.AreEqual(RDCoreDocument.BlockUri(Module, "Account").AbsolutePath, account.SyntaxTree.Identity.DocumentUri);
    }

    [TestMethod]
    public void ACommentOnAHeaderOrAnEndLine_IsTheFilesTrivia_WhereItIsWritten()
    {
        var parse = Parse(Module, "Class Account ' the ledger", "    Public Sub Deposit()", "    End Sub", "End Class   ' of the ledger");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        var comments = parse.Trivia.OfType<CommentTriviaNode>().ToList();
        CollectionAssert.AreEqual(new[] { new SourcePosition(0, 14), new SourcePosition(3, 12) },
            comments.Select(comment => comment.SourceLocation.Range.Start).ToArray());
        CollectionAssert.AreEqual(new[] { "Deposit" }, Members(parse.BlockModules.Single().SyntaxTree));
        Assert.IsEmpty(parse.SyntaxTree!.Children, "the comments are no code");
    }

    [TestMethod]
    public void TwoBlocksOfOneName_AreBothRead()
    {
        var parse = Parse(Module, "Module Logger", "    Public Sub One()", "    End Sub", "End Module", "Module Logger", "    Public Sub Two()", "    End Sub",
            "End Module");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        CollectionAssert.AreEqual(new[] { "One", "Two" }, parse.BlockModules.SelectMany(block => Members(block.SyntaxTree)).ToArray());
        Assert.AreEqual(parse.BlockModules[0].Uri, parse.BlockModules[1].Uri, "one name, one address");
    }

    [TestMethod]
    public void AFileWithoutBlocks_HasNone()
    {
        var parse = Parse(Module, "Public Sub Own()", "End Sub");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        Assert.IsEmpty(parse.BlockModules);
        CollectionAssert.AreEqual(new[] { "Own" }, Members(parse.SyntaxTree!));
    }

    [TestMethod]
    public void AnAnchoredFile_AnchorsItsBlocks()
    {
        var parse = new ModuleParser().Parse(Module, "Module Logger\r\n    Public Sub Log()\r\n    End Sub\r\nEnd Module\r\n", new SourcePosition(10, 4));

        var logger = parse.BlockModules.Single();
        Assert.AreEqual(new SourceRange(new(10, 4), new(13, 10)), logger.Range);
        var log = logger.SyntaxTree.Children.OfType<MemberDeclarationNode>().Single();
        Assert.AreEqual(new SourcePosition(11, 4), log.SourceLocation.Range.Start);
    }
}
