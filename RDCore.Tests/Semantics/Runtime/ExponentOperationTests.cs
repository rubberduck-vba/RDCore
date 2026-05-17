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
[TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
public class ExponentOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryExponentOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBDoubleType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(2, 2, 4)]
    [DataRow(2, 3, 8)]
    [DataRow(10, 2, 100)]
    public void EvaluateExponentiation_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateExponentiation(CreateContext(), lhs, rhs) as INumericValue;
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
    public void EvaluateExponentiation_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        // note: coercing the result to any other type would throw.
        var result = EvaluateExponentiation(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateExponentiation_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateExponentiation(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateExponentiation_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateExponentiation(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateExponentiation_DateTime_ReturnsDouble(object lhs, object rhs)
    {
        var result = EvaluateExponentiation(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBDoubleValue>(result);
    }

    [TestMethod]
    [DataRow("5", 2, 25d)]
    public void EvaluateExponentiation_NumericCoercion(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateExponentiation(CreateContext(), lhs, rhs);
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
    // TODO test all types
    [DataRow(32767, 2, "VBR00006")]
    [DataRow(-32768, 2, "VBR00006")]
    public void EvaluateExponentiation_Overflow(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateExponentiation(CreateContext(), lhs, rhs);
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
    public void EvaluateExponentiation_VBErrorValue_TypeMismatch(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateExponentiation(CreateContext(), lhs, rhs);
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
    public void EvaluateExponentiation_ImplicitDateSerialConversionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateExponentiation(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitDateSerialConversion, assertMissing: !expectDiagnostics);
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitNumericCoercion")]
    [DataRow(40, 2, false)]
    [DataRow(-1, "42", true)]
    [DataRow("DateTime.Now", 1, false)]
    [DataRow("DateTime.Now", "42", true)]
    public void EvaluateExponentiation_ImplicitNumericCoercionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateExponentiation(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, assertMissing: !expectDiagnostics);
    }

    private VBTypedValue EvaluateExponentiation(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocationLHS);
        var rhsValue = WrapLiteralExpression(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Exponentiation, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.RuntimeValue, rhsValue.RuntimeValue)!;
    }
}
