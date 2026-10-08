using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// The conversions every let-coercion and every conversion function is made of, applied to a source they have no conversion for.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.5.1.2 Let-coercion (runtime)")]
public sealed class ValueConversionsTests
{
    public static IEnumerable<object[]> ConversionsOfAnError()
    {
        yield return [nameof(ValueConversions.ToBoolean), ValueConversions.ToBoolean(new VBErrorValue(5))];
        yield return [nameof(ValueConversions.ToDate), ValueConversions.ToDate(new VBErrorValue(5))];
        yield return [nameof(ValueConversions.ToText), ValueConversions.ToText(new VBErrorValue(5))];
        yield return [nameof(ValueConversions.ToNumeric), ValueConversions.ToNumeric(new VBErrorValue(5), VBLongType.TypeInfo)];
    }

    [TestMethod]
    [DynamicData(nameof(ConversionsOfAnError))]
    public void AnError_ConvertsToNothingButAVariantOrAnError(string conversion, ValueConversionResult result)
    {
        // MS-VBAL 5.5.1.2.9: a statement that coerces its operand with a strategy of its own reaches the conversion itself.
        Assert.IsTrue(result.IsApplicable, conversion);
        Assert.AreEqual(VBRuntimeErrorId.TypeMismatch, result.Error, conversion);
    }
}
