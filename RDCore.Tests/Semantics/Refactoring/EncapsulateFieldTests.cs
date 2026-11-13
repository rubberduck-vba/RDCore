using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// A Public field encapsulated behind properties named after it: the properties its type takes, the private type that holds its value, the references inside
/// its module written through that type's variable, the ones outside left to refer to the properties, and what is refused.
/// </summary>
[TestClass]
public sealed class EncapsulateFieldTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Module1 = new UriBuilder(Root) { Fragment = "Module1" }.Uri;
    private static readonly Uri Module2 = new UriBuilder(Root) { Fragment = "Module2" }.Uri;

    private const string Header = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n";

    private const string UserHeader = "Attribute VB_Name = \"Module2\"\r\nOption Explicit\r\n";

    private const string Point = "Public Type TPoint\r\n    X As Long\r\n    Y As Long\r\nEnd Type\r\n";

    private sealed record class Workspace(RenameContext Context, ModuleParseResult Parse, string Text);

    private static Workspace Compose(string module1, string module2, ModuleType type1 = ModuleType.StdModule)
    {
        var first = new ModuleParser().Parse(Module1, module1);
        var second = new ModuleParser().Parse(Module2, module2);
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            Root, [(Module1, type1, first), (Module2, ModuleType.StdModule, second)], new IntrinsicSymbolResolver());
        var context = new RenameContext(composition.Resolver, composition.ScopeTree,
            [new RenameModule(Module1, first.SyntaxTree!, module1), new RenameModule(Module2, second.SyntaxTree!, module2)]);
        return new(context, first, module1);
    }

    // the field declared or named at the first place `marker` is written in Module1, encapsulated.
    private static (Encapsulation? Encapsulation, EncapsulationRefusal? Refusal) Encapsulate(Workspace workspace, string marker)
    {
        var field = EncapsulateField.FieldAt(workspace.Context, Module1, PositionOf(workspace.Text, marker));
        Assert.IsNotNull(field, $"a field at '{marker}'");
        return EncapsulateField.Plan(workspace.Context, Module1, workspace.Parse, field);
    }

    private static SourcePosition PositionOf(string text, string marker)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        var before = text[..at];
        var line = before.Count(character => character == '\n');
        return new SourcePosition(line, at - (before.LastIndexOf('\n') + 1));
    }

    private static string Encapsulated(Workspace workspace, string marker)
    {
        var (encapsulation, refusal) = Encapsulate(workspace, marker);
        Assert.IsNull(refusal, refusal?.ToString());
        return DeclarationEdits.ApplyTo(workspace.Text, encapsulation!.Edits);
    }

    [TestMethod]
    public void AValueField_GetsAGetAndALet_AndTheModuleReadsItThroughItsPrivateType()
    {
        var module1 = Header + "Public count As Long\r\n\r\nPublic Sub Reset()\r\n    count = 0\r\nEnd Sub\r\n";

        var text = Encapsulated(Compose(module1, UserHeader), "count");

        Assert.AreEqual(Header
            + "Private Type TModule1\r\n    count As Long\r\nEnd Type\r\n\r\nPrivate this As TModule1\r\n\r\n"
            + "Public Sub Reset()\r\n    this.count = 0\r\nEnd Sub\r\n\r\n"
            + "Public Property Get count() As Long\r\n    count = this.count\r\nEnd Property\r\n\r\n"
            + "Public Property Let count(ByVal value As Long)\r\n    this.count = value\r\nEnd Property\r\n", text);
    }

    [TestMethod]
    public void AnObjectField_GetsAGetAndASet()
    {
        var module1 = Header + "Public items As Collection\r\n\r\nPublic Sub Clear()\r\n    Set items = New Collection\r\nEnd Sub\r\n";

        var text = Encapsulated(Compose(module1, UserHeader), "items");

        StringAssert.Contains(text, "    Set this.items = New Collection\r\n");
        StringAssert.Contains(text, "Public Property Get items() As Collection\r\n    Set items = this.items\r\nEnd Property\r\n");
        StringAssert.Contains(text, "Public Property Set items(ByVal value As Collection)\r\n    Set this.items = value\r\nEnd Property\r\n");
        Assert.DoesNotContain("Property Let", text);
    }

    [TestMethod]
    public void AVariantField_GetsAGetThatAssignsAnObjectWithSet_AndALetAndASet()
    {
        var module1 = Header + "Public anything As Variant\r\n";

        var text = Encapsulated(Compose(module1, UserHeader), "anything");

        StringAssert.Contains(text, "Public Property Get anything() As Variant\r\n    If IsObject(this.anything) Then\r\n        Set anything = this.anything\r\n"
            + "    Else\r\n        anything = this.anything\r\n    End If\r\nEnd Property\r\n");
        StringAssert.Contains(text, "Public Property Let anything(ByVal value As Variant)\r\n    this.anything = value\r\nEnd Property\r\n");
        StringAssert.Contains(text, "Public Property Set anything(ByVal value As Variant)\r\n    Set this.anything = value\r\nEnd Property\r\n");
    }

    [TestMethod]
    public void AFieldNamedValue_GetsItsLetParameterNamedOtherwise()
    {
        var text = Encapsulated(Compose(Header + "Public Value As Long\r\n", UserHeader), "Value");

        StringAssert.Contains(text, "Public Property Let Value(ByVal newValue As Long)\r\n    this.Value = newValue\r\n");
    }

    [TestMethod]
    public void AUserDefinedTypeFieldThatIsOnlyReadOutside_GetsAGetOnly()
    {
        var module1 = Header + Point + "\r\nPublic Origin As TPoint\r\n";
        var module2 = UserHeader + "Public Sub Main()\r\n    Debug.Print Origin.X\r\nEnd Sub\r\n";

        var text = Encapsulated(Compose(module1, module2), "Origin");

        StringAssert.Contains(text, "Public Property Get Origin() As TPoint\r\n    Origin = this.Origin\r\nEnd Property\r\n");
        Assert.DoesNotContain("Property Let", text);
    }

    [TestMethod]
    [DataRow("    Origin.X = 5", DisplayName = "a member assigned")]
    [DataRow("    Module1.Origin.X = 5", DisplayName = "a member assigned, the field qualified")]
    [DataRow("    Bump Origin.X", DisplayName = "a member passed ByRef")]
    [DataRow("    With Origin\r\n        .X = 5\r\n    End With", DisplayName = "a member assigned in a With block")]
    [DataRow("    Dim p As TPoint\r\n    Origin = p", DisplayName = "the whole field assigned")]
    public void AUserDefinedTypeFieldWrittenOutside_IsRefused_ForTheWriteWouldGoToACopy(string write)
    {
        var module1 = Header + Point + "\r\nPublic Origin As TPoint\r\n";
        var module2 = UserHeader + "Private Sub Bump(ByRef n As Long)\r\n    n = n + 1\r\nEnd Sub\r\n\r\nPublic Sub Main()\r\n" + write + "\r\nEnd Sub\r\n";

        var (encapsulation, refusal) = Encapsulate(Compose(module1, module2), "Origin");

        Assert.IsNull(encapsulation);
        Assert.AreEqual(EncapsulationRefusalKind.WritesThroughACopy, refusal?.Kind, refusal?.ToString());
        Assert.AreEqual(Module2.AbsoluteUri, refusal!.Module!.AbsoluteUri);
    }

    [TestMethod]
    public void AUserDefinedTypeFieldWhoseMemberIsPassedByValueOutside_IsEncapsulated()
    {
        var module1 = Header + Point + "\r\nPublic Origin As TPoint\r\n";
        var module2 = UserHeader + "Private Sub Show(ByVal n As Long)\r\nEnd Sub\r\n\r\nPublic Sub Main()\r\n    Show Origin.X\r\nEnd Sub\r\n";

        var (encapsulation, refusal) = Encapsulate(Compose(module1, module2), "Origin");

        Assert.IsNull(refusal, refusal?.ToString());
        Assert.IsNotNull(encapsulation);
    }

    [TestMethod]
    public void ASecondField_IsAddedToThePrivateTypeThatHasASingleVariable_WhateverTheirNames()
    {
        var module1 = Header + "Private Type TState\r\n    count As Long\r\nEnd Type\r\n\r\nPrivate state As TState\r\nPublic total As Long\r\n\r\n"
            + "Public Sub Reset()\r\n    total = state.count\r\nEnd Sub\r\n";

        var text = Encapsulated(Compose(module1, UserHeader), "total");

        StringAssert.StartsWith(text, Header + "Private Type TState\r\n    count As Long\r\n    total As Long\r\nEnd Type\r\n\r\nPrivate state As TState\r\n\r\n");
        StringAssert.Contains(text, "    state.total = state.count\r\n");
        StringAssert.Contains(text, "Public Property Get total() As Long\r\n    total = state.total\r\nEnd Property\r\n");
    }

    [TestMethod]
    public void AFieldPassedByRefOutsideAStandardModule_IsEncapsulated_AndTheCallIsReported()
    {
        var module2 = UserHeader + "Private Sub Bump(ByRef n As Long)\r\n    n = n + 1\r\nEnd Sub\r\n\r\nPublic Sub Main()\r\n    Bump count\r\nEnd Sub\r\n";

        var (encapsulation, refusal) = Encapsulate(Compose(Header + "Public count As Long\r\n", module2), "count");

        Assert.IsNull(refusal, refusal?.ToString());
        Assert.HasCount(1, encapsulation!.PassedByRef);
        Assert.AreEqual(Module2.AbsoluteUri, encapsulation.PassedByRef[0].Module.AbsoluteUri);
    }

    [TestMethod]
    [DataRow("Private count As Long", "NotPublic", DisplayName = "a private field")]
    [DataRow("Public count() As Long", "NotSupported", DisplayName = "an array")]
    [DataRow("Public count As String * 4", "NotSupported", DisplayName = "a fixed-length string")]
    [DataRow("Public count As New Collection", "NotSupported", DisplayName = "an auto-instantiated object")]
    [DataRow("#If VBA7 Then\r\nPublic count As Long\r\n#End If", "Conditional", DisplayName = "in an #If branch")]
    [DataRow("Public count As Long: Public other As Long", "SharesItsLine", DisplayName = "sharing its line")]
    [DataRow("Public count As Long\r\nPublic Sub Main()\r\n    Dim this As Long\r\nEnd Sub", "NameTaken", DisplayName = "this taken")]
    public void AFieldThatIsNotEncapsulatedYet_IsRefused(string declaration, string expected)
    {
        var (encapsulation, refusal) = Encapsulate(Compose(Header + declaration + "\r\n", UserHeader), "count");

        Assert.IsNull(encapsulation);
        Assert.AreEqual(expected, refusal?.Kind.ToString());
    }

    [TestMethod]
    public void AWithEventsField_IsRefused()
    {
        var module1 = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\nPublic WithEvents source As Collection\r\n";

        var (encapsulation, refusal) = Encapsulate(Compose(module1, UserHeader, ModuleType.ClassModule), "source");

        Assert.IsNull(encapsulation);
        Assert.AreEqual(EncapsulationRefusalKind.WithEvents, refusal?.Kind);
    }

    [TestMethod]
    public void TheReferences_RefertoThePropertiesOutside_AndToTheTypeMemberInside_AndNoneIsLost()
    {
        var module1 = Header + "Public count As Long\r\n\r\nPublic Sub Reset()\r\n    count = 0\r\nEnd Sub\r\n";
        var module2 = UserHeader + "Public Sub Main()\r\n    count = count + 1\r\n    Module1.count = 2\r\nEnd Sub\r\n";
        var before = Compose(module1, module2);

        var (encapsulation, _) = Encapsulate(before, "count");
        var after = Compose(DeclarationEdits.ApplyTo(module1, encapsulation!.Edits), module2);
        var changes = BindingCheck.Changes(before.Context, after.Context,
            new Dictionary<string, IReadOnlyList<RefactorTextEdit>> { [Module1.AbsoluteUri] = encapsulation.Edits });

        Assert.IsFalse(changes.Any(change => change.Kind is BindingChangeKind.Lost or BindingChangeKind.Unknown), string.Join("; ", changes));
        // the properties are known by the name the field was: the references outside refer to them as they referred to the field.
        Assert.IsFalse(changes.Any(change => change.Module.AbsoluteUri == Module2.AbsoluteUri), string.Join("; ", changes));
        var found = NameOccurrences.Find(after.Context, "count", [after.Context.Modules[1]]);
        Assert.IsEmpty(found.Unknown);
        Assert.HasCount(3, found.Occurrences);
        Assert.IsTrue(found.Occurrences.All(occurrence => occurrence.Binding is { } binding
            && NameOccurrences.SymbolOf(after.Context, binding) is { } symbol && symbol is not RDCore.SDK.Model.Symbols.VBProject.VBModuleFieldVariableMemberSymbol),
            string.Join("; ", found.Occurrences.Select(occurrence => occurrence.Binding)));
        var inside = changes.Single(change => change.Module.AbsoluteUri == Module1.AbsoluteUri && change.Kind is BindingChangeKind.Retargeted);
        Assert.AreEqual(new SourceRange(9, 9, 9, 14), inside.After, "this.count, in Reset");
    }
}
