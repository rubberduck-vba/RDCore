using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Runtime.Libraries;

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
    private static (Encapsulation? Encapsulation, EncapsulationRefusal? Refusal) Encapsulate(Workspace workspace, string marker, EncapsulationOptions? options = null)
    {
        var field = EncapsulateField.FieldAt(workspace.Context, Module1, PositionOf(workspace.Text, marker));
        Assert.IsNotNull(field, $"a field at '{marker}'");
        return EncapsulateField.Plan(workspace.Context, Module1, workspace.Parse, field, options);
    }

    private static SourcePosition PositionOf(string text, string marker)
    {
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        var before = text[..at];
        var line = before.Count(character => character == '\n');
        return new SourcePosition(line, at - (before.LastIndexOf('\n') + 1));
    }

    private static string Encapsulated(Workspace workspace, string marker, EncapsulationOptions? options = null)
    {
        var (encapsulation, refusal) = Encapsulate(workspace, marker, options);
        Assert.IsNull(refusal, refusal?.ToString());
        return DeclarationEdits.ApplyTo(workspace.Text, encapsulation!.Edits);
    }

    private const string Counter = "Public count As Long\r\n\r\nPublic Sub Reset()\r\n    count = 0\r\n    Debug.Print count\r\nEnd Sub\r\n";

    [TestMethod]
    public void ABackingField_HoldsTheValue_AndTheReferencesInsideTheModuleNameIt()
    {
        var text = Encapsulated(Compose(Header + Counter, UserHeader), "count", new EncapsulationOptions(BackingField: "mCount"));

        Assert.AreEqual(Header
            + "Private mCount As Long\r\n\r\n"
            + "Public Sub Reset()\r\n    mCount = 0\r\n    Debug.Print mCount\r\nEnd Sub\r\n\r\n"
            + "Public Property Get count() As Long\r\n    count = mCount\r\nEnd Property\r\n\r\n"
            + "Public Property Let count(ByVal value As Long)\r\n    mCount = value\r\nEnd Property\r\n", text);
    }

    [TestMethod]
    public void ABackingFieldNamedValue_IsNotShadowedByTheLetParameter()
    {
        var text = Encapsulated(Compose(Header + Counter, UserHeader), "count", new EncapsulationOptions(BackingField: "value"));

        StringAssert.Contains(text, "Public Property Let count(ByVal newValue As Long)\r\n    value = newValue\r\n");
    }

    [TestMethod]
    [DataRow("count", "NameTaken", DisplayName = "the field's own name")]
    [DataRow("Reset", "NameTaken", DisplayName = "a name of the module")]
    [DataRow("Dim", "NotAName", DisplayName = "a keyword")]
    [DataRow("2nd", "NotAName", DisplayName = "no identifier")]
    public void ABackingFieldName_ThatCannotBeDeclared_IsRefused(string backing, string kind)
    {
        var (_, refusal) = Encapsulate(Compose(Header + Counter, UserHeader), "count", new EncapsulationOptions(BackingField: backing));

        Assert.AreEqual(Enum.Parse<EncapsulationRefusalKind>(kind), refusal?.Kind);
        Assert.AreEqual(backing, refusal?.Name);
    }

    [TestMethod]
    public void AReadOnlyField_GetsAGetOnly_AndIsStillWrittenInsideItsModule()
    {
        var text = Encapsulated(Compose(Header + Counter, UserHeader), "count", new EncapsulationOptions(ReadOnly: true, InsideThroughProperties: true));

        StringAssert.Contains(text, "    this.count = 0\r\n    Debug.Print count\r\n");
        StringAssert.Contains(text, "Public Property Get count() As Long\r\n");
        Assert.DoesNotContain("Property Let", text);
    }

    [TestMethod]
    public void AReadOnlyField_AssignedOutsideItsModule_IsRefused_WhereItIsAssigned()
    {
        var module2 = UserHeader + "Public Sub Main()\r\n    count = 5\r\nEnd Sub\r\n";

        var (_, refusal) = Encapsulate(Compose(Header + Counter, module2), "count", new EncapsulationOptions(ReadOnly: true));

        Assert.AreEqual(EncapsulationRefusalKind.WrittenOutside, refusal?.Kind);
        Assert.AreEqual(Module2, refusal?.Module);
        Assert.AreEqual(3, refusal?.Range?.Start.Line);
    }

    private const string Text = "Public text As String\r\n\r\nPublic Sub Retitle()\r\n    Mid(text, 1, 1) = \"x\"\r\n    Debug.Print text\r\nEnd Sub\r\n";

    [TestMethod]
    [DataRow("    Mid(text, 1, 1) = \"x\"", DisplayName = "Mid")]
    [DataRow("    Mid$(text, 1, 1) = \"x\"", DisplayName = "Mid$")]
    [DataRow("    MidB(text, 1, 2) = \"x\"", DisplayName = "MidB")]
    [DataRow("    MidB$(text, 1) = \"x\"", DisplayName = "MidB$, no length")]
    [DataRow("    Mid(Module1.text, 1, 1) = \"x\"", DisplayName = "Mid, the field qualified")]
    public void AFieldAMidStatementWritesOutsideItsModule_IsRefused_WhereItIsWritten(string write)
    {
        var module2 = UserHeader + "Public Sub Main()\r\n" + write + "\r\nEnd Sub\r\n";

        var (_, refusal) = Encapsulate(Compose(Header + Text, module2), "text");

        Assert.AreEqual(EncapsulationRefusalKind.ReplacedByMid, refusal?.Kind);
        Assert.AreEqual(Module2, refusal?.Module);
        Assert.AreEqual(3, refusal?.Range?.Start.Line);
    }

    [TestMethod]
    public void AFieldAMidStatementWritesInsideItsModule_IsEncapsulated_TheMidWritingTheState()
    {
        var text = Encapsulated(Compose(Header + Text, UserHeader), "text");

        StringAssert.Contains(text, "    Mid(this.text, 1, 1) = \"x\"\r\n    Debug.Print this.text\r\n");
        var parsed = new ModuleParser().Parse(Module1, text);
        Assert.IsTrue(parsed.IsSuccess, string.Join("; ", parsed.SyntaxErrors.Select(error => error.Verbose)));
    }

    [TestMethod]
    public void AMidStatementInsideTheModule_StillWritesTheState_WhenTheReferencesGoThroughTheProperties()
    {
        var text = Encapsulated(Compose(Header + Text, UserHeader), "text", new EncapsulationOptions(InsideThroughProperties: true));

        StringAssert.Contains(text, "    Mid(this.text, 1, 1) = \"x\"\r\n    Debug.Print text\r\n");
    }

    [TestMethod]
    public void TheReferencesInsideTheModule_GoThroughTheProperties_WhenAsked()
    {
        var text = Encapsulated(Compose(Header + Counter, UserHeader), "count", new EncapsulationOptions(InsideThroughProperties: true));

        StringAssert.Contains(text, "Public Sub Reset()\r\n    count = 0\r\n    Debug.Print count\r\nEnd Sub\r\n");
        StringAssert.Contains(text, "Public Property Let count(ByVal value As Long)\r\n    this.count = value\r\n");
    }

    [TestMethod]
    public void AFieldPassedByRefInsideAClassModule_ThroughTheProperties_IsReported()
    {
        var module1 = Header + "Public count As Long\r\n\r\nPrivate Sub Bump(ByRef n As Long)\r\n    n = n + 1\r\nEnd Sub\r\n\r\nPublic Sub Run()\r\n    Bump count\r\nEnd Sub\r\n";
        var workspace = Compose(module1, UserHeader, ModuleType.ClassModule);

        var (state, _) = Encapsulate(workspace, "count");
        var (properties, _) = Encapsulate(workspace, "count", new EncapsulationOptions(InsideThroughProperties: true));

        Assert.IsEmpty(state!.Fields.Single().PassedByRef);
        Assert.AreEqual(Module1, properties!.Fields.Single().PassedByRef.Single().Module);
    }

    [TestMethod]
    public void AUserDefinedTypeField_WhoseMemberIsWrittenInsideItsModule_ThroughTheProperties_IsRefused()
    {
        var module1 = Header + Point + "\r\nPublic Origin As TPoint\r\n\r\nPublic Sub Reset()\r\n    Origin.X = 0\r\nEnd Sub\r\n";

        var (_, refusal) = Encapsulate(Compose(module1, UserHeader), "Origin", new EncapsulationOptions(InsideThroughProperties: true));

        Assert.AreEqual(EncapsulationRefusalKind.WritesThroughACopy, refusal?.Kind);
        Assert.AreEqual(Module1, refusal?.Module);
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
    [DataRow("VBA.Collection", DisplayName = "the library's class, qualified")]
    [DataRow("Collection", DisplayName = "the project's class named like it")]
    public void TheTypeOfTheMemberAndTheProperties_IsWrittenAsTheFieldDeclaresIt_AndNamesTheSameType(string written)
    {
        var collection = new UriBuilder(Root) { Fragment = "Collection" }.Uri;
        var module1 = Header + $"Public Items As {written}\r\n";
        (Uri, ModuleType, string) project = (collection, ModuleType.ClassModule, "Attribute VB_Name = \"Collection\"\r\nOption Explicit\r\n");
        var workspace = ComposeWithLibraries(module1, project);
        var field = (ITypedSymbol)EncapsulateField.FieldAt(workspace.Context, Module1, PositionOf(module1, "Items"))!;

        var text = Encapsulated(workspace, "Items");

        StringAssert.Contains(text, $"    Items As {written}\r\n");
        StringAssert.Contains(text, $"Public Property Get Items() As {written}\r\n");
        StringAssert.Contains(text, $"Public Property Set Items(ByVal value As {written})\r\n");
        var after = ComposeWithLibraries(text, project);
        var property = (ITypedSymbol)RenameBinding.SymbolAt(after.Context, Module1, PositionOf(text, "Items() As"))!.Value.Symbol;
        Assert.IsInstanceOfType<VBClassType>(field.ResolvedType);
        Assert.AreEqual(((VBClassType)field.ResolvedType!).Symbol.Uri, (property.ResolvedType as VBClassType)?.Symbol.Uri);
    }

    // Module1 and other modules, composed with the VBA library referenced.
    private static Workspace ComposeWithLibraries(string module1, params (Uri Uri, ModuleType Type, string Text)[] others)
    {
        IReadOnlyList<Symbol> libraries = [.. new LibrarySymbolProvider(Root, ReferencedLibraries.Load(new InMemoryLibrarySource(), [], [StandardLibrary.LibraryName])).ProvideSymbols()];
        var modules = new[] { (Uri: Module1, Type: ModuleType.StdModule, Text: module1) }.Concat(others)
            .Select(module => (module.Uri, module.Type, module.Text, Parse: new ModuleParser().Parse(module.Uri, module.Text)))
            .ToList();
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            Root, [.. modules.Select(module => (module.Uri, module.Type, module.Parse))], new IntrinsicSymbolResolver(), libraries: libraries);
        var context = new RenameContext(composition.Resolver, composition.ScopeTree, [.. modules.Select(module => new RenameModule(module.Uri, module.Parse.SyntaxTree!, module.Text))]);
        return new(context, modules[0].Parse, module1);
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
        var passed = encapsulation!.Fields.Single().PassedByRef;
        Assert.HasCount(1, passed);
        Assert.AreEqual(Module2.AbsoluteUri, passed[0].Module.AbsoluteUri);
    }

    private const string Grow = "Public items() As Long\r\n\r\nPublic Sub Grow()\r\n    ReDim items(1 To 3)\r\n    items(1) = 5\r\n    Debug.Print items(1)\r\nEnd Sub\r\n";

    [TestMethod]
    public void ADynamicArrayField_IsHeldAsAnArrayMember_AndItsPropertiesGiveAndTakeAnArray()
    {
        var text = Encapsulated(Compose(Header + Grow, UserHeader), "items");

        Assert.AreEqual(Header
            + "Private Type TModule1\r\n    items() As Long\r\nEnd Type\r\n\r\nPrivate this As TModule1\r\n\r\n"
            + "Public Sub Grow()\r\n    ReDim this.items(1 To 3)\r\n    this.items(1) = 5\r\n    Debug.Print this.items(1)\r\nEnd Sub\r\n\r\n"
            + "Public Property Get items() As Long()\r\n    items = this.items\r\nEnd Property\r\n\r\n"
            + "Public Property Let items(value() As Long)\r\n    this.items = value\r\nEnd Property\r\n", text);
    }

    [TestMethod]
    public void ADynamicArrayField_ThroughTheProperties_IsStillWrittenInsideItsModuleThroughTheState()
    {
        var text = Encapsulated(Compose(Header + Grow, UserHeader), "items", new EncapsulationOptions(InsideThroughProperties: true));

        StringAssert.Contains(text, "    ReDim this.items(1 To 3)\r\n    this.items(1) = 5\r\n    Debug.Print items(1)\r\n");
    }

    [TestMethod]
    public void ADynamicArrayField_WithABackingField_IsDeclaredAsAnArray()
    {
        var text = Encapsulated(Compose(Header + Grow, UserHeader), "items", new EncapsulationOptions(BackingField: "mItems"));

        StringAssert.StartsWith(text, Header + "Private mItems() As Long\r\n\r\n");
        StringAssert.Contains(text, "    ReDim mItems(1 To 3)\r\n    mItems(1) = 5\r\n");
    }

    [TestMethod]
    [DataRow("    items(1) = 5", DisplayName = "an element assigned")]
    [DataRow("    Module1.items(1) = 5", DisplayName = "an element assigned, the field qualified")]
    [DataRow("    ReDim items(1 To 2)", DisplayName = "the array resized")]
    [DataRow("    Erase items", DisplayName = "the array erased")]
    public void AnArrayFieldWrittenOutside_IsRefused_ForTheWriteWouldGoToACopy(string write)
    {
        var module2 = UserHeader + "Public Sub Main()\r\n" + write + "\r\nEnd Sub\r\n";

        var (encapsulation, refusal) = Encapsulate(Compose(Header + Grow, module2), "items");

        Assert.IsNull(encapsulation);
        Assert.AreEqual(EncapsulationRefusalKind.WritesAnElement, refusal?.Kind, refusal?.ToString());
        Assert.AreEqual(Module2.AbsoluteUri, refusal!.Module!.AbsoluteUri);
        Assert.AreEqual(3, refusal.Range?.Start.Line);
    }

    [TestMethod]
    [DataRow("    Bump items(1)", 1, DisplayName = "an element passed")]
    [DataRow("    Bump n:=items(1)", 1, DisplayName = "an element passed by name")]
    [DataRow("    Bump (items(1))", 0, DisplayName = "an element in parentheses")]
    [DataRow("    Debug.Print items(1) + UBound(items)", 0, DisplayName = "an element read")]
    public void AnArrayElementPassedToACallOutside_IsEncapsulated_AndTheCallIsReported(string call, int reported)
    {
        var module2 = UserHeader + "Private Sub Bump(ByRef n As Long)\r\n    n = n + 1\r\nEnd Sub\r\n\r\nPublic Sub Main()\r\n" + call + "\r\nEnd Sub\r\n";

        var (encapsulation, refusal) = Encapsulate(Compose(Header + Grow, module2), "items");

        Assert.IsNull(refusal, refusal?.ToString());
        var passed = encapsulation!.Fields.Single().ElementsPassed;
        Assert.HasCount(reported, passed);
        Assert.IsTrue(passed.All(reference => reference.Module.AbsoluteUri == Module2.AbsoluteUri && reference.Range.Start.Line == 7), string.Join("; ", passed));
    }

    [TestMethod]
    public void AFixedLengthStringField_KeepsItsLengthWhereItIsHeld_AndItsPropertiesGiveAndTakeAString()
    {
        var module1 = Header + "Public code As String * 4\r\n";

        var held = Encapsulated(Compose(module1, UserHeader), "code");
        var backed = Encapsulated(Compose(module1, UserHeader), "code", new EncapsulationOptions(BackingField: "mCode"));

        StringAssert.Contains(held, "Private Type TModule1\r\n    code As String * 4\r\nEnd Type\r\n");
        StringAssert.Contains(held, "Public Property Get code() As String\r\n    code = this.code\r\nEnd Property\r\n");
        StringAssert.Contains(held, "Public Property Let code(ByVal value As String)\r\n    this.code = value\r\nEnd Property\r\n");
        StringAssert.StartsWith(backed, Header + "Private mCode As String * 4\r\n");
    }

    // the fields declared or named at the first place each marker is written in Module1, encapsulated in one edit.
    private static (Encapsulation? Encapsulation, EncapsulationRefusal? Refusal) EncapsulateAll(Workspace workspace, params (string Marker, EncapsulationOptions Options)[] fields)
        => EncapsulateField.Plan(workspace.Context, Module1, workspace.Parse,
            [.. fields.Select(each => (EncapsulateField.FieldAt(workspace.Context, Module1, PositionOf(workspace.Text, each.Marker))!, each.Options))]);

    private const string Totals = "Public count As Long\r\nPublic total As Currency\r\n\r\nPublic Sub Reset()\r\n    count = 0\r\n    total = 0\r\nEnd Sub\r\n";

    [TestMethod]
    public void FieldsEncapsulatedInOneEdit_ShareThePrivateType_AndTheirPropertiesFollowOneAnother()
    {
        var workspace = Compose(Header + Totals, UserHeader);

        var (encapsulation, refusal) = EncapsulateAll(workspace, ("count", EncapsulationOptions.Defaults), ("total", new EncapsulationOptions(ReadOnly: true)));

        Assert.IsNull(refusal, refusal?.ToString());
        Assert.AreEqual(Header
            + "Private Type TModule1\r\n    count As Long\r\n    total As Currency\r\nEnd Type\r\n\r\nPrivate this As TModule1\r\n\r\n"
            + "Public Sub Reset()\r\n    this.count = 0\r\n    this.total = 0\r\nEnd Sub\r\n\r\n"
            + "Public Property Get count() As Long\r\n    count = this.count\r\nEnd Property\r\n\r\n"
            + "Public Property Let count(ByVal value As Long)\r\n    this.count = value\r\nEnd Property\r\n\r\n"
            + "Public Property Get total() As Currency\r\n    total = this.total\r\nEnd Property\r\n", DeclarationEdits.ApplyTo(workspace.Text, encapsulation!.Edits));
        CollectionAssert.AreEqual(new[] { "count", "total" }, encapsulation.Fields.Select(each => each.Field.Name).ToArray());
    }

    [TestMethod]
    public void FieldsEncapsulatedInOneEdit_WithBackingFields_EachHaveTheirOwn()
    {
        var workspace = Compose(Header + Totals, UserHeader);

        var (encapsulation, refusal) = EncapsulateAll(workspace, ("count", new EncapsulationOptions(BackingField: "mCount")), ("total", new EncapsulationOptions(BackingField: "mTotal")));

        Assert.IsNull(refusal, refusal?.ToString());
        var text = DeclarationEdits.ApplyTo(workspace.Text, encapsulation!.Edits);
        StringAssert.StartsWith(text, Header + "Private mCount As Long\r\nPrivate mTotal As Currency\r\n\r\nPublic Sub Reset()\r\n    mCount = 0\r\n    mTotal = 0\r\n");
    }

    [TestMethod]
    public void FieldsEncapsulatedInOneEdit_AreAddedToThePrivateTypeThatHasASingleVariable_InOneEdit()
    {
        var module1 = Header + "Private Type TState\r\n    name As String\r\nEnd Type\r\n\r\nPrivate state As TState\r\n" + Totals;
        var workspace = Compose(module1, UserHeader);

        var (encapsulation, refusal) = EncapsulateAll(workspace, ("count", EncapsulationOptions.Defaults), ("total", EncapsulationOptions.Defaults));

        Assert.IsNull(refusal, refusal?.ToString());
        StringAssert.StartsWith(DeclarationEdits.ApplyTo(workspace.Text, encapsulation!.Edits),
            Header + "Private Type TState\r\n    name As String\r\n    count As Long\r\n    total As Currency\r\nEnd Type\r\n\r\nPrivate state As TState\r\n\r\n");
    }

    [TestMethod]
    public void FieldsNextToOneAnotherInOneStatement_AreRefused_AndTheRefusalNamesBoth()
    {
        var workspace = Compose(Header + "Public count As Long, total As Long\r\n", UserHeader);

        var (encapsulation, refusal) = EncapsulateAll(workspace, ("count", EncapsulationOptions.Defaults), ("total", EncapsulationOptions.Defaults));

        Assert.IsNull(encapsulation);
        Assert.AreEqual(EncapsulationRefusalKind.SharesItsStatement, refusal?.Kind);
        Assert.AreEqual("count", refusal?.Name);
        Assert.AreEqual("total", refusal?.Field?.Name);
    }

    [TestMethod]
    public void FieldsEncapsulatedInOneEdit_WithTheSameBackingField_AreRefused()
    {
        var workspace = Compose(Header + Totals, UserHeader);

        var (_, refusal) = EncapsulateAll(workspace, ("count", new EncapsulationOptions(BackingField: "mValue")), ("total", new EncapsulationOptions(BackingField: "mValue")));

        Assert.AreEqual(EncapsulationRefusalKind.NameTaken, refusal?.Kind);
        Assert.AreEqual("mValue", refusal?.Name);
        Assert.AreEqual("total", refusal?.Field?.Name);
    }

    [TestMethod]
    public void AFieldEncapsulatedWithOthers_ThatIsRefused_IsNamedByTheRefusal()
    {
        var workspace = Compose(Header + "Public count As Long\r\nPublic WithEvents source As Collection\r\n", UserHeader, ModuleType.ClassModule);

        var (_, refusal) = EncapsulateAll(workspace, ("count", EncapsulationOptions.Defaults), ("source", EncapsulationOptions.Defaults));

        Assert.AreEqual(EncapsulationRefusalKind.WithEvents, refusal?.Kind);
        Assert.AreEqual("source", refusal?.Field?.Name);
    }

    [TestMethod]
    [DataRow("Private count As Long", "NotPublic", DisplayName = "a private field")]
    [DataRow("Public count(1 To 3) As Long", "NotSupported", DisplayName = "an array of a fixed size")]
    [DataRow("Public count As New Collection", "NotSupported", DisplayName = "an auto-instantiated object")]
    [DataRow("#If VBA7 Then\r\nPublic count As Long\r\n#End If", "Conditional", DisplayName = "in an #If branch")]
    [DataRow("Public count As Long: Public other As Long", "SharesItsLine", DisplayName = "sharing its line")]
    [DataRow("Public count As Long\r\nPublic Sub Main()\r\n    Dim this As Long\r\nEnd Sub", "NameTaken", DisplayName = "this taken")]
    public void AFieldThatIsNotEncapsulated_IsRefused(string declaration, string expected)
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
    public void TheReferences_ReferToThePropertiesOutside_AndToTheTypeMemberInside_AndNoneIsLost()
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
