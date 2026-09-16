using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for the <c>Like</c> operator runtime semantics: VBA wildcard pattern
/// matching (<c>? # * [...] [!...]</c>) over the string effective type (RD-VBAL §5.0.2.1).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
public sealed class LikeRelationalOperatorRuntimeTests : OperatorRelationalRuntimeSemanticsTests
{
    private LikeRelationalOperatorRuntimeSemantics Like() => new(FakeProvider(), Formatter());

    [TestMethod]
    public void ExactMatch_NoWildcards_True()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("abc"), new VBStringValue("abc")), true);

    [TestMethod]
    public void ExactMatch_NoWildcards_PartialOverlap_False()
        // "abcd" must NOT match "a.c" — the pattern is matched in full, not as a substring.
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("abcd"), new VBStringValue("abc")), false);

    [TestMethod]
    public void QuestionMark_MatchesExactlyOneCharacter()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("cat"), new VBStringValue("c?t")), true);

    [TestMethod]
    public void QuestionMark_DoesNotMatchZeroCharacters()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("ct"), new VBStringValue("c?t")), false);

    [TestMethod]
    public void Hash_MatchesASingleDigit()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("a1"), new VBStringValue("a#")), true);

    [TestMethod]
    public void Hash_DoesNotMatchANonDigit()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("ax"), new VBStringValue("a#")), false);

    [TestMethod]
    public void Asterisk_MatchesZeroOrMoreCharacters()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("abcdef"), new VBStringValue("a*f")), true);

    [TestMethod]
    public void Asterisk_MatchesEmptySpan()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("af"), new VBStringValue("a*f")), true);

    [TestMethod]
    public void CharacterList_MatchesAnyListedCharacter()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("b"), new VBStringValue("[abc]")), true);

    [TestMethod]
    public void CharacterList_DoesNotMatchAnUnlistedCharacter()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("d"), new VBStringValue("[abc]")), false);

    [TestMethod]
    public void NegatedCharacterList_MatchesAnUnlistedCharacter()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("d"), new VBStringValue("[!abc]")), true);

    [TestMethod]
    public void NegatedCharacterList_DoesNotMatchAListedCharacter()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("a"), new VBStringValue("[!abc]")), false);

    [TestMethod]
    public void RegexMetacharacter_DoesNotMatchAnyCharacter()
        // "." in the pattern is a literal character, not "any character" — a raw, unescaped
        // translation into .NET regex would wrongly let "a.c" match "axc" too.
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("axc"), new VBStringValue("a.c")), false);

    [TestMethod]
    public void RegexMetacharacter_MatchesItsLiteralSelf()
        => AssertResult<VBBooleanValue>(Evaluate(Like(), new VBStringValue("a.c"), new VBStringValue("a.c")), true);

    [TestMethod]
    public void UnterminatedCharacterList_IsInvalidPatternStringRuntimeError()
        // MS-VBAL 5.6.9.6: a pattern that doesn't form a valid, complete like-pattern-element raises
        // runtime error 93 (Invalid pattern string) — never an uncaught regex exception.
        => AssertError(Evaluate(Like(), new VBStringValue("a"), new VBStringValue("[abc")), VBRuntimeErrorId.InvalidPatternString);

    [TestMethod]
    public void EitherOperandNull_ResultIsNull()
    {
        var result = Evaluate(Like(), VBNullValue.Null, new VBStringValue("a*"));
        Assert.IsNull(result.ErrorInfo);
        Assert.IsInstanceOfType<VBNullValue>(result.Result);
    }

    [TestMethod]
    public void NonStringOperand_LetCoercesToStringInsteadOfThrowing()
        // MS-VBAL 5.6.9.6: both operands are Let-coerced to String regardless of their own value
        // type — Like must not resolve an effective type off the base numeric/date table.
        => AssertResult<VBBooleanValue>(
            Evaluate(new LikeRelationalOperatorRuntimeSemantics(RealCoercionProvider(), Formatter()), new VBIntegerValue(5), new VBStringValue("5")),
            true);
}
