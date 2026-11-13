using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The environment host states what a member of a class module is there for - an implementation of an interface member, or a handler of an event - and the language
/// a module is loaded as, which are what an analyzer decides what to say of the member and of the syntax by.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL 5.0.3 Semantic Analysis")]
public sealed class HostDeclarationRoleTests
{
    private static string Program(params string[] lines)
        => $"Attribute VB_Name = \"Program\"\r\n{string.Join("\r\n", lines)}\r\n";

    private static readonly (string, string) Shape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Public Sub Draw()", "End Sub", "Public Function Area() As Long", "End Function"));
    private static readonly (string, string) Button = ("Button", ModuleWorkspace.ClassModule("Button", "Public Event Click()"));

    private static async Task<ModuleSemanticsDto?> ModelOfAsync(string name, params (string Name, string Source)[] classes)
    {
        var payload = await ModuleWorkspace.SemanticsAsync([Shape, Button, .. classes], Program("Public Sub Main()", "End Sub"));
        return payload.Modules.SingleOrDefault(module => module.Module.Fragment.TrimStart('#') == name);
    }

    private static DeclarationRole RoleOf(ModuleSemanticsDto model, string member)
        => model.Declarations.First(declaration => declaration.Name == member).Role;

    private static string Implementer(params string[] lines)
        => ModuleWorkspace.ClassModule("Implementer", ["Implements IShape", "Private WithEvents Source As Button", .. lines]);

    [TestMethod]
    public async Task AMemberNamedAfterAnInterfaceMember_ImplementsIt_WhateverItsAccess()
    {
        var model = await ModelOfAsync("Implementer", ("Implementer", Implementer(
            "Public Sub IShape_Draw()", "End Sub",
            "Private Function IShape_Area() As Long", "End Function")));

        Assert.IsNotNull(model);
        Assert.AreEqual(DeclarationRole.InterfaceImplementation, RoleOf(model, "IShape_Draw"));
        Assert.AreEqual(DeclarationRole.InterfaceImplementation, RoleOf(model, "IShape_Area"));
        Assert.AreEqual(RDCore.SDK.Model.AccessModifier.Public, model.Declarations.First(declaration => declaration.Name == "IShape_Draw").Access);
    }

    [TestMethod]
    public async Task ThePropertyThatImplementsAnInterfaceMember_ImplementsIt_FromItsGetToo()
    {
        // a property's declaration is its first accessor, here a Get: of a property of the interface, and of a variable of it.
        var settings = ("ISettings", ModuleWorkspace.ClassModule("ISettings", "Public Label As String",
            "Public Property Get Tag() As Variant", "End Property", "Public Property Let Tag(ByVal value As Variant)", "End Property"));
        var model = await ModelOfAsync("Settings", settings, ("Settings", ModuleWorkspace.ClassModule("Settings", "Implements ISettings",
            "Private Property Get ISettings_Label() As String", "End Property", "Private Property Let ISettings_Label(ByVal value As String)", "End Property",
            "Private Property Get ISettings_Tag() As Variant", "End Property", "Private Property Let ISettings_Tag(ByVal value As Variant)", "End Property")));

        Assert.IsNotNull(model);
        Assert.AreEqual(DeclarationRole.InterfaceImplementation, RoleOf(model, "ISettings_Label"));
        Assert.AreEqual(DeclarationRole.InterfaceImplementation, RoleOf(model, "ISettings_Tag"));
    }

    [TestMethod]
    public async Task AMemberNamedAfterAnEventOfAWithEventsVariable_HandlesIt()
    {
        var model = await ModelOfAsync("Implementer", ("Implementer", Implementer("Public Sub Source_Click()", "End Sub")));

        Assert.AreEqual(DeclarationRole.EventHandler, RoleOf(model!, "Source_Click"));
    }

    [TestMethod]
    public async Task AMemberThatOnlyBeginsLikeOne_IsAMemberLikeAnyOther()
    {
        var model = await ModelOfAsync("Implementer", ("Implementer", Implementer(
            "Public Sub IShape_Helper()", "End Sub",
            "Public Sub Source_Other()", "End Sub",
            "Public Sub Other_Draw()", "End Sub")));

        Assert.AreEqual(DeclarationRole.None, RoleOf(model!, "IShape_Helper"), "the interface has no such member");
        Assert.AreEqual(DeclarationRole.None, RoleOf(model!, "Source_Other"), "the event source has no such event");
        Assert.AreEqual(DeclarationRole.None, RoleOf(model!, "Other_Draw"));
    }

