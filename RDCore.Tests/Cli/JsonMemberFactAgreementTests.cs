using RDCore.Runtime.Execution.Hosting.Json;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics;

namespace RDCore.Tests.Cli;

/// <summary>
/// The members a JSON object of a class has, and those its keys can give a value to, as <c>JsonValues</c> selects them, are the members that the facts of a
/// declaration of the class state (<see cref="DeclarationFact.DeclaredClasses"/>) and an analyzer reads: a variable, or a property that takes no argument,
/// read through its <c>Get</c> and given a value through its <c>Let</c> or <c>Set</c>. A change to either side that the other does not follow fails here.
/// </summary>
[TestClass]
public sealed class JsonMemberFactAgreementTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Order", ModuleWorkspace.ClassModule("Order",
            "Public Id As Long",
            "Private mCustomer As Customer",
            "Dim mHidden As Long",
            "Public Tags As Variant",
            "Public Property Get Customer() As Customer",
            "Set Customer = mCustomer",
            "End Property",
            "Public Property Set Customer(ByVal value As Customer)",
            "Set mCustomer = value",
            "End Property",
            "Public Property Get Total() As Currency",
            "End Property",
            "Public Property Let WriteOnly(ByVal value As Long)",
            "End Property",
            "Public Property Get Line(ByVal index As Long) As Long",
            "End Property",
            "Public Property Get Pick(Optional ByVal index As Long) As Long",
            "End Property",
            "Public Property Let Slot(ByVal index As Long, ByVal value As Long)",
            "End Property",
            "Property Get Nick() As String",
            "End Property",
            "Property Let Nick(ByVal value As String)",
            "End Property",
            "Friend Property Get Internal() As Long",
            "End Property",
            "Private Property Get Concealed() As Long",
            "End Property",
            "Public Function Method() As Long",
            "End Function",
            "Public Sub Act()",
            "End Sub")),
        ("Customer", ModuleWorkspace.ClassModule("Customer",
            "Public Name As String",
            "Private mOrders() As Order",
            "Public Property Get Orders() As Order()",
            "Orders = mOrders",
            "End Property",
            "Public Property Get Order() As Order",
            "End Property",
            "Public Property Let Order(ByVal value As Order)",
            "End Property")),
    ];

    private static readonly string Program = ModuleWorkspace.StandardModule("Program",
        "Option Explicit",
        "Public Sub Main()",
        "End Sub",
        "Public Function Post(ByVal body As Order) As Order",
        "End Function");

    // the round-trip rule (RDW00003): a member is written when it is a variable, or a property whose Get takes no argument.
    private static bool IsWritten(ClassMemberFact member)
        => member.Classification is ExpressionClassification.Variable || member.Accessors.HasFlag(PropertyAccessors.Get) && member.Parameters == 0;

    // the round-trip rule (RDW00003): a member is given a value when it is a variable, or a property whose Let or Set takes no argument besides its value.
    private static bool IsGivenAValue(ClassMemberFact member)
        => member.Classification is ExpressionClassification.Variable
            || (member.Accessors & (PropertyAccessors.Let | PropertyAccessors.Set)) != 0 && member.Parameters == 0;

    [TestMethod]
    public async Task EachClassABodyParameterNames_HasTheMembersJsonValuesSelects()
    {
        var compared = 0;
        await ModuleWorkspace.InspectAsync(Classes, Program, async (provider, _, _) =>
        {
            await provider.Image.Semantics.WhenAllEvaluatedAsync();
            var model = provider.Image.Semantics.All.Single(candidate => SymbolUri.ModuleNameOf(candidate.Module) == "Program");
            var body = model.Declarations.Single(declaration => declaration.Name == "body");

            foreach (var declared in body.DeclaredClasses)
            {
                Assert.IsTrue(provider.Session.Symbols.TryGetModule(declared.Class, out var module), declared.Name);
                var classModule = (VBClassModuleSymbol)module;

                CollectionAssert.AreEqual(
                    JsonValues.JsonMembersOf(classModule).Select(member => member.Name).ToArray(),
                    declared.Members.Where(IsWritten).Select(member => member.Name).ToArray(),
                    $"the members of a JSON object of {declared.Name}");
                CollectionAssert.AreEquivalent(
                    classModule.DefaultInterfaceMembers.Select(member => member.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                        .Where(name => JsonValues.SetterOf(classModule, name, null) is not null).ToArray(),
                    declared.Members.Where(IsGivenAValue).Select(member => member.Name).ToArray(),
                    $"the members a key gives a value to, of {declared.Name}");
                compared++;
            }
        });

        // both classes, or the comparison proved nothing.
        Assert.AreEqual(Classes.Length, compared);
    }
}
