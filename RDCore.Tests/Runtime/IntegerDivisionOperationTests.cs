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

namespace RDCore.Tests;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.6: Binary '\\' Operator")]
public class IntegerDivisionOperationTests : SymbolOperationTests
{
    [TestMethod]
    [DataRow(2, 2, 1)]
    [DataRow(-2, 2, -1)]
    [DataRow(-2, -2, 1)]
    [DataRow(2, -2, -1)]
    [DataRow(12, 2, 6)]
    [DataRow(5, 2, 2)]
    public void EvaluateIntegerDivision_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateIntegerDivision(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.NumericValue);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.DivisionByZero")]
    [DataRow(1, 0, "VBR00011")]
    [DataRow(-1, 0, "VBR00011")]
    public void EvaluateIntegerDivision_IntegerDivisionByZero(object lhs, object rhs, object expected)
    {
        try
        {
            _ = EvaluateIntegerDivision(CreateContext(), lhs, rhs) as INumericValue;
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
    [DataRow(5, null)]   // MS-VBAL: Any * Null -> Null
    [DataRow("Empty", null)]   // MS-VBAL: Any * Null -> Null
    [DataRow("#2026-12-31#", null)]   // MS-VBAL: Any * Null -> Null
    public void EvaluateIntegerDivision_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateIntegerDivision(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateIntegerDivision_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBUserDefinedTypeValue>(TestLocation)
            .WithResultValue(new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateIntegerDivision(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateIntegerDivision_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBResizableArrayValue>(TestLocation)
            .WithResultValue(new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateIntegerDivision(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateIntegerDivision_DateTimeLHS_ReturnsLong(object lhs, object rhs)
    {
        var result = EvaluateIntegerDivision(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBLongValue>(result);
    }

    [TestMethod]
    [DataRow(42, "DateTime.Now")]
    public void EvaluateIntegerDivision_DateTimeRHS_ReturnsInteger(object lhs, object rhs)
    {
        var result = EvaluateIntegerDivision(CreateContext(), lhs, rhs);
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
    public void EvaluateIntegerDivision_DataType(object lhs, object rhs, string type)
    {
        var result = EvaluateIntegerDivision(CreateContext(), lhs, rhs);
        Assert.AreEqual(type, result.TypeInfo.Name);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.11: Let-coercion from 'Empty'")]
    public void EvaluateIntegerDivision_Empty_LetCoercion_Numeric_IsZero()
    {
        var depth = 0;
        var result = VBEmptyValue.Empty.AsCoercedDouble(ref depth);
        Assert.AreEqual(0, result.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.11: Let-coercion from 'Empty'")]
    public void EvaluateIntegerDivision_Empty_LetCoercion_String_IsEmptyString()
    {
        var depth = 0;
        var result = VBEmptyValue.Empty.AsCoercedString(ref depth);
        Assert.AreEqual(VBStringValue.ZeroLengthString, result);
    }

    [TestMethod]
    [DataRow(-32767, 0.5d, "VBR00011")]
    [DataRow("1.5", 1, 2)]
    [DataRow(10, 1.5d, 5)]
    public void EvaluateIntegerDivision_NumericCoercion(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateIntegerDivision(CreateContext(), lhs, rhs);
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
    public void EvaluateIntegerDivision_Overflow(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateIntegerDivision(CreateContext(), lhs, rhs);
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
    public void EvaluateIntegerDivision_VBErrorValue_TypeMismatch(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateIntegerDivision(CreateContext(), lhs, rhs);
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
    public void EvaluateIntegerDivision_ImplicitDateSerialConversionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateIntegerDivision(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitDateSerialConversion, assertMissing: !expectDiagnostics);
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitNumericCoercion")]
    [DataRow(40, 2, false, false)]
    [DataRow(-1, "42", false, true)]
    [DataRow("DateTime.Now", "42", false, true)]
    [DataRow("DateTime.Now", 1, false, false)]
    public void EvaluateIntegerDivision_ImplicitNumericCoercionDiagnostics(object lhs, object rhs, bool expectDiagnosticsLHS, bool expectDiagnosticsRHS)
    {
        var context = CreateContext();
        _ = EvaluateIntegerDivision(context, lhs, rhs);

        if (expectDiagnosticsLHS)
        {
            AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, TestLocationLHS.Range, assertMissing: !(expectDiagnosticsLHS || expectDiagnosticsRHS));
        }

        if (expectDiagnosticsRHS)
        {
            AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, TestLocationRHS.Range, assertMissing: !(expectDiagnosticsLHS || expectDiagnosticsRHS));
        }
    }

    private VBTypedValue EvaluateIntegerDivision(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = Wrap(lhs, TestLocationLHS);
        var rhsValue = Wrap(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.IntegerDivision, lhsValue, rhsValue, TestLocation);

        var semantics = new IntegerDivisionOperatorRuntimeSemantics();
        return semantics.Evaluate(context, expression, lhsValue.ResultValue, rhsValue.ResultValue)!;
    }
}
