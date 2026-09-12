using RDCore.Runtime.Semantics.Operators.Relational;
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
}
