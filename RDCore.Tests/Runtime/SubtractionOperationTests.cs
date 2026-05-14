using RDCore.Parsing;
using RDCore.Parsing.Model.Expressions.Operators;
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
[TestCategory("MS-VBAL 5.6.9.3.3: Binary '-' Operator")]
public class SubtractionOperationTests : SymbolOperationTests
{
    [TestMethod]
    [DataRow(2, 2, 0)]
    [DataRow(-2, 2, -4)]
    [DataRow(-2, -2, 0)]
    [DataRow(2, -2, 4)]
    [DataRow(0, -2, 2)]
    [DataRow(-2, 0, -2)]
    [DataRow(0, 0, 0)]
    public void EvaluateSubtraction_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateSubtraction(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.NumericValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, 5)]   // MS-VBAL: Null - Any -> Null
    [DataRow(null, "#1-1-2026#")]   // MS-VBAL: Null - Any -> Null
    [DataRow(null, 1.23d)]   // MS-VBAL: Null - Any -> Null
    [DataRow(5, null)]   // MS-VBAL: Any - Null -> Null
    [DataRow("Empty", null)]   // MS-VBAL: Any - Null -> Null
    [DataRow("#2026-12-31#", null)]   // MS-VBAL: Any - Null -> Null
    public void EvaluateSubtraction_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateSubtraction(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateSubtraction_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBUserDefinedTypeValue>(TestLocation)
            .WithResultValue(new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateSubtraction(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateSubtraction_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBResizableArrayValue>(TestLocation)
            .WithResultValue(new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateSubtraction(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow("1.5", 1, 0.5d)]         // String - Integer -> Double
    [DataRow(32767, -1.0, 32768.0d)]   // Integer.MaxValue - -Double -> Double (Safe)
    public void EvaluateSubtraction_NumericCoercion(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateSubtraction(CreateContext(), lhs, rhs);
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
    [DataRow(32767, -1, "VBR00006")]
    [DataRow(-32768, 1, "VBR00006")]
    public void EvaluateSubtraction_Overflow(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateSubtraction(CreateContext(), lhs, rhs);
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
    [DataRow("1.5", "1", "VBR00013")]
    [DataRow(42, "VBErrorValue", "VBR00013")]
    [DataRow("ABC", "VBErrorValue", "VBR00013")]
    [DataRow("VBErrorValue", "VBErrorValue", "VBR00013")]
    public void EvaluateSubtraction_TypeMismatch(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateSubtraction(CreateContext(), lhs, rhs);
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
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateSubtraction_DateTime_ReturnsDateTime(object lhs, object rhs)
    {
        var result = EvaluateSubtraction(CreateContext(), lhs, rhs);
        if (string.Equals(lhs, "DateTime.Now") && string.Equals(rhs, "DateTime.Now"))
        {
            Assert.IsInstanceOfType<VBDoubleValue>(result);
        }
        else
        {
            Assert.IsInstanceOfType<VBDateValue>(result);
        }
    }

    [TestMethod]
    public void EvaluateSubtraction_BothEmpty_ResultsIsIntegerZero()
    {
        var result = EvaluateSubtraction(CreateContext(), "Empty", "Empty");

        Assert.IsInstanceOfType<VBIntegerValue>(result);
        Assert.AreEqual(0, ((VBIntegerValue)result).Value);
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitDateSerialConversion")]
    [DataRow(-1, "DateTime.Now", true)]
    [DataRow("DateTime.Now", 1, true)]
    [DataRow("DateTime.Now", "DateTime.Now", true)]
    public void EvaluateSubtraction_ImplicitDateSerialConversionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateSubtraction(context, lhs, rhs);

        Assert.AreEqual(expectDiagnostics, context.Diagnostics.Any(e => e.Code == RDCoreDiagnosticId.ImplicitDateSerialConversion.ToDiagnosticCode()));
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitNumericCoercion")]
    [DataRow(40, 2, false)]
    [DataRow(-1, "42", true)]
    [DataRow("DateTime.Now", 1, false)]
    [DataRow("DateTime.Now", 1.5d, false)]
    [DataRow("DateTime.Now", "42", true)]
    public void EvaluateSubtraction_ImplicitNumericCoercionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateSubtraction(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, assertMissing: !expectDiagnostics);
    }

    private VBTypedValue EvaluateSubtraction(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = Wrap(lhs, TestLocationLHS);
        var rhsValue = Wrap(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Subtraction, lhsValue, rhsValue, TestLocation);

        var semantics = new SubtractionOperatorRuntimeSematics();
        return semantics.Evaluate(context, expression, lhsValue.ResultValue, rhsValue.ResultValue)!;
    }
}
