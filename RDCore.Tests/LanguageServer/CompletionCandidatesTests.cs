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
    [DataRow("Class_Initialize", "Cl_ass", false, DisplayName = "an underscore typed ends a part: what follows it starts the next hump")]
    [DataRow("ClassModule", "Cla_ss", false, DisplayName = "an underscore typed ends a part, in a label with none")]
    [DataRow("Class_Initialize", "C_", true, DisplayName = "a part ended by an underscore, and nothing after it")]
    [DataRow("Class_Initialize", "C__I", true, DisplayName = "underscores typed in a row are one separator")]
    [DataRow("cmd_Click", "c_C", true, DisplayName = "a lowercase hump, an underscore, a capital")]
    [DataRow("xyz", "_x", false, DisplayName = "a word that starts with an underscore, a label that does not")]
    [DataRow("_NewEnum", "_NEn", true, DisplayName = "a word that starts with an underscore, a label that does")]
    [DataRow("_NewEnum", "NE", true, DisplayName = "a label's leading underscore is of no hump")]
    [DataRow("_private", "__", true, DisplayName = "underscores alone, a label that starts with one")]
    [DataRow("Class_Initialize", "__", false, DisplayName = "underscores alone, a label that does not start with one")]
    [DataRow("[My Field]", "My", true, DisplayName = "a name in brackets, the start of the name")]
    [DataRow("[My Field]", "[My F", true, DisplayName = "a name in brackets, its opening bracket and a space typed")]
    [DataRow("[My Field]", "MF", true, DisplayName = "a space separates humps")]
    [DataRow("[my field]", "mf", true, DisplayName = "a space separates lowercase humps")]
    [DataRow("[My Field]", "Field", false, DisplayName = "a name in brackets, a hump that is not the first")]
    [DataRow("Total", "[To", true, DisplayName = "an identifier, typed from an opening bracket")]
    [DataRow("Total", "[", true, DisplayName = "an opening bracket alone")]
    public void AWord_MatchesALabelItStarts_OrTheLabelsHumps(string label, string word, bool matches)
        => Assert.AreEqual(matches, CompletionCandidates.MatchOf(label, word) != CompletionMatch.None);

    // a client narrows the list of a word by the same rule as the word grows: what a longer word matches, the word matches too.
    [TestMethod]
    public void EveryLabelALongerWordMatches_TheWordMatchesToo()
    {
        string[] labels = ["Class_Initialize", "ClassModule", "cmd_Click", "_NewEnum", "xyz", "_xyz", "__init", "MultiPlayer", "vbKeyEscape", "CLngPtr", "Win32Api", "IOError", "x_y_z", "[My Field]", "Total"];
        string[] words = ["C_I", "Cl_ass", "c_C", "_NEn", "_x", "__x", "__N", "___i", "MuPl", "vbKEs", "CLnP", "W32A", "IOE", "x_y_z", "C__I", "x__z", "[My Fi", "[MF", "[To"];
        foreach (var word in words)
        {
            for (var length = 1; length < word.Length; length++)
            {
                foreach (var label in labels.Where(label => CompletionCandidates.MatchOf(label, word[..(length + 1)]) != CompletionMatch.None))
                {
                    Assert.AreNotEqual(CompletionMatch.None, CompletionCandidates.MatchOf(label, word[..length]), $"{label}: '{word[..(length + 1)]}' but not '{word[..length]}'");
                }
            }
        }
    }

    [TestMethod]
    public void TheLabelsAWordStarts_ComeBeforeTheOnesItMatchesTheHumpsOf_WhateverTheirRank()
    {
        CompletionCandidate[] candidates = [new("MultiPlayer", CompletionItemKind.Variable, Rank: 0), new("MPEG", CompletionItemKind.Constant, Rank: 3)];

        CollectionAssert.AreEqual(new[] { "MPEG", "MultiPlayer" }, CompletionCandidates.ListOf("MP", [], candidates).Items.Select(item => item.Label).ToArray());
    }

    [TestMethod]
    public void AnItemThatDoesNotMatch_RanksAfterTheOnesThatDo()
    {
        var candidate = new CompletionCandidate("Total", CompletionItemKind.Variable);

        Assert.IsTrue(string.CompareOrdinal(candidate.ToCompletionItem(CompletionMatch.Humps).SortText, candidate.ToCompletionItem(CompletionMatch.None).SortText) < 0);
        Assert.IsTrue(string.CompareOrdinal(candidate.ToCompletionItem(CompletionMatch.Prefix).SortText, candidate.ToCompletionItem(CompletionMatch.Humps).SortText) < 0);
    }

    // the candidates most likely to be written are a tier of their own: the word's best match among them is preselected, though another name it starts is not.
    [TestMethod]
    public void TheFirstCandidates_RankBeforeTheOthers_ThoughOnlyTheirHumpsMatch_AndTheBestOfThemIsPreselected()
    {
        CompletionCandidate[] constants = [new("DarkRed", CompletionItemKind.EnumMember), new("DeepRed", CompletionItemKind.EnumMember), new("DRed", CompletionItemKind.EnumMember)];
        var items = CompletionCandidates.ListOf("DR", [new(constants)], [new("Drum", CompletionItemKind.Function), new("DarkRed", CompletionItemKind.Variable)]).Items.ToList();

        CollectionAssert.AreEqual(new[] { "DRed", "DarkRed", "DeepRed", "Drum" }, items.Select(item => item.Label).ToArray(),
            string.Join(", ", items.Select(item => item.Label + "/" + item.SortText)));
        Assert.IsTrue(items[0].Preselect);
        Assert.AreEqual(1, items.Count(item => item.Preselect));
    }

    // each group is a tier, in its order; in each tier the labels the word starts come before the ones whose humps it matches.
    [TestMethod]
    public void EachGroupIsATier_InWhichWhatTheWordStartsComesFirst()
    {
        CompletionCandidate[] constants = [new("SmallCaps", CompletionItemKind.EnumMember), new("ScSmall", CompletionItemKind.EnumMember)];
        CompletionCandidate[] names = [new("SubCaption:=", CompletionItemKind.Variable), new("Sc:=", CompletionItemKind.Variable)];
        CompletionCandidate[] others = [new("StyleCount", CompletionItemKind.Function), new("Scale", CompletionItemKind.Function)];

        var items = CompletionCandidates.ListOf("Sc", [new(constants), new(names, IsSelectedWhenNothingIsTyped: false)], others).Items.ToList();

        CollectionAssert.AreEqual(new[] { "ScSmall", "SmallCaps", "Sc:=", "SubCaption:=", "Scale", "StyleCount" }, items.Select(item => item.Label).ToArray(),
            string.Join(", ", items.Select(item => item.Label + "/" + item.SortText)));
        Assert.AreEqual("ScSmall", items.Single(item => item.Preselect).Label);
    }

    // a label the word is, in any case, is what the user means to write: it is selected, wherever it ranks, and the tiers keep their order.
    [TestMethod]
    public void ALabelTheWordIs_IsSelected_ThoughTheTiersBeforeItKeepTheirOrder()
    {
        CompletionCandidate[] constants = [new("ScSmall", CompletionItemKind.EnumMember)];
        CompletionCandidate[] names = [new("SubCaption:=", CompletionItemKind.Variable), new("Sc:=", CompletionItemKind.Variable)];
        CompletionCandidate[] others = [new("Scale", CompletionItemKind.Function), new("sc", CompletionItemKind.Variable)];

        var items = CompletionCandidates.ListOf("SC", [new(constants), new(names, IsSelectedWhenNothingIsTyped: false)], others).Items.ToList();

        CollectionAssert.AreEqual(new[] { "ScSmall", "Sc:=", "SubCaption:=", "sc", "Scale" }, items.Select(item => item.Label).ToArray(),
            string.Join(", ", items.Select(item => item.Label + "/" + item.SortText)));
        Assert.AreEqual("sc", items.Single(item => item.Preselect).Label);
    }

    // a group as large as a workspace's names keeps its order past the thousandth.
    [TestMethod]
    public void ALargeGroup_KeepsItsOrder()
    {
        CompletionCandidate[] group = [.. Enumerable.Range(0, 1_500).Select(index => new CompletionCandidate($"Item{1_500 - index}", CompletionItemKind.Variable))];

        var items = CompletionCandidates.ListOf(string.Empty, [new(group)], []).Items.ToList();

        CollectionAssert.AreEqual(group.Select(candidate => candidate.Label).ToArray(), items.Select(item => item.Label).ToArray());
    }

    // where nothing is typed, a group that is not selected then is skipped: the first item that can be selected is.
    [TestMethod]
    [DataRow("", "Scale", DisplayName = "nothing typed")]
    [DataRow("S", "SubTitle:=", DisplayName = "a word typed")]
    public void AGroupNotSelectedWhereNothingIsTyped_IsSelectedOnceAWordIs(string word, string selected)
    {
        CompletionCandidate[] names = [new("SubTitle:=", CompletionItemKind.Variable)];
        CompletionCandidate[] others = [new("Scale", CompletionItemKind.Function)];

        var items = CompletionCandidates.ListOf(word, [new([]), new(names, IsSelectedWhenNothingIsTyped: false)], others).Items.ToList();

        Assert.AreEqual("SubTitle:=", items[0].Label);
        Assert.AreEqual(selected, items.Single(item => item.Preselect).Label);
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
