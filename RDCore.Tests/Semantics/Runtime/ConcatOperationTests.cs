using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Semantics.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Operators;
namespace RDCore.Tests.Semantics.Runtime;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.4 Binary '&' Operator")]
public class ConcatOperationTests : BinaryOperatorOperationTests
{
    protected override BinaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.ConcatOp;
    internal override IRuntimeSemantics Semantics => new BinaryConcatOperatorRuntimeSemantics();
    internal override IEnumerable<VBType> EffectiveTypes => [
        VBStringType.TypeInfo,
        VBNullType.TypeInfo,
    ];

    [TestMethod]
    [DataRow("Hello, ", "world!", "Hello, world!")]
    [DataRow(15, 2, "152")]
    [DataRow("24", 5.5, "245.5")]
    public void Operator_CalculatesResult(object lhs, object rhs, string expected)
    {
        var actual = EvaluateBinaryOp(CreateContext(), lhs, rhs) as VBStringValue;
        Assert.AreEqual(expected, actual?.Value);
    }
}
