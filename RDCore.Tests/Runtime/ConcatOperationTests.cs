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
using System.Text;
using System.Text.Unicode;

namespace RDCore.Tests.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.4 Binary '&' Operator")]
public class ConcatOperationTests : SymbolOperationTests
{
    internal override RuntimeSemantics Semantics => new ConcatOperatorRuntimeSemantics();
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
        var udt = new VBUserDefinedType("Test", new VBUserDefinedTypeMember(new Uri("file://TestProject/TestModule/TestUDT"), "TestUDT", TestLocation.Range, TestLocation.Range, new Uri("file://TestProject")));

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
        var rhs = new LiteralExpression(TestLocation, new VBResizableArrayValue(0, 0, VBIntegerType.TypeInfo));

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
        var lhsBuffer = Encoding.UTF8.GetBytes("ABC");
        var lhsUnicode = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, lhsBuffer, 0, lhsBuffer.Length);
        var lhs = new VBResizableArrayValue(0, lhsUnicode.Length - 1, VBByteType.TypeInfo);

        for (var i = 0; i < lhsUnicode.Length; i++)
        {
            lhs.Dimensions[0].State[i] = new VBByteValue().WithValue(lhsUnicode[i]);
        }
        var lhsType = new VBArrayType(lhs);
        lhs = lhs with { TypeInfo = lhsType };

        var rhsBuffer = Encoding.UTF8.GetBytes("XYZ");
        var rhsUnicode = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, rhsBuffer, 0, rhsBuffer.Length);
        var rhs = new VBResizableArrayValue(0, rhsUnicode.Length - 1, VBByteType.TypeInfo);

        for (var i = 0; i < rhsUnicode.Length; i++)
        {
            rhs.Dimensions[0].State[i] = new VBByteValue().WithValue(rhsUnicode[i]);
        }
        var rhsType = new VBArrayType(rhs);
        rhs = rhs with { TypeInfo = rhsType };

        var resultUnicode = EvaluateConcat(CreateContext(), lhs, rhs) as VBStringValue;
        var resultBuffer = Encoding.Unicode.GetBytes(resultUnicode!.Value);
        var resultUtf8 = Encoding.Convert(Encoding.Unicode, Encoding.UTF8, resultBuffer, 0, resultBuffer.Length);

        Assert.AreEqual("ABCXYZ", Encoding.UTF8.GetString(resultUtf8));
    }

    private VBTypedValue EvaluateConcat(VBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapLiteralExpression(lhs, TestLocationLHS);
        var rhsValue = WrapLiteralExpression(rhs, TestLocationRHS);
        var expression = new VBBinaryOperatorExpression(GlobalSymbols.Concat, lhsValue, rhsValue, TestLocation);

        return Semantics.Evaluate(context, expression, lhsValue.ResultValue, rhsValue.ResultValue)!;
    }
}
