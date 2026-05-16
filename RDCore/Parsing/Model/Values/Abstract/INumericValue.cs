using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values.Intrinsic;

namespace RDCore.Parsing.Model.Values.Abstract;

internal interface INumericValue
{
    double NumericValue { get; }
    INumericValue WithValue(double value);

    VBBooleanValue AsBoolean();
    VBByteValue AsByte();
    VBIntegerValue AsInteger();
    VBLongValue AsLong();
    VBLongLongValue AsLongLong();
    VBSingleValue AsSingle();
    VBDoubleValue AsDouble();
    VBCurrencyValue AsCurrency();
    VBDecimalValue AsDecimal();
}

internal interface INumericValue<VBTValue> : INumericValue
    where VBTValue : VBTypedValue
{
    VBType TypeInfo { get; }

    VBTValue MinValue { get; }
    VBTValue MaxValue { get; }
    VBTValue Zero { get; }
}

internal interface INumericCoercion
{
    VBDoubleValue? AsCoercedDouble(ref int depth);
}

internal interface IStringCoercion
{
    VBStringValue? AsCoercedString(ref int depth);
    VBFixedStringValue? AsCoercedFixedLengthString(int length, ref int depth);
}

internal interface IBooleanCoercion
{
    VBBooleanValue AsCoercedBoolean(ref int depth);
}

internal interface IDateCoercion
{
    VBDateValue AsCoercedDate(ref int depth);
}
