using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.Runtime.Semantics.Operators.Logical;
using RDCore.Runtime.Semantics.Operators.Relational;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Context.Abstract;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The flags each family of operators reports about an operation (MS-VBAL 5.6.9): the effective type it is evaluated in, and
/// the facts about its operands the family has flags for. Through <c>Analyze</c>, with the real operators over the real
/// let-coercion analysis.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.6.9 Operators (analysis)")]
public sealed class OperatorAnalysisFlagsTests : LetCoercionRuntimeSemanticsTests
{
    private static ILetCoercionRuntimeSemanticsProvider Provider() => LetCoercionAnalysisHarness.BuildProvider();

    private static VBUnaryOperatorExpressionNode UnaryOf(string token)
        => new(token, NodeId, TestLocations.TestLocation, [new LiteralExpressionNode(default, TestLocations.TestLocation, new VBIntegerValue((short)0))]);

    private static TFlags FlagsOf<TContext, TFlags>(IRuntimeSemantics<TContext, TFlags> semantics, params VBTypedValue[] operands)
        where TContext : SemanticContext<TFlags>, new() where TFlags : struct, Enum
        => OperatorAnalysisHarness.Analyze(semantics, operands.Length == 1 ? UnaryOf("-") : ThrowawayExpression, operands).Flags;

    private static VBObjectValue AnObject() => new(new ValueBindingHandle(new VBRuntimeValue<int>(1)));

    #region arithmetic (MS-VBAL 5.6.9.3)

    public static IEnumerable<object[]> ArithmeticOperators()
    {
        yield return [new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinarySubtractionOperatorRuntimeSematics(Provider(), Formatter())];
        yield return [new BinaryMultiplicationOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryDivisionOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryExponentOperatorRuntimeSemantics(Provider(), Formatter())];
    }

