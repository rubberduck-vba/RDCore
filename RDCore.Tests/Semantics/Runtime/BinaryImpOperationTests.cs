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
[TestCategory("MS-VBAL 5.6.9.8.6 Binary 'Imp' Operator")]
public class BinaryImpOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryImpBitwiseOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBBooleanType.TypeInfo,
        VBByteType.TypeInfo,
        VBIntegerType.TypeInfo,
        VBLongType.TypeInfo,
        VBLongLongType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow(0, 0, -1)]  // False Imp False = True (-1 in VB)
    [DataRow(0, -1, -1)]  // False Imp True = True (-1 in VB)
    [DataRow(-1, 0, 0)]   // True Imp False = False (0 in VB)
    [DataRow(-1, -1, -1)]  // True Imp True = True (-1 in VB)
    public void EvaluateImp_BitwiseContext_CalculatesResult(int lhs, int rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateImp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, (int?)actual?.Value);
    }

    [TestMethod]
    [DataRow(1, 2, -2)]
    [DataRow(5, 3, -5)]
    [DataRow(0, 0, -1)]
    public void EvaluateImp_IntegerBitwise_CalculatesResult(int lhs, int rhs, int expected)
    {
        var context = CreateContext();
        var actual = EvaluateImp(context, lhs, rhs) as VBIntegerValue;
        Assert.AreEqual(expected, (int?)actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateImp_BothNullOperands_ResultIsNull()
    {
        var result = EvaluateImp(CreateContext(), null, null);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(0, null)]    // False Imp Null = -1
    [DataRow(null, -1)]   // Null Imp True = -1
    public void EvaluateImp_SingleNullOperand_ManagedValue(object lhs, object rhs)
    {
        var result = EvaluateImp(CreateContext(), lhs, rhs);
        Assert.IsTrue(result is VBNumericTypedValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(-1, null)]   // True Imp Null = Null
    [DataRow(null, 0)]    // Null Imp False = Null
    public void EvaluateImp_SingleNullOperand_NullValue(object lhs, object rhs)
    {
        var result = EvaluateImp(CreateContext(), lhs, rhs);
        Assert.IsTrue(result is VBNullValue);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateImp_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMemberSymbol(ScopeKind.Module, new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", Accessibility.Private, TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateImp(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateImp_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateImp(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [DataRow(0, 0, true)]   // False Imp False = True
    [DataRow(0, -1, true)]  // False Imp True = True
    [DataRow(-1, 0, false)]  // True Imp False = False
    [DataRow(-1, -1, true)]  // True Imp True = True
    public void EvaluateImp_BooleanSemantics(object lhs, object rhs, bool expected)
    {
        var context = CreateContext();
        var actual = EvaluateImp(context, lhs, rhs) as VBIntegerValue;
        var expectedValue = expected ? -1 : 0;
        Assert.AreEqual(expectedValue, (int?)actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(0, "VBErrorValue")]
    [DataRow("VBErrorValue", 0)]
    public void EvaluateImp_VBErrorValue_TypeMismatch(object lhs, object rhs)
    {
        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateImp(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateImp(IVBExecutionContext context, object? lhs, object? rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);

        var lhsExpression = new LiteralExpression(TestLocationLHS, lhsValue);
        var rhsExpression = new LiteralExpression(TestLocationRHS, rhsValue);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.BitwiseImp, lhsExpression, rhsExpression, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
