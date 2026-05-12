using RDCore.Parsing;
using RDCore.Parsing.Model.Expressions.Operators;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model;
using RDCore.Runtime.Model.Operators;
using RDCore.Server;

namespace RDCore.Tests;


[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.4: Binary '*' Operator")]
public class MultiplicationOperationTests : SymbolOperationTests
{
    [TestMethod]
    [DataRow(2, 2, 4)]
    [DataRow(-2, 2, -4)]
    [DataRow(-2, -2, 4)]
    [DataRow(2, -2, -4)]
    [DataRow(0, 2, 0)]
    [DataRow(-2, 0, 0)]
    [DataRow(0, 0, 0)]
    public void EvaluateMultiplication_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateMultiplication(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.NumericValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, 5)]   // MS-VBAL: Null * Any -> Null
    [DataRow(null, "#1-1-2026#")]   // MS-VBAL: Null * Any -> Null
    [DataRow(null, 1.23d)]   // MS-VBAL: Null * Any -> Null
    [DataRow(5, null)]   // MS-VBAL: Any * Null -> Null
    [DataRow("Empty", null)]   // MS-VBAL: Any * Null -> Null
    [DataRow("#2026-12-31#", null)]   // MS-VBAL: Any * Null -> Null
    public void EvaluateMultiplication_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateMultiplication(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateMultiplication_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBUserDefinedTypeValue>(TestLocation)
            .WithResultValue(new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateMultiplication(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateMultiplication_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBResizableArrayValue>(TestLocation)
            .WithResultValue(new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateMultiplication(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateMultiplication_DateTime_ReturnsDouble(object lhs, object rhs)
    {
        var result = EvaluateMultiplication(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBDoubleValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.11: Let-coercion from 'Empty'")]
    public void EvaluateMultiplication_Empty_LetCoercion_Numeric_IsZero()
    {
        var depth = 0;
        var result = VBEmptyValue.Empty.AsCoercedNumeric(ref depth);
        Assert.AreEqual(0, result.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.11: Let-coercion from 'Empty'")]
    public void EvaluateMultiplication_Empty_LetCoercion_String_IsEmptyString()
    {
        var depth = 0;
        var result = VBEmptyValue.Empty.AsCoercedString(ref depth);
        Assert.AreEqual(VBStringValue.ZeroLengthString, result);
    }

    [TestMethod]
    [DataRow("1.5", 1, 1.5d)]
    //[DataRow(32767, -2.0d, -65534.0d)]
    public void EvaluateMultiplication_NumericCoercion(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateMultiplication(CreateContext(), lhs, rhs);
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
    public void EvaluateMultiplication_Overflow(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateMultiplication(CreateContext(), lhs, rhs);
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
    public void EvaluateMultiplication_VBErrorValue_TypeMismatch(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateMultiplication(CreateContext(), lhs, rhs);
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
    public void EvaluateMultiplication_ImplicitDateSerialConversionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateMultiplication(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitDateSerialConversion, assertMissing: !expectDiagnostics);
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitNumericCoercion")]
    [DataRow(40, 2, false)]
    [DataRow(-1, "42", true)]
    [DataRow("DateTime.Now", 1, false)]
    [DataRow("DateTime.Now", "42", true)]
    public void EvaluateMultiplication_ImplicitNumericCoercionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateMultiplication(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, assertMissing: !expectDiagnostics);
    }


    private VBTypedValue EvaluateMultiplication(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = Wrap(lhs, TestLocationLHS);
        var rhsValue = Wrap(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Multiplication, lhsValue, rhsValue, TestLocation);

        return SymbolOperation.EvaluateBinaryMultiplication(context, expression, lhsValue.ResultValue, rhsValue.ResultValue);
    }
}
