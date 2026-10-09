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
