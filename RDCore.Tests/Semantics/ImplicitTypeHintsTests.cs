using RDCore.Parsing;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.Tests.Semantics;

/// <summary>
/// A variable or a parameter that states no type is a <c>Variant</c>, and is hinted so after its name: not when it states one (an <c>As</c> clause or a type-declaration
/// character), not when a <c>Def&lt;Type&gt;</c> directive gives it another, not a constant, and not an array parameter.
/// </summary>
[TestClass]
public sealed class ImplicitTypeHintsTests
{
    private static (int Line, int Character)[] HintsIn(params string[] lines)
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Hints.bas"), string.Join("\r\n", lines) + "\r\n");
        var everything = new SourceRange(new SourcePosition(0, 0), new SourcePosition(1000, 0));
        return [.. ImplicitTypeHints.In(parse.SyntaxTree!, everything).Select(hint => (hint.Position.Line, hint.Position.Character))];
    }

    [TestMethod]
    public void AVariable_WithNoType_IsHintedAfterItsName()
        => CollectionAssert.AreEqual(new[] { (0, 5) }, HintsIn("Dim x"));

    [TestMethod]
    public void AVariable_WithABoundsList_IsHintedAfterTheBounds()
        => CollectionAssert.AreEqual(new[] { (0, 9) }, HintsIn("Dim a(10)"), "the hint is where the name and its bounds end, as `As Variant` is written");

    [TestMethod]
    public void ADeclaration_ThatStatesAType_HasNoHint()
        => Assert.AreEqual(0, HintsIn("Dim y As Long", "Dim z$", "Dim w&", "Dim v(3) As String").Length);

    [TestMethod]
    public void ADefTypeDirective_GivesTheNamesItCoversAnotherType_SoTheyHaveNoHint()
    {
        CollectionAssert.AreEqual(new[] { (2, 5) }, HintsIn("DefInt A-C", "Dim a", "Dim x"), "`a` is an Integer; `x` is not covered, and is a Variant");
        CollectionAssert.AreEqual(new[] { (1, 5) }, HintsIn("DefVar A-Z", "Dim a"), "a directive that gives the Variant it is changes nothing");
    }

    [TestMethod]
    public void AConstant_HasNoHint_ItsTypeIsTheTypeOfItsValue()
        => Assert.AreEqual(0, HintsIn("Const K = 1").Length);

    [TestMethod]
    public void AnArrayParameter_HasNoHint_ButAnotherParameterOfTheSameProcedureHas()
        => CollectionAssert.AreEqual(new[] { (0, 21) }, HintsIn("Sub S(Items(), Prompt)", "End Sub"));

    [TestMethod]
    public void AParameter_ThatStatesAType_HasNoHint()
        => Assert.AreEqual(0, HintsIn("Sub S(Prompt As String, Count%)", "End Sub").Length);

    [TestMethod]
    public void OnlyTheDeclarationsInTheRange_AreHinted()
    {
        var parse = new ModuleParser().Parse(new Uri("file:///c:/ws/Hints.bas"), "Dim a\r\nDim b\r\nDim c\r\n");

        var hints = ImplicitTypeHints.In(parse.SyntaxTree!, new SourceRange(new SourcePosition(1, 0), new SourcePosition(1, 100)));

        Assert.AreEqual(1, hints.Count);
        Assert.AreEqual(1, hints[0].Position.Line);
    }
}
