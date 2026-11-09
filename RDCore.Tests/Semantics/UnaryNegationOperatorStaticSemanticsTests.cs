using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.Runtime.LanguageCore.Static.Operators;
using RDCore.Tests.Semantics.Abstract;
using System.Reflection;

namespace RDCore.Tests.Semantics;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
public sealed class UnaryNegationOperatorStaticSemanticsTests : UnaryOperatorStaticSemanticsTests
{
    public const string GetUnaryOperatorTypeMapName = "GetUnaryOperatorTypeMap";
    public const string GetTestNameMethod = nameof(GetTestName);
    public static string GetTestName(MethodInfo method, object[] data) => $"({((VBType)data[0]).Name}):{((VBType)data[1]).Name}";

    protected sealed override StaticSemantics Semantics => new UnaryNegationOperatorStaticSemantics();

    public static IEnumerable<object[]> GetUnaryOperatorTypeMap()
        => StaticSemanticsVBTypeMap.OverrideUnaryOperatorMap((VBByteType.TypeInfo, VBIntegerType.TypeInfo));

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics / unary operators)
    /// </summary>
    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics)")]
    [DynamicData(GetUnaryOperatorTypeMapName, DynamicDataDisplayName = GetTestNameMethod)]
    public void EvaluateUnaryOperatorStaticSemantics(VBType operand, VBType expected)
        => AssertDeterminedDeclaredType(operand, expected);
}

