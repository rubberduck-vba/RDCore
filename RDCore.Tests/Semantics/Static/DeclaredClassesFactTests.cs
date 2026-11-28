using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// What the facts of a parameter, a function or a property say of the classes of the workspace its declared type names
/// (<see cref="DeclarationFact.DeclaredClasses"/>): each class, and the members of its public interface, as the binding has them.
/// </summary>
[TestClass]
public sealed class DeclaredClassesFactTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Order", ModuleWorkspace.ClassModule("Order",
            "Public Id As Long",
            "Private mCustomer As Customer",
            "Dim mHidden As Long",
            "Public Event Changed()",
            "Public Property Get Customer() As Customer",
            "Set Customer = mCustomer",
            "End Property",
            "Public Property Set Customer(ByVal value As Customer)",
            "Set mCustomer = value",
            "End Property",
            "Public Property Let WriteOnly(ByVal value As Long)",
            "End Property",
            "Public Property Get Line(ByVal index As Long) As Long",
            "End Property",
            "Friend Property Get Internal() As Long",
            "End Property",
            "Public Function Method() As Long",
            "End Function",
            "Sub Act()",
            "End Sub")),
        ("Customer", ModuleWorkspace.ClassModule("Customer",
            "Public Name As String",
            "Private mOrders() As Order",
            "Public Lookup As Collection",
            "Public Property Get Orders() As Order()",
            "Orders = mOrders",
            "End Property")),
    ];

    private const string Program = """
        Attribute VB_Name = "Program"
        Option Explicit
        Private mOrder As Order
        Public Sub Main()
            Dim local1 As Order
            Set local1 = mOrder
        End Sub
        Public Function Post(ByVal body As Order) As Customer
        End Function
        Public Property Let Current(ByVal value As Order)
        End Property
        Public Property Get Current() As Order
        End Property
        Public Sub Take(ByRef items() As Customer, ByVal count As Long, ByVal anything As Object)
        End Sub
        """;

    private static async Task<ImmutableDictionary<string, DeclarationFact>> DeclarationsAsync()
    {
        var model = await ModuleWorkspace.ModelAsync(Classes, Program.ReplaceLineEndings("\r\n") + "\r\n");

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        return model.Declarations.ToImmutableDictionary(declaration => declaration.Name, StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    [DataRow("body", "Order,Customer", DisplayName = "a parameter, then the classes its members name")]
    [DataRow("Post", "Customer,Order", DisplayName = "what a function returns")]
    [DataRow("Current", "Order,Customer", DisplayName = "a property declared Let first, by its Get")]
    [DataRow("items", "Customer,Order", DisplayName = "an array parameter, by its elements")]
    public async Task ADeclarationOfAClassOfTheWorkspace_StatesTheClassesItsTypeNames(string name, string classes)
    {
        var declarations = await DeclarationsAsync();

        Assert.IsTrue(declarations.TryGetValue(name, out var declaration), string.Join(", ", declarations.Keys));
        CollectionAssert.AreEqual(classes.Split(','), declaration.DeclaredClasses.Select(declared => declared.Name).ToArray());
    }

    [TestMethod]
    [DataRow("count", DisplayName = "a parameter of an intrinsic type")]
    [DataRow("anything", DisplayName = "a parameter As Object")]
    [DataRow("local1", DisplayName = "a local")]
    [DataRow("mOrder", DisplayName = "a variable of the module")]
    public async Task AnyOtherDeclaration_StatesNone(string name)
    {
        var declarations = await DeclarationsAsync();

        Assert.IsTrue(declarations.TryGetValue(name, out var declaration), string.Join(", ", declarations.Keys));
        Assert.IsTrue(declaration.DeclaredClasses.IsDefaultOrEmpty);
    }

    [TestMethod]
    public async Task TheMembersOfAClass_AreItsPublicInterface_AsTheBindingHasIt()
    {
        var order = (await DeclarationsAsync())["body"].DeclaredClasses[0];

        CollectionAssert.AreEqual(
            new[]
            {
                "Id Variable None 0 Intrinsic Long",
                "Customer Property Get, Set 0 Class Customer",
                "WriteOnly Property Let 0 Intrinsic Long",
                "Line Property Get 1 Intrinsic Long",
                "Method Function None 0 Intrinsic Long",
                "Act Subroutine None 0  ",
            },
            order.Members.Select(member => $"{member.Name} {member.Classification} {member.Accessors} {member.Parameters} {member.TypeKind} {member.TypeName}")
                .ToArray());
    }

    [TestMethod]
    public async Task AMemberOfAClass_NamesTheModuleOrTheLibraryOfItsType()
    {
        var declared = (await DeclarationsAsync())["body"].DeclaredClasses;
        var customer = declared[1];
        var orders = customer.Members.Single(member => member.Name == "Orders");
        var lookup = customer.Members.Single(member => member.Name == "Lookup");

        Assert.IsTrue(orders.IsArray);
        Assert.AreEqual(declared[0].Class.AbsoluteUri, orders.TypeClass?.AbsoluteUri);
        Assert.AreEqual("VBA", lookup.TypeLibrary);
        Assert.IsNull(lookup.TypeClass);
    }
}
