using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Cli;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// What the sweep of a module states besides its conversions and operations: the run-time errors its instructions raise whenever they run, the members
/// its code accesses, and what its <c>LSet</c>, <c>RSet</c> and <c>With</c> statements are (<see cref="RuntimeFacts"/>).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class RuntimeFactsExposureTests
{
    private static async Task<RuntimeFacts> FactsOfAsync(string parameters, params string[] body)
    {
        var program = $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\nPublic Sub Main({parameters})\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";
        var model = await ModuleWorkspace.ModelAsync([], program);

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

        Assert.AreEqual(MemberAccessOperationSemanticFlags.Implicit | MemberAccessOperationSemanticFlags.DefaultMember, access.Flags);
        Assert.AreEqual("Item", access.Member, "the code does not name it: the class does");
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
        Assert.AreEqual(MemberAccessOperationSemanticFlags.Explicit | MemberAccessOperationSemanticFlags.WithBlockVariable, access.Flags);
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

    [TestMethod]
    [Ignore("Needs Variant type tracking (deferred types): the analysis does not track the type a Variant holds, so the effective type of an operation of a Variant that is not known is the assumed Empty's (Integer), stated as a fact.")]
    public async Task AnOperationOfAVariantThatIsNotKnown_HasNoKnownEffectiveType()
    {
        var facts = await FactsOfAsync("ByVal v As Variant, ByVal w As Variant", "Dim r As Variant", "r = v + w");

        Assert.IsNull(facts.Operations.Single().EffectiveType);
    }
}
