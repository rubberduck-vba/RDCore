using RDCore.Parsing;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Parser;

[TestClass]
[TestCategory("RD-VBAL §3.2.0 Literal Expressions")]
public sealed class NumericLiteralTypeHintTests
{
    private const string _module = """
        Option Explicit
        Public Const HintInteger = 1%
        Public Const HintLong = 1&
        Public Const HintLongLong = 1^
        Public Const HintSingle = 1!
        Public Const HintDouble = 1#
        Public Const HintCurrency = 1@
        Public Const UnsuffixedSmall = 42
        Public Const UnsuffixedLong = 40000
        Public Const UnsuffixedDouble = 3000000000
        Public Const UnsuffixedHuge = 99999999999999999999
        Public Const UnsuffixedFloat = 2.5
        """;

    private static Dictionary<string, Type> LiteralTypesByConstName()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), ModuleType.StdModule, _module);
        Assert.IsNotNull(result.SyntaxTree);

        return Descendants(result.SyntaxTree!)
            .OfType<ConstantDeclarationNode>()
            .ToDictionary(
                node => node.Name,
                node => Descendants(node).OfType<LiteralExpressionNode>().Single().StaticValue.GetType());
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    [TestMethod]
    [DataRow("HintInteger", typeof(VBIntegerValue))]
    [DataRow("HintLong", typeof(VBLongValue))]
    [DataRow("HintLongLong", typeof(VBLongLongValue))]
    [DataRow("HintSingle", typeof(VBSingleValue))]
    [DataRow("HintDouble", typeof(VBDoubleValue))]
    [DataRow("HintCurrency", typeof(VBCurrencyValue))]
    [DataRow("UnsuffixedSmall", typeof(VBIntegerValue))]
    [DataRow("UnsuffixedLong", typeof(VBLongValue))]
    [DataRow("UnsuffixedDouble", typeof(VBDoubleValue))]
    // MS-VBAL §3.3.2 note: an unsuffixed integer past Long range widens to Double, never LongLong.
    [DataRow("UnsuffixedHuge", typeof(VBDoubleValue))]
    [DataRow("UnsuffixedFloat", typeof(VBDoubleValue))]
    public void ResolvesLiteralStaticType(string constName, Type expected)
        => Assert.AreEqual(expected, LiteralTypesByConstName()[constName]);
}
