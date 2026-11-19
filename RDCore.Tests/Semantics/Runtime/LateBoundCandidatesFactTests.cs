using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The members a late-bound member access may reach when it runs (<see cref="MemberAccessFact.LateBoundCandidates"/>, MS-VBAL §5.6.12, §5.6.3): the
/// members of its name of the class of the object, as the code of the access sees them, none when no member of the class is reachable, and not known when
/// the object is not.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class LateBoundCandidatesFactTests
{
    private static readonly (string Name, string Source) Holder = ("Holder", ModuleWorkspace.ClassModule("Holder", "Option Explicit", "Private m As Long",
        "Public Tag As Long",
        "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = m", "End Property",
        "Public Property Let Value(ByVal v As Long)", "Attribute Value.VB_UserMemId = 0", "m = v", "End Property",
        "Friend Function Badge() As String", "Badge = \"b\"", "End Function",
        "Private Function Secret() As Long", "Secret = 1", "End Function"));

    private static readonly (string Name, string Source) Shape = ("IShape", ModuleWorkspace.ClassModule("IShape", "Option Explicit",
        "Public Function Area() As Double", "End Function"));

    private static readonly (string Name, string Source) Square = ("Square", ModuleWorkspace.ClassModule("Square", "Option Explicit", "Implements IShape",
        "Private Function IShape_Area() As Double", "IShape_Area = 4", "End Function",
        "Public Function Side() As Double", "Side = 2", "End Function"));

    private static readonly (string Name, string Source) Bag = ("Bag", ModuleWorkspace.ClassModule("Bag", "Option Explicit",
        "Public Function Item(ByVal key As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = 1", "End Function"));

    // the candidates of the procedure's one member access, each as the module and member it addresses, and its access modifier; null when not known.
    private static async Task<(MemberAccessOperationSemanticFlags Flags, string? Candidates)> CandidatesOfAsync(string parameters, string body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main({parameters})\r\n"
            + $"{string.Join("\r\n", body.Split('|'))}\r\nEnd Sub\r\n";
        var model = await ModuleWorkspace.ModelAsync([Holder, Shape, Square, Bag], program);
        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));

        var access = model.Procedures.Single().Runtime!.Operations.OfType<MemberAccessFact>().Single();
        return (access.Flags, access.LateBoundCandidates is { } candidates
            ? string.Join(", ", candidates.Select(candidate => $"{candidate.Member.Fragment.TrimStart('#')} {candidate.Access}"))
            : null);
    }

    [TestMethod]
    [DataRow("Set o = New Holder|x = o.Value", "Holder.Value Public, Holder.Value]Let Public", DisplayName = "a property: its Get and its Let")]
    [DataRow("Set o = New Holder|x = o.Tag", "Holder.Tag Public", DisplayName = "a public variable")]
    [DataRow("Set o = New Holder|x = o.Badge", "Holder.Badge Friend", DisplayName = "a Friend member, from code of its project")]
    [DataRow("Set o = New Holder|x = o.Secret", "", DisplayName = "a Private member: none, which raises 438")]
    [DataRow("Set o = New Holder|x = o.Nope", "", DisplayName = "no member of the name: none, which raises 438")]
    [DataRow("Set o = New Square|x = o.Area", "", DisplayName = "a member of an interface the class implements: none, its implementation is private")]
    [DataRow("Set o = New Square|x = o.Side", "Square.Side Public", DisplayName = "a member of the class that implements an interface")]
    [DataRow("Set o = New Bag|x = o(1)", "Bag.Item Public", DisplayName = "the default member an argument list goes to")]
    [DataRow("Set o = New Holder|Set o = New Square|x = o.Side", "Square.Side Public", DisplayName = "each class the module's Set statements give it")]
    public async Task ALateBoundAccessOfAnObjectOfAKnownClass_MayReachTheMembersOfItsName(string body, string expected)
    {
        var (flags, candidates) = await CandidatesOfAsync("", "Dim o As Object|Dim x As Variant|" + body);

        Assert.IsTrue(flags.HasFlag(MemberAccessOperationSemanticFlags.LateBound));
        Assert.AreEqual(expected, candidates);
    }

    [TestMethod]
    public async Task ALateBoundAccessOfAVariantThatHoldsAnObject_MayReachTheMembersOfItsName()
    {
        var (flags, candidates) = await CandidatesOfAsync("", "Dim v As Variant|Dim x As Variant|Set v = New Square|x = v.Side");

        Assert.IsTrue(flags.HasFlag(MemberAccessOperationSemanticFlags.LateBound));
        Assert.AreEqual("Square.Side Public", candidates);
    }

    [TestMethod]
    [DataRow("ByVal o As Object", "Dim x As Variant|x = o.Value", DisplayName = "an object given as an argument")]
    [DataRow("", "Dim o As Object|Dim x As Variant|x = o.Value", DisplayName = "Nothing, which raises 91")]
    [DataRow("", "Dim o As Object|Dim s As IShape|Dim x As Variant|Set s = New Square|Set o = s|x = o.Area",
        DisplayName = "an object assigned from a variable, of any class that implements the one it is declared as")]
    public async Task ALateBoundAccessOfAnObjectThatIsNotKnown_HasNoKnownCandidates(string parameters, string body)
    {
        var (flags, candidates) = await CandidatesOfAsync(parameters, body);

        Assert.IsTrue(flags.HasFlag(MemberAccessOperationSemanticFlags.LateBound));
        Assert.IsNull(candidates);
    }

    [TestMethod]
    [DataRow("Dim s As IShape|Dim x As Variant|Set s = New Square|x = s.Area", DisplayName = "through an interface")]
    [DataRow("Dim h As New Holder|Dim x As Variant|x = h.Tag", DisplayName = "through a class")]
    public async Task AnEarlyBoundAccess_HasNoCandidates(string body)
    {
        var (flags, candidates) = await CandidatesOfAsync("", body);

        Assert.IsFalse(flags.HasFlag(MemberAccessOperationSemanticFlags.LateBound));
        Assert.IsNull(candidates);
    }
}