    // MS-VBAL §5.3.1.10: a subroutine with no parameters, named Class_Initialize or Class_Terminate, of a class module.
    [TestMethod]
    [DataRow("Public Sub Class_Initialize()", "Class_Initialize", DeclarationRole.EventHandler, DisplayName = "Initialize, Public")]
    [DataRow("Private Sub Class_Terminate()", "Class_Terminate", DeclarationRole.EventHandler, DisplayName = "Terminate, Private")]
    [DataRow("Public Sub class_initialize()", "class_initialize", DeclarationRole.EventHandler, DisplayName = "in any case")]
    [DataRow("Public Sub Class_Initialize(ByVal n As Long)", "Class_Initialize", DeclarationRole.None, DisplayName = "with a parameter")]
    [DataRow("Public Function Class_Initialize() As Long", "Class_Initialize", DeclarationRole.None, DisplayName = "a function")]
    [DataRow("Public Sub Class_Activate()", "Class_Activate", DeclarationRole.None, DisplayName = "no event of the lifecycle")]
    public async Task ALifecycleHandlerOfAClass_HandlesItsEvent(string declaration, string name, DeclarationRole expected)
    {
        var model = await ModelOfAsync("Implementer", ("Implementer", Implementer(declaration, declaration.Contains("Function") ? "End Function" : "End Sub")));

        Assert.AreEqual(expected, RoleOf(model!, name));
    }

    [TestMethod]
    public async Task ALifecycleHandlerOfAStandardModule_IsAProcedureLikeAnyOther()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Program("Public Sub Class_Initialize()", "End Sub"));

        Assert.AreEqual(DeclarationRole.None, RoleOf(payload.Modules.Single(), "Class_Initialize"));
    }

    // what MS-VBA's host calls when its document raises an event, which RD-VBA analyzes and does not host.
    [TestMethod]
    [DataRow("GameSheet", DocClassType.ExcelWorksheet, "Public Sub Worksheet_Change(ByVal Target As Range)", "Worksheet_Change", DeclarationRole.EventHandler,
        DisplayName = "a worksheet's")]
    [DataRow("ThisWorkbook", DocClassType.ExcelWorkbook, "Public Sub Workbook_Open()", "Workbook_Open", DeclarationRole.EventHandler, DisplayName = "a workbook's")]
    [DataRow("GameSheet", DocClassType.ExcelWorksheet, "Public Sub Worksheet_Foo()", "Worksheet_Foo", DeclarationRole.None,
        DisplayName = "no event of the host's class")]
    [DataRow("GameSheet", DocClassType.ExcelWorksheet, "Public Sub Workbook_Open()", "Workbook_Open", DeclarationRole.None,
        DisplayName = "an event of another class")]
    public async Task AHandlerOfAnEventOfADocumentsHost_HandlesIt(string module, DocClassType super, string declaration, string name, DeclarationRole expected)
    {
        var payload = await ModuleWorkspace.SemanticsAsync(
            [], Program("Public Sub Main()", "End Sub"), libraries: DocumentModuleTests.Excel, documents: [(module, $"Option Explicit\r\n{declaration}\r\nEnd Sub\r\n", super)]);

        Assert.AreEqual(expected, RoleOf(payload.Modules.Single(candidate => SDK.Model.Symbols.SymbolUri.ModuleNameOf(candidate.Module) == module), name));
    }

    [TestMethod]
    public async Task AModuleThatImplementsNothing_HasNoMemberWithARole()
    {
        var payload = await ModuleWorkspace.SemanticsAsync([Shape, Button], Program("Public Sub IShape_Draw()", "End Sub"));

        Assert.IsTrue(payload.Modules.SelectMany(module => module.Declarations).All(declaration => declaration.Role == DeclarationRole.None));
    }

    [TestMethod]
    [DataRow("vba", "vba")]
    [DataRow("vb6", "vb6")]
    [DataRow("basic", "basic")]
    public async Task TheLanguageTheModuleIsLoadedAs_IsAFactOfTheModule(string language, string expected)
    {
        var payload = await ModuleWorkspace.SemanticsAsync([], Program("Public Sub Main()", "End Sub"), language: SupportedLanguages.Get(language));

        Assert.AreEqual(expected, payload.Modules.Single().Language);
    }

    [TestMethod]
    public void OnlyABasic_IsWrittenInClassicBasicSyntax()
    {
        Assert.IsTrue(SupportedLanguages.BASIC.UsesClassicBasicSyntax);
        Assert.IsFalse(SupportedLanguages.RDVBA.UsesClassicBasicSyntax);
        Assert.IsFalse(SupportedLanguages.VB6.UsesClassicBasicSyntax);
    }
}
