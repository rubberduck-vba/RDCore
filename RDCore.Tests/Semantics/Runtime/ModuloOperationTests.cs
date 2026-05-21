using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Model;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.6 Binary 'Mod' Operator")]
public class ModuloOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryModuloOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(24, 12, 0)]
    [DataRow(15, 2, 1)]
    [DataRow(24, 5, 4)]
    public void EvaluateModulo_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateModulo(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.DivisionByZero")]
    [DataRow(1, 0)]
    [DataRow(-1, 0)]
    [DataRow(2.5, 0.5)]
    public void EvaluateModulo_DivisionByZero(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorDivisionByZeroException>(() => EvaluateModulo(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, 5)]   // MS-VBAL: Null * Any -> Null
    [DataRow(null, "#1-1-2026#")]   // MS-VBAL: Null * Any -> Null
    [DataRow(null, 1.23d)]   // MS-VBAL: Null * Any -> Null
    public void EvaluateModulo_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [DataRow("32768", "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateModulo_GivenDateTimeLHS_ReturnsLong(object lhs, object rhs)
    {
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBLongValue>(result);
    }

    [TestMethod]
    [DataRow(42, "DateTime.Now")]
    public void EvaluateModulo_GivenDateTimeRHS_ReturnsInteger(object lhs, object rhs)
    {
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBIntegerValue>(result);
    }

    [TestMethod]
    [DataRow(10, "DateTime.Now")]
    public void EvaluateModulo_DateTimeRHS_ReturnsInteger(object lhs, object rhs)
    {
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBIntegerValue>(result);
    }

    [TestMethod]
    [DataRow("Empty", (byte)10, Tokens.Integer)]
    [DataRow(32767, "DateTime.Now", Tokens.Integer)]
    [DataRow(true, 2, Tokens.Integer)]
    [DataRow(32767, 2.5d, Tokens.Integer)]
    [DataRow(123.456d, 2.5d, Tokens.Long)]
    [DataRow("123.456", 2, Tokens.Long)]
    [DataRow("DateTime.Now", 2, Tokens.Long)]
    [DataRow((long)123456, 42, Tokens.LongLong)]
    [DataRow((long)123456, "42", Tokens.LongLong)]
    [DataRow((long)123456, "DateTime.Now", Tokens.LongLong)]
    public void EvaluateModulo_DataType(object lhs, object rhs, string type)
    {
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.AreEqual(type, result.TypeInfo.Name);
    }

    [TestMethod]
    [DataRow("1.5", 1, 0)]
    [DataRow(10, 1.5d, 0)]
    [DataRow(11, 4.0d, 3)]
    public void EvaluateModulo_NumericCoercion(object lhs, object rhs, object expected)
    {
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).ManagedValue, 0.0001);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, 2)]
    [DataRow(-32768, 2)]
    public void EvaluateModulo_Overflow(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorOverflowException>(() => EvaluateModulo(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateModulo(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var lhsExpression = WrapLiteralExpression(lhsValue, TestLocationLHS);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsExpression = WrapLiteralExpression(rhs, TestLocationRHS);
        
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.Modulo, lhsExpression, rhsExpression, TestLocation);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
