using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics;

/// <summary>
/// What the static pass states of the property an expression names, and of the default member of the class an expression is of (MS-VBAL 5.3.1.7, 5.6.2.3):
/// the accessors the property declares, the type its Property Let is given, and the default member's name, kind, accessors and required parameters.
/// </summary>
[TestClass]
public sealed class MemberFactsTests
{
    private static readonly (string Name, string Source) Widget = ("Widget", ModuleWorkspace.ClassModule("Widget", "Option Explicit",
        "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "End Property",
        "Public Property Get Owner() As Object", "End Property",
        "Public Property Let Owner(ByVal o As Object)", "End Property",
        "Public Property Set Owner(ByVal o As Object)", "End Property"));

    private static async Task<ExpressionFactDto[]> FactsAsync(params string[] body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n"
            + "Public Property Get Total() As Variant\r\nEnd Property\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([Widget], program);
        return [.. payload.Modules.Single(module => module.Module.Fragment.TrimStart('#') == "Program").Procedures.SelectMany(procedure => procedure.Expressions)];
    }

    [TestMethod]
    public async Task AVariableOfAClass_HasItsClasssDefaultMember()
    {
        var facts = await FactsAsync("Dim w As Widget", "Debug.Print w");

        var member = facts.Select(fact => fact.DefaultMember).OfType<DefaultMemberFact>().First();
        Assert.AreEqual(new DefaultMemberFact("Value", ExpressionClassification.Property, PropertyAccessors.Get, 0), member);
    }

    [TestMethod]
    public async Task AMemberOfAnObject_HasTheAccessorsItsClassDeclares_AndTheTypeItsLetIsGiven()
    {
        var facts = await FactsAsync("Dim w As Widget", "Debug.Print w.Owner Is Nothing");

        var owner = facts.Single(fact => fact.Accessors != PropertyAccessors.None);
        Assert.AreEqual(PropertyAccessors.Get | PropertyAccessors.Let | PropertyAccessors.Set, owner.Accessors);
        Assert.AreEqual("Object", owner.LetValueType);
    }

    [TestMethod]
    public async Task APropertyOfTheModule_HasTheAccessorsTheModuleDeclares()
    {
        var facts = await FactsAsync("Debug.Print Total");

        var total = facts.Single(fact => fact.Accessors != PropertyAccessors.None);
        Assert.AreEqual(PropertyAccessors.Get, total.Accessors);
        Assert.IsNull(total.LetValueType);
    }

    [TestMethod]
    public async Task AVariableOfNoClass_HasNoDefaultMember_NorAccessors()
    {
        var facts = await FactsAsync("Dim n As Long", "Debug.Print n");

        Assert.IsTrue(facts.All(fact => fact.DefaultMember is null && fact.Accessors == PropertyAccessors.None));
    }
}
