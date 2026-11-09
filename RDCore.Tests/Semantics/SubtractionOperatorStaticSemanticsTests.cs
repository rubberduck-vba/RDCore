using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.Runtime.LanguageCore.Static.Operators;
using RDCore.Tests.Semantics.Abstract;
using System.Reflection;

namespace RDCore.Tests.Semantics;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
public sealed class SubtractionOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    public const string GetBinaryOperatorTypeMapName = "GetBinaryOperatorTypeMap";
    public const string GetTestNameMethod = nameof(GetTestName);
    public static string GetTestName(MethodInfo method, object[] data) => $"({((VBType)data[0]).Name}, {((VBType)data[1]).Name}):{((VBType)data[2]).Name}";

    protected sealed override StaticSemantics Semantics => new BinarySubtractionOperatorStaticSemantics();

    public static IEnumerable<object[]> GetBinaryOperatorTypeMap()
        => StaticSemanticsVBTypeMap.OverrideBinaryOperatorMap((VBDateType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo));

    [TestMethod]
    [DynamicData(GetBinaryOperatorTypeMapName, DynamicDataDisplayName = GetTestNameMethod)]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics)")]
    public void EvaluateBinaryOperatorStaticSemantics(VBType lhs, VBType rhs, VBType expected)
        => AssertDeterminedDeclaredType((lhs, rhs), expected);
}

