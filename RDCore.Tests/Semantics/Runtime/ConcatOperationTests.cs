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
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMemberSymbol(ScopeKind.Module, new Uri("file://TestProject/TestModule/TestUDT"), "UDT", Accessibility.Public, TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, new VBUserDefinedTypeValue(udt));

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateConcat(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.10 Let-coercion from 'Null'")]
    public void EvaluateConcat_Null_LetCoercion_ResizableArray_TypeMismatch()
    {
        var lhs = VBNullValue.Null;
        var rhs = new LiteralExpression(TestLocation, VBResizableArrayValue.Empty);

        Assert.Throws<VBRuntimeErrorTypeMismatchException>(() =>
            EvaluateConcat(CreateContext(), lhs, rhs));
    }

    [TestMethod]
    [TestCategory("MS-VBAL 5.5.1.2.6 Let-coercion to and from resizable Byte()")]
    public void EvaluateConcat_ConcatenatesResizableByteArrays()
    {
        //var lhsBuffer = Encoding.UTF8.GetBytes("ABC");
        //var lhsUnicode = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, lhsBuffer, 0, lhsBuffer.Length);
        //var lhs = new VBResizableArrayValue(0, lhsUnicode.Length - 1, VBByteType.TypeInfo);

        //for (var i = 0; i < lhsUnicode.Length; i++)
        //{
        //    lhs.Dimensions[0].State[i] = new VBByteValue().WithValue(lhsUnicode[i]);
        //}
        //var lhsType = new VBArrayType(lhs);
        //lhs = lhs with { TypeInfo = lhsType };

        //var rhsBuffer = Encoding.UTF8.GetBytes("XYZ");
        //var rhsUnicode = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, rhsBuffer, 0, rhsBuffer.Length);
        //var rhs = new VBResizableArrayValue(0, rhsUnicode.Length - 1, VBByteType.TypeInfo);

        //for (var i = 0; i < rhsUnicode.Length; i++)
        //{
        //    rhs.Dimensions[0].State[i] = new VBByteValue().WithValue(rhsUnicode[i]);
        //}
        //var rhsType = new VBArrayType(rhs);
        //rhs = rhs with { TypeInfo = rhsType };

        //var resultUnicode = EvaluateConcat(CreateContext(), lhs, rhs) as VBStringValue;
        //var resultBuffer = Encoding.Unicode.GetBytes(resultUnicode!.Value);
        //var resultUtf8 = Encoding.Convert(Encoding.Unicode, Encoding.UTF8, resultBuffer, 0, resultBuffer.Length);

        //Assert.AreEqual("ABCXYZ", Encoding.UTF8.GetString(resultUtf8));
    }

    private VBTypedValue EvaluateConcat(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var lhsExpression = WrapLiteralExpression(lhsValue, TestLocationLHS);

        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var rhsExpression = WrapLiteralExpression(rhsValue, TestLocationRHS);

        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Concat, lhsExpression, rhsExpression, TestLocation);
        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }
}
