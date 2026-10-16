using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Operators;
using RDCore.Tests.Semantics.Abstract;
using System.Reflection;

namespace RDCore.Tests.Semantics;

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
[TestCategory("MS-VBAL 5.6.9.3.6 Binary 'Mod' Operator")]
public sealed class IntegerDivisionOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    public const string GetBinaryOperatorTypeMapName = "GetBinaryOperatorTypeMap";
    public const string GetTestNameMethod = nameof(GetTestName);
    public static string GetTestName(MethodInfo method, object[] data) => $"({((VBType)data[0]).Name}, {((VBType)data[1]).Name}):{((VBType)data[2]).Name}";
    protected sealed override StaticSemantics Semantics => new BinaryIntegerDivisionOperatorStaticSematics();

    public static IEnumerable<object[]> GetBinaryOperatorTypeMap()
        => StaticSemanticsVBTypeMap.OverrideBinaryOperatorMap(
            // a LongLong divided by a non-integral value is a LongLong: TypeName(7^ \ 2.5) is "LongLong" in MS-VBA.
            (VBSingleType.TypeInfo, VBByteType.TypeInfo, VBLongType.TypeInfo),
            (VBSingleType.TypeInfo, VBBooleanType.TypeInfo, VBLongType.TypeInfo),
            (VBSingleType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo),
            (VBByteType.TypeInfo, VBSingleType.TypeInfo, VBLongType.TypeInfo),
            (VBBooleanType.TypeInfo, VBSingleType.TypeInfo, VBLongType.TypeInfo),
            (VBIntegerType.TypeInfo, VBSingleType.TypeInfo, VBLongType.TypeInfo),
            (VBSingleType.TypeInfo, VBLongType.TypeInfo, VBLongType.TypeInfo),
            (VBSingleType.TypeInfo, VBLongLongType.TypeInfo, VBLongLongType.TypeInfo),
            (VBLongType.TypeInfo, VBSingleType.TypeInfo, VBLongType.TypeInfo),
            (VBLongLongType.TypeInfo, VBSingleType.TypeInfo, VBLongLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBByteType.TypeInfo, VBLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBLongType.TypeInfo, VBLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBLongLongType.TypeInfo, VBLongLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBSingleType.TypeInfo, VBLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBDoubleType.TypeInfo, VBLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBStringType.TypeInfo, VBLongType.TypeInfo),
            (VBByteType.TypeInfo, VBDoubleType.TypeInfo, VBLongType.TypeInfo),
            (VBIntegerType.TypeInfo, VBDoubleType.TypeInfo, VBLongType.TypeInfo),
            (VBLongType.TypeInfo, VBDoubleType.TypeInfo, VBLongType.TypeInfo),
            (VBLongLongType.TypeInfo, VBDoubleType.TypeInfo, VBLongLongType.TypeInfo),
            (VBSingleType.TypeInfo, VBDoubleType.TypeInfo, VBLongType.TypeInfo),
            (VBStringType.TypeInfo, VBDoubleType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBByteType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBLongType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBLongLongType.TypeInfo, VBLongLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBSingleType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBCurrencyType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBStringType.TypeInfo, VBLongType.TypeInfo),
            (VBByteType.TypeInfo, VBCurrencyType.TypeInfo, VBLongType.TypeInfo),
            (VBIntegerType.TypeInfo, VBCurrencyType.TypeInfo, VBLongType.TypeInfo),
            (VBLongType.TypeInfo, VBCurrencyType.TypeInfo, VBLongType.TypeInfo),
            (VBLongLongType.TypeInfo, VBCurrencyType.TypeInfo, VBLongLongType.TypeInfo),
            (VBSingleType.TypeInfo, VBCurrencyType.TypeInfo, VBLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo, VBLongType.TypeInfo),
            (VBDecimalType.TypeInfo, VBCurrencyType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBByteType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBIntegerType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBLongType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBLongLongType.TypeInfo, VBLongLongType.TypeInfo),
            (VBDateType.TypeInfo, VBSingleType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBDoubleType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBCurrencyType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBDecimalType.TypeInfo, VBLongType.TypeInfo),
            (VBDateType.TypeInfo, VBStringType.TypeInfo, VBLongType.TypeInfo),
            (VBByteType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo),
            (VBIntegerType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo),
            (VBLongType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo),
            (VBLongLongType.TypeInfo, VBDateType.TypeInfo, VBLongLongType.TypeInfo),
            (VBSingleType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo),
            (VBDoubleType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo),
            (VBCurrencyType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo),
            (VBDecimalType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo),
            (VBStringType.TypeInfo, VBDateType.TypeInfo, VBLongType.TypeInfo));

    [TestMethod]
    [DynamicData(GetBinaryOperatorTypeMapName, DynamicDataDisplayName = GetTestNameMethod)]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics)")]
    public void EvaluateBinaryOperatorStaticSemantics(VBType lhs, VBType rhs, VBType expected)
        => AssertDeterminedDeclaredType((lhs, rhs), expected);

    // MS-VBAL 5.6.9.3.7: Mod shares the table of '\'.
    [TestMethod]
    [DynamicData(GetBinaryOperatorTypeMapName, DynamicDataDisplayName = GetTestNameMethod)]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics)")]
    public void EvaluateModuloOperatorStaticSemantics(VBType lhs, VBType rhs, VBType expected)
        => AssertDeterminedDeclaredType(new BinaryModuloOperatorStaticSemantics(), [lhs, rhs], expected);
}

