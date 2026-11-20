using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Cli;
using System.Globalization;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The declaration of a variable moved to the statement that first uses it: a Private variable of a module into the procedure that uses it, a variable of a
/// procedure down to its first use, and what is refused.
/// </summary>
[TestClass]
public sealed class CloserToUsageTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Module1 = new UriBuilder(Root) { Fragment = "Module1" }.Uri;

    private const string Header = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n";

    private sealed record class Workspace(RenameContext Context, RenameModule Source, ModuleParseResult Parse);

    private static Workspace Compose(string text, ModuleType type = ModuleType.StdModule)
    {
        var parse = new ModuleParser().Parse(Module1, text);
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [(Module1, type, parse)], new IntrinsicSymbolResolver());
        var source = new RenameModule(Module1, parse.SyntaxTree!, text);
        return new(new RenameContext(composition.Resolver, composition.ScopeTree, [source]), source, parse);
    }

    // the variable named at the first place `marker` is written.
    private static (CloserToUsageTarget? Target, CloserToUsageNotice? Refusal) Prepare(Workspace workspace, string marker)
        => CloserToUsage.Prepare(workspace.Context, workspace.Source, workspace.Parse, PositionOf(workspace.Source.Source, marker));

    private static SourcePosition PositionOf(string text, string marker)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        var before = text[..at];
        return new SourcePosition(before.Count(character => character == '\n'), at - (before.LastIndexOf('\n') + 1));
    }

    private static string Moved(string text, string marker, CloserToUsageKeyword keyword = CloserToUsageKeyword.Static, ModuleType type = ModuleType.StdModule)
    {
        var workspace = Compose(text, type);
        var (target, refusal) = Prepare(workspace, marker);
        Assert.IsNull(refusal, refusal?.ToMessage());
        var (edits, refused) = CloserToUsage.Plan(target!, keyword);
        Assert.IsNull(refused, refused?.ToMessage());
        return DeclarationEdits.ApplyTo(text, edits!.Single().Edits);
    }

    // a project of modules, by name: what they resolve in, and the parse of each.
    private sealed record class Project(RenameContext Context, IReadOnlyDictionary<string, (RenameModule Module, ModuleParseResult Parse)> Modules)
    {
        public ModuleParseResult? ParseOf(Uri module) => Modules.Values.FirstOrDefault(each => each.Module.Module.AbsoluteUri == module.AbsoluteUri).Parse;

        // the variable named at the first place `marker` is written in the named module.
        public (CloserToUsageTarget? Target, CloserToUsageNotice? Refusal) Prepare(string module, string marker)
        {
            var (source, parse) = Modules[module];
            return CloserToUsage.Prepare(Context, source, parse, PositionOf(source.Source, marker), ParseOf);
        }

        // the text of each module once the declaration named in a module is moved, by name.
        public IReadOnlyDictionary<string, string> Moved(string module, string marker, CloserToUsageKeyword keyword = CloserToUsageKeyword.Static)
        {
            var (target, refusal) = Prepare(module, marker);
            Assert.IsNull(refusal, refusal?.ToMessage());
            var (edits, refused) = CloserToUsage.Plan(target!, keyword);
            Assert.IsNull(refused, refused?.ToMessage());
            return Modules.ToDictionary(
                each => each.Key,
                each => edits!.FirstOrDefault(edit => edit.Module.AbsoluteUri == each.Value.Module.Module.AbsoluteUri) is { } edited
                    ? DeclarationEdits.ApplyTo(each.Value.Module.Source, edited.Edits)
                    : each.Value.Module.Source);
        }
    }

    private static Project ComposeAll(params (string Name, ModuleType Type, string Text)[] modules)
    {
        var parsed = modules.Select(module => (module.Name, Uri: new UriBuilder(Root) { Fragment = module.Name }.Uri, module.Type, module.Text)).ToList();
        var parses = parsed.ToDictionary(module => module.Name, module => new ModuleParser().Parse(module.Uri, module.Text));
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(Root, [.. parsed.Select(module => (module.Uri, module.Type, parses[module.Name]))],
            new IntrinsicSymbolResolver());
        var sources = parsed.ToDictionary(module => module.Name, module => new RenameModule(module.Uri, parses[module.Name].SyntaxTree!, module.Text));
        return new(new RenameContext(composition.Resolver, composition.ScopeTree, [.. sources.Values]),
            sources.ToDictionary(each => each.Key, each => (each.Value, parses[each.Key])));
    }

    private const string Counter = "Private count As Long\r\n\r\nPublic Sub Tick()\r\n    Debug.Print \"tick\"\r\n    count = count + 1\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow(nameof(CloserToUsageKeyword.Static), "Static", DisplayName = "Static, the default")]
    [DataRow(nameof(CloserToUsageKeyword.Dim), "Dim", DisplayName = "Dim")]
    public void AVariableOfTheModule_GoesIntoTheProcedureThatUsesIt_BeforeItsFirstUse(string keyword, string written)
    {
        var text = Moved(Header + Counter, "count", Enum.Parse<CloserToUsageKeyword>(keyword));

        Assert.AreEqual(Header + "\r\nPublic Sub Tick()\r\n    Debug.Print \"tick\"\r\n    " + written + " count As Long\r\n    count = count + 1\r\nEnd Sub\r\n", text);
    }

    [TestMethod]
    public void AVariableOfAStandardModule_KeepsItsValueAsStatic_AndNotAsDim()
    {
        var (target, _) = Prepare(Compose(Header + Counter), "count");

        Assert.IsNull(CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Static));
        Assert.AreEqual(CloserToUsageChangeKind.NotKept, CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Dim)?.Change);
    }

    // a Static local of a member of a class has the extent of the object the member is called on, as the variable of the class did (RD-VBAL §5.4.3.1).
    [TestMethod]
    public void AVariableOfAClassModule_KeepsItsValueAsStatic_AndNotAsDim()
    {
        var (target, _) = Prepare(Compose(Header + Counter, ModuleType.ClassModule), "count");

        Assert.IsNull(CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Static));
        Assert.AreEqual(CloserToUsageChangeKind.NotKept, CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Dim)?.Change);
    }

    // a class whose Tick counts the calls on each object, and a program that calls it on two objects: three times on one, once on the other.
    private static readonly string Ticker = ModuleWorkspace.ClassModule("Ticker", "Option Explicit", "Private count As Long", "",
        "Public Function Tick() As Long", "    count = count + 1", "    Tick = count", "End Function");

    private static readonly string TwoTickers = ModuleWorkspace.StandardModule("Program", "Public Sub Main()",
        "    Dim a As New Ticker", "    Dim b As New Ticker", "    a.Tick", "    a.Tick", "    Debug.Print a.Tick", "    Debug.Print b.Tick", "End Sub");

    [TestMethod]
    public async Task AVariableOfAClassModule_MovedAsStatic_KeepsAValueForEachObject()
    {
        var moved = Moved(Ticker, "count", CloserToUsageKeyword.Static, ModuleType.ClassModule);

        StringAssert.Contains(moved, "    Static count As Long\r\n    count = count + 1");
        var before = await ModuleWorkspace.RunAsync([("Ticker", Ticker)], TwoTickers);
        CollectionAssert.AreEqual(new[] { "3", "1" }, before, "each object counts its own calls");
        CollectionAssert.AreEqual(before, await ModuleWorkspace.RunAsync([("Ticker", moved)], TwoTickers), "and still does");
    }

    [TestMethod]
    public async Task AVariableOfAClassModule_MovedWithDim_IsInitializedAtEachCall_AsItsNoticeSays()
    {
        var (target, _) = Prepare(Compose(Ticker, ModuleType.ClassModule), "count");
        var moved = Moved(Ticker, "count", CloserToUsageKeyword.Dim, ModuleType.ClassModule);

        Assert.AreEqual(CloserToUsageChangeKind.NotKept, CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Dim)?.Change);
        CollectionAssert.AreEqual(new[] { "1", "1" }, await ModuleWorkspace.RunAsync([("Ticker", moved)], TwoTickers));
    }

    [TestMethod]
    public void AStaticProcedure_KeepsTheValueOfAVariableDeclaredWithDim()
    {
        var (target, _) = Prepare(Compose(Header + Counter.Replace("Public Sub", "Public Static Sub", StringComparison.Ordinal)), "count");

        Assert.IsNull(CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Dim));
    }

    [TestMethod]
    public void AVariableOfAProcedure_GoesIntoTheInnermostBlockThatHoldsEveryUse()
    {
        const string body = "Public Sub F()\r\n    Dim total As Long\r\n    Debug.Print 1\r\n    If True Then\r\n        Debug.Print 2\r\n        total = 1\r\n        Debug.Print total\r\n    End If\r\nEnd Sub\r\n";

        var text = Moved(Header + body, "total");

        Assert.AreEqual(Header + "Public Sub F()\r\n    Debug.Print 1\r\n    If True Then\r\n        Debug.Print 2\r\n        Dim total As Long\r\n        total = 1\r\n        Debug.Print total\r\n    End If\r\nEnd Sub\r\n", text);
    }

    [TestMethod]
    public void AUseInTheConditionOfABlock_KeepsTheDeclarationBeforeTheBlock()
    {
        const string body = "Public Sub F()\r\n    Dim total As Long\r\n    Debug.Print 1\r\n    If total = 0 Then\r\n        total = 1\r\n    End If\r\nEnd Sub\r\n";

        var text = Moved(Header + body, "total");

        Assert.AreEqual(Header + "Public Sub F()\r\n    Debug.Print 1\r\n    Dim total As Long\r\n    If total = 0 Then\r\n        total = 1\r\n    End If\r\nEnd Sub\r\n", text);
    }

    [TestMethod]
    public void AVariableOfAStatementOfSeveral_IsDeclaredOnALineOfItsOwn_WithItsKeyword()
    {
        const string body = "Public Sub F()\r\n    Static a As Long, b(1 To 3) As String\r\n    a = 1\r\n    Debug.Print a\r\n    b(1) = \"x\"\r\nEnd Sub\r\n";

        var text = Moved(Header + body, "b(1 To");

        Assert.AreEqual(Header + "Public Sub F()\r\n    Static a As Long\r\n    a = 1\r\n    Debug.Print a\r\n    Static b(1 To 3) As String\r\n    b(1) = \"x\"\r\nEnd Sub\r\n", text);
    }

    [TestMethod]
    [DataRow("Private count As Long\r\n\r\nPublic Sub A()\r\n    count = 1\r\nEnd Sub\r\n\r\nPublic Sub B()\r\n    count = 2\r\nEnd Sub\r\n", "count", nameof(CloserToUsageRefusalKind.SeveralProcedures), DisplayName = "used by two procedures")]
    [DataRow("Private count As Long\r\n\r\nPublic Sub Tick()\r\nEnd Sub\r\n", "count", nameof(CloserToUsageRefusalKind.NotUsed), DisplayName = "used by nothing")]
    [DataRow("Public Sub F()\r\n    Dim a As Long\r\n    Dim b As Long\r\n    a = 1\r\nEnd Sub\r\n", "a As", nameof(CloserToUsageRefusalKind.AlreadyClose), DisplayName = "only declarations before its first use")]
    [DataRow("Private Sub Tick()\r\nEnd Sub\r\n", "Tick", nameof(CloserToUsageRefusalKind.NotAVariable), DisplayName = "a procedure")]
    [DataRow("Private count As Long: Private other As Long\r\n\r\nPublic Sub Tick()\r\n    count = 1\r\n    other = 1\r\nEnd Sub\r\n", "count", nameof(CloserToUsageRefusalKind.SharesItsLine), DisplayName = "a statement that shares its line")]
    public void ADeclarationThatCannotMove_IsRefused(string body, string marker, string kind)
    {
        var workspace = Compose(Header + body);
        var (target, refusal) = Prepare(workspace, marker);
        if (target is not null)
        {
            (_, refusal) = CloserToUsage.Plan(target, CloserToUsageKeyword.Static);
        }

        Assert.AreEqual(Enum.Parse<CloserToUsageRefusalKind>(kind), refusal?.Kind);
    }

    private const string Globals = "Attribute VB_Name = \"Globals\"\r\nOption Explicit\r\nPublic gCount As Long\r\n";

    // a Public variable of a standard module that one procedure uses, of its module or of another, goes into that procedure: its uses are the names bound
    // to it in every module of the project (MS-VBAL §5.6.10, §5.6.12).
    [TestMethod]
    public void APublicVariableOfAStandardModule_GoesIntoTheProcedureOfItsModuleThatUsesIt()
    {
        var text = Moved(Header + "Public count As Long\r\n\r\nPublic Sub Tick()\r\n    count = 1\r\nEnd Sub\r\n", "count");

        Assert.AreEqual(Header + "\r\nPublic Sub Tick()\r\n    Static count As Long\r\n    count = 1\r\nEnd Sub\r\n", text);
    }

    private const string Tallies = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n\r\nPublic Function Tally() As Long\r\n    Debug.Print \"tally\"\r\n"
        + "    Globals.gCount = gCount + 1\r\n    Tally = gCount\r\nEnd Function\r\n";

    [TestMethod]
    [DataRow("Globals", "gCount", DisplayName = "named at its declaration")]
    [DataRow("Program", "gCount + 1", DisplayName = "named at a use")]
    public void APublicVariableOfAStandardModule_GoesIntoTheProcedureOfAnotherModuleThatUsesIt_Unqualified(string module, string marker)
    {
        var project = ComposeAll(("Globals", ModuleType.StdModule, Globals), ("Program", ModuleType.StdModule, Tallies));

        var moved = project.Moved(module, marker);

        Assert.AreEqual("Attribute VB_Name = \"Globals\"\r\nOption Explicit\r\n", moved["Globals"]);
        Assert.AreEqual("Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n\r\nPublic Function Tally() As Long\r\n    Debug.Print \"tally\"\r\n"
            + "    Static gCount As Long\r\n    gCount = gCount + 1\r\n    Tally = gCount\r\nEnd Function\r\n", moved["Program"]);
    }

    [TestMethod]
    public async Task APublicVariableMovedIntoAProcedureOfAnotherModule_KeepsItsValueBetweenCalls()
    {
        var program = Tallies + "Public Sub Main()\r\n    Tally\r\n    Tally\r\n    Debug.Print Tally\r\nEnd Sub\r\n";
        var moved = ComposeAll(("Globals", ModuleType.StdModule, Globals), ("Program", ModuleType.StdModule, program)).Moved("Globals", "gCount");

        var before = await ModuleWorkspace.RunAsync([], [("Globals", Globals)], program);
        var after = await ModuleWorkspace.RunAsync([], [("Globals", moved["Globals"])], moved["Program"]);

        CollectionAssert.AreEqual(new[] { "tally", "tally", "tally", "3" }, before);
        CollectionAssert.AreEqual(before, after);
    }

    [TestMethod]
    public void APublicVariableOfTwoModulesProcedures_IsRefused_NamingThem()
    {
        var globals = Globals + "\r\nPublic Sub Add()\r\n    gCount = gCount + 1\r\nEnd Sub\r\n";
        var (_, refusal) = ComposeAll(("Globals", ModuleType.StdModule, globals), ("Program", ModuleType.StdModule, Tallies)).Prepare("Globals", "gCount");

        Assert.AreEqual(CloserToUsageRefusalKind.SeveralProcedures, refusal?.Kind);
        Assert.AreEqual("Add, Program.Tally", refusal?.Procedure);
    }

    [TestMethod]
    public void APublicVariable_WhoseProcedureNamesAnotherOfItsName_IsRefused()
    {
        var program = Tallies.Replace("Option Explicit\r\n", "Option Explicit\r\nPrivate gCount As Long\r\n", StringComparison.Ordinal)
            .Replace("    Tally = gCount\r\n", "    Tally = Globals.gCount\r\n", StringComparison.Ordinal);
        var (_, refusal) = ComposeAll(("Globals", ModuleType.StdModule, Globals), ("Program", ModuleType.StdModule, program)).Prepare("Globals", "gCount");

        Assert.AreEqual(CloserToUsageRefusalKind.HidesAName, refusal?.Kind, "Tally names the Private gCount of its module too");
    }

    // a variable of a class is a member of each of its objects.
    [TestMethod]
    public void APublicVariableOfAClassModule_IsRefused()
    {
        var (_, refusal) = Prepare(
            Compose(Header + "Public count As Long\r\n\r\nPublic Sub Tick()\r\n    count = 1\r\nEnd Sub\r\n", ModuleType.ClassModule), "count");

        Assert.AreEqual(CloserToUsageRefusalKind.NotPrivate, refusal?.Kind);
    }

    // a Static local of a member of a class has one value for each object (RD-VBAL §5.4.3.1); the variable of a standard module had one for the program.
    [TestMethod]
    public void APublicVariableOfAStandardModule_MovedIntoAMemberOfAClass_HasOneValueForEachObject()
    {
        var counter = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Counter\"\r\nOption Explicit\r\n\r\n"
            + "Public Sub Tick()\r\n    gCount = gCount + 1\r\nEnd Sub\r\n";
        var (target, _) = ComposeAll(("Globals", ModuleType.StdModule, Globals), ("Counter", ModuleType.ClassModule, counter)).Prepare("Globals", "gCount");

        Assert.AreEqual(CloserToUsageChangeKind.OneForEachObject, CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Static)?.Change);
        Assert.AreEqual(CloserToUsageChangeKind.NotKept, CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Dim)?.Change);
    }

    [TestMethod]
    public void TheChangeOfAKeyword_IsSaidInFrench()
    {
        var message = CloserToUsageNotice.Format("MoveCloserToUsage_NotKept", CultureInfo.GetCultureInfo("fr-CA"), "count", "Tick");

        Assert.AreEqual("« count » garde sa valeur entre les appels : déclarée avec Dim dans « Tick », elle est initialisée à chaque appel.", message);
    }
}
