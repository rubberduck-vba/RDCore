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
[TestCategory("MS-VBAL 5.6.9.4 Binary '&' Operator")]
public class ConcatOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new BinaryConcatOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBStringType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow("Hello, ", "world!", "Hello, world!")]
    [DataRow(15, 2, "152")]
    [DataRow("24", 5.5, "245.5")]
    public void EvaluateConcat_HappyPath_CalculatesResult(object lhs, object rhs, string expected)
    {
        var actual = EvaluateConcat(CreateContext(), lhs, rhs) as VBStringValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    [DataRow(null, null)]   
    public void EvaluateConcat_BothNullOperands_ResultIsNull(object lhs, object rhs)
    {
        var result = EvaluateConcat(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateConcat_Null_LetCoercion_UDT_TypeMismatch()
    {
        var name = "TestUDT";
        var symbol = new VBUserDefinedTypeMemberSymbol(
            ScopeKind.Module, 
            TestUri.TestModuleUserDefinedTypeUri(name), 
            name, Accessibility.Private, 
            TestLocation.Range, 
            TestLocation.Range, 
            TestUri.WorkspaceRoot());
        var udt = new VBUserDefinedType(symbol, [], []);

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt, symbol));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateConcat(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateConcat_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() => EvaluateConcat(CreateContext(), lhs, rhs));
    }

    private VBTypedValue EvaluateConcat(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var lhsExpression = WrapLiteralExpression(lhsValue, TestLocationLHS);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsExpression = WrapLiteralExpression(rhsValue, TestLocationRHS);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.OperatorSymbols.Concat, lhsExpression, rhsExpression, TestLocation);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
