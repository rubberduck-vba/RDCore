using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
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
        var (edits, refused) = CloserToUsage.Plan(text, workspace.Parse, target!, keyword);
        Assert.IsNull(refused, refused?.ToMessage());
        return DeclarationEdits.ApplyTo(text, edits!);
    }

    private const string Counter = "Private count As Long\r\n\r\nPublic Sub Tick()\r\n    Debug.Print \"tick\"\r\n    count = count + 1\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow(CloserToUsageKeyword.Static, "Static", DisplayName = "Static, the default")]
    [DataRow(CloserToUsageKeyword.Dim, "Dim", DisplayName = "Dim")]
    public void AVariableOfTheModule_GoesIntoTheProcedureThatUsesIt_BeforeItsFirstUse(CloserToUsageKeyword keyword, string written)
    {
        var text = Moved(Header + Counter, "count", keyword);

        Assert.AreEqual(Header + "\r\nPublic Sub Tick()\r\n    Debug.Print \"tick\"\r\n    " + written + " count As Long\r\n    count = count + 1\r\nEnd Sub\r\n", text);
    }

    [TestMethod]
    public void AVariableOfAStandardModule_KeepsItsValueAsStatic_AndNotAsDim()
    {
        var (target, _) = Prepare(Compose(Header + Counter), "count");

        Assert.IsNull(CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Static));
        Assert.AreEqual(CloserToUsageChangeKind.NotKept, CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Dim)?.Change);
    }

    [TestMethod]
    public void AVariableOfAClassModule_ChangesItsExtent_WithEitherKeyword()
    {
        var (target, _) = Prepare(Compose(Header + Counter, ModuleType.ClassModule), "count");

        Assert.AreEqual(CloserToUsageChangeKind.ModuleExtent, CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Static)?.Change);
        Assert.AreEqual(CloserToUsageChangeKind.NotKept, CloserToUsage.ChangeOf(target!, CloserToUsageKeyword.Dim)?.Change);
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
    [DataRow("Public count As Long\r\n\r\nPublic Sub Tick()\r\n    count = 1\r\nEnd Sub\r\n", "count", CloserToUsageRefusalKind.NotPrivate, DisplayName = "a Public variable of the module")]
    [DataRow("Private count As Long\r\n\r\nPublic Sub A()\r\n    count = 1\r\nEnd Sub\r\n\r\nPublic Sub B()\r\n    count = 2\r\nEnd Sub\r\n", "count", CloserToUsageRefusalKind.SeveralProcedures, DisplayName = "used by two procedures")]
    [DataRow("Private count As Long\r\n\r\nPublic Sub Tick()\r\nEnd Sub\r\n", "count", CloserToUsageRefusalKind.NotUsed, DisplayName = "used by nothing")]
    [DataRow("Public Sub F()\r\n    Dim a As Long\r\n    Dim b As Long\r\n    a = 1\r\nEnd Sub\r\n", "a As", CloserToUsageRefusalKind.AlreadyClose, DisplayName = "only declarations before its first use")]
    [DataRow("Private Sub Tick()\r\nEnd Sub\r\n", "Tick", CloserToUsageRefusalKind.NotAVariable, DisplayName = "a procedure")]
    [DataRow("Private count As Long: Private other As Long\r\n\r\nPublic Sub Tick()\r\n    count = 1\r\n    other = 1\r\nEnd Sub\r\n", "count", CloserToUsageRefusalKind.SharesItsLine, DisplayName = "a statement that shares its line")]
    public void ADeclarationThatCannotMove_IsRefused(string body, string marker, CloserToUsageRefusalKind kind)
    {
        var workspace = Compose(Header + body);
        var (target, refusal) = Prepare(workspace, marker);
        if (target is not null)
        {
            (_, refusal) = CloserToUsage.Plan(workspace.Source.Source, workspace.Parse, target, CloserToUsageKeyword.Static);
        }

        Assert.AreEqual(kind, refusal?.Kind);
    }

    [TestMethod]
    public void TheChangeOfAKeyword_IsSaidInFrench()
    {
        var message = CloserToUsageNotice.Format("MoveCloserToUsage_NotKept", CultureInfo.GetCultureInfo("fr-CA"), "count", "Tick");

        Assert.AreEqual("« count » garde sa valeur entre les appels : déclarée avec Dim dans « Tick », elle est initialisée à chaque appel.", message);
    }
}
