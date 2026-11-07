using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.Symbols;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a completion list offers, read off the scope tree of a composed workspace: real source through <c>WorkspaceSymbolResolver</c>.
/// </summary>
[TestClass]
public sealed class CompletionCandidatesTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static Uri ModuleUri(string name) => new UriBuilder(Root) { Fragment = name }.Uri;

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Std(string name, string body)
        => (ModuleUri(name), ModuleType.StdModule, new ModuleParser().Parse(new Uri($"file:///c:/ws/{name}.bas"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    private static (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse) Class(string name, string body)
        => (ModuleUri(name), ModuleType.ClassModule, new ModuleParser().Parse(new Uri($"file:///c:/ws/{name}.cls"), $"Attribute VB_Name = \"{name}\"\r\n{body}"));

    [TestMethod]
    public void APositionInAProcedureABranchWritesWhole_IsInThatProcedure_NotInTheOneWhoseHeaderTheBlockSplits()
    {
        // line 1 is the first after the attribute: F's main header is on line 2, G is on lines 5-6, inside F's lines 2-10.
        var (tree, _) = Compose(Std("Main", string.Join("\r\n",
            "#If VBA7 Then", "Private Function F() As Long", "#Else", "Private Sub G()", "    Debug.Print 1", "End Sub", "Private Function F() As Long", "#End If",
            "    F = 1", "End Function", "")));

        Assert.AreEqual(new UriBuilder(ModuleUri("Main")) { Fragment = "Main.G" }.Uri.AbsoluteUri, CompletionCandidates.OriginAt(tree, ModuleUri("Main"), new SourcePosition(5, 6)).AbsoluteUri);
        Assert.AreEqual(new UriBuilder(ModuleUri("Main")) { Fragment = "Main.F" }.Uri.AbsoluteUri, CompletionCandidates.OriginAt(tree, ModuleUri("Main"), new SourcePosition(9, 6)).AbsoluteUri);
    }

    private static (ScopeTree Tree, ISymbolResolver Resolver) Compose(params (Uri Uri, ModuleType ModuleType, ModuleParseResult Parse)[] modules)
    {
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, modules, new IntrinsicSymbolResolver());
        return (composition.ScopeTree, composition.Resolver);
    }

    private const string Main = """
        Private Counter As Long
        Public Total As Double

        Public Sub Run(ByVal Amount As Long)
            Dim Widget As Widget
            Dim Counter As String

        End Sub
        """;

    private const string Helpers = """
        Public Type Point
            X As Long
            Y As Long
        End Type

        Public Function Twice(ByVal Value As Long) As Long
            Twice = Value * 2
        End Function

        Private Sub Secret()
        End Sub
        """;

    private const string WidgetClass = """
        Public Name As String

        Public Function Grow(ByVal By As Long) As Long
        End Function

        Private Sub Hidden()
        End Sub
        """;

    private static (ScopeTree Tree, ISymbolResolver Resolver) Workspace() => Compose(Std("Main", Main), Std("Helpers", Helpers), Class("Widget", WidgetClass));

    // line 7 of the module is the empty line in Run: the attribute line is line 0.
    private static Uri InRun(ScopeTree tree) => CompletionCandidates.OriginAt(tree, ModuleUri("Main"), new SourcePosition(7, 8));

    private static string[] Labels(IEnumerable<CompletionCandidate> candidates) => [.. candidates.Select(candidate => candidate.Label)];

    [TestMethod]
    public void TheOrigin_IsTheProcedureThePositionIsIn_OrElseTheModule()
    {
        var (tree, _) = Workspace();

        Assert.AreEqual(ModuleUri("Main.Run").AbsoluteUri, InRun(tree).AbsoluteUri);
        Assert.AreEqual(ModuleUri("Main").AbsoluteUri, CompletionCandidates.OriginAt(tree, ModuleUri("Main"), new SourcePosition(1, 0)).AbsoluteUri);
    }

    [TestMethod]
    public void TheNames_AreWhatTheProcedureTheModuleTheProjectAndTheLanguageDeclare()
    {
        var (tree, _) = Workspace();
        var labels = Labels(CompletionCandidates.Names(tree, InRun(tree)));

        CollectionAssert.IsSubsetOf(new[] { "Amount", "Widget", "Counter", "Total", "Run", "Twice", "Helpers", "MsgBox", "Len" }, labels);
        CollectionAssert.DoesNotContain(labels, "Secret", "a Private member of another module is not accessible");
        CollectionAssert.DoesNotContain(labels, "Point", "a user-defined type is no value");
        CollectionAssert.DoesNotContain(labels, "Dim", "a keyword is no name: which of them are offered is the rules' to say");
    }

    [TestMethod]
    public void ANameDeclaredNearer_ShadowsOneDeclaredFarther_AndIsOfferedOnce()
    {
        var (tree, _) = Workspace();
        var counter = CompletionCandidates.Names(tree, InRun(tree)).Single(candidate => candidate.Label == "Counter");

        Assert.AreEqual("Counter As String", counter.Detail, "the local, not the module's field");
        Assert.AreEqual(CompletionItemKind.Variable, counter.Kind);
    }

    [TestMethod]
    public void AProcedure_IsDescribedByItsSignature()
    {
        var (tree, _) = Workspace();
        var twice = CompletionCandidates.Names(tree, InRun(tree)).Single(candidate => candidate.Label == "Twice");

        Assert.AreEqual(CompletionItemKind.Function, twice.Kind);
        Assert.AreEqual("Function Twice(ByVal Value As Long) As Long", twice.Detail);
    }

    [TestMethod]
    public void TheTypes_AreTheIntrinsicOnes_AndWhatTheProjectAndTheLibrariesDeclare()
    {
        var (tree, _) = Workspace();
        var labels = Labels(CompletionCandidates.Types(tree, InRun(tree), creatable: false));

        CollectionAssert.IsSubsetOf(new[] { "Long", "String", "Variant", "Widget", "Point", "Collection", "VBA", "New" }, labels);
        CollectionAssert.DoesNotContain(labels, "Amount", "a local is no type");
        CollectionAssert.DoesNotContain(labels, "Twice");
        CollectionAssert.DoesNotContain(labels, "Helpers", "a procedural module is no type");
        CollectionAssert.DoesNotContain(labels, "Strings", "nor is a library's");
    }

    [TestMethod]
    public void New_IsOfferedTheClassesThatCanBeCreated()
    {
        var (tree, _) = Workspace();
        var labels = Labels(CompletionCandidates.Types(tree, InRun(tree), creatable: true));

        CollectionAssert.IsSubsetOf(new[] { "Widget", "Collection" }, labels);
        CollectionAssert.DoesNotContain(labels, "Point");
        CollectionAssert.DoesNotContain(labels, "Long");
    }

    [TestMethod]
    public void TheMembersOfAModule_AreItsAccessibleOnes()
    {
        var (tree, resolver) = Workspace();
        var helpers = resolver.ResolveValue("Helpers", ScopeKind.Global, InRun(tree)).Symbol!;

        var fromMain = Labels(CompletionCandidates.MembersOf(tree, helpers, InRun(tree)));
        CollectionAssert.Contains(fromMain, "Twice");
        CollectionAssert.DoesNotContain(fromMain, "Secret");

        var fromInside = Labels(CompletionCandidates.MembersOf(tree, helpers, ModuleUri("Helpers.Twice")));
        CollectionAssert.Contains(fromInside, "Secret", "a Private member is accessible in its own module");
    }

    [TestMethod]
    public void TheMembersOfAValue_AreTheMembersOfItsType()
    {
        var (tree, resolver) = Workspace();
        var widget = (ITypedSymbol)resolver.ResolveValue("Widget", ScopeKind.Global, InRun(tree)).Symbol!;

        var labels = Labels(CompletionCandidates.MembersOf(widget.ResolvedType));
        CollectionAssert.IsSubsetOf(new[] { "Name", "Grow" }, labels);
        CollectionAssert.DoesNotContain(labels, "Hidden");
    }

    [TestMethod]
    public void TheMembersOfTheStandardLibrary_AreItsModules_AndTheirMembers()
    {
        var (tree, resolver) = Workspace();
        var vba = resolver.ResolveValue("VBA", ScopeKind.Global, InRun(tree)).Symbol!;

        CollectionAssert.IsSubsetOf(new[] { "Strings", "Interaction", "Len", "MsgBox" }, Labels(CompletionCandidates.MembersOf(tree, vba, InRun(tree))));
        CollectionAssert.Contains(Labels(CompletionCandidates.TypesOf(tree, vba, creatable: true)), "Collection");
    }

    // a word matches a label it starts, or the label's humps, a part of the word to the start of each hump in turn: never letters spread anywhere in the label.
    [TestMethod]
    [DataRow("Print", "Pr", true, DisplayName = "a prefix")]
    [DataRow("Print", "pR", true, DisplayName = "a prefix in any case")]
    [DataRow("CLngPtr", "Pr", false, DisplayName = "letters inside the label")]
    [DataRow("DatePart", "Pr", false, DisplayName = "letters in two humps, the first not the label's")]
    [DataRow("NPer", "Pr", false, DisplayName = "letters after the first hump")]
    [DataRow("MultiPlayer", "MP", true, DisplayName = "the first letter of each hump")]
    [DataRow("MultiPlayer", "mp", true, DisplayName = "humps in any case")]
    [DataRow("MultiPlayer", "MuPl", true, DisplayName = "the first letters of each hump")]
    [DataRow("MultiPlayer", "MuuP", false, DisplayName = "a letter the hump does not start with")]
    [DataRow("MultiPlayer", "Player", false, DisplayName = "a hump that is not the first")]
    [DataRow("MultiPlayer", "MPx", false, DisplayName = "more than the humps have")]
    [DataRow("LoadPicture", "LoPi", true, DisplayName = "two letters of each hump")]
    [DataRow("LoadPicture", "LoadPc", false, DisplayName = "a whole hump, then a letter the next does not start with")]
    [DataRow("vbKeyEscape", "vbKEs", true, DisplayName = "a lowercase prefix is a hump")]
    [DataRow("vbKeyEscape", "vKE", true, DisplayName = "a part of the lowercase prefix")]
    [DataRow("vbKeyEscape", "KE", false, DisplayName = "the lowercase prefix left out")]
    [DataRow("vbKeyEscape", "vbE", false, DisplayName = "a hump skipped")]
    [DataRow("CLngPtr", "CLP", true, DisplayName = "a capital before a hump is a hump of its own")]
    [DataRow("CLngPtr", "CLnP", true, DisplayName = "a part of the hump after the capital")]
    [DataRow("CLngPtr", "LP", false, DisplayName = "the capital left out")]
    [DataRow("ADODBRecordset", "ADR", true, DisplayName = "a run of capitals is one hump")]
    [DataRow("ADODBRecordset", "AR", true, DisplayName = "a part of a run of capitals")]
    [DataRow("ADODBRecordset", "DR", false, DisplayName = "the run of capitals started inside")]
    [DataRow("IOError", "IE", true, DisplayName = "a run of two capitals")]
    [DataRow("IOError", "OE", false, DisplayName = "a run of two capitals started inside")]
    [DataRow("VBA", "VA", false, DisplayName = "capitals alone are one hump")]
    [DataRow("DDEInitiate", "DI", true, DisplayName = "a run of capitals ends before the capital of a word")]
    [DataRow("Win32Api", "W32A", true, DisplayName = "digits are a hump")]
    [DataRow("Win32Api", "W3A", true, DisplayName = "a part of the digits")]
    [DataRow("Win32Api", "WA", false, DisplayName = "the digits skipped")]
    [DataRow("Class_Initialize", "CI", true, DisplayName = "an underscore separates humps")]
    [DataRow("Class_Initialize", "C_I", true, DisplayName = "an underscore typed separates as well")]
    [DataRow("Class_Initialize", "Class_I", true, DisplayName = "a prefix with its underscore")]
    [DataRow("Class_Initialize", "CsI", false, DisplayName = "a letter not at the start of a hump")]
    public void AWord_MatchesALabelItStarts_OrTheLabelsHumps(string label, string word, bool matches)
    {
        var matching = CompletionCandidates.Matching([new CompletionCandidate(label, CompletionItemKind.Variable)], word);

        Assert.AreEqual(matches, matching.Count == 1);
    }

    [TestMethod]
    public void TheLabelsAWordStarts_ComeBeforeTheOnesItMatchesTheHumpsOf_WhateverTheirRank()
    {
        CompletionCandidate[] candidates = [new("MultiPlayer", CompletionItemKind.Variable, Rank: 0), new("MPEG", CompletionItemKind.Constant, Rank: 3)];

        CollectionAssert.AreEqual(new[] { "MPEG", "MultiPlayer" }, Labels(CompletionCandidates.Matching(candidates, "MP")));
    }

    [TestMethod]
    public void TheItems_RankWhatIsNearerFirst()
    {
        var (tree, _) = Workspace();
        var placement = new CompletionPlacement(CompletionProcedure.Sub, [], [], []);
        var items = CompletionRules.At(tree, InRun(tree), new CompletionSite(CompletionSiteKind.Name, IsStatementStart: true), placement, new CompletionLanguage())
            .Select(candidate => candidate.ToCompletionItem()).ToList();

        string SortOf(string label) => items.Single(item => item.Label == label).SortText!;
        Assert.IsTrue(string.CompareOrdinal(SortOf("Amount"), SortOf("Total")) < 0, "a parameter before a field of the module");
        Assert.IsTrue(string.CompareOrdinal(SortOf("Total"), SortOf("MsgBox")) < 0, "a field of the module before the standard library");
        Assert.IsTrue(string.CompareOrdinal(SortOf("MsgBox"), SortOf("Dim")) < 0, "the keywords last");
    }
}
