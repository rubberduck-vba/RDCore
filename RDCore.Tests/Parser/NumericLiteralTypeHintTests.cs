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
        Public Const DExponentLower = 1.5d3
        Public Const DExponentUpper = 15D2
        Public Const EExponent = 1.5e3
        """;

    private static LiteralExpressionNode LiteralOf(string constName)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), _module);
        Assert.IsNotNull(result.SyntaxTree);

        var constant = Descendants(result.SyntaxTree!)
            .OfType<ConstantDeclarationNode>()
            .Single(node => node.Name == constName);
        return Descendants(constant).OfType<LiteralExpressionNode>().Single();
    }

    private static Dictionary<string, Type> LiteralTypesByConstName()
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), _module);
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
    // MS-VBAL §3.3.2: a FLOATLITERAL exponent letter is [DEde]; D is the legacy double marker.
    [DataRow("DExponentLower", typeof(VBDoubleValue))]
    [DataRow("DExponentUpper", typeof(VBDoubleValue))]
    [DataRow("EExponent", typeof(VBDoubleValue))]
    public void ResolvesLiteralStaticType(string constName, Type expected)
        => Assert.AreEqual(expected, LiteralTypesByConstName()[constName]);

    [TestMethod]
    // the D exponent must resolve to the same value as the equivalent E exponent, not be swallowed.
    [DataRow("DExponentLower", 1500.0)]
    [DataRow("DExponentUpper", 1500.0)]
    [DataRow("EExponent", 1500.0)]
    public void ResolvesDAndEExponentToTheSameValue(string constName, double expected)
    {
        var literal = LiteralOf(constName);
        Assert.IsInstanceOfType<VBDoubleValue>(literal.StaticValue);
        Assert.AreEqual(expected, ((VBDoubleValue)literal.StaticValue).Value);
    }

    [TestMethod]
    // an out-of-range literal is a located syntax error now, not a silent success (review C3).
    // the declaration pass and the #Const pass agree — both report it (review C4).
    [DataRow("Public Const N = 99999%", DisplayName = "declaration pass")]
    [DataRow("#Const N = 99999%\r\n#If N Then\r\n#End If", DisplayName = "#Const pass")]
    public void OverflowingLiteral_IsALocatedSyntaxError(string source)
    {
        var result = new ModuleParser().Parse(TestUri.TestModuleUri(), source);

        Assert.IsFalse(result.IsSuccess);
        var overflow = result.SyntaxErrors.Single(e => e.VBCompileErrorId == SDK.Model.Errors.VBCompileErrorId.NumericLiteralOverflow);
        Assert.AreEqual(TestUri.TestModuleUri(), overflow.Location.Uri);
        Assert.AreNotEqual(SDK.Model.Source.SourceRange.Empty, overflow.Location.Range);
    }
}
