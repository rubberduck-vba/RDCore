using RDCore.Parsing;
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
[TestCategory("MS-VBAL 5.6.9.4: Binary '&' Operator")]
public class ConcatOperationTests : SymbolOperationTests
{
    [TestMethod]
    //[DataRow("Hello, ", "world!", "Hello, world!")]
    [DataRow(15, 2, "152")]
    //[DataRow("24", 5.5, "245.5")]
    public void EvaluateConcat_HappyPath_CalculatesResult(object lhs, object rhs, string expected)
    {
        var actual = EvaluateConcat(CreateContext(), lhs, rhs) as VBStringValue;
        Assert.AreEqual(expected, actual?.Value);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    [DataRow(null, null)]   
    public void EvaluateConcat_BothNullOperands_ResultIsNull(object lhs, object rhs)
    {
        var result = EvaluateConcat(CreateContext(), lhs, rhs);
        Assert.IsInstanceOfType<VBNullValue>(result);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateConcat_Null_LetCoercion_UDT_TypeMismatch()
    {
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBUserDefinedTypeValue>(TestLocation)
            .WithResultValue(new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateConcat(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10: Let-coercion from 'Null'")]
    public void EvaluateConcat_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression<VBResizableArrayValue>(TestLocation)
            .WithResultValue(new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateConcat(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("Diagnostics.ImplicitStringCoercion")]
    [DataRow("ABC", "XYZ", false)]
    [DataRow(-1, 42, true)]
    [DataRow("DateTime.Now", 1, true)]
    [DataRow(null, "42", false)]
    public void EvaluateConcat_ImplicitStringCoercionDiagnostics(object lhs, object rhs, bool expectDiagnostics)
    {
        var context = CreateContext();
        _ = EvaluateConcat(context, lhs, rhs);

        AssertDiagnostic(context, RDCoreDiagnosticId.ImplicitStringCoercion, assertMissing: !expectDiagnostics);
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.6 Let-coercion to and from resizable Byte()")]
    public void EvaluateConcat_ConcatenatesResizableByteArrays()
    {
        var lhs = new VBResizableArrayValue(0, 0, VBByteType.TypeInfo);
        lhs.Dimensions[0].State[0] = new VBByteValue().WithValue(65); // A
        var lhsType = new VBArrayType(lhs);
        lhs = lhs with { TypeInfo = lhsType };

        var rhs = new VBResizableArrayValue(0, 1, VBByteType.TypeInfo);
        rhs.Dimensions[0].State[0] = new VBByteValue().WithValue(66); // B
        rhs.Dimensions[0].State[1] = new VBByteValue().WithValue(67); // C
        var rhsType = new VBArrayType(rhs);
        rhs = rhs with { TypeInfo = rhsType };

        var result = EvaluateConcat(CreateContext(), lhs, rhs) as VBStringValue;

        Assert.AreEqual("ABC", result?.Value);
    }

    private VBTypedValue EvaluateConcat(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = Wrap(lhs, TestLocationLHS);
        var rhsValue = Wrap(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Concat, lhsValue, rhsValue, TestLocation);

        var semantics = new ConcatOperatorRuntimeSemantics();
        return semantics.Evaluate(context, expression, lhsValue.ResultValue, rhsValue.ResultValue)!;
    }
}
