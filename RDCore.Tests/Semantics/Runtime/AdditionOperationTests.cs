using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Model;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;

namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
public class AdditionOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryAdditionOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBByteType.TypeInfo, 
        VBIntegerType.TypeInfo, 
        VBLongType.TypeInfo, 
        VBLongLongType.TypeInfo,
        VBSingleType.TypeInfo,
        VBDoubleType.TypeInfo,
        VBCurrencyType.TypeInfo,
        VBDecimalType.TypeInfo,
        VBDateType.TypeInfo,
        VBStringType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(2, 2, 4)]
    [DataRow(-2, 2, 0)]
    [DataRow(-2, -2, -4)]
    [DataRow(2, -2, 0)]
    [DataRow(0, 2, 2)]
    [DataRow(2, 0, 2)]
    [DataRow(0, 0, 0)]
    public void EvaluateAddition_HappyPath_CalculatesResult(object lhs, object rhs, object expected)
    {
        var actual = EvaluateAddition(CreateContext(), lhs, rhs) as INumericValue;
        Assert.AreEqual(Convert.ToDouble(expected), actual?.ManagedValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(null, 5)]
    [DataRow(null, "#1-1-2026#")]
    [DataRow(null, 1.23d)]
    [DataRow(5, null)]
    [DataRow("Empty", null)]
    [DataRow("#2026-12-31#", null)]
    public void EvaluateAddition_NullOperand_ResultIsNull(object lhs, object rhs)
    {
        var result = EvaluateAddition(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateAddition_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMemberSymbol(ScopeKind.Module, new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", Accessibility.Private, TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));
            
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateAddition(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateAddition_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateAddition(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow("1.5", 1, 2.5d)]         // String + Integer -> Double
    [DataRow("1.5", "1", "1.51")]         // String + String -> String
    [DataRow(32767, 1.0, 32768.0d)]   // Integer + Double -> Double (Safe)
    public void EvaluateAddition_NumericCoercion(object lhs, object rhs, object expected)
    {
        var result = EvaluateAddition(CreateContext(), lhs, rhs);
        if (expected is not string)
        {
            Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).ManagedValue, 0.0001);
        }
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.Overflow")]
    [DataRow(32767, 1, VBRuntimeErrorId.Overflow)]
    [DataRow(-32768, -1, VBRuntimeErrorId.Overflow)]
    public void EvaluateAddition_Overflow(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateAddition(CreateContext(), lhs, rhs);
            if (expected is not string)
            {
                Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).ManagedValue, 0.0001);
            }
        }
        catch (VBRuntimeErrorException ex)
        {
            Assert.AreEqual((int)expected, ex.VBErrorNumber);
        }
    }

    [TestMethod]
    [TestCategory("Diagnostics.VBRuntimeError.TypeMismatch")]
    [DataRow(42, "VBErrorValue", VBRuntimeErrorId.TypeMismatch)]
    [DataRow("ABC", "VBErrorValue", VBRuntimeErrorId.TypeMismatch)]
    [DataRow("VBErrorValue", "VBErrorValue", VBRuntimeErrorId.TypeMismatch)]
    public void EvaluateAddition_VBErrorValue_TypeMismatch(object lhs, object rhs, object expected)
    {
        try
        {
            var result = EvaluateAddition(CreateContext(), lhs, rhs);
            if (expected is not string)
            {
                Assert.AreEqual(Convert.ToDouble(expected), ((INumericValue)result).ManagedValue, 0.0001);
            }
        }
        catch (VBRuntimeErrorException ex)
        {
            Assert.AreEqual((int)expected, ex.VBErrorNumber);
        }
    }

    [TestMethod]
    [DataRow(-1, "DateTime.Now")]
    [DataRow("DateTime.Now", 1)]
    [DataRow("DateTime.Now", "DateTime.Now")]
    public void EvaluateAddition_DateTime_ReturnsDateTime(object lhs, object rhs)
    {
        var result = EvaluateAddition(CreateContext(), lhs, rhs);

        Assert.IsInstanceOfType<VBDateValue>(result);
    }

/*    [TestMethod]
    [TestCategory("Diagnostics.ImplicitDateSerialConversion")]
    [DataRow(-1, "DateTime.Now", true)]
    [DataRow("DateTime.Now", 1, true)]
    [DataRow("DateTime.Now", "DateTime.Now", true)]
    public void EvaluateAddition_ImplicitDateSerialConversionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateAddition(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitDateSerialConversion, assertMissing: !expectDiagnostics);
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitNumericCoercion")]
    [DataRow(40, 2, false)]
    [DataRow(-1, "42", true)]
    [DataRow("DateTime.Now", 1d, false)]
    [DataRow("DateTime.Now", "42", true)]
    public void EvaluateAddition_ImplicitNumericCoercionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateAddition(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitNumericCoercion, assertMissing: !expectDiagnostics);
    }
*/
    //[TestMethod]
    //[TestCategory("Diagnostics.AmbiguousConcatenation")]
    //[DataRow(40, 2, false)]
    //[DataRow(-2, "42", false)]
    //[DataRow("-2", 42, false)]
    //[DataRow("42", "-2", true)]
    //[DataRow("10", "2", true)]
    //public void EvaluateAddition_AmbiguousConcatenationDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    //{
    //    var context = CreateContext();
    //    _ = EvaluateAddition(context, lhs, rhs);

    //    AssertDiagnostic(context, RDCoreDiagnosticId.AmbiguousConcatenation, assertMissing: !expectDiagnostics);
    //}

    [TestMethod]
    public void EvaluateAddition_BothEmpty_ResultsIsIntegerZero()
    {
        var result = EvaluateAddition(CreateContext(), "Empty", "Empty");

        Assert.IsInstanceOfType<VBIntegerValue>(result);
        Assert.AreEqual(0, ((VBIntegerValue)result).Value);
    }

    private VBTypedValue EvaluateAddition(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);

        var lhsExpression = new LiteralExpression(TestLocationLHS, lhsValue);
        var rhsExpression = new LiteralExpression(TestLocationRHS, rhsValue);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Addition, lhsExpression, rhsExpression, TestLocation);

        if (lhsValue?.TypeInfo is null)
        {
            Assert.Inconclusive();
        }
        if (rhsValue?.TypeInfo is null)
        {
            Assert.Inconclusive();
        }

        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
