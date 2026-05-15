using RDCore.Parsing;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model;
using RDCore.Runtime.Model.Operators;
using RDCore.Runtime.Model.Operators.RuntimeSemantics;
using RDCore.Server;

namespace RDCore.Tests.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.6 Binary 'Like' Operator")]
public class LikeOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new LikeRelationalOperatorRuntimeSemantics();
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
        var actual = EvaluateLike(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("hello", "h*", true)]      // * matches zero or more characters
    [DataRow("hello", "h?llo", true)]   // ? matches exactly one character
    [DataRow("hello", "h?ll?", true)]
    [DataRow("hello", "h?l", false)]    // Pattern too short
    public void EvaluateLike_WildcardPatterns_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateLike(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("h3llo", "h#llo", true)]   // # matches exactly one digit
    [DataRow("h3llo", "h##llo", false)] // Need two digits
    [DataRow("h123llo", "h#*llo", true)]  // # followed by *
    [DataRow("hllo", "h#llo", false)]   // No digit
    public void EvaluateLike_DigitPattern_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateLike(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("a1c", "a[0-9]c", true)]       // Character class
    [DataRow("abc", "a[0-9]c", false)]
    [DataRow("aXc", "a[a-z]c", true)]       // Range in class
    public void EvaluateLike_CharacterClass_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateLike(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [DataRow("aXc", "a[!0-9]c", true)] 
    [DataRow("a1c", "a[!0-9]c", false)]
    public void EvaluateLike_NegatedCharacterClass_CalculatesResult(string lhs, string rhs, bool expected)
    {
        var actual = EvaluateLike(CreateContext(), lhs, rhs) as VBBooleanValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateLike_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateLike(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(null, "hello")]
    [DataRow("hello", null)]
    public void EvaluateLike_SingleNullOperand_ResultIsNull(object lhs, object rhs)
    {
        var result = EvaluateLike(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateLike_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateLike(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateLike_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateLike(CreateContext(), lhs, rhs));
    }

    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [TestMethod]
    [DataRow("hello", "VBErrorValue")]
    public void EvaluateLike_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateLike(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateLike(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocationLHS);
        var rhsValue = WrapLiteralExpression(rhs, TestLocationRHS);
        // We need a symbol for Like. For now, use a placeholder
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Like, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.ResultValue, rhsValue.ResultValue)!;
    }
}
