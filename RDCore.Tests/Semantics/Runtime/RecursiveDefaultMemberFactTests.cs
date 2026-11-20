using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// What the facts of the code say of an object read through more than one default member: one that returns an object, whose default member is read in
/// turn (<strong>MS-VBAL §5.6.2.3</strong>, <strong>§5.6.13</strong>, <strong>§5.6.14</strong>).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class RecursiveDefaultMemberFactTests
{
    private static readonly (string, string)[] Classes =
    [
        ("Leaf", ModuleWorkspace.ClassModule("Leaf",
            "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = 7", "End Property")),
        ("Indexed", ModuleWorkspace.ClassModule("Indexed",
            "Public Function Item(ByVal Key As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = 10", "End Function")),
        ("ViaVariant", ModuleWorkspace.ClassModule("ViaVariant",
            "Public Property Get Child() As Variant", "Attribute Child.VB_UserMemId = 0", "Set Child = New Leaf", "End Property")),
        ("ToIndexed", ModuleWorkspace.ClassModule("ToIndexed",
            "Public Property Get Child() As Indexed", "Attribute Child.VB_UserMemId = 0", "Set Child = New Indexed", "End Property")),
        ("ToArray", ModuleWorkspace.ClassModule("ToArray",
            "Public Property Get Items() As Long()", "Attribute Items.VB_UserMemId = 0", "Dim a(2) As Long", "Items = a", "End Property")),
    ];

    private static async Task<RuntimeFacts> FactsOfAsync(params string[] body)
    {
        var model = await ModuleWorkspace.ModelAsync(Classes,
            $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n");

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        var facts = model.Procedures.Single().Runtime;
        Assert.IsNotNull(facts, "the module was loaded, so its code was evaluated");
        return facts;
    }

    private static string Describe(RuntimeFacts facts)
        => string.Join("; ", facts.Operations.OfType<MemberAccessFact>().Select(fact => $"{fact.Operator} {fact.Member} {fact.Flags}")
            .Concat(facts.Conversions.Select(fact => $"{fact.Source.Name}->{fact.Destination.Name} {fact.Flags}")));

    [TestMethod]
    [DataRow("Dim t As ToIndexed", "Set t = New ToIndexed", "Debug.Print t(1)", true, DisplayName = "an index, through a default member that returns an object")]
    [DataRow("Dim t As New ToIndexed", "", "Debug.Print t(1)", true, DisplayName = "an index of an object the analysis assumes, by its class")]
    [DataRow("Dim d As Indexed", "Set d = New Indexed", "Debug.Print d(1)", false, DisplayName = "an index that the default member takes")]
    [DataRow("Dim d As New Indexed", "", "Debug.Print d(1)", false, DisplayName = "an index that the default member of an assumed object takes")]
    [DataRow("Dim r As ToArray", "Set r = New ToArray", "Debug.Print r(1)", false, DisplayName = "an index of the array a default member returns")]
    public async Task AnIndexedObject_IsRecursiveWhenItsDefaultMemberReturnsAnObject(string declaration, string assignment, string statement, bool isRecursive)
    {
        var facts = await FactsOfAsync(declaration, assignment, statement);

        var accesses = facts.Operations.OfType<MemberAccessFact>()
            .Where(fact => fact.Flags.HasFlag(MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember)).ToList();
        Assert.HasCount(1, accesses, Describe(facts));
        Assert.AreEqual(isRecursive, accesses[0].Flags.HasFlag(MemberAccessOperationSemanticFlags.Recursive), Describe(facts));
    }

    [TestMethod]
    [DataRow("Dim t As ToIndexed", "Set t = New ToIndexed", "Debug.Print t!Name", true, DisplayName = "a dictionary access, through a default member that returns an object")]
    [DataRow("Dim d As Indexed", "Set d = New Indexed", "Debug.Print d!Name", false, DisplayName = "a dictionary access that the default member takes")]
    public async Task ADictionaryAccess_IsRecursiveWhenTheDefaultMemberReturnsAnObject(string declaration, string assignment, string statement, bool isRecursive)
    {
        var facts = await FactsOfAsync(declaration, assignment, statement);

        var accesses = facts.Operations.OfType<MemberAccessFact>()
            .Where(fact => fact.Flags.HasFlag(MemberAccessOperationSemanticFlags.DictionaryAccess)).ToList();
        Assert.HasCount(1, accesses, Describe(facts));
        Assert.AreEqual(isRecursive, accesses[0].Flags.HasFlag(MemberAccessOperationSemanticFlags.Recursive), Describe(facts));
    }

    // MS-VBA refuses to read a value through a default member declared to return a class or Object, which MS-VBAL §5.6.2.3 lets it do: what is left is one
    // that returns a Variant, which may hold an object. The analysis runs no procedure it calls, and what the default member returns is not known: whether the
    // value is read through a second default member is not a fact of it.
    [TestMethod]
    [DataRow("n = New ViaVariant", DisplayName = "a value let-assigned")]
    public async Task AnObjectConvertedToItsValue_IsNotKnownToBeReadThroughASecondDefaultMember(string statement)
    {
        var facts = await FactsOfAsync("Dim n As Long", statement);

        var conversions = facts.Conversions.Where(fact => fact.Flags.HasFlag(ConversionSemanticFlags.ObjectOperand)).ToList();
        Assert.IsNotEmpty(conversions, Describe(facts));
        Assert.IsFalse(conversions.Any(fact => fact.Flags.HasFlag(ConversionSemanticFlags.Recursive)), Describe(facts));
    }
}
