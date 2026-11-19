using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// What the code outside a module sees of its members (<strong>MS-VBAL §5.2.3</strong>, <strong>MS-VBAL §5.6.10</strong>): the other modules of its project
/// see a member that is not private to the module, another project what the module makes accessible to it, and code of no known project nothing.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3 Module Declarations")]
public sealed class ProjectVisibilityTests
{
    private static readonly Uri Root = new("file:///c:/ws/NavalLib/");
    private static readonly Uri ReferencingRoot = new("file:///c:/ws/NavalConsole/");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly VBStandardModuleSymbol Rules = new(Root, Root, "GameRules");

    private static VBModuleFieldVariableMemberSymbol Field(AccessModifier access = AccessModifier.Implicit)
        => new(Root, Rules.Uri, "Score", ScopeKind.Module, VBLongType.TypeInfo, R, R, access);

    private static VBProcedureMemberSymbol Procedure(AccessModifier access = AccessModifier.Implicit)
        => new(Root, Rules.Uri, "PlaceShip", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);

    private static VBEnumMemberSymbol Enum(AccessModifier access = AccessModifier.Implicit)
        => new(Root, Rules.Uri, "ShipKind", ScopeKind.Module, SymbolKindExt.Enum, VBUnknownType.TypeInfo, R, R, access);

    [TestMethod]
    public void AMemberDeclaredPublicGlobalOrFriend_IsProjectVisible_AndOneDeclaredPrivateIsNot()
    {
        Assert.IsTrue(ProjectVisibility.Instance.IsProjectVisible(Field(AccessModifier.Public), Rules));
        Assert.IsTrue(ProjectVisibility.Instance.IsProjectVisible(Field(AccessModifier.Global), Rules));
        Assert.IsTrue(ProjectVisibility.Instance.IsProjectVisible(Procedure(AccessModifier.Friend), Rules));
        Assert.IsFalse(ProjectVisibility.Instance.IsProjectVisible(Procedure(AccessModifier.Private), Rules));
    }

    [TestMethod]
    public void AMemberDeclaredWithNoAccessModifier_IsProjectVisible_ExceptAModuleVariableOrAConstant()
    {
        var constant = new VBConstantMemberSymbol(Root, Rules.Uri, "GridSize", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);

        Assert.IsTrue(ProjectVisibility.Instance.IsProjectVisible(Procedure(), Rules));
        Assert.IsTrue(ProjectVisibility.Instance.IsProjectVisible(Enum(), Rules));
        Assert.IsFalse(ProjectVisibility.Instance.IsProjectVisible(Field(), Rules));
        Assert.IsFalse(ProjectVisibility.Instance.IsProjectVisible(constant, Rules));
    }

    [TestMethod]
    public void ASymbolWithNoAccessModifier_IsNotProjectVisible()
    {
        var procedure = Procedure();
        var local = new VBLocalVariableSymbol(Root, procedure.Uri, "temp", ScopeKind.Local, R, R, DeclaredBy: LocalDeclarationKind.Dim);

        Assert.IsFalse(ProjectVisibility.Instance.IsProjectVisible(Rules, Rules));
        Assert.IsFalse(ProjectVisibility.Instance.IsProjectVisible(local, Rules));
    }

    [TestMethod]
    public void AFriendMember_IsVisibleFromItsOwnProject_AndNotFromAnotherOne()
    {
        var friend = Procedure(AccessModifier.Friend);

        Assert.IsTrue(ProjectVisibility.Instance.IsVisibleFrom(friend, Rules, Root));
        Assert.IsFalse(ProjectVisibility.Instance.IsVisibleFrom(friend, Rules, ReferencingRoot));
        Assert.IsTrue(ProjectVisibility.Instance.IsVisibleFrom(Procedure(AccessModifier.Public), Rules, ReferencingRoot));
    }

    [TestMethod]
    public void AMemberOfAPrivateModule_IsVisibleFromItsOwnProject_AndNotFromAnotherOne()
    {
        var hidden = Rules with { Directives = new ModuleDirectives(PrivateModule: true) };

        Assert.IsTrue(ProjectVisibility.Instance.IsVisibleFrom(Procedure(AccessModifier.Public), hidden, Root));
        Assert.IsFalse(ProjectVisibility.Instance.IsVisibleFrom(Procedure(AccessModifier.Public), hidden, ReferencingRoot));
    }

    [TestMethod]
    public void CodeOfNoKnownProject_SeesNoMember()
    {
        Assert.IsFalse(ProjectVisibility.Instance.IsVisibleFrom(Procedure(AccessModifier.Public), Rules, null));
        Assert.IsFalse(ProjectVisibility.Instance.IsVisibleFrom(Enum(AccessModifier.Public), Rules, null));
    }
}
