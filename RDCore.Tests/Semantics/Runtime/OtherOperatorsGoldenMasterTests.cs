using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Semantics.Operators;
using RDCore.Runtime.Semantics.Operators.Arithmetic;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Context;
using RDCore.SDK.Semantics.Facts;
using System.Text;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// A golden master of the operators <see cref="ArithmeticOperatorsGoldenMasterTests"/> does not record - the relational operators, <c>Like</c>, <c>Is</c>,
/// <c>&amp;</c>, the logical operators, <c>Not</c> and unary <c>+</c> - compared line for line with what was recorded before their operation plans were extracted
/// from the operator semantics. A line that differs is a change in behaviour.
/// </summary>
/// <remarks>
/// <para>
/// Every binary operator is recorded over every pair of a broad set of operand values, in binary comparison mode. The relational operators and <c>Like</c> are
/// recorded again over strings, in both comparison modes: the strings a comparison compares are letters and digits only, which every collation orders alike,
/// so that the snapshot is the same on every platform.
/// </para>
/// <para>
/// The object among the operands is a reference the session has no object for: an operation that only compares it is what it would be for any object, but
/// one that asks it for a value is an internal error here, where code would have its default member.
/// </para>
/// <para>
/// The snapshot is <c>GoldenMasters/other-operators.txt.gz</c>, next to this file, recorded and compared as <see cref="GoldenMaster"/> says.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.9 Operators")]
public sealed class OtherOperatorsGoldenMasterTests : LetCoercionRuntimeSemanticsTests
{
    private sealed class NoSymbols : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => [];
    }

    private static readonly StaticSymbol Procedure = new("DoWork", SymbolKindExt.Procedure, VBVoidType.TypeInfo);

    private static readonly string[] BinaryTokens =
    [
        Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareGreaterThanOp,
        Tokens.CompareGreaterThanOrEqualOp, Tokens.CompareLikeOp, Tokens.CompareIsOp, Tokens.ConcatOp,
        Tokens.LogicalAndOp, Tokens.LogicalOrOp, Tokens.LogicalXOrOp, Tokens.LogicalEqvOp, Tokens.LogicalImpOp,
    ];

    private static readonly string[] RelationalTokens =
    [
        Tokens.CompareEqualOp, Tokens.CompareNotEqualOp, Tokens.CompareLessThanOp, Tokens.CompareLessThanOrEqualOp, Tokens.CompareGreaterThanOp,
        Tokens.CompareGreaterThanOrEqualOp,
    ];

    // one object, so that it is the same reference on both sides of an 'Is'; not one of the session's.
    private static readonly VBObjectValue AnObject = new(new VBRuntimeObjectId());

    // each value type's default and another of its values, a NaN, the operands only a Variant or an analysis has, an object, and an array of Bytes and one of
    // something else.
    private static VBTypedValue[] Values() =>
    [
        new VBByteValue(0), new VBByteValue(7), new VBByteValue(255),
        new VBBooleanValue(false), new VBBooleanValue(true),
        new VBIntegerValue(0), new VBIntegerValue(7), new VBIntegerValue(-7),
        new VBLongValue(0), new VBLongValue(7),
        new VBLongLongValue(0), new VBLongLongValue(7),
        new VBSingleValue(0), new VBSingleValue(2.5f),
        new VBDoubleValue(0), new VBDoubleValue(2.5), new VBDoubleValue(-2.5), new VBDoubleValue(double.NaN),
        new VBCurrencyValue(0), new VBCurrencyValue(2.5m),
        new VBDecimalValue(0), new VBDecimalValue(2.5m),
        new VBDateValue(0), new VBDateValue(2),
        new VBStringValue(string.Empty), new VBStringValue("7"), new VBStringValue("2.5"), new VBStringValue("abc"),
        VBEmptyValue.Empty,
        VBNullValue.Null,
        new VBErrorValue(5),
        VBObjectValue.Nothing, AnObject,
        ByteArray(), LongArray(),
        new VBVariantValue(new VBLongValue(7)), new VBVariantValue(new VBStringValue("7")), new VBVariantValue(new VBStringValue("abc")),
        new VBVariantValue(VBEmptyValue.Empty), new VBVariantValue(VBNullValue.Null), new VBVariantValue(AnObject), new VBVariantValue(ByteArray()),
        VBLongType.TypeInfo.CreateIndeterminateValue(), VBDoubleType.TypeInfo.CreateIndeterminateValue(), VBVariantType.TypeInfo.CreateIndeterminateValue(),
    ];

    // the strings the comparison modes tell apart: by case, by a trailing byte, and digits from letters.
    private static VBTypedValue[] ComparedStrings() =>
    [
        new VBStringValue(string.Empty), new VBStringValue("abc"), new VBStringValue("ABC"), new VBStringValue("abd"), new VBStringValue("ABD"),
        new VBStringValue("7"), new VBStringValue("10"), VBStringValue.FromBytes([0x61, 0x00, 0x62]),
    ];

    private static VBTypedValue[] LikeSubjects() =>
    [
        new VBStringValue("abc"), new VBStringValue("ABC"), new VBStringValue("a7c"), new VBStringValue(string.Empty), VBStringValue.FromBytes([0x61, 0x00, 0x62]),
    ];

    // each kind of like-pattern-element, a pattern that matches anything and one that matches nothing but the zero-length string, and two that are invalid.
    private static VBTypedValue[] LikePatterns() =>
    [
        new VBStringValue("abc"), new VBStringValue("ABC"), new VBStringValue("a*"), new VBStringValue("A?C"), new VBStringValue("a#c"),
        new VBStringValue("[a-c]BC"), new VBStringValue("[!a]bc"), new VBStringValue("*"), new VBStringValue(string.Empty), new VBStringValue("["),
        new VBStringValue("a["),
    ];

    // "A" (0x41 0x00), which a Byte() concatenates as.
    private static VBResizableByteArrayValue ByteArray()
    {
        var array = new VBResizableByteArrayValue([(0, 1)]);
        array.TrySetElement(new VBByteValue(0x41).Handle, 0);
        return array;
    }

    private static VBResizableArrayValue LongArray() => new([(0, 1)], VBLongType.TypeInfo);

    private static LiteralExpressionNode Operand() => new(NodeId, TestLocations.TestLocation, new VBLongValue(1));

    [TestMethod]
    public void TheOtherOperators_DoWhatTheyWereRecordedToDo() => GoldenMaster.AssertMatches(Record(), "other-operators.txt.gz");

    private static string Record()
    {
        var text = new StringBuilder();
        var binary = SessionIn(OptionCompare.Binary);
        var textMode = SessionIn(OptionCompare.Text);

        foreach (var token in BinaryTokens)
        {
            foreach (var left in Values())
            {
                foreach (var right in Values())
                {
                    RecordBinary(text, binary, token, left, right);
                }
            }
        }

        foreach (var operand in Values())
        {
            var (observer, operators) = Observed();
            var outcome = GoldenMaster.Outcome(() => operators.EvaluateUnaryOperator(binary,
                new VBUnaryOperatorExpressionNode(Tokens.LogicalNotOp, NodeId, TestLocations.TestLocation, [Operand()]), operand));
            text.Append($"{Tokens.LogicalNotOp} {GoldenMaster.Describe(operand)} => {outcome}{GoldenMaster.Describe(observer)}\n");
        }


        foreach (var session in new[] { binary, textMode })
        {
            foreach (var token in RelationalTokens)
            {
                foreach (var left in ComparedStrings())
                {
                    foreach (var right in ComparedStrings())
                    {
                        RecordBinary(text, session, token, left, right);
                    }
                }
            }

            foreach (var subject in LikeSubjects())
            {
                foreach (var pattern in LikePatterns())
                {
                    RecordBinary(text, session, Tokens.CompareLikeOp, subject, pattern);
                }
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    private static void RecordBinary(StringBuilder text, IRuntimeSession session, string token, VBTypedValue left, VBTypedValue right)
    {
        var (observer, operators) = Observed();
        var outcome = GoldenMaster.Outcome(() => operators.EvaluateBinaryOperator(session,
            new VBBinaryOperatorExpressionNode(token, NodeId, TestLocations.TestLocation, Operand(), Operand()), left, right));
        text.Append($"{GoldenMaster.Describe(left)} {token} {GoldenMaster.Describe(right)} => {outcome}{GoldenMaster.Describe(observer)}\n");
    }

    // binary mode is what a session compares in when nothing is executing; text mode is a module's, so the session is executing a procedure of one.
    private static IRuntimeSession SessionIn(OptionCompare compare)
    {
        var session = RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, SupportsOptionCompareDatabase: true, OptionCompare.Text), new NoSymbols());

        if (compare is not OptionCompare.Binary)
        {
            var frame = session.Symbols.CreateFrame(new SyntaxNodeId("file://rdcore-test/Mod1.bas", [1]), Procedure, new ModuleDirectives(Compare: compare));
            Assert.IsTrue(session.CallStack.TryPush(frame));
        }

        return session;
    }

    // a provider owns its coercion stack: one per operation. Observed, so that what an analysis is told of the operation is recorded with it.
    private static (RecordingAnalysisObserver Observer, IOperatorRuntimeSemanticsProvider Operators) Observed()
    {
        var observer = new RecordingAnalysisObserver();
        var observation = new AnalysisObservation(observer);
        return (observer, new OperatorRuntimeSemanticsProvider(LetCoercionAnalysisHarness.BuildProvider(observation), Formatter(), observation));
    }
}