    [TestMethod]
    [DynamicData(nameof(ArithmeticOperators))]
    public void ANumericOperation_HasANumericEffectiveType(BinaryArithmeticOperatorRuntimeSemantics op)
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType, FlagsOf(op, new VBLongValue(6), new VBLongValue(3)));

    [TestMethod]
    public void ADateAdditionOrSubtraction_HasADateEffectiveType()
    {
        Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBDateEffectiveType, FlagsOf(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()), new VBDateValue(6), new VBLongValue(3)));
        Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBDateEffectiveType, FlagsOf(new BinarySubtractionOperatorRuntimeSematics(Provider(), Formatter()), new VBDateValue(6), new VBLongValue(3)));
    }

    // MS-VBAL 5.6.9.3.4-5.6.9.3.7: a Date is only a Date in a sum or a difference; any other arithmetic on it is done on its Double representation.
    [TestMethod]
    public void ADateMultiplicationDivisionOrExponentiation_HasANumericEffectiveType()
    {
        Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType, FlagsOf(new BinaryMultiplicationOperatorRuntimeSemantics(Provider(), Formatter()), new VBDateValue(6), new VBLongValue(3)));
        Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType, FlagsOf(new BinaryDivisionOperatorRuntimeSemantics(Provider(), Formatter()), new VBDateValue(6), new VBLongValue(3)));
        Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType, FlagsOf(new BinaryExponentOperatorRuntimeSemantics(Provider(), Formatter()), new VBDateValue(6), new VBLongValue(3)));
    }

    [TestMethod]
    [DynamicData(nameof(ArithmeticOperators))]
    public void ANullOperation_HasANullEffectiveType(BinaryArithmeticOperatorRuntimeSemantics op)
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNullEffectiveType, FlagsOf(op, VBNullValue.Null, new VBLongValue(3)));

    [TestMethod]
    [DynamicData(nameof(ArithmeticOperators))]
    public void AnErrorOperation_HasAnErrorEffectiveType(BinaryArithmeticOperatorRuntimeSemantics op)
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBErrorEffectiveType, FlagsOf(op, new VBErrorValue(5), new VBLongValue(3)));

    [TestMethod]
    public void ANumericOperandInAStringOperation_MakesANumericOperation()
        // a String operand is let-coerced to the numeric effective type: "6" + 3 is a Double sum, not a concatenation.
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType,
            FlagsOf(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()), new VBStringValue("6"), new VBLongValue(3)));

    [TestMethod]
    public void AnOperandRoundedToAWholeNumber_IsBankersRounding()
        // 7.5 \ 2: the Double operand is let-coerced to Long, 7.5 -> 8, and the operation runs on 8.
        => Assert.IsTrue(FlagsOf(new BinaryIntegerDivisionOperatorRuntimeSemantics(Provider(), Formatter()), new VBDoubleValue(7.5), new VBLongValue(2))
            .HasFlag(ArithmeticOperatorSemanticFlags.BankersRounding));

    [TestMethod]
    public void ModuloOfAFractionalOperand_IsBankersRounding()
        => Assert.IsTrue(FlagsOf(new BinaryModuloOperatorRuntimeSemantics(Provider(), Formatter()), new VBDoubleValue(7.5), new VBLongValue(2))
            .HasFlag(ArithmeticOperatorSemanticFlags.BankersRounding));

    [TestMethod]
    public void AWholeNumberOperation_IsNotBankersRounding()
        => Assert.IsFalse(FlagsOf(new BinaryIntegerDivisionOperatorRuntimeSemantics(Provider(), Formatter()), new VBLongValue(7), new VBLongValue(2))
            .HasFlag(ArithmeticOperatorSemanticFlags.BankersRounding));

    [TestMethod]
    public void ADoubleSum_IsNotBankersRounding()
        // nothing is rounded: both operands stay Double.
        => Assert.IsFalse(FlagsOf(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()), new VBDoubleValue(7.5), new VBLongValue(2))
            .HasFlag(ArithmeticOperatorSemanticFlags.BankersRounding));

    #endregion

    #region unary arithmetic (MS-VBAL 5.6.9.3.1-2)

    public static IEnumerable<object[]> UnaryArithmeticOperators()
    {
        yield return [new UnaryNegationOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new UnaryPlusOperatorRuntimeSemantics(Provider(), Formatter())];
    }

    [TestMethod]
    [DynamicData(nameof(UnaryArithmeticOperators))]
    public void ANumericUnaryOperation_HasANumericEffectiveType(UnaryArithmeticOperatorRuntimeSemantics op)
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType, FlagsOf(op, new VBLongValue(1)));

    [TestMethod]
    [DynamicData(nameof(UnaryArithmeticOperators))]
    public void ADateUnaryOperation_HasADateEffectiveType(UnaryArithmeticOperatorRuntimeSemantics op)
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBDateEffectiveType, FlagsOf(op, new VBDateValue(1)));

    [TestMethod]
    [DynamicData(nameof(UnaryArithmeticOperators))]
    public void ANullUnaryOperation_HasANullEffectiveType(UnaryArithmeticOperatorRuntimeSemantics op)
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBNullEffectiveType, FlagsOf(op, VBNullValue.Null));

    #endregion

    #region relational (MS-VBAL 5.6.9.5)

    public static IEnumerable<object[]> RelationalOperators()
    {
        yield return [new BinaryEqRelationalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryNeqRelationalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryLtRelationalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryGtRelationalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryLtEqRelationalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryGtEqRelationalOperatorRuntimeSemantics(Provider(), Formatter())];
    }

    private static ComparisonOperatorSemanticFlags Compare(VBTypedValue left, VBTypedValue right)
        => FlagsOf(new BinaryEqRelationalOperatorRuntimeSemantics(Provider(), Formatter()), left, right);

    [TestMethod]
    [DynamicData(nameof(RelationalOperators))]
    public void EveryRelationalOperator_ReportsTheEffectiveType(BinaryRelationalOperatorRuntimeSemantics op)
        => Assert.AreEqual(
            ComparisonOperatorSemanticFlags.LongEffectiveType | ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType,
            FlagsOf(op, new VBLongValue(1), new VBLongValue(2)));

    [TestMethod]
    public void TheIntegralTypes_AreIntegral()
    {
        const ComparisonOperatorSemanticFlags integral = ComparisonOperatorSemanticFlags.IntegralNumericEffectiveType;

        Assert.AreEqual(integral | ComparisonOperatorSemanticFlags.ByteEffectiveType, Compare(new VBByteValue(1), new VBByteValue(1)));
        Assert.AreEqual(integral | ComparisonOperatorSemanticFlags.IntegerEffectiveType, Compare(new VBIntegerValue(1), new VBIntegerValue(1)));
        Assert.AreEqual(integral | ComparisonOperatorSemanticFlags.LongEffectiveType, Compare(new VBLongValue(1), new VBLongValue(1)));
        Assert.AreEqual(integral | ComparisonOperatorSemanticFlags.LongLongEffectiveType, Compare(new VBLongLongValue(1), new VBLongLongValue(1)));
    }

    [TestMethod]
    public void TheFloatingPointTypes_AreFloatingPoint()
    {
        const ComparisonOperatorSemanticFlags floating = ComparisonOperatorSemanticFlags.FloatingPointNumericEffectiveType;

        Assert.AreEqual(floating | ComparisonOperatorSemanticFlags.SingleEffectiveType, Compare(new VBSingleValue(1), new VBSingleValue(1)));
        Assert.AreEqual(floating | ComparisonOperatorSemanticFlags.DoubleEffectiveType, Compare(new VBDoubleValue(1), new VBDoubleValue(1)));
    }

    [TestMethod]
    public void TheFixedPointTypes_AreFixedPoint()
    {
        const ComparisonOperatorSemanticFlags fixedPoint = ComparisonOperatorSemanticFlags.FixedPointNumericEffectiveType;

        Assert.AreEqual(fixedPoint | ComparisonOperatorSemanticFlags.CurrencyEffectiveType, Compare(new VBCurrencyValue(1), new VBCurrencyValue(1)));
        Assert.AreEqual(fixedPoint | ComparisonOperatorSemanticFlags.DecimalEffectiveType, Compare(new VBDecimalValue(1), new VBDecimalValue(1)));
    }

    [TestMethod]
    public void AStringComparison_ReportsAStringEffectiveType()
        => Assert.AreEqual(ComparisonOperatorSemanticFlags.StringEffectiveType | ComparisonOperatorSemanticFlags.StringComparisonBinary, Compare(new VBStringValue("a"), new VBStringValue("b")));

    [TestMethod]
    public void ABooleanComparison_ReportsABooleanEffectiveType()
        => Assert.AreEqual(ComparisonOperatorSemanticFlags.BooleanEffectiveType, Compare(VBBooleanValue.True, VBBooleanValue.False));

    [TestMethod]
    public void ANullComparison_ReportsANullEffectiveType()
        => Assert.AreEqual(ComparisonOperatorSemanticFlags.NullEffectiveType, Compare(VBNullValue.Null, new VBLongValue(1)));

    [TestMethod]
    public void ANaNOperand_IsFlagged()
        => Assert.IsTrue(Compare(new VBDoubleValue(double.NaN), new VBDoubleValue(1)).HasFlag(ComparisonOperatorSemanticFlags.HasNaNOperand));

    [TestMethod]
    public void NoNaN_IsNotFlagged()
        => Assert.IsFalse(Compare(new VBDoubleValue(1), new VBDoubleValue(1)).HasFlag(ComparisonOperatorSemanticFlags.HasNaNOperand));

    [TestMethod]
    public void TwoErrorValues_AreAnErrorComparison_AndStandardWhenBothAreInTheStandardRange()
    {
        var flags = Compare(new VBErrorValue(5), new VBErrorValue(6));

        Assert.IsTrue(flags.HasFlag(ComparisonOperatorSemanticFlags.ErrorEffectiveType));
        Assert.IsTrue(flags.HasFlag(ComparisonOperatorSemanticFlags.HasStandardErrorCodes));
    }

    [TestMethod]
    [DataRow(0, 65535, true, DisplayName = "the bounds of the standard range are standard")]
    [DataRow(5, 65536, false, DisplayName = "one code out of range")]
    [DataRow(-1, 5, false, DisplayName = "a negative code")]
    public void ErrorCodes_AreStandardWhenBothAreBetween0And65535(int left, int right, bool standard)
        => Assert.AreEqual(standard, Compare(new VBErrorValue(left), new VBErrorValue(right)).HasFlag(ComparisonOperatorSemanticFlags.HasStandardErrorCodes));

    [TestMethod]
    public void ANaNComparisonsError_IsTheOneTheComparisonRaises()
    {
        var eq = new BinaryEqRelationalOperatorRuntimeSemantics(Provider(), Formatter());
        VBTypedValue[] operands = [new VBDoubleValue(double.NaN), new VBDoubleValue(1)];

        var raised = OperatorAnalysisHarness.Evaluate(eq, ThrowawayExpression, operands).ErrorInfo?.ErrorId;
        var analyzed = OperatorAnalysisHarness.Analyze(eq, ThrowawayExpression, operands).Errors.SingleOrDefault()?.ErrorId;

        Assert.AreEqual(raised, analyzed);
    }

    #endregion

    #region logical (MS-VBAL 5.6.9.8)

    public static IEnumerable<object[]> LogicalOperators()
    {
        yield return [new BinaryAndLogicalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryOrLogicalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryXorLogicalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryEqvLogicalOperatorRuntimeSemantics(Provider(), Formatter())];
        yield return [new BinaryImpLogicalOperatorRuntimeSemantics(Provider(), Formatter())];
    }

    private static LogicalOperatorSemanticFlags Logic(IRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> op, VBTypedValue left, VBTypedValue right)
        => FlagsOf(op, left, right);

    [TestMethod]
    [DynamicData(nameof(LogicalOperators))]
    public void IntegralOperands_MakeABitwiseOperation_InTheirOwnType(IRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> op)
        => Assert.AreEqual(LogicalOperatorSemanticFlags.IsBitwiseSemantics | LogicalOperatorSemanticFlags.LongEffectiveType,
            Logic(op, new VBLongValue(6), new VBLongValue(3)));

    [TestMethod]
    [DynamicData(nameof(LogicalOperators))]
    public void BooleanOperands_MakeALogicalOperation_NotABitwiseOne(IRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> op)
        => Assert.AreEqual(LogicalOperatorSemanticFlags.BooleanEffectiveType, Logic(op, VBBooleanValue.True, VBBooleanValue.False));

    [TestMethod]
    public void EachIntegralEffectiveType_HasItsOwnFlag()
    {
        var and = new BinaryAndLogicalOperatorRuntimeSemantics(Provider(), Formatter());
        const LogicalOperatorSemanticFlags bitwise = LogicalOperatorSemanticFlags.IsBitwiseSemantics;

        Assert.AreEqual(bitwise | LogicalOperatorSemanticFlags.ByteEffectiveType, Logic(and, new VBByteValue(1), new VBByteValue(1)));
        Assert.AreEqual(bitwise | LogicalOperatorSemanticFlags.IntegerEffectiveType, Logic(and, new VBIntegerValue(1), new VBIntegerValue(1)));
        Assert.AreEqual(bitwise | LogicalOperatorSemanticFlags.LongEffectiveType, Logic(and, new VBLongValue(1), new VBLongValue(1)));
        Assert.AreEqual(bitwise | LogicalOperatorSemanticFlags.LongLongEffectiveType, Logic(and, new VBLongLongValue(1), new VBLongLongValue(1)));
    }

    [TestMethod]
    [DynamicData(nameof(LogicalOperators))]
    public void ALongLong_IsALongLong_NotALong(IRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> op)
    {
        var flags = Logic(op, new VBLongLongValue(1), new VBLongLongValue(3));

        Assert.IsTrue(flags.HasFlag(LogicalOperatorSemanticFlags.LongLongEffectiveType));
        Assert.IsFalse(flags.HasFlag(LogicalOperatorSemanticFlags.LongEffectiveType));
    }

    [TestMethod]
    [DynamicData(nameof(LogicalOperators))]
    public void ANullOperand_IsFlagged(IRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> op)
        => Assert.IsTrue(Logic(op, VBNullValue.Null, new VBLongValue(3)).HasFlag(LogicalOperatorSemanticFlags.HasNullOperand));

    [TestMethod]
    [DynamicData(nameof(LogicalOperators))]
    public void TwoNulls_HaveANullEffectiveType_AndAreNotBitwise(IRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> op)
        => Assert.AreEqual(LogicalOperatorSemanticFlags.HasNullOperand | LogicalOperatorSemanticFlags.NullEffectiveType, Logic(op, VBNullValue.Null, VBNullValue.Null));

    [TestMethod]
    [DynamicData(nameof(LogicalOperators))]
    public void ANonNullOperation_HasNoNullOperand(IRuntimeSemantics<BinaryLogicalOperatorSemanticContext, LogicalOperatorSemanticFlags> op)
        => Assert.IsFalse(Logic(op, new VBLongValue(1), new VBLongValue(3)).HasFlag(LogicalOperatorSemanticFlags.HasNullOperand));

    [TestMethod]
    public void Not_IsAnalyzedLikeTheBinaryLogicals()
    {
        var not = new UnaryNotOperatorRuntimeSemantics(Provider(), Formatter());

        Assert.AreEqual(LogicalOperatorSemanticFlags.IsBitwiseSemantics | LogicalOperatorSemanticFlags.LongEffectiveType, FlagsOf(not, new VBLongValue(1)));
        // MS-VBAL 5.6.9.8: the unary effective type of a Boolean is Integer.
        Assert.AreEqual(LogicalOperatorSemanticFlags.IntegerEffectiveType, FlagsOf(not, VBBooleanValue.True));
        Assert.AreEqual(LogicalOperatorSemanticFlags.HasNullOperand | LogicalOperatorSemanticFlags.NullEffectiveType, FlagsOf(not, VBNullValue.Null));
    }

    #endregion

    #region concatenation (MS-VBAL 5.6.9.4)

    private static ConcatOperationSemanticFlags Concat(VBTypedValue left, VBTypedValue right)
        => FlagsOf(new BinaryConcatOperatorRuntimeSemantics(Provider(), Formatter()), left, right);

    private static VBResizableByteArrayValue Bytes() => new([(0, 1)]);

    [TestMethod]
    public void TwoStrings_ConcatenateToAString()
        => Assert.AreEqual(ConcatOperationSemanticFlags.StringEffectiveType, Concat(new VBStringValue("a"), new VBStringValue("b")));

    [TestMethod]
    public void ANumericOperand_IsFlagged()
        => Assert.AreEqual(ConcatOperationSemanticFlags.StringEffectiveType | ConcatOperationSemanticFlags.HasNumericOperand, Concat(new VBStringValue("a"), new VBLongValue(1)));

    [TestMethod]
    public void TwoNulls_ConcatenateToNull()
        => Assert.AreEqual(ConcatOperationSemanticFlags.NullEffectiveType | ConcatOperationSemanticFlags.HasNullOperand, Concat(VBNullValue.Null, VBNullValue.Null));

    [TestMethod]
    public void ANullAgainstAString_IsAStringWithANullOperand()
        => Assert.AreEqual(ConcatOperationSemanticFlags.StringEffectiveType | ConcatOperationSemanticFlags.HasNullOperand, Concat(VBNullValue.Null, new VBStringValue("b")));

    [TestMethod]
    public void TwoByteArrays_ConcatenateToAString()
        => Assert.AreEqual(ConcatOperationSemanticFlags.StringEffectiveType | ConcatOperationSemanticFlags.HasByteArrayOperand, Concat(Bytes(), Bytes()));

    [TestMethod]
    public void AFixedSizeByteArray_IsAByteArrayToo()
        // the effective type accepts any array of Byte elements, so the operand flag does.
        => Assert.IsTrue(Concat(new VBFixedSizeArrayValue([(0, 1)], VBByteType.TypeInfo), Bytes()).HasFlag(ConcatOperationSemanticFlags.HasByteArrayOperand));

    [TestMethod]
    public void AnErrorOperand_IsFlagged_AndIsATypeMismatch()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryConcatOperatorRuntimeSemantics(Provider(), Formatter()), ThrowawayExpression, new VBErrorValue(5), new VBStringValue("b"));

        Assert.IsTrue(context.Flags.HasFlag(ConcatOperationSemanticFlags.HasErrorOperand));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, context.Errors.Single().ErrorId);
    }

    [TestMethod]
    public void ANonByteArrayOperand_IsFlagged_AndIsATypeMismatch()
    {
        var context = OperatorAnalysisHarness.Analyze(new BinaryConcatOperatorRuntimeSemantics(Provider(), Formatter()), ThrowawayExpression,
            new VBResizableArrayValue([(0, 1)], VBLongType.TypeInfo), new VBStringValue("b"));

        Assert.IsTrue(context.Flags.HasFlag(ConcatOperationSemanticFlags.HasNonByteArrayOperand));
        Assert.IsFalse(context.Flags.HasFlag(ConcatOperationSemanticFlags.HasByteArrayOperand));
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, context.Errors.Single().ErrorId);
    }

    [TestMethod]
    public void AUserDefinedTypeOperand_IsFlagged()
        => Assert.IsTrue(Concat(new VBUserDefinedTypeValue(new VBUserDefinedType(
                new RDCore.SDK.Model.Symbols.VBProject.VBUserDefinedTypeMemberSymbol(new Uri("file://rdcore-test"), new Uri("file://rdcore-test"), "Point",
                    RDCore.SDK.Model.Symbols.Abstract.ScopeKind.Module, RDCore.SDK.Model.Source.SourceRange.Empty, RDCore.SDK.Model.Source.SourceRange.Empty, RDCore.SDK.Model.AccessModifier.Public), [])),
            new VBStringValue("b")).HasFlag(ConcatOperationSemanticFlags.HasUserDefinedTypeOperand));

    #endregion

    #region operands no effective type is defined for

    // an object is no operand of any operator here: there is no effective type to report, and the operation's error says why.

    private static void AssertNoEffectiveType<TContext, TFlags>(IRuntimeSemantics<TContext, TFlags> op, params VBTypedValue[] operands)
        where TContext : SemanticContext<TFlags>, new()
        where TFlags : struct, Enum
    {
        var context = OperatorAnalysisHarness.Analyze(op, operands.Length == 1 ? UnaryOf("-") : ThrowawayExpression, operands);

        Assert.AreEqual(default, context.Flags, op.GetType().Name);
        Assert.HasCount(1, context.Errors, op.GetType().Name);
    }

    [TestMethod]
    public void AnObjectOperand_LeavesAnArithmeticOperationWithNoEffectiveType()
        => AssertNoEffectiveType(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()), AnObject(), new VBLongValue(1));

    [TestMethod]
    public void AnObjectOperand_LeavesAUnaryArithmeticOperationWithNoEffectiveType()
        => AssertNoEffectiveType(new UnaryNegationOperatorRuntimeSemantics(Provider(), Formatter()), AnObject());

    [TestMethod]
    public void AnObjectOperand_LeavesARelationalOperationWithNoEffectiveType()
        => AssertNoEffectiveType(new BinaryEqRelationalOperatorRuntimeSemantics(Provider(), Formatter()), AnObject(), new VBStringValue("a"));

    [TestMethod]
    public void AnObjectOperand_LeavesALogicalOperationWithNoEffectiveType()
        => AssertNoEffectiveType(new BinaryAndLogicalOperatorRuntimeSemantics(Provider(), Formatter()), AnObject(), new VBLongValue(1));

    [TestMethod]
    public void TwoStrings_AreAddedAsStrings()
        // MS-VBAL 5.6.9.3.2: the sum of two Strings is their concatenation.
        => Assert.AreEqual(ArithmeticOperatorSemanticFlags.VBStringEffectiveType,
            FlagsOf(new BinaryAdditionOperatorRuntimeSemantics(Provider(), Formatter()), new VBStringValue("a"), new VBStringValue("b")));

    #endregion

    #region the let operators

    [TestMethod]
    public void AnExplicitLetCoercion_IsFlaggedExplicit()
        => Assert.IsTrue(FlagsOf(new BinaryLetCoerceOperatorRuntimeSemantics(Provider(), Formatter()), new VBLongValue(1), new RDCore.SDK.Model.Values.Meta.VBTypeDescValue(VBDoubleType.TypeInfo))
            .HasFlag(ConversionSemanticFlags.Explicit));

    #endregion
}
