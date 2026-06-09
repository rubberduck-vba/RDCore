using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
public class LikeOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.CompareLikeOp;

    internal override IRuntimeSemantics Semantics => new LikeRelationalOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBStringType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow("hello", "hello", true)]
    [DataRow("hello", "HELLO", true)]  // Case-insensitive
    [DataRow("hello", "world", false)]
    public void EvaluateLike_LiteralMatch_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("hello", "h*", true)]      // * matches zero or more characters
    [DataRow("hello", "h?llo", true)]   // ? matches exactly one character
    [DataRow("hello", "h?ll?", true)]
    [DataRow("hello", "h?l", false)]    // Pattern too short
    public void EvaluateLike_WildcardPatterns_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("h3llo", "h#llo", true)]   // # matches exactly one digit
    [DataRow("h3llo", "h##llo", false)] // Need two digits
    [DataRow("h123llo", "h#*llo", true)]  // # followed by *
    [DataRow("hllo", "h#llo", false)]   // No digit
    public void EvaluateLike_DigitPattern_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("a1c", "a[0-9]c", true)]       // Character class
    [DataRow("abc", "a[0-9]c", false)]
    [DataRow("aXc", "a[a-z]c", true)]       // Range in class
    public void EvaluateLike_CharacterClass_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("aXc", "a[!0-9]c", true)] 
    [DataRow("a1c", "a[!0-9]c", false)]
    public void EvaluateLike_NegatedCharacterClass_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }
}
