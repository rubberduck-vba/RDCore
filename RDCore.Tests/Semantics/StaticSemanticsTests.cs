using NSubstitute;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Operators;

namespace RDCore.Tests.Semantics;

// FIXME: this is the entire matrix of specified (MS-VBAL) static semantics for operators. and it counts as a single test... fix it.

public abstract class StaticSemanticsTests
{
    internal abstract IStaticSemantics Semantics { get; }

    internal virtual Dictionary<VBType, VBType> UnaryOperatorTypeMap { get; } = new()
    {
        [VBByteType.TypeInfo] = VBByteType.TypeInfo,
        [VBBooleanType.TypeInfo] = VBIntegerType.TypeInfo,
        [VBIntegerType.TypeInfo] = VBIntegerType.TypeInfo,
        [VBLongType.TypeInfo] = VBLongType.TypeInfo,
        [VBLongLongType.TypeInfo] = VBLongLongType.TypeInfo,
        [VBSingleType.TypeInfo] = VBSingleType.TypeInfo,
        [VBDoubleType.TypeInfo] = VBDoubleType.TypeInfo,
        [VBStringType.TypeInfo] = VBDoubleType.TypeInfo,
        [VBCurrencyType.TypeInfo] = VBCurrencyType.TypeInfo,
        [VBDateType.TypeInfo] = VBDateType.TypeInfo,
        [VBVariantType.TypeInfo] = VBVariantType.TypeInfo,
    };

    internal virtual Dictionary<(VBType, VBType), VBType> BinaryOperatorTypeMap { get; } = new()
    {
        [(VBByteType.TypeInfo, VBByteType.TypeInfo)] = VBByteType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBByteType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBBooleanType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBIntegerType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBByteType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBBooleanType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBIntegerType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBByteType.TypeInfo, VBBooleanType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBByteType.TypeInfo, VBIntegerType.TypeInfo)] = VBIntegerType.TypeInfo,
        [(VBLongType.TypeInfo, VBByteType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBLongType.TypeInfo, VBBooleanType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBLongType.TypeInfo, VBIntegerType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBLongType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBByteType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBByteType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBIntegerType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBByteType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBLongType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongLongType.TypeInfo,
        [(VBSingleType.TypeInfo, VBByteType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBBooleanType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBIntegerType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBByteType.TypeInfo, VBSingleType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBBooleanType.TypeInfo, VBSingleType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBSingleType.TypeInfo)] = VBSingleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBLongType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBByteType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBSingleType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBStringType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBByteType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBIntegerType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBLongType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBLongLongType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBSingleType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBStringType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBByteType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBSingleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBDecimalType.TypeInfo, VBCurrencyType.TypeInfo)] = VBCurrencyType.TypeInfo,
        [(VBDateType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBByteType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBIntegerType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBLongType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBLongLongType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBSingleType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBDoubleType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBDecimalType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDateType.TypeInfo, VBStringType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBByteType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBLongType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBSingleType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBDecimalType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBStringType.TypeInfo, VBDateType.TypeInfo)] = VBDateType.TypeInfo,
        [(VBVariantType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBByteType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBBooleanType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBIntegerType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBLongType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBLongLongType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBSingleType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBDoubleType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBCurrencyType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBDecimalType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBDateType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBStringType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBObjectType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBNullType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBEmptyType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBVariantType.TypeInfo, VBErrorType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBByteType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBIntegerType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBLongType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBLongLongType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBSingleType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBDoubleType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBCurrencyType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBDecimalType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBDateType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBStringType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBObjectType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
        [(VBNullType.TypeInfo, VBVariantType.TypeInfo)] = VBVariantType.TypeInfo,
    };

    internal void AssertDeterminedDeclaredType(VBType[] operandDeclaredTypes, VBType expected)
    {
        var context = Substitute.For<IVBExecutionContext>();
        var result = Semantics.DetermineDeclaredType(context, operandDeclaredTypes);
        Assert.AreEqual(expected, result);
    }
}

public abstract class UnaryOperatorStaticSemanticsTests : StaticSemanticsTests
{
    internal void AssertDeterminedDeclaredType(VBType operandDeclaredType, VBType expected) => AssertDeterminedDeclaredType([operandDeclaredType], expected);
}

public abstract class BinaryOperatorStaticSemanticsTests : StaticSemanticsTests
{
    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics)")]
    public void EvaluateBinaryOperatorStaticSemantics()
    {
        var context = Substitute.For<IVBExecutionContext>();
        foreach (var kvp in BinaryOperatorTypeMap)
        {

            var actual = Semantics.DetermineDeclaredType(context, kvp.Key.Item1, kvp.Key.Item2);
            Assert.AreEqual(kvp.Value.Name, actual?.Name, $"{Semantics.GetType().Name}({kvp.Key.Item1.Name},{kvp.Key.Item2.Name})");
        }
    }

    internal void AssertDeterminedDeclaredType((VBType lhs, VBType rhs) operandDeclaredTypes, VBType expected) => AssertDeterminedDeclaredType([operandDeclaredTypes.lhs, operandDeclaredTypes.rhs], expected);
}

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.1 Unary '-' Operator")]
public sealed class UnaryNegationOperatorStaticSemanticsTests : UnaryOperatorStaticSemanticsTests
{
    internal sealed override IStaticSemantics Semantics => new UnaryNegationOperatorStaticSemantics();

