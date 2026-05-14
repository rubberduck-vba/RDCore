using RDCore.Parsing;
using RDCore.Parsing.Model;
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
[TestCategory("MS-VBAL 5.6.9.3.6 Binary 'Mod' Operator")]
public class ModuloOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new ModuloOperatorRuntimeSemantics();
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
        Assert.AreEqual(Convert.ToDouble(expected), actual?.NumericValue);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.DivisionByZero")]
    [DataRow(1, 0, "VBR00011")]
    [DataRow(-1, 0, "VBR00011")]
    [DataRow(2.5, 0.5, "VBR00011")]
    public void EvaluateModulo_DivisionByZero(object lhs, object rhs, object expected)
    {
        try
        {
            _ = EvaluateModulo(CreateContext(), lhs, rhs) as INumericValue;
        }
        catch (VBRuntimeErrorException ex)
        {
            Assert.AreEqual(expected, ex.VBErrorNumber.ToDiagnosticCode());
        }
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, 5)]   // MS-VBAL: Null * Any -> Null
    [DataRow(null, "#1-1-2026#")]   // MS-VBAL: Null * Any -> Null
    [DataRow(null, 1.23d)]   // MS-VBAL: Null * Any -> Null
    //[DataRow(5, null)]   // MS-VBAL: Any * Null -> Null
    //[DataRow("Empty", null)]   // MS-VBAL: Any * Null -> Null
    //[DataRow("#2026-12-31#", null)]   // MS-VBAL: Any * Null -> Null
    public void EvaluateModulo_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateModulo_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBUserDefinedTypeValue>(TestLocation)
            .WithResultValue(new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateModulo(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateModulo_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBResizableArrayValue>(TestLocation)
            .WithResultValue(new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateModulo(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    //[DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateModulo_DateTimeLHS_ReturnsLong(object lhs, object rhs)
    {
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBLongValue>(result);
    }

    [TestMethod]
    [DataRow(10, "DateTime.Now")]
    public void EvaluateModulo_DateTimeRHS_ReturnsInteger(object lhs, object rhs)
    {
        var result = EvaluateModulo(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBIntegerValue>(result);
    }

    [TestMethod]
    [DataRow(32767, "DateTime.Now", Tokens.Integer)]
    [DataRow("Empty", (byte)10, Tokens.Integer)]
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
    [DataRow(-32767, 0.5d, "VBR00011")]
    [DataRow("1.5", 1, 2)]
    [DataRow(10, 1.5d, 5)]
    public void EvaluateModulo_NumericCoercion(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateModulo(CreateContext(), lhs, rhs);
            if (expected is not string)
            {
                Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).NumericValue, 0.0001);
            }
        }
        catch (VBRuntimeErrorException ex)
        {
            Assert.AreEqual(expected, ex.VBErrorNumber.ToDiagnosticCode());
        }
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, 2, "VBR00006")]
    [DataRow(-32768, 2, "VBR00006")]
    public void EvaluateModulo_Overflow(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateModulo(CreateContext(), lhs, rhs);
            if (expected is not string)
            {
                Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).NumericValue, 0.0001);
            }
        }
        catch (VBRuntimeErrorException ex)
        {
            Assert.AreEqual(expected, ex.VBErrorNumber.ToDiagnosticCode());
        }
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow(42, "VBErrorValue", "VBR00013")]
    [DataRow("ABC", "VBErrorValue", "VBR00013")]
    [DataRow("VBErrorValue", "VBErrorValue", "VBR00013")]
    public void EvaluateModulo_VBErrorValue_TypeMismatch(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateModulo(CreateContext(), lhs, rhs);
            if (expected is not string)
            {
                Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).NumericValue, 0.0001);
            }
        }
        catch (VBRuntimeErrorException ex)
        {
            Assert.AreEqual(expected, ex.VBErrorNumber.ToDiagnosticCode());
        }
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitDateSerialConversion")]
    [DataRow(-1, "DateTime.Now", true)]
    [DataRow("DateTime.Now", 1, true)]
    [DataRow("DateTime.Now", "DateTime.Now", true)]
    public void EvaluateModulo_ImplicitDateSerialConversionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateModulo(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitDateSerialConversion, assertMissing: !expectDiagnostics);
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitNumericCoercion")]
    [DataRow(40, 2, false, false)]
    [DataRow(-1, "42", false, true)]
    [DataRow("DateTime.Now", "42", false, true)]
    [DataRow("DateTime.Now", 1, false, false)]
    public void EvaluateModulo_ImplicitNumericCoercionDiagnostics(object lhs, object rhs, bool expectDiagnosticsLHS, bool expectDiagnosticsRHS)
    {
        var context = CreateContext();
        _ = EvaluateModulo(context, lhs, rhs);

        if (expectDiagnosticsLHS)
        {
            AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, TestLocationLHS.Range, assertMissing: !(expectDiagnosticsLHS || expectDiagnosticsRHS));
        }

        if (expectDiagnosticsRHS)
        {
            AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, TestLocationRHS.Range, assertMissing: !(expectDiagnosticsLHS || expectDiagnosticsRHS));
        }
    }

    private VBTypedValue EvaluateModulo(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocationLHS);
        var rhsValue = WrapLiteralExpression(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Modulo, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.ResultValue, rhsValue.ResultValue)!;
    }
}
