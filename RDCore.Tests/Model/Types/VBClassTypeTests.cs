using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;

namespace RDCore.Tests.Model.Types;

/// <summary>
/// Characterization matrix for <see cref="VBClassType.FromClassModule"/> — a class's own default
/// interface is its <c>Public</c> (and implicitly-public) members only; <c>Private</c>/<c>Friend</c>
/// members are never reached through a member-access expression typed against the class itself.
/// </summary>
[TestClass]
public sealed class VBClassTypeTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBClassModuleSymbol ClassModule() => new(Root, Root, "Widget");

    private static VBInstanceFieldVariableMemberSymbol Field(Uri moduleUri, string name, AccessModifier access)
        => new(Root, moduleUri, name, R, R, VBLongType.TypeInfo, access);

    [TestMethod]
    public void ExcludesPrivateAndFriendMembers()
    {
        var classModule = ClassModule();
        var populated = classModule with
        {
            Members =
            [
                Field(classModule.Uri, "PublicField", AccessModifier.Public),
                Field(classModule.Uri, "ImplicitField", AccessModifier.Implicit),
                Field(classModule.Uri, "PrivateField", AccessModifier.Private),
                Field(classModule.Uri, "FriendField", AccessModifier.Friend),
            ],
        };

        var classType = VBClassType.FromClassModule(populated);

        Assert.HasCount(2, classType.Members);
        Assert.IsTrue(classType.Members.Any(member => member.Name == "PublicField"));
        Assert.IsTrue(classType.Members.Any(member => member.Name == "ImplicitField"));
    }

    [TestMethod]
    public void NoPublicMembers_YieldsAnEmptyInterface()
    {
        var classModule = ClassModule();
        var populated = classModule with { Members = [Field(classModule.Uri, "PrivateField", AccessModifier.Private)] };

        var classType = VBClassType.FromClassModule(populated);

        Assert.IsEmpty(classType.Members);
    }
}
