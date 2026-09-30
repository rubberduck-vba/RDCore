using NSubstitute;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Runtime.StdLib;

/// <summary>
/// <strong>MS-VBAL §6.1.2.3.1</strong> The rest of the Conversion module — <c>CBool</c>, <c>CDate</c>,
/// <c>CStr</c>, <c>CVar</c>, <c>CVErr</c>, <c>Error</c>, <c>Fix</c>, <c>Int</c>, <c>Hex</c>, <c>Oct</c>,
/// <c>Str</c> and <c>Val</c>, and the <c>$</c> forms whose only difference is the declared return type.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.3 Conversion")]
public sealed class ExplicitConversionFunctionTests
{
    private static StdConversion Conversion(int? lastError = null)
    {
        var session = Substitute.For<IRuntimeSession>();
        if (lastError is { } code)
        {
            var error = Substitute.For<IVBRaisableError>();
            error.ErrorId.Returns(code);
            session.Errors.Current.Returns(error);
        }

        return new StdConversion(session);
    }

    private static VBVariantValue Variant(VBTypedValue value) => new(value);

    private static VBVariantValue Text(string value) => Variant(new VBStringValue(value));

    private static T ValueOf<T>(RuntimeSemanticsEvaluationResult<T> result) where T : VBTypedValue
    {
        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Verbose);
        return result.Result!;
    }

    private static VBTypedValue Inner(RuntimeSemanticsEvaluationResult<VBVariantValue> result) => ValueOf(result).TypedValue;

    private static VBRuntimeErrorId ErrorOf<T>(RuntimeSemanticsEvaluationResult<T> result) where T : VBTypedValue
    {
        Assert.IsFalse(result.IsSuccess, "expected an error");
        return (VBRuntimeErrorId)result.ErrorInfo!.ErrorId;
    }

    #region CBool

    [TestMethod]
    public void CBool_ReadsTheBooleanTokens()
    {
        Assert.IsTrue((bool)ValueOf(Conversion().CBool(Text("true"))).Value);
        Assert.IsFalse((bool)ValueOf(Conversion().CBool(Text("#FALSE#"))).Value);
    }

    [TestMethod]
    public void CBool_ZeroIsFalseAndAnythingElseIsTrue()
    {
        Assert.IsFalse((bool)ValueOf(Conversion().CBool(Variant(new VBLongValue(0)))).Value);
        Assert.IsTrue((bool)ValueOf(Conversion().CBool(Variant(new VBDoubleValue(0.1)))).Value);
    }

    [TestMethod]
    public void CBool_OfAnErrorIsItsCodeCoerced()
        => Assert.IsTrue((bool)ValueOf(Conversion().CBool(Variant(new VBErrorValue(5)))).Value);

    [TestMethod]
    public void CBool_OfNullIsInvalidUseOfNull()
        => Assert.AreEqual(VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Conversion().CBool(Variant(VBNullValue.Null))));

    #endregion

    #region CDate / CVDate

    [TestMethod]
    public void CDate_ReadsASerialNumber()
        => Assert.AreEqual(1.5, ValueOf(Conversion().CDate(Variant(new VBDoubleValue(1.5)))).SerialValue);

    [TestMethod]
    public void CDate_OfAnErrorIsATypeMismatch()
        // MS-VBAL 6.1.2.3.1.4: unlike every other conversion, an Error has no date.
        => Assert.AreEqual(VBRuntimeErrorId.TypeMismatch, ErrorOf(Conversion().CDate(Variant(new VBErrorValue(5)))));

    [TestMethod]
    public void CVDate_IsCDateInAVariant()
        => Assert.AreEqual(1.5, ((VBDateValue)Inner(Conversion().CVDate(Variant(new VBDoubleValue(1.5))))).SerialValue);

    #endregion

    #region CStr / CVar / CVErr

    [TestMethod]
    public void CStr_FormatsANumberToFifteenDigits()
        => Assert.AreEqual("0.3", ValueOf(Conversion().CStr(Variant(new VBDoubleValue(0.1 + 0.2)))).Value);

    [TestMethod]
    public void CStr_OfABooleanIsItsToken()
        => Assert.AreEqual("True", ValueOf(Conversion().CStr(Variant(VBBooleanValue.True))).Value);

    [TestMethod]
    public void CStr_OfEmptyIsTheZeroLengthString()
        => Assert.AreEqual(string.Empty, ValueOf(Conversion().CStr(Variant(VBEmptyValue.Empty))).Value);

    [TestMethod]
    public void CStr_OfAnErrorIsErrorAndItsCode()
        // MS-VBAL 6.1.2.3.1.12: "Error" followed by a single space and the code as a String.
        => Assert.AreEqual("Error 13", ValueOf(Conversion().CStr(Variant(new VBErrorValue(13)))).Value);

    [TestMethod]
    public void CStr_OfNullIsInvalidUseOfNull()
        => Assert.AreEqual(VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Conversion().CStr(Variant(VBNullValue.Null))));

    [TestMethod]
    public void CVar_ReturnsTheArgument()
    {
        var argument = Variant(new VBLongValue(7));
        Assert.AreSame(argument, ValueOf(Conversion().CVar(argument)));
    }

    [TestMethod]
    public void CVErr_MakesAnErrorOfTheCode()
        => Assert.AreEqual(13, ((VBErrorValue)Inner(Conversion().CVErr(Variant(new VBLongValue(13))))).Value);

    [TestMethod]
    public void CVErr_OfAnErrorReturnsIt()
        => Assert.AreEqual(9, ((VBErrorValue)Inner(Conversion().CVErr(Variant(new VBErrorValue(9))))).Value);

    [TestMethod]
    public void CVErr_RefusesACodeOutsideZeroTo65535()
    {
        Assert.AreEqual(VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Conversion().CVErr(Variant(new VBLongValue(65536)))));
        Assert.AreEqual(VBRuntimeErrorId.InvalidProcedureCallOrArgument, ErrorOf(Conversion().CVErr(Variant(new VBLongValue(-1)))));
    }

    #endregion

    #region Error / Error$

    [TestMethod]
    public void Error_IsTheTextOfAKnownCode()
    {
        var text = VBRuntimeErrorInfo.GetErrorString(VBRuntimeErrorId.Overflow);
        Assert.AreEqual(text, ((VBStringValue)Inner(Conversion().Error(Variant(new VBLongValue(6))))).Value);
        Assert.AreEqual(text, ValueOf(Conversion().ErrorStr(Variant(new VBLongValue(6)))).Value);
    }

    [TestMethod]
    public void Error_OfZeroIsTheZeroLengthString()
        => Assert.AreEqual(string.Empty, ValueOf(Conversion().ErrorStr(Variant(new VBLongValue(0)))).Value);

    [TestMethod]
    public void Error_OfAnUnknownCodeIsTheApplicationDefinedText()
        => Assert.AreEqual(
            "Application-defined or object-defined error.",
            ValueOf(Conversion().ErrorStr(Variant(new VBLongValue(31999)))).Value);

    [TestMethod]
    public void Error_AboveTheLargestCodeIsOverflow()
        => Assert.AreEqual(VBRuntimeErrorId.Overflow, ErrorOf(Conversion().ErrorStr(Variant(new VBLongValue(65536)))));

    [TestMethod]
    public void Error_WithoutAnArgumentIsTheMostRecentlyRaisedError()
    {
        var expected = VBRuntimeErrorInfo.GetErrorString(VBRuntimeErrorId.Overflow);
        Assert.AreEqual(expected, ValueOf(Conversion(lastError: 6).ErrorStr()).Value);
        Assert.AreEqual(string.Empty, ValueOf(Conversion().ErrorStr()).Value);
    }

    #endregion

    #region Fix / Int

    [TestMethod]
    public void Fix_DropsTheFractionAndIntGoesDown()
    {
        Assert.AreEqual(-2d, ((VBDoubleValue)Inner(Conversion().Fix(Variant(new VBDoubleValue(-2.7))))).Value);
        Assert.AreEqual(-3d, ((VBDoubleValue)Inner(Conversion().Int(Variant(new VBDoubleValue(-2.7))))).Value);
        Assert.AreEqual(2d, ((VBDoubleValue)Inner(Conversion().Fix(Variant(new VBDoubleValue(2.7))))).Value);
        Assert.AreEqual(2d, ((VBDoubleValue)Inner(Conversion().Int(Variant(new VBDoubleValue(2.7))))).Value);
    }

    [TestMethod]
    public void Fix_OfNullIsNull()
        => Assert.IsInstanceOfType<VBNullValue>(Inner(Conversion().Fix(Variant(VBNullValue.Null))));

    [TestMethod]
    public void Fix_ReturnsAnIntegerTypeUnchanged()
        => Assert.AreEqual(5, ((VBLongValue)Inner(Conversion().Fix(Variant(new VBLongValue(5))))).Value);

    [TestMethod]
    public void Int_KeepsTheTypeOfTheNumber()
    {
        Assert.AreEqual(2f, ((VBSingleValue)Inner(Conversion().Int(Variant(new VBSingleValue(2.5f))))).Value);
        Assert.AreEqual(-3m, ((VBDecimalValue)Inner(Conversion().Int(Variant(new VBDecimalValue(-2.5m))))).Value);
    }

    [TestMethod]
    public void Int_OfAStringIsADouble()
        => Assert.AreEqual(-3d, ((VBDoubleValue)Inner(Conversion().Int(Text("-2.5")))).Value);

    [TestMethod]
    public void Int_OfADateIsADate()
        => Assert.AreEqual(1d, ((VBDateValue)Inner(Conversion().Int(Variant(new VBDateValue(1.5))))).SerialValue);

    [TestMethod]
    public void Int_OfABooleanIsAnInteger()
        => Assert.AreEqual((short)-1, ((VBIntegerValue)Inner(Conversion().Int(Variant(VBBooleanValue.True)))).Value);

    #endregion

    #region Hex / Oct

    [TestMethod]
    public void Hex_OfAPositiveNumberHasNoLeadingZeros()
    {
        Assert.AreEqual("FF", ((VBStringValue)Inner(Conversion().Hex(Variant(new VBLongValue(255))))).Value);
        Assert.AreEqual("0", ValueOf(Conversion().HexStr(Variant(new VBLongValue(0)))).Value);
    }

    [TestMethod]
    public void Hex_OfANegativeNumberIsItsTwosComplementInTheNarrowestWidth()
    {
        Assert.AreEqual("FFFF", ValueOf(Conversion().HexStr(Variant(new VBIntegerValue(-1)))).Value);
        Assert.AreEqual("FFFF8000", ValueOf(Conversion().HexStr(Variant(new VBLongValue(-32768)))).Value);
    }

    [TestMethod]
    public void Hex_OfALongLongIsNeverNarrowed()
        => Assert.AreEqual("FFFFFFFFFFFFFFFF", ValueOf(Conversion().HexStr(Variant(new VBLongLongValue(-1)))).Value);

    [TestMethod]
    public void Hex_OfEmptyIsZero()
        => Assert.AreEqual("0", ValueOf(Conversion().HexStr(Variant(VBEmptyValue.Empty))).Value);

    [TestMethod]
    public void Hex_OfNullIsNullButHexDollarRaises()
    {
        Assert.IsInstanceOfType<VBNullValue>(Inner(Conversion().Hex(Variant(VBNullValue.Null))));
        Assert.AreEqual(VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Conversion().HexStr(Variant(VBNullValue.Null))));
    }

    [TestMethod]
    public void Oct_FollowsTheSameWidths()
    {
        Assert.AreEqual("10", ValueOf(Conversion().OctStr(Variant(new VBLongValue(8)))).Value);
        Assert.AreEqual("177777", ValueOf(Conversion().OctStr(Variant(new VBIntegerValue(-1)))).Value);
        Assert.AreEqual("37777700000", ValueOf(Conversion().OctStr(Variant(new VBLongValue(-32768)))).Value);
        Assert.AreEqual("1777777777777777777777", ValueOf(Conversion().OctStr(Variant(new VBLongLongValue(-1)))).Value);
    }

    #endregion

    #region Str / Str$

    [TestMethod]
    public void Str_LeavesRoomForTheSign()
    {
        Assert.AreEqual(" 5", ValueOf(Conversion().StrStr(Variant(new VBLongValue(5)))).Value);
        Assert.AreEqual("-5", ValueOf(Conversion().StrStr(Variant(new VBLongValue(-5)))).Value);
        Assert.AreEqual(" 1.5", ((VBStringValue)Inner(Conversion().Str(Variant(new VBDoubleValue(1.5))))).Value);
    }

    [TestMethod]
    public void Str_OfOtherTypesGoesThroughDouble()
    {
        Assert.AreEqual("-1", ValueOf(Conversion().StrStr(Variant(VBBooleanValue.True))).Value);
        Assert.AreEqual(" 12", ValueOf(Conversion().StrStr(Text("12"))).Value);
    }

    [TestMethod]
    public void Str_OfAnErrorIsErrorAndItsCode()
        => Assert.AreEqual("Error 5", ValueOf(Conversion().StrStr(Variant(new VBErrorValue(5)))).Value);

    [TestMethod]
    public void Str_OfNullIsNullButStrDollarRaises()
    {
        Assert.IsInstanceOfType<VBNullValue>(Inner(Conversion().Str(Variant(VBNullValue.Null))));
        Assert.AreEqual(VBRuntimeErrorId.InvalidUseOfNull, ErrorOf(Conversion().StrStr(Variant(VBNullValue.Null))));
    }

    #endregion

    #region Val

    [TestMethod]
    [DataRow("", 0d)]
    [DataRow("12abc", 12d)]
    [DataRow(" 1 2\t3\n", 123d)]
    [DataRow("abc", 0d)]
    [DataRow("-3.5", -3.5)]
    [DataRow(".5", 0.5)]
    [DataRow("1.5e2x", 150d)]
    [DataRow("1d2", 100d)]
    [DataRow("1e", 1d)]
    [DataRow("&HFF", 255d)]
    [DataRow("&o17", 15d)]
    [DataRow("&HFFZ", 255d)]
    [DataRow("$1,000", 0d)]
    public void Val_ReadsTheLeadingNumber(string text, double expected)
        => Assert.AreEqual(expected, ValueOf(Conversion().Val(new VBStringValue(text))).Value);

    [TestMethod]
    public void Val_BeyondDoubleIsOverflow()
        => Assert.AreEqual(VBRuntimeErrorId.Overflow, ErrorOf(Conversion().Val(new VBStringValue("1e999"))));

    #endregion
}
