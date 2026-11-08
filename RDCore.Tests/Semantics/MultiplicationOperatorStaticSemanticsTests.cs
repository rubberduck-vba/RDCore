using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.Runtime.LanguageCore.Static.Operators;
using RDCore.Tests.Semantics.Abstract;
using System.Reflection;

namespace RDCore.Tests.Semantics;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
public sealed class MultiplicationOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    public const string GetBinaryOperatorTypeMapName = "GetBinaryOperatorTypeMap";
    public const string GetTestNameMethod = nameof(GetTestName);
    public static string GetTestName(MethodInfo method, object[] data) => $"({((VBType)data[0]).Name}, {((VBType)data[1]).Name}):{((VBType)data[2]).Name}";
    protected sealed override StaticSemantics Semantics => new BinaryMultiplicationOperatorStaticSemantics();

    public static IEnumerable<object[]> GetBinaryOperatorTypeMap()
        => StaticSemanticsVBTypeMap.OverrideBinaryOperatorMap(
            (VBCurrencyType.TypeInfo, VBSingleType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBCurrencyType.TypeInfo, VBStringType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBSingleType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBStringType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBStringType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBSingleType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDateType.TypeInfo, VBDecimalType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBByteType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBStringType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBIntegerType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBLongType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBLongLongType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBSingleType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDoubleType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBCurrencyType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo), 
            (VBDecimalType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo));

    [TestMethod]
    [DynamicData(GetBinaryOperatorTypeMapName, DynamicDataDisplayName = GetTestNameMethod)]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics)")]
    public void EvaluateBinaryOperatorStaticSemantics(VBType lhs, VBType rhs, VBType expected)
        => AssertDeterminedDeclaredType((lhs, rhs), expected);
}

