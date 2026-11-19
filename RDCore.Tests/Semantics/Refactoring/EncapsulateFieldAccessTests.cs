using RDCore.LanguageServer.Services.Refactoring;
using RDCore.LanguageServer.Symbols;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.Symbols;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The access modifiers of the properties a field is encapsulated behind (<strong>MS-VBAL §5.3.1.1</strong>): a <c>Public</c> <c>Property Get</c>, and a
/// <c>Friend</c> <c>Property Let</c> and <c>Property Set</c> in a class module, by default; <c>Public</c> ones in a standard module, which has no
/// <c>Friend</c> procedure; and what a <c>Private</c> one refuses.
/// </summary>
[TestClass]
public sealed class EncapsulateFieldAccessTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly Uri Widget = new UriBuilder(Root) { Fragment = "Widget" }.Uri;
    private static readonly Uri User = new UriBuilder(Root) { Fragment = "User" }.Uri;

    private const string NoUser = "Attribute VB_Name = \"User\"\r\nOption Explicit\r\n";

    private sealed record class Workspace(RenameContext Context, ModuleParseResult Parse, string Text);

    // the module Widget, of the type given, and the standard module User, which may refer to Widget's fields.
    private static Workspace Compose(string widget, ModuleType type, string user = NoUser)
    {
        var first = new ModuleParser().Parse(Widget, widget);
        var second = new ModuleParser().Parse(User, user);
        var composition = WorkspaceSymbolResolver.ComposeWithScopes(
            Root, [(Widget, type, first), (User, ModuleType.StdModule, second)], new IntrinsicSymbolResolver());
        var context = new RenameContext(composition.Resolver, composition.ScopeTree,
            [new RenameModule(Widget, first.SyntaxTree!, widget), new RenameModule(User, second.SyntaxTree!, user)]);
        return new(context, first, widget);
    }

    // the module's text as the refactoring sees it: a class module's header block is the harness's (ClassHeader).
    private static string ClassWith(string field) => ModuleWorkspace.StandardModule("Widget", "Option Explicit", field);

    private static string StandardWith(string field) => ModuleWorkspace.StandardModule("Widget", "Option Explicit", field);

    private const string ClassHeader = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n";

    private static (Encapsulation? Encapsulation, EncapsulationRefusal? Refusal) Encapsulate(
        Workspace workspace, string name, EncapsulationOptions? options = null)
    {
        var at = workspace.Text.IndexOf($" {name} As", StringComparison.Ordinal) + 1;
        var before = workspace.Text[..at];
        var position = new SourcePosition(before.Count(character => character == '\n'), at - (before.LastIndexOf('\n') + 1));
        var field = EncapsulateField.FieldAt(workspace.Context, Widget, position);
        Assert.IsNotNull(field, $"a field named {name}");
        return EncapsulateField.Plan(workspace.Context, Widget, workspace.Parse, field, options);
    }

    private static string Encapsulated(Workspace workspace, string name, EncapsulationOptions? options = null)
    {
        var (encapsulation, refusal) = Encapsulate(workspace, name, options);
        Assert.IsNull(refusal, refusal?.ToString());
        return DeclarationEdits.ApplyTo(workspace.Text, encapsulation!.Edits);
    }

    [TestMethod]
    [DataRow("Public count As Long", new[] { "Public Property Get count() As Long", "Friend Property Let count(ByVal value As Long)" },
        DisplayName = "a value: a Public Get, a Friend Let")]
    [DataRow("Public items As Collection", new[] { "Public Property Get items() As Collection", "Friend Property Set items(ByVal value As Collection)" },
        DisplayName = "an object: a Public Get, a Friend Set")]
    [DataRow("Public anything As Variant", new[]
        {
            "Public Property Get anything() As Variant", "Friend Property Let anything(ByVal value As Variant)",
            "Friend Property Set anything(ByVal value As Variant)",
        }, DisplayName = "a Variant: a Public Get, a Friend Let and Set")]
    public void AFieldOfAClassModule_IsEncapsulated_ByDefault_WithAPublicGet_AndFriendWriters(string field, string[] headers)
    {
        var text = Encapsulated(Compose(ClassWith(field), ModuleType.ClassModule), field.Split(' ')[1]);

        foreach (var header in headers)
        {
            StringAssert.Contains(text, header + "\r\n");
        }
    }

    [TestMethod]
    public void AFieldOfAStandardModule_IsEncapsulated_ByDefault_WithPublicProperties()
    {
        var text = Encapsulated(Compose(StandardWith("Public anything As Variant"), ModuleType.StdModule), "anything");

        StringAssert.Contains(text, "Public Property Get anything() As Variant\r\n");
        StringAssert.Contains(text, "Public Property Let anything(ByVal value As Variant)\r\n");
        StringAssert.Contains(text, "Public Property Set anything(ByVal value As Variant)\r\n");
        Assert.DoesNotContain("Friend", text);
    }

    [TestMethod]
    [DataRow(AccessModifier.Public, AccessModifier.Public, "Public", "Public")]
    [DataRow(AccessModifier.Friend, AccessModifier.Friend, "Friend", "Friend")]
    [DataRow(AccessModifier.Public, AccessModifier.Private, "Public", "Private")]
    [DataRow(AccessModifier.Friend, AccessModifier.Public, "Friend", "Public")]
    [DataRow(AccessModifier.Private, AccessModifier.Private, "Private", "Private")]
    public void TheAccessorsOfAClassModulesField_AreDeclaredAsChosen(AccessModifier read, AccessModifier write, string get, string writers)
    {
        var text = Encapsulated(Compose(ClassWith("Public anything As Variant"), ModuleType.ClassModule), "anything",
            new EncapsulationOptions(ReadAccess: read, WriteAccess: write));

        StringAssert.Contains(text, $"{get} Property Get anything() As Variant\r\n");
        StringAssert.Contains(text, $"{writers} Property Let anything(ByVal value As Variant)\r\n");
        StringAssert.Contains(text, $"{writers} Property Set anything(ByVal value As Variant)\r\n");
    }

    [TestMethod]
    public void AReadOnlyEncapsulation_WritesNoLetOrSet_WhateverTheirAccess()
    {
        var text = Encapsulated(Compose(ClassWith("Public count As Long"), ModuleType.ClassModule), "count",
            new EncapsulationOptions(ReadOnly: true, ReadAccess: AccessModifier.Friend, WriteAccess: AccessModifier.Public));

        StringAssert.Contains(text, "Friend Property Get count() As Long\r\n");
        Assert.DoesNotContain("Property Let", text);
    }

    [TestMethod]
    [DataRow(AccessModifier.Friend, null, DisplayName = "a Friend Get")]
    [DataRow(AccessModifier.Public, AccessModifier.Friend, DisplayName = "a Friend Let")]
    public void AFieldOfAStandardModule_WithAFriendProperty_IsRefused(AccessModifier read, AccessModifier? write)
    {
        var (encapsulation, refusal) = Encapsulate(Compose(StandardWith("Public count As Long"), ModuleType.StdModule), "count",
            new EncapsulationOptions(ReadAccess: read, WriteAccess: write));

        Assert.IsNull(encapsulation);
        Assert.AreEqual(EncapsulationRefusalKind.FriendInStandardModule, refusal!.Kind);
    }

    [TestMethod]
    [DataRow(AccessModifier.Private, AccessModifier.Public, "    Debug.Print w.count", "ReadOutside", DisplayName = "a Private Get, read outside")]
    [DataRow(AccessModifier.Public, AccessModifier.Private, "    w.count = 1", "AssignedOutside", DisplayName = "a Private Let, assigned outside")]
    public void APrivateProperty_ThatAReferenceOutsideTheModuleGoesThrough_IsRefused(AccessModifier read, AccessModifier write, string reference, string kind)
    {
        var user = ModuleWorkspace.StandardModule("User", "Option Explicit", "Public Sub Use()", "    Dim w As New Widget", reference, "End Sub");

        var (encapsulation, refusal) = Encapsulate(Compose(ClassWith("Public count As Long"), ModuleType.ClassModule, user), "count",
            new EncapsulationOptions(ReadAccess: read, WriteAccess: write));

        Assert.IsNull(encapsulation);
        Assert.AreEqual(Enum.Parse<EncapsulationRefusalKind>(kind), refusal!.Kind);
        Assert.AreEqual(User, refusal.Module);
    }

    [TestMethod]
    public void APrivateLet_ThatOnlyAReadOutsideTheModuleGoesAround_IsWritten()
    {
        var user = ModuleWorkspace.StandardModule(
            "User", "Option Explicit", "Public Sub Use()", "    Dim w As New Widget", "    Debug.Print w.count", "End Sub");

        var text = Encapsulated(Compose(ClassWith("Public count As Long"), ModuleType.ClassModule, user), "count",
            new EncapsulationOptions(WriteAccess: AccessModifier.Private));

        StringAssert.Contains(text, "Private Property Let count(ByVal value As Long)\r\n");
    }

    // the class encapsulated with its defaults, then run: another module of its project assigns the field through the Friend Let and reads it through the
    // Get.
    [TestMethod]
    public async Task TheFriendLetOfAClassModulesField_IsAssigned_ByAnotherModuleOfItsProject_AfterTheEdit()
    {
        var text = Encapsulated(Compose(ClassWith("Public count As Long"), ModuleType.ClassModule), "count");
        StringAssert.Contains(text, "Friend Property Let count(");

        var printed = await ModuleWorkspace.RunAsync([("Widget", ClassHeader + text)], ModuleWorkspace.StandardModule(
            "Program", "Option Explicit", "Public Sub Main()", "    Dim w As New Widget", "    w.count = 5", "    Debug.Print w.count", "End Sub"));

        CollectionAssert.AreEqual(new[] { "5" }, printed);
    }
}
