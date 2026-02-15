using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Values;

internal interface INumericValue
{
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
    VBDoubleValue? AsCoercedNumeric(int depth = 0);
}

internal interface IStringCoercion
{
    VBStringValue? AsCoercedString(int depth = 0);
}
