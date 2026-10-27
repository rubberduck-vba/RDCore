using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// Before each positional argument of a call, the name of the parameter it is given to; none for a named argument, an argument named as its parameter, an
/// argument list that is no call's, or a ParamArray's arguments after its first.
/// </summary>
[TestClass]
public sealed class ParameterHintsTests
{
    private static readonly Uri Root = new("file://rdcore-test");

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
        + "End Sub\r\n"
        + "Public Sub Add(ByVal amount As Long, Optional ByVal by As Long = 1)\r\n"
        + "End Sub\r\n"
        + "Public Function Twice(ByVal n As Long) As Long\r\n"
        + "End Function\r\n"
        + "Public Sub Sum(ParamArray values() As Variant)\r\n"
        + "End Sub\r\n";

    private static RenameContext Workspace()
    {
        var uri = new UriBuilder(Root) { Fragment = "Module1" }.Uri;
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Module1.bas"), Module1);
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));

        var libraries = new LibrarySymbolProvider(Root, ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StdLibSymbolProvider.LibraryName])).ProvideSymbols();
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(uri, ModuleType.StdModule, parse)], new IntrinsicSymbolResolver(), libraries: [.. libraries]);
        return new RenameContext(composition.Resolver, composition.ScopeTree, [new RenameModule(uri, parse.SyntaxTree!, Module1)]);
    }

    // each hint on a line, as name@character.
    private static string[] HintsOn(int line)
        => [.. ParameterHints.In(Workspace(), new UriBuilder(Root) { Fragment = "Module1" }.Uri, new SourceRange(0, 0, 20, 0))
            .Where(hint => hint.Position.Line == line)
            .Select(hint => $"{hint.Name}@{hint.Position.Character}")];

    [TestMethod]
    public void EachPositionalArgument_IsHintedWithItsParameter()
        => CollectionAssert.AreEqual(new[] { "amount@8", "by@11" }, HintsOn(5), "Add 1, 2");

    [TestMethod]
    public void ANamedArgument_AndAnArgumentNamedAsItsParameter_HaveNoHint()
        => Assert.IsEmpty(HintsOn(6), "Add amount, by:=2");

    [TestMethod]
    public void TheArgumentsOfAnArray_HaveNoHint_ButTheCallAroundThemHas()
        => CollectionAssert.AreEqual(new[] { "n@22" }, HintsOn(7), "Debug.Print Twice(values(0))");

    [TestMethod]
    public void AParamArray_IsHintedOnItsFirstArgument()
        => CollectionAssert.AreEqual(new[] { "values@8" }, HintsOn(8), "Sum 1, 2, 3");

    [TestMethod]
    public void AProcedureOfTheLibrary_IsHintedWithItsParameters()
        => CollectionAssert.AreEqual(new[] { "Prompt@11", "Buttons@20" }, HintsOn(9), "MsgBox \"Hello\", vbOKOnly");

    [TestMethod]
    public void OnlyTheCallsInTheRange_AreHinted()
        => Assert.IsTrue(ParameterHints.In(Workspace(), new UriBuilder(Root) { Fragment = "Module1" }.Uri, new SourceRange(5, 0, 5, 20))
            .All(hint => hint.Position.Line == 5));
}
