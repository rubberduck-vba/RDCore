using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// What the sweep of a module states besides its conversions and operations: the run-time errors its instructions raise whenever they run, the members
/// its code accesses, and what its <c>LSet</c>, <c>RSet</c> and <c>With</c> statements are (<see cref="RuntimeFacts"/>).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class RuntimeFactsExposureTests
{
    private static Task<RuntimeFacts> FactsOfAsync(string parameters, params string[] body) => FactsOfAsync([], parameters, body);

    private static async Task<RuntimeFacts> FactsOfAsync(IReadOnlyList<(string Name, string Source)> classes, string parameters, params string[] body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main({parameters})\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var model = await ModuleWorkspace.ModelAsync(classes, program);

        Assert.IsTrue(model.IsValid, string.Join("; ", model.CompileErrors.Select(error => error.Verbose)));
        var facts = model.Procedures.Single().Runtime;
        Assert.IsNotNull(facts, "the module was loaded, so its code was evaluated");
        return facts;
    }

    private static MemberAccessFact Access(RuntimeFacts facts)
        => facts.Operations.OfType<MemberAccessFact>().Single();

    [TestMethod]
    public async Task ALiteralTheTargetCannotHold_IsARunTimeErrorOfTheAssignment()
    {
        var facts = await FactsOfAsync("", "Dim n As Byte", "n = 5678");

        var error = facts.Errors.Single();
        Assert.IsInstanceOfType<VBRuntimeErrorInfo>(error);
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, error.ErrorId);
        Assert.AreEqual(4, error.Location.Range.Start.Line, "the assignment is the instruction that raises it");
    }

    [TestMethod]
    public async Task ADivisionByALiteralZero_IsARunTimeError()
    {
        var facts = await FactsOfAsync("", "Dim r As Long", "r = 1 / 0");

        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, facts.Errors.Single().ErrorId);
    }

    [TestMethod]
    public async Task AnErrorTheCodeRaisesForItself_IsAnApplicationError()
    {
        var facts = await FactsOfAsync("", "Error 5");

        Assert.IsInstanceOfType<VBApplicationErrorInfo>(facts.Errors.Single(), "the code raises it on purpose: it is not the language's");
    }

    [TestMethod]
    public async Task AnInstructionThatRaisesNothing_StatesNoError()
    {
        var facts = await FactsOfAsync("", "Dim n As Byte", "n = 56");

        Assert.IsEmpty(facts.Errors);
    }

    // every one of these raises an error in the sweep, for a value it only assumed: an unknown object is Nothing, an unknown number is 0, an unknown file
    // does not exist. None is a fact about the code.
    [TestMethod]
    [DataRow("ByVal o As Object", "o.Foo", DisplayName = "member of an object that is not known")]
    [DataRow("ByVal o As Object", "o.Foo = 1", DisplayName = "member assignment on an object that is not known")]
    [DataRow("ByVal o As Object", "Dim x As Variant|x = o!Foo", DisplayName = "dictionary access on an object that is not known")]
    [DataRow("ByVal o As Object", "With o|    .Foo = 1|End With", DisplayName = "member of a With block whose target is not known")]
    [DataRow("ByVal c As Collection", "Dim x As Variant|For Each x In c|Next", DisplayName = "For Each over an object that is not known")]
    [DataRow("ByVal v As Variant", "Dim x As Variant|For Each x In v|Next", DisplayName = "For Each over a Variant that is not known")]
    [DataRow("ByVal n As Long", "Error n", DisplayName = "Error with a number that is not known")]
    [DataRow("ByVal n As Long", "Dim r As Long|r = 1 / n", DisplayName = "division by a number that is not known")]
    [DataRow("ByVal s As String", "Dim b As Byte|b = CByte(s)", DisplayName = "library call with an argument that is not known")]
    [DataRow("", "Dim n As Long|n = Int(Rnd() * 10)", DisplayName = "library call that has state")]
    [DataRow("", "Dim o As Object|Set o = CreateObject(\"Scripting.Dictionary\")", DisplayName = "call into the outside world")]
    [DataRow("", "Dim s As String|Open \"x\" For Input As #1|Line Input #1, s|Close #1", DisplayName = "file statements")]
    [DataRow("", "Dim c As Collection|c.Add 1", DisplayName = "member of a local that is not known along every path")]
    public async Task AnErrorRaisedForAnAssumedValue_IsNotStated(string parameters, string body)
    {
        var facts = await FactsOfAsync(parameters, body.Split('|'));

        Assert.IsEmpty(facts.Errors, string.Join(", ", facts.Errors.Select(error => error.ErrorId)));
    }

    [TestMethod]
    public async Task AMemberOfAnObjectOfNoParticularClass_IsAnExplicitLateBoundAccess()
    {
        var access = Access(await FactsOfAsync("ByVal o As Object", "Dim x As Variant", "x = o.Foo"));

        Assert.AreEqual(Tokens.MemberAccess, access.Operator);
        Assert.AreEqual("Foo", access.Member);
        Assert.AreEqual(MemberAccessOperationSemanticFlags.Explicit | MemberAccessOperationSemanticFlags.LateBound, access.Flags);
        Assert.IsFalse(access.IsValueKnown);
        Assert.IsNull(access.EffectiveType, "a late-bound member is not declared");
        Assert.IsNull(access.Error);
    }

    [TestMethod]
    public async Task AMemberOfAClass_IsEarlyBound_AndHasTheTypeItIsDeclaredWith()
    {
        var access = Access(await FactsOfAsync("ByVal c As Collection", "Dim n As Long", "n = c.Count"));

        Assert.AreEqual(MemberAccessOperationSemanticFlags.Explicit, access.Flags);
        Assert.AreEqual(VBLongType.TypeInfo, access.EffectiveType);
    }

    [TestMethod]
    public async Task AnObjectThatIsIndexed_IsAnImplicitAccessOfItsDefaultMember()
    {
        var access = Access(await FactsOfAsync("ByVal c As Collection", "Dim x As Variant", "x = c(1)"));

        Assert.AreEqual(MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember | MemberAccessOperationSemanticFlags.ArgumentList,
            access.Flags);
        Assert.AreEqual("Item", access.Member, "the code does not name it: the class does");
    }

    // a class whose default member takes an argument, and can be assigned.
    private static readonly (string Name, string Source) Box = ("Box", ModuleWorkspace.ClassModule("Box", "Option Explicit", "Private m As Long",
        "Public Property Get Item(ByVal k As Variant) As Long", "Attribute Item.VB_UserMemId = 0", "Item = m", "End Property",
        "Public Property Let Item(ByVal k As Variant, ByVal v As Long)", "m = v", "End Property"));

    // an argument list given to an object that is assigned goes to its default member, as a read of it does, and is stated where the code writes it.
    [TestMethod]
    [DataRow("b(1) = 5", MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember | MemberAccessOperationSemanticFlags.ArgumentList,
        4, DisplayName = "the object")]
    [DataRow("b.Item(1) = 5", MemberAccessOperationSemanticFlags.Explicit | MemberAccessOperationSemanticFlags.DefaultMember | MemberAccessOperationSemanticFlags.ArgumentList,
        6, DisplayName = "the member named")]
    public async Task AnArgumentListGivenToAnObjectThatIsAssigned_IsAnAccessOfItsDefaultMember(string assignment, MemberAccessOperationSemanticFlags expected, int end)
    {
        var access = Access(await FactsOfAsync([Box], "", "Dim b As New Box", assignment));

        Assert.AreEqual(expected, access.Flags);
        Assert.AreEqual("Item", access.Member);
        Assert.AreEqual((4, 0, 4, end), (access.Location.Range.Start.Line, access.Location.Range.Start.Character, access.Location.Range.End.Line, access.Location.Range.End.Character));
    }

    // a class whose default member is a property the code can assign.
    private static readonly (string Name, string Source) Cell = ("Cell", ModuleWorkspace.ClassModule("Cell", "Option Explicit", "Private m As Long",
        "Public Property Get Value() As Long", "Attribute Value.VB_UserMemId = 0", "Value = m", "End Property",
        "Public Property Let Value(ByVal v As Long)", "Attribute Value.VB_UserMemId = 0", "m = v", "End Property"));

    // a value assigned to the object itself is given to its default member, which the code does not name; naming it is an explicit access of it.
    [TestMethod]
    [DataRow("h = 5", MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember, DisplayName = "the object")]
    [DataRow("h.Value = 5", MemberAccessOperationSemanticFlags.Explicit | MemberAccessOperationSemanticFlags.DefaultMember, DisplayName = "the member named")]
    public async Task AValueAssignedToAnObject_IsAnAccessOfItsDefaultMember(string assignment, MemberAccessOperationSemanticFlags expected)
    {
        var access = Access(await FactsOfAsync([Cell], "", "Dim h As New Cell", assignment));

        Assert.AreEqual(expected, access.Flags);
        Assert.AreEqual("Value", access.Member);
    }

    // the element is the target the code writes: the access is at the element, subscripts included, and not at the array's name.
    [TestMethod]
    public async Task AValueAssignedToAnElementThatHoldsAnObject_IsAnAccessOfItsDefaultMember_AtTheElement()
    {
        var access = Access(await FactsOfAsync([Cell], "", "Dim a(1) As Cell", "Set a(1) = New Cell", "a(1) = 5"));

        Assert.AreEqual(MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember, access.Flags);
        Assert.AreEqual((5, 0, 5, 4), (access.Location.Range.Start.Line, access.Location.Range.Start.Character, access.Location.Range.End.Line, access.Location.Range.End.Character));
    }

    [TestMethod]
    public async Task ADictionaryAccess_IsAnImplicitCallOfTheDefaultMember()
    {
        var access = Access(await FactsOfAsync("ByVal c As Collection", "Dim x As Variant", "x = c!Key"));

        Assert.AreEqual(Tokens.DictionaryAccess, access.Operator);
        Assert.AreEqual(
            MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember | MemberAccessOperationSemanticFlags.DictionaryAccess,
            access.Flags);
    }

    [TestMethod]
    public async Task ForEachOverAnObject_IsAnImplicitAccessOfItsEnumerationMember()
    {
        var access = Access(await FactsOfAsync("ByVal c As Collection", "Dim x As Variant", "For Each x In c", "Next"));

        Assert.AreEqual(MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.NewEnumMember, access.Flags);
    }

    [TestMethod]
    public async Task ForEachOverAnArray_AccessesNoMember()
    {
        var facts = await FactsOfAsync("", "Dim a(1) As Long", "Dim x As Variant", "For Each x In a", "Next");

        Assert.IsEmpty(facts.Operations.OfType<MemberAccessFact>());
    }

    [TestMethod]
    public async Task AMemberOfAWithBlock_IsAccessedThroughTheBlockVariable()
    {
        var facts = await FactsOfAsync("", "With New Collection", "    .Add 1", "End With");

        var access = Access(facts);
        Assert.AreEqual(MemberAccessOperationSemanticFlags.Explicit | MemberAccessOperationSemanticFlags.WithBlockVariable | MemberAccessOperationSemanticFlags.ArgumentList,
            access.Flags);
        Assert.IsTrue(access.IsValueKnown, "a new object is known");
    }

    [TestMethod]
    public async Task AMemberThatIsAssigned_IsAMemberAccess()
    {
        var access = Access(await FactsOfAsync("ByVal o As Object", "o.Foo = 1"));

        Assert.AreEqual("Foo", access.Member);
        Assert.AreEqual(MemberAccessOperationSemanticFlags.Explicit | MemberAccessOperationSemanticFlags.LateBound, access.Flags);
    }

    [TestMethod]
    public async Task AWithStatement_SaysWhetherItsTargetIsAnObject_AndWhetherItIsKnown()
    {
        var unknown = (WithStatementFact)(await FactsOfAsync("ByVal c As Collection", "With c", "End With")).Statements.Single();
        var known = (WithStatementFact)(await FactsOfAsync("", "With New Collection", "End With")).Statements.Single();

        Assert.AreEqual(Tokens.With, unknown.Statement);
        Assert.AreEqual(WithStatementSemanticFlags.ClassTarget, unknown.Flags);
        Assert.IsFalse(unknown.IsValueKnown);
        Assert.IsTrue(known.IsValueKnown);
    }

    [TestMethod]
    public async Task LSetAndRSet_OfAString_FitTheValueToTheTarget()
    {
        var facts = await FactsOfAsync("ByVal s As String", "LSet s = \"abc\"", "RSet s = \"abc\"");

        CollectionAssert.AreEqual(new[] { Tokens.LSet, Tokens.RSet }, facts.Statements.Select(fact => fact.Statement).ToArray());
        Assert.IsTrue(facts.Statements.Cast<FixedAssignmentStatementFact>().All(fact => fact.Flags == FixedAssignmentSemanticFlags.StringTarget));
    }

    [TestMethod]
    public async Task LSetOfARecordThatHoldsAString_IsAByteCopyOfAPointer()
    {
        var program = """
            Attribute VB_Name = "Program"
            Option Explicit
            Private Type TName
                Value As String
            End Type
            Private Type TCode
                Value As String * 4
            End Type
            Public Sub Main()
                Dim a As TName
                Dim b As TCode
                LSet b = a
            End Sub
            """.ReplaceLineEndings("\r\n") + "\r\n";
        var model = await ModuleWorkspace.ModelAsync([], program);

        var fact = (FixedAssignmentStatementFact)model.Procedures.Single().Runtime!.Statements.Single();
        Assert.AreEqual(FixedAssignmentSemanticFlags.UserDefinedTypeCopy | FixedAssignmentSemanticFlags.SourceHoldsVariableLengthString, fact.Flags);
    }

    // a parameter is the caller's argument, which the analysis has none of: its subtype is not known, and the Empty it assumes is no fact.
    [TestMethod]
    public async Task AnOperationOfAVariantThatIsNotKnown_HasNoKnownEffectiveType()
    {
        var facts = await FactsOfAsync("ByVal v As Variant, ByVal w As Variant", "Dim r As Variant", "r = v + w");

        Assert.IsNull(facts.Operations.Single().EffectiveType);
    }

    [TestMethod]
    public async Task AnOperationOfAnOperationOfAVariantThatIsNotKnown_HasNoKnownEffectiveType()
    {
        var facts = await FactsOfAsync("ByVal v As Variant", "Dim r As Variant, s As Variant", "r = v + 1", "s = r * 2");

        Assert.IsTrue(facts.Operations.All(operation => operation.EffectiveType is null));
    }

    [TestMethod]
    public async Task AVariantAssignedADouble_IsADouble_WhateverItsValue()
    {
        var facts = await FactsOfAsync("ByVal x As Double", "Dim v As Variant, r As Variant", "v = x * 1.5", "r = v + 1");

        CollectionAssert.AreEqual(new[] { "Double", "Double" }, facts.Operations.Select(operation => operation.EffectiveType?.Name).ToArray());
    }

    // a local the code has not assigned holds the Empty it starts as.
    [TestMethod]
    public async Task ALocalVariantNotYetAssigned_IsEmpty()
    {
        var facts = await FactsOfAsync("", "Dim v As Variant, r As Variant", "r = v + 1");

        Assert.AreEqual("Integer", facts.Operations.Single().EffectiveType?.Name);
    }

    // MS-VBAL 5.6.9.3 would make a Variant's Integer result that overflows a Long, or a Double: the analysis presumes it does not overflow.
    [TestMethod]
    public async Task AVariantResultThatCouldOverflow_IsPresumedNotTo()
    {
        var facts = await FactsOfAsync("ByVal x As Integer", "Dim v As Variant, r As Variant, s As Variant", "v = x", "r = v + 1", "s = r + 1");

        CollectionAssert.AreEqual(new[] { "Integer", "Integer" }, facts.Operations.Select(operation => operation.EffectiveType?.Name).ToArray());
    }

    [TestMethod]
    public async Task AConversionOfAVariantThatIsNotKnown_IsFromAVariant()
    {
        var facts = await FactsOfAsync("ByVal v As Variant", "Dim l As Long", "l = v");

        Assert.AreEqual("Variant", facts.Conversions.Single().Source.Name);
    }

    [TestMethod]
    public async Task AConversionOfAVariantAssignedADouble_IsFromADouble()
    {
        var facts = await FactsOfAsync("ByVal x As Double", "Dim v As Variant, l As Long", "v = x", "l = v");

        Assert.AreEqual("Double", facts.Conversions.Last().Source.Name);
    }

    // no operator yields an internal error for a Variant of a subtype that is not known, on either side.
    [TestMethod]
    [DataRow("v + w")] [DataRow("v - 1")] [DataRow("2 * v")] [DataRow("v / w")] [DataRow("v \\ 2")] [DataRow("v Mod w")] [DataRow("v ^ 2")]
    [DataRow("v & w")] [DataRow("v = w")] [DataRow("v <> 1")] [DataRow("v < \"a\"")] [DataRow("v >= w")] [DataRow("v Like \"a*\"")]
    [DataRow("v And w")] [DataRow("v Or 1")] [DataRow("v Xor w")] [DataRow("v Eqv w")] [DataRow("v Imp w")] [DataRow("Not v")] [DataRow("-v")]
    public async Task AnyOperatorOfAVariantThatIsNotKnown_IsFullyAnalyzed_WithNoKnownEffectiveType(string operation)
    {
        var facts = await FactsOfAsync("ByVal v As Variant, ByVal w As Variant", "Dim r As Variant", $"r = {operation}");

        Assert.IsTrue(facts.IsFullyAnalyzed);
        Assert.IsEmpty(facts.Errors);
        Assert.IsTrue(facts.Operations.All(operation => operation.EffectiveType is null));
    }

    // a module variable is entered with whatever the rest of the program last left in it.
    [TestMethod]
    public async Task AnOperationOfAModuleVariant_HasNoKnownEffectiveType()
    {
        var program = "Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPrivate m As Variant\r\nPublic Sub Main()\r\nDim r As Variant\r\nr = m + 1\r\nEnd Sub\r\n";
        var model = await ModuleWorkspace.ModelAsync([], program);

        Assert.IsNull(model.Procedures.Single().Runtime!.Operations.Single().EffectiveType);
    }
}
