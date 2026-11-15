using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Server.Configuration;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;
using RDCore.Runtime.LanguageCore.Refactoring;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// Before each positional argument of a call, the name of the parameter it is given to, in brackets: <c>Add [amount:]1, [by:]2</c>. None for a named
/// argument, an argument named as its parameter, the indexes of an array, or a ParamArray's arguments after its first.
/// </summary>
[TestClass]
public sealed class InlayHintsTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static readonly SdkInlayHintOptions Arguments = new() { BlockEnds = false, NextVariables = false, ImplicitModifiers = false, ImplicitTypes = false };

    private const string Module1 = "Attribute VB_Name = \"Module1\"\r\n"
        + "Option Explicit\r\n"
        + "Public Sub Main()\r\n"
        + "    Dim amount As Long\r\n"
        + "    Dim values(3) As Long\r\n"
        + "    Add 1, 2\r\n"
        + "    Add amount, by:=2\r\n"
        + "    Debug.Print Twice(values(0))\r\n"
        + "    Sum 1, 2, 3\r\n"
        + "    MsgBox \"Hello\", vbOKOnly\r\n"
        + "    Call Add(amount + 1)\r\n"
        + "End Sub\r\n"
        + "Public Sub Add(ByVal amount As Long, Optional ByVal by As Long = 1)\r\n"
        + "End Sub\r\n"
        + "Public Function Twice(ByVal n As Long) As Long\r\n"
        + "End Function\r\n"
        + "Public Sub Sum(ParamArray values() As Variant)\r\n"
        + "End Sub\r\n";

    private static Uri Module => new UriBuilder(Root) { Fragment = "Module1" }.Uri;

    private static RenameContext Workspace()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Module1.bas"), Module1);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var libraries = new LibrarySymbolProvider(Root, ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols();
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Module, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver(), libraries: [.. libraries]);
        return new RenameContext(composition.Resolver, composition.ScopeTree, [new RenameModule(Module, parse.SyntaxTree!, Module1)]);
    }

    // each hint on a line, as label@character.
    private static string[] HintsOn(int line)
        => [.. InlayHints.In(Workspace(), Module, new SourceRange(0, 0, 20, 0), Arguments)
            .Where(hint => hint.Position.Line == line)
            .Select(hint => $"{hint.Label}@{hint.Position.Character}")];

    [TestMethod]
    public void EachPositionalArgument_IsHintedWithItsParameter_InBrackets_BeforeIt()
        => CollectionAssert.AreEqual(new[] { "[amount:]@8", "[by:]@11" }, HintsOn(5), "Add 1, 2");

    [TestMethod]
    public void ANamedArgument_AndAnArgumentNamedAsItsParameter_HaveNoHint()
        => Assert.IsEmpty(HintsOn(6), "Add amount, by:=2");

    [TestMethod]
    public void TheArgumentsOfAnArray_HaveNoHint_ButTheCallAroundThemHas()
        => CollectionAssert.AreEqual(new[] { "[n:]@22" }, HintsOn(7), "Debug.Print Twice(values(0))");

    [TestMethod]
    public void AParamArray_IsHintedOnItsFirstArgument()
        => CollectionAssert.AreEqual(new[] { "[values:]@8" }, HintsOn(8), "Sum 1, 2, 3");

    [TestMethod]
    public void AProcedureOfTheLibrary_IsHintedWithItsParameters()
        => CollectionAssert.AreEqual(new[] { "[Prompt:]@11", "[Buttons:]@20" }, HintsOn(9), "MsgBox \"Hello\", vbOKOnly");

    [TestMethod]
    public void AnArgumentOfACallStatement_InParentheses_IsHintedInsideThem()
        => CollectionAssert.AreEqual(new[] { "[amount:]@13" }, HintsOn(10), "Call Add([amount:]amount + 1)");

    [TestMethod]
    public void AnArgumentHint_IsOfNoPadding_AndIsAParameterHint()
    {
        var hint = InlayHints.In(Workspace(), Module, new SourceRange(5, 0, 5, 20), Arguments)[0];

        Assert.IsFalse(hint.PaddingLeft || hint.PaddingRight, "[amount:]1 reads as one");
        Assert.AreEqual(OmniSharp.Extensions.LanguageServer.Protocol.Models.InlayHintKind.Parameter, hint.ToInlayHint(new(5, 8)).Kind);
    }

    [TestMethod]
    public void OnlyTheCallsInTheRange_AreHinted()
        => Assert.IsTrue(InlayHints.In(Workspace(), Module, new SourceRange(5, 0, 5, 20), Arguments).All(hint => hint.Position.Line == 5));

    [TestMethod]
    public void NoArgumentIsHinted_WhenArgumentsAreTurnedOff()
        => Assert.IsFalse(InlayHints.In(Workspace(), Module, new SourceRange(0, 0, 20, 0), new SdkInlayHintOptions { Arguments = false })
            .Any(hint => hint.Site == RDCore.SDK.Semantics.Refactoring.InlayHintSite.Arguments));
}
