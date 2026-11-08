using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.Runtime.LanguageCore.Static.Operators;
using RDCore.Tests.Semantics.Abstract;
using System.Reflection;

namespace RDCore.Tests.Semantics;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
public sealed class DivisionOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    public const string GetBinaryOperatorTypeMapName = "GetBinaryOperatorTypeMap";
    public const string GetTestNameMethod = nameof(GetTestName);
    public static string GetTestName(MethodInfo method, object[] data) => $"({((VBType)data[0]).Name}, {((VBType)data[1]).Name}):{((VBType)data[2]).Name}";
    protected sealed override StaticSemantics Semantics => new BinaryDivisionOperatorStaticSemantics();
    public static IEnumerable<object[]> GetBinaryOperatorTypeMap()
        => StaticSemanticsVBTypeMap.OverrideBinaryOperatorMap(
            (VBBooleanType.TypeInfo, VBBooleanType.TypeInfo, VBDoubleType.TypeInfo),
            (VBBooleanType.TypeInfo, VBBooleanType.TypeInfo, VBDoubleType.TypeInfo),
            (VBBooleanType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo),
            (VBBooleanType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo),
            (VBIntegerType.TypeInfo, VBBooleanType.TypeInfo, VBDoubleType.TypeInfo),
            (VBByteType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo),
            (VBByteType.TypeInfo, VBBooleanType.TypeInfo, VBDoubleType.TypeInfo),
            (VBByteType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo),
            (VBIntegerType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo),
            (VBIntegerType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongType.TypeInfo, VBBooleanType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBByteType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBBooleanType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBIntegerType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongLongType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongLongType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongLongType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongLongType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBByteType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBBooleanType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBIntegerType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBSingleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBStringType.TypeInfo, VBDoubleType.TypeInfo),
            (VBByteType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBIntegerType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongLongType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBSingleType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBStringType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBSingleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBStringType.TypeInfo, VBDoubleType.TypeInfo),
            (VBByteType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBIntegerType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongLongType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBSingleType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDecimalType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBByteType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBIntegerType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBLongLongType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBSingleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBDoubleType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBDecimalType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDateType.TypeInfo, VBStringType.TypeInfo, VBDoubleType.TypeInfo),
            (VBByteType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBIntegerType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBLongLongType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBSingleType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDoubleType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBDecimalType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo),
            (VBStringType.TypeInfo, VBDateType.TypeInfo, VBDoubleType.TypeInfo));

    [TestMethod]
    [DynamicData(GetBinaryOperatorTypeMapName, DynamicDataDisplayName = GetTestNameMethod)]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics)")]
    public void EvaluateBinaryOperatorStaticSemantics(VBType lhs, VBType rhs, VBType expected)
        => AssertDeterminedDeclaredType((lhs, rhs), expected);
}

