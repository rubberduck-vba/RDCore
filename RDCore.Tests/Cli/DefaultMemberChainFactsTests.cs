using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Cli;

/// <summary>
/// The host states the default members an argument list or the name of a dictionary access given to an object goes through (MS-VBAL 5.6.13, 5.6.14),
/// in the order they are called, from the class the object is declared as: the code that names them all is what the access is short for.
/// </summary>
[TestClass]
public sealed class DefaultMemberChainFactsTests
{
    // Root's default member returns a Branch, whose default member returns a Leaf, whose default member takes the key.
    private static readonly (string, string)[] Classes =
    [
        ("Leaf", ModuleWorkspace.ClassModule("Leaf", "Option Explicit",
            "Public Property Get Item(ByVal key As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = 1", "End Property")),
        ("Branch", ModuleWorkspace.ClassModule("Branch", "Option Explicit",
            "Public Function Child() As Leaf", "Attribute Child.VB_UserMemId = 0", "Set Child = New Leaf", "End Function")),
        ("Root", ModuleWorkspace.ClassModule("Root", "Option Explicit",
            "Public Property Get Inner() As Branch", "Attribute Inner.VB_UserMemId = 0", "Set Inner = New Branch", "End Property")),
    ];

    private const MemberAccessOperationSemanticFlags Implied = MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember;

    // the access of a default member the code does not name, given an argument list or a name, in Main, whose body is `body`.
    private static async Task<OperatorFactDto> AccessAsync(string body)
    {
        var program = ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. body.Split('|'), "End Sub"]);
        var payload = await ModuleWorkspace.SemanticsAsync(Classes, program);
        var model = payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program");
        return model.Procedures.SelectMany(procedure => procedure.Runtime?.Operations ?? [])
            .First(operation => operation.MemberAccessOpSemanticFlags is { } flags && (flags & Implied) == Implied
                && (flags & (MemberAccessOperationSemanticFlags.ArgumentList | MemberAccessOperationSemanticFlags.DictionaryAccess)) != 0);
    }

    [TestMethod]
    [DataRow("Dim t As New Branch|Dim x As Long|x = t(1)", new[] { "Child", "Item" }, DisplayName = "an argument list given through two default members")]
    [DataRow("Dim t As New Branch|Dim x As Long|x = t!Name", new[] { "Child", "Item" }, DisplayName = "a dictionary access through two default members")]
    [DataRow("Dim d As New Root|Dim x As Long|x = d!Name", new[] { "Inner", "Child", "Item" }, DisplayName = "a dictionary access three levels deep")]
    [DataRow("Dim d As New Root|Dim x As Long|x = d(\"Name\")", new[] { "Inner", "Child", "Item" }, DisplayName = "an argument list three levels deep")]
    [DataRow("Dim l As New Leaf|Dim x As Long|x = l(1)", new[] { "Item" }, DisplayName = "a default member that takes the argument")]
    public async Task TheDefaultMembersAnAccessGoesThrough_AreStatedInTheOrderTheyAreCalled(string body, string[] expected)
    {
        var access = await AccessAsync(body);

        Assert.IsNotNull(access.DefaultMembers, $"{access.Member} {access.MemberAccessOpSemanticFlags}");
        CollectionAssert.AreEqual(expected, access.DefaultMembers.Value.Select(member => member.Name).ToArray());
        Assert.AreEqual(expected[0], access.Member, "the first is the member the access states");
    }

    [TestMethod]
    public async Task EachDefaultMember_SaysWhatItIs_AndTheArgumentsItRequires()
    {
        var access = await AccessAsync("Dim d As New Root|Dim x As Long|x = d!Name");

        CollectionAssert.AreEqual(
            new[]
            {
                new DefaultMemberFact("Inner", ExpressionClassification.Property, PropertyAccessors.Get, 0),
                new DefaultMemberFact("Child", ExpressionClassification.Function, PropertyAccessors.None, 0),
                new DefaultMemberFact("Item", ExpressionClassification.Property, PropertyAccessors.Get, 1),
            },
            access.DefaultMembers!.Value.ToArray());
    }

    [TestMethod]
    [DataRow("Dim o As Object|Dim x As Long|Set o = New Branch|x = o(1)", DisplayName = "an object declared As Object, whose class the code does not say")]
    [DataRow("Dim v As Variant|Dim x As Long|Set v = New Root|x = v!Name", DisplayName = "a Variant")]
    public async Task AnAccessOfAnObjectNotDeclaredAsAClass_StatesNoDefaultMembers(string body)
        => Assert.IsNull((await AccessAsync(body)).DefaultMembers);
}
