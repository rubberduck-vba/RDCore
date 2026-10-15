using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The language core's statements about the code of a module, found by evaluating every instruction of every procedure once, wherever the code paths
/// lead (<see cref="RDCore.Runtime.Execution.ModuleSweep"/>).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class ModuleSweepTests
{
    private static async Task<RuntimeFacts> FactsOfAsync(params string[] body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var model = await ModuleWorkspace.ModelAsync([], program);

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        var procedure = model.Procedures.Single();
        Assert.IsNotNull(procedure.Runtime, "the module was loaded, so its code was evaluated");
        return procedure.Runtime;
    }

    private static ConversionFact Assignment(RuntimeFacts facts)
        => facts.Conversions.Single(conversion => conversion.Site == ConversionSite.Assignment);

    [TestMethod]
    public async Task ADoubleAssignedToALong_IsAnImplicitNarrowingConversion_ThatRoundsHalfToEven()
    {
        var facts = await FactsOfAsync("Dim d As Double", "Dim n As Long", "n = d");

        var conversion = Assignment(facts);
        Assert.AreEqual(VBDoubleType.TypeInfo, conversion.Source);
        Assert.AreEqual(VBLongType.TypeInfo, conversion.Destination);
        Assert.AreEqual(
            ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding,
            conversion.Flags & (ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Explicit | ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy
                | ConversionSemanticFlags.BankersRounding));
        Assert.IsFalse(conversion.IsValueKnown, "what the variable holds is not known: the conversion is stated from the types alone");
        Assert.IsNull(conversion.Error, "nothing is guaranteed to fail for a Double that is not known");
        Assert.IsTrue(facts.IsFullyAnalyzed);
    }

    [TestMethod]
    public async Task ALongAssignedToADouble_IsAnImplicitWideningConversion()
    {
        var facts = await FactsOfAsync("Dim d As Double", "Dim n As Long", "d = n");

        var conversion = Assignment(facts);
        Assert.IsTrue(conversion.Flags.HasFlag(ConversionSemanticFlags.Widening));
        Assert.IsFalse(conversion.Flags.HasFlag(ConversionSemanticFlags.Narrowing));
    }

    [TestMethod]
    public async Task TheBranchNobodyTakes_IsEvaluatedLikeTheRest()
    {
        var facts = await FactsOfAsync(
            "Dim d As Double", "Dim n As Long",
            "If False Then", "    n = d", "End If");

        Assert.IsTrue(Assignment(facts).Flags.HasFlag(ConversionSemanticFlags.Narrowing));
    }

    [TestMethod]
    public async Task AValueTheCodeAssigns_IsNotTrustedByAReadThatMayFollowAnotherPath()
    {
        // n is 0 along the path that took the branch, and 1 along the other: the division is not a certain error.
        var facts = await FactsOfAsync(
            "Dim n As Long", "Dim r As Long", "Dim c As Boolean",
            "n = 1", "If c Then n = 0", "r = 1 / n");

        Assert.IsTrue(facts.IsFullyAnalyzed);
        Assert.IsFalse(facts.Operations.Any(operation => operation.Error is not null), "a value assumed does not raise a stated error");
    }

    [TestMethod]
    public async Task ALiteralTheDestinationCannotHold_IsAStatedRunTimeError()
    {
        var facts = await FactsOfAsync("Dim b As Byte", "b = 300");

        var conversion = Assignment(facts);
        Assert.IsTrue(conversion.IsValueKnown);
        Assert.IsNotNull(conversion.Error, "300 does not fit a Byte, whatever else is true of the code");
        Assert.AreEqual(6, conversion.Error.ErrorId);
    }

    [TestMethod]
    public async Task WhatTheStandardLibraryReturnsForAnArgumentThatIsNotKnown_IsNotKnown_AndIsNoStatedRunTimeError()
    {
        // s is not known, so neither is its length: 300 - Len(s) is not known to overflow a Byte, whatever the length of the string s assumes.
        var facts = await FactsOfAsync("Dim s As String", "Dim b As Byte", "b = 300 - Len(s)");

        var conversion = Assignment(facts);
        Assert.IsFalse(conversion.IsValueKnown, "Len(s) is not known when s is not");
        Assert.IsNull(conversion.Error, "nothing is guaranteed to fail for a string that is not known");
        Assert.IsFalse(facts.Operations.Any(operation => operation.IsValueKnown), "300 - Len(s) is not known either");
        Assert.IsTrue(facts.IsFullyAnalyzed);
    }

    [TestMethod]
    public async Task WhatTheStandardLibraryReturnsForAnArgumentThatIsNotKnown_IsConvertedLikeAVariableThatIsNotKnown()
    {
        // Len returns a Variant, and a Variant that is not known is not known to hold a Long either: 200 + Len(s) is what 200 + v is.
        var library = Assignment(await FactsOfAsync("Dim s As String", "Dim b As Byte", "b = 200 + Len(s)"));
        var variable = Assignment(await FactsOfAsync("Dim v As Variant", "Dim b As Byte", "b = 200 + v"));

        Assert.IsFalse(variable.IsValueKnown);
        Assert.IsFalse(library.IsValueKnown, "200 + Len(s) is not known to fit a Byte");
        Assert.AreEqual(variable.Source, library.Source);
        Assert.AreEqual(variable.Flags, library.Flags);
    }

    [TestMethod]
    public async Task WhatTheStandardLibraryReturnsForArgumentsThatAreKnown_IsKnown()
    {
        var facts = await FactsOfAsync("Dim b As Byte", "b = 300 - Len(\"abc\")");

        var conversion = Assignment(facts);
        Assert.IsTrue(conversion.IsValueKnown, "the code says what the string is, and so what its length is");
        Assert.IsNotNull(conversion.Error, "297 does not fit a Byte");
        Assert.AreEqual(6, conversion.Error.ErrorId);
    }

    [TestMethod]
    public async Task AMemberOfAClass_IsEvaluatedOnAnInstanceOfIt_WhoseFieldsAreNotKnown()
    {
        var holder = ModuleWorkspace.ClassModule(
            "Holder",
            "Option Explicit",
            "Private Type TState",
            "    Name As String",
            "    Size As Long",
            "    Target As Object",
            "End Type",
            "Private this As TState",
            "Public Property Set Target(ByVal value As Object)",
            "    Set this.Target = value",
            "End Property",
            "Public Sub Resize(ByVal factor As Double)",
            "    this.Size = factor",
            "    Dim d As Double",
            "    d = this.Size",
            "End Sub");

        var model = await ModuleWorkspace.ModelAsync([("Holder", holder)], "Attribute VB_Name = \"Program\"\r\n", moduleName: "Holder");

        Assert.AreEqual(2, model.Procedures.Length);
        Assert.IsTrue(model.Procedures[0].Runtime!.IsFullyAnalyzed, "Property Set Target");
        Assert.IsTrue(model.Procedures[1].Runtime!.IsFullyAnalyzed, "Resize");

        Assert.IsTrue(
            model.Procedures[1].Runtime!.Conversions
                .Any(conversion => conversion.Flags.HasFlag(ConversionSemanticFlags.Narrowing)),
            "this.Size = factor narrows a Double into a Long");
    }

    [TestMethod]
    public async Task TheBoundsOfAnArrayThatIsNotKnown_AreNotKnown_AndNotAnError()
    {
        // coords holds whatever Split gave it, which an analysis does not know: it is not known to be an array, and so UBound(coords) is not known to fail either.
        var facts = await FactsOfAsync(
            "Dim coords As Variant", "Dim n As Long", "Dim v As Variant",
            "n = UBound(coords) - LBound(coords) + 1");

        Assert.IsTrue(facts.IsFullyAnalyzed, "bounds");
        facts = await FactsOfAsync("Dim coords As Variant", "Dim v As Variant", "v = coords(1)");
        Assert.IsTrue(facts.IsFullyAnalyzed, "an element of what is not known");
        Assert.IsFalse(facts.Conversions.Any(conversion => conversion.Error is not null));
        Assert.IsFalse(facts.Operations.Any(operation => operation.Error is not null));
    }

    [TestMethod]
    public async Task AnInstructionThatCannotBeEvaluated_DoesNotTakeTheFollowingOnesWithIt()
    {
        // the first statement reads an Enum member, which the interpreter has no value for yet; whether or not it ever does, the narrowing after it is stated.
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Enum Color\r\n    Red\r\n    Green\r\nEnd Enum\r\n"
            + "Public Sub Main()\r\n    Dim c As Long\r\n    c = Color.Green\r\n    Dim d As Double\r\n    Dim n As Long\r\n    n = d\r\nEnd Sub\r\n";

        var model = await ModuleWorkspace.ModelAsync([], program);

        Assert.IsTrue(
            model.Procedures.Single().Runtime!.Conversions.Any(conversion => conversion.Source == VBDoubleType.TypeInfo && conversion.Destination == VBLongType.TypeInfo),
            "n = d");
    }

    [TestMethod]
    public async Task AProcedureIsEvaluatedWhetherOrNotAnythingCallsIt_AndACallIsNotMade()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n"
            + "Public Sub Main()\r\n    Dim n As Long\r\n    n = Half(3)\r\nEnd Sub\r\n"
            + "Private Function Half(ByVal x As Double) As Double\r\n    Dim n As Long\r\n    n = x\r\n    Half = n\r\nEnd Function\r\n"
            + "Private Sub NeverCalled(ByVal x As Double)\r\n    Dim n As Long\r\n    n = x\r\nEnd Sub\r\n";

        var model = await ModuleWorkspace.ModelAsync([], program);

        var main = model.Procedures[0].Runtime!;
        Assert.IsTrue(Assignment(main).Flags.HasFlag(ConversionSemanticFlags.Narrowing), "Half returns a Double, which Main assigns to a Long");
        foreach (var procedure in model.Procedures.Skip(1))
        {
            Assert.IsTrue(procedure.Runtime!.Conversions.Any(conversion => conversion.Flags.HasFlag(ConversionSemanticFlags.Narrowing)), "x is a Double assigned to a Long");
        }
    }
}
