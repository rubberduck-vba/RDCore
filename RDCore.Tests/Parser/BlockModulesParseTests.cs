using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK.Model;
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
            .Concat(parse.PrecompilerTrivia.Concat(parse.Trivia).SelectMany(trivia => (IEnumerable<SyntaxNode>)[trivia, .. trivia.Descendants()]))
            .ToList();
        Assert.HasCount(2, nodes.OfType<BlockBoundaryTriviaNode>());
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
    public void TheHeaderAndTheEndLineOfEachBlock_AreTheFilesTrivia_AsTheyAreWritten()
    {
        var parse = Parse(Module, "  Class Account ' the ledger", "  End Class", "MODULE Logger", "    Public Sub Log()", "    End Sub", "End   Module ' log");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        var boundaries = parse.Trivia.OfType<BlockBoundaryTriviaNode>().ToArray();
        var account = RDCoreDocument.BlockUri(Module, "Account").AbsolutePath;
        var logger = RDCoreDocument.BlockUri(Module, "Logger").AbsolutePath;
        CollectionAssert.AreEqual(new[]
            {
                (account, FileBlockKind.Class, false, "  Class Account ", new SourceRange(new(0, 0), new(0, 16))),
                (account, FileBlockKind.Class, true, "  End Class", new SourceRange(new(1, 0), new(1, 11))),
                (logger, FileBlockKind.Module, false, "MODULE Logger", new SourceRange(new(2, 0), new(2, 13))),
                (logger, FileBlockKind.Module, true, "End   Module ", new SourceRange(new(5, 0), new(5, 13))),
            },
            boundaries.Select(node => (node.Identity.DocumentUri, node.Kind, node.IsEnd, node.Source, node.SourceLocation.Range)).ToArray());
        Assert.IsTrue(boundaries.All(node => node.SourceLocation.Uri == Module), "located in the file");
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
    public void EachBlock_IsAsPartialAndCreatable_AndAsVisible_AsItsHeaderWrites()
    {
        var parse = Parse(Module,
            "Class Plain",
            "End Class",
            "Public Partial Creatable Class Account",
            "    Public Balance As Currency",
            "End Class",
            "Private Partial Module Helpers",
            "End Module",
            "partial module Logger",
            "End Module",
            "Public Class Shown",
            "End Class");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        CollectionAssert.AreEqual(new (string, bool, bool, AccessModifier?)[]
            {
                ("Plain", false, false, null),
                ("Account", true, true, AccessModifier.Public),
                ("Helpers", true, false, AccessModifier.Private),
                ("Logger", true, false, null),
                ("Shown", false, false, AccessModifier.Public),
            },
            parse.BlockModules.Select(block => (block.Name, block.IsPartial, block.IsCreatable, block.Visibility)).ToArray());
    }

    // where a header writes each of its words, in the file's positions: where a finding about the part is reported, and what a rename of the module edits.
    [TestMethod]
    public void EachWordOfABlocksHeader_IsWhereTheFileWritesIt()
    {
        var parse = Parse(Module,
            "Public Partial Creatable Class Account",
            "End Class",
            "partial module Logger",
            "End Module");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        static SourceRange?[] WordsOf(BlockModule block)
            => [block.VisibilityRange, block.PartialRange, block.CreatableRange, block.KindRange, block.NameRange];
        CollectionAssert.AreEqual(
            new SourceRange?[] { new(0, 0, 0, 6), new(0, 7, 0, 14), new(0, 15, 0, 24), new(0, 25, 0, 30), new(0, 31, 0, 38) },
            WordsOf(parse.BlockModules[0]));
        CollectionAssert.AreEqual(new SourceRange?[] { null, new(2, 0, 2, 7), null, new(2, 8, 2, 14), new(2, 15, 2, 21) }, WordsOf(parse.BlockModules[1]));
    }

    // the columns of an indented header, with a comment after it, are the file's: the name starts after the indent and ends before the comment.
    [TestMethod]
    public void AnIndentedHeader_WithACommentAfterIt_IsNamedWhereTheFileWritesTheName()
    {
        var parse = Parse(Module,
            "  Class Account ",
            "End Class",
            "Private Partial Module Helpers ' tools",
            "End Module");

        CollectionAssert.AreEqual(new SourceRange?[] { new SourceRange(new(0, 8), new(0, 15)), new SourceRange(new(2, 23), new(2, 30)) },
            parse.BlockModules.Select(block => block.NameRange).ToArray());
    }

    // a block's header states what these lines state in a module file: each is an error at its line, the attribute of a member's too.
    [TestMethod]
    public void AModuleFilesHeaderLine_InABlock_IsInvalidThere_AtItsLine()
    {
        var parse = Parse(Module,
            "Class Account",
            "VERSION 1.0 CLASS",
            "BEGIN",
            "  MultiUse = -1",
            "END",
            "Attribute VB_Name = \"Account\"",
            "Attribute VB_PredeclaredId = True",
            "Public Sub Deposit()",
            "Attribute Deposit.VB_Description = \"Adds to the balance.\"",
            "End Sub",
            "End Class",
            "Module Helpers",
            "Option Private Module",
            "Option Explicit",
            "End Module");

        CollectionAssert.AreEqual(new[] { 1, 2, 5, 6, 8, 12 }, parse.SyntaxErrors.Select(error => error.Location.Range.Start.Line).ToArray(), Errors(parse));
        Assert.IsTrue(parse.SyntaxErrors.All(error => error.ErrorId == (int)VBCompileErrorId.InvalidInABlock), Errors(parse));
        Assert.IsTrue(parse.SyntaxErrors.All(error => error.Location.Uri == Module), "located in the file");
        CollectionAssert.AreEqual(new[] { "Deposit" }, Members(parse.BlockModules[0].SyntaxTree), "the block is read all the same");
    }

    [TestMethod]
    public void AModuleFilesHeaderLines_OutsideEveryBlock_AreTheFilesOwnModules()
    {
        var parse = Parse(Module,
            "Attribute VB_Name = \"Module1\"",
            "Option Private Module",
            "Module Helpers",
            "End Module");

        Assert.IsEmpty(parse.SyntaxErrors, Errors(parse));
        Assert.HasCount(1, parse.BlockModules);
    }

    // a Partial that is not written first, or after a visibility, makes the line no header: it stays in the file's own module, with its end.
    [TestMethod]
    [DataRow("Partial Public Class Account", "End Class", DisplayName = "Partial before the visibility")]
    [DataRow("Class Partial Account", "End Class", DisplayName = "Partial after the kind")]
    [DataRow("Partial Creatable Module Helpers", "End Module", DisplayName = "Creatable before Module")]
    public void APartialThatMakesNoHeader_DeclaresNoBlock(string header, string end)
    {
        var parse = Parse(Module, header, end);

        Assert.IsEmpty(parse.BlockModules);
        Assert.AreEqual(0, parse.SyntaxErrors.FirstOrDefault()?.Location.Range.Start.Line, Errors(parse));
        Assert.IsFalse(parse.SyntaxErrors.Any(error => error.ErrorId == (int)VBCompileErrorId.InvalidInABlock), Errors(parse));
    }

    [TestMethod]
    public void APartialHeader_ThatNoEndFollows_DeclaresNoBlock()
    {
        var parse = Parse(Module, "Partial Class Account", "    Public Sub Deposit()", "    End Sub");

        Assert.IsEmpty(parse.BlockModules);
        CollectionAssert.Contains(parse.SyntaxErrors.Select(error => error.Location.Range.Start.Line).ToArray(), 0, Errors(parse));
        CollectionAssert.AreEqual(new[] { "Deposit" }, Members(parse.SyntaxTree!), "the file's own module");
    }

    [TestMethod]
    public void AnAnchoredFile_AnchorsItsBlocks()
    {
        var parse = new ModuleParser().Parse(Module, "Module Logger\r\n    Public Sub Log()\r\n    End Sub\r\nEnd Module\r\n", new SourcePosition(10, 4));

        var logger = parse.BlockModules.Single();
        Assert.AreEqual(new SourceRange(new(10, 4), new(13, 10)), logger.Range);
        Assert.AreEqual(new SourceRange(new(10, 11), new(10, 17)), logger.NameRange);
        var log = logger.SyntaxTree.Children.OfType<MemberDeclarationNode>().Single();
        Assert.AreEqual(new SourcePosition(11, 4), log.SourceLocation.Range.Start);
    }
}