    internal sealed override Dictionary<VBType, VBType> UnaryOperatorTypeMap
    {
        get
        {
            var map = base.UnaryOperatorTypeMap;
            map[VBByteType.TypeInfo] = VBIntegerType.TypeInfo;
            return map;
        }
    }

    /// <summary>
    /// MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics / unary operators)
    /// </summary>
    [TestMethod]
    [TestCategory("MS-VBAL 5.6.9.3 Arithmetic Operators (static semantics)")]
    public void EvaluateUnaryOperatorStaticSemantics()
    {
        var context = Substitute.For<IVBExecutionContext>();
        foreach (var kvp in UnaryOperatorTypeMap)
        {
            var actual = Semantics.DetermineDeclaredType(context, kvp.Key);
            Assert.AreEqual(kvp.Value, actual, kvp.Key.Name);
        }
    }
}

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.2 Binary '+' Operator")]
public sealed class AdditionOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    internal sealed override IStaticSemantics Semantics => new BinaryAdditionOperatorStaticSemantics();

    internal sealed override Dictionary<(VBType, VBType), VBType> BinaryOperatorTypeMap
    {
        get
        {
            var map = base.BinaryOperatorTypeMap;
            map[(VBStringType.TypeInfo, VBStringType.TypeInfo)] = VBStringType.TypeInfo;
            return map;
        }
    }
}

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.3 Binary '-' Operator")]
public sealed class SubtractionOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    internal sealed override IStaticSemantics Semantics => new BinarySubtractionOperatorStaticSemantics();

    internal sealed override Dictionary<(VBType, VBType), VBType> BinaryOperatorTypeMap
    {
        get
        {
            var map = base.BinaryOperatorTypeMap;
            map[(VBDateType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            return map;
        }
    }
}

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.4 Binary '*' Operator")]
public sealed class MultiplicationOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    internal sealed override IStaticSemantics Semantics => new BinaryMultiplicationOperatorStaticSemantics();

    internal sealed override Dictionary<(VBType, VBType), VBType> BinaryOperatorTypeMap
    {
        get
        {
            var map = base.BinaryOperatorTypeMap;
            map[(VBCurrencyType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBStringType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDecimalType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBStringType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDecimalType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;

            return map;
        }
    }
}

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.5 Binary '/' Operator")]
public sealed class DivisionOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    internal sealed override IStaticSemantics Semantics => new BinaryDivisionOperatorStaticSemantics();

    internal sealed override Dictionary<(VBType, VBType), VBType> BinaryOperatorTypeMap
    {
        get
        {
            var map = base.BinaryOperatorTypeMap;
            map[(VBBooleanType.TypeInfo, VBBooleanType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBBooleanType.TypeInfo, VBBooleanType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBBooleanType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBBooleanType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBBooleanType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBBooleanType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBBooleanType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBBooleanType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBBooleanType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBStringType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDecimalType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDecimalType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDecimalType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBStringType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;

            return map;
        }
    }
}

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.6 Binary '\\' Operator")]
[TestCategory("MS-VBAL 5.6.9.3.6 Binary 'Mod' Operator")]
public sealed class IntegerDivisionOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    internal sealed override IStaticSemantics Semantics => new BinaryIntegerDivisionOperatorStaticSematics();

    internal sealed override Dictionary<(VBType, VBType), VBType> BinaryOperatorTypeMap
    {
        get
        {
            var map = base.BinaryOperatorTypeMap;
            // MS-VBAL 5.6.9.3.6.
            map[(VBSingleType.TypeInfo, VBByteType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBBooleanType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBIntegerType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBByteType.TypeInfo, VBSingleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBBooleanType.TypeInfo, VBSingleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBSingleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBLongType.TypeInfo, VBSingleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBSingleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBByteType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBIntegerType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBSingleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBStringType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBByteType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBStringType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBByteType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBIntegerType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBSingleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBStringType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBByteType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDecimalType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBByteType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBIntegerType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBLongType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBLongLongType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBSingleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDoubleType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBCurrencyType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDecimalType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDateType.TypeInfo, VBStringType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBByteType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBLongType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBDecimalType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;
            map[(VBStringType.TypeInfo, VBDateType.TypeInfo)] = VBLongType.TypeInfo;

            return map;
        }
    }
}

[TestClass]
[TestCategory("MS-VBAL 5.6.9.3.7 Binary '^' Operator")]
public sealed class ExponentOperatorStaticSemanticsTests : BinaryOperatorStaticSemanticsTests
{
    internal sealed override IStaticSemantics Semantics => new BinaryExponentOperatorStaticSemantics();

    internal sealed override Dictionary<(VBType, VBType), VBType> BinaryOperatorTypeMap
    {
        get
        {
            var map = base.BinaryOperatorTypeMap;
            map[(VBByteType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBStringType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDecimalType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDecimalType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBByteType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBIntegerType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBLongLongType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBSingleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDoubleType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBCurrencyType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBDecimalType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDateType.TypeInfo, VBStringType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBByteType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBIntegerType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBLongLongType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBSingleType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDoubleType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBCurrencyType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBDecimalType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;
            map[(VBStringType.TypeInfo, VBDateType.TypeInfo)] = VBDoubleType.TypeInfo;

            return map;
        }
    }
}

