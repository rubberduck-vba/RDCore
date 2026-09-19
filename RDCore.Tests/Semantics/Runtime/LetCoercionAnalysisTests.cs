using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The analysis half of let-coercion (MS-VBAL §5.5.1.2): what the provider and the strategies report about a coercion —
/// its conversion flags, the operand it implicates, the errors it raises — rather than the value it produces. Runs the
/// real provider over every real strategy into the real flags builder; the strategy-specific cases at the end call the
/// strategy directly, for the source rows the provider (which dispatches by destination type only) does not route to it.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2 Let-coercion (analysis)")]
public sealed class LetCoercionAnalysisTests : LetCoercionRuntimeSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");

    private static readonly VBUnaryOperatorExpressionNode UnaryExpression = new(
        "-", NodeId, TestLocations.TestLocation, [new LiteralExpressionNode(default, TestLocations.TestLocation, new VBIntegerValue((short)0))]);

    // every coercion the provider analyzes is a let-coercion; whether it is implicit or explicit is for the operation asking for it to say.
    private const ConversionSemanticFlags LetCoerced = ConversionSemanticFlags.LetCoerced;

    private static LetCoercionAnalysisHarness.Analysis Analyze(VBTypedValue source, VBType destination, InputIndex operand = InputIndex.BinaryLeftOperand)
        => LetCoercionAnalysisHarness.Analyze(source, destination, ThrowawayExpression, operand);

    #region what the provider adds to every coercion

    [TestMethod]
    public void AnalyzedCoercion_IsLetCoerced_AndAsksForItsOperand()
    {
        var analysis = Analyze(new VBLongValue(1), VBDoubleType.TypeInfo, InputIndex.BinaryLeftOperand);

        Assert.IsTrue(analysis.Flags.HasFlag(LetCoerced));
        Assert.IsTrue(analysis.Flags.HasFlag(ConversionSemanticFlags.BinaryLeftOperand));
        Assert.IsFalse(analysis.Flags.HasFlag(ConversionSemanticFlags.BinaryRightOperand));
        Assert.IsFalse(analysis.Flags.HasFlag(ConversionSemanticFlags.UnaryOperand));
    }

    [TestMethod]
    public void TheRightOperandOfABinaryOperator_IsFlaggedRight()
    {
        var flags = Analyze(new VBLongValue(1), VBDoubleType.TypeInfo, InputIndex.BinaryRightOperand).Flags;

        Assert.IsTrue(flags.HasFlag(ConversionSemanticFlags.BinaryRightOperand));
        Assert.IsFalse(flags.HasFlag(ConversionSemanticFlags.BinaryLeftOperand));
    }

    [TestMethod]
    public void TheOperandOfAUnaryOperator_IsFlaggedUnary()
    {
        var flags = LetCoercionAnalysisHarness.Analyze(new VBLongValue(1), VBDoubleType.TypeInfo, UnaryExpression, InputIndex.UnaryOperand).Flags;

        Assert.IsTrue(flags.HasFlag(ConversionSemanticFlags.UnaryOperand));
        Assert.IsFalse(flags.HasFlag(ConversionSemanticFlags.BinaryLeftOperand | ConversionSemanticFlags.BinaryRightOperand));
    }

    [TestMethod]
    public void TwoOperandsOfOneOperation_KeepTheirOwnFlags()
        // `Long + Integer` coerced to Double and to Integer: the left widens, the right narrows - and neither leaks into the other.
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();

        LetCoercionAnalysisHarness.Analyze(new VBLongValue(1), VBDoubleType.TypeInfo, ThrowawayExpression, InputIndex.BinaryLeftOperand, builder);
        LetCoercionAnalysisHarness.Analyze(new VBDoubleValue(1.5), VBIntegerType.TypeInfo, ThrowawayExpression, InputIndex.BinaryRightOperand, builder);

        var left = builder.LetCoercionFlagsOf(InputIndex.BinaryLeftOperand);
        var right = builder.LetCoercionFlagsOf(InputIndex.BinaryRightOperand);
        Assert.IsTrue(left.HasFlag(ConversionSemanticFlags.Widening));
        Assert.IsFalse(left.HasFlag(ConversionSemanticFlags.Narrowing));
        Assert.IsTrue(right.HasFlag(ConversionSemanticFlags.Narrowing));
        Assert.IsFalse(right.HasFlag(ConversionSemanticFlags.Widening));
        // the operation as a whole has both
        Assert.IsTrue(builder.Flags.HasFlag(ConversionSemanticFlags.Widening | ConversionSemanticFlags.Narrowing));
    }

    [TestMethod]
    public void ADestinationNoStrategyHandles_IsAFailedConversion()
    {
        var analysis = Analyze(new VBLongValue(1), VBUnknownType.TypeInfo);

        Assert.IsTrue(analysis.Flags.HasFlag(ConversionSemanticFlags.Failed));
    }

    [TestMethod]
    public void ACoercionThatRaisesAnError_CarriesThatErrorIntoTheContext()
    {
        var analysis = Analyze(new VBLongValue(300), VBByteType.TypeInfo);

        var errors = analysis.Builder.Build().Errors;
        Assert.HasCount(1, errors);
        Assert.AreEqual((int)VBRuntimeErrorId.Overflow, errors[0].ErrorId);
    }

    [TestMethod]
    public void ACoercionThatSucceeds_CarriesNoError()
        => Assert.IsEmpty(Analyze(new VBLongValue(200), VBByteType.TypeInfo).Builder.Build().Errors);

    [TestMethod]
    public void TheReturnedContext_CarriesTheStrategysFlags_AndNoResultWhenNothingFailed()
    {
        var analysis = Analyze(new VBLongValue(1), VBVariantType.TypeInfo);

        Assert.IsTrue(analysis.Context.Flags.HasFlag(ConversionSemanticFlags.VariantTarget));
        Assert.IsFalse(analysis.Context.Result.IsApplicable);
    }

    #endregion

    #region numeric destinations (MS-VBAL 5.5.1.2.1-2)

    private enum Kind { Integral, Float, Fixed }

    private static readonly (string Name, VBTypedValue Value, Kind Kind)[] NumericSources =
    [
        ("Byte", new VBByteValue(1), Kind.Integral), ("Integer", new VBIntegerValue(1), Kind.Integral),
        ("Long", new VBLongValue(1), Kind.Integral), ("LongLong", new VBLongLongValue(1), Kind.Integral),
        ("Single", new VBSingleValue(1.5f), Kind.Float), ("Double", new VBDoubleValue(1.5), Kind.Float),
        ("Currency", new VBCurrencyValue(1.5m), Kind.Fixed), ("Decimal", new VBDecimalValue(1.5m), Kind.Fixed),
    ];

    private static readonly (string Name, VBType Type, Kind Kind)[] NumericDestinations =
    [
        ("Byte", VBByteType.TypeInfo, Kind.Integral), ("Integer", VBIntegerType.TypeInfo, Kind.Integral),
        ("Long", VBLongType.TypeInfo, Kind.Integral), ("LongLong", VBLongLongType.TypeInfo, Kind.Integral),
        ("Single", VBSingleType.TypeInfo, Kind.Float), ("Double", VBDoubleType.TypeInfo, Kind.Float),
        ("Currency", VBCurrencyType.TypeInfo, Kind.Fixed), ("Decimal", VBDecimalType.TypeInfo, Kind.Fixed),
    ];

    // the numeric types from the narrowest range of values to the widest: Byte 0..255, Integer, Long, Currency (about 9.2e14),
    // LongLong (about 9.2e18), Decimal (about 7.9e28), Single (about 3.4e38) and Double (about 1.8e308).
    private static readonly string[] RangeOrder = ["Byte", "Integer", "Long", "Currency", "LongLong", "Decimal", "Single", "Double"];

    public static IEnumerable<object[]> NumericToNumeric()
    {
        foreach (var source in NumericSources)
        {
            foreach (var destination in NumericDestinations)
            {
                var sourceRank = Array.IndexOf(RangeOrder, source.Name);
                var destinationRank = Array.IndexOf(RangeOrder, destination.Name);

                // a fractional value into a whole-number type is rounded (banker's) and so loses precision; any other conversion
                // gets wider when the destination holds every value of the source, and narrower when it does not (a Double put
                // in a Single loses digits, too).
                ConversionSemanticFlags conversion = (source.Kind, destination.Kind) switch
                {
                    (Kind.Float or Kind.Fixed, Kind.Integral)
                        => ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy | ConversionSemanticFlags.BankersRounding,
                    (Kind.Float, Kind.Float) when destinationRank < sourceRank
                        => ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy,
                    _ when destinationRank > sourceRank => ConversionSemanticFlags.Widening,
                    _ when destinationRank < sourceRank => ConversionSemanticFlags.Narrowing,
                    _ => 0,
                };
                yield return [source.Name, source.Value, destination.Name, destination.Type,
                    LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.Numeric | ConversionSemanticFlags.BinaryLeftOperand | conversion];
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(NumericToNumeric))]
    public void NumericToNumeric_IsReportedByItsKind(string sourceName, VBTypedValue source, string destinationName, VBType destination, ConversionSemanticFlags expected)
        => Assert.AreEqual(expected, Analyze(source, destination).Flags, $"{sourceName} -> {destinationName}");

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void BooleanToNumeric_IsNumericAndConvertible_WithoutAWidthChange(bool value)
        => Assert.AreEqual(
            LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.Numeric | ConversionSemanticFlags.BinaryLeftOperand,
            Analyze(new VBBooleanValue(value), VBLongType.TypeInfo).Flags);

    [TestMethod]
    public void StringToNumeric_IsNumericAndConvertible()
        => Assert.AreEqual(
            LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.Numeric | ConversionSemanticFlags.BinaryLeftOperand,
            Analyze(new VBStringValue("12"), VBDoubleType.TypeInfo).Flags);

    #endregion

    #region Boolean, Date and String destinations

    [TestMethod]
    [DataRow(1, DisplayName = "numeric source")]
    [DataRow(2, DisplayName = "String source")]
    public void ToBoolean_IsNumericAndConvertible(int sourceKind)
        => Assert.AreEqual(
            LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.Numeric | ConversionSemanticFlags.BinaryLeftOperand,
            Analyze(sourceKind == 1 ? new VBLongValue(1) : new VBStringValue("True"), VBBooleanType.TypeInfo).Flags);

    [TestMethod]
    public void ANarrowNumberToDate_IsNumericAndWidening()
        // a Date is a Double underneath, so anything narrower than a Double widens into it.
        => Assert.AreEqual(
            LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.Numeric | ConversionSemanticFlags.Widening | ConversionSemanticFlags.BinaryLeftOperand,
            Analyze(new VBIntegerValue(1), VBDateType.TypeInfo).Flags);

    [TestMethod]
    public void ADoubleToDate_IsNumeric_ButNotWidening()
        => Assert.AreEqual(
            LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.Numeric | ConversionSemanticFlags.BinaryLeftOperand,
            Analyze(new VBDoubleValue(1), VBDateType.TypeInfo).Flags);

    [TestMethod]
    public void ABooleanToDate_IsNumeric()
        => Assert.IsTrue(Analyze(VBBooleanValue.True, VBDateType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.Numeric));

    [TestMethod]
    public void AStringOrDateToDate_IsOnlyConvertible()
    {
        var expected = LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.BinaryLeftOperand;

        Assert.AreEqual(expected, Analyze(new VBStringValue("1/1/2026"), VBDateType.TypeInfo).Flags);
        Assert.AreEqual(expected, Analyze(new VBDateValue(1), VBDateType.TypeInfo).Flags);
    }

    [TestMethod]
    public void ToString_IsConvertible_AndNotNumeric()
    {
        var expected = LetCoerced | ConversionSemanticFlags.CTypeAvailable | ConversionSemanticFlags.BinaryLeftOperand;

        Assert.AreEqual(expected, Analyze(new VBLongValue(1), VBStringType.TypeInfo).Flags);
        Assert.AreEqual(expected, Analyze(VBBooleanValue.True, VBStringType.TypeInfo).Flags);
        Assert.AreEqual(expected, Analyze(new VBDateValue(1), VBStringType.TypeInfo).Flags);
        Assert.AreEqual(expected, Analyze(new VBStringValue("x"), VBStringType.TypeInfo).Flags);
    }

    [TestMethod]
    public void AnEmptyToString_ImplicatesAnEmptyOperand()
        => Assert.IsTrue(Analyze(new VBEmptyValue(), VBStringType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.EmptyOperand));

    [TestMethod]
    public void AByteArrayToString_ImplicatesAByteArrayOperand()
    {
        var bytes = new VBResizableByteArrayValue([(0, 1)]);

        Assert.IsTrue(Analyze(bytes, VBStringType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.ByteArrayOperand));
    }

    #endregion

    #region variant, user-defined and array destinations

    [TestMethod]
    public void ToVariant_IsAVariantTarget()
        => Assert.IsTrue(Analyze(new VBLongValue(1), VBVariantType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.VariantTarget));

    [TestMethod]
    public void ToAUserDefinedType_IsAUserDefinedTypeTarget()
    {
        var symbol = new VBUserDefinedTypeMemberSymbol(Root, Root, "Point", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var udt = new VBUserDefinedType(symbol, []);

        Assert.IsTrue(Analyze(new VBLongValue(1), udt).Flags.HasFlag(ConversionSemanticFlags.UserDefinedTypeTarget));
    }

    [TestMethod]
    public void ToAResizableArray_IsAnArrayTarget_AndNotAByteArrayTarget()
    {
        var flags = Analyze(new VBLongValue(1), new VBResizableArrayType(VBLongType.TypeInfo)).Flags;

        Assert.IsTrue(flags.HasFlag(ConversionSemanticFlags.ArrayTarget));
        Assert.IsFalse(flags.HasFlag(ConversionSemanticFlags.ByteArrayTarget));
    }

    [TestMethod]
    public void ToAByteArray_IsAnArrayTarget_AndAByteArrayTarget()
    {
        var flags = Analyze(new VBStringValue("x"), VBResizableByteArrayType.TypeInfo).Flags;

        Assert.IsTrue(flags.HasFlag(ConversionSemanticFlags.ArrayTarget | ConversionSemanticFlags.ByteArrayTarget));
    }

    #endregion

    #region the strategies for the Null, Empty and Error rows, called directly

    // The provider dispatches by DESTINATION type, so an Empty, Null or Error source reaches these strategies only when the
    // destination is Empty, Null or Error. The rows they implement are source rows (MS-VBAL 5.5.1.2.9-11), so they are
    // analyzed here with the real destination of the coercion, the way the operator semantics will hand them over.

    private static LetCoercionAnalysisHarness.Analysis AnalyzeWith(ILetCoercionRuntimeSemantics strategy, VBTypedValue source, VBType destination,
        InputIndex operand = InputIndex.BinaryLeftOperand, VBOperatorExpression? expression = null)
    {
        expression ??= ThrowawayExpression;
        var frame = new LetCoercionStackFrame(expression.Identity, operand, source, new VBTypeDescValue(destination));
        var builder = new LetCoercionSemanticContextFlagsBuilder();
        var result = strategy.EvaluateLetCoercion(null!, expression, frame);
        return new(strategy.Analyze(builder, null!, expression, frame, result), builder);
    }

    private static VBNullTypeLetCoercionRuntimeSemantics NullStrategy => new(Formatter());

    [TestMethod]
    public void ANullOperand_IsAlwaysFlaggedAsOne()
        => Assert.IsTrue(AnalyzeWith(NullStrategy, new VBNullValue(), VBVariantType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.NullOperand));

    [TestMethod]
    public void NullToVariant_IsNotAFailure()
        => Assert.IsFalse(AnalyzeWith(NullStrategy, new VBNullValue(), VBVariantType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.Failed));

    [TestMethod]
    public void NullToAFixedSizeArray_IsNotAFailure()
        => Assert.IsFalse(AnalyzeWith(NullStrategy, new VBNullValue(), new VBFixedSizeArrayType(VBLongType.TypeInfo)).Flags.HasFlag(ConversionSemanticFlags.Failed));

    [TestMethod]
    [DataRow("Long", DisplayName = "any other type: error 94")]
    [DataRow("String", DisplayName = "String")]
    [DataRow("Date", DisplayName = "Date")]
    public void NullToAScalar_IsAFailure_InvalidUseOfNull(string type)
    {
        VBType destination = type switch { "Long" => VBLongType.TypeInfo, "String" => VBStringType.TypeInfo, _ => VBDateType.TypeInfo };

        var analysis = AnalyzeWith(NullStrategy, new VBNullValue(), destination);

        Assert.IsTrue(analysis.Flags.HasFlag(ConversionSemanticFlags.NullOperand | ConversionSemanticFlags.Failed));
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, analysis.Context.Result.ErrorInfo!.ErrorId);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, analysis.Builder.Build().Errors.Single().ErrorId);
    }

    [TestMethod]
    public void NullToAUserDefinedTypeOrAResizableArray_IsAFailure_TypeMismatch()
    {
        var symbol = new VBUserDefinedTypeMemberSymbol(Root, Root, "Point", ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);

        foreach (VBType destination in new VBType[] { new VBUserDefinedType(symbol, []), new VBResizableArrayType(VBLongType.TypeInfo) })
        {
            var analysis = AnalyzeWith(NullStrategy, new VBNullValue(), destination);

            Assert.IsTrue(analysis.Flags.HasFlag(ConversionSemanticFlags.Failed), destination.Name);
            Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, analysis.Context.Result.ErrorInfo!.ErrorId, destination.Name);
        }
    }

    [TestMethod]
    [DataRow(InputIndex.BinaryLeftOperand, ConversionSemanticFlags.BinaryLeftOperand)]
    [DataRow(InputIndex.BinaryRightOperand, ConversionSemanticFlags.BinaryRightOperand)]
    public void AnEmptyOperand_IsFlaggedEmpty_AndByWhichSideItIs(InputIndex operand, ConversionSemanticFlags side)
    {
        var analysis = AnalyzeWith(new VBEmptyTypeLetCoercionRuntimeSemantics(Formatter()), new VBEmptyValue(), VBLongType.TypeInfo, operand);

        Assert.IsTrue(analysis.Flags.HasFlag(ConversionSemanticFlags.EmptyOperand | side));
        Assert.AreEqual(ConversionSemanticFlags.EmptyOperand | side, analysis.Builder.LetCoercionFlagsOf(operand));
    }

    [TestMethod]
    public void AnEmptyOperandOfAUnaryOperator_IsFlaggedUnary()
    {
        var analysis = AnalyzeWith(new VBEmptyTypeLetCoercionRuntimeSemantics(Formatter()), new VBEmptyValue(), VBLongType.TypeInfo, InputIndex.UnaryOperand, UnaryExpression);

        Assert.IsTrue(analysis.Flags.HasFlag(ConversionSemanticFlags.EmptyOperand | ConversionSemanticFlags.UnaryOperand));
    }

    [TestMethod]
    public void AnErrorOperand_IsFlaggedAsOne()
        => Assert.IsTrue(AnalyzeWith(new VBErrorTypeLetCoercionRuntimeSemantics(FakeProvider(), Formatter()), new VBErrorValue(5), VBLongType.TypeInfo)
            .Flags.HasFlag(ConversionSemanticFlags.ErrorOperand));

    #endregion

    #region what the source of a coercion says, whatever it is coerced to

    // the strategy of a coercion is the one of its destination type: a Null, Empty, Error or object source coerced to a Long
    // never reaches the strategy of its own type, and it is the provider that reports what the source is.
    public static IEnumerable<object[]> SourceOperands()
    {
        yield return ["Null", new VBNullValue(), ConversionSemanticFlags.NullOperand];
        yield return ["Empty", VBEmptyValue.Empty, ConversionSemanticFlags.EmptyOperand];
        yield return ["Error", new VBErrorValue(5), ConversionSemanticFlags.ErrorOperand];
        yield return ["Nothing", VBObjectValue.Nothing, ConversionSemanticFlags.ObjectOperand];
        yield return ["object", new VBObjectValue(new VBRuntimeObjectId()), ConversionSemanticFlags.ObjectOperand];
    }

    [TestMethod]
    [DynamicData(nameof(SourceOperands))]
    public void ASourceIntoALong_IsFlaggedForWhatItIs(string source, VBTypedValue value, ConversionSemanticFlags expected)
        => Assert.IsTrue(Analyze(value, VBLongType.TypeInfo).Flags.HasFlag(expected), source);

    [TestMethod]
    [DynamicData(nameof(SourceOperands))]
    public void ASourceIntoAString_IsFlaggedForWhatItIs(string source, VBTypedValue value, ConversionSemanticFlags expected)
        => Assert.IsTrue(Analyze(value, VBStringType.TypeInfo).Flags.HasFlag(expected), source);

    [TestMethod]
    public void AnObjectIntoAnObject_IsFlaggedAsAnObjectOperand()
        => Assert.IsTrue(Analyze(new VBObjectValue(new VBRuntimeObjectId()), VBObjectType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.ObjectOperand));

    [TestMethod]
    public void ANumericSource_IsNotFlaggedForAnyOfThem()
    {
        var operandFlags = ConversionSemanticFlags.NullOperand | ConversionSemanticFlags.EmptyOperand | ConversionSemanticFlags.ErrorOperand
            | ConversionSemanticFlags.ObjectOperand | ConversionSemanticFlags.ByteArrayOperand;

        Assert.AreEqual((ConversionSemanticFlags)0, Analyze(new VBIntegerValue(1), VBLongType.TypeInfo).Flags & operandFlags);
    }

    #endregion

    #region known gaps in what the analysis reports

    [TestMethod]
    [Ignore("DateSerial is documented as 'a DateSerial conversion from a Date' but is never issued: only the Date strategy has that " +
        "logic, and the provider dispatches by destination type, so a Date coerced to a numeric type is the numeric strategy's, " +
        "which does not flag it. The Date strategy's own Date-source branches are unreachable. Conversions should be able to issue it; " +
        "kept noted until that is picked up.")]
    public void ADateToANumericType_IsADateSerialConversion()
        => Assert.IsTrue(Analyze(new VBDateValue(2), VBLongType.TypeInfo).Flags.HasFlag(ConversionSemanticFlags.DateSerial));

    #endregion
}
