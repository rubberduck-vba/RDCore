using RDCore.External.Protocol;
using RDCore.Runtime.Execution.External.Automation;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using System.Globalization;

namespace RDCore.Runtime.Execution.External.Native;

/// <summary>
/// The values of the language as a native function takes them, and back: how each intrinsic type is passed (<strong>MS-VBAL §5.2.3.5</strong>, the way MS-VBA
/// passes it).
/// </summary>
internal static class NativeValues
{
    /// <summary>
    /// A number as the native type it is passed as: an <c>Integer</c> as 16 bits, a <c>Long</c> as 32, a <c>LongPtr</c> as a pointer, a <c>Boolean</c> as a
    /// <c>VARIANT_BOOL</c>, a <c>Currency</c> as its scaled 64 bits, a <c>Date</c> as its serial number; <c>Nothing</c> as a null pointer.
    /// </summary>
    public static (NativeSlot Slot, object Value)? Scalar(VBTypedValue typed) => typed switch
    {
        VBByteValue value => (NativeSlot.Byte, value.Value),
        VBIntegerValue value => (NativeSlot.Int16, value.Value),
        VBLongValue value => (NativeSlot.Int32, value.Value),
        VBLongLongValue value => (NativeSlot.Int64, value.Value),
        VBLongPtrValue value => (NativeSlot.Pointer, (long)value.Value),
        VBBooleanValue value => (NativeSlot.Int16, (short)((bool)value.Value ? -1 : 0)),
        VBSingleValue value => (NativeSlot.Single, value.Value),
        VBDoubleValue value => (NativeSlot.Double, value.Value),
        VBDateValue value => (NativeSlot.Double, value.SerialValue),
        VBCurrencyValue value => (NativeSlot.Int64, value.Value.StoredValue),
        VBObjectValue value when value.IsNothing() => (NativeSlot.Pointer, 0L),
        _ => null,
    };

    /// <summary>
    /// The typed value an argument of an <c>As Any</c> parameter is, from what it is stored as, when it names no variable whose declared type says.
    /// </summary>
    public static VBTypedValue? TypedOf(IRuntimeValue argument) => argument switch
    {
        VBRuntimeVariantValue variant => variant.WrappedValue,
        VBRuntimeBooleanValue boolean => new VBBooleanValue((bool)boolean),
        VBRuntimeCurrencyValue currency => new VBCurrencyValue(currency.Value),
        VBRuntimeValue<VBRuntimeCurrencyValue> currency => new VBCurrencyValue(currency.Value.Value),
        VBRuntimeEmptyValue or VBRuntimeNullValue => new VBLongValue(0),
        VBRuntimeValue<VBRuntimeObjectId> identity => new VBObjectValue(identity.StoredValue),
        _ => argument.BoxedValue switch
        {
            VBTypedValue typed => typed,
            byte value => new VBByteValue(value),
            short value => new VBIntegerValue(value),
            int value => new VBLongValue(value),
            long value => new VBLongLongValue(value),
            float value => new VBSingleValue(value),
            double value => new VBDoubleValue(value),
            string value => new VBStringValue(value),
            _ => null,
        },
    };

    /// <summary>
    /// The value of a declared type from what a function returned or left, as the native type it was passed as.
    /// </summary>
    public static VBTypedValue Typed(object? value, VBType declared)
    {
        var invariant = CultureInfo.InvariantCulture;
        return declared switch
        {
            VBByteType => new VBByteValue(Convert.ToByte(value, invariant)),
            VBIntegerType => new VBIntegerValue(Convert.ToInt16(value, invariant)),
            VBBooleanType => new VBBooleanValue(Convert.ToInt16(value, invariant) != 0),
            VBLongType or VBEnumType => new VBLongValue(Convert.ToInt32(value, invariant)),
            VBLongLongType => new VBLongLongValue(Convert.ToInt64(value, invariant)),
            VBLongPtrType_x64 or VBLongPtrType_x86 => new VBLongPtrValue(Convert.ToInt64(value, invariant)),
            VBSingleType => new VBSingleValue(Convert.ToSingle(value, invariant)),
            VBDoubleType => new VBDoubleValue(Convert.ToDouble(value, invariant)),
            VBDateType => new VBDateValue(Convert.ToDouble(value, invariant)),
            VBCurrencyType => new VBCurrencyValue(new VBRuntimeCurrencyValue(Convert.ToInt64(value, invariant)).Value),
            VBStringType => new VBStringValue(Convert.ToString(value, invariant) ?? string.Empty),
            _ => AutomationMarshaller.FromAutomation(value, declared, (_, _) => VBObjectValue.Nothing),
        };
    }
}
