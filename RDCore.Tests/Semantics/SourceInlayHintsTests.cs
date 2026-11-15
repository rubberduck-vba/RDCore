using RDCore.Parsing;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;

namespace RDCore.Tests.Semantics;

/// <summary>
/// What the code of a module means without saying it is hinted in square brackets at the place it describes, never at the end of a line it does not
/// describe: the access of a procedure and the passing of a parameter that state none, the name of the procedure at its end, the variable of a Next that
/// names none, and the Variant of a declaration that states no type. Each kind of place is turned on or off by its own setting.
/// </summary>
[TestClass]
public sealed class SourceInlayHintsTests
{
    private static SdkInlayHintOptions Only(InlayHintSite site) => new()
    {
        Arguments = site == InlayHintSite.Arguments,
        BlockEnds = site == InlayHintSite.BlockEnds,
        NextVariables = site == InlayHintSite.NextVariables,
        ImplicitModifiers = site == InlayHintSite.ImplicitModifiers,
        ImplicitTypes = site == InlayHintSite.ImplicitTypes,
    };

    private static IReadOnlyList<SourceInlayHint> All(SdkInlayHintOptions sites, params string[] lines)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Hints.bas"), string.Join("\r\n", lines) + "\r\n");
        Assert.IsTrue(parse.IsSuccess, string.Join("; ", parse.SyntaxErrors.Select(error => error.Verbose)));
        return SourceInlayHints.In(parse.SyntaxTree!, new SourceRange(new SourcePosition(0, 0), new SourcePosition(1000, 0)), sites, _ => null);
    }

    // each hint of a kind, as (line, character, label).
    private static (int Line, int Character, string Label)[] HintsIn(InlayHintSite site, params string[] lines)
        => [.. All(Only(site), lines).Select(hint => (hint.Position.Line, hint.Position.Character, hint.Label))];

    // ---- implicit types (MS-VBAL 5.2.3.1.5) ----

    [TestMethod]
    public void AVariable_WithNoType_IsHintedAfterItsName()
        => CollectionAssert.AreEqual(new[] { (0, 5, "[As Variant]") }, HintsIn(InlayHintSite.ImplicitTypes, "Dim x"));

    [TestMethod]
    public void OneVariableOfADeclaration_WithNoType_IsHintedWhereItsTypeWouldBeWritten()
        => CollectionAssert.AreEqual(new[] { (0, 9, "[As Variant]") }, HintsIn(InlayHintSite.ImplicitTypes, "Dim First, Last As String"));

    [TestMethod]
    public void AVariable_WithABoundsList_IsHintedAfterTheBounds()
        => CollectionAssert.AreEqual(new[] { (0, 9, "[As Variant]") }, HintsIn(InlayHintSite.ImplicitTypes, "Dim a(10)"), "where the name and its bounds end, as `As Variant` is written");

    [TestMethod]
    public void ADeclaration_ThatStatesAType_HasNoHint()
        => Assert.IsEmpty(HintsIn(InlayHintSite.ImplicitTypes, "Dim y As Long", "Dim z$", "Dim w&", "Dim v(3) As String"));

    [TestMethod]
    public void ADefTypeDirective_GivesTheNamesItCoversAnotherType_SoTheyHaveNoHint()
    {
        CollectionAssert.AreEqual(new[] { (2, 5, "[As Variant]") }, HintsIn(InlayHintSite.ImplicitTypes, "DefInt A-C", "Dim a", "Dim x"), "`a` is an Integer; `x` is a Variant");
        CollectionAssert.AreEqual(new[] { (1, 5, "[As Variant]") }, HintsIn(InlayHintSite.ImplicitTypes, "DefVar A-Z", "Dim a"), "a directive that gives the Variant it is changes nothing");
    }

    [TestMethod]
    public void AConstant_HasNoHint_ItsTypeIsTheTypeOfItsValue()
        => Assert.IsEmpty(HintsIn(InlayHintSite.ImplicitTypes, "Const K = 1"));

    [TestMethod]
    public void AnArrayParameter_HasNoHint_ButAnotherParameterOfTheSameProcedureHas()
        => CollectionAssert.AreEqual(new[] { (0, 21, "[As Variant]") }, HintsIn(InlayHintSite.ImplicitTypes, "Sub S(Items(), Prompt)", "End Sub"));

    [TestMethod]
    public void AParameter_ThatStatesAType_HasNoHint()
        => Assert.IsEmpty(HintsIn(InlayHintSite.ImplicitTypes, "Sub S(Prompt As String, Count%)", "End Sub"));

    [TestMethod]
    public void ADeclaration_InAProceduresBody_AndInABlockOfABlockOfIt_IsHinted()
        => CollectionAssert.AreEqual(
            new[] { (1, 13, "[As Variant]"), (4, 20, "[As Variant]") },
            HintsIn(InlayHintSite.ImplicitTypes, "Sub S()", "    Dim local", "    If True Then", "        Do While True", "            Dim deep", "        Loop", "    End If", "End Sub"));

    // ---- implicit modifiers (MS-VBAL 5.3.1.1, 5.3.1.5) ----

    [TestMethod]
    public void AProcedure_WithNoAccess_IsPublic_AndAParameter_PassedNeitherWay_IsByRef()
        => CollectionAssert.AreEqual(new[] { (0, 0, "[Public]"), (0, 9, "[ByRef]") },
            HintsIn(InlayHintSite.ImplicitModifiers, "Sub Work(Total As Long)", "End Sub"));

    [TestMethod]
    public void AnOptionalParameter_IsHintedAfterItsOptional_BeforeItsName()
        => CollectionAssert.AreEqual(new[] { (0, 0, "[Public]"), (0, 20, "[ByRef]") },
            HintsIn(InlayHintSite.ImplicitModifiers, "Function F(Optional x As Long) As Long", "End Function"));

    [TestMethod]
    public void AStaticProcedure_IsHintedBeforeItsStatic()
        => CollectionAssert.AreEqual(new[] { (0, 0, "[Public]") }, HintsIn(InlayHintSite.ImplicitModifiers, "Static Sub S()", "End Sub"));

    [TestMethod]
    public void WhatStatesItsModifiers_AndAParamArray_HaveNoHint()
        => Assert.IsEmpty(HintsIn(InlayHintSite.ImplicitModifiers,
            "Private Sub P(ByVal a As Long, ByRef b As Long, ParamArray c() As Variant)", "End Sub", "Public Function F() As Long", "End Function"));

    [TestMethod]
    public void TheValueOfAPropertyLet_IsPassedByValue()
        => CollectionAssert.AreEqual(new[] { (0, 0, "[Public]"), (0, 19, "[ByVal]") },
            HintsIn(InlayHintSite.ImplicitModifiers, "Property Let Value(v As Long)", "End Property"));

    // ---- block ends ----

    [TestMethod]
    [Ignore("the parser says no place for the statement that closes a procedure yet (MemberDeclarationNode.EndRange)")]
    public void TheEndOfAProcedure_IsHintedWithItsName()
        => CollectionAssert.AreEqual(new[] { (1, 7, "[DoSomething]"), (3, 12, "[F]"), (5, 12, "[Name]") },
            HintsIn(InlayHintSite.BlockEnds, "Sub DoSomething()", "End Sub", "Function F()", "End Function", "Property Get Name()", "End Property"));

    // ---- Next (MS-VBAL 5.4.2.3, 5.4.2.4) ----

    [TestMethod]
    public void ANextThatNamesNoVariable_IsHintedWithTheLoops()
        => CollectionAssert.AreEqual(new[] { (3, 8, "[i]"), (6, 8, "[v]") },
            HintsIn(InlayHintSite.NextVariables, "Sub S()", "    Dim i As Long", "    For i = 1 To 2", "    Next", "    Dim v, c As New Collection",
                "    For Each v In c", "    Next", "End Sub"));

    [TestMethod]
    public void ANextThatNamesItsVariables_HasNoHint()
        => Assert.IsEmpty(HintsIn(InlayHintSite.NextVariables, "Sub S()", "    Dim i As Long, j As Long", "    For i = 1 To 2", "    Next i",
            "    For i = 1 To 2", "        For j = 1 To 2", "    Next j, i", "End Sub"));

    // ---- every kind at once ----

    [TestMethod]
    public void TheHints_AreInBrackets_AtTheirPlaces_PaddedApartFromTheWordTheyFollowOrPrecede()
    {
        var hints = All(new SdkInlayHintOptions { BlockEnds = false }, "Sub Work(Total)", "    Dim i", "    For i = 1 To 2", "    Next", "End Sub");

        CollectionAssert.AreEqual(
            new[] { "0:0 [Public] >", "0:9 [ByRef]", "0:14 < [As Variant]", "1:9 < [As Variant]", "3:8 < [i]" },
            hints.Select(hint => $"{hint.Position.Line}:{hint.Position.Character} {(hint.PaddingLeft ? "< " : string.Empty)}{hint.Label}{(hint.PaddingRight ? " >" : string.Empty)}").ToArray());
    }

    [TestMethod]
    public void AKindTurnedOff_IsNotHinted_AndTheOthersAre()
    {
        var sites = new SdkInlayHintOptions { BlockEnds = false };

        var hints = All(sites, "Sub Work()", "End Sub");

        Assert.AreEqual("[Public]", Assert.ContainsSingle(hints).Label);
    }

    [TestMethod]
    public void OnlyThePlacesInTheRange_AreHinted()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Hints.bas"), "Dim a\r\nDim b\r\nDim c\r\n");

        var hints = SourceInlayHints.In(parse.SyntaxTree!, new SourceRange(new SourcePosition(1, 0), new SourcePosition(1, 100)), new SdkInlayHintOptions(), _ => null);

        Assert.AreEqual((1, 5, "[As Variant]"), (Assert.ContainsSingle(hints).Position.Line, hints[0].Position.Character, hints[0].Label));
    }
}
