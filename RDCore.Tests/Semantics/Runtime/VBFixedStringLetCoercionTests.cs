using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using System.Text;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for let-coercion to a fixed-length string (MS-VBAL §5.5.1.2.5): the result is always exactly
/// <c>length</c> characters — a String source truncated or padded on the right with spaces, any other source first
/// let-coerced to a String. The String coercion itself is the real <see cref="VBStringLetCoercionRuntimeSemantics"/>,
/// reached through the provider the way it is at run time.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.5 Let-coercion to and from FixedString")]
public sealed class VBFixedStringLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    // the provider a fixed-length string reaches its String coercion through: a real String strategy behind it.
    private static ILetCoercionRuntimeSemanticsProvider ProviderOverTheStringStrategy()
    {
        var strings = new VBStringLetCoercionRuntimeSemantics(Formatter());
        var provider = Substitute.For<ILetCoercionRuntimeSemanticsProvider>();
        provider.EvaluateLetCoercionSemantics(default!, default!, default)
            .ReturnsForAnyArgs(call => strings.EvaluateLetCoercion(
                call.ArgAt<ISymbolResolver>(0), call.ArgAt<VBOperatorExpression>(1), call.ArgAt<LetCoercionStackFrame>(2)));
        return provider;
    }

    private static VBFixedStringLetCoercionRuntimeSemantics Sut() => new(ProviderOverTheStringStrategy(), Formatter());

    private static VBResizableByteArrayValue BytesOf(byte[] bytes)
    {
        var array = new VBResizableByteArrayValue([(0, bytes.Length - 1)]);
        for (var i = 0; i < bytes.Length; i++)
        {
            array.TrySetElement(new ValueBindingHandle(new VBRuntimeValue<byte>(bytes[i])), i);
        }
        return array;
    }

    [TestMethod]
    [DataRow("abc", 5, "abc  ", DisplayName = "shorter: padded on the right with spaces")]
    [DataRow("abcde", 5, "abcde", DisplayName = "exactly the length: copied")]
    [DataRow("abcdefgh", 5, "abcde", DisplayName = "longer: truncated to the first length characters")]
    [DataRow("", 3, "   ", DisplayName = "empty string: all spaces")]
    [DataRow("abc", 1, "a", DisplayName = "length 1")]
    [DataRow("  x", 2, "  ", DisplayName = "truncation keeps leading spaces")]
    public void StringSource_IsTruncatedOrPaddedToTheLength(string source, int length, string expected)
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBStringValue(source), new VBFixedStringType(length)), expected);

    [TestMethod]
    public void NumericSource_CoercesToFixedString()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBLongValue(5), new VBFixedStringType(10)), "5         ");

    [TestMethod]
    public void NumericSource_IsLetCoercedToAStringFirst_ThenTruncated()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBLongValue(1234567), new VBFixedStringType(3)), "123");

    [TestMethod]
    public void FractionalSource_KeepsItsDecimalSeparator()
        // the culture-invariant text of 1.5, as the String coercion produces it
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBDoubleValue(1.5), new VBFixedStringType(6)), "1.5   ");

    [TestMethod]
    public void BooleanSource_CoercesThroughItsString()
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), VBBooleanValue.True, new VBFixedStringType(6)), "True  ");

    [TestMethod]
    public void DateSource_CoercesThroughItsString_ToExactlyTheLength()
        // the text of a Date is culture-dependent; the length is not.
        => Assert.HasCount(20, ((VBStringValue)Coerce(Sut(), new VBDateValue(46283.5729), new VBFixedStringType(20)).Result!).Value);

    [TestMethod]
    public void EmptySource_IsAStringOfLengthSpaces()
        // MS-VBAL 5.5.1.2.11
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), new VBEmptyValue(), new VBFixedStringType(4)), "    ");

    [TestMethod]
    public void ByteArraySource_CoercesThroughItsString()
        // MS-VBAL 5.5.1.2.6: Byte() to String * length is the string the bytes represent, fitted to the length.
        => AssertCoercedTo<VBStringValue>(Coerce(Sut(), BytesOf(Encoding.Unicode.GetBytes("Hi")), new VBFixedStringType(5)), "Hi   ");

    [TestMethod]
    public void AnErrorFromTheStringCoercion_IsTheResult()
    {
        var provider = Substitute.For<ILetCoercionRuntimeSemanticsProvider>();
        provider.EvaluateLetCoercionSemantics(default!, default!, default).ReturnsForAnyArgs(call =>
            LetCoercionResult.Error(VBRuntimeErrorInfo.For(VBRuntimeErrorId.Overflow, ThrowawayExpression.Location, "no")));

        AssertError(Coerce(new VBFixedStringLetCoercionRuntimeSemantics(provider, Formatter()), new VBLongValue(5), new VBFixedStringType(4)),
            VBRuntimeErrorId.Overflow);
    }

    [TestMethod]
    public void ASourceThisStrategyDoesNotHandle_IsNotApplicable()
        // Null, objects and the rest are other sections' rules (5.5.1.2.10 and on), not this destination's.
        => Assert.IsFalse(Coerce(Sut(), new VBNullValue(), new VBFixedStringType(4)).IsApplicable);

    [TestMethod]
    public void ADestinationThatIsNotAFixedLengthString_IsNotApplicable()
        => Assert.IsFalse(Coerce(Sut(), new VBStringValue("abc"), VBStringType.TypeInfo).IsApplicable);

    private static ConversionSemanticFlags FlagsOf(VBTypedValue source, int length)
    {
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(new VBFixedStringType(length)));
        var builder = new LetCoercionSemanticContextFlagsBuilder();
        Sut().Analyze(builder, null!, ThrowawayExpression, frame, LetCoercionResult.NotApplicable(frame));
        return builder.Flags;
    }

    [TestMethod]
    public void Analyze_ATruncatedString_IsNarrowingAndLossy()
        => Assert.IsTrue(FlagsOf(new VBStringValue("abcdef"), 3).HasFlag(ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy));

    [TestMethod]
    public void Analyze_ANumberWhoseTextIsTruncated_IsNarrowingAndLossy()
        => Assert.IsTrue(FlagsOf(new VBLongValue(123456), 3).HasFlag(ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy));

    [TestMethod]
    public void Analyze_APaddedString_LosesNothing()
        => Assert.IsFalse(FlagsOf(new VBStringValue("ab"), 5).HasFlag(ConversionSemanticFlags.Lossy));

    [TestMethod]
    public void Analyze_EmptySource_IsFlagged()
        => Assert.IsTrue(FlagsOf(new VBEmptyValue(), 5).HasFlag(ConversionSemanticFlags.EmptyOperand));

    [TestMethod]
    public void Analyze_ByteArraySource_IsFlagged()
        => Assert.IsTrue(FlagsOf(BytesOf([1, 2]), 5).HasFlag(ConversionSemanticFlags.ByteArrayOperand));
}
