using RDCore.Parsing;
using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.Parsing.Model.Values.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;
using RDCore.Runtime;
using RDCore.Runtime.Model;
using RDCore.Runtime.Model.Operators;
using RDCore.Semantics.Diagnostics;
using RDCore.Semantics.Runtime.Abstract;
using RDCore.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
public class DivisionOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryDivisionOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(2, 2, 1)]
    [DataRow(-2, 2, -1)]
    [DataRow(-2, -2, 1)]
    [DataRow(2, -2, -1)]
    [DataRow(12, 2, 6)]
    [DataRow(5, 2, 2.5)]
    public void EvaluateDivision_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateDivision(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.NumericValue);
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.DivisionByZero")]
    [DataRow(1, 0, "VBR00011")]
    [DataRow(-1, 0, "VBR00011")]
    public void EvaluateDivision_DivisionByZero(object lhs, object rhs, object expected)
    {
        try
        {
            _ = EvaluateDivision(CreateContext(), lhs, rhs) as INumericValue;
        }
        catch (VBRuntimeErrorException ex)
        {
            Assert.AreEqual(expected, ex.VBErrorNumber.ToDiagnosticCode());
        }
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, null)]
    [DataRow(null, 5)]
    [DataRow(null, 1.23d)]
    [DataRow(null, "#1-1-2026#")]
    [DataRow(5, null)]
    [DataRow("Empty", null)]
    [DataRow(1.23d, null)]
    [DataRow("#2026-12-31#", null)]
    public void EvaluateDivision_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateDivision(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateDivision_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateDivision(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateDivision_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateDivision(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateDivision_DateTime_ReturnsDouble(object lhs, object rhs)
    {
        var result = EvaluateDivision(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBDoubleValue>(result);
    }

    [TestMethod]
    [DataRow("1.5", 1, 1.5d)]
    [DataRow(-32767, 0.5d, -65534.0d)]
    public void EvaluateDivision_NumericCoercion(object lhs, object rhs, object expected)
    {
        try
        {
            var context = CreateContext();
            var result = EvaluateDivision(context, lhs, rhs);
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
    [DataRow(0, 0, "VBR00006")]
    public void EvaluateDivision_Overflow(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateDivision(CreateContext(), lhs, rhs);
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
    public void EvaluateDivision_VBErrorValue_TypeMismatch(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateDivision(CreateContext(), lhs, rhs);
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
    public void EvaluateDivision_ImplicitDateSerialConversionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateDivision(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitDateSerialConversion, assertMissing: !expectDiagnostics);
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitNumericCoercion")]
    [DataRow("1.5", 1, true)]
    [DataRow(-32, 0.5d, false)]
    [DataRow("50", "2", true)]
    [DataRow(40, 2, false)]
    [DataRow(-1, "42", true)]
    [DataRow("DateTime.Now", 1, false)]
    [DataRow("DateTime.Now", "42", true)]
    public void EvaluateDivision_ImplicitNumericCoercionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateDivision(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, assertMissing: !expectDiagnostics);
    }

    private VBTypedValue EvaluateDivision(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocation);
        var rhsValue = WrapLiteralExpression(rhs, TestLocation);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Multiplication, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.RuntimeValue, rhsValue.RuntimeValue)!;
    }
}
